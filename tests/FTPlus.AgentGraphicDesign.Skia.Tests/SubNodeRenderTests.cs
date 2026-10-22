using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using SkiaSharp;
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

    [Fact]
    public void RenderNode_ResolvesColorTokens()
    {
        var target = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "target",
            Width = 10,
            Height = 10,
            Style = new Style { Fill = new Fill { TokenRef = "{ink}" } }
        };
        var document = Scene(target);
        var engine = new DocumentEngine(renderer: new SkiaRenderer());
        engine.SetColorToken(document, "ink", "#13579b");

        var result = engine.RenderNode(document, target.Id,
            new RenderOptions { Format = RenderFormat.Png }, CancellationToken.None);

        using var bitmap = SKBitmap.Decode(result.Data);
        Assert.Equal(new SKColor(0x13, 0x57, 0x9b), bitmap.GetPixel(5, 5));
    }

    [Fact]
    public void RenderNode_TypographyTokenSizesTheCrop()
    {
        var text = new TextNode { Id = Guid.NewGuid(), Name = "label", Text = "Hi", TypographyTokenRef = "{display}" };
        var document = Scene(text);
        var engine = new DocumentEngine(renderer: new SkiaRenderer(), textMeasurer: new SkiaTextMeasurer());
        engine.SetTypographyToken(document, "display", new TypographyToken { FontSize = 96 });

        var result = engine.RenderNode(document, text.Id,
            new RenderOptions { Format = RenderFormat.Png }, CancellationToken.None);

        // The node's own 16px default would give a crop roughly 20px tall; the token's 96px wins.
        Assert.True(result.Height >= 80, $"crop height {result.Height} ignores the typography token");
    }

    private static Document Scene(Node node) => new()
    {
        Id = Guid.NewGuid(),
        Name = "sub-node-tokens",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = 400, Height = 400 },
        Root = new FrameNode { Id = Guid.NewGuid(), Name = "root", Width = 400, Height = 400, Children = [node] }
    };
}
