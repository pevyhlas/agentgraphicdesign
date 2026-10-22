using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Validation;
using FTPlus.AgentGraphicDesign.Mcp;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Skia;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public class DocumentToolsTests : IDisposable
{
    private readonly string _testDir;
    private readonly TestDocumentStore _store;
    private readonly PathConfinement _pathConfinement;
    private readonly IRenderer _renderer;
    private readonly DocumentTools _tools;

    public DocumentToolsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new TestDocumentStore(_testDir);
        _pathConfinement = new PathConfinement(_testDir);
        _renderer = new SkiaRenderer();
        _tools = new DocumentTools(new DocumentEngine(_store), _pathConfinement, _renderer);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void CreateDocument_ReturnsValidJson()
    {
        var result = _tools.CreateDocument("test-doc", 800, 600, "#ffffff");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("id", out _));
        Assert.Equal("test-doc", data.GetProperty("name").GetString());
        Assert.Equal(800, data.GetProperty("canvas").GetProperty("width").GetDouble());
        Assert.Equal(600, data.GetProperty("canvas").GetProperty("height").GetDouble());
    }

    [Fact]
    public void CreateDocument_NonFiniteDimensions_ReturnsError()
    {
        var result = _tools.CreateDocument("invalid", double.NaN, 100);

        Assert.Equal("INVALID_DIMENSIONS", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void CreateDocument_SavesDocumentToStore()
    {
        var result = _tools.CreateDocument("test-doc", 800, 600);
        var data = ParseData(result);
        var id = Guid.Parse(data.GetProperty("id").GetString()!);

        var loaded = _store.Load(id);

        Assert.NotNull(loaded);
        Assert.Equal("test-doc", loaded.Name);
        Assert.Equal(800, loaded.Canvas.Width);
        Assert.Equal(600, loaded.Canvas.Height);
    }

    [Theory]
    [InlineData("web-icon-32", 32, 32, null)]
    [InlineData("web-icon-24", 24, 24, null)]
    [InlineData("social-1200x630", 1200, 630, "#ffffff")]
    public void CreateDocument_WithTemplate_SeedsCanvasFromCatalog(string templateId, double width, double height, string? background)
    {
        var result = _tools.CreateDocument("templated", template: templateId);
        var data = ParseData(result);

        Assert.Equal(templateId, data.GetProperty("template").GetString());
        Assert.Equal(width, data.GetProperty("canvas").GetProperty("width").GetDouble());
        Assert.Equal(height, data.GetProperty("canvas").GetProperty("height").GetDouble());

        var loaded = _store.Load(Guid.Parse(data.GetProperty("id").GetString()!));
        Assert.NotNull(loaded);
        Assert.Equal(width, loaded.Canvas.Width);
        Assert.Equal(height, loaded.Canvas.Height);
        Assert.Equal(background, loaded.Canvas.Background);
        Assert.Equal(width, loaded.Root.Width);
        Assert.Equal(height, loaded.Root.Height);
    }

    [Fact]
    public void CreateDocument_WithTemplate_ExplicitValuesOverrideTemplate()
    {
        var result = _tools.CreateDocument("override", width: 600, background: "#000000", template: "social-1200x630");
        var data = ParseData(result);

        var loaded = _store.Load(Guid.Parse(data.GetProperty("id").GetString()!));
        Assert.NotNull(loaded);
        Assert.Equal(600, loaded.Canvas.Width);
        Assert.Equal(630, loaded.Canvas.Height);
        Assert.Equal("#000000", loaded.Canvas.Background);
    }

    [Fact]
    public void CreateDocument_UnknownTemplate_ReturnsInvalidArgumentsListingValidIds()
    {
        var result = _tools.CreateDocument("bad-template", template: "poster");
        var error = ParseError(result);

        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        var message = error.GetProperty("message").GetString()!;
        Assert.Contains("'poster'", message);
        foreach (var template in DocumentTemplates.All)
        {
            Assert.Contains(template.Id, message);
        }
        Assert.Empty(((IDocumentStore)_store).List());
    }

    [Fact]
    public void CreateDocument_DeterministicRepeat_DoesNotEraseEdits()
    {
        var first = _tools.CreateDocument("stable", 800, 600, seed: 42);
        var id = ParseData(first).GetProperty("id").GetString()!;
        ParseData(_tools.AddNode(id, "root", "rectangle", name: "keep-me"));

        var repeated = _tools.CreateDocument("stable", 800, 600, seed: 42);

        Assert.Equal("DOCUMENT_EXISTS", ParseError(repeated).GetProperty("code").GetString());
        var nodes = ParseData(_tools.ListNodes(id)).GetProperty("nodes").EnumerateArray().ToList();
        Assert.Contains(nodes, node => node.GetProperty("name").GetString() == "keep-me");
    }

    [Fact]
    public void CreateDocument_Force_ExplicitlyOverwrites()
    {
        var first = _tools.CreateDocument("stable", 800, 600, seed: 43);
        var id = ParseData(first).GetProperty("id").GetString()!;
        ParseData(_tools.AddNode(id, "root", "rectangle"));

        ParseData(_tools.CreateDocument("stable", 800, 600, seed: 43, force: true));

        var nodes = ParseData(_tools.ListNodes(id)).GetProperty("nodes").EnumerateArray().ToList();
        Assert.Single(nodes);
    }

    [Fact]
    public void LoadDocument_ReturnsDocumentInfo()
    {
        var doc = CreateTestDocument("load-test", 1920, 1080);
        _store.Save(doc);

        var result = _tools.LoadDocument(doc.Id.ToString());
        var data = ParseData(result);

        Assert.Equal(doc.Id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal("load-test", data.GetProperty("name").GetString());
        Assert.Equal(1920, data.GetProperty("canvas").GetProperty("width").GetDouble());
    }

    [Fact]
    public void LoadDocument_ByFilePath_Works()
    {
        var doc = CreateTestDocument("file-load-test", 800, 600);
        var filePath = Path.Combine(_testDir, "test-file.agd.json");
        File.WriteAllText(filePath, DocumentSerializer.Serialize(doc));

        var result = _tools.LoadDocument(filePath);
        var data = ParseData(result);

        Assert.Equal("file-load-test", data.GetProperty("name").GetString());
    }

    [Fact]
    public void LoadDocument_NotFound_ReturnsError()
    {
        var result = _tools.LoadDocument(Guid.NewGuid().ToString());
        var error = ParseError(result);

        Assert.Equal("DOCUMENT_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public void LoadDocument_OversizedInput_ReturnsError()
    {
        var path = Path.Combine(_testDir, "oversized.agd.json");
        using (var stream = File.Create(path))
        {
            stream.SetLength(16 * 1024 * 1024 + 1);
        }

        var result = _tools.LoadDocument("oversized.agd.json");

        Assert.Equal("INPUT_TOO_LARGE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void ValidateDocument_ValidDocument_ReturnsValid()
    {
        var doc = CreateTestDocument("valid-doc", 800, 600);
        _store.Save(doc);

        var result = _tools.ValidateDocument(doc.Id.ToString());
        var data = ParseData(result);

        Assert.True(data.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void ValidateDocument_ProducesSameResultAsCore()
    {
        var doc = CreateTestDocument("core-compare", 800, 600);
        _store.Save(doc);

        var mcpResult = _tools.ValidateDocument(doc.Id.ToString());
        var coreResult = DocumentValidator.Validate(doc);
        var data = ParseData(mcpResult);

        Assert.Equal(coreResult.IsValid, data.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void ValidateDocument_WebIconProfileMatchesCoreDiagnostics()
    {
        var doc = CreateTestDocument("profile-compare", 800, 600);
        doc.Root.Children.Add(new TextNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Text = "label"
        });
        _store.Save(doc);

        var mcpResult = _tools.ValidateDocument(doc.Id.ToString(), profile: "web-icon");
        var coreResult = DocumentValidator.Validate(doc, ValidationOptions.WebIcon);
        var data = ParseData(mcpResult);

        Assert.Equal(coreResult.IsValid, data.GetProperty("valid").GetBoolean());
        var error = Assert.Single(data.GetProperty("errors").EnumerateArray());
        Assert.Equal("WEBICON002", error.GetProperty("code").GetString());
        Assert.Equal("root.children[0].type", error.GetProperty("path").GetString());
    }

    [Fact]
    public void ValidateDocument_UnknownProfileReturnsInvalidArguments()
    {
        var doc = CreateTestDocument("unknown-profile", 800, 600);
        _store.Save(doc);

        var result = _tools.ValidateDocument(doc.Id.ToString(), profile: "unknown");

        Assert.Equal("INVALID_ARGUMENTS", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void ListNodes_ReturnsAllNodes()
    {
        var doc = CreateTestDocument("list-test", 800, 600);
        doc.Root.Children.Add(new RectangleNode { Id = Guid.NewGuid(), Name = "rect1", Width = 100, Height = 50 });
        doc.Root.Children.Add(new EllipseNode { Id = Guid.NewGuid(), Name = "circle1", Width = 80, Height = 80 });
        _store.Save(doc);

        var result = _tools.ListNodes(doc.Id.ToString());
        var data = ParseData(result);

        var nodes = data.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void AddNode_AddsNodeToDocument()
    {
        var doc = CreateTestDocument("add-test", 800, 600);
        _store.Save(doc);

        var result = _tools.AddNode(
            doc.Id.ToString(),
            "root",
            "rectangle",
            name: "new-rect",
            width: 100,
            height: 50,
            x: 10,
            y: 20,
            fill: "#ff0000");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("nodeId", out _));

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated);
        Assert.Single(updated.Root.Children);
        Assert.Equal("new-rect", updated.Root.Children[0].Name);
    }

    [Fact]
    public void AddNode_IncrementsRevision()
    {
        var doc = CreateTestDocument("revn-test", 800, 600);
        var originalRevn = doc.Revn;
        _store.Save(doc);

        _tools.AddNode(doc.Id.ToString(), "root", "rectangle");

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated);
        Assert.Equal(originalRevn + 1, updated.Revn);
    }

    [Fact]
    public void AddNode_TokenReferences_ArePersisted()
    {
        var doc = CreateTestDocument("add-token-refs", 800, 600);
        doc.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string> { ["primary"] = "#123456" },
            Typography = new Dictionary<string, TypographyToken>
            {
                ["body"] = new() { FontFamily = "Inter", FontSize = 18 }
            }
        };
        _store.Save(doc);

        var result = _tools.AddNode(
            doc.Id.ToString(), "root", "text", text: "Hello",
            fillToken: "primary", strokeToken: "{primary}", typographyToken: "body");

        ParseData(result);
        var updated = _store.Load(doc.Id);
        var text = Assert.IsType<TextNode>(Assert.Single(updated!.Root.Children));
        Assert.Equal("primary", text.Style!.Fill!.TokenRef);
        Assert.Equal("{primary}", text.Style.Stroke!.TokenRef);
        Assert.Equal("body", text.TypographyTokenRef);
    }

    [Fact]
    public void AddNode_ConflictingLiteralAndTokenReference_ReturnsErrorWithoutPersisting()
    {
        var doc = CreateTestDocument("add-token-conflict", 800, 600);
        doc.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string> { ["primary"] = "#123456" }
        };
        _store.Save(doc);

        var result = _tools.AddNode(
            doc.Id.ToString(), "root", "rectangle", fill: "#ffffff", fillToken: "primary");

        Assert.Equal(EngineErrors.InvalidProperty, ParseError(result).GetProperty("code").GetString());
        Assert.Empty(_store.Load(doc.Id)!.Root.Children);
    }

    [Fact]
    public void UpdateNode_UpdatesProperties()
    {
        var doc = CreateTestDocument("update-test", 800, 600);
        var rect = new RectangleNode { Id = Guid.NewGuid(), Name = "rect", Width = 100, Height = 50 };
        doc.Root.Children.Add(rect);
        _store.Save(doc);

        _tools.UpdateNode(doc.Id.ToString(), rect.Id.ToString(), name: "updated-rect", width: 200);

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated);
        var updatedRect = updated.Root.Children[0] as RectangleNode;
        Assert.NotNull(updatedRect);
        Assert.Equal("updated-rect", updatedRect.Name);
        Assert.Equal(200, updatedRect.Width);
    }

    [Fact]
    public void UpdateNode_TokenReferences_ArePersisted()
    {
        var doc = CreateTestDocument("update-token-refs", 800, 600);
        doc.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string> { ["primary"] = "#123456" },
            Typography = new Dictionary<string, TypographyToken>
            {
                ["body"] = new() { FontFamily = "Inter" }
            }
        };
        var text = new TextNode { Id = Guid.NewGuid(), Text = "Hello" };
        doc.Root.Children.Add(text);
        _store.Save(doc);

        var result = _tools.UpdateNode(
            doc.Id.ToString(), text.Id.ToString(), fillToken: "{primary}",
            strokeToken: "primary", typographyToken: "{body}");

        ParseData(result);
        var updated = Assert.IsType<TextNode>(Assert.Single(_store.Load(doc.Id)!.Root.Children));
        Assert.Equal("{primary}", updated.Style!.Fill!.TokenRef);
        Assert.Equal("primary", updated.Style.Stroke!.TokenRef);
        Assert.Equal("{body}", updated.TypographyTokenRef);

        ParseData(_tools.UpdateNode(
            doc.Id.ToString(), text.Id.ToString(), typographyToken: PropertyClear.ClearValue));
        updated = Assert.IsType<TextNode>(Assert.Single(_store.Load(doc.Id)!.Root.Children));
        Assert.Null(updated.TypographyTokenRef);
    }

    [Fact]
    public void DeleteNode_RemovesNode()
    {
        var doc = CreateTestDocument("delete-test", 800, 600);
        var rect = new RectangleNode { Id = Guid.NewGuid(), Name = "to-delete", Width = 100, Height = 50 };
        doc.Root.Children.Add(rect);
        _store.Save(doc);

        _tools.DeleteNode(doc.Id.ToString(), rect.Id.ToString());

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated);
        Assert.Empty(updated.Root.Children);
    }

    [Fact]
    public void DeleteNode_CannotDeleteRoot_ReturnsError()
    {
        var doc = CreateTestDocument("delete-root-test", 800, 600);
        _store.Save(doc);

        var result = _tools.DeleteNode(doc.Id.ToString(), doc.Root.Id.ToString());
        var error = ParseError(result);

        Assert.Equal("CANNOT_DELETE_ROOT", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportDocument_CreatesSvgFile()
    {
        var doc = CreateTestDocument("export-test", 800, 600);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 100,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });
        _store.Save(doc);

        var outputPath = Path.Combine(_testDir, "output.svg");
        await _tools.ExportDocument(doc.Id.ToString(), outputPath);

        Assert.True(File.Exists(outputPath));
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<svg", svg);
        Assert.Contains("<rect", svg);
    }

    [Fact]
    public async Task ExportDocument_ProducesSameOutputAsAdapter()
    {
        var doc = CreateTestDocument("export-compare", 800, 600);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 100,
            Height = 50
        });
        _store.Save(doc);

        var mcpOutputPath = Path.Combine(_testDir, "mcp-output.svg");
        await _tools.ExportDocument(doc.Id.ToString(), mcpOutputPath);

        var adapter = new SvgExportAdapter();
        var coreResult = await adapter.ExportAsync(doc, new ExportOptions());

        var mcpSvg = await File.ReadAllTextAsync(mcpOutputPath);
        var coreSvg = System.Text.Encoding.UTF8.GetString(coreResult.Data);

        Assert.Equal(coreSvg, mcpSvg);
    }

    [Fact]
    public async Task ExportDocument_StrictInvalidDocument_ReturnsValidationErrorWithoutWritingOutput()
    {
        var doc = CreateTestDocument("strict-invalid", 800, 600);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Width = 10,
            Height = 10,
            Style = new Style { Fill = new Fill { Color = "not-a-color" } }
        });
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "strict-invalid.svg");

        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath, strict: true);

        var error = ParseError(result);
        Assert.Equal("STYLE002", error.GetProperty("code").GetString());
        Assert.Equal("root.children[0].style.fill.color", error.GetProperty("path").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_StrictValidDocument_WritesOutput()
    {
        var doc = CreateTestDocument("strict-valid", 800, 600);
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "strict-valid.svg");

        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath, strict: true);

        ParseData(result);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_WebIconProfileRejectsTextWithoutStrictAndWritesNoOutput()
    {
        var doc = CreateTestDocument("profile-invalid", 800, 600);
        doc.Root.Children.Add(new TextNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Text = "label"
        });
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "profile-invalid.svg");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, profile: "web-icon");

        var error = ParseError(result);
        Assert.Equal("WEBICON002", error.GetProperty("code").GetString());
        Assert.Equal("root.children[0].type", error.GetProperty("path").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_WebIconProfileAllowsWarningsUnlessStrict()
    {
        var doc = CreateTestDocument("profile-warning", 800, 600);
        doc.Root.Children.Add(new PathNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            D = string.Empty
        });
        _store.Save(doc);
        var profileOutput = Path.Combine(_testDir, "profile-warning.svg");
        var strictOutput = Path.Combine(_testDir, "profile-strict-warning.svg");

        var profileResult = await _tools.ExportDocument(
            doc.Id.ToString(), profileOutput, profile: "web-icon");
        var strictResult = await _tools.ExportDocument(
            doc.Id.ToString(), strictOutput, strict: true, profile: "web-icon");

        ParseData(profileResult);
        Assert.True(File.Exists(profileOutput));
        Assert.Equal("PATH001", ParseError(strictResult).GetProperty("code").GetString());
        Assert.False(File.Exists(strictOutput));
    }

    [Fact]
    public async Task ExportDocument_WebIconProfileRejectsOpaqueBackgroundOverride()
    {
        var doc = CreateTestDocument("profile-background", 32, 32);
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "profile-background.svg");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, background: "#ffffff", profile: "web-icon");

        var error = ParseError(result);
        Assert.Equal("WEBICON001", error.GetProperty("code").GetString());
        Assert.Equal("export.background", error.GetProperty("path").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_UnknownProfileReturnsInvalidArgumentsAndWritesNoOutput()
    {
        var doc = CreateTestDocument("unknown-export-profile", 800, 600);
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "unknown-profile.svg");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, profile: "unknown");

        Assert.Equal("INVALID_ARGUMENTS", ParseError(result).GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_UnsafeImageReference_ReturnsStructuredError()
    {
        var doc = CreateTestDocument("unsafe-image", 100, 100);
        doc.Root.Children.Add(new ImageNode
        {
            Id = Guid.NewGuid(),
            Name = "unsafe",
            Width = 10,
            Height = 10,
            Src = "data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg'/>",
        });
        _store.Save(doc);

        var outputPath = Path.Combine(_testDir, "unsafe.svg");
        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath);
        var error = ParseError(result);

        Assert.Equal("EXPORT_FAILED", error.GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void GetDocumentJson_ReturnsSerializedDocument()
    {
        var doc = CreateTestDocument("json-test", 800, 600);
        _store.Save(doc);

        var result = _tools.GetDocumentJson(doc.Id.ToString());
        var data = ParseData(result);

        var json = data.GetProperty("json").GetString();
        Assert.NotNull(json);

        var coreJson = DocumentSerializer.Serialize(doc);
        Assert.Equal(coreJson, json);
    }

    [Fact]
    public void EndToEnd_CreateEditValidateExport_Works()
    {
        var createResult = _tools.CreateDocument("e2e-test", 800, 600, "#ffffff");
        var createData = ParseData(createResult);
        var docId = createData.GetProperty("id").GetString()!;

        _tools.AddNode(docId, "root", "rectangle", name: "box", width: 100, height: 100, x: 50, y: 50, fill: "#ff0000");
        _tools.AddNode(docId, "root", "text", name: "label", text: "Hello MCP", x: 50, y: 200);

        var validateResult = _tools.ValidateDocument(docId);
        var validateData = ParseData(validateResult);
        Assert.True(validateData.GetProperty("valid").GetBoolean());

        var listResult = _tools.ListNodes(docId);
        var listData = ParseData(listResult);
        var nodes = listData.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void AddNode_InvalidParentId_ReturnsError()
    {
        var doc = CreateTestDocument("invalid-parent-test", 800, 600);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "not-a-guid", "rectangle");
        var error = ParseError(result);

        Assert.Equal("PARENT_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public void UpdateNode_InvalidNodeId_ReturnsError()
    {
        var doc = CreateTestDocument("invalid-node-test", 800, 600);
        _store.Save(doc);

        var result = _tools.UpdateNode(doc.Id.ToString(), "not-a-guid", name: "new-name");
        var error = ParseError(result);

        Assert.Equal("NODE_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public void DeleteNode_InvalidNodeId_ReturnsError()
    {
        var doc = CreateTestDocument("invalid-delete-test", 800, 600);
        _store.Save(doc);

        var result = _tools.DeleteNode(doc.Id.ToString(), "not-a-guid");
        var error = ParseError(result);

        Assert.Equal("NODE_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public void LoadDocument_PathEscape_ReturnsError()
    {
        var result = _tools.LoadDocument("../../../etc/passwd");
        var error = ParseError(result);

        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void LoadDocument_AbsolutePathOutside_ReturnsError()
    {
        var result = _tools.LoadDocument("/etc/passwd");
        var error = ParseError(result);

        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void LoadDocument_RelativePathWithin_Works()
    {
        var doc = CreateTestDocument("relative-test", 800, 600);
        var filePath = Path.Combine(_testDir, "relative-test.agd.json");
        File.WriteAllText(filePath, DocumentSerializer.Serialize(doc));

        var result = _tools.LoadDocument("relative-test.agd.json");
        var data = ParseData(result);

        Assert.Equal("relative-test", data.GetProperty("name").GetString());
    }

    [Fact]
    public void LoadDocument_GuidSymlink_IsRejectedAndNotListed()
    {
        var outsideDir = Path.Combine(Path.GetTempPath(), $"agd-mcp-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        var id = Guid.NewGuid();
        var outsidePath = Path.Combine(outsideDir, "outside.agd.json");
        File.WriteAllText(outsidePath, DocumentSerializer.Serialize(CreateTestDocument("outside-secret", 10, 10)));
        var linkPath = Path.Combine(_testDir, $"{id:D}.agd.json");

        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, outsidePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            Assert.Equal("PATH_ESCAPE", ParseError(_tools.LoadDocument(id.ToString())).GetProperty("code").GetString());
            var listResult = _tools.ListDocuments();
            var serialized = ((TextContentBlock)listResult.Content.Single()).Text;
            Assert.DoesNotContain("outside-secret", serialized);
            Assert.DoesNotContain(outsideDir, serialized);
        }
        finally
        {
            if (File.Exists(linkPath)) File.Delete(linkPath);
            Directory.Delete(outsideDir, true);
        }
    }

    [Fact]
    public async Task ExportDocument_OutputPathEscape_ReturnsError()
    {
        var doc = CreateTestDocument("export-escape-test", 800, 600);
        _store.Save(doc);

        var result = await _tools.ExportDocument(doc.Id.ToString(), "/tmp/escape/output.svg");
        var error = ParseError(result);

        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportDocument_OutputPathWithin_Works()
    {
        var doc = CreateTestDocument("export-within-test", 800, 600);
        _store.Save(doc);

        var outputPath = "output/test.svg";
        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath);
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("path", out var path));
        Assert.Equal("output/test.svg", path.GetString());
    }

    [Fact]
    public async Task ExportDocument_ExistingOutputSymlink_IsRejectedWithoutChangingTarget()
    {
        var doc = CreateTestDocument("export-link-test", 32, 32);
        _store.Save(doc);
        var outsideDirectory = Path.Combine(Path.GetTempPath(), $"agd-mcp-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);
        var outsidePath = Path.Combine(outsideDirectory, "target.svg");
        var linkPath = Path.Combine(_testDir, "output-link.svg");
        await File.WriteAllTextAsync(outsidePath, "sentinel");

        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, outsidePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            var result = await _tools.ExportDocument(doc.Id.ToString(), "output-link.svg");

            Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
            Assert.Equal("sentinel", await File.ReadAllTextAsync(outsidePath));
        }
        finally
        {
            if (File.Exists(linkPath)) File.Delete(linkPath);
            Directory.Delete(outsideDirectory, true);
        }
    }

    [Fact]
    public async Task ExportDocument_RasterRejectsScaleAboveMaximum()
    {
        var doc = CreateTestDocument("export-clamp-test", 100, 100);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });
        _store.Save(doc);

        var outputPath = Path.Combine(_testDir, "clamped.png");
        var result = await _tools.ExportDocument(
            doc.Id.ToString(),
            outputPath,
            format: "png",
            scale: 10.0);

        var error = ParseError(result);
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    // ---- Font options ----

    [Fact]
    public async Task ExportDocument_FontDirsOutsideWorkingDirectory_ReturnsPathEscape()
    {
        var doc = CreateTextDocument("export-font-escape", "Inter");
        var outputPath = Path.Combine(_testDir, "font-escape.png");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, format: "png", fontDirs: "fonts,../outside");

        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_FontDirsSymlink_ReturnsPathEscape()
    {
        var doc = CreateTextDocument("export-font-symlink", "Inter");
        var outsideDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"agd-mcp-fonts-{Guid.NewGuid():N}")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(_testDir, "linked-fonts"), outsideDirectory);
        try
        {
            var result = await _tools.ExportDocument(
                doc.Id.ToString(), "font-symlink.png", format: "png", fontDirs: "linked-fonts");

            Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportDocument_MissingFontDir_ReturnsInvalidArguments()
    {
        var doc = CreateTextDocument("export-font-missing-dir", "Inter");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), "font-missing.png", format: "png", fontDirs: "no-such-fonts");

        var error = ParseError(result);
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Contains("no-such-fonts", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("fonts", true, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    public async Task ExportDocument_FontOptionsWithSvg_ReturnsInvalidArguments(string? fontDirs, bool systemFonts, bool strictFonts)
    {
        var doc = CreateTextDocument("export-font-svg", "Inter");
        Directory.CreateDirectory(Path.Combine(_testDir, "fonts"));
        var outputPath = Path.Combine(_testDir, "font-options.svg");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, format: "svg", fontDirs: fontDirs, systemFonts: systemFonts, strictFonts: strictFonts);

        Assert.Equal("INVALID_ARGUMENTS", ParseError(result).GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_MissingFontFamily_SubstitutesAndReportsDiagnostic()
    {
        var doc = CreateTextDocument("export-font-substituted", "Nonexistent Family");
        var outputPath = Path.Combine(_testDir, "substituted.png");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, format: "png", systemFonts: false);

        var data = ParseData(result);
        var diagnostic = Assert.Single(data.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("FONT_SUBSTITUTED", diagnostic.GetProperty("code").GetString());
        Assert.Contains("Nonexistent Family", diagnostic.GetProperty("message").GetString());
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_MissingFontFamily_StrictFonts_ReturnsFontNotFoundWithoutWritingOutput()
    {
        var doc = CreateTextDocument("export-font-strict", "Nonexistent Family");
        var outputPath = Path.Combine(_testDir, "strict.png");

        var result = await _tools.ExportDocument(
            doc.Id.ToString(), outputPath, format: "png", systemFonts: false, strictFonts: true);

        var error = ParseError(result);
        Assert.Equal("FONT_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Contains("Nonexistent Family", error.GetProperty("message").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDocument_FontDirWithinWorkingDirectory_IsUsed()
    {
        var doc = CreateTextDocument("export-font-dir", "Inter", fontWeight: 400);
        var fontDirectory = Directory.CreateDirectory(Path.Combine(_testDir, "fonts")).FullName;
        using (var resource = typeof(BundledFonts).Assembly.GetManifestResourceStream(BundledFonts.BoldResourceName)!)
        using (var file = File.Create(Path.Combine(fontDirectory, "only-bold.ttf")))
        {
            resource.CopyTo(file);
        }

        var bundled = await _tools.ExportDocument(
            doc.Id.ToString(), "bundled.png", format: "png", systemFonts: false, strictFonts: true);
        var fromDirectory = await _tools.ExportDocument(
            doc.Id.ToString(), "directory.png", format: "png", fontDirs: " fonts ", systemFonts: false, strictFonts: true);

        Assert.Empty(ParseData(bundled).GetProperty("diagnostics").EnumerateArray());
        Assert.Empty(ParseData(fromDirectory).GetProperty("diagnostics").EnumerateArray());
        Assert.NotEqual(
            File.ReadAllBytes(Path.Combine(_testDir, "bundled.png")),
            File.ReadAllBytes(Path.Combine(_testDir, "directory.png")));
    }

    [Fact]
    public void RenderPreview_FontDirsOutsideWorkingDirectory_ReturnsPathEscape()
    {
        var doc = CreateTextDocument("preview-font-escape", "Inter");

        var result = _tools.RenderPreview(doc.Id.ToString(), fontDirs: "/tmp");

        Assert.Single(result.Content);
        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void RenderPreview_MissingFontFamily_StrictFonts_ReturnsFontNotFound()
    {
        var doc = CreateTextDocument("preview-font-strict", "Nonexistent Family");

        var result = _tools.RenderPreview(doc.Id.ToString(), systemFonts: false, strictFonts: true);

        Assert.Single(result.Content);
        Assert.Equal("FONT_NOT_FOUND", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void RenderPreview_MissingFontFamily_ReportsDiagnosticAndImage()
    {
        var doc = CreateTextDocument("preview-font-diagnostic", "Nonexistent Family");

        var result = _tools.RenderPreview(doc.Id.ToString(), systemFonts: false);

        Assert.Equal(2, result.Content.Count);
        Assert.IsType<ImageContentBlock>(result.Content[0]);
        var diagnostic = Assert.Single(ParseData(result).GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("FONT_SUBSTITUTED", diagnostic.GetProperty("code").GetString());
    }

    private Document CreateTextDocument(string name, string fontFamily, int fontWeight = 400)
    {
        var doc = CreateTestDocument(name, 200, 100);
        doc.Root.Children.Add(new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "label",
            Text = "Agent Graphic Design",
            FontFamily = fontFamily,
            FontSize = 24,
            FontWeight = fontWeight,
            Transform = new Transform { X = 10, Y = 40 },
            Style = new Style { Fill = new Fill { Color = "#222222" } }
        });
        _store.Save(doc);
        return doc;
    }

    [Fact]
    public void AddNode_PathEscape_ReturnsError()
    {
        var result = _tools.AddNode("../../../etc/shadow", "root", "rectangle");
        var error = ParseError(result);

        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void AddNode_ImageSourcePathEscape_ReturnsError()
    {
        var doc = CreateTestDocument("image-path", 100, 100);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "root", "image", imageSrc: "../../../etc/passwd");

        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void ValidateDocument_PathEscape_ReturnsError()
    {
        var result = _tools.ValidateDocument("../../../etc/passwd");
        var error = ParseError(result);

        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void AddNode_TypeAlias_Works()
    {
        var doc = CreateTestDocument("alias-test", 800, 600);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "root", "rect");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("nodeId", out _));
    }

    [Fact]
    public void AddNode_InvalidType_ReturnsError()
    {
        var doc = CreateTestDocument("bad-type-test", 800, 600);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "root", "hexagon");
        var error = ParseError(result);

        Assert.Equal("INVALID_NODE_TYPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void AddNode_WithCornerRadius_SetsProperty()
    {
        var doc = CreateTestDocument("corner-test", 800, 600);
        _store.Save(doc);

        _tools.AddNode(doc.Id.ToString(), "root", "rectangle", cornerRadius: 8);

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated);
        var rect = Assert.IsType<RectangleNode>(updated.Root.Children[0]);
        Assert.Equal(8, rect.CornerRadius);
    }

    [Fact]
    public void AddNode_WithFontProperties_SetsTextNode()
    {
        var doc = CreateTestDocument("font-test", 800, 600);
        _store.Save(doc);

        _tools.AddNode(doc.Id.ToString(), "root", "text", text: "Hello", fontFamily: "Arial", fontSize: 24, fontWeight: 700);

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated);
        var text = Assert.IsType<TextNode>(updated.Root.Children[0]);
        Assert.Equal("Hello", text.Text);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal(24, text.FontSize);
        Assert.Equal(700, text.FontWeight);
    }

    [Fact]
    public void AddNode_WithTextLayout_SetsLayoutFields()
    {
        var doc = CreateTestDocument("layout-add", 800, 600);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "root", "text", text: "Hello wrapped world",
            textAlign: "Center", verticalAlign: "middle", maxWidth: 80, lineHeight: 1.5, letterSpacing: 0.5);

        Assert.True(ParseData(result).TryGetProperty("nodeId", out _));
        var text = Assert.IsType<TextNode>(_store.Load(doc.Id)!.Root.Children[0]);
        Assert.Equal("center", text.TextAlign);
        Assert.Equal("middle", text.VerticalAlign);
        Assert.Equal(80, text.MaxWidth);
        Assert.Equal(1.5, text.LineHeight);
        Assert.Equal(0.5, text.LetterSpacing);
    }

    [Fact]
    public void AddNode_InvalidTextLayout_ReturnsErrorWithoutSaving()
    {
        var doc = CreateTestDocument("layout-invalid", 800, 600);
        _store.Save(doc);

        var numeric = _tools.AddNode(doc.Id.ToString(), "root", "text", text: "x", maxWidth: 0);
        var align = _tools.AddNode(doc.Id.ToString(), "root", "text", text: "x", textAlign: "justify");

        Assert.Equal("INVALID_PROPERTY", ParseError(numeric).GetProperty("code").GetString());
        Assert.Equal("INVALID_PROPERTY", ParseError(align).GetProperty("code").GetString());
        Assert.Empty(_store.Load(doc.Id)!.Root.Children);
    }

    [Fact]
    public void UpdateNode_TextLayout_UpdatesAndClears()
    {
        var doc = CreateTestDocument("layout-update", 800, 600);
        doc.Root.Children.Add(new TextNode { Id = Guid.NewGuid(), Name = "t", Text = "hi" });
        _store.Save(doc);
        var nodeId = doc.Root.Children[0].Id.ToString();

        _tools.UpdateNode(doc.Id.ToString(), nodeId, textAlign: "end", verticalAlign: "bottom", maxWidth: 120, lineHeight: 2, letterSpacing: 1);
        var text = Assert.IsType<TextNode>(_store.Load(doc.Id)!.Root.Children[0]);
        Assert.Equal("end", text.TextAlign);
        Assert.Equal("bottom", text.VerticalAlign);
        Assert.Equal(120, text.MaxWidth);
        Assert.Equal(2, text.LineHeight);
        Assert.Equal(1, text.LetterSpacing);

        _tools.UpdateNode(doc.Id.ToString(), nodeId, textAlign: PropertyClear.ClearValue, verticalAlign: PropertyClear.ClearValue);
        text = Assert.IsType<TextNode>(_store.Load(doc.Id)!.Root.Children[0]);
        Assert.Null(text.TextAlign);
        Assert.Null(text.VerticalAlign);
        Assert.Equal(120, text.MaxWidth);

        var invalid = _tools.UpdateNode(doc.Id.ToString(), nodeId, lineHeight: -1);
        Assert.Equal("INVALID_PROPERTY", ParseError(invalid).GetProperty("code").GetString());
    }

    [Fact]
    public void UpdateNode_Text_UpdatesContent()
    {
        var doc = CreateTestDocument("update-text", 800, 600);
        doc.Root.Children.Add(new TextNode { Id = Guid.NewGuid(), Name = "t", Text = "original" });
        _store.Save(doc);
        var nodeId = doc.Root.Children[0].Id.ToString();

        _tools.UpdateNode(doc.Id.ToString(), nodeId, text: "updated");

        var updated = _store.Load(doc.Id);
        var textNode = Assert.IsType<TextNode>(updated!.Root.Children[0]);
        Assert.Equal("updated", textNode.Text);
    }

    [Fact]
    public void UpdateNode_ClearFill_RemovesFill()
    {
        var doc = CreateTestDocument("clear-fill", 800, 600);
        var rect = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "r",
            Width = 100,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        };
        doc.Root.Children.Add(rect);
        _store.Save(doc);

        _tools.UpdateNode(doc.Id.ToString(), rect.Id.ToString(), fill: "__clear__");

        var updated = _store.Load(doc.Id);
        var updatedRect = updated!.Root.Children[0];
        Assert.Null(updatedRect.Style?.Fill);
    }

    [Fact]
    public void UpdateNode_Opacity_SetsStyleOpacity()
    {
        var doc = CreateTestDocument("opacity-test", 800, 600);
        doc.Root.Children.Add(new RectangleNode { Id = Guid.NewGuid(), Name = "r", Width = 50, Height = 50 });
        _store.Save(doc);
        var nodeId = doc.Root.Children[0].Id.ToString();

        _tools.UpdateNode(doc.Id.ToString(), nodeId, opacity: 0.5);

        var updated = _store.Load(doc.Id);
        Assert.Equal(0.5, updated!.Root.Children[0].Style?.Opacity);
    }

    [Fact]
    public void MoveNode_ToNewParent_MovesNode()
    {
        var doc = CreateTestDocument("move-test", 800, 600);
        var rect = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "mover",
            Width = 50,
            Height = 50,
            Order = "a0"
        };
        var group = new GroupNode { Id = Guid.NewGuid(), Name = "target", Order = "a1", Children = [] };
        doc.Root.Children.Add(rect);
        doc.Root.Children.Add(group);
        _store.Save(doc);

        var result = _tools.MoveNode(doc.Id.ToString(), rect.Id.ToString(), group.Id.ToString());
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("nodeId", out _));
        Assert.True(data.TryGetProperty("newOrder", out _));

        var updated = _store.Load(doc.Id);
        Assert.Single(updated!.Root.Children); // only group at root
        var updatedGroup = Assert.IsType<GroupNode>(updated.Root.Children[0]);
        Assert.Single(updatedGroup.Children); // rect moved here
    }

    [Fact]
    public void MoveNode_DuplicateSiblingOrders_ReturnsErrorWithoutWeakeningInvariant()
    {
        var doc = CreateTestDocument("move-invalid-order", 800, 600);
        var rect = new RectangleNode { Id = Guid.NewGuid(), Name = "mover", Width = 50, Height = 50 };
        var group = new GroupNode { Id = Guid.NewGuid(), Name = "target", Children = [] };
        doc.Root.Children.Add(rect);
        doc.Root.Children.Add(group);
        _store.Save(doc);

        var result = _tools.MoveNode(doc.Id.ToString(), rect.Id.ToString(), group.Id.ToString());

        Assert.Equal(EngineErrors.InvalidProperty, ParseError(result).GetProperty("code").GetString());
        var unchanged = _store.Load(doc.Id)!;
        Assert.Equal(2, unchanged.Root.Children.Count);
        Assert.Empty(Assert.IsType<GroupNode>(unchanged.Root.Children.Single(node => node.Id == group.Id)).Children);
    }

    [Fact]
    public void MoveNode_ToRoot_MovesNode()
    {
        var doc = CreateTestDocument("move-root", 800, 600);
        var group = new GroupNode { Id = Guid.NewGuid(), Name = "g", Children = [] };
        var rect = new RectangleNode { Id = Guid.NewGuid(), Name = "r", Width = 50, Height = 50 };
        group.Children.Add(rect);
        doc.Root.Children.Add(group);
        _store.Save(doc);

        _tools.MoveNode(doc.Id.ToString(), rect.Id.ToString(), "root");

        var updated = _store.Load(doc.Id);
        Assert.Equal(2, updated!.Root.Children.Count);
    }

    [Fact]
    public void ReorderNode_PlacesBetween_Works()
    {
        var doc = CreateTestDocument("reorder-test", 800, 600);
        var a = new RectangleNode { Id = Guid.NewGuid(), Name = "a", Width = 50, Height = 50, Order = "a0" };
        var b = new RectangleNode { Id = Guid.NewGuid(), Name = "b", Width = 50, Height = 50, Order = "a1" };
        var c = new RectangleNode { Id = Guid.NewGuid(), Name = "c", Width = 50, Height = 50, Order = "a2" };
        doc.Root.Children.Add(a);
        doc.Root.Children.Add(b);
        doc.Root.Children.Add(c);
        _store.Save(doc);

        var result = _tools.ReorderNode(doc.Id.ToString(), c.Id.ToString(), beforeNodeId: b.Id.ToString(), afterNodeId: a.Id.ToString());
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("newOrder", out var newOrder));
        var order = newOrder.GetString()!;
        Assert.True(string.Compare(order, "a0", StringComparison.Ordinal) > 0);
        Assert.True(string.Compare(order, "a1", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void ReorderNode_Before_UsesConventionalMeaning()
    {
        var doc = CreateTestDocument("reorder-before", 100, 100);
        var a = new RectangleNode { Id = Guid.NewGuid(), Name = "a", Order = "a0" };
        var b = new RectangleNode { Id = Guid.NewGuid(), Name = "b", Order = "a1" };
        doc.Root.Children.Add(a);
        doc.Root.Children.Add(b);
        _store.Save(doc);

        ParseData(_tools.ReorderNode(doc.Id.ToString(), b.Id.ToString(), beforeNodeId: a.Id.ToString()));

        var updated = _store.Load(doc.Id)!;
        Assert.True(string.Compare(updated.Root.Children.Single(node => node.Id == b.Id).Order,
            updated.Root.Children.Single(node => node.Id == a.Id).Order, StringComparison.Ordinal) < 0);
    }

    // ---- SetColorToken Tests ----

    [Fact]
    public void SetColorToken_AddsNewToken()
    {
        var doc = CreateTestDocument("set-token-new", 800, 600);
        _store.Save(doc);

        var result = _tools.SetColorToken(doc.Id.ToString(), "primary", "#0066cc");
        var data = ParseData(result);

        Assert.Equal("primary", data.GetProperty("name").GetString());
        Assert.Equal("#0066cc", data.GetProperty("color").GetString());

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated?.Tokens?.Colors);
        Assert.Equal("#0066cc", updated.Tokens.Colors["primary"]);
    }

    [Fact]
    public void SetColorToken_UpdatesExistingToken()
    {
        var doc = CreateTestDocument("set-token-update", 800, 600);
        _store.Save(doc);

        _tools.SetColorToken(doc.Id.ToString(), "primary", "#0066cc");
        var result = _tools.SetColorToken(doc.Id.ToString(), "primary", "#ff0000");
        var data = ParseData(result);

        Assert.Equal("#ff0000", data.GetProperty("color").GetString());

        var updated = _store.Load(doc.Id);
        Assert.Equal("#ff0000", updated!.Tokens!.Colors!["primary"]);
    }

    [Fact]
    public void SetColorToken_ExpectedRevisionConflict_ReturnsError()
    {
        var doc = CreateTestDocument("token-revision", 800, 600);
        _store.Save(doc);
        ParseData(_tools.SetColorToken(doc.Id.ToString(), "primary", "#0066cc", expectedRevn: doc.Revn));

        var result = _tools.SetColorToken(doc.Id.ToString(), "primary", "#ff0000", expectedRevn: doc.Revn);

        Assert.Equal("REVISION_CONFLICT", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void ListDocuments_Cancelled_ReturnsErrorResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = _tools.ListDocuments(cancellation.Token);

        Assert.Equal("CANCELLED", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void SetColorToken_InvalidColorValue_ReturnsErrorWithoutPersisting()
    {
        var doc = CreateTestDocument("set-token-invalid", 800, 600);
        _store.Save(doc);

        var result = _tools.SetColorToken(doc.Id.ToString(), "invalid", "not-a-color");
        var error = ParseError(result);

        Assert.Equal(EngineErrors.InvalidProperty, error.GetProperty("code").GetString());
        var updated = _store.Load(doc.Id);
        Assert.False(updated!.Tokens?.Colors?.ContainsKey("invalid") ?? false);
    }

    // ---- RemoveColorToken Tests ----

    [Fact]
    public void RemoveColorToken_RemovesExistingToken()
    {
        var doc = CreateTestDocument("remove-token-exists", 800, 600);
        _store.Save(doc);
        _tools.SetColorToken(doc.Id.ToString(), "primary", "#0066cc");

        var result = _tools.RemoveColorToken(doc.Id.ToString(), "primary");
        var data = ParseData(result);

        Assert.Equal("primary", data.GetProperty("name").GetString());

        var updated = _store.Load(doc.Id);
        Assert.False(updated!.Tokens?.Colors?.ContainsKey("primary") ?? false);
    }

    [Fact]
    public void RemoveColorToken_ReturnsError_WhenTokenNotFound()
    {
        var doc = CreateTestDocument("remove-token-missing", 800, 600);
        _store.Save(doc);

        var result = _tools.RemoveColorToken(doc.Id.ToString(), "nonexistent");
        var error = ParseError(result);

        Assert.Equal("TOKEN_NOT_FOUND", error.GetProperty("code").GetString());
    }

    // ---- ListColorTokens Tests ----

    [Fact]
    public void ListColorTokens_ReturnsTokensFromDocument()
    {
        var doc = CreateTestDocument("list-tokens-has", 800, 600);
        _store.Save(doc);
        _tools.SetColorToken(doc.Id.ToString(), "primary", "#0066cc");
        _tools.SetColorToken(doc.Id.ToString(), "secondary", "#ff0000");

        var result = _tools.ListColorTokens(doc.Id.ToString());
        var data = ParseData(result);

        Assert.Equal(2, data.GetProperty("count").GetInt32());
        var tokens = data.GetProperty("tokens").EnumerateArray().ToList();
        Assert.Equal(2, tokens.Count);
        Assert.Contains(tokens, t => t.GetProperty("name").GetString() == "primary");
        Assert.Contains(tokens, t => t.GetProperty("name").GetString() == "secondary");
    }

    [Fact]
    public void ListColorTokens_ReturnsEmptyList_WhenNoTokens()
    {
        var doc = CreateTestDocument("list-tokens-empty", 800, 600);
        _store.Save(doc);

        var result = _tools.ListColorTokens(doc.Id.ToString());
        var data = ParseData(result);

        Assert.Equal(0, data.GetProperty("count").GetInt32());
        var tokens = data.GetProperty("tokens").EnumerateArray().ToList();
        Assert.Empty(tokens);
    }

    // ---- ApplyPalette Tests ----

    [Fact]
    public void ApplyPalette_AppliesTailwindPalette()
    {
        var doc = CreateTestDocument("apply-tailwind", 800, 600);
        _store.Save(doc);

        var result = _tools.ApplyPalette(doc.Id.ToString(), "tailwind");
        var data = ParseData(result);

        Assert.Equal("tailwind", data.GetProperty("palette").GetString());
        Assert.True(data.GetProperty("colorCount").GetInt32() > 0);

        var updated = _store.Load(doc.Id);
        Assert.True(updated!.Tokens!.Colors!.ContainsKey("blue-500"));
        Assert.Equal("#3b82f6", updated.Tokens.Colors["blue-500"]);
    }

    [Fact]
    public void ApplyPalette_ReturnsError_WhenPaletteNotFound()
    {
        var doc = CreateTestDocument("apply-invalid", 800, 600);
        _store.Save(doc);

        var result = _tools.ApplyPalette(doc.Id.ToString(), "nonexistent-palette");
        var error = ParseError(result);

        Assert.Equal("PALETTE_NOT_FOUND", error.GetProperty("code").GetString());
    }

    // ---- ListPalettes Tests ----

    [Fact]
    public void ListPalettes_ReturnsAvailablePalettes()
    {
        var result = _tools.ListPalettes();
        var data = ParseData(result);

        Assert.Equal(3, data.GetProperty("count").GetInt32());
        var palettes = data.GetProperty("palettes").EnumerateArray().ToList();
        Assert.Contains(palettes, p => p.GetProperty("name").GetString() == "tailwind");
        Assert.Contains(palettes, p => p.GetProperty("name").GetString() == "material");
        Assert.Contains(palettes, p => p.GetProperty("name").GetString() == "colorbrewer-qualitative");
    }

    // ---- DTCG Token Import/Export Tests ----

    [Fact]
    public async Task ImportDtcgTokens_ValidJson_AddsTokensToDocument()
    {
        var doc = CreateTestDocument("dtcg-import-test", 800, 600);
        _store.Save(doc);

        var dtcgJson = """
            {
                "colors": {
                    "primary": { "$value": "#ff0000", "$type": "color" }
                }
            }
            """;
        var tokensPath = Path.Combine(_testDir, "tokens.json");
        File.WriteAllText(tokensPath, dtcgJson);

        var result = await _tools.ImportDtcgTokens(doc.Id.ToString(), "tokens.json");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("colorCount", out var colorCount));
        Assert.Equal(1, colorCount.GetInt32());

        var updated = _store.Load(doc.Id);
        Assert.NotNull(updated?.Tokens?.Colors);
        Assert.Contains("colors.primary", updated.Tokens.Colors.Keys);
        Assert.Equal("#ff0000", updated.Tokens.Colors["colors.primary"]);
    }

    [Fact]
    public async Task ImportDtcgTokens_InvalidJson_ReturnsError()
    {
        var doc = CreateTestDocument("dtcg-invalid-test", 800, 600);
        _store.Save(doc);

        var invalidJson = "{ not valid json }";
        var tokensPath = Path.Combine(_testDir, "invalid-tokens.json");
        File.WriteAllText(tokensPath, invalidJson);

        var result = await _tools.ImportDtcgTokens(doc.Id.ToString(), "invalid-tokens.json");
        var error = ParseError(result);

        Assert.Equal("INVALID_JSON", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ImportDtcgTokens_FileNotFound_ReturnsError()
    {
        var doc = CreateTestDocument("dtcg-notfound-test", 800, 600);
        _store.Save(doc);

        var result = await _tools.ImportDtcgTokens(doc.Id.ToString(), "nonexistent-tokens.json");
        var error = ParseError(result);

        Assert.Equal("FILE_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportDtcgTokens_WithTokens_ProducesValidDtcgJson()
    {
        var doc = CreateTestDocument("dtcg-export-test", 800, 600);
        doc.Tokens = new Core.Documents.DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["primary"] = "#ff0000",
                ["secondary"] = "#00ff00"
            }
        };
        _store.Save(doc);

        var result = await _tools.ExportDtcgTokens(doc.Id.ToString(), output: "exported-tokens.json");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("path", out var pathProp));
        Assert.Equal("exported-tokens.json", pathProp.GetString());

        var exportedPath = Path.Combine(_testDir, "exported-tokens.json");
        Assert.True(File.Exists(exportedPath));

        var exportedJson = File.ReadAllText(exportedPath);
        Assert.Contains("\"$value\": \"#ff0000\"", exportedJson);
        Assert.Contains("\"$type\": \"color\"", exportedJson);
    }

    [Fact]
    public async Task ExportDtcgTokens_NoTokens_ReturnsEmptyStructure()
    {
        var doc = CreateTestDocument("dtcg-export-empty-test", 800, 600);
        _store.Save(doc);

        var result = await _tools.ExportDtcgTokens(doc.Id.ToString(), output: "empty-tokens.json");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("path", out _));

        var exportedPath = Path.Combine(_testDir, "empty-tokens.json");
        Assert.True(File.Exists(exportedPath));

        var exportedJson = File.ReadAllText(exportedPath);
        var parsed = JsonDocument.Parse(exportedJson);
        // Empty structure should be minimal - just an empty object or no color/typography groups
        Assert.False(parsed.RootElement.TryGetProperty("color", out _));
        Assert.False(parsed.RootElement.TryGetProperty("typography", out _));
    }

    // ---- RenderPreview Tests ----

    [Fact]
    public void RenderPreview_ReturnsImageContentBlock()
    {
        var doc = CreateTestDocument("render-preview-test", 100, 100);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });
        _store.Save(doc);

        var result = _tools.RenderPreview(doc.Id.ToString());

        Assert.Equal(2, result.Content.Count);
        Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.IsType<TextContentBlock>(result.Content[1]);

        var imageBlock = (ImageContentBlock)result.Content[0];
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.True(imageBlock.Data.Length > 0);

        var data = ParseData(result);
        Assert.True(data.TryGetProperty("width", out _));
        Assert.True(data.TryGetProperty("height", out _));
        Assert.True(data.TryGetProperty("mimeType", out _));
    }

    [Fact]
    public void RenderPreview_ScaleAboveMax_ReturnsError()
    {
        var doc = CreateTestDocument("render-scale-clamp", 100, 100);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#00ff00" } }
        });
        _store.Save(doc);

        var result = _tools.RenderPreview(doc.Id.ToString(), scale: 10.0);

        var error = ParseError(result);
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }

    [Fact]
    public void RenderPreview_DimensionAboveMax_ReturnsError()
    {
        var doc = CreateTestDocument("render-dim-clamp", 100, 100);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#0000ff" } }
        });
        _store.Save(doc);

        var result = _tools.RenderPreview(doc.Id.ToString(), width: 3000, height: 3000);

        var error = ParseError(result);
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }

    [Fact]
    public void RenderPreview_InvalidDocument_ReturnsError()
    {
        var result = _tools.RenderPreview(Guid.NewGuid().ToString());

        Assert.Single(result.Content);
        var error = ParseError(result);
        Assert.Equal("DOCUMENT_NOT_FOUND", error.GetProperty("code").GetString());
    }

    // ---- PathBoolean Tests ----

    [Fact]
    public void PathBoolean_Union_TwoOverlappingRectangles_ReturnsCombinedPath()
    {
        var rect1 = "M0,0 L100,0 L100,100 L0,100 Z";
        var rect2 = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _tools.PathBoolean("union", rect1, rect2);
        var data = ParseData(result);

        Assert.Equal("union", data.GetProperty("operation").GetString());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.NotEmpty(pathData);
        Assert.Contains("M", pathData);
        Assert.Contains("Z", pathData);
    }

    [Fact]
    public void PathBoolean_Intersect_TwoOverlappingPaths_ReturnsIntersection()
    {
        var rect1 = "M0,0 L100,0 L100,100 L0,100 Z";
        var rect2 = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _tools.PathBoolean("intersect", rect1, rect2);
        var data = ParseData(result);

        Assert.Equal("intersect", data.GetProperty("operation").GetString());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.NotEmpty(pathData);
    }

    [Fact]
    public void PathBoolean_Difference_ReturnsRemainder()
    {
        var outer = "M0,0 L200,0 L200,200 L0,200 Z";
        var inner = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _tools.PathBoolean("difference", outer, inner);
        var data = ParseData(result);

        Assert.Equal("difference", data.GetProperty("operation").GetString());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.NotEmpty(pathData);
    }

    [Fact]
    public void PathBoolean_InvalidPathData_ReturnsError()
    {
        var validPath = "M0,0 L100,0 L100,100 L0,100 Z";
        var invalidPath = "not a valid path";

        var result = _tools.PathBoolean("union", invalidPath, validPath);
        var error = ParseError(result);

        Assert.Equal("PATH_OP_FAILED", error.GetProperty("code").GetString());
    }

    [Fact]
    public void PathBoolean_InvalidOperation_ReturnsError()
    {
        var path = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _tools.PathBoolean("invalid_op", path, path);
        var error = ParseError(result);

        Assert.Equal("INVALID_OPERATION", error.GetProperty("code").GetString());
    }

    // ---- PathOffset Tests ----

    [Fact]
    public void PathOffset_PositiveOffset_ExpandsPath()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _tools.PathOffset(square, 10);
        var data = ParseData(result);

        Assert.Equal(10, data.GetProperty("offset").GetDouble());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.NotEmpty(pathData);
    }

    [Fact]
    public void PathOffset_NegativeOffset_ContractsPath()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _tools.PathOffset(square, -10);
        var data = ParseData(result);

        Assert.Equal(-10, data.GetProperty("offset").GetDouble());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.NotEmpty(pathData);
    }

    [Fact]
    public void PathOffset_InvalidPath_ReturnsError()
    {
        var invalidPath = "not a valid path";

        var result = _tools.PathOffset(invalidPath, 10);
        var error = ParseError(result);

        Assert.Equal("PATH_OP_FAILED", error.GetProperty("code").GetString());
    }

    [Fact]
    public void PathOffset_InvalidJoinType_ReturnsError()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _tools.PathOffset(square, 10, join: "invalid_join");
        var error = ParseError(result);

        Assert.Equal("INVALID_JOIN", error.GetProperty("code").GetString());
    }

    [Fact]
    public void PathOffset_InvalidEndType_ReturnsError()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _tools.PathOffset(square, 10, end: "invalid_end");
        var error = ParseError(result);

        Assert.Equal("INVALID_END", error.GetProperty("code").GetString());
    }

    // ---- PathUnionAll Tests ----

    [Fact]
    public void PathUnionAll_MultiplePaths_ReturnsUnifiedPath()
    {
        var paths = new[]
        {
            "M0,0 L50,0 L50,50 L0,50 Z",
            "M25,25 L75,25 L75,75 L25,75 Z",
            "M50,50 L100,50 L100,100 L50,100 Z"
        };

        var result = _tools.PathUnionAll(paths);
        var data = ParseData(result);

        Assert.Equal(3, data.GetProperty("inputCount").GetInt32());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.NotEmpty(pathData);
    }

    [Fact]
    public void PathUnionAll_EmptyArray_ReturnsError()
    {
        var result = _tools.PathUnionAll(Array.Empty<string>());
        var error = ParseError(result);

        Assert.Equal("NO_PATHS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void PathUnionAll_SinglePath_ReturnsSamePath()
    {
        var paths = new[] { "M0,0 L100,0 L100,100 L0,100 Z" };

        var result = _tools.PathUnionAll(paths);
        var data = ParseData(result);

        Assert.Equal(1, data.GetProperty("inputCount").GetInt32());
        var pathData = data.GetProperty("pathData").GetString();
        Assert.NotNull(pathData);
        Assert.Contains("M0,0", pathData);
        Assert.Contains("Z", pathData);
    }

    [Fact]
    public void PathUnionAll_InvalidPath_ReturnsError()
    {
        var paths = new[]
        {
            "M0,0 L50,0 L50,50 L0,50 Z",
            "not a valid path"
        };

        var result = _tools.PathUnionAll(paths);
        var error = ParseError(result);

        Assert.Equal("PATH_OP_FAILED", error.GetProperty("code").GetString());
    }

    [Fact]
    public void PathUnionAll_AllInvalidPaths_ReturnsError()
    {
        var paths = new[]
        {
            "not a valid path",
            "also invalid"
        };

        var result = _tools.PathUnionAll(paths);
        var error = ParseError(result);

        Assert.Equal("PATH_OP_FAILED", error.GetProperty("code").GetString());
    }

    // ---- ImportSvg Tests ----

    [Fact]
    public async Task ImportSvg_ValidSvg_CreatesDocument()
    {
        var svgContent = """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 200 100">
                <rect x="10" y="10" width="80" height="60" fill="#ff0000"/>
            </svg>
            """;
        var svgPath = Path.Combine(_testDir, "test-import.svg");
        File.WriteAllText(svgPath, svgContent);

        var result = await _tools.ImportSvg("test-import.svg");
        var data = ParseData(result);

        Assert.True(data.TryGetProperty("documentId", out _));
        Assert.True(data.TryGetProperty("documentName", out _));
        Assert.True(data.TryGetProperty("canvas", out var canvas));
        Assert.Equal(200, canvas.GetProperty("width").GetDouble());
        Assert.Equal(100, canvas.GetProperty("height").GetDouble());
        Assert.True(data.TryGetProperty("nodeCount", out var nodeCount));
        Assert.True(nodeCount.GetInt32() >= 1);

        // Verify the output file was created
        var expectedOutputPath = Path.Combine(_testDir, "test-import.agd.json");
        Assert.True(File.Exists(expectedOutputPath));
    }

    [Fact]
    public async Task ImportSvg_InvalidSvg_ReturnsErrorWithWarnings()
    {
        // Create an invalid SVG (not valid XML)
        var invalidSvg = "<svg not valid xml>";
        var svgPath = Path.Combine(_testDir, "invalid-import.svg");
        File.WriteAllText(svgPath, invalidSvg);

        var result = await _tools.ImportSvg("invalid-import.svg");
        var error = ParseError(result);

        // Should return an error code for parse/import failure
        Assert.True(error.TryGetProperty("code", out var code));
        Assert.NotNull(code.GetString());
    }

    [Fact]
    public async Task ImportSvg_FileNotFound_ReturnsError()
    {
        var result = await _tools.ImportSvg("nonexistent-file.svg");
        var error = ParseError(result);

        Assert.Equal("FILE_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ImportSvg_NonSvgExtension_ReturnsError()
    {
        var content = "not an svg";
        var path = Path.Combine(_testDir, "test-file.txt");
        File.WriteAllText(path, content);

        var result = await _tools.ImportSvg("test-file.txt");
        var error = ParseError(result);

        Assert.Equal("UNSUPPORTED_FORMAT", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ImportSvg_PathEscape_ReturnsError()
    {
        var result = await _tools.ImportSvg("../../../etc/passwd.svg");
        var error = ParseError(result);

        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ImportSvg_CustomOutputPath_UsesSpecifiedPath()
    {
        var svgContent = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
                <circle cx="50" cy="50" r="40" fill="#00ff00"/>
            </svg>
            """;
        var svgPath = Path.Combine(_testDir, "source.svg");
        File.WriteAllText(svgPath, svgContent);

        var result = await _tools.ImportSvg("source.svg", outputPath: "output/custom-output.agd.json");
        var data = ParseData(result);

        var expectedPath = Path.Combine(_testDir, "output", "custom-output.agd.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Equal("output/custom-output.agd.json", data.GetProperty("locator").GetString());
    }

    [Fact]
    public async Task ImportSvg_ReturnedId_LoadsAndSavesBackToSourceLocator()
    {
        var svgPath = Path.Combine(_testDir, "roundtrip.svg");
        File.WriteAllText(svgPath, """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
                <rect width="10" height="10" />
            </svg>
            """);
        var imported = await _tools.ImportSvg("roundtrip.svg", outputPath: "nested/roundtrip.agd.json");
        var documentId = ParseData(imported).GetProperty("documentId").GetString()!;

        ParseData(_tools.AddNode(documentId, "root", "rectangle", name: "saved-by-id"));

        var source = File.ReadAllText(Path.Combine(_testDir, "nested", "roundtrip.agd.json"));
        Assert.Contains("saved-by-id", source);
        var listed = ParseData(_tools.ListDocuments()).GetProperty("documents").EnumerateArray().ToList();
        Assert.Contains(listed, item => item.GetProperty("locator").GetString() == "nested/roundtrip.agd.json");
    }

    [Fact]
    public async Task ImportSvg_Repeat_DoesNotEraseEditsWithoutForce()
    {
        var svgPath = Path.Combine(_testDir, "stable-import.svg");
        File.WriteAllText(svgPath, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>");
        var first = await _tools.ImportSvg("stable-import.svg", outputPath: "stable-import.agd.json");
        var id = ParseData(first).GetProperty("documentId").GetString()!;
        ParseData(_tools.AddNode(id, "root", "rectangle", name: "keep-import-edit"));

        var repeated = await _tools.ImportSvg("stable-import.svg", outputPath: "stable-import.agd.json");

        Assert.Equal("DOCUMENT_EXISTS", ParseError(repeated).GetProperty("code").GetString());
        Assert.Contains("keep-import-edit", File.ReadAllText(Path.Combine(_testDir, "stable-import.agd.json")));
    }

    [Fact]
    public async Task ImportSvg_CustomDocumentName_SetsName()
    {
        var svgContent = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
                <ellipse cx="50" cy="50" rx="40" ry="30" fill="#0000ff"/>
            </svg>
            """;
        var svgPath = Path.Combine(_testDir, "named-import.svg");
        File.WriteAllText(svgPath, svgContent);

        var result = await _tools.ImportSvg("named-import.svg", documentName: "My Custom Design");
        var data = ParseData(result);

        Assert.Equal("My Custom Design", data.GetProperty("documentName").GetString());
    }

    [Fact]
    public void AddNode_OversizedPersistedStyleValue_IsRejectedBeforeMutation()
    {
        var doc = CreateTestDocument("oversized-style", 100, 100);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "root", "rectangle", fill: new string('x', 1025));

        Assert.Equal("INPUT_TOO_LARGE", ParseError(result).GetProperty("code").GetString());
        Assert.Empty(_store.Load(doc.Id)!.Root.Children);
    }

    [Fact]
    public void UpdateNode_OversizedFontFamily_IsRejectedBeforeMutation()
    {
        var doc = CreateTestDocument("oversized-font", 100, 100);
        var text = new TextNode { Id = Guid.NewGuid(), Text = "unchanged", FontFamily = "Inter" };
        doc.Root.Children.Add(text);
        _store.Save(doc);

        var result = _tools.UpdateNode(doc.Id.ToString(), text.Id.ToString(), fontFamily: new string('x', 1025));

        Assert.Equal("INPUT_TOO_LARGE", ParseError(result).GetProperty("code").GetString());
        Assert.Equal("Inter", Assert.IsType<TextNode>(_store.Load(doc.Id)!.Root.Children.Single()).FontFamily);
    }

    [Fact]
    public void ToolError_DoesNotDiscloseUnrecognizedUnixRoot()
    {
        var result = ToolResult.Fail("TEST", "Unable to read /Users/private-user/designs/secret.agd.json");

        var message = ParseError(result).GetProperty("message").GetString();
        Assert.DoesNotContain("/Users/", message);
        Assert.Contains("secret.agd.json", message);
    }


    // ---- currentColor ----

    private const string SameWindingRing = "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z";
    private const string OuterSquare = "M0,0 L100,0 L100,100 L0,100 Z";
    private const string InnerSquare = "M25,25 L75,25 L75,75 L25,75 Z";

    [Fact]
    public async Task ExportDocument_CurrentColor_RewritesMatchingPaintOnly()
    {
        var doc = CreateTestDocument("current-color", 100, 100);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "ink",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style { Fill = new Fill { Color = "#000000" }, Stroke = new Stroke { Color = "#ff0000", Width = 2 } }
        });
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "current-color.svg");

        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath, currentColor: "#000000");

        ParseData(result);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
        Assert.DoesNotContain("#000000", svg);
    }

    [Fact]
    public async Task ExportDocument_CurrentColorWithRasterFormat_ReturnsInvalidArguments()
    {
        var doc = CreateTestDocument("current-color-png", 100, 100);
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "current-color.png");

        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath, format: "png", currentColor: "#000000");

        var error = ParseError(result);
        Assert.Equal(EngineErrors.InvalidArguments, error.GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("black")]
    [InlineData("#12")]
    [InlineData("")]
    public async Task ExportDocument_InvalidCurrentColor_ReturnsInvalidProperty(string currentColor)
    {
        var doc = CreateTestDocument("current-color-bad", 100, 100);
        _store.Save(doc);
        var outputPath = Path.Combine(_testDir, "current-color-bad.svg");

        var result = await _tools.ExportDocument(doc.Id.ToString(), outputPath, currentColor: currentColor);

        var error = ParseError(result);
        Assert.Equal(EngineErrors.InvalidProperty, error.GetProperty("code").GetString());
        Assert.False(File.Exists(outputPath));
    }

    // ---- fillRule on nodes ----

    [Fact]
    public void AddNode_PathWithFillRule_PersistsNormalizedValue()
    {
        var doc = CreateTestDocument("fill-rule-add", 100, 100);
        _store.Save(doc);

        var data = ParseData(_tools.AddNode(doc.Id.ToString(), "root", "path", pathData: SameWindingRing, fillRule: "EvenOdd"));

        var nodeId = Guid.Parse(data.GetProperty("nodeId").GetString()!);
        var path = Assert.IsType<PathNode>(Assert.Single(_store.Load(doc.Id)!.Root.Children));
        Assert.Equal(nodeId, path.Id);
        Assert.Equal("evenodd", path.Style!.FillRule);
    }

    [Fact]
    public void AddNode_FillRuleOnRectangle_ReturnsInvalidProperty()
    {
        var doc = CreateTestDocument("fill-rule-add-rect", 100, 100);
        _store.Save(doc);

        var result = _tools.AddNode(doc.Id.ToString(), "root", "rectangle", width: 10, height: 10, fillRule: "evenodd");

        Assert.Equal(EngineErrors.InvalidProperty, ParseError(result).GetProperty("code").GetString());
        Assert.Empty(_store.Load(doc.Id)!.Root.Children);
    }

    [Fact]
    public void UpdateNode_FillRule_SetsAndClears()
    {
        var doc = CreateTestDocument("fill-rule-update", 100, 100);
        var path = new PathNode { Id = Guid.NewGuid(), Name = "ring", Order = "a0", D = SameWindingRing };
        doc.Root.Children.Add(path);
        _store.Save(doc);

        ParseData(_tools.UpdateNode(doc.Id.ToString(), path.Id.ToString(), fillRule: "evenodd"));
        Assert.Equal("evenodd", Assert.IsType<PathNode>(_store.Load(doc.Id)!.Root.Children[0]).Style!.FillRule);

        ParseData(_tools.UpdateNode(doc.Id.ToString(), path.Id.ToString(), fillRule: "__clear__"));
        Assert.Null(Assert.IsType<PathNode>(_store.Load(doc.Id)!.Root.Children[0]).Style?.FillRule);
    }

    [Fact]
    public void UpdateNode_UnknownFillRule_ReturnsInvalidProperty()
    {
        var doc = CreateTestDocument("fill-rule-update-bad", 100, 100);
        var path = new PathNode { Id = Guid.NewGuid(), Name = "ring", Order = "a0", D = SameWindingRing };
        doc.Root.Children.Add(path);
        _store.Save(doc);

        var result = _tools.UpdateNode(doc.Id.ToString(), path.Id.ToString(), fillRule: "even-odd");

        Assert.Equal(EngineErrors.InvalidProperty, ParseError(result).GetProperty("code").GetString());
        Assert.Null(_store.Load(doc.Id)!.Root.Children[0].Style?.FillRule);
    }

    // ---- fillRule on path tools ----

    [Fact]
    public void PathBoolean_EvenOdd_KeepsHole()
    {
        var evenOdd = ParseData(_tools.PathBoolean("intersect", SameWindingRing, OuterSquare, fillRule: "evenodd"));
        var nonZero = ParseData(_tools.PathBoolean("intersect", SameWindingRing, OuterSquare));

        Assert.Equal("evenodd", evenOdd.GetProperty("fillRule").GetString());
        Assert.Equal("nonzero", nonZero.GetProperty("fillRule").GetString());
        Assert.Equal(2, CountSubpaths(evenOdd.GetProperty("pathData").GetString()!));
        Assert.Equal(1, CountSubpaths(nonZero.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathBoolean_UnknownFillRule_ReturnsError()
    {
        var result = _tools.PathBoolean("union", OuterSquare, OuterSquare, fillRule: "even-odd");

        Assert.Equal("INVALID_FILL_RULE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void PathOffset_EvenOdd_OffsetsHoleInward()
    {
        var evenOdd = ParseData(_tools.PathOffset(SameWindingRing, 5, fillRule: "evenodd"));
        var nonZero = ParseData(_tools.PathOffset(SameWindingRing, 5));

        Assert.Equal("evenodd", evenOdd.GetProperty("fillRule").GetString());
        Assert.Equal("nonzero", nonZero.GetProperty("fillRule").GetString());
        Assert.Equal(2, CountSubpaths(evenOdd.GetProperty("pathData").GetString()!));
        Assert.Equal(1, CountSubpaths(nonZero.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathOffset_UnknownFillRule_ReturnsError()
    {
        var result = _tools.PathOffset(OuterSquare, 5, fillRule: "winding");

        Assert.Equal("INVALID_FILL_RULE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void PathUnionAll_EvenOdd_TurnsNestedSquareIntoHole()
    {
        var evenOdd = ParseData(_tools.PathUnionAll([OuterSquare, InnerSquare], fillRule: "evenodd"));
        var nonZero = ParseData(_tools.PathUnionAll([OuterSquare, InnerSquare]));

        Assert.Equal("evenodd", evenOdd.GetProperty("fillRule").GetString());
        Assert.Equal("nonzero", nonZero.GetProperty("fillRule").GetString());
        Assert.Equal(2, CountSubpaths(evenOdd.GetProperty("pathData").GetString()!));
        Assert.Equal(1, CountSubpaths(nonZero.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathUnionAll_UnknownFillRule_ReturnsError()
    {
        var result = _tools.PathUnionAll([OuterSquare], fillRule: "");

        Assert.Equal("INVALID_FILL_RULE", ParseError(result).GetProperty("code").GetString());
    }

    private static int CountSubpaths(string pathData) => pathData.Count(c => c == 'M');

    [Fact]
    public void ApplyPatch_AppliesOperationsAtomicallyAndReturnsCreatedIds()
    {
        var doc = CreateTestDocument("patch-test", 800, 600);
        var rect = new RectangleNode { Id = Guid.NewGuid(), Name = "rect", Width = 100, Height = 50 };
        doc.Root.Children.Add(rect);
        _store.Save(doc);

        var result = ApplyPatch(doc.Id.ToString(), $$"""
            [
              { "op": "set", "nodeId": "{{rect.Id}}", "path": "/transform/rotation", "value": 30 },
              { "op": "addNode", "node": { "type": "text", "text": "hi" } },
              { "op": "setColorToken", "name": "primary", "value": "#123456" }
            ]
            """);
        var data = ParseData(result);

        Assert.Equal(3, data.GetProperty("applied").GetInt32());
        Assert.Equal(2, data.GetProperty("revn").GetInt64());
        var createdId = Guid.Parse(data.GetProperty("createdIds").GetProperty("1").GetString()!);
        var updated = _store.Load(doc.Id)!;
        Assert.Equal(2, updated.Revn);
        Assert.Equal(30, updated.Root.Children[0].Transform!.Rotation);
        Assert.Equal(createdId, updated.Root.Children[1].Id);
        Assert.Equal("#123456", updated.Tokens!.Colors!["primary"]);
    }

    [Fact]
    public void ApplyPatch_InvalidOperations_ReturnsInvalidArguments()
    {
        var doc = CreateTestDocument("patch-invalid", 800, 600);
        _store.Save(doc);

        var notJson = ParseError(_tools.ApplyPatch(doc.Id.ToString(), null!));
        var unknownOp = ParseError(ApplyPatch(doc.Id.ToString(), """[{ "op": "warp" }]"""));

        Assert.Equal("INVALID_ARGUMENTS", notJson.GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", unknownOp.GetProperty("code").GetString());
        Assert.Equal(0, unknownOp.GetProperty("operationIndex").GetInt32());
        Assert.Equal(1, _store.Load(doc.Id)!.Revn);
    }

    [Fact]
    public void ApplyPatch_FailingOperation_ReportsIndexAndPersistsNothing()
    {
        var doc = CreateTestDocument("patch-fail", 800, 600);
        _store.Save(doc);

        var result = ApplyPatch(doc.Id.ToString(), $$"""
            [
              { "op": "addNode", "node": { "type": "rectangle" } },
              { "op": "deleteNode", "nodeId": "{{Guid.NewGuid()}}" }
            ]
            """);
        var error = ParseError(result);

        Assert.Equal("NODE_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal(1, error.GetProperty("operationIndex").GetInt32());
        var stored = _store.Load(doc.Id)!;
        Assert.Equal(1, stored.Revn);
        Assert.Empty(stored.Root.Children);
    }

    [Fact]
    public void ApplyPatch_PathEscape_ReturnsError()
    {
        var result = ApplyPatch("../../../etc/passwd", """[{ "op": "setCanvas", "width": 10 }]""");

        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void ApplyPatch_RevisionConflict_ReturnsError()
    {
        var doc = CreateTestDocument("patch-revn", 800, 600);
        _store.Save(doc);

        var conflict = ParseError(ApplyPatch(doc.Id.ToString(), """[{ "op": "setCanvas", "width": 10 }]""", expectedRevn: 5));
        var success = ParseData(ApplyPatch(doc.Id.ToString(), """[{ "op": "setCanvas", "width": 10 }]""", expectedRevn: 1));

        Assert.Equal("REVISION_CONFLICT", conflict.GetProperty("code").GetString());
        Assert.False(conflict.TryGetProperty("operationIndex", out _));
        Assert.Equal(2, success.GetProperty("revn").GetInt64());
    }

    [Fact]
    public void ApplyPatch_ImageSources_AreConfinedLikeAddNode()
    {
        var doc = CreateTestDocument("patch-image", 800, 600);
        var image = new ImageNode { Id = Guid.NewGuid(), Src = "a.png", Width = 10, Height = 10 };
        doc.Root.Children.Add(image);
        _store.Save(doc);

        var escape = ParseError(ApplyPatch(doc.Id.ToString(), """
            [{ "op": "addNode", "node": { "type": "group", "children": [
                 { "type": "image", "src": "../outside.png", "width": 1, "height": 1 } ] } }]
            """));
        Assert.Equal("PATH_ESCAPE", escape.GetProperty("code").GetString());
        Assert.Equal(0, escape.GetProperty("operationIndex").GetInt32());

        var setEscape = ParseError(ApplyPatch(doc.Id.ToString(), $$"""
            [{ "op": "set", "nodeId": "{{image.Id}}", "path": "/src", "value": "/etc/passwd" }]
            """));
        Assert.Equal("PATH_ESCAPE", setEscape.GetProperty("code").GetString());

        ParseData(ApplyPatch(doc.Id.ToString(), $$"""
            [
              { "op": "set", "nodeId": "{{image.Id}}", "path": "/src", "value": "{{Path.Combine(_testDir, "assets", "pic.png").Replace("\\", "/")}}" },
              { "op": "addNode", "node": { "type": "image", "src": "https://example.com/x.png", "width": 1, "height": 1 } }
            ]
            """));
        var stored = _store.Load(doc.Id)!;
        Assert.Equal("assets/pic.png", ((ImageNode)stored.Root.Children[0]).Src);
        Assert.Equal("https://example.com/x.png", ((ImageNode)stored.Root.Children[1]).Src);
    }

    [Fact]
    public void ApplyPatch_ChildrenPaths_CannotBypassImageConfinement()
    {
        var doc = CreateTestDocument("patch-nested-image", 800, 600);
        var image = new ImageNode { Id = Guid.NewGuid(), Src = "a.png", Width = 10, Height = 10 };
        var group = new GroupNode { Id = Guid.NewGuid(), Name = "g", Children = [image] };
        doc.Root.Children.Add(group);
        _store.Save(doc);

        var nested = ParseError(ApplyPatch(doc.Id.ToString(), $$"""
            [{ "op": "set", "nodeId": "{{group.Id}}", "path": "/children/0/src", "value": "/etc/passwd" }]
            """));
        Assert.Equal("INVALID_PROPERTY", nested.GetProperty("code").GetString());
        Assert.Equal(0, nested.GetProperty("operationIndex").GetInt32());

        var appended = ParseError(ApplyPatch(doc.Id.ToString(), $$"""
            [{ "op": "set", "nodeId": "{{doc.Root.Id}}", "path": "/children/-",
               "value": { "type": "image", "src": "../outside.png", "width": 1, "height": 1 } }]
            """));
        Assert.Equal("INVALID_PROPERTY", appended.GetProperty("code").GetString());
        Assert.Equal(0, appended.GetProperty("operationIndex").GetInt32());

        var stored = _store.Load(doc.Id)!;
        var storedGroup = Assert.IsType<GroupNode>(Assert.Single(stored.Root.Children));
        Assert.Equal("a.png", Assert.IsType<ImageNode>(Assert.Single(storedGroup.Children)).Src);
        Assert.Equal(doc.Revn, stored.Revn);
    }

    [Fact]
    public void ApplyPatch_ExpectedRevisionNegative_ReturnsError()
    {
        var result = ApplyPatch(Guid.NewGuid().ToString(), "[]", expectedRevn: -1);

        Assert.Equal("INVALID_PROPERTY", ParseError(result).GetProperty("code").GetString());
    }

    private CallToolResult ApplyPatch(string idOrPath, string json, long? expectedRevn = null) =>
        _tools.ApplyPatch(idOrPath, JsonSerializer.Deserialize<JsonElement[]>(json)!, expectedRevn);

    [Fact]
    public void ApplyPatch_Utf8AdapterBoundary_PreservesByteLimitAndSourceOnRejection()
    {
        var doc = CreateTestDocument("byte-boundary", 800, 600);
        _store.Save(doc);
        const string prefix = "[{\"op\":\"setCanvas\",\"width\":400";
        const string suffix = "}]";
        Assert.Equal(16 * 1024 * 1024, FTPlus.AgentGraphicDesign.Core.Patching.PatchLimits.MaxJsonBytes);
        var exact = prefix + new string(' ', 16 * 1024 * 1024 - prefix.Length - suffix.Length) + suffix;
        ParseData(ApplyPatch(doc.Id.ToString(), exact));
        var before = File.ReadAllText(Path.Combine(_testDir, doc.Id + ".agd.json"));
        var over = prefix + new string(' ', exact.Length - prefix.Length - suffix.Length + 1) + suffix;
        Assert.Equal("RESOURCE_LIMIT", ParseError(ApplyPatch(doc.Id.ToString(), over)).GetProperty("code").GetString());
        Assert.Equal(before, File.ReadAllText(Path.Combine(_testDir, doc.Id + ".agd.json")));
        const string unicodePrefix = "[{\"op\":\"warp\",\"padding\":\"";
        const string unicodeSuffix = "\"}]";
        var budget = 16 * 1024 * 1024 - unicodePrefix.Length - unicodeSuffix.Length;
        var unicode = unicodePrefix + new string('é', budget / 2) + (budget % 2 == 0 ? "" : "a") + unicodeSuffix;
        Assert.Equal("INVALID_ARGUMENTS", ParseError(ApplyPatch(doc.Id.ToString(), unicode)).GetProperty("code").GetString());
        var unicodeOver = unicode[..^unicodeSuffix.Length] + "a" + unicodeSuffix;
        Assert.Equal("RESOURCE_LIMIT", ParseError(ApplyPatch(doc.Id.ToString(), unicodeOver)).GetProperty("code").GetString());
        Assert.Equal(before, File.ReadAllText(Path.Combine(_testDir, doc.Id + ".agd.json")));
    }

    private static Document CreateTestDocument(string name, double width, double height)
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = name,
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

    private sealed class TestDocumentStore : IDocumentStore
    {
        private readonly FileDocumentStore _inner;

        public TestDocumentStore(string path)
        {
            _inner = new FileDocumentStore(path);
        }

        public Document? Load(Guid id) => ((IDocumentStore)_inner).Load(id)?.Document;

        public void Save(Document document) =>
            ((IDocumentStore)_inner).Save(new DocumentStoreEntry(document, SourcePath: null));

        DocumentStoreEntry? IDocumentStore.Load(Guid id) => ((IDocumentStore)_inner).Load(id);

        void IDocumentStore.Save(DocumentStoreEntry entry, long? expectedRevn) =>
            ((IDocumentStore)_inner).Save(entry, expectedRevn);

        void IDocumentStore.Create(Document document) => ((IDocumentStore)_inner).Create(document);

        void IDocumentStore.Delete(Guid id) => ((IDocumentStore)_inner).Delete(id);

        IEnumerable<DocumentInfo> IDocumentStore.List() => ((IDocumentStore)_inner).List();
    }

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
