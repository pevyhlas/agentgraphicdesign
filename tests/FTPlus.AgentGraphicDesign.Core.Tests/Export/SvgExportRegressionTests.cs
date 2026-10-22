using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Export;

public class SvgExportRegressionTests
{
    private static readonly string FixturesDir = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "Fixtures");

    private static readonly string GoldenDir = Path.Combine(FixturesDir, "golden");

    private readonly SvgExportAdapter _adapter = new();

    [Fact]
    public async Task BasicShapes_ExportMatchesGolden()
    {
        var fixture = await LoadFixtureAsync("basic-shapes");
        var goldenPath = Path.Combine(GoldenDir, "basic-shapes.svg");

        var result = await _adapter.ExportAsync(fixture, new ExportOptions());
        var actual = Encoding.UTF8.GetString(result.Data);

        if (!File.Exists(goldenPath))
        {
            await File.WriteAllTextAsync(goldenPath, actual);
            Assert.Fail($"Golden file did not exist. Created at: {goldenPath}. Re-run test to verify.");
        }

        var expected = await File.ReadAllTextAsync(goldenPath);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task BasicShapes_ExportIsDeterministic()
    {
        var fixture = await LoadFixtureAsync("basic-shapes");

        var result1 = await _adapter.ExportAsync(fixture, new ExportOptions());
        var result2 = await _adapter.ExportAsync(fixture, new ExportOptions());

        var svg1 = Encoding.UTF8.GetString(result1.Data);
        var svg2 = Encoding.UTF8.GetString(result2.Data);

        Assert.Equal(svg1, svg2);
    }

    [Fact]
    public async Task ExportWithScale_ProducesScaledOutput()
    {
        var fixture = await LoadFixtureAsync("basic-shapes");

        var result = await _adapter.ExportAsync(fixture, new ExportOptions { Scale = 2.0 });
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("width=\"1600\"", svg);
        Assert.Contains("height=\"1200\"", svg);
        Assert.Contains("viewBox=\"0 0 800 600\"", svg);
    }

    [Fact]
    public async Task ExportWithBackground_IncludesBackgroundRect()
    {
        var fixture = await LoadFixtureAsync("basic-shapes");

        var result = await _adapter.ExportAsync(fixture, new ExportOptions { Background = "#ffffff" });
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>", svg);
    }

    [Fact]
    public async Task Export_ProducesValidSvgStructure()
    {
        var fixture = await LoadFixtureAsync("basic-shapes");

        var result = await _adapter.ExportAsync(fixture, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", svg);
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
        var rootStart = svg.IndexOf("<svg ", StringComparison.Ordinal);
        var rootTag = svg[rootStart..(svg.IndexOf('>', rootStart) + 1)];
        Assert.DoesNotContain("version=", rootTag);
        Assert.EndsWith("</svg>", svg.TrimEnd());
    }

    [Fact]
    public async Task Export_IncludesAllVisibleNodes()
    {
        var fixture = await LoadFixtureAsync("basic-shapes");

        var result = await _adapter.ExportAsync(fixture, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("data-name=\"red-square\"", svg);
        Assert.Contains("data-name=\"blue-circle\"", svg);
        Assert.Contains("data-name=\"title\"", svg);
    }

    // ---- Text layout (roadmap 0.2) ----

    [Fact]
    public async Task Export_TextWithoutLayoutFields_KeepsSingleLineForm()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Name = "title", Text = "Hello, World!", FontFamily = "Arial", FontSize = 24, FontWeight = 700 });

        var svg = await ExportSvgAsync(document);

        Assert.Contains("<text data-name=\"title\" y=\"24\" font-size=\"24\" font-family=\"Arial\" font-weight=\"700\" fill=\"#000000\">Hello, World!</text>", svg);
        Assert.DoesNotContain("tspan", svg);
        Assert.DoesNotContain("text-anchor", svg);
        Assert.DoesNotContain("letter-spacing", svg);
    }

    [Fact]
    public async Task Export_ExplicitNewlines_EmitOneTspanPerLine()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Text = "one\ntwo\nthree", FontSize = 20 });

        var svg = await ExportSvgAsync(document);

        Assert.Contains("<text y=\"20\" font-size=\"20\" font-weight=\"400\" fill=\"#000000\">"
            + "<tspan x=\"0\" dy=\"0\">one</tspan><tspan x=\"0\" dy=\"24\">two</tspan><tspan x=\"0\" dy=\"24\">three</tspan></text>", svg);
    }

    [Fact]
    public async Task Export_WrappedText_UsesMeasurerAndLineHeight()
    {
        var document = CreateMinimalDocument();
        // Estimator: 6 px per glyph at fontSize 10; 55 px wraps every word.
        document.Root.Children.Add(new TextNode { Text = "hello world again", FontSize = 10, MaxWidth = 55, LineHeight = 2 });

        var svg = await ExportSvgAsync(document);

        Assert.Contains("<tspan x=\"0\" dy=\"0\">hello</tspan><tspan x=\"0\" dy=\"20\">world</tspan><tspan x=\"0\" dy=\"20\">again</tspan>", svg);
    }

    [Fact]
    public async Task Export_WrappedText_UsesInjectedMeasurer()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Text = "aa bb cc", FontSize = 10, MaxWidth = 50 });
        var wide = new SvgExportAdapter(new FixedWidthMeasurer(20));
        var narrow = new SvgExportAdapter(new FixedWidthMeasurer(5));

        var wideSvg = Encoding.UTF8.GetString((await wide.ExportAsync(document, new ExportOptions())).Data);
        var narrowSvg = Encoding.UTF8.GetString((await narrow.ExportAsync(document, new ExportOptions())).Data);

        Assert.Contains("<tspan x=\"0\" dy=\"0\">aa</tspan><tspan x=\"0\" dy=\"12\">bb</tspan><tspan x=\"0\" dy=\"12\">cc</tspan>", wideSvg);
        Assert.Contains(">aa bb cc</text>", narrowSvg);
    }

    [Fact]
    public async Task Export_EmptyLine_RollsItsPitchIntoTheNextTspan()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Text = "first\n\nthird", FontSize = 10 });

        var svg = await ExportSvgAsync(document);

        Assert.Contains("<tspan x=\"0\" dy=\"0\">first</tspan><tspan x=\"0\" dy=\"24\">third</tspan>", svg);
    }

    [Theory]
    [InlineData("center", "middle")]
    [InlineData("end", "end")]
    public async Task Export_TextAlign_MapsToTextAnchor(string align, string anchor)
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Text = "Hi", FontSize = 16, TextAlign = align });

        var svg = await ExportSvgAsync(document);

        Assert.Contains($"font-weight=\"400\" text-anchor=\"{anchor}\"", svg);
    }

    [Fact]
    public async Task Export_LetterSpacing_EmitsAttribute()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Text = "Hi", FontSize = 16, LetterSpacing = 1.25 });

        var svg = await ExportSvgAsync(document);

        Assert.Contains("font-weight=\"400\" letter-spacing=\"1.25\"", svg);
    }

    [Theory]
    [InlineData("top", "20")]
    [InlineData("baseline", "0")]
    [InlineData("middle", "-4")]
    [InlineData("bottom", "-28")]
    public async Task Export_VerticalAlign_ShiftsTheYAttribute(string align, string expectedY)
    {
        var document = CreateMinimalDocument();
        // Two lines × 1.2 × 20 = 48 px block; y = fontSize + offset.
        document.Root.Children.Add(new TextNode { Text = "a\nb", FontSize = 20, VerticalAlign = align });

        var svg = await ExportSvgAsync(document);

        Assert.Contains($"<text y=\"{expectedY}\" font-size=\"20\"", svg);
    }

    [Fact]
    public async Task Export_TokenLineHeight_AppliesUnlessNodeOverrides()
    {
        var document = CreateMinimalDocument();
        document.Tokens = new DesignTokens
        {
            Typography = new Dictionary<string, TypographyToken> { ["body"] = new() { LineHeight = 2 } }
        };
        document.Root.Children.Add(new TextNode { Name = "token", Text = "a\nb", FontSize = 10, TypographyTokenRef = "body" });
        document.Root.Children.Add(new TextNode { Name = "node", Text = "a\nb", FontSize = 10, TypographyTokenRef = "body", LineHeight = 3, Order = "a1" });

        var svg = await ExportSvgAsync(document);

        Assert.Contains("data-name=\"token\" y=\"10\" font-size=\"10\" font-weight=\"400\" fill=\"#000000\"><tspan x=\"0\" dy=\"0\">a</tspan><tspan x=\"0\" dy=\"20\">b</tspan>", svg);
        Assert.Contains("data-name=\"node\" y=\"10\" font-size=\"10\" font-weight=\"400\" fill=\"#000000\"><tspan x=\"0\" dy=\"0\">a</tspan><tspan x=\"0\" dy=\"30\">b</tspan>", svg);
    }

    [Fact]
    public async Task Export_WrappedTextExample_ValidatesAndWraps()
    {
        var fixture = await LoadFixtureAsync("wrapped-text");

        var svg = await ExportSvgAsync(fixture);

        Assert.Contains("text-anchor=\"middle\"", svg);
        Assert.Contains("text-anchor=\"end\"", svg);
        Assert.Contains("<tspan x=\"0\" dy=\"0\">", svg);
        Assert.Contains("<tspan x=\"0\" dy=\"24\">", svg);
    }

    private async Task<string> ExportSvgAsync(Document document)
    {
        var result = await _adapter.ExportAsync(document, new ExportOptions());
        return Encoding.UTF8.GetString(result.Data);
    }

    private sealed class FixedWidthMeasurer(double widthPerCharacter) : Core.Layout.ITextMeasurer
    {
        public Core.Layout.TextMeasurement Measure(Core.Layout.TextMeasurementRequest request, CancellationToken cancellationToken = default) => new()
        {
            Width = request.Text.Length * widthPerCharacter,
            Ascent = request.FontSize,
            Descent = request.FontSize * 0.2,
            LineHeight = request.FontSize * 1.2,
            Measured = true
        };
    }

    private static async Task<Document> LoadFixtureAsync(string name)
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var path = Path.Combine(examplesDir, $"{name}.agd.json");

        var json = await File.ReadAllTextAsync(path);
        return DocumentSerializer.Deserialize(json)
               ?? throw new InvalidOperationException($"Failed to load fixture: {name}");
    }

    // ---- Security: SVG injection prevention ----

    [Theory]
    [InlineData("red\" onload=\"alert(1)")]
    [InlineData("#ff0000\" onclick=\"evil()")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("url(https://example.test/paint.svg#x)")]
    public async Task Export_RejectsUnsafePaintValues(string maliciousFill)
    {
        var doc = CreateDocumentWithFill(maliciousFill);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions()));
    }

    [Fact]
    public async Task Export_UrlFillValue_IsRejected()
    {
        var doc = CreateDocumentWithFill("url(#gradient)");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions()));
    }

    [Theory]
    [InlineData("blue\" onload=\"alert(1)")]
    [InlineData("#0000ff\" onclick=\"evil()")]
    public async Task Export_RejectsUnsafeStrokeColor(string maliciousStroke)
    {
        var doc = CreateDocumentWithStroke(maliciousStroke);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions()));
    }

    [Theory]
    [InlineData("white\" onload=\"alert(1)")]
    [InlineData("#ffffff\"><script>")]
    public async Task Export_RejectsUnsafeBackgroundColor(string maliciousBg)
    {
        var doc = CreateMinimalDocument();
        doc.Canvas.Background = maliciousBg;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions()));
    }

    [Theory]
    [InlineData("white\" onload=\"alert(1)")]
    public async Task Export_RejectsUnsafeOptionsBackground(string maliciousBg)
    {
        var doc = CreateMinimalDocument();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions { Background = maliciousBg }));
    }

    [Fact]
    public async Task Export_ValidColors_WorkNormally()
    {
        var doc = CreateDocumentWithFill("#ff0000");

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("fill=\"#ff0000\"", svg);
    }

    [Fact]
    public async Task Export_StrokeOnlyFilledPrimitive_HasNoImplicitFill()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "outline",
            Width = 20,
            Height = 20,
            Style = new Style { Stroke = new Stroke { Color = "#000000", Width = 2 } }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("data-name=\"outline\" width=\"20\" height=\"20\" fill=\"none\" stroke=\"#000000\"", svg);
    }

    [Fact]
    public async Task Export_UnsafeQuotedPaint_IsRejected()
    {
        var doc = CreateDocumentWithFill("red\" bad=\"");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions()));
    }

    [Fact]
    public async Task Export_LineNode_RendersCorrectly()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new LineNode
        {
            Id = Guid.NewGuid(),
            Name = "diagonal",
            X1 = 10,
            Y1 = 20,
            X2 = 80,
            Y2 = 90,
            Style = new Style
            {
                Stroke = new Stroke { Color = "#ff0000", Width = 2 }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<line", svg);
        Assert.Contains("data-name=\"diagonal\"", svg);
        Assert.Contains("x1=\"10\"", svg);
        Assert.Contains("y1=\"20\"", svg);
        Assert.Contains("x2=\"80\"", svg);
        Assert.Contains("y2=\"90\"", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
    }

    [Fact]
    public async Task Export_ClosedPolygonNode_RendersAsPolygon()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new PolygonNode
        {
            Id = Guid.NewGuid(),
            Name = "triangle",
            Closed = true,
            Points = [
                new Point { X = 0, Y = 0 },
                new Point { X = 50, Y = 100 },
                new Point { X = 100, Y = 0 }
            ],
            Style = new Style
            {
                Fill = new Fill { Color = "#00ff00" }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<polygon", svg);
        Assert.Contains("data-name=\"triangle\"", svg);
        Assert.Contains("points=\"0,0 50,100 100,0\"", svg);
        Assert.Contains("fill=\"#00ff00\"", svg);
    }

    [Fact]
    public async Task Export_OpenPolygonNode_RendersAsPolyline()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new PolygonNode
        {
            Id = Guid.NewGuid(),
            Name = "path",
            Closed = false,
            Points = [
                new Point { X = 10, Y = 10 },
                new Point { X = 50, Y = 50 },
                new Point { X = 90, Y = 10 }
            ],
            Style = new Style
            {
                Stroke = new Stroke { Color = "#0000ff", Width = 2 }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<polyline", svg);
        Assert.Contains("data-name=\"path\"", svg);
        Assert.Contains("points=\"10,10 50,50 90,10\"", svg);
        Assert.Contains("stroke=\"#0000ff\"", svg);
    }

    [Fact]
    public async Task Export_RemoteImageNode_IsRejectedByDefault()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new ImageNode
        {
            Id = Guid.NewGuid(),
            Name = "photo",
            Src = "https://example.com/image.png",
            Width = 100,
            Height = 75,
            Transform = new Transform { X = 20, Y = 30 }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(doc, new ExportOptions()));
    }

    [Fact]
    public async Task Export_RemoteImageNode_RequiresExplicitOptIn()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new ImageNode
        {
            Name = "photo",
            Src = "https://example.com/image.png",
            Width = 100,
            Height = 75
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions { AllowExternalResources = true });
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains(" href=\"https://example.com/image.png\"", svg);
        Assert.DoesNotContain("xlink", svg);
    }

    [Fact]
    public async Task Export_ImageNode_EscapesSrcAttribute()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new ImageNode
        {
            Id = Guid.NewGuid(),
            Name = "xss-attempt",
            Src = "images/img.png\" onload=\"alert(1)",
            Width = 100,
            Height = 100
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("href=\"images/img.png&quot; onload=&quot;alert(1)\"", svg);
        Assert.DoesNotContain("onload=\"alert", svg);
    }

    private static Document CreateMinimalDocument()
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = "test",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 100, Height = 100 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = 100,
                Height = 100,
                Children = []
            }
        };
    }

    private static Document CreateDocumentWithFill(string fillColor)
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Fill = new Fill { Color = fillColor }
            }
        });
        return doc;
    }

    private static Document CreateDocumentWithStroke(string strokeColor)
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Stroke = new Stroke { Color = strokeColor }
            }
        });
        return doc;
    }

    // ---- Style: Gradients ----

    [Fact]
    public async Task Export_LinearGradient_RendersDefsAndUrl()
    {
        var doc = CreateMinimalDocument();
        var rectId = Guid.NewGuid();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = rectId,
            Name = "gradient-rect",
            Width = 100,
            Height = 50,
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
                            new GradientStop { Offset = 0, Color = "#ff0000" },
                            new GradientStop { Offset = 1, Color = "#0000ff" }
                        ]
                    }
                }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<defs>", svg);
        Assert.Contains("<linearGradient", svg);
        Assert.Contains("x1=\"0%\"", svg);
        Assert.Contains("x2=\"100%\"", svg);
        Assert.Contains("<stop offset=\"0%\" stop-color=\"#ff0000\"/>", svg);
        Assert.Contains("<stop offset=\"100%\" stop-color=\"#0000ff\"/>", svg);
        Assert.Contains($"fill=\"url(#grad-{rectId:N})\"", svg);
    }

    [Fact]
    public async Task Export_RadialGradient_RendersDefsAndUrl()
    {
        var doc = CreateMinimalDocument();
        var rectId = Guid.NewGuid();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = rectId,
            Name = "radial-rect",
            Width = 100,
            Height = 100,
            Style = new Style
            {
                Fill = new Fill
                {
                    Gradient = new RadialGradient
                    {
                        Cx = 0.5,
                        Cy = 0.5,
                        R = 0.5,
                        Stops =
                        [
                            new GradientStop { Offset = 0, Color = "#ffffff" },
                            new GradientStop { Offset = 1, Color = "#000000", Opacity = 0.5 }
                        ]
                    }
                }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<radialGradient", svg);
        Assert.Contains("cx=\"50%\"", svg);
        Assert.Contains("cy=\"50%\"", svg);
        Assert.Contains("r=\"50%\"", svg);
        Assert.Contains("<stop offset=\"0%\" stop-color=\"#ffffff\"/>", svg);
        Assert.Contains("stop-opacity=\"0.5\"", svg);
    }

    // ---- Style: Stroke Properties ----

    [Theory]
    [InlineData(StrokeCap.Round, "stroke-linecap=\"round\"")]
    [InlineData(StrokeCap.Square, "stroke-linecap=\"square\"")]
    public async Task Export_StrokeCap_RendersCorrectAttribute(StrokeCap cap, string expectedAttr)
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Stroke = new Stroke { Color = "#000000", Cap = cap }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains(expectedAttr, svg);
    }

    [Theory]
    [InlineData(StrokeJoin.Round, "stroke-linejoin=\"round\"")]
    [InlineData(StrokeJoin.Bevel, "stroke-linejoin=\"bevel\"")]
    public async Task Export_StrokeJoin_RendersCorrectAttribute(StrokeJoin join, string expectedAttr)
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Stroke = new Stroke { Color = "#000000", Join = join }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains(expectedAttr, svg);
    }

    [Fact]
    public async Task Export_StrokeDash_RendersCorrectAttribute()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Stroke = new Stroke
                {
                    Color = "#000000",
                    DashArray = [5, 2, 3],
                    DashOffset = 1.5
                }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("stroke-dasharray=\"5 2 3\"", svg);
        Assert.Contains("stroke-dashoffset=\"1.5\"", svg);
    }

    [Fact]
    public async Task Export_StrokeMiterLimit_RendersWhenNotDefault()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Stroke = new Stroke { Color = "#000000", Join = StrokeJoin.Miter, MiterLimit = 8 }
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("stroke-miterlimit=\"8\"", svg);
    }

    // ---- Style: Blend Modes ----

    [Theory]
    [InlineData("multiply")]
    [InlineData("screen")]
    [InlineData("overlay")]
    public async Task Export_BlendMode_RendersMixBlendMode(string blendMode)
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Fill = new Fill { Color = "#ff0000" },
                BlendMode = blendMode
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains($"mix-blend-mode: {blendMode}", svg);
    }

    [Fact]
    public async Task Export_UnsupportedBlendMode_DoesNotRender()
    {
        var doc = CreateMinimalDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 50,
            Height = 50,
            Style = new Style
            {
                Fill = new Fill { Color = "#ff0000" },
                BlendMode = "unsupported-mode"
            }
        });

        var result = await _adapter.ExportAsync(doc, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.DoesNotContain("mix-blend-mode", svg);
    }

    [Fact]
    public async Task Export_PreCancelled_ReturnsCancelledTask()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _adapter.ExportAsync(CreateMinimalDocument(), new ExportOptions(), cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1001)]
    public async Task Export_InvalidScale_IsRejected(double scale)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _adapter.ExportAsync(CreateMinimalDocument(), new ExportOptions { Scale = scale }));
    }

    [Fact]
    public async Task Export_NonFiniteCanvasDimension_IsRejected()
    {
        var document = CreateMinimalDocument();
        document.Canvas.Width = double.NaN;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _adapter.ExportAsync(document, new ExportOptions()));
    }

    [Fact]
    public async Task Export_ResolvesColorAliasAndTypographyWithoutMutatingDocument()
    {
        var document = CreateMinimalDocument();
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["base"] = "#123456",
                ["semantic"] = "{base}"
            },
            Typography = new Dictionary<string, TypographyToken>
            {
                ["heading"] = new() { FontFamily = "Inter", FontSize = 28, FontWeight = 700 }
            }
        };
        var rectangle = new RectangleNode
        {
            Width = 20,
            Height = 20,
            Style = new Style { Fill = new Fill { TokenRef = "{semantic}" } }
        };
        var text = new TextNode
        {
            Text = "Title",
            FontFamily = "serif",
            FontSize = 10,
            FontWeight = 400,
            TypographyTokenRef = "{heading}"
        };
        document.Root.Children.Add(rectangle);
        document.Root.Children.Add(text);
        var sourceBefore = DocumentSerializer.Serialize(document);

        var result = await _adapter.ExportAsync(document, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("fill=\"#123456\"", svg);
        Assert.Contains("font-family=\"Inter\"", svg);
        Assert.Contains("font-size=\"28\"", svg);
        Assert.Contains("font-weight=\"700\"", svg);
        Assert.Null(rectangle.Style!.Fill!.Color);
        Assert.Equal("serif", text.FontFamily);
        Assert.Equal(10, text.FontSize);
        Assert.Equal(sourceBefore, DocumentSerializer.Serialize(document));
    }

    [Fact]
    public async Task Export_MissingOrCyclicColorToken_UsesSafeBlackFallback()
    {
        var document = CreateMinimalDocument();
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["a"] = "{b}",
                ["b"] = "{a}"
            }
        };
        document.Root.Children.Add(new RectangleNode
        {
            Width = 10,
            Height = 10,
            Style = new Style { Fill = new Fill { TokenRef = "{a}" } }
        });

        var result = await _adapter.ExportAsync(document, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("fill=\"#000000\"", svg);
        Assert.DoesNotContain("fill=\"{", svg);
    }

    [Fact]
    public async Task Export_UnstyledPrimitiveDefaults_AreExplicitAndConsistent()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new RectangleNode { Width = 10, Height = 10 });
        document.Root.Children.Add(new LineNode { X1 = 0, Y1 = 0, X2 = 10, Y2 = 10 });

        var result = await _adapter.ExportAsync(document, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<rect width=\"10\" height=\"10\" fill=\"#000000\"", svg);
        Assert.Contains("<line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"10\" fill=\"none\" stroke=\"#000000\"", svg);
    }

    [Fact]
    public async Task Export_FrameStyle_IsEmittedOnContainer()
    {
        var document = CreateMinimalDocument();
        document.Root.Style = new Style { Opacity = 0.5 };

        var result = await _adapter.ExportAsync(document, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("fill=\"none\" opacity=\"0.5\"", svg);
    }

    [Fact]
    public async Task Export_ImageStyleAndStretchSemantics_ArePreserved()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new ImageNode
        {
            Src = "assets/photo.png",
            Width = 80,
            Height = 40,
            Style = new Style { Opacity = 0.25, BlendMode = "multiply" }
        });

        var result = await _adapter.ExportAsync(document, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("preserveAspectRatio=\"none\"", svg);
        Assert.Contains("opacity=\"0.25\"", svg);
        Assert.Contains("mix-blend-mode: multiply", svg);
    }

    [Fact]
    public async Task Export_TextUsesTopOriginBaselineOffset()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new TextNode { Text = "Text", FontSize = 24 });

        var result = await _adapter.ExportAsync(document, new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains("<text y=\"24\" font-size=\"24\"", svg);
    }

    [Fact]
    public async Task Export_UsesLfLineEndingsOnly()
    {
        var result = await _adapter.ExportAsync(CreateMinimalDocument(), new ExportOptions());
        var svg = Encoding.UTF8.GetString(result.Data);

        Assert.Contains('\n', svg);
        Assert.DoesNotContain('\r', svg);
    }

    [Fact]
    public async Task Export_OverPathSourceBudget_IsRejectedBeforeOutputGrowth()
    {
        var document = CreateMinimalDocument();
        document.Root.Children.Add(new PathNode
        {
            D = new string('0', SvgExportAdapter.MaxPathDataCharacters + 1)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(document, new ExportOptions()));
    }

    [Fact]
    public async Task Export_CyclicSceneGraph_IsRejected()
    {
        var document = CreateMinimalDocument();
        var group = new GroupNode();
        group.Children.Add(group);
        document.Root.Children.Add(group);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExportAsync(document, new ExportOptions()));
    }
}
