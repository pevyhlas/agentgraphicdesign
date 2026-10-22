using System.Globalization;

namespace FTPlus.AgentGraphicDesign.Core.Lint;

/// <summary>
/// An sRGB color with straight (non-premultiplied) alpha, each channel in [0, 1].
/// Parses the document color grammar (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>,
/// <c>#rrggbbaa</c>, <c>rgb()</c>, <c>rgba()</c>).
/// </summary>
public readonly record struct SrgbColor(double R, double G, double B, double A)
{
    public static SrgbColor White { get; } = new(1, 1, 1, 1);

    public static SrgbColor Black { get; } = new(0, 0, 0, 1);

    public bool IsOpaque => A >= 1;

    public bool IsTransparent => A <= 0;

    /// <summary>WCAG 2.x relative luminance of the color, ignoring alpha.</summary>
    public double Luminance => WcagContrast.RelativeLuminance(R, G, B);

    public static bool TryParse(string? value, out SrgbColor color)
    {
        color = default;
        if (!DocumentLimits.IsValidColor(value))
            return false;

        var text = value!.Trim();
        if (text[0] == '#')
        {
            var hex = text[1..];
            return hex.Length switch
            {
                3 => TryHex(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], 'f', 'f', out color),
                4 => TryHex(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], hex[3], hex[3], out color),
                6 => TryHex(hex[0], hex[1], hex[2], hex[3], hex[4], hex[5], 'f', 'f', out color),
                _ => TryHex(hex[0], hex[1], hex[2], hex[3], hex[4], hex[5], hex[6], hex[7], out color)
            };
        }

        var open = text.IndexOf('(');
        var parts = text[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries);
        var r = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
        var g = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
        var b = int.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture);
        var a = parts.Length == 4
            ? double.Parse(parts[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : 1;
        color = new SrgbColor(r / 255.0, g / 255.0, b / 255.0, Math.Clamp(a, 0, 1));
        return true;
    }

    /// <summary>Composites this color over an opaque <paramref name="background"/> (source-over).</summary>
    public SrgbColor Over(SrgbColor background)
    {
        var alpha = Math.Clamp(A, 0, 1);
        var backdropAlpha = Math.Clamp(background.A, 0, 1);
        var outAlpha = alpha + backdropAlpha * (1 - alpha);
        if (outAlpha <= 0)
            return new SrgbColor(0, 0, 0, 0);

        static double Channel(double top, double topAlpha, double bottom, double bottomAlpha, double outAlpha) =>
            (top * topAlpha + bottom * bottomAlpha * (1 - topAlpha)) / outAlpha;

        return new SrgbColor(
            Channel(R, alpha, background.R, backdropAlpha, outAlpha),
            Channel(G, alpha, background.G, backdropAlpha, outAlpha),
            Channel(B, alpha, background.B, backdropAlpha, outAlpha),
            outAlpha);
    }

    public SrgbColor WithAlpha(double alpha) => this with { A = Math.Clamp(alpha, 0, 1) };

    /// <summary><c>#rrggbb</c>, or <c>#rrggbbaa</c> when the color is not opaque.</summary>
    public string ToHex()
    {
        static int Byte(double channel) => (int)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.AwayFromZero);
        var rgb = $"#{Byte(R):x2}{Byte(G):x2}{Byte(B):x2}";
        return IsOpaque ? rgb : $"{rgb}{Byte(A):x2}";
    }

    private static bool TryHex(char r1, char r2, char g1, char g2, char b1, char b2, char a1, char a2, out SrgbColor color)
    {
        color = new SrgbColor(Pair(r1, r2) / 255.0, Pair(g1, g2) / 255.0, Pair(b1, b2) / 255.0, Pair(a1, a2) / 255.0);
        return true;

        static int Pair(char high, char low) =>
            (Convert.ToInt32(high.ToString(), 16) << 4) | Convert.ToInt32(low.ToString(), 16);
    }
}

/// <summary>WCAG 2.x relative luminance and contrast ratio.</summary>
public static class WcagContrast
{
    /// <summary>Relative luminance of sRGB components in [0, 1] (WCAG 2.x definition).</summary>
    public static double RelativeLuminance(double r, double g, double b) =>
        0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);

    /// <summary>Contrast ratio between two luminances, from 1 to 21.</summary>
    public static double Ratio(double luminanceA, double luminanceB)
    {
        var lighter = Math.Max(luminanceA, luminanceB);
        var darker = Math.Min(luminanceA, luminanceB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Contrast ratio of <paramref name="foreground"/> drawn over <paramref name="background"/>.
    /// A translucent background is first flattened over white; a translucent foreground is
    /// composited over the flattened background.
    /// </summary>
    public static double Ratio(SrgbColor foreground, SrgbColor background)
    {
        var backdrop = background.IsOpaque ? background : background.Over(SrgbColor.White);
        var ink = foreground.IsOpaque ? foreground : foreground.Over(backdrop);
        return Ratio(ink.Luminance, backdrop.Luminance);
    }

    /// <summary>Contrast ratio of two document color strings; false when either does not parse.</summary>
    public static bool TryRatio(string? foreground, string? background, out double ratio)
    {
        ratio = 0;
        if (!SrgbColor.TryParse(foreground, out var ink) || !SrgbColor.TryParse(background, out var backdrop))
            return false;
        ratio = Ratio(ink, backdrop);
        return true;
    }

    private static double Linearize(double channel)
    {
        var c = Math.Clamp(channel, 0, 1);
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
