using System.Text;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Export;

/// <summary>
/// Sub-node export (D-10): exporting the subtree rooted at one node as a standalone image,
/// cropped to the subtree's own bounds and drawn in isolation from ancestor transforms/clipping.
/// </summary>
public class SubNodeExportTests
{
    [Fact]
    public async Task ExportNode_CropsToSubtreeBounds_ExcludingAncestorTransformAndSiblings()
    {
        var target = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "target",
            Order = "a0",
            Transform = new Transform { X = 10, Y = 10 },
            Width = 30,
            Height = 30,
            Style = new Style { Fill = new Fill { Color = "#111111" } }
        };
        var sibling = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "sibling",
            Order = "a1",
            Transform = new Transform { X = 100, Y = 100 },
            Width = 40,
            Height = 40,
            Style = new Style { Fill = new Fill { Color = "#222222" } }
        };
        var document = CreateDocument(200, 200);
        // An ancestor group carries a translate that must NOT appear in the isolated export.
        document.Root.Children =
        [
            new GroupNode
            {
                Id = Guid.NewGuid(),
                Name = "wrapper",
                Transform = new Transform { X = 50, Y = 50 },
                Children = [target, sibling]
            }
        ];
        var engine = new DocumentEngine();

        var result = await engine.ExportNodeAsync(document, target.Id, new ExportOptions { Format = "svg" });

        var svg = Encoding.UTF8.GetString(result.Data);
        // Viewport equals the subtree's own bounds (30x30 at origin), not the canvas.
        Assert.Contains("viewBox=\"0 0 30 30\"", svg);
        // The target is present; the sibling and the ancestor translate are excluded.
        Assert.Contains("#111111", svg);
        Assert.DoesNotContain("#222222", svg);
    }

    [Fact]
    public async Task ExportNode_UnknownId_FailsWithNodeNotFound()
    {
        var document = CreateDocument(100, 100);
        var engine = new DocumentEngine();

        var ex = await Assert.ThrowsAsync<DocumentEngineException>(
            () => engine.ExportNodeAsync(document, Guid.NewGuid(), new ExportOptions { Format = "svg" }));

        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    [Fact]
    public async Task ExportNode_NoVisibleBounds_FailsWithNodeNoBounds()
    {
        var empty = new GroupNode { Id = Guid.NewGuid(), Name = "empty", Children = [] };
        var document = CreateDocument(100, 100);
        document.Root.Children = [empty];
        var engine = new DocumentEngine();

        var ex = await Assert.ThrowsAsync<DocumentEngineException>(
            () => engine.ExportNodeAsync(document, empty.Id, new ExportOptions { Format = "svg" }));

        Assert.Equal(EngineErrors.NodeNoBounds, ex.Code);
    }

    [Fact]
    public async Task ExportNode_ResolvesDocumentTokens()
    {
        var target = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "target",
            Order = "a0",
            Width = 20,
            Height = 20,
            Style = new Style { Fill = new Fill { TokenRef = "{ink}" } }
        };
        var document = CreateDocument(100, 100);
        document.Root.Children = [target];
        var engine = new DocumentEngine();
        engine.SetColorToken(document, "ink", "#13579b");

        var whole = Encoding.UTF8.GetString((await engine.ExportAsync(document, new ExportOptions { Format = "svg" })).Data);
        var node = Encoding.UTF8.GetString((await engine.ExportNodeAsync(document, target.Id, new ExportOptions { Format = "svg" })).Data);

        // The isolated subtree resolves the same token value as the whole document.
        Assert.Contains("#13579b", whole, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#13579b", node, StringComparison.OrdinalIgnoreCase);
    }

    private static Document CreateDocument(double width, double height) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "sub-node",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = width, Height = height },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = width,
                Height = height,
                Children = []
            }
        };
}
