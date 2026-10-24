using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Validation;
using FTPlus.AgentGraphicDesign.Skia;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public ExportCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("Path to the .agd.json document file")]
        public required string File { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output file path (default: derived from input)")]
        public string? Output { get; init; }

        [CommandOption("-t|--type <TYPE>")]
        [Description("Export format: svg, png, jpeg, webp (default: svg)")]
        [DefaultValue("svg")]
        public string Type { get; init; } = "svg";

        [CommandOption("-s|--scale <SCALE>")]
        [Description("Uniform geometry and viewport scale (default: 1.0); use 0.25 for a 128-unit canvas at 32px")]
        [DefaultValue(1.0)]
        public double Scale { get; init; } = 1.0;

        [CommandOption("--width <WIDTH>")]
        [Description("Logical viewport width before scale; crops/pads, does not fit geometry (standalone SVG or raster)")]
        public int? Width { get; init; }

        [CommandOption("--height <HEIGHT>")]
        [Description("Logical viewport height before scale; crops/pads, does not fit geometry (standalone SVG or raster)")]
        public int? Height { get; init; }

        [CommandOption("--background <COLOR>")]
        [Description("Override background color")]
        public string? Background { get; init; }

        [CommandOption("--symbol <ID>")]
        [Description("Emit a reusable SVG symbol fragment with this id for sprite assembly (svg only; excludes scale/width/height)")]
        public string? Symbol { get; init; }

        [CommandOption("--node <ID>")]
        [Description("Export only the subtree rooted at this node id, cropped to its own bounds and drawn in isolation (excludes symbol/width/height)")]
        public string? Node { get; init; }

        [CommandOption("--current-color <COLOR>")]
        [Description("Write currentColor for node fills and strokes that resolve to this color (svg only)")]
        public string? CurrentColor { get; init; }

        [CommandOption("--strict")]
        [Description("Validate before export and reject errors or warnings")]
        public bool Strict { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Validate against an additional profile before export: web-icon")]
        public string? Profile { get; init; }

        [CommandOption("--font-dir <DIR>")]
        [Description("Directory of .ttf/.otf files searched before the bundled Inter family and system fonts (repeatable; raster only)")]
        public string[]? FontDirs { get; init; }

        [CommandOption("--no-system-fonts")]
        [Description("Never use host-installed fonts; unresolved families substitute bundled Inter (raster only)")]
        public bool NoSystemFonts { get; init; }

        [CommandOption("--strict-fonts")]
        [Description("Fail with FONT_NOT_FOUND when a font family cannot be resolved instead of substituting bundled Inter (raster only)")]
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

        if (!ValidationProfiles.TryParse(settings.Profile, out var profile))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown validation profile. Supported: {ValidationProfiles.WebIcon}", format);
            return ExitCodes.InvalidArguments;
        }

        var exportType = settings.Type.ToLowerInvariant();
        if (exportType is not ("svg" or "png" or "jpeg" or "jpg" or "webp"))
        {
            AgentOutput.WriteError(EngineErrors.ExportUnsupported,
                $"Unsupported export format: {settings.Type}", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.Symbol is not null)
        {
            if (exportType is not "svg")
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    "--symbol only applies to svg export", format);
                return ExitCodes.InvalidArguments;
            }
            if (settings.Scale != 1.0 || settings.Width is not null || settings.Height is not null)
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    "--symbol cannot be combined with --scale, --width, or --height; a symbol has no intrinsic size", format);
                return ExitCodes.InvalidArguments;
            }
            if (!SvgExportAdapter.IsValidSymbolId(settings.Symbol))
            {
                AgentOutput.WriteError(EngineErrors.InvalidProperty,
                    $"Symbol id must start with a letter, use only letters, digits, '-' or '_', and be at most {SvgExportAdapter.MaxSymbolIdCharacters} characters", format);
                return ExitCodes.InvalidArguments;
            }
        }

        Guid? nodeId = null;
        if (settings.Node is not null)
        {
            if (settings.Symbol is not null || settings.Width is not null || settings.Height is not null)
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    "--node cannot be combined with --symbol, --width, or --height; the exported size is intrinsic to the subtree", format);
                return ExitCodes.InvalidArguments;
            }
            if (!Guid.TryParse(settings.Node, out var parsedNodeId))
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    $"--node must be a valid node id (GUID): {settings.Node}", format);
                return ExitCodes.InvalidArguments;
            }
            nodeId = parsedNodeId;
        }

        if (settings.CurrentColor is not null)
        {
            if (exportType is not "svg")
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    "--current-color only applies to svg export", format);
                return ExitCodes.InvalidArguments;
            }
            if (!DocumentLimits.IsValidColor(settings.CurrentColor))
            {
                AgentOutput.WriteError(EngineErrors.InvalidProperty,
                    "--current-color must be a hex, rgb(), or rgba() color", format);
                return ExitCodes.InvalidArguments;
            }
        }

        var fontDirectories = settings.FontDirs ?? [];
        var hasFontOptions = fontDirectories.Length > 0 || settings.NoSystemFonts || settings.StrictFonts;
        if (hasFontOptions && !IsRasterFormat(exportType))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                "--font-dir, --no-system-fonts, and --strict-fonts only apply to raster export (png, jpeg, webp)", format);
            return ExitCodes.InvalidArguments;
        }
        if (fontDirectories.Length > FontResolutionOptions.MaxFontDirectories)
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"At most {FontResolutionOptions.MaxFontDirectories} --font-dir values are allowed", format);
            return ExitCodes.InvalidArguments;
        }
        foreach (var directory in fontDirectories)
        {
            if (!Directory.Exists(directory))
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    $"Font directory not found: {directory}", format);
                return ExitCodes.InvalidArguments;
            }
        }

        if (!SurfaceIO.IsFinite(settings.Scale) || settings.Scale is < 0.1 or > 4.0 ||
            settings.Width is < 1 or > SurfaceIO.MaxRasterDimension ||
            settings.Height is < 1 or > SurfaceIO.MaxRasterDimension)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty,
                $"Scale must be 0.1-4 and dimensions must be 1-{SurfaceIO.MaxRasterDimension}", format);
            return ExitCodes.InvalidArguments;
        }

        if (!File.Exists(settings.File))
        {
            AgentOutput.WriteError("FILE_NOT_FOUND", $"File not found: {settings.File}", format);
            return ExitCodes.GeneralError;
        }
        if (!SurfaceIO.TryCheckFileSize(settings.File, SurfaceIO.MaxInputFileBytes, out var sizeError))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", sizeError!, format);
            return ExitCodes.InvalidArguments;
        }

        var loadResult = _engine.TryLoadDocumentFromFile(settings.File, ct);

        if (!loadResult.Success || loadResult.Document is null)
        {
            var error = loadResult.Error;
            AgentOutput.WriteError("PARSE_ERROR", error?.Message ?? "Failed to parse document", format, error?.Path);
            return ExitCodes.GeneralError;
        }

        var document = loadResult.Document;

        if (settings.Strict || profile != ValidationProfile.Default)
        {
            var validation = _engine.Validate(document, new ValidationOptions
            {
                Profile = profile,
                BackgroundOverride = settings.Background
            });
            ValidationError? error = validation.Errors.FirstOrDefault();
            if (error is not null)
            {
                AgentOutput.WriteError(error.Code, error.Message, format, error.Path);
                return ExitCodes.ValidationFailed;
            }

            var warning = settings.Strict ? validation.Warnings.FirstOrDefault() : null;
            if (warning is not null)
            {
                AgentOutput.WriteError(warning.Code, warning.Message, format, warning.Path);
                return ExitCodes.ValidationWarnings;
            }
        }

        // A sub-node export sizes the surface to the subtree bounds, not the canvas, so the
        // canvas-based pre-check does not apply; the renderer's own dimension guard covers it.
        if (IsRasterFormat(exportType) && nodeId is null)
        {
            var effectiveWidth = settings.Width ?? Math.Ceiling(document.Canvas.Width * settings.Scale);
            var effectiveHeight = settings.Height ?? Math.Ceiling(document.Canvas.Height * settings.Scale);
            if (effectiveWidth > SurfaceIO.MaxRasterDimension || effectiveHeight > SurfaceIO.MaxRasterDimension)
            {
                AgentOutput.WriteError(EngineErrors.InvalidDimensions,
                    $"Effective raster dimensions cannot exceed {SurfaceIO.MaxRasterDimension}", format);
                return ExitCodes.InvalidArguments;
            }
        }

        try
        {
            byte[] data;
            string mimeType;
            IReadOnlyList<RenderDiagnostic> diagnostics = [];

            if (IsRasterFormat(exportType))
            {
                // Explicit names only: the internal Rgba8 buffer is never a file format.
                var renderFormat = exportType switch
                {
                    "png" => RenderFormat.Png,
                    "jpeg" or "jpg" => RenderFormat.Jpeg,
                    "webp" => RenderFormat.Webp,
                    _ => throw new DocumentEngineException(EngineErrors.ExportUnsupported, $"Unsupported export format: {settings.Type}")
                };

                var renderOptions = new RenderOptions
                {
                    Scale = settings.Scale,
                    Width = settings.Width,
                    Height = settings.Height,
                    Background = settings.Background,
                    Format = renderFormat,
                    FontDirectories = fontDirectories,
                    SystemFonts = !settings.NoSystemFonts,
                    StrictFonts = settings.StrictFonts
                };

                var result = nodeId is { } rasterNode
                    ? _engine.RenderNode(document, rasterNode, renderOptions, ct)
                    : _engine.Render(document, renderOptions, ct);
                data = result.Data;
                mimeType = result.MimeType;
                diagnostics = result.Diagnostics;
            }
            else
            {
                var exportOptions = new ExportOptions
                {
                    Format = settings.Type,
                    Scale = settings.Scale,
                    Width = settings.Width,
                    Height = settings.Height,
                    Background = settings.Background,
                    SymbolId = settings.Symbol,
                    CurrentColor = settings.CurrentColor
                };

                var result = nodeId is { } vectorNode
                    ? await _engine.ExportNodeAsync(document, vectorNode, exportOptions, ct)
                    : await _engine.ExportAsync(document, exportOptions, ct);
                data = result.Data;
                mimeType = result.MimeType;
            }

            if (data.LongLength > SurfaceIO.MaxOutputBytes)
            {
                AgentOutput.WriteError("OUTPUT_TOO_LARGE",
                    $"Output exceeds the {SurfaceIO.MaxOutputBytes} byte limit", format);
                return ExitCodes.GeneralError;
            }

            var outputPath = settings.Output
                ?? DeriveOutputPath(settings.File, settings.Symbol is null ? settings.Type : "symbol.svg");
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await File.WriteAllBytesAsync(outputPath, data, ct);

            var exportResult = new
            {
                path = SurfaceIO.DisplayPath(outputPath),
                type = settings.Type,
                size = data.Length,
                mimeType,
                diagnostics = diagnostics.Select(d => new { code = d.Code, message = d.Message }).ToArray()
            };

            foreach (var diagnostic in diagnostics)
            {
                AgentOutput.WriteDiagnostic(diagnostic.Code, diagnostic.Message, format);
            }
            AgentOutput.WriteSuccess($"Exported to: {outputPath}", format, exportResult);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static string DeriveOutputPath(string inputPath, string type)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        if (name.EndsWith(".agd", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return Path.Combine(dir, $"{name}.{type}");
    }

    private static bool IsRasterFormat(string format) =>
        format is "png" or "jpeg" or "jpg" or "webp";
}
