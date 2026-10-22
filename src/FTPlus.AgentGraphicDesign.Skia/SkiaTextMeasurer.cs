using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;

namespace FTPlus.AgentGraphicDesign.Skia;

/// <summary>
/// Shaping-backed <see cref="ITextMeasurer"/>. It resolves fonts and shapes text
/// through the same <see cref="TextShapingService"/> path as <see cref="SkiaRenderer"/>,
/// so the reported width is the rendered advance and the ascent/descent come from the
/// typeface that actually draws, and letter spacing widens it exactly as it widens the
/// rendered line. Shaping failures fall back to the glyph-count estimate with
/// <c>Measured = false</c> instead of throwing.
/// </summary>
/// <remarks>
/// The parameterless measurer uses the default font policy. Construct one with a
/// <see cref="FontPolicy"/> to measure under the same fonts a policy-bound render would use,
/// so bounds/layout/lint match the rendered text under custom font directories or strict fonts.
/// </remarks>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly TextShapingService _shaping;

    /// <summary>Measures with the default font policy (bundled family, host fonts, non-strict).</summary>
    public SkiaTextMeasurer() => _shaping = new TextShapingService();

    /// <summary>Measures with the fonts resolved by <paramref name="policy"/>.</summary>
    public SkiaTextMeasurer(FontPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _shaping = new TextShapingService(FontResolutionOptions.From(policy));
    }

    public TextMeasurement Measure(TextMeasurementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new TextShapingOptions
        {
            FontFamily = request.FontFamily,
            FontSize = request.FontSize,
            FontWeight = request.FontWeight,
            LetterSpacing = request.LetterSpacing
        };

        try
        {
            var metrics = _shaping.GetFontMetrics(options);
            var shaped = _shaping.ShapeText(request.Text, options, cancellationToken);
            var ascent = -metrics.Ascent;
            var descent = metrics.Descent;
            return new TextMeasurement
            {
                Width = shaped.TotalWidth,
                Ascent = ascent,
                Descent = descent,
                LineHeight = ascent + descent,
                Measured = true,
                Diagnostic = shaped.FallbackDiagnostic
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return EstimatedTextMeasurer.Estimate(request,
                $"Text measurement failed ({ex.Message}); extents are estimated");
        }
    }

    public void Dispose() => _shaping.Dispose();
}
