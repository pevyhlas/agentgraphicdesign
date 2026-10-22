using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class UpdateCommand : Command<UpdateCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public UpdateCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandArgument(1, "<NODE_ID>")]
        [Description("Node ID (GUID) to update")]
        public required string NodeId { get; init; }

        [CommandOption("-n|--name <NAME>")]
        [Description("New name")]
        public string? Name { get; init; }

        [CommandOption("-w|--width <WIDTH>")]
        [Description("New width in pixels")]
        public double? Width { get; init; }

        [CommandOption("-h|--height <HEIGHT>")]
        [Description("New height in pixels")]
        public double? Height { get; init; }

        [CommandOption("-x|--pos-x <X>")]
        [Description("New X position")]
        public double? X { get; init; }

        [CommandOption("-y|--pos-y <Y>")]
        [Description("New Y position")]
        public double? Y { get; init; }

        [CommandOption("--fill <COLOR>")]
        [Description("New fill color in hex format, or '__clear__' to remove")]
        public string? Fill { get; init; }

        [CommandOption("--stroke <COLOR>")]
        [Description("New stroke color in hex format, or '__clear__' to remove")]
        public string? Stroke { get; init; }

        [CommandOption("--fill-token <TOKEN>")]
        [Description("New fill color token reference")]
        public string? FillToken { get; init; }

        [CommandOption("--stroke-token <TOKEN>")]
        [Description("New stroke color token reference")]
        public string? StrokeToken { get; init; }

        [CommandOption("--visible <BOOL>")]
        [Description("New visibility (true/false)")]
        public bool? Visible { get; init; }

        [CommandOption("--text <CONTENT>")]
        [Description("New text content (text nodes only)")]
        public string? Text { get; init; }

        [CommandOption("--path-data <DATA>")]
        [Description("New SVG path data (path nodes only)")]
        public string? PathData { get; init; }

        [CommandOption("--corner-radius <RADIUS>")]
        [Description("New corner radius (rectangle nodes only)")]
        public double? CornerRadius { get; init; }

        [CommandOption("--font-family <FAMILY>")]
        [Description("New font family (text nodes only)")]
        public string? FontFamily { get; init; }

        [CommandOption("--font-size <SIZE>")]
        [Description("New font size in pixels (text nodes only)")]
        public double? FontSize { get; init; }

        [CommandOption("--font-weight <WEIGHT>")]
        [Description("New font weight 100-900 (text nodes only)")]
        public int? FontWeight { get; init; }

        [CommandOption("--typography-token <TOKEN>")]
        [Description("New typography token reference, or '__clear__' to detach (text nodes only)")]
        public string? TypographyToken { get; init; }

        [CommandOption("--text-align <ALIGN>")]
        [Description("New horizontal alignment relative to x: start, center, end, or '__clear__' for the default (text nodes only)")]
        public string? TextAlign { get; init; }

        [CommandOption("--vertical-align <ALIGN>")]
        [Description("New vertical anchor at y: top, middle, bottom, baseline, or '__clear__' for the default (text nodes only)")]
        public string? VerticalAlign { get; init; }

        [CommandOption("--max-width <WIDTH>")]
        [Description("New wrap width in pixels; words wrap so no line exceeds it (text nodes only)")]
        public double? MaxWidth { get; init; }

        [CommandOption("--line-height <FACTOR>")]
        [Description("New line height as a multiple of the font size, default 1.2 (text nodes only)")]
        public double? LineHeight { get; init; }

        [CommandOption("--letter-spacing <PIXELS>")]
        [Description("New extra spacing after each glyph in pixels (text nodes only)")]
        public double? LetterSpacing { get; init; }

        [CommandOption("--opacity <OPACITY>")]
        [Description("New opacity 0.0-1.0")]
        public double? Opacity { get; init; }

        [CommandOption("--fill-rule <RULE>")]
        [Description("New fill rule: nonzero or evenodd, or '__clear__' to reset (path and polygon nodes only)")]
        public string? FillRule { get; init; }

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
            !ValidFinite(settings.X) || !ValidFinite(settings.Y) ||
            !ValidFinite(settings.CornerRadius) || !ValidFinite(settings.FontSize) || !ValidFinite(settings.Opacity) ||
            !ValidFinite(settings.MaxWidth) || !ValidFinite(settings.LineHeight) || !ValidFinite(settings.LetterSpacing) ||
            settings.Width is < 0 || settings.Height is < 0 || settings.CornerRadius is < 0 ||
            settings.FontSize is <= 0 || settings.FontWeight is < 100 or > 900 ||
            settings.MaxWidth is <= 0 || settings.LineHeight is <= 0 ||
            settings.Opacity is < 0 or > 1 || settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "One or more numeric values are non-finite or outside the allowed range", format);
            return ExitCodes.InvalidArguments;
        }

        if (SurfaceIO.IsTooLong(settings.Name, SurfaceIO.MaxNameLength) ||
            SurfaceIO.IsTooLong(settings.Fill, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.Stroke, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.FillToken, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.StrokeToken, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.TypographyToken, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.FontFamily, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.FillRule, SurfaceIO.MaxPersistedValueLength) ||
            SurfaceIO.IsTooLong(settings.TextAlign, SurfaceIO.MaxNameLength) ||
            SurfaceIO.IsTooLong(settings.VerticalAlign, SurfaceIO.MaxNameLength) ||
            SurfaceIO.IsTooLong(settings.Text, SurfaceIO.MaxTextLength) ||
            SurfaceIO.IsTooLong(settings.PathData, SurfaceIO.MaxTextLength))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", "One or more text values exceed the allowed limit", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        if (!Guid.TryParse(settings.NodeId, out var nodeId))
        {
            AgentOutput.WriteError(EngineErrors.NodeNotFound, $"Invalid node ID: {settings.NodeId}", format);
            return ExitCodes.GeneralError;
        }

        try
        {
            var node = _engine.UpdateNode(handle.Document, nodeId, new NodeUpdates
            {
                Name = settings.Name,
                Width = settings.Width,
                Height = settings.Height,
                X = settings.X,
                Y = settings.Y,
                Fill = settings.Fill,
                Stroke = settings.Stroke,
                FillTokenRef = settings.FillToken,
                StrokeTokenRef = settings.StrokeToken,
                Visible = settings.Visible,
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
                FillRule = settings.FillRule
            });

            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            var result = new
            {
                nodeId = node.Id,
                nodeName = node.Name,
                documentRevn = handle.Document.Revn
            };

            AgentOutput.WriteSuccess($"Updated node: {node.Id}", format, result);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static bool ValidFinite(double? value) => !value.HasValue || SurfaceIO.IsFinite(value.Value);
}
