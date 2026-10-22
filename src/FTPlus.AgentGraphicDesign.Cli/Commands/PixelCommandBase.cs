using System.ComponentModel;
using System.Globalization;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

/// <summary>
/// Shared settings for every <c>agd pixels</c> subcommand: the target asset directory
/// and the common <c>-f|--format</c> option. Relative paths resolve against the
/// invocation working directory before Core's absolute-path rules apply.
/// </summary>
public abstract class PixelDirectorySettings : CommandSettings
{
    [CommandArgument(0, "<DIRECTORY>")]
    [Description("Pixel asset directory")]
    public required string Directory { get; init; }

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: text, json (default: text)")]
    public string? Format { get; init; }
}

/// <summary>
/// Base for the native pixel CLI commands. Parses the shared output format, invokes the
/// requested operation and maps the reviewed <see cref="NativePixelException"/> taxonomy onto
/// the contract's CLI exit codes. Cancellation stays an <see cref="OperationCanceledException"/>
/// and is handled by the host's exit-code-130 convention, not caught here. Completed
/// non-success outcomes (an invalid validation report, a check mismatch) are not exceptions;
/// their subcommands render them directly.
/// </summary>
public abstract class PixelCommandBase<TSettings>(NativePixelService service) : AsyncCommand<TSettings>
    where TSettings : PixelDirectorySettings
{
    protected NativePixelService Service { get; } = service;

    protected abstract Task<int> RunAsync(TSettings settings, OutputFormat format, CancellationToken ct);

    protected sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken ct)
    {
        var parsed = OutputFormatExtensions.Parse(settings.Format);
        if (!parsed.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parsed.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        try
        {
            return await RunAsync(settings, parsed.Format, ct);
        }
        catch (NativePixelException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, parsed.Format, ex.Path, ex.OperationIndex);
            return PixelExit.Map(ex.Code);
        }
    }

    /// <summary>Resolves a CLI path to an absolute local path against the invocation directory.</summary>
    protected static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new NativePixelException(EngineErrors.InvalidArguments, "A local path must be provided.");
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new NativePixelException(EngineErrors.InvalidArguments, "Invalid local filesystem path.");
        }
    }

    /// <summary>
    /// Parses <c>--rect</c>: exactly four comma-separated base-10 integers in x,y,width,height
    /// order, with no fractional, exponent, sign-plus, whitespace or thousands syntax. Bounds
    /// checking is Core's responsibility.
    /// </summary>
    protected static PixelRect ParseRect(string? raw)
    {
        const string message = "The rectangle must be exactly four comma-separated integers: x,y,width,height.";
        if (string.IsNullOrEmpty(raw))
            throw new NativePixelException(EngineErrors.InvalidArguments, message);
        var parts = raw.Split(',');
        if (parts.Length != 4)
            throw new NativePixelException(EngineErrors.InvalidArguments, message);
        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            var token = parts[i];
            if (!IsCanonicalInteger(token) ||
                !int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[i]))
                throw new NativePixelException(EngineErrors.InvalidArguments, message);
        }
        return new PixelRect(values[0], values[1], values[2], values[3]);
    }

    private static bool IsCanonicalInteger(string token)
    {
        var digits = token.StartsWith('-') ? token.AsSpan(1) : token.AsSpan();
        return digits.Length > 0 && !digits.ContainsAnyExcept("0123456789");
    }

    /// <summary>
    /// Reads a closed <c>Snapshot</c> precondition file (UTF-8, no BOM, at most 1 KiB) into a typed
    /// snapshot via the shared wire parser, or returns null when no path is supplied. The 1 KiB
    /// bound is enforced by the parser.
    /// </summary>
    protected static PixelSnapshot? ReadSnapshot(string? path)
        => path is null ? null : NativePixelWire.ParseSnapshot(ReadInputFile(path, "The snapshot input"));

    /// <summary>
    /// Reads a request/snapshot input file, bounding the read at the 1 MiB JSON cap. Absent or
    /// unreadable inputs surface <c>PIXEL_INPUT_UNAVAILABLE</c>; the shared parser enforces the
    /// exact per-input byte bound on the returned bytes.
    /// </summary>
    protected static byte[] ReadInputFile(string path, string subject)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            throw Unavailable(subject);
        }

        if (!info.Exists)
            throw Unavailable(subject);
        if (info.Length > NativePixelFormat.MaxJsonBytes)
            throw new NativePixelException("RESOURCE_LIMIT", "The input file exceeds its byte limit.");

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw Unavailable(subject);
        }
    }

    private static NativePixelException Unavailable(string subject)
        => new(NativePixelErrors.PixelInputUnavailable, $"{subject} is absent or unreadable.");

    protected static void WriteToken(PixelSnapshot snapshot)
        => AnsiConsole.MarkupLine(
            $"  captured source token: {snapshot.AssetId} rev {snapshot.Revn} (sourceDigest {ShortHash(snapshot.SourceDigest)})");

    protected static string ShortHash(string hash) => hash.Length <= 12 ? hash : hash[..12] + "…";
}

/// <summary>
/// Maps native execution-error codes onto the contract's CLI exit codes. Completed non-success
/// outcomes (invalid validation report → 2, check mismatch → 6) are handled by their subcommands,
/// not here; cancellation (130) arrives as <see cref="OperationCanceledException"/>.
/// </summary>
internal static class PixelExit
{
    internal static int Map(string code) => code switch
    {
        "INVALID_ARGUMENTS" or "INVALID_FORMAT" or "UNSUPPORTED_VERSION" or "PIXEL_PROFILE_UNSUPPORTED"
            => ExitCodes.InvalidArguments, // 4
        "PIXEL_OUTPUT_DRIFT" => 6,         // native single-output drift: distinct code, shares exit 6
        "CANCELLED" => 130,                // defensive: cancellation normally propagates as OperationCanceledException
        _ => ExitCodes.GeneralError,       // 1: every other native execution failure
    };
}
