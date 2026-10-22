using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Layout;

/// <summary>
/// Pure line-breaking and anchoring rules shared by bounds, SVG export, and the Skia
/// renderer. A fixed-width measurer (10 px per character) keeps the expectations exact.
/// </summary>
public class TextLayoutTests
{
    private static double TenPerChar(string line) => line.Length * 10;

    [Fact]
    public void BreakLines_NoMaxWidthNoNewline_ReturnsTextUnchanged()
    {
        var lines = TextLayout.BreakLines("Hello, World!  spaced", null, TenPerChar);

        Assert.Equal(["Hello, World!  spaced"], lines);
    }

    [Fact]
    public void BreakLines_ExplicitNewlines_AlwaysBreak()
    {
        var lines = TextLayout.BreakLines("one\ntwo\r\nthree\rfour", null, TenPerChar);

        Assert.Equal(["one", "two", "three", "four"], lines);
    }

    [Fact]
    public void BreakLines_WrapsGreedilyWithinMaxWidth()
    {
        // 10 px per character, limit 110 px: at most 11 characters per line.
        var lines = TextLayout.BreakLines("the quick brown fox jumps over the lazy dog", 110, TenPerChar);

        Assert.Equal(["the quick", "brown fox", "jumps over", "the lazy", "dog"], lines);
        Assert.All(lines, line => Assert.True(TenPerChar(line) <= 110));
    }

    [Fact]
    public void BreakLines_LongSingleWord_StaysOnItsOwnLineUnbroken()
    {
        var lines = TextLayout.BreakLines("a supercalifragilistic word", 100, TenPerChar);

        Assert.Equal(["a", "supercalifragilistic", "word"], lines);
        Assert.True(TenPerChar(lines[1]) > 100);
    }

    [Fact]
    public void BreakLines_EmptyParagraphsAndSpaceRuns_AreKeptAsEmptyLines()
    {
        var lines = TextLayout.BreakLines("first\n\n   \nlast", 100, TenPerChar);

        Assert.Equal(["first", "", "", "last"], lines);
    }

    [Fact]
    public void BreakLines_CollapsesSpaceRunsOnlyWhenWrapping()
    {
        Assert.Equal(["a  b"], TextLayout.BreakLines("a  b", null, TenPerChar));
        Assert.Equal(["a b"], TextLayout.BreakLines("a  b", 100, TenPerChar));
    }

    [Fact]
    public void BreakLines_IsDeterministic()
    {
        const string text = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor";

        var first = TextLayout.BreakLines(text, 155, TenPerChar);
        var second = TextLayout.BreakLines(text, 155, TenPerChar);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BreakLines_NonPositiveMaxWidth_DoesNotWrap()
    {
        Assert.Equal(["a b c"], TextLayout.BreakLines("a b c", 0, TenPerChar));
        Assert.Equal(["a b c"], TextLayout.BreakLines("a b c", double.NaN, TenPerChar));
    }

    [Theory]
    [InlineData("start", 0)]
    [InlineData(null, 0)]
    [InlineData("center", -50)]
    [InlineData("end", -100)]
    public void AlignOffset_ShiftsLineByItsOwnWidth(string? align, double expected)
    {
        Assert.Equal(expected, TextLayout.AlignOffset(align, 100));
    }

    [Theory]
    [InlineData("top", 0)]
    [InlineData(null, 0)]
    [InlineData("baseline", -20)]
    [InlineData("middle", -36)]
    [InlineData("bottom", -72)]
    public void VerticalOffset_UsesBlockOfLinesTimesPitch(string? align, double expected)
    {
        // 3 lines × 1.2 × 20 = 72 px block; the default keeps the first baseline at y = fontSize.
        Assert.Equal(expected, TextLayout.VerticalOffset(align, 3, 20, 1.2), 9);
    }

    [Fact]
    public void ResolveLineHeight_NodeThenTokenThenDefault()
    {
        Assert.Equal(1.5, TextLayout.ResolveLineHeight(1.5, 2.0));
        Assert.Equal(2.0, TextLayout.ResolveLineHeight(null, 2.0));
        Assert.Equal(TextLayout.DefaultLineHeight, TextLayout.ResolveLineHeight(null, null));
        // A zero token line height (allowed by token validation) falls through to the default.
        Assert.Equal(TextLayout.DefaultLineHeight, TextLayout.ResolveLineHeight(null, 0));
        Assert.Equal(2.0, TextLayout.ResolveLineHeight(0, 2.0));
    }

    [Fact]
    public void Resolve_TokenFontPropertiesWinButNodeLineHeightWins()
    {
        var text = new TextNode
        {
            Text = "x",
            FontFamily = "Node",
            FontSize = 10,
            FontWeight = 400,
            LineHeight = 1.4,
            LetterSpacing = 2,
            TextAlign = "center",
            VerticalAlign = "bottom",
            MaxWidth = 120
        };
        var token = new TypographyToken { FontFamily = "Token", FontSize = 20, FontWeight = 700, LineHeight = 1.8 };

        var resolved = TextLayout.Resolve(text, token);

        Assert.Equal("Token", resolved.FontFamily);
        Assert.Equal(20, resolved.FontSize);
        Assert.Equal(700, resolved.FontWeight);
        Assert.Equal(1.4, resolved.LineHeight);
        Assert.Equal(2, resolved.LetterSpacing);
        Assert.Equal("center", resolved.TextAlign);
        Assert.Equal("bottom", resolved.VerticalAlign);
        Assert.Equal(120, resolved.MaxWidth);
    }

    [Fact]
    public void Resolve_InvalidValues_FallBackToDefaults()
    {
        var text = new TextNode
        {
            Text = "x",
            TextAlign = "diagonal",
            VerticalAlign = "sideways",
            MaxWidth = -5,
            LineHeight = double.NaN,
            LetterSpacing = double.PositiveInfinity
        };

        var resolved = TextLayout.Resolve(text, null);

        Assert.Equal(TextLayout.AlignStart, resolved.TextAlign);
        Assert.Equal(TextLayout.VerticalTop, resolved.VerticalAlign);
        Assert.Null(resolved.MaxWidth);
        Assert.Equal(TextLayout.DefaultLineHeight, resolved.LineHeight);
        Assert.Equal(0, resolved.LetterSpacing);
    }

    [Fact]
    public void SvgTextAnchor_MapsBothWays()
    {
        Assert.Null(TextLayout.ToSvgTextAnchor("start"));
        Assert.Equal("middle", TextLayout.ToSvgTextAnchor("center"));
        Assert.Equal("end", TextLayout.ToSvgTextAnchor("end"));
        Assert.Equal("center", TextLayout.FromSvgTextAnchor(" Middle "));
        Assert.Equal("end", TextLayout.FromSvgTextAnchor("end"));
        Assert.Null(TextLayout.FromSvgTextAnchor("start"));
        Assert.Null(TextLayout.FromSvgTextAnchor("inherit"));
    }

    [Fact]
    public void EstimatedMeasurer_IncludesLetterSpacingPerGlyph()
    {
        var plain = EstimatedTextMeasurer.Instance.Measure(new TextMeasurementRequest { Text = "abcd", FontSize = 10 });
        var spaced = EstimatedTextMeasurer.Instance.Measure(new TextMeasurementRequest { Text = "abcd", FontSize = 10, LetterSpacing = 2 });

        Assert.Equal(24, plain.Width);
        Assert.Equal(32, spaced.Width);
    }
}
