using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.TestFixtures;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// Feature 002 T008: bounds, layout, lint, and contact sheets must resolve fonts through the
/// same <see cref="FontPolicy"/> a render would use, so measured/wrapped text matches rendered
/// text. These tests prove the policy reaches the measurer for each surface — deterministically
/// with recording measurers, and end to end with the real shaping path.
/// </summary>
public class FontPolicyUnificationTests
{
    private static Document TextDocument(Guid textId, string? fontFamily, string text = "Hello world", double canvasWidth = 120, double canvasHeight = 60) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "font-policy",
            Canvas = new Canvas { Width = canvasWidth, Height = canvasHeight, Background = "#ffffff" },
            Root = new FrameNode
            {
                Width = canvasWidth,
                Height = canvasHeight,
                Children =
                [
                    new TextNode
                    {
                        Id = textId,
                        Text = text,
                        FontFamily = fontFamily,
                        FontSize = 24,
                        Order = "a0",
                        Transform = new Transform { X = 10, Y = 10 },
                        Style = new Style { Fill = new Fill { Color = "#000000" } }
                    }
                ]
            }
        };

    private static double TextWidth(IReadOnlyList<NodeBounds> bounds, Guid textId) =>
        bounds.Single(b => b.Id == textId).Local!.Value.Width;

    [Fact]
    public void GetBounds_NonDefaultPolicy_UsesFactoryMeasurerAndDisposesIt()
    {
        var textId = Guid.NewGuid();
        var document = TextDocument(textId, fontFamily: null);

        var factoryCalls = 0;
        FontPolicy? seenPolicy = null;
        var rentals = new List<RecordingMeasurer>();
        using var defaultMeasurer = new RecordingMeasurer(8);
        var engine = new DocumentEngine(
            textMeasurer: defaultMeasurer,
            measurerFactory: policy =>
            {
                factoryCalls++;
                seenPolicy = policy;
                var measurer = new RecordingMeasurer(800);
                rentals.Add(measurer);
                return measurer;
            });

        var requested = new FontPolicy { FontDirectories = ["/fonts"], SystemFonts = false, StrictFonts = true };
        var width = TextWidth(engine.GetBounds(document, null, requested, CancellationToken.None), textId);

        Assert.Equal(1, factoryCalls);
        Assert.Same(requested, seenPolicy);
        Assert.True(width > 400, $"expected policy-bound measurer (800) to drive the width; got {width}");
        Assert.Single(rentals);
        Assert.True(rentals[0].Disposed, "the per-call policy-bound measurer must be disposed after the call");
        Assert.False(defaultMeasurer.Disposed, "the shared default measurer is owned by the caller and must not be disposed");
    }

    [Fact]
    public void GetBounds_NullPolicy_UsesSharedDefaultMeasurerAndSkipsFactory()
    {
        var textId = Guid.NewGuid();
        var document = TextDocument(textId, fontFamily: null);

        var factoryCalls = 0;
        using var defaultMeasurer = new RecordingMeasurer(8);
        var engine = new DocumentEngine(
            textMeasurer: defaultMeasurer,
            measurerFactory: _ => { factoryCalls++; return new RecordingMeasurer(800); });

        var width = TextWidth(engine.GetBounds(document, null, null, CancellationToken.None), textId);

        Assert.Equal(0, factoryCalls);
        Assert.True(width < 100, $"expected the shared default measurer (8) to drive the width; got {width}");
    }

    [Fact]
    public void Lint_MeasuresUnderRequestedPolicy_ChangingOutsideCanvasFinding()
    {
        var textId = Guid.NewGuid();
        var document = TextDocument(textId, fontFamily: null);

        using var defaultMeasurer = new RecordingMeasurer(8);
        var engine = new DocumentEngine(
            textMeasurer: defaultMeasurer,
            measurerFactory: _ => new RecordingMeasurer(5000));

        var options = LintOptions.Default;
        var withDefault = engine.Lint(document, options, null, CancellationToken.None);
        var withPolicy = engine.Lint(document, options, new FontPolicy { SystemFonts = false }, CancellationToken.None);

        // Default measurement (width 8) keeps the text inside the 120px canvas; the policy-bound
        // measurement (width 5000) pushes it past the edge, which only LINT004 can see if lint
        // actually measured under the supplied policy.
        Assert.DoesNotContain(withDefault.Diagnostics, d => d.Code == LintCodes.OutsideCanvas && d.NodeId == textId);
        Assert.Contains(withPolicy.Diagnostics, d => d.Code == LintCodes.OutsideCanvas && d.NodeId == textId);
    }

    [Fact]
    public void RenderContactSheet_StrictFonts_FailsWhenATileFamilyCannotResolve()
    {
        var document = TextDocument(Guid.NewGuid(), fontFamily: "No Such Family 9Z");
        var engine = new DocumentEngine(contactSheetRenderer: new SkiaContactSheetRenderer());
        var entries = new[] { new ContactSheetEntry(document, document.Name) };

        // Default policy substitutes the bundled family, so the sheet renders.
        var ok = engine.RenderContactSheet(entries, new ContactSheetOptions { CellSize = 64 }, CancellationToken.None);
        Assert.NotEmpty(ok.Data);

        // Strict fonts with no host fonts must propagate the unresolved family as a hard failure,
        // proving the tile render honours the contact-sheet font policy.
        var strict = new ContactSheetOptions { CellSize = 64, SystemFonts = false, StrictFonts = true };
        var error = Assert.Throws<DocumentEngineException>(() =>
            engine.RenderContactSheet(entries, strict, CancellationToken.None));
        Assert.Equal(EngineErrors.FontNotFound, error.Code);
    }

    [Fact]
    public void GetBounds_FontDirectoryPolicy_ChangesRealMeasuredWidth()
    {
        var fontDirectory = PortableTestFonts.VerifiedDirectory();

        var textId = Guid.NewGuid();
        var document = TextDocument(textId, fontFamily: PortableTestFonts.SansFamily, text: PortableTestFonts.WidthProbe);
        var engine = new DocumentEngine(measurerFactory: policy => new SkiaTextMeasurer(policy));

        // Both measurements disable host fonts so the difference is the font directory alone:
        // without it "Noto Sans" substitutes the bundled Inter; with it the real face is used.
        var substituted = engine.GetBounds(document, textId,
            new FontPolicy { SystemFonts = false }, CancellationToken.None);
        var resolved = engine.GetBounds(document, textId,
            new FontPolicy { FontDirectories = [fontDirectory], SystemFonts = false }, CancellationToken.None);

        var substitutedBounds = substituted.Single(b => b.Id == textId);
        var resolvedBounds = resolved.Single(b => b.Id == textId);
        Assert.True(substitutedBounds.Measured);
        Assert.True(resolvedBounds.Measured);
        Assert.True(
            Math.Abs(resolvedBounds.Local!.Value.Width - substitutedBounds.Local!.Value.Width) > 1.0,
            $"Noto Sans and the bundled substitute should measure differently; got {resolvedBounds.Local!.Value.Width} vs {substitutedBounds.Local!.Value.Width}");
    }

    /// <summary>A text measurer that reports a fixed line width and records whether it was disposed.</summary>
    private sealed class RecordingMeasurer(double width) : ITextMeasurer, IDisposable
    {
        public bool Disposed { get; private set; }

        public TextMeasurement Measure(TextMeasurementRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return new TextMeasurement
            {
                Width = width,
                Ascent = request.FontSize,
                Descent = request.FontSize * 0.2,
                LineHeight = request.FontSize * 1.2,
                Measured = true
            };
        }

        public void Dispose() => Disposed = true;
    }
}
