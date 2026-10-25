using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

public sealed class TransientDocumentBuilderTests
{
    [Fact]
    public void MasksRestoreAuthoredStateAndBackgroundWithoutRecopying()
    {
        var selected = new RectangleNode { Width = 12, Height = 9, Order = "a0" };
        var hidden = new RectangleNode { Width = 30, Height = 40, Visible = false, Order = "a1" };
        var sibling = new RectangleNode { Width = 50, Height = 60, Order = "a1" };
        var parent = new FrameNode
        {
            Width = 20,
            Height = 20,
            ClipContent = true,
            Order = "a0",
            Transform = new Transform { X = 7, Rotation = 15 },
            Style = new Style { Opacity = 0.5 },
            Children = [selected, hidden]
        };
        var source = Scene(parent, sibling);
        source.Canvas.Background = "#123456";
        new DocumentEngine().SetColorToken(source, "ink", "#abcdef");
        var before = DocumentSerializer.Serialize(source);
        var copies = 0;
        var builder = new TransientDocumentBuilder((_, _, _, _) => new(null, []), document =>
        {
            Assert.True(Monitor.IsEntered(DocumentSynchronization.For(document)));
            copies++;
            return DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))!;
        });
        var transient = builder.Build(source);
        var authored = DocumentSerializer.Serialize(transient.Document);
        var index = DocumentIndex.Build(transient.Document);

        transient.ApplyMask([selected.Id]);
        Assert.True(index.Find(parent.Id)!.Visible);
        Assert.True(index.Find(selected.Id)!.Visible);
        Assert.False(index.Find(hidden.Id)!.Visible);
        Assert.False(index.Find(sibling.Id)!.Visible);
        transient.ApplyMask([parent.Id]);
        Assert.False(index.Find(hidden.Id)!.Visible);
        transient.ApplyMask([sibling.Id]);
        Assert.False(index.Find(parent.Id)!.Visible);
        Assert.True(index.Find(sibling.Id)!.Visible);
        transient.ApplyMask([]);
        Assert.False(transient.Document.Root.Visible);
        transient.SetBackground(TransientBackground.None);
        Assert.Null(transient.Document.Canvas.Background);
        transient.ApplyMask(null);
        transient.SetBackground(TransientBackground.Source);
        Assert.Equal(authored, DocumentSerializer.Serialize(transient.Document));
        Assert.Equal(before, DocumentSerializer.Serialize(source));
        Assert.Equal(1, copies);
        Assert.NotSame(source.Tokens, transient.Document.Tokens);
    }

    [Fact]
    public void HiddenAncestorIsNeverMadeVisibleAndInvalidMasksAreAtomic()
    {
        var leaf = new RectangleNode { Width = 10, Height = 10 };
        var parent = new GroupNode { Visible = false, Children = [leaf] };
        var transient = Builder().Build(Scene(parent));
        transient.ApplyMask([leaf.Id]);
        Assert.False(DocumentIndex.Build(transient.Document).Find(parent.Id)!.Visible);
        var before = DocumentSerializer.Serialize(transient.Document);
        Assert.Throws<DocumentEngineException>(() => transient.ApplyMask([Guid.NewGuid()]));
        Assert.Throws<OperationCanceledException>(() => transient.ApplyMask(null, new CancellationToken(true)));
        Assert.Equal(before, DocumentSerializer.Serialize(transient.Document));
    }

    [Fact]
    public void CancellationAndFailedMeasurementLeaveSourceUnchanged()
    {
        var source = Scene(new RectangleNode { Width = 10, Height = 20 });
        var before = DocumentSerializer.Serialize(source);
        var copies = 0;
        var builder = new TransientDocumentBuilder((_, _, _, _) => throw new InvalidOperationException("probe"), doc =>
        {
            copies++;
            return DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc))!;
        });
        Assert.Throws<OperationCanceledException>(() => builder.Build(source, cancellationToken: new CancellationToken(true)));
        Assert.Equal(0, copies);
        Assert.Throws<InvalidOperationException>(() => builder.Build(source));
        Assert.Equal(1, copies);
        Assert.Equal(before, DocumentSerializer.Serialize(source));
    }

    [Fact]
    public void CancellationAfterSnapshotAndOversizedMasksAreBounded()
    {
        var source = Scene(new RectangleNode { Width = 10, Height = 20 });
        var before = DocumentSerializer.Serialize(source);
        using var cancellation = new CancellationTokenSource();
        var builder = new TransientDocumentBuilder((_, _, _, _) => throw new Xunit.Sdk.XunitException("must not measure"), doc =>
        {
            var copy = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc))!;
            cancellation.Cancel();
            return copy;
        });
        Assert.Throws<OperationCanceledException>(() => builder.Build(source, cancellationToken: cancellation.Token));
        var transient = Builder().Build(source);
        Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<DocumentEngineException>(() =>
            transient.ApplyMask(Enumerable.Repeat(source.Root.Id, 10_001).ToArray())).Code);
        Assert.Equal(before, DocumentSerializer.Serialize(source));
        Assert.Equal(before, DocumentSerializer.Serialize(transient.Document));
    }

    [Fact]
    public void InkAndCanvasSelectTheSameIsolatedNodeWithDifferentViewports()
    {
        var node = new RectangleNode { Width = 10, Height = 20, Transform = new Transform { X = 7, Y = -4 } };
        var source = Scene(node);
        source.Canvas.Background = "#ffffff";
        var builder = new TransientDocumentBuilder((probe, id, policy, _) =>
        {
            Assert.False(policy.StrictFonts);
            Assert.NotSame(source.Root.Children[0], probe.Root.Children[0]);
            return new(new BoundingBox(7, -4, 10, 20), []);
        });
        var ink = builder.Build(source, node.Id, TransientCrop.Ink, new FontPolicy { StrictFonts = true });
        Assert.Equal(new BoundingBox(7, -4, 10, 20), ink.CropBounds);
        Assert.Equal(10, ink.Document.Canvas.Width);
        Assert.Null(ink.Document.Canvas.Background);
        var wrapper = Assert.IsType<GroupNode>(ink.Document.Root.Children.Single());
        Assert.Equal((-7d, 4d), (wrapper.Transform!.X, wrapper.Transform.Y));
        var canvas = builder.Build(source, node.Id);
        Assert.Equal(source.Canvas.Width, canvas.Document.Canvas.Width);
        canvas.SetBackground(TransientBackground.Source);
        Assert.Equal("#ffffff", canvas.Document.Canvas.Background);
        var missing = Assert.Throws<DocumentEngineException>(() => builder.Build(source, Guid.NewGuid()));
        Assert.Equal(EngineErrors.NodeNotFound, missing.Code);
        var empty = Assert.Throws<DocumentEngineException>(() => Builder().Build(source, node.Id, TransientCrop.Ink));
        Assert.Equal(EngineErrors.NodeNoBounds, empty.Code);
    }

    [Fact]
    public void RenderCropUsesNonStrictCallerPolicyAndPreservesActualRenderOptions()
    {
        var text = new TextNode { Text = "Hi" };
        var document = Scene(text);
        var renderer = new RecordingRenderer();
        FontPolicy? seen = null;
        var measurer = new Measuring();
        var engine = new DocumentEngine(renderer: renderer, measurerFactory: policy => { seen = policy; return measurer; });
        var options = new RenderOptions { FontDirectories = ["fonts"], SystemFonts = false, StrictFonts = true };
        var result = engine.RenderNode(document, text.Id, options, CancellationToken.None);
        Assert.Equal(77, result.Width);
        Assert.Equal(options.FontDirectories, seen!.FontDirectories);
        Assert.False(seen.SystemFonts);
        Assert.False(seen.StrictFonts);
        Assert.True(measurer.Disposed);
        Assert.Same(options, renderer.Options);
        Assert.Contains(result.Diagnostics, d => d.Code == RenderDiagnosticCodes.BoundsDiagnostic && d.Message == "test measure");
        Assert.Contains(result.Diagnostics, d => d.Code == "RENDER_NOTE");
    }

    [Fact]
    public async Task DiagnosticsAreVisibleOrderedDeduplicatedAndBounded()
    {
        var children = Enumerable.Range(0, 70).Select(i => (Node)new TextNode
        {
            Text = "x",
            TypographyTokenRef = "{missing" + i + "}",
            Order = i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
        }).ToList();
        var frame = new FrameNode { Width = 100, Height = 100, ClipContent = true, Children = children };
        var document = Scene(frame);
        var engine = new DocumentEngine();
        var first = await engine.ExportNodeAsync(document, frame.Id, new ExportOptions());
        var second = await engine.ExportNodeAsync(document, frame.Id, new ExportOptions());
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(65, first.Diagnostics.Count);
        Assert.Equal(RenderDiagnosticCodes.DiagnosticsTruncated, first.Diagnostics[^1].Code);
        Assert.Equal(64, first.Diagnostics.Take(64).Select(d => d.Message).Distinct().Count());
        Assert.Contains("missing0", first.Diagnostics[0].Message);
        Assert.Contains(first.Diagnostics, d => d.Message == EstimatedTextMeasurer.NoMeasurerDiagnostic);
        frame.Visible = false;
        var hidden = await engine.ExportNodeAsync(document, frame.Id, new ExportOptions());
        Assert.Empty(hidden.Diagnostics);
    }

    private static TransientDocumentBuilder Builder() => new((_, _, _, _) => new(null, []));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedVisibleTextRefusesBeforeMeasurementAndPreservesSource(bool aggregate)
    {
        var children = Enumerable.Range(0, aggregate ? 16 : 1).Select(i => (Node)new TextNode
        {
            Text = new string('x', aggregate ? 64_000 : 1_000_000),
            MaxWidth = 1_000_000_000,
            Order = i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
        }).ToList();
        var frame = new FrameNode { Width = 10, Height = 10, ClipContent = true, Children = children };
        var source = Scene(frame);
        var before = DocumentSerializer.Serialize(source);
        var engine = new DocumentEngine(renderer: new RecordingRenderer(), measurerFactory: _ =>
            throw new Xunit.Sdk.XunitException("must refuse before acquiring a measurer"));
        var svg = await Assert.ThrowsAsync<DocumentEngineException>(() => engine.ExportNodeAsync(source, frame.Id, new ExportOptions()));
        Assert.Equal(EngineErrors.ResourceLimit, svg.Code);
        var raster = Assert.Throws<DocumentEngineException>(() => engine.RenderNode(source, frame.Id, new RenderOptions(), CancellationToken.None));
        Assert.Equal(EngineErrors.ResourceLimit, raster.Code);
        Assert.Equal(before, DocumentSerializer.Serialize(source));
        // A hidden ancestor must not turn skipped text into a new render failure.
        frame.Children = [new GroupNode { Visible = false, Children = children }];
        var hidden = await new DocumentEngine().ExportNodeAsync(source, frame.Id, new ExportOptions());
        Assert.Empty(hidden.Diagnostics);
    }

    [Fact]
    public void WrappingWorkIsBoundedEvenWithinTheTextLengthLimit()
    {
        var text = new TextNode { Text = string.Concat(Enumerable.Repeat("a ", 32_000)), MaxWidth = 1_000_000_000 };
        var frame = new FrameNode { Width = 10, Height = 10, ClipContent = true, Children = [text] };
        var source = Scene(frame);
        var before = DocumentSerializer.Serialize(source);
        var measurer = new Measuring();
        var engine = new DocumentEngine(renderer: new RecordingRenderer(), measurerFactory: _ => measurer);
        var error = Assert.Throws<DocumentEngineException>(() => engine.RenderNode(source, frame.Id, new RenderOptions(), CancellationToken.None));
        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.True(measurer.Disposed);
        Assert.Equal(before, DocumentSerializer.Serialize(source));
    }
    private static Document Scene(params Node[] children) => new()
    {
        Id = Guid.NewGuid(),
        Canvas = new Canvas { Width = 200, Height = 150 },
        Root = new FrameNode { Width = 200, Height = 150, Children = [.. children] }
    };

    private sealed class Measuring : ITextMeasurer, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public TextMeasurement Measure(TextMeasurementRequest request, CancellationToken cancellationToken = default) =>
            new() { Width = 77, Ascent = 10, Descent = 2, LineHeight = 12, Measured = true, Diagnostic = "test measure" };
    }

    private sealed class RecordingRenderer : IRenderer
    {
        public RenderOptions? Options { get; private set; }
        public RenderResult Render(Document document, RenderOptions options)
        {
            Options = options;
            return new() { Data = [], MimeType = "image/png", Width = (int)document.Canvas.Width, Diagnostics = [new("RENDER_NOTE", "rendered")] };
        }
    }
}
