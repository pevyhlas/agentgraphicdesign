using System.Text;

namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// Measures a single line of text with resolved typography (wrapping happens in
/// <see cref="TextLayout"/> on top of it). The Skia project supplies
/// the shaping-backed implementation; the core falls back to
/// <see cref="EstimatedTextMeasurer"/> when no measurer is registered.
/// </summary>
public interface ITextMeasurer
{
    TextMeasurement Measure(TextMeasurementRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Text plus the typography that applies after token resolution.</summary>
public sealed record TextMeasurementRequest
{
    public required string Text { get; init; }

    public string? FontFamily { get; init; }

    public double FontSize { get; init; } = 16;

    public int FontWeight { get; init; } = 400;

    /// <summary>Extra advance after each glyph in pixels; included in the measured width.</summary>
    public double LetterSpacing { get; init; }
}

/// <summary>
/// Measured extents. <see cref="Ascent"/> and <see cref="Descent"/> are positive
/// distances above and below the baseline; the renderer places the baseline at
/// <c>y = fontSize</c> in node-local space.
/// </summary>
public sealed record TextMeasurement
{
    public required double Width { get; init; }

    public required double Ascent { get; init; }

    public required double Descent { get; init; }

    public required double LineHeight { get; init; }

    /// <summary>True when a shaping engine produced the values; false for the estimator.</summary>
    public required bool Measured { get; init; }

    public string? Diagnostic { get; init; }
}

/// <summary>Fallback behaviour shared by every caller of <see cref="ITextMeasurer"/>.</summary>
public static class TextMeasurerExtensions
{
    /// <summary>
    /// Measures <paramref name="request"/>, converting a measurer's argument, state, or
    /// support failure into the glyph-count estimate (flagged <c>Measured = false</c>) so
    /// layout never fails on a font problem. Cancellation still propagates.
    /// </summary>
    public static TextMeasurement MeasureSafely(this ITextMeasurer measurer, TextMeasurementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return measurer.Measure(request, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return EstimatedTextMeasurer.Estimate(request,
                $"Text measurement failed ({ex.Message}); extents are estimated");
        }
    }
}

/// <summary>
/// Glyph-count estimator used only when no <see cref="ITextMeasurer"/> is registered:
/// width = fontSize × 0.6 × glyph count, height = fontSize × 1.2, <c>Measured = false</c>.
/// </summary>
public sealed class EstimatedTextMeasurer : ITextMeasurer
{
    public const double WidthPerGlyph = 0.6;
    public const double LineHeightFactor = 1.2;
    public const string NoMeasurerDiagnostic = "Text extents are estimated; no text measurer is registered";

    public static EstimatedTextMeasurer Instance { get; } = new();

    public TextMeasurement Measure(TextMeasurementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Estimate(request, NoMeasurerDiagnostic);
    }

    public static TextMeasurement Estimate(TextMeasurementRequest request, string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(request);
        var glyphs = 0;
        foreach (var _ in request.Text.EnumerateRunes())
            glyphs++;

        var fontSize = request.FontSize;
        var letterSpacing = double.IsFinite(request.LetterSpacing) ? request.LetterSpacing : 0;
        return new TextMeasurement
        {
            Width = (fontSize * WidthPerGlyph + letterSpacing) * glyphs,
            Ascent = fontSize,
            Descent = fontSize * (LineHeightFactor - 1),
            LineHeight = fontSize * LineHeightFactor,
            Measured = false,
            Diagnostic = diagnostic
        };
    }
}
