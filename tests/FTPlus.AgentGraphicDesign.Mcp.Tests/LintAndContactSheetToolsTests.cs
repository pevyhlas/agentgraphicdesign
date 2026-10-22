using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Mcp;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Skia;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// The lint_document and render_contact_sheet MCP tools, wired with a real
/// contact-sheet renderer so the grid actually composes.
/// </summary>
public class LintAndContactSheetToolsTests : IDisposable
{
    private readonly string _testDir;
    private readonly DocumentTools _tools;

    public LintAndContactSheetToolsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-lint-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var store = new FileDocumentStore(_testDir);
        var renderer = new SkiaRenderer();
        var engine = new DocumentEngine(store, null, renderer, null, new SkiaContactSheetRenderer(renderer));
        _tools = new DocumentTools(engine, new PathConfinement(_testDir), renderer);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    // ---- lint_document ----

    [Fact]
    public void LintDocument_WhiteTextOnWhiteCanvas_ReportsContrastWarning()
    {
        WriteDoc("lint.agd.json", WhiteTextDoc());

        var result = _tools.LintDocument("lint.agd.json");
        var data = ParseData(result);

        var codes = data.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString()).ToList();
        Assert.Contains("LINT001", codes);
        Assert.Equal("default", data.GetProperty("profile").GetString());
        Assert.False(data.GetProperty("gridChecked").GetBoolean());
        Assert.True(data.GetProperty("warningCount").GetInt32() >= 1);
    }

    [Fact]
    public void LintDocument_WebIconProfile_ChecksGrid()
    {
        WriteDoc("icon.agd.json", WhiteTextDoc());

        var result = _tools.LintDocument("icon.agd.json", profile: "web-icon");
        var data = ParseData(result);

        Assert.Equal("web-icon", data.GetProperty("profile").GetString());
        Assert.True(data.GetProperty("gridChecked").GetBoolean());
    }

    [Fact]
    public void LintDocument_UnknownProfile_ReturnsError()
    {
        WriteDoc("lint.agd.json", WhiteTextDoc());

        var result = _tools.LintDocument("lint.agd.json", profile: "bogus");

        Assert.Equal(true, result.IsError);
    }

    [Fact]
    public void LintDocument_MissingDocument_ReturnsError()
    {
        var result = _tools.LintDocument("nope.agd.json");

        Assert.Equal(true, result.IsError);
    }

    // ---- render_contact_sheet ----

    [Fact]
    public void RenderContactSheet_TwoDocuments_ReturnsImageAndCells()
    {
        WriteDoc("a.agd.json", ColoredDoc("#ff0000"));
        WriteDoc("b.agd.json", ColoredDoc("#0000ff"));

        var result = _tools.RenderContactSheet("a.agd.json,b.agd.json", cell: 64);
        Assert.NotEqual(true, result.IsError);

        Assert.Contains(result.Content, block => block is ImageContentBlock);

        var data = ParseData(result);
        Assert.Equal(2, data.GetProperty("cells").GetArrayLength());
        Assert.Equal("image/png", data.GetProperty("mimeType").GetString());
        Assert.True(data.GetProperty("width").GetInt32() > 0);
    }

    [Fact]
    public void RenderContactSheet_MissingDocument_ReturnsError()
    {
        WriteDoc("a.agd.json", ColoredDoc("#ff0000"));

        var result = _tools.RenderContactSheet("a.agd.json,missing.agd.json");

        Assert.Equal(true, result.IsError);
    }

    // ---- helpers ----

    private void WriteDoc(string name, Document doc) =>
        File.WriteAllText(Path.Combine(_testDir, name), DocumentSerializer.Serialize(doc));

    private static Document WhiteTextDoc()
    {
        var doc = BaseDoc(800, 600);
        doc.Root.Children.Add(new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Order = "a0",
            Text = "Hello",
            FontSize = 16,
            Transform = new Transform { X = 10, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#ffffff" } }
        });
        return doc;
    }

    private static Document ColoredDoc(string color)
    {
        var doc = BaseDoc(100, 100);
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "fill",
            Order = "a0",
            Width = 100,
            Height = 100,
            Style = new Style { Fill = new Fill { Color = color } }
        });
        return doc;
    }

    private static Document BaseDoc(double width, double height) => new()
    {
        Schema = Document.SchemaUrl,
        Id = Guid.NewGuid(),
        Name = "doc",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = width, Height = height, Background = "#ffffff" },
        Root = new FrameNode { Id = Guid.NewGuid(), Name = "root", Width = width, Height = height, Children = [] }
    };

    private static JsonElement ParseData(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content.Last(block => block is TextContentBlock)).Text;
        var json = JsonDocument.Parse(text);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean(), $"Expected ok=true but got: {text}");
        return json.RootElement.GetProperty("data");
    }
}
