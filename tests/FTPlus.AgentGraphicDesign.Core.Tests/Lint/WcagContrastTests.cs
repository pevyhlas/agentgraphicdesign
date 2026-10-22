using FTPlus.AgentGraphicDesign.Core.Lint;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Lint;

/// <summary>
/// WCAG 2.x relative luminance, contrast ratio, sRGB parsing, and source-over
/// compositing used by the LINT001 text-contrast rule.
/// </summary>
public class WcagContrastTests
{
    [Fact]
    public void RelativeLuminance_WhiteIsOne_BlackIsZero()
    {
        Assert.Equal(1.0, WcagContrast.RelativeLuminance(1, 1, 1), 6);
        Assert.Equal(0.0, WcagContrast.RelativeLuminance(0, 0, 0), 6);
    }

    [Fact]
    public void Ratio_BlackOnWhite_Is21()
    {
        Assert.Equal(21.0, WcagContrast.Ratio(SrgbColor.White, SrgbColor.Black), 6);
        Assert.Equal(21.0, WcagContrast.Ratio(SrgbColor.Black, SrgbColor.White), 6);
    }

    [Fact]
    public void Ratio_SameColor_IsOne()
    {
        Assert.Equal(1.0, WcagContrast.Ratio(SrgbColor.White, SrgbColor.White), 6);
        Assert.Equal(1.0, WcagContrast.Ratio(SrgbColor.Black, SrgbColor.Black), 6);
    }

    [Fact]
    public void Ratio_IsSymmetricInLuminanceOrder()
    {
        var a = WcagContrast.Ratio(0.4, 0.02);
        var b = WcagContrast.Ratio(0.02, 0.4);
        Assert.Equal(a, b, 9);
    }

    [Theory]
    [InlineData("#ffffff", 1.0, 1.0, 1.0, 1.0)]
    [InlineData("#000", 0.0, 0.0, 0.0, 1.0)]
    [InlineData("#ff0000", 1.0, 0.0, 0.0, 1.0)]
    [InlineData("rgb(0, 128, 255)", 0.0, 0.501960, 1.0, 1.0)]
    [InlineData("#00000080", 0.0, 0.0, 0.0, 0.501960)]
    public void TryParse_ReadsDocumentColorGrammar(string value, double r, double g, double b, double a)
    {
        Assert.True(SrgbColor.TryParse(value, out var color));
        Assert.Equal(r, color.R, 5);
        Assert.Equal(g, color.G, 5);
        Assert.Equal(b, color.B, 5);
        Assert.Equal(a, color.A, 5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-color")]
    [InlineData("#12345")]
    public void TryParse_RejectsInvalidColor(string? value)
    {
        Assert.False(SrgbColor.TryParse(value, out _));
    }

    [Fact]
    public void TryRatio_ParsesBothColors()
    {
        Assert.True(WcagContrast.TryRatio("#000000", "#ffffff", out var ratio));
        Assert.Equal(21.0, ratio, 6);
    }

    [Fact]
    public void TryRatio_FailsWhenEitherColorIsInvalid()
    {
        Assert.False(WcagContrast.TryRatio("nope", "#ffffff", out _));
        Assert.False(WcagContrast.TryRatio("#ffffff", "nope", out _));
    }

    [Fact]
    public void Over_HalfBlackOnWhite_IsMidGrey()
    {
        var composited = SrgbColor.Black.WithAlpha(0.5).Over(SrgbColor.White);
        Assert.Equal(0.5, composited.R, 6);
        Assert.Equal(0.5, composited.G, 6);
        Assert.Equal(0.5, composited.B, 6);
        Assert.True(composited.IsOpaque);
    }

    [Fact]
    public void Ratio_FlattensTranslucentForegroundOverBackground()
    {
        // A 50%-black ink over white is identical to an opaque mid-grey over white.
        var translucent = WcagContrast.Ratio(SrgbColor.Black.WithAlpha(0.5), SrgbColor.White);
        var opaqueEquivalent = WcagContrast.Ratio(new SrgbColor(0.5, 0.5, 0.5, 1), SrgbColor.White);
        Assert.Equal(opaqueEquivalent, translucent, 9);
        Assert.InRange(translucent, 1.0, 21.0);
    }
}
