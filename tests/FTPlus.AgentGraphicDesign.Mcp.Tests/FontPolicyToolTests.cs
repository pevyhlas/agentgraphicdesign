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
/// Feature 002 T008: the get_bounds, lint_document, and render_contact_sheet tools expose font
/// options, confine the directories to the working directory like every other path, and thread
/// the policy into the measurement/render path.
/// </summary>
public class FontPolicyToolTests : IDisposable
{
    private readonly string _testDir;
    private readonly DocumentEngine _engine;
    private readonly DocumentTools _tools;

    public FontPolicyToolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-mcp-fontpolicy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var store = new FileDocumentStore(_testDir);
        _engine = new DocumentEngine(store,
            renderer: new SkiaRenderer(),
            contactSheetRenderer: new SkiaContactSheetRenderer(),
            measurerFactory: policy => new SkiaTextMeasurer(policy));
        _tools = new DocumentTools(_engine, new PathConfinement(_testDir), new SkiaRenderer());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
        GC.SuppressFinalize(this);
    }

    private string WriteTextDocument(string? fontFamily)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "font-policy",
            Canvas = new Canvas { Width = 200, Height = 100, Background = "#ffffff" },
            Root = new FrameNode
            {
                Width = 200,
                Height = 100,
                Children =
                [
                    new TextNode
                    {
                        Id = Guid.NewGuid(),
                        Text = "Agent Graphic Design",
                        FontFamily = fontFamily,
                        FontSize = 24,
                        Transform = new Transform { X = 10, Y = 40 },
                        Style = new Style { Fill = new Fill { Color = "#222222" } }
                    }
                ]
            }
        };
        var path = Path.Combine(_testDir, "doc.agd.json");
        _engine.SaveDocumentToFile(document, path);
        return "doc.agd.json";
    }

    [Fact]
    public void GetBounds_FontDirsOutsideWorkingDirectory_ReturnsPathEscape()
    {
        var rel = WriteTextDocument(null);
        var result = _tools.GetBounds(rel, fontDirs: "/tmp");
        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void LintDocument_FontDirsOutsideWorkingDirectory_ReturnsPathEscape()
    {
        var rel = WriteTextDocument(null);
        var result = _tools.LintDocument(rel, fontDirs: "/tmp");
        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void RenderContactSheet_FontDirsOutsideWorkingDirectory_ReturnsPathEscape()
    {
        var rel = WriteTextDocument(null);
        var result = _tools.RenderContactSheet(rel, fontDirs: "/tmp");
        Assert.Equal("PATH_ESCAPE", ParseError(result).GetProperty("code").GetString());
    }

    [Fact]
    public void RenderContactSheet_StrictFonts_FailsWhenFamilyCannotResolve()
    {
        var rel = WriteTextDocument("No Such Family 9Z");

        // Default policy substitutes the bundled family and renders an image block.
        var lenient = _tools.RenderContactSheet(rel);
        Assert.NotEqual(true, lenient.IsError);
        Assert.Contains(lenient.Content, block => block is ImageContentBlock);

        // Strict fonts with no host fonts surface the unresolved family as a hard failure.
        var strict = _tools.RenderContactSheet(rel, systemFonts: false, strictFonts: true);
        Assert.Equal("FONT_NOT_FOUND", ParseError(strict).GetProperty("code").GetString());
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
