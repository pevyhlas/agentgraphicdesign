using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

// The native pixel CLI surface (P6). Every subcommand shares PixelDirectorySettings
// (a <DIRECTORY> argument and -f|--format) and the PixelCommandBase error/exit mapping.
// Text output identifies the captured source token and the operation outcome without
// claiming continued source freshness; JSON output carries the full native result in the
// shared envelope.

public sealed class PixelCreateCommand(NativePixelService service)
    : PixelCommandBase<PixelCreateCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--name <NAME>")]
        [Description("Name for the new pixel asset")]
        public required string Name { get; init; }

        [CommandOption("--width <INT>")]
        [Description("Canvas width in pixels")]
        public required int Width { get; init; }

        [CommandOption("--height <INT>")]
        [Description("Canvas height in pixels")]
        public required int Height { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var result = await Service.CreateAsync(
            new CreatePixelAssetRequest(ResolvePath(settings.Directory), settings.Name, settings.Width, settings.Height), ct);

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(result, format);
        }
        else
        {
            var state = result.State;
            AnsiConsole.MarkupLine($"[green]✓[/] Created pixel asset: {Markup.Escape(state.Name)} ({state.Width}×{state.Height})");
            WriteToken(state.Snapshot);
            AnsiConsole.MarkupLine($"  initial layer: {result.InitialLayerId}");
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelInspectCommand(NativePixelService service)
    : PixelCommandBase<PixelInspectCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings;

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var info = await Service.InspectAsync(new InspectPixelAssetRequest(ResolvePath(settings.Directory)), ct);

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(info, format);
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"Pixel asset: {Markup.Escape(info.Name)} ({info.Width}×{info.Height}), " +
                $"{info.Layers.Length} layer(s), {info.LogicalSourceBytes} source byte(s)");
            WriteToken(info.Snapshot);
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelRegionCommand(NativePixelService service)
    : PixelCommandBase<PixelRegionCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--layer <GUID>")]
        [Description("Layer id to sample")]
        public required string Layer { get; init; }

        [CommandOption("--target <TARGET>")]
        [Description("Sample target: pixels or mask")]
        public required string Target { get; init; }

        [CommandOption("--rect <X,Y,W,H>")]
        [Description("Region rectangle: four comma-separated integers x,y,width,height")]
        public required string Rect { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        if (!Guid.TryParse(settings.Layer, out var layerId))
            throw new NativePixelException(EngineErrors.InvalidArguments, "The layer id must be a GUID.");
        var target = settings.Target switch
        {
            "pixels" => PixelTarget.Pixels,
            "mask" => PixelTarget.Mask,
            _ => throw new NativePixelException(EngineErrors.InvalidArguments, "The target must be pixels or mask.")
        };
        var rect = ParseRect(settings.Rect);

        var result = await Service.InspectRegionAsync(
            new InspectPixelRegionRequest(ResolvePath(settings.Directory), layerId, target, rect), ct);

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(result, format);
        }
        else
        {
            var (kind, count) = result switch
            {
                RgbaRegionResult rgba => ("pixels", rgba.Samples.Length),
                MaskRegionResult mask => ("mask", mask.Samples.Length),
                _ => ("region", 0)
            };
            AnsiConsole.MarkupLine(
                $"Region ({kind}) layer {result.LayerId} rect {result.Rect.X},{result.Rect.Y},{result.Rect.Width},{result.Rect.Height} — {count} sample(s)");
            WriteToken(result.Snapshot);
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelEditCommand(NativePixelService service)
    : PixelCommandBase<PixelEditCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--request <JSON_FILE>")]
        [Description("Path to the edit-request JSON file (expectedSnapshot + operations)")]
        public required string Request { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var directory = ResolvePath(settings.Directory);
        var utf8 = ReadInputFile(settings.Request, "The request input");
        // Relative replacement inputPaths resolve against the invocation directory; the shared
        // wire parser produces the immutable typed request and rejects malformed members.
        var request = NativePixelWire.ParseEdit(utf8, directory, Environment.CurrentDirectory);

        var result = await Service.EditAsync(request, ct);

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(result, format);
        }
        else
        {
            AnsiConsole.MarkupLine(result.Changed
                ? "[green]✓[/] Edit applied"
                : "[green]✓[/] Edit accepted (no changes)");
            AnsiConsole.MarkupLine($"  before token: {result.BeforeSnapshot.AssetId} rev {result.BeforeSnapshot.Revn} (sourceDigest {ShortHash(result.BeforeSnapshot.SourceDigest)})");
            WriteToken(result.State.Snapshot);
            AnsiConsole.MarkupLine($"  created layers: {result.CreatedLayers.Length}, affected: {result.AffectedLayerIds.Length}");
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelValidateCommand(NativePixelService service)
    : PixelCommandBase<PixelValidateCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings;

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var result = await Service.ValidateAsync(new ValidatePixelAssetRequest(ResolvePath(settings.Directory)), ct);

        // A completed invalid report is not an execution error: ok:true, data.valid:false, exit 2.
        if (result is InvalidPixelAssetResult invalid)
        {
            if (format == OutputFormat.Json)
            {
                AgentOutput.WriteData(new
                {
                    valid = false,
                    errors = invalid.Errors.Select(e => new { e.Code, e.Message, e.Path })
                }, format);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Errors:[/]");
                foreach (var diagnostic in invalid.Errors)
                {
                    AnsiConsole.MarkupLine($"  [red]✗[/] [[[grey]{diagnostic.Code}[/]]] {Markup.Escape(diagnostic.Message)}");
                    if (diagnostic.Path is not null)
                        AnsiConsole.MarkupLine($"    at: {Markup.Escape(diagnostic.Path)}");
                }
                AnsiConsole.MarkupLine($"[red]✗[/] Invalid: {invalid.Errors.Length} error(s)");
            }

            return ExitCodes.ValidationFailed;
        }

        var valid = (ValidPixelAssetResult)result;
        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(new { valid = true, state = valid.State }, format);
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Pixel asset is valid: {Markup.Escape(valid.State.Name)} ({valid.State.Width}×{valid.State.Height})");
            WriteToken(valid.State.Snapshot);
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelPreviewCommand(NativePixelService service)
    : PixelCommandBase<PixelPreviewCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--output <PNG>")]
        [Description("Output PNG path (preview is an explicit file write)")]
        public required string Output { get; init; }

        [CommandOption("--rect <X,Y,W,H>")]
        [Description("Optional region rectangle: four comma-separated integers x,y,width,height")]
        public string? Rect { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite an existing output file")]
        public bool Overwrite { get; init; }

        [CommandOption("--expected-read-snapshot <JSON_FILE>")]
        [Description("Optional snapshot precondition file (closed Snapshot JSON, at most 1 KiB)")]
        public string? ExpectedReadSnapshot { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var rect = settings.Rect is null ? (PixelRect?)null : ParseRect(settings.Rect);
        var snapshot = ReadSnapshot(settings.ExpectedReadSnapshot);
        // The CLI never emits inline terminal binary/base64: preview is always a confined file write.
        var delivery = new PixelPreviewDelivery.File(ResolvePath(settings.Output), settings.Overwrite);

        var result = await Service.PreviewAsync(
            new PreviewPixelAssetRequest(ResolvePath(settings.Directory), delivery, rect, snapshot), ct);

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(result, format);
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Preview written: {Markup.Escape(settings.Output)}");
            WriteToken(result.SourceToken);
            AnsiConsole.MarkupLine(
                $"  rect: {result.Rect.X},{result.Rect.Y},{result.Rect.Width},{result.Rect.Height}  " +
                $"rgbaSha256: {ShortHash(result.RgbaSha256)}  encodedBytes: {result.EncodedBytes}  fileSha256: {ShortHash(result.FileSha256)}");
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelExportCommand(NativePixelService service)
    : PixelCommandBase<PixelExportCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--output <PNG>")]
        [Description("Output PNG path")]
        public required string Output { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite an existing output file")]
        public bool Overwrite { get; init; }

        [CommandOption("--expected-read-snapshot <JSON_FILE>")]
        [Description("Optional snapshot precondition file (closed Snapshot JSON, at most 1 KiB)")]
        public string? ExpectedReadSnapshot { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var snapshot = ReadSnapshot(settings.ExpectedReadSnapshot);

        var result = await Service.ExportAsync(
            new ExportPixelAssetRequest(ResolvePath(settings.Directory), ResolvePath(settings.Output), settings.Overwrite, snapshot), ct);

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(result, format);
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Exported: {Markup.Escape(settings.Output)}");
            WriteToken(result.SourceToken);
            AnsiConsole.MarkupLine(
                $"  rect: {result.Rect.X},{result.Rect.Y},{result.Rect.Width},{result.Rect.Height}  " +
                $"rgbaSha256: {ShortHash(result.RgbaSha256)}  encodedBytes: {result.EncodedBytes}  fileSha256: {ShortHash(result.FileSha256)}");
        }

        return ExitCodes.Success;
    }
}

public sealed class PixelCheckCommand(NativePixelService service)
    : PixelCommandBase<PixelCheckCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--output <PNG>")]
        [Description("Existing output PNG to compare against a fresh evaluation")]
        public required string Output { get; init; }

        [CommandOption("--expected-read-snapshot <JSON_FILE>")]
        [Description("Optional snapshot precondition file (closed Snapshot JSON, at most 1 KiB)")]
        public string? ExpectedReadSnapshot { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var snapshot = ReadSnapshot(settings.ExpectedReadSnapshot);

        var result = await Service.CheckExportAsync(
            new CheckPixelExportRequest(ResolvePath(settings.Directory), ResolvePath(settings.Output), snapshot), ct);

        // A completed mismatch is not an execution error: ok:false with the comparison in data,
        // error.code PIXEL_OUTPUT_DRIFT, exit 6. A matching check is plain success.
        if (!result.Matches)
        {
            if (format == OutputFormat.Json)
            {
                AgentOutput.WriteJson(new OutputEnvelope
                {
                    Ok = false,
                    Data = result,
                    Error = new OutputError
                    {
                        Code = NativePixelErrors.PixelOutputDrift,
                        Message = "The output PNG differs from a fresh evaluation."
                    }
                });
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] [[{NativePixelErrors.PixelOutputDrift}]] Output drift: {string.Join(", ", result.Differences)}");
                WriteToken(result.SourceToken);
                AnsiConsole.MarkupLine(
                    $"  reference: {result.Rect.Width}×{result.Rect.Height} rgbaSha256 {ShortHash(result.RgbaSha256)}  " +
                    $"observed: {result.Observed.Width}×{result.Observed.Height} rgbaSha256 {ShortHash(result.Observed.RgbaSha256)}");
            }

            return 6;
        }

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(result, format);
        }
        else
        {
            AnsiConsole.MarkupLine("[green]✓[/] Output matches a fresh evaluation");
            WriteToken(result.SourceToken);
            AnsiConsole.MarkupLine($"  {result.Rect.Width}×{result.Rect.Height} rgbaSha256 {ShortHash(result.RgbaSha256)}");
        }

        return ExitCodes.Success;
    }
}
