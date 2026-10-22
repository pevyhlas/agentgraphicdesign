using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

/// <summary>
/// Fallback node type for unknown/future node types. Preserves all properties via
/// <see cref="Node.ExtensionData"/> so the document survives round-trip without data loss.
/// </summary>
public sealed class UnknownNode : Node
{
    [JsonPropertyName("originalType")]
    public string? OriginalType { get; set; }

    [JsonIgnore]
    internal string? SourceDiscriminator { get; set; }
}

public sealed class RectangleNode : Node
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("cornerRadius")]
    public double CornerRadius { get; set; }
}

public sealed class EllipseNode : Node
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public sealed class PathNode : Node
{
    [JsonPropertyName("d")]
    public string D { get; set; } = string.Empty;
}

public sealed class TextNode : Node
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; set; }

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 16;

    [JsonPropertyName("fontWeight")]
    public int FontWeight { get; set; } = 400;

    [JsonPropertyName("typographyTokenRef")]
    public string? TypographyTokenRef { get; set; }

    /// <summary>
    /// Horizontal alignment of every line relative to the node's x: <c>start</c> (default),
    /// <c>center</c>, or <c>end</c>. See <see cref="Layout.TextLayout"/>.
    /// </summary>
    [JsonPropertyName("textAlign")]
    public string? TextAlign { get; set; }

    /// <summary>
    /// Which edge of the text block sits at the node's y: <c>top</c> (default; the first
    /// baseline is one font size below y), <c>middle</c>, <c>bottom</c>, or <c>baseline</c>.
    /// </summary>
    [JsonPropertyName("verticalAlign")]
    public string? VerticalAlign { get; set; }

    /// <summary>Wrap width in pixels; null disables wrapping.</summary>
    [JsonPropertyName("maxWidth")]
    public double? MaxWidth { get; set; }

    /// <summary>Line pitch as a multiple of the font size; null means the token value or 1.2.</summary>
    [JsonPropertyName("lineHeight")]
    public double? LineHeight { get; set; }

    /// <summary>Extra advance after each glyph in pixels; null means 0.</summary>
    [JsonPropertyName("letterSpacing")]
    public double? LetterSpacing { get; set; }
}

public sealed class LineNode : Node
{
    [JsonPropertyName("x1")]
    public double X1 { get; set; }

    [JsonPropertyName("y1")]
    public double Y1 { get; set; }

    [JsonPropertyName("x2")]
    public double X2 { get; set; }

    [JsonPropertyName("y2")]
    public double Y2 { get; set; }
}

public sealed class PolygonNode : Node
{
    [JsonPropertyName("points")]
    public List<Point> Points { get; set; } = [];

    [JsonPropertyName("closed")]
    public bool Closed { get; set; } = true;
}

public sealed class Point
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class ImageNode : Node
{
    [JsonPropertyName("src")]
    public string Src { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}
