using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Export;

public class CurrentColorExportTests
{
    [Fact]
    public async Task Export_CurrentColor_RewritesMatchingFillAndStrokeOnly()
    {
        var document = CreateDocument(
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "ink",
                Order = "a0",
                Width = 40,
                Height = 40,
                Style = new Style
                {
                    Fill = new Fill { Color = "#000000" },
                    Stroke = new Stroke { Color = "#000000", Width = 2 }
                }
            },
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "accent",
                Order = "a1",
                Width = 40,
                Height = 40,
                Style = new Style
                {
                    Fill = new Fill { Color = "#ff0000" },
                    Stroke = new Stroke { Color = "#00ff00", Width = 2 }
                }
            });

        var svg = await ExportSvg(document, new ExportOptions { CurrentColor = "#000000" });

        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.Contains("stroke=\"currentColor\"", svg);
        Assert.Contains("fill=\"#ff0000\"", svg);
        Assert.Contains("stroke=\"#00ff00\"", svg);
        Assert.DoesNotContain("#000000", svg);
    }

    [Fact]
    public async Task Export_WithoutCurrentColor_LeavesPaintUntouched()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "ink",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var svg = await ExportSvg(document, new ExportOptions());

        Assert.Contains("fill=\"#000000\"", svg);
        Assert.DoesNotContain("currentColor", svg);
    }

    [Fact]
    public async Task Export_CurrentColor_ComparesNormalizedRgbaValues()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "ink",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style
            {
                Fill = new Fill { Color = "#000" },
                Stroke = new Stroke { Color = "rgb(0, 0, 0)", Width = 2 }
            }
        });

        var svg = await ExportSvg(document, new ExportOptions { CurrentColor = "#000000" });

        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.Contains("stroke=\"currentColor\"", svg);
    }

    [Fact]
    public async Task Export_CurrentColor_RequiresExactAlphaMatch()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "ink",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style { Fill = new Fill { Color = "#00000080" } }
        });

        var svg = await ExportSvg(document, new ExportOptions { CurrentColor = "#000000" });

        Assert.DoesNotContain("currentColor", svg);
    }

    [Fact]
    public async Task Export_CurrentColor_MatchesTokenResolvedPaint()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "ink",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style
            {
                Fill = new Fill { TokenRef = "ink" },
                Stroke = new Stroke { TokenRef = "{accent}", Width = 2 }
            }
        });
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["ink"] = "#000000",
                ["accent"] = "#ff0000"
            }
        };

        var svg = await ExportSvg(document, new ExportOptions { CurrentColor = "#000000" });

        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
    }

    [Fact]
    public async Task Export_CurrentColor_AppliesToImplicitBlackDefaultFill()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "plain",
            Order = "a0",
            Width = 40,
            Height = 40
        });

        var svg = await ExportSvg(document, new ExportOptions { CurrentColor = "#000000" });

        Assert.Contains("fill=\"currentColor\"", svg);
    }

    [Fact]
    public async Task Export_CurrentColor_LeavesGradientStopsAndBackgroundAlone()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "grad",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style
            {
                Fill = new Fill
                {
                    Gradient = new LinearGradient
                    {
                        X1 = 0,
                        Y1 = 0,
                        X2 = 1,
                        Y2 = 0,
                        Stops =
                        [
                            new GradientStop { Offset = 0, Color = "#000000" },
                            new GradientStop { Offset = 1, Color = "#ffffff" }
                        ]
                    }
                }
            }
        });
        document.Canvas.Background = "#000000";

        var svg = await ExportSvg(document, new ExportOptions { CurrentColor = "#000000" });

        Assert.Contains("stop-color=\"#000000\"", svg);
        Assert.Contains("<rect width=\"100%\" height=\"100%\" fill=\"#000000\"/>", svg);
        Assert.DoesNotContain("currentColor", svg);
    }

    [Fact]
    public async Task Export_CurrentColor_AppliesInsideSymbolFragment()
    {
        var document = CreateDocument(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "bolt",
            Order = "a0",
            D = "M18 2 L6 18 L14 18 L12 30 L26 12 L16 12 Z",
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var svg = await ExportSvg(document, new ExportOptions { SymbolId = "ic-bolt", CurrentColor = "#000000" });

        Assert.StartsWith("<symbol id=\"ic-bolt\"", svg);
        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.DoesNotContain("#000000", svg);
    }

    [Theory]
    [InlineData("")]
    [InlineData("black")]
    [InlineData("#12")]
    [InlineData("rgb(0,0)")]
    public async Task Export_InvalidCurrentColor_Throws(string currentColor)
    {
        var document = CreateDocument();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => new SvgExportAdapter().ExportAsync(document, new ExportOptions { CurrentColor = currentColor }));

        Assert.Contains("Current color", exception.Message);
    }

    private static async Task<string> ExportSvg(Document document, ExportOptions options)
    {
        var result = await new SvgExportAdapter().ExportAsync(document, options);
        return Encoding.UTF8.GetString(result.Data);
    }

    private static Document CreateDocument(params Node[] children) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "current-color",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 100, Height = 100 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = 100,
                Height = 100,
                Children = [.. children]
            }
        };
}
