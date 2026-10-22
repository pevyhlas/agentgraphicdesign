using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Export;

public class SymbolExportTests
{
    [Fact]
    public async Task Export_WithSymbolId_ProducesBareSymbolFragment()
    {
        var document = CreateDocument("ic-power", 32, 32);
        document.Root.Children =
        [
            new PathNode
            {
                Id = Guid.NewGuid(),
                Name = "bolt",
                D = "M18 2 L6 18 L14 18 L12 30 L26 12 L16 12 Z",
                Style = new Style { Fill = new Fill { Color = "#000000" } }
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-power" });

        var svg = Encoding.UTF8.GetString(result.Data);
        Assert.StartsWith("<symbol id=\"ic-power\" viewBox=\"0 0 32 32\">", svg);
        Assert.EndsWith("</symbol>\n", svg);
        Assert.DoesNotContain("<?xml", svg);
        Assert.DoesNotContain("<svg", svg);
        Assert.DoesNotContain("xmlns", svg);
        Assert.Contains("<path", svg);
        Assert.Contains("fill=\"#000000\"", svg);
        Assert.Equal("image/svg+xml", result.MimeType);
        Assert.Equal("ic-power.symbol.svg", result.FileName);
    }

    [Fact]
    public async Task Export_WithSymbolId_IsDeterministic()
    {
        var document = CreateDocument("ic-a", 32, 32);
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "r",
                Width = 10,
                Height = 10,
                Style = new Style { Fill = new Fill { Color = "#123456" } }
            }
        ];
        var adapter = new SvgExportAdapter();

        var first = await adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-a" });
        var second = await adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-a" });

        Assert.Equal(first.Data, second.Data);
    }

    [Fact]
    public async Task Export_SymbolWithGradient_EmitsDefsInsideSymbol()
    {
        var document = CreateDocument("ic-grad", 32, 32);
        var nodeId = Guid.NewGuid();
        document.Root.Children =
        [
            new RectangleNode
            {
                Id = nodeId,
                Name = "g",
                Width = 32,
                Height = 32,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            X1 = 0, Y1 = 0, X2 = 1, Y2 = 1,
                            Stops =
                            [
                                new GradientStop { Offset = 0, Color = "#000000" },
                                new GradientStop { Offset = 1, Color = "#ffffff" }
                            ]
                        }
                    }
                }
            }
        ];
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-grad" });

        var svg = Encoding.UTF8.GetString(result.Data);
        var defsIndex = svg.IndexOf("<defs>", StringComparison.Ordinal);
        Assert.True(defsIndex > 0, "defs missing from symbol fragment");
        Assert.True(defsIndex > svg.IndexOf("<symbol", StringComparison.Ordinal));
        Assert.Contains($"linearGradient id=\"grad-{nodeId:N}\"", svg);
        Assert.Contains($"fill=\"url(#grad-{nodeId:N})\"", svg);
    }

    [Fact]
    public async Task Export_SymbolWithCanvasBackground_EmitsBackgroundRectInsideSymbol()
    {
        var document = CreateDocument("ic-bg", 32, 32, "#ff0000");
        var adapter = new SvgExportAdapter();

        var result = await adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-bg" });

        var svg = Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<rect width=\"100%\" height=\"100%\" fill=\"#ff0000\"/>", svg);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1starts-with-digit")]
    [InlineData("-starts-with-dash")]
    [InlineData("has space")]
    [InlineData("has\"quote")]
    [InlineData("has<angle")]
    public async Task Export_InvalidSymbolId_Throws(string symbolId)
    {
        var document = CreateDocument("ic-x", 32, 32);
        var adapter = new SvgExportAdapter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.ExportAsync(document, new ExportOptions { SymbolId = symbolId }));
    }

    [Fact]
    public async Task Export_OverlongSymbolId_Throws()
    {
        var document = CreateDocument("ic-x", 32, 32);
        var adapter = new SvgExportAdapter();
        var symbolId = "a" + new string('b', SvgExportAdapter.MaxSymbolIdCharacters);

        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.ExportAsync(document, new ExportOptions { SymbolId = symbolId }));
    }

    [Fact]
    public async Task Export_SymbolWithSizingOptions_Throws()
    {
        var document = CreateDocument("ic-x", 32, 32);
        var adapter = new SvgExportAdapter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-x", Scale = 2.0 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-x", Width = 64 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.ExportAsync(document, new ExportOptions { SymbolId = "ic-x", Height = 64 }));
    }

    [Theory]
    [InlineData("ic-power")]
    [InlineData("Icon_2")]
    [InlineData("a")]
    public void IsValidSymbolId_AcceptsWellFormedIds(string symbolId) =>
        Assert.True(SvgExportAdapter.IsValidSymbolId(symbolId));

    [Fact]
    public void IsValidSymbolId_RejectsNull() =>
        Assert.False(SvgExportAdapter.IsValidSymbolId(null));

    private static Document CreateDocument(string name, double width, double height, string? background = null) =>
        new()
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
