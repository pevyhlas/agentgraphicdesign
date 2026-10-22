using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// The Skia measurer must agree with the shaping engine the renderer uses, and world
/// bounds reported by the engine must match where pixels actually land.
/// </summary>
public class SkiaTextMeasurerTests : IDisposable
{
    private readonly SkiaTextMeasurer _measurer = new();
    private readonly TextShapingService _shaping = new();

    public void Dispose()
    {
        _measurer.Dispose();
        _shaping.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Measure_WidthMatchesShapedText()
    {
        var options = new TextShapingOptions { FontSize = 24 };
        var shaped = _shaping.ShapeText("Hello world", options);

        var measurement = _measurer.Measure(new TextMeasurementRequest { Text = "Hello world", FontSize = 24 });

        Assert.True(measurement.Measured);
        Assert.InRange(measurement.Width, shaped.TotalWidth - 0.5, shaped.TotalWidth + 0.5);
        Assert.True(measurement.Ascent > 0, "ascent should be positive above the baseline");
        Assert.True(measurement.Descent >= 0);
        Assert.Equal(measurement.Ascent + measurement.Descent, measurement.LineHeight, 3);
    }

    [Fact]
    public void Measure_EmptyText_HasZeroWidthButLineHeight()
    {
        var measurement = _measurer.Measure(new TextMeasurementRequest { Text = "", FontSize = 16 });

        Assert.True(measurement.Measured);
        Assert.Equal(0, measurement.Width);
        Assert.True(measurement.LineHeight > 0);
    }

    [Fact]
    public void Measure_InvalidFontSize_FallsBackToEstimate()
    {
        var measurement = _measurer.Measure(new TextMeasurementRequest { Text = "abc", FontSize = 0 });

        Assert.False(measurement.Measured);
        Assert.NotNull(measurement.Diagnostic);
    }

    [Fact]
    public void Engine_TextBounds_UseRealMeasurer()
    {
        var engine = new DocumentEngine(textMeasurer: _measurer);
        var document = CreateEmptyDocument();
        var text = new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = "Hello world", FontSize = 24 };
        document.Root.Children.Add(text);
        var shaped = _shaping.ShapeText("Hello world", new TextShapingOptions { FontSize = 24 });
        var metrics = _shaping.GetFontMetrics(new TextShapingOptions { FontSize = 24 });

        var bounds = Assert.Single(engine.GetBounds(document, text.Id));

        Assert.Equal(true, bounds.Measured);
        Assert.Empty(bounds.Diagnostics);
        var local = bounds.World!.Value;
        Assert.InRange(local.Width, shaped.TotalWidth - 0.5, shaped.TotalWidth + 0.5);
        // The renderer draws the baseline at y = fontSize, so the box top sits at fontSize - ascent.
        Assert.Equal(24 + metrics.Ascent, local.Y, 3);
        Assert.Equal(metrics.Descent - metrics.Ascent, local.Height, 3);
    }

    [Fact]
    public void RenderedRectangle_PixelExtentsMatchReportedWorldBounds_RotatedGroup()
    {
        var engine = new DocumentEngine(textMeasurer: _measurer);
        var document = CreateEmptyDocument();
        var group = new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "group",
            Order = "a0",
            Transform = new Transform { X = 50, Y = 50, Rotation = 90 },
            Children = []
        };
        var rect = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Order = "a0",
            Width = 10,
            Height = 20,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        };
        group.Children.Add(rect);
        document.Root.Children.Add(group);

        var world = engine.GetBounds(document, rect.Id).Single().World!.Value;
        Assert.Equal(new BoundingBox(30, 50, 20, 10), world);

        AssertPixelExtents(document, world);
    }

    [Fact]
    public void RenderedRectangle_PixelExtentsMatchReportedWorldBounds_ScaledGroup()
    {
        var engine = new DocumentEngine(textMeasurer: _measurer);
        var document = CreateEmptyDocument();
        var group = new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "group",
            Order = "a0",
            Transform = new Transform { X = 20, Y = 30, ScaleX = 2, ScaleY = 2 },
            Children = []
        };
        var rect = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Order = "a0",
            Width = 10,
            Height = 10,
            Transform = new Transform { X = 5, Y = 5 },
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        };
        group.Children.Add(rect);
        document.Root.Children.Add(group);

        var world = engine.GetBounds(document, rect.Id).Single().World!.Value;
        Assert.Equal(new BoundingBox(30, 40, 20, 20), world);

        AssertPixelExtents(document, world);
    }

    [Fact]
    public void Measure_LetterSpacing_AddsSpacingPerGlyph()
    {
        var plain = _measurer.Measure(new TextMeasurementRequest { Text = "HHHH", FontSize = 24 });
        var spaced = _measurer.Measure(new TextMeasurementRequest { Text = "HHHH", FontSize = 24, LetterSpacing = 4 });

        Assert.True(spaced.Measured);
        Assert.Equal(plain.Width + 16, spaced.Width, 2);
    }

    [Fact]
    public void Engine_WrappedTextBounds_EveryLineFitsMaxWidth()
    {
        var engine = new DocumentEngine(textMeasurer: _measurer);
        var document = CreateEmptyDocument(400, 400);
        const string content = "The quick brown fox jumps over the lazy dog and keeps running through the field";
        var text = new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = content, FontSize = 16, MaxWidth = 140, LineHeight = 1.5 };
        document.Root.Children.Add(text);
        var options = new TextShapingOptions { FontSize = 16 };

        var bounds = Assert.Single(engine.GetBounds(document, text.Id));
        var lines = TextLayout.BreakLines(content, 140, line => _shaping.ShapeText(line, options).TotalWidth);

        Assert.Equal(true, bounds.Measured);
        Assert.True(lines.Count >= 4, $"expected several lines, got {lines.Count}");
        Assert.All(lines, line => Assert.True(_shaping.ShapeText(line, options).TotalWidth <= 140));
        var local = bounds.Local!.Value;
        Assert.True(local.Width <= 140 + 0.01);
        Assert.Equal(lines.Max(line => _shaping.ShapeText(line, options).TotalWidth), local.Width, 2);
        var metrics = _shaping.GetFontMetrics(options);
        Assert.Equal((lines.Count - 1) * 24 + (metrics.Descent - metrics.Ascent), local.Height, 2);
    }

    [Fact]
    public void Engine_CenteredTextBounds_MatchRenderedInk()
    {
        var engine = new DocumentEngine(textMeasurer: _measurer);
        var document = CreateEmptyDocument(300, 100);
        var text = new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Order = "a0",
            Text = "HHHH",
            FontSize = 32,
            TextAlign = "center",
            Transform = new Transform { X = 150, Y = 20 },
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        };
        document.Root.Children.Add(text);

        var world = engine.GetBounds(document, text.Id).Single().World!.Value;
        var result = new SkiaRenderer().Render(document, new RenderOptions { Format = RenderFormat.Png });
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var inkLeft = Enumerable.Range(0, bitmap.Width).First(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).Alpha > 0));
        var inkRight = Enumerable.Range(0, bitmap.Width).Last(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).Alpha > 0));
        Assert.Equal(150, world.CenterX, 1);
        Assert.InRange(inkLeft, world.X - 1, world.X + 4);
        Assert.InRange(inkRight, world.Right - 5, world.Right + 1);
    }

    private static void AssertPixelExtents(Document document, BoundingBox world)
    {
        var renderer = new SkiaRenderer();
        var result = renderer.Render(document, new RenderOptions { Format = RenderFormat.Png });
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var left = (int)world.X;
        var top = (int)world.Y;
        var right = (int)world.Right - 1;
        var bottom = (int)world.Bottom - 1;

        AssertFilled(bitmap, left, top);
        AssertFilled(bitmap, right, top);
        AssertFilled(bitmap, left, bottom);
        AssertFilled(bitmap, right, bottom);
        AssertFilled(bitmap, (left + right) / 2, (top + bottom) / 2);

        AssertEmpty(bitmap, left - 1, (top + bottom) / 2);
        AssertEmpty(bitmap, right + 1, (top + bottom) / 2);
        AssertEmpty(bitmap, (left + right) / 2, top - 1);
        AssertEmpty(bitmap, (left + right) / 2, bottom + 1);
    }

    private static void AssertFilled(SKBitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.True(pixel.Alpha > 200 && pixel.Red > 200, $"expected filled pixel at ({x},{y}) but found {pixel}");
    }

    private static void AssertEmpty(SKBitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.True(pixel.Alpha < 30, $"expected empty pixel at ({x},{y}) but found {pixel}");
    }

    private static Document CreateEmptyDocument(double width = 200, double height = 200) => new()
    {
        Schema = Document.SchemaUrl,
        Id = Guid.NewGuid(),
        Name = "test",
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
