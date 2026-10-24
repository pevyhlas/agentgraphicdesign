using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

/// <summary>
/// Feature 012 AC-01 (FR-001, plan C1): a document of axis-aligned opaque and translucent
/// rectangles rendered as <see cref="RenderFormat.Rgba8"/> equals a hand-derived byte array and the
/// Core managed decoder's output for the same document's PNG render; plus the
/// <see cref="RgbaImage"/> contract.
/// </summary>
public sealed class Rgba8RenderTests
{
    private readonly SkiaRenderer _renderer = new();

    // 6x2 canvas, transparent background:
    //   x 0-1, both rows: opaque #2563eb
    //   x 2-3, both rows: #ff6300 under a 0.5 style opacity (layer alpha round(0.5·255) = 128)
    //   x 4,   row 0:     rgba(255, 0, 255, 0.25) (paint alpha round(0.25·255) = 64)
    //   everything else:  transparent
    private static Document Fixture()
    {
        var document = new Document
        {
            Schema = Document.SchemaUrl,
            Id = Guid.Parse("0c5e1201-0000-4000-8000-000000000000"),
            Name = "rgba8-fixture",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 6, Height = 2 },
            Root = new FrameNode { Id = Guid.Parse("0c5e1201-0000-4000-8000-0000000000f0"), Name = "root", Width = 6, Height = 2 }
        };
        document.Root.Children.Add(Rect("0c5e1201-0000-4000-8000-000000000001", "a0", 0, 2, 2, "#2563eb", 1));
        document.Root.Children.Add(Rect("0c5e1201-0000-4000-8000-000000000002", "a1", 2, 2, 2, "#ff6300", 0.5));
        document.Root.Children.Add(Rect("0c5e1201-0000-4000-8000-000000000003", "a2", 4, 1, 1, "rgba(255, 0, 255, 0.25)", 1));
        return document;
    }

    private static RectangleNode Rect(string id, string order, double x, double width, double height, string color, double opacity) => new()
    {
        Id = Guid.Parse(id),
        Name = order,
        Order = order,
        Width = width,
        Height = height,
        Transform = new Transform { X = x },
        Style = new Style { Fill = new Fill { Color = color }, Opacity = opacity }
    };

    [Fact]
    public void Rgba8_EqualsTheHandDerivedPixelArray()
    {
        var result = _renderer.Render(Fixture(), new RenderOptions { Format = RenderFormat.Rgba8 });

        Assert.Equal(RgbaImage.MimeType, result.MimeType);
        Assert.Equal(6, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(6 * 2 * 4, result.Data.Length);
        var image = RgbaImage.FromRender(result);

        var opaque = new RgbaPixel(0x25, 0x63, 0xeb, 0xff);
        var clear = new RgbaPixel(0, 0, 0, 0);
        // Row-major, top-down: row 0 then row 1.
        RgbaPixel?[] expected =
        [
            opaque, opaque, null, null, new RgbaPixel(0xff, 0x00, 0xff, 64), clear,
            opaque, opaque, null, null, clear, clear
        ];
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                var actual = image.Pixel(x, y);
                if (expected[y * 6 + x] is { } exact)
                {
                    // α 0 and 255 exact; the 64-alpha magenta channels are 0 or 255, which the recorded
                    // rule maps back exactly (SkiaRule(64, 64) = 255, SkiaRule(0, 64) = 0).
                    Assert.Equal(exact, actual);
                    continue;
                }

                // The 0.5-opacity layer: α is exactly 128; R 0xff and B 0x00 come back exactly; G
                // 0x63 goes through an unknown premultiply rounding, so it must be the recorded rule
                // applied to floor or ceil of 0x63·128/255 (and so within ±1 of 0x63).
                Assert.Equal(128, actual.A);
                Assert.Equal(0xff, actual.R);
                Assert.Equal(0x00, actual.B);
                var exactPremultiplied = 0x63 * 128 / 255.0;
                var candidates = new[] { (int)Math.Floor(exactPremultiplied), (int)Math.Ceiling(exactPremultiplied) }
                    .Select(p => Rgba8UnpremultiplyCharacterizationTests.SkiaRule(p, 128));
                Assert.Contains((int)actual.G, candidates);
                Assert.InRange(actual.G, 0x63 - 1, 0x63 + 1);
            }
        }
        Assert.Equal(64, image.Alpha(4, 0));
        Assert.Equal(0, image.Alpha(4, 1));
    }

    [Fact]
    public void Rgba8_EqualsTheManagedDecoderOfThePngRenderOnEveryPixel()
    {
        // T003 records that the PNG encoder unpremultiplies identically on every (α, c ≤ α) pair, so
        // the decoder oracle applies to every pixel, not only to α 0 and 255.
        var document = Fixture();
        var raw = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Rgba8, Scale = 3 });
        var png = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Png, Scale = 3 });

        Assert.Equal((18, 6), (raw.Width, raw.Height));
        var decoded = PngRgbaVerifier.DecodeOwned(png.Data, png.Width, png.Height, static () => { }, static _ => { });
        Assert.Equal(decoded.Rgba, raw.Data);
    }

    [Fact]
    public void Rgba8_HonoursBackgroundAndScaleLikeEveryOtherFormat()
    {
        var result = _renderer.Render(Fixture(), new RenderOptions { Format = RenderFormat.Rgba8, Background = "#ffffff", Scale = 2 });
        var image = RgbaImage.FromRender(result);

        Assert.Equal((12, 4), (image.Width, image.Height));
        Assert.Equal(new RgbaPixel(0xff, 0xff, 0xff, 0xff), image.Pixel(11, 3));
        Assert.Equal(new RgbaPixel(0x25, 0x63, 0xeb, 0xff), image.Pixel(3, 3));
        Assert.All(Enumerable.Range(0, 12), x => Assert.Equal(255, image.Alpha(x, 0)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(1, 7)]
    [InlineData(7, 1)]
    [InlineData(8192, 1)]
    [InlineData(1, 8192)]
    public void Rgba8_OddThinAndBoundarySidesHaveNoPadding(int width, int height)
    {
        var document = Fixture();
        document.Canvas = new Canvas { Width = width, Height = height };
        document.Root.Width = width;
        document.Root.Height = height;
        document.Root.Children.Clear();
        var marker = Rect("0c5e1201-0000-4000-8000-000000000001", "a0", width - 1, 1, 1, "#e07020", 1);
        marker.Transform = new Transform { X = width - 1, Y = height - 1 };
        document.Root.Children.Add(marker);
        var options = new RenderOptions { Format = RenderFormat.Rgba8, Background = "#2563eb" };

        var raw = _renderer.Render(document, options);
        var image = RgbaImage.FromRender(raw);
        Assert.Equal((width, height), (image.Width, image.Height));
        Assert.Equal(width * height * 4, raw.Data.Length);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = x == width - 1 && y == height - 1
                    ? new RgbaPixel(0xe0, 0x70, 0x20, 255)
                    : new RgbaPixel(0x25, 0x63, 0xeb, 255);
                Assert.Equal(expected, image.Pixel(x, y));
            }
        }

        var png = _renderer.Render(document, options with { Format = RenderFormat.Png });
        Assert.Equal(PngRgbaVerifier.DecodeOwned(png.Data, width, height, static () => { }, static _ => { }).Rgba, raw.Data);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(8193, 1)]
    [InlineData(1, 8193)]
    [InlineData(-1, 1)]
    public void RgbaImage_RefusesSidesOutsideTheRendererLimits(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbaImage(width, height, ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void RgbaImage_RefusesMoreThanTheRendererPixelCount()
    {
        // 8192 x 7813 = 64 004 096 > 64 000 000; checked before the buffer length.
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new RgbaImage(8192, 7813, ReadOnlyMemory<byte>.Empty));
        Assert.Contains("64000000", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void RgbaImage_RefusesABufferOfTheWrongLength(int length)
    {
        var error = Assert.Throws<ArgumentException>(() => new RgbaImage(2, 2, new byte[length]));
        Assert.Equal("pixels", error.ParamName);
    }

    [Fact]
    public void RgbaImage_ReadsPixelsRowMajorTopDownAndRefusesOutOfRangeCoordinates()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24];
        var image = new RgbaImage(3, 2, bytes);

        Assert.Equal(new RgbaPixel(1, 2, 3, 4), image.Pixel(0, 0));
        Assert.Equal(new RgbaPixel(9, 10, 11, 12), image.Pixel(2, 0));
        Assert.Equal(new RgbaPixel(13, 14, 15, 16), image.Pixel(0, 1));
        Assert.Equal(24, image.Alpha(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Pixel(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Pixel(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Alpha(-1, 0));
    }

    [Fact]
    public void RgbaImage_FromRender_AcceptsOnlyRgba8Results()
    {
        var png = _renderer.Render(Fixture(), new RenderOptions { Format = RenderFormat.Png });

        var error = Assert.Throws<ArgumentException>(() => RgbaImage.FromRender(png));
        Assert.Contains("image/png", error.Message);
        Assert.Throws<ArgumentException>(() => RgbaImage.FromRender(new RenderResult
        {
            Data = new byte[4],
            MimeType = RgbaImage.MimeType,
            Width = 2,
            Height = 1
        }));
    }
}
