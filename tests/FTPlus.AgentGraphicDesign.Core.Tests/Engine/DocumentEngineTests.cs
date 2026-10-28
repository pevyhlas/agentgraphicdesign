using System.Text;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Engine;

public class DocumentEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly InMemoryDocumentStore _store;
    private readonly DocumentEngine _engine;

    public DocumentEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-engine-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new InMemoryDocumentStore();
        _engine = new DocumentEngine(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }

        GC.SuppressFinalize(this);
    }

    // ---- Lifecycle ----

    [Fact]
    public void CreateDocument_SetsDefaultsAndRootFrame()
    {
        var doc = _engine.CreateDocument("test");

        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.Equal("test", doc.Name);
        Assert.Equal(1, doc.Version);
        Assert.Equal(1, doc.Revn);
        Assert.Equal(1920, doc.Canvas.Width);
        Assert.Equal(1080, doc.Canvas.Height);
        Assert.Equal("root", doc.Root.Name);
        Assert.Empty(doc.Root.Children);
    }

    [Fact]
    public void CreateDocument_PopulatesSchemaUrl()
    {
        var doc = _engine.CreateDocument("test");

        Assert.Equal(Document.SchemaUrl, doc.Schema);
        Assert.Contains("agd-document.schema.json", doc.Schema);
    }

    [Fact]
    public void CreateDocument_WithBackground_SetsCanvasBackground()
    {
        var doc = _engine.CreateDocument("test", 800, 600, "#ffffff");

        Assert.Equal("#ffffff", doc.Canvas.Background);
        Assert.Equal(800, doc.Canvas.Width);
        Assert.Equal(600, doc.Canvas.Height);
    }

    [Fact]
    public void CreateDocument_NonPositiveDimensions_Throws()
    {
        var ex = Assert.Throws<DocumentEngineException>(() => _engine.CreateDocument("bad", 0, 600));
        Assert.Equal(EngineErrors.InvalidDimensions, ex.Code);
    }

    [Fact]
    public void CreateDocument_WithSeed_ProducesDeterministicIds()
    {
        var options = new CreateDocumentOptions { Seed = 42 };

        var doc1 = _engine.CreateDocument("test", 1920, 1080, null, options);
        var doc2 = _engine.CreateDocument("test", 1920, 1080, null, options);

        Assert.Equal(doc1.Id, doc2.Id);
        Assert.Equal(doc1.Root.Id, doc2.Root.Id);
        Assert.NotEqual(Guid.Empty, doc1.Id);
        Assert.NotEqual(Guid.Empty, doc1.Root.Id);
    }

    [Fact]
    public void CreateDocument_DifferentSeeds_ProduceDifferentIds()
    {
        var doc1 = _engine.CreateDocument("test", 1920, 1080, null, new CreateDocumentOptions { Seed = 1 });
        var doc2 = _engine.CreateDocument("test", 1920, 1080, null, new CreateDocumentOptions { Seed = 2 });

        Assert.NotEqual(doc1.Id, doc2.Id);
        Assert.NotEqual(doc1.Root.Id, doc2.Root.Id);
    }

    [Fact]
    public void CreateDocument_ExplicitIdsOverrideSeed()
    {
        var explicitDocId = Guid.NewGuid();
        var explicitRootId = Guid.NewGuid();
        var options = new CreateDocumentOptions
        {
            Seed = 42,
            DocumentId = explicitDocId,
            RootId = explicitRootId
        };

        var doc = _engine.CreateDocument("test", 1920, 1080, null, options);

        Assert.Equal(explicitDocId, doc.Id);
        Assert.Equal(explicitRootId, doc.Root.Id);
    }

    [Fact]
    public void CreateDocument_WithNullOptions_GeneratesRandomIds()
    {
        var doc1 = _engine.CreateDocument("test", 1920, 1080, null, options: null);
        var doc2 = _engine.CreateDocument("test", 1920, 1080, null, options: null);

        Assert.NotEqual(doc1.Id, doc2.Id);
        Assert.NotEqual(doc1.Root.Id, doc2.Root.Id);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsThroughStore()
    {
        var doc = _engine.CreateDocument("persisted");
        _engine.SaveDocument(doc);

        var loaded = _engine.LoadDocument(doc.Id);

        Assert.NotNull(loaded);
        Assert.Equal(doc.Id, loaded.Id);
        Assert.Equal("persisted", loaded.Name);
    }

    [Fact]
    public void SaveToFileAndLoadFromFile_RoundTrips()
    {
        var doc = _engine.CreateDocument("file-doc");
        var path = Path.Combine(_testDir, "file-doc.agd.json");

        _engine.SaveDocumentToFile(doc, path);
        var loaded = _engine.LoadDocumentFromFile(path);

        Assert.NotNull(loaded);
        Assert.Equal(doc.Id, loaded.Id);
    }

    [Fact]
    public void LoadDocumentFromFile_MissingFile_ReturnsNull()
    {
        Assert.Null(_engine.LoadDocumentFromFile(Path.Combine(_testDir, "nope.agd.json")));
    }

    [Fact]
    public void LoadDocumentFromFile_MalformedJson_ReturnsNull()
    {
        var path = Path.Combine(_testDir, "broken.agd.json");
        File.WriteAllText(path, "{ not json !");

        Assert.Null(_engine.LoadDocumentFromFile(path));
    }

    // ---- Open / Save handle ----

    [Fact]
    public void Open_ByGuidString_LoadsFromStore()
    {
        var doc = _engine.CreateDocument("by-id");
        _engine.SaveDocument(doc);

        var handle = _engine.Open(doc.Id.ToString());

        Assert.NotNull(handle);
        Assert.Null(handle.SourcePath);
        Assert.Equal(doc.Id, handle.Document.Id);
    }

    [Fact]
    public void Open_ByFilePath_LoadsFromFileAndRemembersOrigin()
    {
        var doc = _engine.CreateDocument("by-path");
        var path = Path.Combine(_testDir, "by-path.agd.json");
        _engine.SaveDocumentToFile(doc, path);

        var handle = _engine.Open(path);

        Assert.NotNull(handle);
        Assert.Equal(FTPlus.AgentGraphicDesign.Core.Paths.PlatformPaths.FullPath(path), handle.SourcePath);
    }

    [Fact]
    public void Open_Unknown_ReturnsNull()
    {
        Assert.Null(_engine.Open(Guid.NewGuid().ToString()));
        Assert.Null(_engine.Open(Path.Combine(_testDir, "missing.agd.json")));
    }

    [Fact]
    public void Save_HandleFromFile_WritesBackToSameFile()
    {
        var doc = _engine.CreateDocument("handle-save");
        var path = Path.Combine(_testDir, "handle-save.agd.json");
        _engine.SaveDocumentToFile(doc, path);

        var handle = _engine.Open(path)!;
        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle" });
        _engine.Save(handle);

        var reloaded = _engine.LoadDocumentFromFile(path);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded.Root.Children);
    }

    [Fact]
    public void StoreOperations_WithoutStore_ThrowStoreUnavailable()
    {
        var storeless = new DocumentEngine();
        var doc = storeless.CreateDocument("no-store");

        var ex = Assert.Throws<DocumentEngineException>(() => storeless.SaveDocument(doc));
        Assert.Equal(EngineErrors.StoreUnavailable, ex.Code);
    }

    // ---- Query ----

    [Fact]
    public void CountNodes_CountsWholeTree()
    {
        var doc = BuildNestedDocument();

        Assert.Equal(5, _engine.CountNodes(doc));
    }

    [Fact]
    public void FindNode_FindsNestedNode()
    {
        var doc = BuildNestedDocument();
        var nested = ((GroupNode)doc.Root.Children[1]).Children[0];

        var found = _engine.FindNode(doc, nested.Id);

        Assert.NotNull(found);
        Assert.Equal(nested.Id, found.Id);
    }

    [Fact]
    public void FindNode_Missing_ReturnsNull()
    {
        Assert.Null(_engine.FindNode(BuildNestedDocument(), Guid.NewGuid()));
    }

    [Fact]
    public void ListNodes_ReturnsDocumentOrderWithPathsAndDepth()
    {
        var doc = BuildNestedDocument();

        var nodes = _engine.ListNodes(doc);

        Assert.Equal(5, nodes.Count);
        Assert.Equal("root", nodes[0].Path);
        Assert.Equal(0, nodes[0].Depth);
        Assert.Equal("frame", nodes[0].Type);
        Assert.Equal("rect-a", nodes[1].Name);
        Assert.Equal("root.children[0]", nodes[1].Path);
        Assert.Equal("root.children[1].children[0]", nodes[3].Path);
        Assert.Equal(2, nodes[3].Depth);
    }

    [Fact]
    public void Validate_DelegatesToValidator()
    {
        var doc = _engine.CreateDocument("valid");

        Assert.True(_engine.Validate(doc).IsValid);
    }

    // ---- AddNode ----

    [Fact]
    public void AddNode_ToRoot_AppendsAndBumpsRevn()
    {
        var doc = _engine.CreateDocument("add");

        var node = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "box", Width = 50, Height = 40 });

        Assert.Single(doc.Root.Children);
        Assert.Equal(2, doc.Revn);
        Assert.Equal("box", node.Name);
        Assert.NotEqual(Guid.Empty, node.Id);
        var rect = Assert.IsType<RectangleNode>(node);
        Assert.Equal(50, rect.Width);
    }

    [Fact]
    public void AddNode_AssignsIncreasingFractionalOrders()
    {
        var doc = _engine.CreateDocument("order");

        var first = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });
        var second = _engine.AddNode(doc, null, new NewNodeSpec { Type = "ellipse" });

        Assert.True(string.CompareOrdinal(first.Order, second.Order) < 0);
    }

    [Fact]
    public void AddNode_ToNestedGroup_Works()
    {
        var doc = _engine.CreateDocument("nested-add");
        var group = _engine.AddNode(doc, null, new NewNodeSpec { Type = "group", Name = "g" });

        var child = _engine.AddNode(doc, group.Id, new NewNodeSpec { Type = "text", Text = "hi" });

        var groupNode = Assert.IsType<GroupNode>(doc.Root.Children[0]);
        Assert.Single(groupNode.Children);
        Assert.Equal(child.Id, groupNode.Children[0].Id);
    }

    [Fact]
    public void AddNode_UnknownParent_Throws()
    {
        var doc = _engine.CreateDocument("bad-parent");

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(doc, Guid.NewGuid(), new NewNodeSpec { Type = "rectangle" }));
        Assert.Equal(EngineErrors.ParentNotFound, ex.Code);
    }

    [Fact]
    public void AddNode_ParentNotContainer_Throws()
    {
        var doc = _engine.CreateDocument("leaf-parent");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(doc, rect.Id, new NewNodeSpec { Type = "ellipse" }));
        Assert.Equal(EngineErrors.ParentNotContainer, ex.Code);
    }

    [Fact]
    public void AddNode_UnknownType_Throws()
    {
        var doc = _engine.CreateDocument("bad-type");

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(doc, null, new NewNodeSpec { Type = "hexagon" }));
        Assert.Equal(EngineErrors.InvalidNodeType, ex.Code);
    }

    [Fact]
    public void AddNode_NegativeDimensions_Throws()
    {
        var doc = _engine.CreateDocument("neg");

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Width = -5 }));
        Assert.Equal(EngineErrors.InvalidDimensions, ex.Code);
    }

    [Fact]
    public void AddNode_FailedAdd_DoesNotBumpRevn()
    {
        var doc = _engine.CreateDocument("no-bump");

        Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(doc, null, new NewNodeSpec { Type = "hexagon" }));

        Assert.Equal(1, doc.Revn);
    }

    [Fact]
    public void AddNode_TokenReferences_AreAuthoredAndValidated()
    {
        var doc = _engine.CreateDocument("token-style");
        _engine.SetColorToken(doc, "primary", "#123456");
        _engine.SetTypographyToken(doc, "body", new TypographyToken
        {
            FontFamily = "Inter",
            FontSize = 18,
            FontWeight = 500
        });

        var node = Assert.IsType<TextNode>(_engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Text = "Token text",
            FillTokenRef = "primary",
            StrokeTokenRef = "{primary}",
            TypographyTokenRef = "body"
        }));

        Assert.Equal("primary", node.Style!.Fill!.TokenRef);
        Assert.Null(node.Style.Fill.Color);
        Assert.Equal("{primary}", node.Style.Stroke!.TokenRef);
        Assert.Equal("body", node.TypographyTokenRef);
        Assert.True(_engine.Validate(doc).IsValid);
    }

    [Fact]
    public void AddNode_ConflictingLiteralAndToken_DoesNotMutate()
    {
        var doc = _engine.CreateDocument("token-conflict");
        _engine.SetColorToken(doc, "primary", "#123456");
        var revision = doc.Revn;

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Fill = "#ffffff",
            FillTokenRef = "primary"
        }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal(revision, doc.Revn);
        Assert.Empty(doc.Root.Children);
    }

    [Fact]
    public void AddNode_MissingTokenReference_DoesNotMutate()
    {
        var doc = _engine.CreateDocument("missing-token");

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            FillTokenRef = "missing"
        }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal(1, doc.Revn);
        Assert.Empty(doc.Root.Children);
    }

    [Fact]
    public void AddNode_Line_SetsCoordinates()
    {
        var doc = _engine.CreateDocument("line");

        var node = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "line",
            Name = "diagonal",
            X1 = 10,
            Y1 = 20,
            X2 = 100,
            Y2 = 200
        });

        var line = Assert.IsType<LineNode>(node);
        Assert.Equal("diagonal", line.Name);
        Assert.Equal(10, line.X1);
        Assert.Equal(20, line.Y1);
        Assert.Equal(100, line.X2);
        Assert.Equal(200, line.Y2);
    }

    [Fact]
    public void AddNode_Line_DefaultsTo100x100()
    {
        var doc = _engine.CreateDocument("line-defaults");

        var node = _engine.AddNode(doc, null, new NewNodeSpec { Type = "line" });

        var line = Assert.IsType<LineNode>(node);
        Assert.Equal(0, line.X1);
        Assert.Equal(0, line.Y1);
        Assert.Equal(100, line.X2);
        Assert.Equal(100, line.Y2);
    }

    [Fact]
    public void AddNode_Polygon_ParsesPoints()
    {
        var doc = _engine.CreateDocument("polygon");

        var node = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "polygon",
            Name = "triangle",
            Points = "0,0 50,100 100,0"
        });

        var polygon = Assert.IsType<PolygonNode>(node);
        Assert.Equal("triangle", polygon.Name);
        Assert.Equal(3, polygon.Points.Count);
        Assert.Equal(0, polygon.Points[0].X);
        Assert.Equal(0, polygon.Points[0].Y);
        Assert.Equal(50, polygon.Points[1].X);
        Assert.Equal(100, polygon.Points[1].Y);
        Assert.Equal(100, polygon.Points[2].X);
        Assert.Equal(0, polygon.Points[2].Y);
        Assert.True(polygon.Closed);
    }

    [Fact]
    public void AddNode_Polygon_OpenPolyline()
    {
        var doc = _engine.CreateDocument("polyline");

        var node = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "polygon",
            Points = "0,0 100,100",
            PolygonClosed = false
        });

        var polygon = Assert.IsType<PolygonNode>(node);
        Assert.False(polygon.Closed);
    }

    [Fact]
    public void AddNode_Image_SetsSrcAndDimensions()
    {
        var doc = _engine.CreateDocument("image");

        var node = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "image",
            Name = "photo",
            ImageSrc = "https://example.com/image.png",
            Width = 400,
            Height = 300
        });

        var image = Assert.IsType<ImageNode>(node);
        Assert.Equal("photo", image.Name);
        Assert.Equal("https://example.com/image.png", image.Src);
        Assert.Equal(400, image.Width);
        Assert.Equal(300, image.Height);
    }

    [Fact]
    public void AddNode_Image_DefaultsTo100x100()
    {
        var doc = _engine.CreateDocument("image-defaults");

        var node = _engine.AddNode(doc, null, new NewNodeSpec { Type = "image" });

        var image = Assert.IsType<ImageNode>(node);
        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
    }

    // ---- UpdateNode ----

    [Fact]
    public void UpdateNode_AppliesPartialUpdates()
    {
        var doc = _engine.CreateDocument("update");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Width = 10, Height = 10 });

        var updated = _engine.UpdateNode(doc, rect.Id, new NodeUpdates
        {
            Name = "renamed",
            Width = 200,
            X = 5,
            Fill = "#ff0000",
            Visible = false
        });

        var r = Assert.IsType<RectangleNode>(updated);
        Assert.Equal("renamed", r.Name);
        Assert.Equal(200, r.Width);
        Assert.Equal(10, r.Height);
        Assert.Equal(5, r.Transform?.X);
        Assert.Equal("#ff0000", r.Style?.Fill?.Color);
        Assert.False(r.Visible);
        Assert.Equal(3, doc.Revn);
    }

    [Fact]
    public void UpdateNode_TokenReferences_ReplaceLiteralStyles()
    {
        var doc = _engine.CreateDocument("update-token-style");
        _engine.SetColorToken(doc, "primary", "#123456");
        _engine.SetTypographyToken(doc, "body", new TypographyToken { FontFamily = "Inter" });
        var text = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Fill = "#ffffff",
            Stroke = "#000000"
        });

        var updated = Assert.IsType<TextNode>(_engine.UpdateNode(doc, text.Id, new NodeUpdates
        {
            FillTokenRef = "{primary}",
            StrokeTokenRef = "primary",
            TypographyTokenRef = "{body}"
        }));

        Assert.Equal("{primary}", updated.Style!.Fill!.TokenRef);
        Assert.Null(updated.Style.Fill.Color);
        Assert.Equal("primary", updated.Style.Stroke!.TokenRef);
        Assert.Equal("{body}", updated.TypographyTokenRef);
        Assert.True(_engine.Validate(doc).IsValid);
    }

    [Fact]
    public void UpdateNode_ClearTypographyToken_DetachesReference()
    {
        var doc = _engine.CreateDocument("clear-typography-token");
        _engine.SetTypographyToken(doc, "body", new TypographyToken { FontFamily = "Inter" });
        var text = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            TypographyTokenRef = "body"
        });

        var updated = Assert.IsType<TextNode>(_engine.UpdateNode(doc, text.Id, new NodeUpdates
        {
            TypographyTokenRef = PropertyClear.ClearValue
        }));

        Assert.Null(updated.TypographyTokenRef);
    }

    [Fact]
    public void UpdateNode_Missing_Throws()
    {
        var doc = _engine.CreateDocument("update-missing");

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, Guid.NewGuid(), new NodeUpdates { Name = "x" }));
        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    [Fact]
    public void UpdateNode_SizeOnTextNode_Throws()
    {
        var doc = _engine.CreateDocument("text-size");
        var text = _engine.AddNode(doc, null, new NewNodeSpec { Type = "text", Text = "hi" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, text.Id, new NodeUpdates { Width = 100 }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void UpdateNode_NegativeDimensions_Throws()
    {
        var doc = _engine.CreateDocument("update-neg");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, rect.Id, new NodeUpdates { Height = -1 }));
        Assert.Equal(EngineErrors.InvalidDimensions, ex.Code);
    }

    [Fact]
    public void UpdateNode_Text_UpdatesTextContent()
    {
        var doc = _engine.CreateDocument("update-text");
        var text = _engine.AddNode(doc, null, new NewNodeSpec { Type = "text", Text = "hello" });

        _engine.UpdateNode(doc, text.Id, new NodeUpdates { Text = "goodbye" });

        var textNode = Assert.IsType<TextNode>(_engine.FindNode(doc, text.Id));
        Assert.Equal("goodbye", textNode.Text);
    }

    [Fact]
    public void UpdateNode_TextProperties_UpdatesFontSettings()
    {
        var doc = _engine.CreateDocument("update-font");
        var text = _engine.AddNode(doc, null, new NewNodeSpec { Type = "text", Text = "hi" });

        _engine.UpdateNode(doc, text.Id, new NodeUpdates
        {
            FontFamily = "Arial",
            FontSize = 24,
            FontWeight = 700
        });

        var textNode = Assert.IsType<TextNode>(_engine.FindNode(doc, text.Id));
        Assert.Equal("Arial", textNode.FontFamily);
        Assert.Equal(24, textNode.FontSize);
        Assert.Equal(700, textNode.FontWeight);
    }

    [Fact]
    public void AddNode_TextLayoutFields_AreStoredNormalized()
    {
        var doc = _engine.CreateDocument("add-layout");

        var node = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Text = "Hello wrapped world",
            TextAlign = " Center ",
            VerticalAlign = "MIDDLE",
            MaxWidth = 80,
            LineHeight = 1.5,
            LetterSpacing = -0.25
        });

        var text = Assert.IsType<TextNode>(node);
        Assert.Equal("center", text.TextAlign);
        Assert.Equal("middle", text.VerticalAlign);
        Assert.Equal(80, text.MaxWidth);
        Assert.Equal(1.5, text.LineHeight);
        Assert.Equal(-0.25, text.LetterSpacing);
    }

    [Fact]
    public void AddNode_TextLayoutFieldsOnNonText_IsRejected()
    {
        var doc = _engine.CreateDocument("add-layout-rect");

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", MaxWidth = 80 }));

        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Theory]
    [InlineData("justify", null, null, null)]
    [InlineData(null, "center", null, null)]
    [InlineData(null, null, 0d, null)]
    [InlineData(null, null, null, -1d)]
    public void AddNode_InvalidTextLayoutValues_AreRejected(string? textAlign, string? verticalAlign, double? maxWidth, double? lineHeight)
    {
        var doc = _engine.CreateDocument("add-layout-invalid");

        var ex = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Text = "x",
            TextAlign = textAlign,
            VerticalAlign = verticalAlign,
            MaxWidth = maxWidth,
            LineHeight = lineHeight
        }));

        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
        Assert.Empty(doc.Root.Children);
    }

    [Fact]
    public void UpdateNode_TextLayoutFields_UpdateAndClear()
    {
        var doc = _engine.CreateDocument("update-layout");
        var text = _engine.AddNode(doc, null, new NewNodeSpec { Type = "text", Text = "hi" });

        _engine.UpdateNode(doc, text.Id, new NodeUpdates
        {
            TextAlign = "end",
            VerticalAlign = "baseline",
            MaxWidth = 200,
            LineHeight = 2,
            LetterSpacing = 1
        });
        var textNode = Assert.IsType<TextNode>(_engine.FindNode(doc, text.Id));
        Assert.Equal("end", textNode.TextAlign);
        Assert.Equal("baseline", textNode.VerticalAlign);
        Assert.Equal(200, textNode.MaxWidth);
        Assert.Equal(2, textNode.LineHeight);
        Assert.Equal(1, textNode.LetterSpacing);

        _engine.UpdateNode(doc, text.Id, new NodeUpdates
        {
            TextAlign = PropertyClear.ClearValue,
            VerticalAlign = PropertyClear.ClearValue
        });
        Assert.Null(textNode.TextAlign);
        Assert.Null(textNode.VerticalAlign);
        Assert.Equal(200, textNode.MaxWidth);

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, text.Id, new NodeUpdates { LetterSpacing = double.NaN }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void UpdateNode_PathData_UpdatesPathD()
    {
        var doc = _engine.CreateDocument("update-path");
        var path = _engine.AddNode(doc, null, new NewNodeSpec { Type = "path", PathData = "M0 0" });

        _engine.UpdateNode(doc, path.Id, new NodeUpdates { PathData = "M10 10 L20 20" });

        var pathNode = Assert.IsType<PathNode>(_engine.FindNode(doc, path.Id));
        Assert.Equal("M10 10 L20 20", pathNode.D);
    }

    [Fact]
    public void UpdateNode_CornerRadius_UpdatesRectangle()
    {
        var doc = _engine.CreateDocument("update-radius");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        _engine.UpdateNode(doc, rect.Id, new NodeUpdates { CornerRadius = 8 });

        var rectNode = Assert.IsType<RectangleNode>(_engine.FindNode(doc, rect.Id));
        Assert.Equal(8, rectNode.CornerRadius);
    }

    [Fact]
    public void UpdateNode_Opacity_UpdatesStyleOpacity()
    {
        var doc = _engine.CreateDocument("update-opacity");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        _engine.UpdateNode(doc, rect.Id, new NodeUpdates { Opacity = 0.5 });

        var rectNode = _engine.FindNode(doc, rect.Id)!;
        Assert.Equal(0.5, rectNode.Style?.Opacity);
    }

    [Fact]
    public void UpdateNode_ClearFill_RemovesFill()
    {
        var doc = _engine.CreateDocument("clear-fill");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Fill = "#ff0000" });

        _engine.UpdateNode(doc, rect.Id, new NodeUpdates { Fill = PropertyClear.ClearValue });

        var rectNode = _engine.FindNode(doc, rect.Id)!;
        Assert.Null(rectNode.Style?.Fill);
    }

    [Fact]
    public void UpdateNode_ClearStroke_RemovesStroke()
    {
        var doc = _engine.CreateDocument("clear-stroke");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Stroke = "#000000" });

        _engine.UpdateNode(doc, rect.Id, new NodeUpdates { Stroke = PropertyClear.ClearValue });

        var rectNode = _engine.FindNode(doc, rect.Id)!;
        Assert.Null(rectNode.Style?.Stroke);
    }

    [Fact]
    public void UpdateNode_TextOnNonText_Throws()
    {
        var doc = _engine.CreateDocument("text-on-rect");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, rect.Id, new NodeUpdates { Text = "oops" }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void UpdateNode_PathDataOnNonPath_Throws()
    {
        var doc = _engine.CreateDocument("pathdata-on-rect");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, rect.Id, new NodeUpdates { PathData = "M0 0" }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void UpdateNode_CornerRadiusOnNonRect_Throws()
    {
        var doc = _engine.CreateDocument("radius-on-ellipse");
        var ellipse = _engine.AddNode(doc, null, new NewNodeSpec { Type = "ellipse" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, ellipse.Id, new NodeUpdates { CornerRadius = 5 }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void UpdateNode_InvalidOpacity_Throws()
    {
        var doc = _engine.CreateDocument("bad-opacity");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, rect.Id, new NodeUpdates { Opacity = 1.5 }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void UpdateNode_InvalidFontWeight_Throws()
    {
        var doc = _engine.CreateDocument("bad-weight");
        var text = _engine.AddNode(doc, null, new NewNodeSpec { Type = "text" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, text.Id, new NodeUpdates { FontWeight = 1000 }));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    // ---- MoveNode ----

    [Fact]
    public void MoveNode_ToNewParent_MovesAndBumpsRevn()
    {
        var doc = _engine.CreateDocument("move");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "mover" });
        var group = _engine.AddNode(doc, null, new NewNodeSpec { Type = "group", Name = "target" });
        var revnBefore = doc.Revn;

        var moved = _engine.MoveNode(doc, rect.Id, group.Id);

        Assert.Equal(revnBefore + 1, doc.Revn);
        var groupNode = Assert.IsType<GroupNode>(_engine.FindNode(doc, group.Id));
        Assert.Single(groupNode.Children);
        Assert.Equal(rect.Id, groupNode.Children[0].Id);
        Assert.Single(doc.Root.Children); // only group remains at root
    }

    [Fact]
    public void MoveNode_ToRoot_MovesWithNullParent()
    {
        var doc = _engine.CreateDocument("move-root");
        var group = _engine.AddNode(doc, null, new NewNodeSpec { Type = "group" });
        var rect = _engine.AddNode(doc, group.Id, new NewNodeSpec { Type = "rectangle" });

        _engine.MoveNode(doc, rect.Id, null);

        var groupNode = Assert.IsType<GroupNode>(_engine.FindNode(doc, group.Id));
        Assert.Empty(groupNode.Children);
        Assert.Equal(2, doc.Root.Children.Count);
    }

    [Fact]
    public void MoveNode_Root_Throws()
    {
        var doc = _engine.CreateDocument("move-root-fail");

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.MoveNode(doc, doc.Root.Id, null));
        Assert.Equal(EngineErrors.CannotDeleteRoot, ex.Code);
    }

    [Fact]
    public void MoveNode_ToDescendant_Throws()
    {
        var doc = _engine.CreateDocument("move-descendant");
        var group = _engine.AddNode(doc, null, new NewNodeSpec { Type = "group", Name = "parent" });
        var childGroup = _engine.AddNode(doc, group.Id, new NewNodeSpec { Type = "group", Name = "child" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.MoveNode(doc, group.Id, childGroup.Id));
        Assert.Equal(EngineErrors.InvalidProperty, ex.Code);
    }

    [Fact]
    public void MoveNode_ToNonContainer_Throws()
    {
        var doc = _engine.CreateDocument("move-noncontainer");
        var rect = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });
        var ellipse = _engine.AddNode(doc, null, new NewNodeSpec { Type = "ellipse" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.MoveNode(doc, rect.Id, ellipse.Id));
        Assert.Equal(EngineErrors.ParentNotContainer, ex.Code);
    }

    // ---- ReorderNode ----

    [Fact]
    public void ReorderNode_PlacesBetweenSiblings()
    {
        var doc = _engine.CreateDocument("reorder");
        var a = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "a" });
        var b = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "b" });
        var c = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "c" });

        _engine.ReorderNode(doc, c.Id, a.Id, b.Id);

        var nodes = _engine.ListNodes(doc).Skip(1).ToList(); // skip root
        Assert.Equal("a", nodes[0].Name);
        Assert.Equal("c", nodes[1].Name);
        Assert.Equal("b", nodes[2].Name);
    }

    [Fact]
    public void ReorderNode_MoveToEnd()
    {
        var doc = _engine.CreateDocument("reorder-end");
        var a = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "a" });
        var b = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "b" });
        var c = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "c" });

        _engine.ReorderNode(doc, a.Id, c.Id, null);

        var nodes = _engine.ListNodes(doc).Skip(1).ToList();
        Assert.Equal("b", nodes[0].Name);
        Assert.Equal("c", nodes[1].Name);
        Assert.Equal("a", nodes[2].Name);
    }

    [Fact]
    public void ReorderNode_MoveToStart()
    {
        var doc = _engine.CreateDocument("reorder-start");
        var a = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "a" });
        var b = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "b" });
        var c = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "c" });

        _engine.ReorderNode(doc, c.Id, null, a.Id);

        var nodes = _engine.ListNodes(doc).Skip(1).ToList();
        Assert.Equal("c", nodes[0].Name);
        Assert.Equal("a", nodes[1].Name);
        Assert.Equal("b", nodes[2].Name);
    }

    [Fact]
    public void ReorderNode_Root_Throws()
    {
        var doc = _engine.CreateDocument("reorder-root");
        _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(doc, doc.Root.Id, null, null));
        Assert.Equal(EngineErrors.CannotDeleteRoot, ex.Code);
    }

    [Fact]
    public void ReorderNode_SiblingNotInParent_Throws()
    {
        var doc = _engine.CreateDocument("reorder-wrong-parent");
        var a = _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "a" });
        var group = _engine.AddNode(doc, null, new NewNodeSpec { Type = "group" });
        var b = _engine.AddNode(doc, group.Id, new NewNodeSpec { Type = "rectangle", Name = "b" });

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(doc, a.Id, b.Id, null));
        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    // ---- RemoveNode ----

    [Fact]
    public void RemoveNode_RemovesNestedNodeViaParentLink()
    {
        var doc = BuildNestedDocument();
        var group = (GroupNode)doc.Root.Children[1];
        var nested = group.Children[0];
        var revnBefore = doc.Revn;

        _engine.RemoveNode(doc, nested.Id);

        Assert.Single(group.Children);
        Assert.Equal(revnBefore + 1, doc.Revn);
        Assert.Null(_engine.FindNode(doc, nested.Id));
    }

    [Fact]
    public void RemoveNode_Root_Throws()
    {
        var doc = _engine.CreateDocument("no-root-delete");

        var ex = Assert.Throws<DocumentEngineException>(() => _engine.RemoveNode(doc, doc.Root.Id));
        Assert.Equal(EngineErrors.CannotDeleteRoot, ex.Code);
    }

    [Fact]
    public void RemoveNode_Missing_Throws()
    {
        var doc = _engine.CreateDocument("remove-missing");

        var ex = Assert.Throws<DocumentEngineException>(() => _engine.RemoveNode(doc, Guid.NewGuid()));
        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    // ---- Export / render ----

    [Fact]
    public async Task ExportAsync_Svg_ProducesSvg()
    {
        var doc = _engine.CreateDocument("export", 800, 600);
        _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Width = 10, Height = 10, Fill = "#ff0000" });

        var result = await _engine.ExportAsync(doc, new ExportOptions());

        Assert.Equal("image/svg+xml", result.MimeType);
        var svg = Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<svg", svg);
        Assert.Contains("<rect", svg);
    }

    [Fact]
    public async Task ExportAsync_UnknownFormat_Throws()
    {
        var doc = _engine.CreateDocument("export-unknown");

        var ex = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            _engine.ExportAsync(doc, new ExportOptions { Format = "png" }));
        Assert.Equal(EngineErrors.ExportUnsupported, ex.Code);
    }

    [Fact]
    public void Render_WithoutRenderer_Throws()
    {
        var doc = _engine.CreateDocument("render");

        var ex = Assert.Throws<DocumentEngineException>(() => _engine.Render(doc, new RenderOptions()));
        Assert.Equal(EngineErrors.RenderUnavailable, ex.Code);
    }

    // ---- Fixtures ----

    /// <summary>root frame → [rect-a, group-b → [rect-b1, text-b2]] (5 nodes total).</summary>
    private Document BuildNestedDocument()
    {
        var doc = _engine.CreateDocument("nested");
        _engine.AddNode(doc, null, new NewNodeSpec { Type = "rectangle", Name = "rect-a" });
        var group = _engine.AddNode(doc, null, new NewNodeSpec { Type = "group", Name = "group-b" });
        _engine.AddNode(doc, group.Id, new NewNodeSpec { Type = "rectangle", Name = "rect-b1" });
        _engine.AddNode(doc, group.Id, new NewNodeSpec { Type = "text", Name = "text-b2", Text = "hello" });
        return doc;
    }

    private sealed class InMemoryDocumentStore : IDocumentStore
    {
        private readonly Dictionary<Guid, string> _documents = [];

        public DocumentStoreEntry? Load(Guid id) =>
            _documents.TryGetValue(id, out var json)
                ? new DocumentStoreEntry(Core.Serialization.DocumentSerializer.Deserialize(json)!, SourcePath: null)
                : null;

        public void Save(DocumentStoreEntry entry, long? expectedRevn = null)
        {
            var document = entry.Document;
            if (expectedRevn.HasValue)
            {
                var existing = Load(document.Id)?.Document;
                if (existing is not null && existing.Revn != expectedRevn.Value)
                {
                    throw new DocumentEngineException(EngineErrors.RevisionConflict,
                        $"Revision conflict: expected {expectedRevn.Value}, got {existing.Revn}");
                }
            }
            _documents[document.Id] = Core.Serialization.DocumentSerializer.Serialize(document);
        }

        public void Create(Document document)
        {
            if (_documents.ContainsKey(document.Id))
            {
                throw new DocumentEngineException(DocumentStoreErrors.DocumentExists, "Document exists");
            }

            _documents.Add(document.Id, Core.Serialization.DocumentSerializer.Serialize(document));
        }

        public void Delete(Guid id) => _documents.Remove(id);

        public IEnumerable<DocumentInfo> List() =>
            _documents.Keys.Select(id =>
            {
                var doc = Load(id)!.Document;
                return new DocumentInfo(doc.Id, doc.Name, DateTimeOffset.UnixEpoch);
            });
    }


    // ---- Fill rule ----

    [Fact]
    public void AddNode_PathWithFillRule_PersistsNormalizedValue()
    {
        var doc = _engine.CreateDocument("fill-rule");

        var path = Assert.IsType<PathNode>(_engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "path",
            PathData = "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z",
            FillRule = "EvenOdd"
        }));

        Assert.Equal("evenodd", path.Style!.FillRule);
        Assert.True(_engine.Validate(doc).IsValid);
    }

    [Fact]
    public void AddNode_FillRuleOnRectangle_ThrowsInvalidProperty()
    {
        var doc = _engine.CreateDocument("fill-rule-rect");

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            FillRule = "evenodd"
        }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Empty(doc.Root.Children);
    }

    [Fact]
    public void AddNode_UnknownFillRule_ThrowsInvalidProperty()
    {
        var doc = _engine.CreateDocument("fill-rule-bad");

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "path",
            PathData = "M0,0 L10,0 L10,10 Z",
            FillRule = "even-odd"
        }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
    }

    [Fact]
    public void UpdateNode_FillRule_SetsAndClears()
    {
        var doc = _engine.CreateDocument("fill-rule-update");
        var path = _engine.AddNode(doc, null, new NewNodeSpec { Type = "path", PathData = "M0,0 L10,0 L10,10 Z" });

        var updated = Assert.IsType<PathNode>(_engine.UpdateNode(doc, path.Id, new NodeUpdates { FillRule = "evenodd" }));
        Assert.Equal("evenodd", updated.Style!.FillRule);

        var cleared = Assert.IsType<PathNode>(_engine.UpdateNode(doc, path.Id, new NodeUpdates { FillRule = PropertyClear.ClearValue }));
        Assert.Null(cleared.Style?.FillRule);
    }

    [Fact]
    public void UpdateNode_FillRuleOnEllipse_ThrowsInvalidProperty()
    {
        var doc = _engine.CreateDocument("fill-rule-ellipse");
        var ellipse = _engine.AddNode(doc, null, new NewNodeSpec { Type = "ellipse", Width = 10, Height = 10 });

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, ellipse.Id, new NodeUpdates { FillRule = "evenodd" }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Null(ellipse.Style?.FillRule);
    }

    [Fact]
    public void UpdateNode_UnknownFillRule_ThrowsInvalidProperty()
    {
        var doc = _engine.CreateDocument("fill-rule-update-bad");
        var path = _engine.AddNode(doc, null, new NewNodeSpec { Type = "path", PathData = "M0,0 L10,0 L10,10 Z" });

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(doc, path.Id, new NodeUpdates { FillRule = "winding" }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
    }
}
