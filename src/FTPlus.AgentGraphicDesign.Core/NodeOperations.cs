namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// Canonical node types. The engine accepts both enum values and their string equivalents.
/// </summary>
public enum NodeType
{
    Rectangle,
    Ellipse,
    Text,
    Path,
    Group,
    Frame,
    Line,
    Polygon,
    Image
}

/// <summary>
/// Maps node type strings (including aliases) to canonical <see cref="NodeType"/>.
/// </summary>
public static class NodeTypeParser
{
    /// <summary>
    /// Try to parse a node type string. Supports canonical names and aliases:
    /// rectangle/rect, ellipse/circle, text, path, group, frame.
    /// </summary>
    public static bool TryParse(string typeString, out NodeType nodeType)
    {
        nodeType = typeString.ToLowerInvariant() switch
        {
            "rectangle" or "rect" => NodeType.Rectangle,
            "ellipse" or "circle" => NodeType.Ellipse,
            "text" => NodeType.Text,
            "path" => NodeType.Path,
            "group" => NodeType.Group,
            "frame" => NodeType.Frame,
            "line" => NodeType.Line,
            "polygon" or "poly" => NodeType.Polygon,
            "image" or "img" => NodeType.Image,
            _ => (NodeType)(-1)
        };
        return (int)nodeType >= 0;
    }

    /// <summary>All accepted type strings including aliases.</summary>
    public static readonly string[] AcceptedTypes = ["rectangle", "rect", "ellipse", "circle", "text", "path", "group", "frame", "line", "polygon", "poly", "image", "img"];

    /// <summary>Canonical type names (no aliases).</summary>
    public static readonly string[] CanonicalTypes = ["rectangle", "ellipse", "text", "path", "group", "frame", "line", "polygon", "image"];
}

/// <summary>Specification for creating a new node via <see cref="IDocumentEngine.AddNode"/>.</summary>
public sealed record NewNodeSpec
{
    /// <summary>Node type: rectangle, ellipse, text, path, group, frame (aliases: rect, circle).</summary>
    public required string Type { get; init; }

    public string? Name { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public string? Fill { get; init; }

    public string? Stroke { get; init; }

    /// <summary>Reference to a color token used for the fill.</summary>
    public string? FillTokenRef { get; init; }

    /// <summary>Reference to a color token used for the stroke.</summary>
    public string? StrokeTokenRef { get; init; }

    /// <summary>Text content (text nodes only).</summary>
    public string? Text { get; init; }

    /// <summary>SVG path data (path nodes only).</summary>
    public string? PathData { get; init; }

    /// <summary>Corner radius (rectangle nodes only).</summary>
    public double? CornerRadius { get; init; }

    /// <summary>Font family (text nodes only).</summary>
    public string? FontFamily { get; init; }

    /// <summary>Font size in pixels (text nodes only).</summary>
    public double? FontSize { get; init; }

    /// <summary>Font weight 100-900 (text nodes only).</summary>
    public int? FontWeight { get; init; }

    /// <summary>Reference to a typography token, or <see cref="PropertyClear.ClearValue"/> to detach it.</summary>
    public string? TypographyTokenRef { get; init; }

    /// <summary>Horizontal alignment relative to x: start, center, end (text nodes only).</summary>
    public string? TextAlign { get; init; }

    /// <summary>Vertical anchor at y: top, middle, bottom, baseline (text nodes only).</summary>
    public string? VerticalAlign { get; init; }

    /// <summary>Wrap width in pixels, &gt; 0 (text nodes only).</summary>
    public double? MaxWidth { get; init; }

    /// <summary>Line height as a multiple of the font size, &gt; 0 (text nodes only).</summary>
    public double? LineHeight { get; init; }

    /// <summary>Extra advance after each glyph in pixels (text nodes only).</summary>
    public double? LetterSpacing { get; init; }

    /// <summary>Opacity 0.0-1.0.</summary>
    public double? Opacity { get; init; }

    /// <summary>Fill rule, <c>nonzero</c> or <c>evenodd</c> (path and polygon nodes only).</summary>
    public string? FillRule { get; init; }

    /// <summary>Line start X (line nodes only).</summary>
    public double? X1 { get; init; }

    /// <summary>Line start Y (line nodes only).</summary>
    public double? Y1 { get; init; }

    /// <summary>Line end X (line nodes only).</summary>
    public double? X2 { get; init; }

    /// <summary>Line end Y (line nodes only).</summary>
    public double? Y2 { get; init; }

    /// <summary>Points as "x1,y1 x2,y2 ..." string (polygon nodes only).</summary>
    public string? Points { get; init; }

    /// <summary>Whether polygon is closed (polygon nodes only, default true).</summary>
    public bool? PolygonClosed { get; init; }

    /// <summary>Image source URL or path (image nodes only).</summary>
    public string? ImageSrc { get; init; }
}

/// <summary>
/// Sentinel value for clearing style properties. Use <see cref="ClearValue"/> for
/// fill/stroke to remove them; null means "leave unchanged".
/// </summary>
public static class PropertyClear
{
    /// <summary>Sentinel string that means "clear this property".</summary>
    public const string ClearValue = "__clear__";

    /// <summary>Check if a value is the clear sentinel.</summary>
    public static bool IsClear(string? value) => value == ClearValue;
}

/// <summary>Partial updates applied by <see cref="IDocumentEngine.UpdateNode"/>. Null fields are left unchanged.</summary>
public sealed record NodeUpdates
{
    public string? Name { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public double? X { get; init; }

    public double? Y { get; init; }

    /// <summary>Fill color, or <see cref="PropertyClear.ClearValue"/> to remove fill.</summary>
    public string? Fill { get; init; }

    /// <summary>Stroke color, or <see cref="PropertyClear.ClearValue"/> to remove stroke.</summary>
    public string? Stroke { get; init; }

    /// <summary>Reference to a color token used for the fill.</summary>
    public string? FillTokenRef { get; init; }

    /// <summary>Reference to a color token used for the stroke.</summary>
    public string? StrokeTokenRef { get; init; }

    public bool? Visible { get; init; }

    /// <summary>Text content (text nodes only).</summary>
    public string? Text { get; init; }

    /// <summary>SVG path d attribute (path nodes only).</summary>
    public string? PathData { get; init; }

    /// <summary>Corner radius (rectangle nodes only).</summary>
    public double? CornerRadius { get; init; }

    /// <summary>Font family (text nodes only).</summary>
    public string? FontFamily { get; init; }

    /// <summary>Font size in pixels (text nodes only).</summary>
    public double? FontSize { get; init; }

    /// <summary>Font weight 100-900 (text nodes only).</summary>
    public int? FontWeight { get; init; }

    /// <summary>Reference to a typography token (text nodes only).</summary>
    public string? TypographyTokenRef { get; init; }

    /// <summary>Horizontal alignment: start, center, end, or <see cref="PropertyClear.ClearValue"/> for the default (text nodes only).</summary>
    public string? TextAlign { get; init; }

    /// <summary>Vertical anchor: top, middle, bottom, baseline, or <see cref="PropertyClear.ClearValue"/> for the default (text nodes only).</summary>
    public string? VerticalAlign { get; init; }

    /// <summary>Wrap width in pixels, &gt; 0 (text nodes only).</summary>
    public double? MaxWidth { get; init; }

    /// <summary>Line height as a multiple of the font size, &gt; 0 (text nodes only).</summary>
    public double? LineHeight { get; init; }

    /// <summary>Extra advance after each glyph in pixels (text nodes only).</summary>
    public double? LetterSpacing { get; init; }

    /// <summary>Style opacity 0.0-1.0.</summary>
    public double? Opacity { get; init; }

    /// <summary>
    /// Fill rule, <c>nonzero</c> or <c>evenodd</c> (path and polygon nodes only), or
    /// <see cref="PropertyClear.ClearValue"/> to reset to the nonzero default.
    /// </summary>
    public string? FillRule { get; init; }
}

/// <summary>A flattened view of one node, in document order.</summary>
public sealed record NodeInfo(
    Guid Id,
    string Type,
    string? Name,
    string Order,
    string Path,
    bool Visible,
    int Depth);
