using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// Raster sub-node export (D-10): <see cref="IDocumentEngine.RenderNode"/> sizes the raster
/// surface to the selected subtree's own bounds (times scale), not the canvas.
/// </summary>
public class SubNodeRenderTests
{
    [Fact]
    public void RenderNode_SizesRasterToSubtreeBounds_TimesScale()
    {
        var target = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "target",
            Transform = new Transform { X = 10, Y = 10 },
            Width = 30,
            Height = 30,
            Style = new Style { Fill = new Fill { Color = "#3366ff" } }
        };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "sub-node-raster",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 200, Height = 200 },
            Root = new FrameNode { Id = Guid.NewGuid(), Name = "root", Width = 200, Height = 200, Children = [target] }
        };
        var engine = new DocumentEngine(renderer: new SkiaRenderer());

        var result = engine.RenderNode(document, target.Id,
            new RenderOptions { Format = RenderFormat.Png, Scale = 2.0 }, CancellationToken.None);

        // 30x30 subtree at scale 2 → 60x60 pixels, independent of the 200x200 canvas.
        Assert.Equal(60, result.Width);
        Assert.Equal(60, result.Height);
        Assert.Equal("image/png", result.MimeType);
    }

    [Fact]
    public void RenderNode_NoVisibleBounds_FailsWithNodeNoBounds()
    {
        var empty = new GroupNode { Id = Guid.NewGuid(), Name = "empty", Children = [] };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "sub-node-raster",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 100, Height = 100 },
            Root = new FrameNode { Id = Guid.NewGuid(), Name = "root", Width = 100, Height = 100, Children = [empty] }
        };
        var engine = new DocumentEngine(renderer: new SkiaRenderer());

        var ex = Assert.Throws<DocumentEngineException>(
            () => engine.RenderNode(document, empty.Id,
                new RenderOptions { Format = RenderFormat.Png }, CancellationToken.None));

        Assert.Equal(EngineErrors.NodeNoBounds, ex.Code);
    }
}
