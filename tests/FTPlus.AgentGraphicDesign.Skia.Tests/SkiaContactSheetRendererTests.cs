using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// Composes several documents into one PNG grid. Verifies the raster is a real PNG,
/// its dimensions match the computed layout, and every entry gets a cell.
/// </summary>
public class SkiaContactSheetRendererTests
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly SkiaContactSheetRenderer _renderer = new();

    [Fact]
    public void Render_TwoDocuments_ProducesAPng()
    {
        var entries = new List<ContactSheetEntry>
        {
            new(DocWithRect("#ff0000"), "red"),
            new(DocWithRect("#0000ff"), "blue")
        };
        var options = new ContactSheetOptions { CellSize = 64 };

        var result = _renderer.Render(entries, options, CancellationToken.None);

        Assert.Equal("image/png", result.MimeType);
        Assert.True(result.Data.Length > PngMagic.Length);
        Assert.Equal(PngMagic, result.Data.Take(PngMagic.Length));
        Assert.Equal(2, result.Cells.Count);

        var layout = ContactSheetLayout.Compute(2, options);
        Assert.Equal(layout.Width, result.Width);
        Assert.Equal(layout.Height, result.Height);
    }

    [Fact]
    public void Render_WithLabels_IsTallerThanWithout()
    {
        var entries = new List<ContactSheetEntry> { new(DocWithRect("#00aa00"), "green") };

        var plain = _renderer.Render(entries, new ContactSheetOptions { CellSize = 64 }, CancellationToken.None);
        var labelled = _renderer.Render(entries, new ContactSheetOptions { CellSize = 64, Labels = true }, CancellationToken.None);

        Assert.Equal(plain.Width, labelled.Width);
        Assert.True(labelled.Height > plain.Height);
    }

    [Fact]
    public void Render_AspectFitsANonSquareCanvasInsideTheCell()
    {
        var entries = new List<ContactSheetEntry> { new(DocWithRect("#000000", width: 200, height: 100), "wide") };
        var options = new ContactSheetOptions { CellSize = 80 };

        var result = _renderer.Render(entries, options, CancellationToken.None);
        var image = result.Cells[0].Image;

        // A 2:1 canvas fills the cell width and half its height.
        Assert.Equal(80, image.Width);
        Assert.Equal(40, image.Height);
    }

    private static Document DocWithRect(string color, double width = 100, double height = 100) => new()
    {
        Schema = Document.SchemaUrl,
        Id = Guid.NewGuid(),
        Name = "sheet-doc",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = width, Height = height, Background = "#ffffff" },
        Root = new FrameNode
        {
            Id = Guid.NewGuid(),
            Name = "root",
            Width = width,
            Height = height,
            Children =
            [
                new RectangleNode
                {
                    Id = Guid.NewGuid(),
                    Name = "fill",
                    Order = "a0",
                    Width = width,
                    Height = height,
                    Style = new Style { Fill = new Fill { Color = color } }
                }
            ]
        }
    };
}
