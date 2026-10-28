using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

/// <summary>
/// Assembles multiple scene documents into a single SVG sprite sheet: each document is
/// exported as a symbol fragment (id = document name) and the fragments are wrapped in
/// one svg/defs root, ready for <c>use href="#id"</c> references.
/// </summary>
public sealed class SpriteCommand : AsyncCommand<SpriteCommand.Settings>
{
    public const int MaxDocuments = 256;

    private readonly IDocumentEngine _engine;

    public SpriteCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILES>")]
        [Description("Paths to the .agd.json document files to combine, in sheet order")]
        public required string[] Files { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output sprite file path")]
        public required string Output { get; init; }

        [CommandOption("--strict")]
        [Description("Validate each document before export and reject errors or warnings")]
        public bool Strict { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Validate each document against an additional profile before export: web-icon")]
        public string? Profile { get; init; }

        [CommandOption("--current-color <COLOR>")]
        [Description("Write currentColor for node fills and strokes that resolve to this color")]
        public string? CurrentColor { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }
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

        if (!ValidationProfiles.TryParse(settings.Profile, out var profile))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown validation profile. Supported: {ValidationProfiles.WebIcon}", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.CurrentColor is not null && !DocumentLimits.IsValidColor(settings.CurrentColor))
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty,
                "--current-color must be a hex, rgb(), or rgba() color", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.Files.Length == 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "At least one document file is required", format);
            return ExitCodes.InvalidArguments;
        }
        if (settings.Files.Length > MaxDocuments)
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"A sprite can combine at most {MaxDocuments} documents", format);
            return ExitCodes.InvalidArguments;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var fragments = new List<byte[]>(settings.Files.Length);
        var symbolIds = new List<string>(settings.Files.Length);

        try
        {
            foreach (var input in settings.Files)
            {
                if (!SurfaceIO.TryResolvePath(input, out var file) || !File.Exists(file))
                {
                    AgentOutput.WriteError("FILE_NOT_FOUND", $"File not found: {input}", format);
                    return ExitCodes.GeneralError;
                }
                if (!SurfaceIO.TryCheckFileSize(file, SurfaceIO.MaxInputFileBytes, out var sizeError))
                {
                    AgentOutput.WriteError("INPUT_TOO_LARGE", sizeError!, format);
                    return ExitCodes.InvalidArguments;
                }

                var loadResult = _engine.TryLoadDocumentFromFile(file, ct);
                if (!loadResult.Success || loadResult.Document is null)
                {
                    var loadError = loadResult.Error;
                    AgentOutput.WriteError("PARSE_ERROR",
                        $"{file}: {loadError?.Message ?? "Failed to parse document"}", format, loadError?.Path);
                    return ExitCodes.GeneralError;
                }

                var document = loadResult.Document;

                if (!SvgExportAdapter.IsValidSymbolId(document.Name))
                {
                    AgentOutput.WriteError(EngineErrors.InvalidProperty,
                        $"{file}: document name \"{document.Name}\" is not a valid symbol id (start with a letter; letters, digits, '-' or '_'; at most {SvgExportAdapter.MaxSymbolIdCharacters} characters)",
                        format);
                    return ExitCodes.InvalidArguments;
                }
                if (!seenIds.Add(document.Name))
                {
                    AgentOutput.WriteError(EngineErrors.InvalidArguments,
                        $"{file}: duplicate symbol id \"{document.Name}\"; document names must be unique within a sprite", format);
                    return ExitCodes.InvalidArguments;
                }

                if (settings.Strict || profile != ValidationProfile.Default)
                {
                    var validation = _engine.Validate(document, new ValidationOptions { Profile = profile });
                    ValidationError? error = validation.Errors.FirstOrDefault();
                    if (error is not null)
                    {
                        AgentOutput.WriteError(error.Code, $"{file}: {error.Message}", format, error.Path);
                        return ExitCodes.ValidationFailed;
                    }

                    var warning = settings.Strict ? validation.Warnings.FirstOrDefault() : null;
                    if (warning is not null)
                    {
                        AgentOutput.WriteError(warning.Code, $"{file}: {warning.Message}", format, warning.Path);
                        return ExitCodes.ValidationWarnings;
                    }
                }

                var exportResult = await _engine.ExportAsync(document, new ExportOptions
                {
                    Format = "svg",
                    SymbolId = document.Name,
                    CurrentColor = settings.CurrentColor
                }, ct);

                fragments.Add(exportResult.Data);
                symbolIds.Add(document.Name);
            }

            var data = SvgSpriteComposer.Compose(fragments, ct);
            if (data.LongLength > SurfaceIO.MaxOutputBytes)
            {
                AgentOutput.WriteError("OUTPUT_TOO_LARGE",
                    $"Output exceeds the {SurfaceIO.MaxOutputBytes} byte limit", format);
                return ExitCodes.GeneralError;
            }

            var outputPath = PlatformPaths.FullPath(settings.Output);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await File.WriteAllBytesAsync(outputPath, data, ct);

            var spriteResult = new
            {
                path = SurfaceIO.DisplayPath(outputPath),
                symbols = symbolIds,
                size = data.Length,
                mimeType = "image/svg+xml"
            };

            AgentOutput.WriteSuccess($"Sprite with {symbolIds.Count} symbol(s) written to: {settings.Output}", format, spriteResult);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ex.Code == EngineErrors.InvalidArguments ? ExitCodes.InvalidArguments : ExitCodes.GeneralError;
        }
    }
}
