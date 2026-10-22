using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests;

public class MutationCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly DocumentEngine _engine;

    public MutationCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _engine = new DocumentEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void AddNode_Rectangle_CreatesNodeAndSaves()
    {
        var docPath = CreateTestDocument("add-rect-test");

        var handle = _engine.Open(docPath)!;
        var node = _engine.AddNode(handle.Document, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "rect1",
            Width = 100,
            Height = 50,
            X = 10,
            Y = 20,
            Fill = "#ff0000"
        });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded.Root.Children);
        var rect = reloaded.Root.Children[0] as RectangleNode;
        Assert.NotNull(rect);
        Assert.Equal("rect1", rect.Name);
        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
        Assert.NotNull(rect.Transform);
        Assert.Equal(10, rect.Transform.X);
        Assert.Equal(20, rect.Transform.Y);
    }

    [Fact]
    public void AddNode_Text_CreatesTextNode()
    {
        var docPath = CreateTestDocument("add-text-test");

        var handle = _engine.Open(docPath)!;
        var node = _engine.AddNode(handle.Document, null, new NewNodeSpec
        {
            Type = "text",
            Name = "label",
            Text = "Hello World",
            X = 50,
            Y = 100
        });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        var text = reloaded.Root.Children[0] as TextNode;
        Assert.NotNull(text);
        Assert.Equal("Hello World", text.Text);
    }

    [Fact]
    public void AddNode_Ellipse_CreatesEllipseNode()
    {
        var docPath = CreateTestDocument("add-ellipse-test");

        var handle = _engine.Open(docPath)!;
        _engine.AddNode(handle.Document, null, new NewNodeSpec
        {
            Type = "ellipse",
            Name = "circle1",
            Width = 80,
            Height = 80
        });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        var ellipse = reloaded.Root.Children[0] as EllipseNode;
        Assert.NotNull(ellipse);
        Assert.Equal(80, ellipse.Width);
    }

    [Fact]
    public void AddNode_Group_CreatesGroupNode()
    {
        var docPath = CreateTestDocument("add-group-test");

        var handle = _engine.Open(docPath)!;
        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "group", Name = "mygroup" });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        var group = reloaded.Root.Children[0] as GroupNode;
        Assert.NotNull(group);
        Assert.Equal("mygroup", group.Name);
    }

    [Fact]
    public void AddNode_ToGroup_NestsCorrectly()
    {
        var docPath = CreateTestDocument("nested-add-test");

        var handle = _engine.Open(docPath)!;
        var group = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "group", Name = "container" });
        _engine.AddNode(handle.Document, group.Id, new NewNodeSpec { Type = "rectangle", Name = "child", Width = 50, Height = 50 });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        var reloadedGroup = reloaded.Root.Children[0] as GroupNode;
        Assert.NotNull(reloadedGroup);
        Assert.Single(reloadedGroup.Children);
        Assert.Equal("child", reloadedGroup.Children[0].Name);
    }

    [Fact]
    public void AddNode_InvalidParent_Throws()
    {
        var docPath = CreateTestDocument("invalid-parent-test");
        var handle = _engine.Open(docPath)!;

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(handle.Document, Guid.NewGuid(), new NewNodeSpec { Type = "rectangle", Width = 100, Height = 100 }));

        Assert.Equal(EngineErrors.ParentNotFound, ex.Code);
    }

    [Fact]
    public void AddNode_InvalidType_Throws()
    {
        var docPath = CreateTestDocument("invalid-type-test");
        var handle = _engine.Open(docPath)!;

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "unknown" }));

        Assert.Equal(EngineErrors.InvalidNodeType, ex.Code);
    }

    [Fact]
    public void UpdateNode_ChangesProperties()
    {
        var docPath = CreateTestDocument("update-test");
        var handle = _engine.Open(docPath)!;
        var node = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "rect", Width = 100, Height = 50 });
        _engine.Save(handle);

        handle = _engine.Open(docPath)!;
        _engine.UpdateNode(handle.Document, node.Id, new NodeUpdates { Name = "updated", Width = 200, Fill = "#00ff00" });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        var rect = reloaded.Root.Children[0] as RectangleNode;
        Assert.NotNull(rect);
        Assert.Equal("updated", rect.Name);
        Assert.Equal(200, rect.Width);
        Assert.Equal("#00ff00", rect.Style?.Fill?.Color);
    }

    [Fact]
    public void UpdateNode_PartialUpdate_PreservesOtherProperties()
    {
        var docPath = CreateTestDocument("partial-update-test");
        var handle = _engine.Open(docPath)!;
        var node = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "rect", Width = 100, Height = 50, X = 10, Y = 20 });
        _engine.Save(handle);

        handle = _engine.Open(docPath)!;
        _engine.UpdateNode(handle.Document, node.Id, new NodeUpdates { Width = 200 });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        var rect = reloaded.Root.Children[0] as RectangleNode;
        Assert.NotNull(rect);
        Assert.Equal(200, rect.Width);
        Assert.Equal(50, rect.Height);
        Assert.NotNull(rect.Transform);
        Assert.Equal(10, rect.Transform.X);
    }

    [Fact]
    public void UpdateNode_Visibility_Works()
    {
        var docPath = CreateTestDocument("visibility-test");
        var handle = _engine.Open(docPath)!;
        var node = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Width = 100, Height = 50 });
        _engine.Save(handle);

        handle = _engine.Open(docPath)!;
        _engine.UpdateNode(handle.Document, node.Id, new NodeUpdates { Visible = false });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.Root.Children[0].Visible);
    }

    [Fact]
    public void UpdateNode_NotFound_Throws()
    {
        var docPath = CreateTestDocument("update-notfound-test");
        var handle = _engine.Open(docPath)!;

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(handle.Document, Guid.NewGuid(), new NodeUpdates { Name = "x" }));

        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    [Fact]
    public void DeleteNode_RemovesNode()
    {
        var docPath = CreateTestDocument("delete-test");
        var handle = _engine.Open(docPath)!;
        var node = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Width = 100, Height = 50 });
        _engine.Save(handle);

        handle = _engine.Open(docPath)!;
        _engine.RemoveNode(handle.Document, node.Id);
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(docPath);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded.Root.Children);
    }

    [Fact]
    public void DeleteNode_CannotDeleteRoot_Throws()
    {
        var docPath = CreateTestDocument("delete-root-test");
        var handle = _engine.Open(docPath)!;

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.RemoveNode(handle.Document, handle.Document.Root.Id));

        Assert.Equal(EngineErrors.CannotDeleteRoot, ex.Code);
    }

    [Fact]
    public void DeleteNode_NotFound_Throws()
    {
        var docPath = CreateTestDocument("delete-notfound-test");
        var handle = _engine.Open(docPath)!;

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.RemoveNode(handle.Document, Guid.NewGuid()));

        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    [Fact]
    public void ListNodes_ReturnsAllNodes()
    {
        var docPath = CreateTestDocument("list-test");
        var handle = _engine.Open(docPath)!;
        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "r1", Width = 100, Height = 50 });
        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "ellipse", Name = "e1", Width = 80, Height = 80 });
        _engine.Save(handle);

        handle = _engine.Open(docPath)!;
        var nodes = _engine.ListNodes(handle.Document);

        Assert.Equal(3, nodes.Count);
        Assert.Contains(nodes, n => n.Name == "root" && n.Type == "frame");
        Assert.Contains(nodes, n => n.Name == "r1" && n.Type == "rectangle");
        Assert.Contains(nodes, n => n.Name == "e1" && n.Type == "ellipse");
    }

    [Fact]
    public void ListNodes_IncludesDepthAndPath()
    {
        var docPath = CreateTestDocument("list-depth-test");
        var handle = _engine.Open(docPath)!;
        var group = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "group", Name = "g1" });
        _engine.AddNode(handle.Document, group.Id, new NewNodeSpec { Type = "rectangle", Name = "nested", Width = 50, Height = 50 });
        _engine.Save(handle);

        handle = _engine.Open(docPath)!;
        var nodes = _engine.ListNodes(handle.Document);

        var nested = nodes.First(n => n.Name == "nested");
        Assert.Equal(2, nested.Depth);
        Assert.Contains("children", nested.Path);
    }

    [Fact]
    public void MutationCommands_IncrementRevn()
    {
        var docPath = CreateTestDocument("revn-test");
        var handle = _engine.Open(docPath)!;
        var initialRevn = handle.Document.Revn;

        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Width = 100, Height = 50 });
        Assert.Equal(initialRevn + 1, handle.Document.Revn);

        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "ellipse", Width = 80, Height = 80 });
        Assert.Equal(initialRevn + 2, handle.Document.Revn);
    }

    private string CreateTestDocument(string name)
    {
        var doc = _engine.CreateDocument(name, 800, 600);
        var path = Path.Combine(_testDir, $"{name}.agd.json");
        _engine.SaveDocumentToFile(doc, path);
        return path;
    }
}
