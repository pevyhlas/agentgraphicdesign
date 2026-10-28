using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Import;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class ImportCommand : AsyncCommand<ImportCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public ImportCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("Path to the SVG file to import")]
        public required string File { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output .agd.json file path (default: derived from input)")]
        public string? Output { get; init; }

        [CommandOption("-n|--name <NAME>")]
        [Description("Document name (default: derived from input filename)")]
        public string? Name { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--force")]
        [Description("Overwrite the output file if it already exists")]
        public bool Force { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parseResult = OutputFormatExtensions.Parse(settings.Format);
        if (!parseResult.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parseResult.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        var format = parseResult.Format;

        if (!SurfaceIO.TryResolvePath(settings.File, out var inputPath) || !File.Exists(inputPath))
        {
            AgentOutput.WriteError("FILE_NOT_FOUND", $"File not found: {settings.File}", format);
            return ExitCodes.GeneralError;
        }

        if (!SurfaceIO.TryCheckFileSize(inputPath, SurfaceIO.MaxInputFileBytes, out var sizeError))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", sizeError!, format);
            return ExitCodes.InvalidArguments;
        }

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (extension != ".svg")
        {
            AgentOutput.WriteError("UNSUPPORTED_FORMAT", $"Only SVG import is supported; got: {extension}", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.Name is { Length: > SurfaceIO.MaxNameLength })
        {
            AgentOutput.WriteError("INVALID_NAME", $"Name cannot exceed {SurfaceIO.MaxNameLength} characters", format);
            return ExitCodes.InvalidArguments;
        }

        var options = new SvgImportOptions { DocumentName = settings.Name };
        var svg = await File.ReadAllTextAsync(inputPath, ct);
        var result = _engine.ImportSvg(svg, options, ct);

        if (!result.Success || result.Document is null)
        {
            AgentOutput.WriteError(result.ErrorCode ?? "IMPORT_FAILED", result.ErrorMessage ?? "Import failed", format);
            return ExitCodes.GeneralError;
        }

        var document = result.Document;
        var outputPath = settings.Output ?? DeriveOutputPath(inputPath);

        try
        {
            await SurfaceIO.CreateDocumentFileAsync(_engine, document, outputPath, settings.Force, ct);

            var importResult = new
            {
                locator = SurfaceIO.DisplayPath(outputPath),
                documentId = document.Id,
                documentName = document.Name,
                canvas = new
                {
                    width = document.Canvas.Width,
                    height = document.Canvas.Height
                },
                nodeCount = _engine.CountNodes(document),
                warnings = result.Warnings.Select(w => new
                {
                    code = w.Code.ToString(),
                    message = w.Message,
                    context = w.Context
                }).ToArray()
            };

            if (result.Warnings.Count > 0)
            {
                AgentOutput.WriteSuccess($"Imported to: {outputPath} ({result.Warnings.Count} warning(s))", format, importResult);
            }
            else
            {
                AgentOutput.WriteSuccess($"Imported to: {outputPath}", format, importResult);
            }

            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static string DeriveOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{name}.agd.json");
    }
}
