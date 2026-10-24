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
/// Feature 012 AC-02 (FR-001): the internal Rgba8 buffer is never an MCP file format.
/// `export_document` refuses raw-RGBA format names with EXPORT_UNSUPPORTED and writes nothing;
/// `render_preview` and `render_contact_sheet` always return PNG images.
/// </summary>
public sealed class RawRgbaRefusalToolsTests : IDisposable
{
    private readonly string _directory;
    private readonly DocumentTools _tools;

    public RawRgbaRefusalToolsTests()
    {
        _directory = Directory.CreateTempSubdirectory("agd-raw-rgba-mcp-").FullName;
        var renderer = new SkiaRenderer();
        var engine = new DocumentEngine(new FileDocumentStore(_directory), null, renderer, null, new SkiaContactSheetRenderer(renderer));
        _tools = new DocumentTools(engine, new PathConfinement(_directory), renderer);
        File.WriteAllText(Path.Combine(_directory, "doc.agd.json"), DocumentSerializer.Serialize(Document()));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Theory]
    [InlineData("rgba8")]
    [InlineData("RGBA8")]
    [InlineData("rgba")]
    [InlineData("raw")]
    public async Task ExportDocument_RawRgbaFormat_IsRefused(string format)
    {
        var result = await _tools.ExportDocument("doc.agd.json", "doc.rgba", format);

        Assert.True(result.IsError);
        Assert.Equal(EngineErrors.ExportUnsupported, ErrorCode(result));
        Assert.False(File.Exists(Path.Combine(_directory, "doc.rgba")));
    }

    [Fact]
    public void RenderPreview_AlwaysReturnsPng()
    {
        var result = _tools.RenderPreview("doc.agd.json");

        Assert.NotEqual(true, result.IsError);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], image.DecodedData.ToArray().Take(4));
    }

    [Fact]
    public void RenderContactSheet_AlwaysReturnsPng()
    {
        var result = _tools.RenderContactSheet("doc.agd.json", cell: 16);

        Assert.NotEqual(true, result.IsError);
        var image = Assert.Single(result.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], image.DecodedData.ToArray().Take(4));
    }

    private static string? ErrorCode(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(result.Content.Last(block => block is TextContentBlock)).Text;
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    private static Document Document() => new()
    {
        Schema = Core.Documents.Document.SchemaUrl,
        Id = Guid.Parse("0c5e1203-0000-4000-8000-000000000000"),
        Name = "raw-rgba-refusal",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = 16, Height = 16, Background = "#ffffff" },
        Root = new FrameNode
        {
            Id = Guid.Parse("0c5e1203-0000-4000-8000-0000000000f0"),
            Name = "root",
            Width = 16,
            Height = 16,
            Children =
            [
                new RectangleNode
                {
                    Id = Guid.Parse("0c5e1203-0000-4000-8000-000000000001"),
                    Name = "fill",
                    Order = "a0",
                    Width = 8,
                    Height = 8,
                    Style = new Style { Fill = new Fill { Color = "#2563eb" } }
                }
            ]
        }
    };
}
