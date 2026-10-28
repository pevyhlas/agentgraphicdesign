using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

/// <summary>
/// Renders several documents into one PNG grid for a quick visual review: each document is
/// aspect-fitted into a square cell, optionally with its name underneath.
/// </summary>
public sealed class ContactSheetCommand : AsyncCommand<ContactSheetCommand.Settings>
{
    public const int MaxDocuments = ContactSheetLayout.MaxEntries;

    private readonly IDocumentEngine _engine;

    public ContactSheetCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILES>")]
        [Description("Paths to the .agd.json document files to place on the sheet, in grid order")]
        public required string[] Files { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output PNG file path")]
        public required string Output { get; init; }

        [CommandOption("--columns <N>")]
        [Description("Grid columns (default: ceil(sqrt(count)))")]
        public int? Columns { get; init; }

        [CommandOption("--cell <PIXELS>")]
        [Description("Square cell size in pixels (default: 128)")]
        public int? Cell { get; init; }

        [CommandOption("--gap <PIXELS>")]
        [Description("Gap between cells and around the edge (default: 8)")]
        public int? Gap { get; init; }

        [CommandOption("--background <COLOR>")]
        [Description("Sheet background color (default: #f4f4f5)")]
        public string? Background { get; init; }

        [CommandOption("--labels")]
        [Description("Print each document's name under its cell")]
        public bool Labels { get; init; }

        [CommandOption("--font-dir <DIR>")]
        [Description("Directory of .ttf/.otf files searched before the bundled Inter family and system fonts for every tile and label (repeatable)")]
        public string[]? FontDirs { get; init; }

        [CommandOption("--no-system-fonts")]
        [Description("Never use host-installed fonts; unresolved families substitute bundled Inter")]
        public bool NoSystemFonts { get; init; }

        [CommandOption("--strict-fonts")]
        [Description("Fail a tile with FONT_NOT_FOUND when a font family cannot be resolved instead of substituting bundled Inter")]
        public bool StrictFonts { get; init; }

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

        if (settings.Files.Length == 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "At least one document file is required", format);
            return ExitCodes.InvalidArguments;
        }
        if (settings.Files.Length > MaxDocuments)
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"A contact sheet can hold at most {MaxDocuments} documents", format);
            return ExitCodes.InvalidArguments;
        }
        if (!string.Equals(Path.GetExtension(settings.Output), ".png", StringComparison.OrdinalIgnoreCase))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "Contact sheets are PNG only; the output path must end with .png", format);
            return ExitCodes.InvalidArguments;
        }

        var fontResult = FontPolicyOptions.Resolve(settings.FontDirs, settings.NoSystemFonts, settings.StrictFonts);
        if (!fontResult.Success)
        {
            AgentOutput.WriteError(fontResult.ErrorCode!, fontResult.ErrorMessage!, format);
            return ExitCodes.InvalidArguments;
        }
        var fontPolicy = fontResult.Policy ?? FontPolicy.Default;

        var options = new ContactSheetOptions
        {
            Columns = settings.Columns,
            CellSize = settings.Cell ?? ContactSheetOptions.DefaultCellSize,
            Gap = settings.Gap ?? ContactSheetOptions.DefaultGap,
            Background = settings.Background,
            Labels = settings.Labels,
            FontDirectories = fontPolicy.FontDirectories,
            SystemFonts = fontPolicy.SystemFonts,
            StrictFonts = fontPolicy.StrictFonts
        };

        try
        {
            // Fail on grid limits before reading any file.
            ContactSheetLayout.Compute(settings.Files.Length, options);

            var entries = new List<ContactSheetEntry>(settings.Files.Length);
            foreach (var input in settings.Files)
            {
                ct.ThrowIfCancellationRequested();
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

                entries.Add(new ContactSheetEntry(loadResult.Document, loadResult.Document.Name));
            }

            var result = _engine.RenderContactSheet(entries, options, ct);
            if (result.Data.LongLength > SurfaceIO.MaxOutputBytes)
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

            await File.WriteAllBytesAsync(outputPath, result.Data, ct);

            var sheet = new
            {
                path = SurfaceIO.DisplayPath(outputPath),
                width = result.Width,
                height = result.Height,
                columns = result.Layout.Columns,
                rows = result.Layout.Rows,
                cellSize = result.Layout.CellSize,
                gap = result.Layout.Gap,
                labels = options.Labels,
                size = result.Data.Length,
                mimeType = result.MimeType,
                cells = result.Cells.Select((cell, index) => new
                {
                    index = cell.Index,
                    label = cell.Label,
                    source = SurfaceIO.DisplayPath(settings.Files[index]),
                    column = cell.Column,
                    row = cell.Row,
                    cell = new { x = cell.Cell.X, y = cell.Cell.Y, width = cell.Cell.Width, height = cell.Cell.Height },
                    image = new { x = cell.Image.X, y = cell.Image.Y, width = cell.Image.Width, height = cell.Image.Height }
                })
            };

            AgentOutput.WriteSuccess(
                $"Contact sheet with {entries.Count} document(s) ({result.Width}x{result.Height}) written to: {settings.Output}",
                format, sheet);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ex.Code is EngineErrors.InvalidArguments or EngineErrors.InvalidProperty or EngineErrors.InvalidDimensions
                ? ExitCodes.InvalidArguments
                : ExitCodes.GeneralError;
        }
    }
}
