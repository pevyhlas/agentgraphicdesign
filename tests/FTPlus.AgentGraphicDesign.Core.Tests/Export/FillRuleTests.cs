using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Export;

public class FillRuleTests
{
    private const string Ring = "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z";

    [Fact]
    public void Serialize_FillRule_RoundTrips()
    {
        var document = CreateDocument(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "ring",
            Order = "a0",
            D = Ring,
            Style = new Style { Fill = new Fill { Color = "#000000" }, FillRule = FillRules.EvenOdd }
        });

        var json = DocumentSerializer.Serialize(document);
        var deserialized = DocumentSerializer.Deserialize(json);

        Assert.Contains("\"fillRule\"", json);
        Assert.Contains("\"evenodd\"", json);
        var path = Assert.IsType<PathNode>(Assert.Single(deserialized!.Root.Children));
        Assert.Equal("evenodd", path.Style!.FillRule);
    }

    [Fact]
    public void Serialize_NullFillRule_IsOmittedFromCanonicalJson()
    {
        var document = CreateDocument(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "plain",
            Order = "a0",
            D = Ring,
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var json = DocumentSerializer.Serialize(document);

        Assert.DoesNotContain("fillRule", json);
        Assert.Null(DocumentSerializer.Deserialize(json)!.Root.Children[0].Style!.FillRule);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("nonzero", true)]
    [InlineData("evenodd", true)]
    [InlineData("EvenOdd", false)]
    [InlineData("even-odd", false)]
    [InlineData("", false)]
    public void FillRules_IsValid_AcceptsOnlyCanonicalValues(string? value, bool expected)
    {
        Assert.Equal(expected, FillRules.IsValid(value));
    }

    [Theory]
    [InlineData("nonzero", "nonzero")]
    [InlineData("EVENODD", "evenodd")]
    public void FillRules_TryNormalize_ProducesCanonicalLowercase(string value, string expected)
    {
        Assert.True(FillRules.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("even-odd")]
    [InlineData(" evenodd ")]
    [InlineData("")]
    [InlineData(null)]
    public void FillRules_TryNormalize_RejectsUnknownValues(string? value)
    {
        Assert.False(FillRules.TryNormalize(value, out _));
    }

    [Fact]
    public async Task Export_EvenOddPath_EmitsFillRuleAttribute()
    {
        var document = CreateDocument(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "ring",
            Order = "a0",
            D = Ring,
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = "evenodd" }
        });

        var svg = await ExportSvg(document, new ExportOptions());

        Assert.Contains("<path", svg);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nonzero")]
    public async Task Export_NonZeroPath_OmitsFillRuleAttribute(string? fillRule)
    {
        var document = CreateDocument(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "ring",
            Order = "a0",
            D = Ring,
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = fillRule }
        });

        var svg = await ExportSvg(document, new ExportOptions());

        Assert.Contains("<path", svg);
        Assert.DoesNotContain("fill-rule", svg);
    }

    [Fact]
    public async Task Export_EvenOddPolygon_EmitsFillRuleAttribute()
    {
        var document = CreateDocument(new PolygonNode
        {
            Id = Guid.NewGuid(),
            Name = "star",
            Order = "a0",
            Points =
            [
                new Point { X = 60, Y = 10 }, new Point { X = 89.39, Y = 100.45 }, new Point { X = 12.45, Y = 44.55 },
                new Point { X = 107.55, Y = 44.55 }, new Point { X = 30.61, Y = 100.45 }
            ],
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = "evenodd" }
        });

        var svg = await ExportSvg(document, new ExportOptions());

        Assert.Contains("<polygon", svg);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
    }

    [Fact]
    public async Task Export_EvenOddOnRectangle_IsNotEmitted()
    {
        var document = CreateDocument(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "box",
            Order = "a0",
            Width = 40,
            Height = 40,
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = "evenodd" }
        });

        var svg = await ExportSvg(document, new ExportOptions());

        Assert.Contains("<rect", svg);
        Assert.DoesNotContain("fill-rule", svg);
    }

    [Fact]
    public async Task Export_SymbolFragment_EmitsFillRuleAttribute()
    {
        var document = CreateDocument(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "ring",
            Order = "a0",
            D = Ring,
            Style = new Style { Fill = new Fill { Color = "#000000" }, FillRule = "evenodd" }
        });

        var svg = await ExportSvg(document, new ExportOptions { SymbolId = "ic-ring" });

        Assert.StartsWith("<symbol id=\"ic-ring\"", svg);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
    }

    private static async Task<string> ExportSvg(Document document, ExportOptions options)
    {
        var result = await new SvgExportAdapter().ExportAsync(document, options);
        return Encoding.UTF8.GetString(result.Data);
    }

    private static Document CreateDocument(Node child) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "fill-rule",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 120, Height = 120 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = 120,
                Height = 120,
                Children = [child]
            }
        };
}
