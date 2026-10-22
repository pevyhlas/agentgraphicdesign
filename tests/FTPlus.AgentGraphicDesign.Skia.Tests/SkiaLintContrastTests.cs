using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

public class SkiaLintContrastTests
{
    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 24)]
    [InlineData(false, 16)]
    public void SharedTranslucentContainer_SkipsContrastRatherThanMisreporting(bool isGroup, double fontSize)
    {
        Node ancestor = isGroup ? new GroupNode() : new FrameNode { Width = 200, Height = 100 };
        ancestor.Style = new Style { Opacity = 0.5 };
        var container = (IContainerNode)ancestor;
        container.Children.Add(new RectangleNode
        {
            Width = 200,
            Height = 100,
            Order = "a0",
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });
        container.Children.Add(new TextNode
        {
            Text = "Hello",
            FontSize = fontSize,
            Order = "a1",
            Transform = new Transform { X = 10, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#ffffff" } }
        });
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "shared-opacity-contrast",
            Canvas = new Canvas { Width = 200, Height = 100, Background = "#ffffff" },
            Root = new FrameNode { Width = 200, Height = 100, Children = [ancestor] }
        };
        using var measurer = new SkiaTextMeasurer();
        var engine = new DocumentEngine(textMeasurer: measurer);

        var render = new SkiaRenderer().Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(render.Data);
        Assert.NotNull(bitmap);

        var background = bitmap.GetPixel(5, 5);
        Assert.InRange(background.Red, (byte)127, (byte)128);
        Assert.Equal(background.Red, background.Green);
        Assert.Equal(background.Red, background.Blue);
        Assert.Equal((byte)255, background.Alpha);
        Assert.Contains(SKColors.White, bitmap.Pixels);
        var ratio = WcagContrast.Ratio(SrgbColor.White,
            new SrgbColor(background.Red / 255.0, background.Green / 255.0, background.Blue / 255.0, 1));
        Assert.InRange(ratio, 3.9, 4.1);
        Assert.Equal(fontSize >= 24, ratio >= (fontSize >= 24 ? 3 : 4.5));

        // Even the normal-size case is skipped, not approved by a supported contrast calculation.
        Assert.DoesNotContain(engine.Lint(document).Diagnostics, d => d.Code == LintCodes.TextContrast);
    }

    [Fact]
    public void WrappedTextExample_PaintsCardBackgroundAndPassesContrast()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "wrapped-text.agd.json");
        var document = DocumentSerializer.Deserialize(File.ReadAllText(path));
        Assert.NotNull(document);
        using var measurer = new SkiaTextMeasurer();
        var engine = new DocumentEngine(textMeasurer: measurer);

        var render = new SkiaRenderer().Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(render.Data);
        Assert.NotNull(bitmap);

        Assert.Equal(new SKColor(0xf4, 0xf4, 0xf5), bitmap.GetPixel(20, 20));
        Assert.Equal(SKColors.White, bitmap.GetPixel(48, 48));
        Assert.DoesNotContain(engine.Lint(document).Diagnostics, d => d.Code == LintCodes.TextContrast);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrameFill_IsNotBackgroundUnlessRectanglePaintsIt(bool paintedBackground)
    {
        var text = new TextNode
        {
            Text = "Hello",
            FontSize = 24,
            Order = "a1",
            Transform = new Transform { X = 10, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#ffffff" } }
        };
        var frame = new FrameNode
        {
            Width = 160,
            Height = 80,
            Transform = new Transform { X = 20, Y = 20 },
            Style = new Style { Fill = new Fill { Color = "#000000" } },
            Children = [text]
        };
        if (paintedBackground)
        {
            // Added after text in the list, but painted first by sibling order.
            frame.Children.Add(new RectangleNode
            {
                Width = 160,
                Height = 80,
                Order = "a0",
                Style = new Style { Fill = new Fill { Color = "#000000" } }
            });
        }
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "frame-contrast",
            Canvas = new Canvas { Width = 200, Height = 120, Background = "#ffffff" },
            Root = new FrameNode { Width = 200, Height = 120, Children = [frame] }
        };
        using var measurer = new SkiaTextMeasurer();
        var engine = new DocumentEngine(textMeasurer: measurer);

        var render = new SkiaRenderer().Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(render.Data);
        Assert.NotNull(bitmap);
        var findings = engine.Lint(document).Diagnostics.Where(d => d.Code == LintCodes.TextContrast).ToList();

        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
        Assert.Equal(paintedBackground ? SKColors.Black : SKColors.White, bitmap.GetPixel(25, 25));
        if (paintedBackground)
        {
            Assert.Empty(findings);
            var textPixels = from y in Enumerable.Range(30, 30)
                             from x in Enumerable.Range(30, 70)
                             select bitmap.GetPixel(x, y);
            Assert.Contains(SKColors.White, textPixels);
        }
        else
        {
            Assert.All(bitmap.Pixels, pixel => Assert.Equal(SKColors.White, pixel));
            var finding = Assert.Single(findings);
            Assert.Equal(text.Id, finding.NodeId);
            Assert.Equal("canvas", finding.Details["backgroundSource"]);
            Assert.Equal(1.0, finding.Details["ratio"]);
        }
    }
}
