using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Skia;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// MCP plumbing for get_bounds, align_nodes, distribute_nodes, and fit_frame:
/// argument parsing, JSON shapes, persistence, revision checks, and path confinement.
/// </summary>
public class LayoutToolsTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileDocumentStore _store;
    private readonly DocumentTools _tools;

    public LayoutToolsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-mcp-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new FileDocumentStore(_testDir);
        _tools = new DocumentTools(new DocumentEngine(_store, exportAdapters: null, new SkiaRenderer(), new SkiaTextMeasurer()), new PathConfinement(_testDir), new SkiaRenderer());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void GetBounds_AllNodes_ReturnsLocalAndWorldBoxes()
    {
        var (path, doc, a, _) = CreateTwoRectangles();

        var data = ParseData(_tools.GetBounds(path));

        Assert.Equal(doc.Id.ToString(), data.GetProperty("documentId").GetString());
        Assert.Equal(3, data.GetProperty("nodeCount").GetInt32());
        var nodes = data.GetProperty("nodes").EnumerateArray().ToList();
        var rect = nodes.Single(n => n.GetProperty("id").GetString() == a.Id.ToString());
        Assert.Equal("rectangle", rect.GetProperty("type").GetString());
        Assert.Equal(0, rect.GetProperty("local").GetProperty("x").GetDouble());
        Assert.Equal(100, rect.GetProperty("local").GetProperty("width").GetDouble());
        Assert.Equal(10, rect.GetProperty("world").GetProperty("x").GetDouble());
        Assert.Equal(10, rect.GetProperty("world").GetProperty("y").GetDouble());
        Assert.False(rect.GetProperty("clipped").GetBoolean());
        Assert.False(rect.TryGetProperty("measured", out _));
        Assert.Empty(rect.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void GetBounds_SingleTextNode_IsMeasured()
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "Hello", FontSize = 20 });
        var path = Write(doc, "text.agd.json");

        var data = ParseData(_tools.GetBounds(path, text.Id.ToString()));

        var node = Assert.Single(data.GetProperty("nodes").EnumerateArray());
        Assert.True(node.GetProperty("measured").GetBoolean());
        Assert.True(node.GetProperty("world").GetProperty("width").GetDouble() > 0);
    }

    [Fact]
    public void GetBounds_UnknownNode_ReturnsNodeNotFound()
    {
        var (path, _, _, _) = CreateTwoRectangles();

        Assert.Equal("NODE_NOT_FOUND", ParseError(_tools.GetBounds(path, Guid.NewGuid().ToString())).GetProperty("code").GetString());
        Assert.Equal("NODE_NOT_FOUND", ParseError(_tools.GetBounds(path, "not-a-guid")).GetProperty("code").GetString());
    }

    [Fact]
    public void GetBounds_OutsideWorkingDirectory_IsRejected()
    {
        var result = _tools.GetBounds("../../../etc/passwd");

        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void AlignNodes_ArrayIds_PersistsTransforms()
    {
        var (path, doc, a, b) = CreateTwoRectangles();

        var data = ParseData(_tools.AlignNodes(path, Ids(a.Id, b.Id), "left"));

        Assert.Equal("left", data.GetProperty("mode").GetString());
        Assert.Equal("selection", data.GetProperty("relativeTo").GetString());
        Assert.Equal(10, data.GetProperty("reference").GetProperty("x").GetDouble());
        Assert.Equal(2, data.GetProperty("documentRevn").GetInt64());
        var nodes = data.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal(2, nodes.Count);
        var moved = nodes.Single(n => n.GetProperty("id").GetString() == b.Id.ToString());
        Assert.Equal(200, moved.GetProperty("previousX").GetDouble());
        Assert.Equal(10, moved.GetProperty("x").GetDouble());

        var reloaded = DocumentSerializer.Deserialize(File.ReadAllText(path))!;
        Assert.Equal(2, reloaded.Revn);
        Assert.Equal(10, reloaded.Root.Children.Single(n => n.Id == b.Id).Transform!.X);
    }

    [Fact]
    public void AlignNodes_JsonArrayIds_Works()
    {
        var (path, _, a, b) = CreateTwoRectangles();

        var data = ParseData(_tools.AlignNodes(path, Ids(a.Id, b.Id), "centerY", relativeTo: "selection"));

        Assert.Equal(2, data.GetProperty("nodes").GetArrayLength());
    }

    [Fact]
    public void AlignNodes_SingleNodeDefaultsToParent()
    {
        var (path, _, a, _) = CreateTwoRectangles();

        var data = ParseData(_tools.AlignNodes(path, Ids(a.Id), "right"));

        Assert.Equal("parent", data.GetProperty("relativeTo").GetString());
        Assert.Equal(700, Assert.Single(data.GetProperty("nodes").EnumerateArray()).GetProperty("x").GetDouble());
    }

    [Theory]
    [InlineData("", "INVALID_ARGUMENTS")]
    [InlineData("not-a-guid", "INVALID_ARGUMENTS")]
    public void AlignNodes_BadNodeIds_ReturnsInvalidArguments(string nodeIds, string expectedCode)
    {
        var (path, _, _, _) = CreateTwoRectangles();

        Assert.Equal(expectedCode, ParseError(_tools.AlignNodes(path, [nodeIds], "left")).GetProperty("code").GetString());
    }

    [Fact]
    public void AlignNodes_BadModeOrReference_ReturnsInvalidArguments()
    {
        var (path, _, a, b) = CreateTwoRectangles();

        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.AlignNodes(path, Ids(a.Id, b.Id), "diagonal")).GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.AlignNodes(path, Ids(a.Id, b.Id), "left", relativeTo: "moon")).GetProperty("code").GetString());
    }

    [Fact]
    public void AlignNodes_MixedParents_ReturnsMixedParents()
    {
        var doc = NewDocument();
        var group = Add(doc.Root, new GroupNode { Id = Guid.NewGuid(), Name = "group", Children = [] }, index: 0);
        var inGroup = Add(group, Rect(10, 10, x: 50));
        var inRoot = Add(doc.Root, Rect(10, 10, x: 200), index: 1);
        var path = Write(doc, "mixed.agd.json");

        Assert.Equal("MIXED_PARENTS", ParseError(_tools.AlignNodes(path, Ids(inGroup.Id, inRoot.Id), "left")).GetProperty("code").GetString());
        ParseData(_tools.AlignNodes(path, Ids(inGroup.Id, inRoot.Id), "left", relativeTo: "canvas"));
    }

    [Fact]
    public void AlignNodes_StaleRevision_ReturnsRevisionConflict()
    {
        var (path, _, a, b) = CreateTwoRectangles();

        var result = _tools.AlignNodes(path, Ids(a.Id, b.Id), "left", expectedRevn: 99);

        Assert.Equal("REVISION_CONFLICT", ParseError(result).GetProperty("code").GetString());
        Assert.Equal(1, DocumentSerializer.Deserialize(File.ReadAllText(path))!.Revn);
        Assert.Equal("INVALID_PROPERTY", ParseError(_tools.AlignNodes(path, Ids(a.Id, b.Id), "left", expectedRevn: -1)).GetProperty("code").GetString());
    }

    [Fact]
    public void DistributeNodes_GapMode_ReturnsDerivedGap()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, x: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, x: 15), index: 1);
        var c = Add(doc.Root, Rect(10, 10, x: 100), index: 2);
        var path = Write(doc, "distribute.agd.json");

        var data = ParseData(_tools.DistributeNodes(path, Ids(a.Id, b.Id, c.Id), "horizontal"));

        Assert.Equal("horizontal", data.GetProperty("axis").GetString());
        Assert.Equal("gap", data.GetProperty("mode").GetString());
        Assert.Equal(40, data.GetProperty("gap").GetDouble());
        var middle = data.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == b.Id.ToString());
        Assert.Equal(50, middle.GetProperty("x").GetDouble());
        Assert.Equal(2, data.GetProperty("documentRevn").GetInt64());
    }

    [Fact]
    public void DistributeNodes_ExplicitGapAndCenters_Work()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, y: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, y: 90), index: 1);
        var path = Write(doc, "distribute-two.agd.json");

        var data = ParseData(_tools.DistributeNodes(path, Ids(a.Id, b.Id), "vertical", mode: "centers", gap: 25));

        Assert.Equal("centers", data.GetProperty("mode").GetString());
        var moved = data.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == b.Id.ToString());
        Assert.Equal(25, moved.GetProperty("y").GetDouble());
    }

    [Fact]
    public void DistributeNodes_InvalidInputs_ReturnStructuredErrors()
    {
        var (path, _, a, b) = CreateTwoRectangles();

        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.DistributeNodes(path, Ids(a.Id, b.Id), "horizontal")).GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.DistributeNodes(path, Ids(a.Id, b.Id), "diagonal", gap: 5)).GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.DistributeNodes(path, Ids(a.Id, b.Id), "horizontal", mode: "random", gap: 5)).GetProperty("code").GetString());
        Assert.Equal("INVALID_PROPERTY", ParseError(_tools.DistributeNodes(path, Ids(a.Id, b.Id), "horizontal", gap: double.NaN)).GetProperty("code").GetString());
    }

    [Fact]
    public void FitFrame_WithPadding_ResizesFrameAndKeepsChildrenVisuallyInPlace()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, new FrameNode
        {
            Id = Guid.NewGuid(),
            Name = "frame",
            Width = 50,
            Height = 50,
            ClipContent = false,
            Transform = new Transform { X = 100, Y = 100 },
            Children = []
        });
        var child = Add(frame, Rect(30, 30, x: -10, y: 20));
        var path = Write(doc, "fit.agd.json");
        var before = ParseData(_tools.GetBounds(path, child.Id.ToString())).GetProperty("nodes")[0].GetProperty("world");

        var data = ParseData(_tools.FitFrame(path, frame.Id.ToString(), padding: 5));

        Assert.Equal(frame.Id.ToString(), data.GetProperty("frameId").GetString());
        Assert.Equal(85, data.GetProperty("x").GetDouble());
        Assert.Equal(115, data.GetProperty("y").GetDouble());
        Assert.Equal(40, data.GetProperty("width").GetDouble());
        Assert.Equal(40, data.GetProperty("height").GetDouble());
        Assert.Equal(50, data.GetProperty("previous").GetProperty("width").GetDouble());
        var moved = Assert.Single(data.GetProperty("children").EnumerateArray());
        Assert.Equal(5, moved.GetProperty("x").GetDouble());
        Assert.Equal(5, moved.GetProperty("y").GetDouble());

        var after = ParseData(_tools.GetBounds(path, child.Id.ToString())).GetProperty("nodes")[0].GetProperty("world");
        Assert.Equal(before.GetProperty("x").GetDouble(), after.GetProperty("x").GetDouble());
        Assert.Equal(before.GetProperty("y").GetDouble(), after.GetProperty("y").GetDouble());

        var stale = _tools.FitFrame(path, frame.Id.ToString(), padding: 5, expectedRevn: 1);
        Assert.Equal("REVISION_CONFLICT", ParseError(stale).GetProperty("code").GetString());
        Assert.Equal(2, DocumentSerializer.Deserialize(File.ReadAllText(path))!.Revn);
    }

    [Fact]
    public void FitFrame_InvalidInputs_ReturnStructuredErrors()
    {
        var (path, doc, a, _) = CreateTwoRectangles();

        Assert.Equal("NODE_NOT_FOUND", ParseError(_tools.FitFrame(path, "not-a-guid")).GetProperty("code").GetString());
        Assert.Equal("NODE_NOT_FOUND", ParseError(_tools.FitFrame(path, Guid.NewGuid().ToString())).GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.FitFrame(path, a.Id.ToString())).GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", ParseError(_tools.FitFrame(path, doc.Root.Id.ToString())).GetProperty("code").GetString());
        Assert.Equal("INVALID_PROPERTY", ParseError(_tools.FitFrame(path, a.Id.ToString(), padding: -1)).GetProperty("code").GetString());
        Assert.Equal("INVALID_PROPERTY", ParseError(_tools.FitFrame(path, a.Id.ToString(), expectedRevn: -1)).GetProperty("code").GetString());
    }

    [Fact]
    public void LayoutMutations_OutsideWorkingDirectory_AreRejected()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Equal("PATH_ESCAPE", ParseError(_tools.AlignNodes("../escape.agd.json", [id], "left")).GetProperty("code").GetString());
        Assert.Equal("PATH_ESCAPE", ParseError(_tools.DistributeNodes("../escape.agd.json", [id], "horizontal", gap: 1)).GetProperty("code").GetString());
        Assert.Equal("PATH_ESCAPE", ParseError(_tools.FitFrame("../escape.agd.json", id)).GetProperty("code").GetString());
    }

    // ---- Helpers ----

    private static string[] Ids(params Guid[] ids) => ids.Select(id => id.ToString()).ToArray();

    private (string Path, Document Document, RectangleNode A, RectangleNode B) CreateTwoRectangles()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(100, 50, x: 10, y: 10), index: 0);
        var b = Add(doc.Root, Rect(40, 20, x: 200, y: 100), index: 1);
        return (Write(doc, $"{doc.Id:N}.agd.json"), doc, a, b);
    }

    private string Write(Document doc, string fileName)
    {
        var path = Path.Combine(_testDir, fileName);
        File.WriteAllText(path, DocumentSerializer.Serialize(doc));
        return path;
    }

    private static Document NewDocument(double width = 800, double height = 600) => new()
    {
        Schema = Document.SchemaUrl,
        Id = Guid.NewGuid(),
        Name = "layout",
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

    private static T Add<T>(IContainerNode parent, T node, int index = 0) where T : Node
    {
        node.Order = $"a{index}";
        parent.Children.Add(node);
        return node;
    }

    private static RectangleNode Rect(double width, double height, double x = 0, double y = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "rect",
        Width = width,
        Height = height,
        Transform = new Transform { X = x, Y = y }
    };

    private static JsonElement ParseData(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content.Last(block => block is TextContentBlock)).Text;
        var json = JsonDocument.Parse(text);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean(), $"Expected ok=true but got: {text}");
        return json.RootElement.GetProperty("data");
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
