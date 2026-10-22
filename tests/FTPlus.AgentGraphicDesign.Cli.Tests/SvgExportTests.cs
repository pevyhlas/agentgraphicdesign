using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests;

public class SvgExportTests
{
    [Fact]
    public async Task Export_EmptyDocument_ProducesSvgWithBackground()
    {
        var document = CreateDocument("test", 800, 600, "#ffffff");
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.Contains("width=\"800\"", svg);
        Assert.Contains("height=\"600\"", svg);
        Assert.Contains("fill=\"#ffffff\"", svg);
    }

    [Fact]
    public async Task Export_WithScale_AppliesScaling()
    {
        var document = CreateDocument("test", 800, 600);
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions { Scale = 2.0 });

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("width=\"1600\"", svg);
        Assert.Contains("height=\"1200\"", svg);
        Assert.Contains("viewBox=\"0 0 800 600\"", svg);
    }

    [Fact]
    public async Task Export_Rectangle_ProducesRectElement()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect1",
                Width = 100,
                Height = 50,
                CornerRadius = 8,
                Style = new Style { Fill = new Fill { Color = "#ff0000" } }
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<rect", svg);
        Assert.Contains("width=\"100\"", svg);
        Assert.Contains("height=\"50\"", svg);
        Assert.Contains("rx=\"8\"", svg);
        Assert.Contains("fill=\"#ff0000\"", svg);
    }

    [Fact]
    public async Task Export_Ellipse_ProducesEllipseElement()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new EllipseNode
            {
                Id = Guid.NewGuid(),
                Name = "circle",
                Width = 100,
                Height = 100,
                Style = new Style { Fill = new Fill { Color = "#0000ff" } }
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<ellipse", svg);
        Assert.Contains("cx=\"50\"", svg);
        Assert.Contains("cy=\"50\"", svg);
        Assert.Contains("rx=\"50\"", svg);
        Assert.Contains("ry=\"50\"", svg);
    }

    [Fact]
    public async Task Export_Text_ProducesTextElement()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new TextNode
            {
                Id = Guid.NewGuid(),
                Name = "label",
                Text = "Hello, World!",
                FontFamily = "Arial",
                FontSize = 24,
                FontWeight = 700
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<text", svg);
        Assert.Contains("font-size=\"24\"", svg);
        Assert.Contains("font-family=\"Arial\"", svg);
        Assert.Contains("font-weight=\"700\"", svg);
        Assert.Contains("Hello, World!", svg);
    }

    [Fact]
    public async Task Export_Path_ProducesPathElement()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new PathNode
            {
                Id = Guid.NewGuid(),
                Name = "path1",
                D = "M 0 0 L 100 100 Z"
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<path", svg);
        Assert.Contains("d=\"M 0 0 L 100 100 Z\"", svg);
    }

    [Fact]
    public async Task Export_NodeWithTransform_AppliesTransform()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Width = 100,
                Height = 100,
                Transform = new Transform { X = 50, Y = 25, Rotation = 45 }
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("transform=\"translate(50, 25) rotate(45)\"", svg);
    }

    [Fact]
    public async Task Export_HiddenNode_IsNotRendered()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "hidden-rect",
                Visible = false,
                Width = 100,
                Height = 100
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.DoesNotContain("hidden-rect", svg);
    }

    [Fact]
    public async Task Export_StrokeStyle_IncludesStrokeAttributes()
    {
        var document = CreateDocument("test", 800, 600);
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Stroke = new Stroke { Color = "#000000", Width = 2 }
                }
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions());

        var svg = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("stroke=\"#000000\"", svg);
        Assert.Contains("stroke-width=\"2\"", svg);
    }

    [Fact]
    public void Adapter_HasCorrectCapabilities()
    {
        var adapter = new SvgExportAdapter();

        Assert.Equal("svg", adapter.Id);
        Assert.Equal(ExportCapabilities.Vector, adapter.Capabilities);
    }

    [Fact]
    public async Task Export_SameDocument_ProducesDeterministicOutput()
    {
        var document = CreateDocument("test", 800, 600);
        document.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        document.Root.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Width = 100,
                Height = 100
            }
        ];
        var adapter = new SvgExportAdapter();

        var result1 = await adapter.ExportAsync(document, new ExportOptions());
        var result2 = await adapter.ExportAsync(document, new ExportOptions());

        Assert.Equal(result1.Data, result2.Data);
    }

    private static Document CreateDocument(string name, double width, double height, string? background = null)
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = name,
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = width, Height = height, Background = background },
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
}
