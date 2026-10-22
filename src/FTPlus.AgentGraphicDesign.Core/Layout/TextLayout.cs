using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// Paragraph layout shared by bounds, SVG export, and the Skia renderer, so every
/// surface breaks, aligns, and stacks lines the same way.
/// </summary>
/// <remarks>
/// Coordinate model (node-local, y down): the node origin is the anchor. Line <c>i</c>
/// has its baseline at <c>fontSize + i × lineHeight × fontSize</c>; the block occupies
/// <c>lineCount × lineHeight × fontSize</c> starting at y = 0. <c>textAlign</c> shifts each
/// line left by a fraction of its own width; <c>verticalAlign</c> shifts the whole block so
/// its top (default), middle, bottom, or first baseline lands on the origin. A single
/// line with default alignment therefore renders exactly as it did before these fields
/// existed: baseline at <c>y = fontSize</c>, first glyph at <c>x = 0</c>.
/// </remarks>
public static class TextLayout
{
    public const string AlignStart = "start";
    public const string AlignCenter = "center";
    public const string AlignEnd = "end";

    public const string VerticalTop = "top";
    public const string VerticalMiddle = "middle";
    public const string VerticalBottom = "bottom";
    public const string VerticalBaseline = "baseline";

    /// <summary>Line pitch multiplier used when neither the node nor its typography token sets one.</summary>
    public const double DefaultLineHeight = 1.2;

    public static readonly string[] TextAlignValues = [AlignStart, AlignCenter, AlignEnd];

    public static readonly string[] VerticalAlignValues = [VerticalTop, VerticalMiddle, VerticalBottom, VerticalBaseline];

    /// <summary>True for null (default) or one of <see cref="TextAlignValues"/>.</summary>
    public static bool IsValidTextAlign(string? value) =>
        value is null || Array.IndexOf(TextAlignValues, value) >= 0;

    /// <summary>True for null (default) or one of <see cref="VerticalAlignValues"/>.</summary>
    public static bool IsValidVerticalAlign(string? value) =>
        value is null || Array.IndexOf(VerticalAlignValues, value) >= 0;

    /// <summary>Line pitch multiplier: the node value wins, then a positive token value, then <see cref="DefaultLineHeight"/>.</summary>
    public static double ResolveLineHeight(double? nodeLineHeight, double? tokenLineHeight)
    {
        if (nodeLineHeight is { } node && double.IsFinite(node) && node > 0)
            return node;
        if (tokenLineHeight is { } token && double.IsFinite(token) && token > 0)
            return token;
        return DefaultLineHeight;
    }

    /// <summary>
    /// Typography that applies to <paramref name="text"/> after its token is resolved. Font
    /// family, size, and weight come from the token when it sets them (as before); line
    /// height prefers the node value; alignment, wrap width, and letter spacing are node-only.
    /// </summary>
    public static ResolvedTextStyle Resolve(TextNode text, TypographyToken? token)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ResolvedTextStyle
        {
            FontFamily = token?.FontFamily ?? text.FontFamily,
            FontSize = token?.FontSize ?? text.FontSize,
            FontWeight = token?.FontWeight ?? text.FontWeight,
            LineHeight = ResolveLineHeight(text.LineHeight, token?.LineHeight),
            LetterSpacing = text.LetterSpacing is { } spacing && double.IsFinite(spacing) ? spacing : 0,
            TextAlign = IsValidTextAlign(text.TextAlign) ? text.TextAlign ?? AlignStart : AlignStart,
            VerticalAlign = IsValidVerticalAlign(text.VerticalAlign) ? text.VerticalAlign ?? VerticalTop : VerticalTop,
            MaxWidth = text.MaxWidth is { } width && double.IsFinite(width) && width > 0 ? width : null
        };
    }

    /// <summary>
    /// Splits <paramref name="text"/> into lines. Explicit line breaks (<c>\n</c>, <c>\r\n</c>,
    /// <c>\r</c>) always break. When <paramref name="maxWidth"/> is a positive finite number,
    /// each paragraph is wrapped greedily at spaces so that <paramref name="measureWidth"/> of
    /// every line is at most <paramref name="maxWidth"/>; a single word wider than the limit
    /// stays on its own line and is never broken inside. Runs of spaces between wrapped
    /// words collapse to one space. The result is deterministic for a deterministic measurer.
    /// </summary>
    public static IReadOnlyList<string> BreakLines(string text, double? maxWidth, Func<string, double> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureWidth);

        var lines = new List<string>();
        var wrap = maxWidth is { } limit && double.IsFinite(limit) && limit > 0;
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (!wrap || paragraph.Length == 0)
            {
                lines.Add(paragraph);
                continue;
            }

            WrapParagraph(paragraph, maxWidth!.Value, measureWidth, lines);
        }

        return lines;
    }

    /// <summary>Horizontal offset of a line's start from the anchor for <paramref name="textAlign"/>.</summary>
    public static double AlignOffset(string? textAlign, double lineWidth) => textAlign switch
    {
        AlignCenter => -lineWidth / 2,
        AlignEnd => -lineWidth,
        _ => 0
    };

    /// <summary>
    /// Vertical offset applied to the whole block so that the edge named by
    /// <paramref name="verticalAlign"/> sits on the anchor. The block is
    /// <paramref name="lineCount"/> × <paramref name="lineHeight"/> × <paramref name="fontSize"/>
    /// tall with its top at 0 and its first baseline at <paramref name="fontSize"/>.
    /// </summary>
    public static double VerticalOffset(string? verticalAlign, int lineCount, double fontSize, double lineHeight) => verticalAlign switch
    {
        VerticalMiddle => -BlockHeight(lineCount, fontSize, lineHeight) / 2,
        VerticalBottom => -BlockHeight(lineCount, fontSize, lineHeight),
        VerticalBaseline => -fontSize,
        _ => 0
    };

    /// <summary>Distance between consecutive baselines.</summary>
    public static double LinePitch(double fontSize, double lineHeight) => fontSize * lineHeight;

    /// <summary>Nominal block height: <paramref name="lineCount"/> line boxes of <see cref="LinePitch"/>.</summary>
    public static double BlockHeight(int lineCount, double fontSize, double lineHeight) =>
        Math.Max(lineCount, 1) * LinePitch(fontSize, lineHeight);

    /// <summary>SVG <c>text-anchor</c> value for <paramref name="textAlign"/>, or null for the default.</summary>
    public static string? ToSvgTextAnchor(string? textAlign) => textAlign switch
    {
        AlignCenter => "middle",
        AlignEnd => "end",
        _ => null
    };

    /// <summary>Inverse of <see cref="ToSvgTextAnchor"/>; null for <c>start</c>, unknown values, or null.</summary>
    public static string? FromSvgTextAnchor(string? textAnchor) => textAnchor?.Trim().ToLowerInvariant() switch
    {
        "middle" => AlignCenter,
        "end" => AlignEnd,
        _ => null
    };

    private static void WrapParagraph(string paragraph, double maxWidth, Func<string, double> measureWidth, List<string> lines)
    {
        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            lines.Add(string.Empty);
            return;
        }

        var current = words[0];
        for (var index = 1; index < words.Length; index++)
        {
            var candidate = current + " " + words[index];
            if (measureWidth(candidate) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = words[index];
            }
        }

        lines.Add(current);
    }
}

/// <summary>Typography of one text node after token resolution and defaulting.</summary>
public sealed record ResolvedTextStyle
{
    public string? FontFamily { get; init; }

    public double FontSize { get; init; } = 16;

    public int FontWeight { get; init; } = 400;

    public double LineHeight { get; init; } = TextLayout.DefaultLineHeight;

    public double LetterSpacing { get; init; }

    public string TextAlign { get; init; } = TextLayout.AlignStart;

    public string VerticalAlign { get; init; } = TextLayout.VerticalTop;

    public double? MaxWidth { get; init; }

    public TextMeasurementRequest ToMeasurementRequest(string line) => new()
    {
        Text = line,
        FontFamily = FontFamily,
        FontSize = FontSize,
        FontWeight = FontWeight,
        LetterSpacing = LetterSpacing
    };
}
