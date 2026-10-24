using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// Feature 012 AC-02 (FR-001, plan C1): every <see cref="RenderFormat"/> member either encodes an
/// image file or is the raw <see cref="RenderFormat.Rgba8"/> buffer, and the encoder throws for
/// <see cref="RenderFormat.Rgba8"/> and for undefined values instead of falling through to PNG.
/// </summary>
public sealed class RenderFormatContractTests
{
    private readonly SkiaRenderer _renderer = new();

    [Fact]
    public void EveryRenderFormatMember_EncodesAFileOrIsTheRawBuffer()
    {
        var document = Document();
        foreach (var format in Enum.GetValues<RenderFormat>())
        {
            var result = _renderer.Render(document, new RenderOptions { Format = format });
            switch (format)
            {
                case RenderFormat.Png:
                    Assert.Equal("image/png", result.MimeType);
                    Assert.Equal([0x89, 0x50, 0x4E, 0x47], result.Data.Take(4));
                    break;
                case RenderFormat.Jpeg:
                    Assert.Equal("image/jpeg", result.MimeType);
                    Assert.Equal([0xFF, 0xD8], result.Data.Take(2));
                    break;
                case RenderFormat.Webp:
                    Assert.Equal("image/webp", result.MimeType);
                    Assert.Equal("RIFF"u8.ToArray(), result.Data.Take(4));
                    Assert.Equal("WEBP"u8.ToArray(), result.Data.Skip(8).Take(4));
                    break;
                case RenderFormat.Rgba8:
                    Assert.Equal(RgbaImage.MimeType, result.MimeType);
                    Assert.Equal(result.Width * result.Height * 4, result.Data.Length);
                    Assert.NotEqual([0x89, 0x50, 0x4E, 0x47], result.Data.Take(4));
                    break;
                default:
                    Assert.Fail($"RenderFormat.{format} has no contract case: decide whether it encodes a file or is a raw buffer");
                    break;
            }
        }
    }

    [Fact]
    public void EncodeImage_ThrowsForRgba8AndUndefinedFormats()
    {
        using var surface = SKSurface.Create(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var image = surface.Snapshot();

        Assert.Throws<NotSupportedException>(() => SkiaRenderer.EncodeImage(image, RenderFormat.Rgba8));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkiaRenderer.EncodeImage(image, (RenderFormat)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkiaRenderer.GetMimeType((RenderFormat)42));
        Assert.Equal(RgbaImage.MimeType, SkiaRenderer.GetMimeType(RenderFormat.Rgba8));
    }

    [Fact]
    public void Render_RefusesAnUndefinedFormatBeforeRendering()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _renderer.Render(Document(), new RenderOptions { Format = (RenderFormat)42 }));
    }

    [Fact]
    public void Rgba8_IsUnpremultipliedReadBackOfTheSamePixelsAsPng()
    {
        var document = Document();
        var raw = RgbaImage.FromRender(_renderer.Render(document, new RenderOptions { Format = RenderFormat.Rgba8, Scale = 2 }));
        var png = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Png, Scale = 2 });
        using var bitmap = SKBitmap.Decode(png.Data, new SKImageInfo(raw.Width, raw.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        Assert.Equal((bitmap.Width, bitmap.Height), (raw.Width, raw.Height));
        for (var y = 0; y < raw.Height; y++)
        {
            for (var x = 0; x < raw.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var pixel = raw.Pixel(x, y);
                Assert.Equal((color.Red, color.Green, color.Blue, color.Alpha), (pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }
    }

    private static Document Document() => new()
    {
        Schema = Core.Documents.Document.SchemaUrl,
        Id = Guid.Parse("0c5e1202-0000-4000-8000-000000000000"),
        Name = "format-contract",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = 8, Height = 4 },
        Root = new FrameNode
        {
            Id = Guid.Parse("0c5e1202-0000-4000-8000-0000000000f0"),
            Name = "root",
            Width = 8,
            Height = 4,
            Children =
            [
                new EllipseNode
                {
                    Id = Guid.Parse("0c5e1202-0000-4000-8000-000000000001"),
                    Name = "dot",
                    Order = "a0",
                    Width = 6,
                    Height = 3,
                    Transform = new Transform { X = 1, Y = 0.5 },
                    Style = new Style { Fill = new Fill { Color = "#2563eb" }, Opacity = 0.75 }
                }
            ]
        }
    };
}
