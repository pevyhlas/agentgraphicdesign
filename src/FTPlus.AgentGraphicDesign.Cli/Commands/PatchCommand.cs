using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Patching;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class PatchCommand : Command<PatchCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public PatchCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandOption("--file <PATH>")]
        [Description("Read the JSON operations array from a file")]
        public string? File { get; init; }

        [CommandOption("--ops <JSON>")]
        [Description("Inline JSON operations array")]
        public string? Ops { get; init; }

        [CommandOption("--stdin")]
        [Description("Read the JSON operations array from standard input")]
        public bool Stdin { get; init; }

        [CommandOption("--expected-revn <REVN>")]
        [Description("Expected document revision (optimistic concurrency)")]
        public long? ExpectedRevn { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parseResult = OutputFormatExtensions.Parse(settings.Format);
        if (!parseResult.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parseResult.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        var format = parseResult.Format;

        if (settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Expected revision cannot be negative", format);
            return ExitCodes.InvalidArguments;
        }

        var sources = (settings.File is null ? 0 : 1) + (settings.Ops is null ? 0 : 1) + (settings.Stdin ? 1 : 0);
        if (sources != 1)
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "Specify exactly one of --file, --ops or --stdin", format);
            return ExitCodes.InvalidArguments;
        }

        string json;
        try
        {
            json = ReadOperations(settings, ct);
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.InvalidArguments;
        }

        var parsed = PatchOperationParser.Parse(json);
        if (!parsed.Success)
        {
            WritePatchError(parsed.Error!, format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        try
        {
            var result = _engine.ApplyPatch(handle, parsed.Operations!, settings.ExpectedRevn, ct);
            if (!result.Success)
            {
                WritePatchError(result.Error!, format);
                return ExitCodes.GeneralError;
            }

            var data = new
            {
                applied = result.Applied,
                revn = result.Revn,
                createdIds = result.CreatedIds.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
                warnings = result.Warnings.Select(w => new { w.Code, w.Message, w.Path }).ToArray()
            };

            if (format == OutputFormat.Json)
            {
                AgentOutput.WriteData(data, format);
                return ExitCodes.Success;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Applied {result.Applied} operation(s); revision {result.Revn}");
            foreach (var (index, id) in result.CreatedIds.OrderBy(pair => pair.Key))
            {
                AnsiConsole.MarkupLine($"  created by operation {index}: {id}");
            }

            foreach (var warning in result.Warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]![/] [[[grey]{warning.Code}[/]]] {Markup.Escape(warning.Message)}");
                if (warning.Path is not null)
                {
                    AnsiConsole.MarkupLine($"    at: {Markup.Escape(warning.Path)}");
                }
            }

            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static string ReadOperations(Settings settings, CancellationToken ct)
    {
        if (settings.Ops is not null)
        {
            return settings.Ops;
        }

        if (settings.Stdin)
        {
            return Console.In.ReadToEnd();
        }

        var path = settings.File!;
        if (!SurfaceIO.TryResolvePath(path, out var canonicalPath) || !System.IO.File.Exists(canonicalPath))
        {
            throw new DocumentEngineException("FILE_NOT_FOUND", $"Operations file not found: {path}");
        }

        if (!SurfaceIO.TryCheckFileSize(canonicalPath, PatchLimits.MaxJsonBytes, out var sizeError))
        {
            throw new DocumentEngineException("INPUT_TOO_LARGE", sizeError!);
        }

        ct.ThrowIfCancellationRequested();
        return System.IO.File.ReadAllText(canonicalPath);
    }

    private static void WritePatchError(PatchError error, OutputFormat format) =>
        AgentOutput.WriteError(error.Code, error.Message, format, error.Path, error.OperationIndex);
}
