using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Mcp;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Skia;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// Feature 002 T010 (D-10): the export_document tool's <c>nodeId</c> parameter exports the
/// subtree rooted at one node, cropped to its own bounds and drawn in isolation.
/// </summary>
public class ExportSubNodeToolTests : IDisposable
{
    [Theory]
    [InlineData("svg")]
    [InlineData("png")]
    public async Task ExportNodeReportsBoundsDiagnostics(string format)
    {
        var text = new TextNode { Text = "Hello", TypographyTokenRef = "{missing}" };
        var document = _engine.CreateDocument("diagnostic");
        document.Root.Children = [text];
        var path = Path.Combine(_testDir, "diagnostic.agd.json");
        // Deliberately unresolved token is accepted for best-effort export.
        File.WriteAllText(path, Core.Serialization.DocumentSerializer.Serialize(document));
        var before = File.ReadAllBytes(path);
        var result = await _tools.ExportDocument("diagnostic.agd.json", "diagnostic." + format,
            format: format, nodeId: text.Id.ToString());
        Assert.NotEqual(true, result.IsError);
        var payload = Assert.IsType<TextContentBlock>(result.Content.Single()).Text;
        using var json = JsonDocument.Parse(payload);
        Assert.Contains(json.RootElement.GetProperty("data").GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "BOUNDS_DIAGNOSTIC" && d.GetProperty("message").GetString()!.Contains("missing"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private readonly string _testDir;
    private readonly DocumentEngine _engine;
    private readonly DocumentTools _tools;

    public ExportSubNodeToolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-mcp-subnode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var store = new FileDocumentStore(_testDir);
        _engine = new DocumentEngine(store, renderer: new SkiaRenderer());
        _tools = new DocumentTools(_engine, new PathConfinement(_testDir), new SkiaRenderer());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
        GC.SuppressFinalize(this);
    }

    private (string RelPath, Guid NodeId) WriteDocumentWithRectangle()
    {
        var nodeId = Guid.NewGuid();
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "sub-node",
            Canvas = new Canvas { Width = 800, Height = 600 },
            Root = new FrameNode
            {
                Width = 800,
                Height = 600,
                Children =
                [
                    new RectangleNode
                    {
                        Id = nodeId,
                        Transform = new Transform { X = 40, Y = 40 },
                        Width = 30,
                        Height = 30,
                        Style = new Style { Fill = new Fill { Color = "#3366ff" } }
                    }
                ]
            }
        };
        _engine.SaveDocumentToFile(document, Path.Combine(_testDir, "doc.agd.json"));
        return ("doc.agd.json", nodeId);
    }

    [Fact]
    public async Task ExportDocument_WithNodeId_CropsSvgToSubtreeBounds()
    {
        var (rel, nodeId) = WriteDocumentWithRectangle();

        var result = await _tools.ExportDocument(rel, "node.svg", format: "svg", nodeId: nodeId.ToString());

        Assert.NotEqual(true, result.IsError);
        var svg = await File.ReadAllTextAsync(Path.Combine(_testDir, "node.svg"));
        Assert.Contains("viewBox=\"0 0 30 30\"", svg);
        Assert.DoesNotContain("viewBox=\"0 0 800 600\"", svg);
    }

    [Fact]
    public async Task ExportDocument_NodeIdWithSymbolId_IsRejected()
    {
        var (rel, nodeId) = WriteDocumentWithRectangle();

        var result = await _tools.ExportDocument(rel, "node.svg", format: "svg",
            symbolId: "icon", nodeId: nodeId.ToString());

        Assert.Equal("INVALID_ARGUMENTS", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportDocument_UnknownNodeId_ReturnsNodeNotFound()
    {
        var (rel, _) = WriteDocumentWithRectangle();

        var result = await _tools.ExportDocument(rel, "node.svg", format: "svg",
            nodeId: Guid.NewGuid().ToString());

        Assert.Equal("NODE_NOT_FOUND", ParseError(result).GetProperty("code").GetString());
    }

    private static JsonElement ParseError(CallToolResult result)
    {
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content.Single(block => block is TextContentBlock)).Text;
        var json = JsonDocument.Parse(text);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean(), $"Expected ok=false but got: {text}");
        return json.RootElement.GetProperty("error");
    }
}
