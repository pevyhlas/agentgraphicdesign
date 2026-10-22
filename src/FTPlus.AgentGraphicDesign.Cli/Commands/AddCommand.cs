using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class AddCommand : Command<AddCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public AddCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandArgument(1, "<TYPE>")]
        [Description("Node type: rectangle, ellipse, text, path, group, frame, line, polygon, image (aliases: rect, circle, poly, img)")]
        public required string Type { get; init; }

        [CommandOption("-p|--parent <ID>")]
        [Description("Parent node ID (default: root)")]
        public string? Parent { get; init; }

        [CommandOption("-n|--name <NAME>")]
        [Description("Node name")]
        public string? Name { get; init; }

        [CommandOption("-w|--width <WIDTH>")]
        [Description("Width in pixels")]
        public double? Width { get; init; }

        [CommandOption("-h|--height <HEIGHT>")]
        [Description("Height in pixels")]
        public double? Height { get; init; }

        [CommandOption("-x|--pos-x <X>")]
        [Description("X position")]
        [DefaultValue(0.0)]
        public double X { get; init; }

        [CommandOption("-y|--pos-y <Y>")]
        [Description("Y position")]
        [DefaultValue(0.0)]
        public double Y { get; init; }

        [CommandOption("--fill <COLOR>")]
        [Description("Fill color in hex format (e.g., #ff0000)")]
        public string? Fill { get; init; }

        [CommandOption("--stroke <COLOR>")]
        [Description("Stroke color in hex format")]
        public string? Stroke { get; init; }

        [CommandOption("--fill-token <TOKEN>")]
        [Description("Fill color token reference")]
        public string? FillToken { get; init; }

        [CommandOption("--stroke-token <TOKEN>")]
        [Description("Stroke color token reference")]
        public string? StrokeToken { get; init; }

        [CommandOption("--text <CONTENT>")]
        [Description("Text content (for text nodes)")]
        public string? Text { get; init; }

        [CommandOption("--path-data <DATA>")]
        [Description("SVG path data (for path nodes)")]
        public string? PathData { get; init; }

        [CommandOption("--corner-radius <RADIUS>")]
        [Description("Corner radius (for rectangle nodes)")]
        public double? CornerRadius { get; init; }

        [CommandOption("--font-family <FAMILY>")]
        [Description("Font family (for text nodes)")]
        public string? FontFamily { get; init; }

        [CommandOption("--font-size <SIZE>")]
        [Description("Font size in pixels (for text nodes)")]
        public double? FontSize { get; init; }

        [CommandOption("--font-weight <WEIGHT>")]
        [Description("Font weight 100-900 (for text nodes)")]
        public int? FontWeight { get; init; }

        [CommandOption("--typography-token <TOKEN>")]
        [Description("Typography token reference (for text nodes)")]
        public string? TypographyToken { get; init; }

        [CommandOption("--text-align <ALIGN>")]
        [Description("Horizontal alignment relative to x: start, center, end (for text nodes)")]
        public string? TextAlign { get; init; }

        [CommandOption("--vertical-align <ALIGN>")]
        [Description("Vertical anchor at y: top, middle, bottom, baseline (for text nodes)")]
        public string? VerticalAlign { get; init; }

        [CommandOption("--max-width <WIDTH>")]
        [Description("Wrap width in pixels; words wrap so no line exceeds it (for text nodes)")]
        public double? MaxWidth { get; init; }

        [CommandOption("--line-height <FACTOR>")]
        [Description("Line height as a multiple of the font size, default 1.2 (for text nodes)")]
        public double? LineHeight { get; init; }

        [CommandOption("--letter-spacing <PIXELS>")]
        [Description("Extra spacing after each glyph in pixels (for text nodes)")]
        public double? LetterSpacing { get; init; }

        [CommandOption("--opacity <OPACITY>")]
        [Description("Opacity 0.0-1.0")]
        public double? Opacity { get; init; }

        [CommandOption("--fill-rule <RULE>")]
        [Description("Fill rule: nonzero or evenodd (for path and polygon nodes)")]
        public string? FillRule { get; init; }

        [CommandOption("--x1 <X1>")]
        [Description("Line start X (for line nodes)")]
        public double? X1 { get; init; }

        [CommandOption("--y1 <Y1>")]
        [Description("Line start Y (for line nodes)")]
        public double? Y1 { get; init; }

        [CommandOption("--x2 <X2>")]
        [Description("Line end X (for line nodes)")]
        public double? X2 { get; init; }

        [CommandOption("--y2 <Y2>")]
        [Description("Line end Y (for line nodes)")]
        public double? Y2 { get; init; }

        [CommandOption("--points <POINTS>")]
        [Description("Points as \"x1,y1 x2,y2 ...\" string (for polygon nodes)")]
        public string? Points { get; init; }

        [CommandOption("--open")]
        [Description("Create an open polygon instead of a closed polygon")]
        public bool PolygonOpen { get; init; }

        [CommandOption("--src <SRC>")]
        [Description("Image source URL or path (for image nodes)")]
        public string? ImageSrc { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--expected-revn <REVN>")]
        [Description("Expected document revision (optimistic concurrency)")]
        public long? ExpectedRevn { get; init; }
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

        if (!ValidFinite(settings.Width) || !ValidFinite(settings.Height) ||
            settings.Width is < 0 || settings.Height is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidDimensions,
                "Node dimensions must be finite and non-negative", format);
            return ExitCodes.InvalidArguments;
        }

        if (!ValidFinite(settings.X) || !ValidFinite(settings.Y) ||
            !ValidFinite(settings.CornerRadius) || !ValidFinite(settings.FontSize) ||
            !ValidFinite(settings.MaxWidth) || !ValidFinite(settings.LineHeight) || !ValidFinite(settings.LetterSpacing) ||
            !ValidFinite(settings.Opacity) || !ValidFinite(settings.X1) ||
            !ValidFinite(settings.Y1) || !ValidFinite(settings.X2) || !ValidFinite(settings.Y2) ||
            settings.CornerRadius is < 0 ||
            settings.FontSize is <= 0 || settings.FontWeight is < 100 or > 900 ||
            settings.MaxWidth is <= 0 || settings.LineHeight is <= 0 ||
            settings.Opacity is < 0 or > 1 || settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "One or more numeric values are non-finite or outside the allowed range", format);
            return ExitCodes.InvalidArguments;
        }

        if (SurfaceIO.IsTooLong(settings.Type, SurfaceIO.MaxNameLength) ||
            SurfaceIO.IsTooLong(settings.Name, SurfaceIO.MaxNameLength) ||
            SurfaceIO.IsTooLong(settings.Fill, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.Stroke, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.FillToken, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.StrokeToken, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.TypographyToken, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.FontFamily, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.FillRule, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.TextAlign, SurfaceIO.MaxNameLength) ||
            SurfaceIO.IsTooLong(settings.VerticalAlign, SurfaceIO.MaxNameLength) ||
            TooLong(settings.Text) || TooLong(settings.PathData) || TooLong(settings.Points) || TooLong(settings.ImageSrc))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", "One or more text inputs exceed the allowed limit", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        Guid? parentId = null;
        if (!string.IsNullOrEmpty(settings.Parent))
        {
            if (!Guid.TryParse(settings.Parent, out var parsed))
            {
                AgentOutput.WriteError(EngineErrors.ParentNotFound, $"Invalid parent ID: {settings.Parent}", format);
                return ExitCodes.GeneralError;
            }

            parentId = parsed;
        }

        try
        {
            var node = _engine.AddNode(handle.Document, parentId, new NewNodeSpec
            {
                Type = settings.Type,
                Name = settings.Name,
                Width = settings.Width,
                Height = settings.Height,
                X = settings.X,
                Y = settings.Y,
                Fill = settings.Fill,
                Stroke = settings.Stroke,
                FillTokenRef = settings.FillToken,
                StrokeTokenRef = settings.StrokeToken,
                Text = settings.Text,
                PathData = settings.PathData,
                CornerRadius = settings.CornerRadius,
                FontFamily = settings.FontFamily,
                FontSize = settings.FontSize,
                FontWeight = settings.FontWeight,
                TypographyTokenRef = settings.TypographyToken,
                TextAlign = settings.TextAlign,
                VerticalAlign = settings.VerticalAlign,
                MaxWidth = settings.MaxWidth,
                LineHeight = settings.LineHeight,
                LetterSpacing = settings.LetterSpacing,
                Opacity = settings.Opacity,
                FillRule = settings.FillRule,
                X1 = settings.X1,
                Y1 = settings.Y1,
                X2 = settings.X2,
                Y2 = settings.Y2,
                Points = settings.Points,
                PolygonClosed = settings.Type.Equals("polygon", StringComparison.OrdinalIgnoreCase) ||
                    settings.Type.Equals("poly", StringComparison.OrdinalIgnoreCase)
                        ? !settings.PolygonOpen
                        : null,
                ImageSrc = settings.ImageSrc
            });

            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            var result = new
            {
                nodeId = node.Id,
                nodeType = settings.Type,
                nodeName = node.Name,
                documentRevn = handle.Document.Revn
            };

            AgentOutput.WriteSuccess($"Added {settings.Type} node: {node.Id}", format, result);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static bool ValidFinite(double? value) => !value.HasValue || SurfaceIO.IsFinite(value.Value);

    private static bool TooLong(string? value) => value?.Length > SurfaceIO.MaxTextLength;
}
