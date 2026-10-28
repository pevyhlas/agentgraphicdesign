using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Validation;

public class DocumentValidatorTests
{
    [Fact]
    public void BasicShapesFixture_ValidatesClean()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var basicShapesPath = Path.Combine(examplesDir, "basic-shapes.agd.json");

        var json = File.ReadAllText(basicShapesPath);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid, $"Expected valid document but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FullFeaturedFixture_ValidatesClean()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var fullFeaturedPath = Path.Combine(examplesDir, "full-featured.agd.json");

        var json = File.ReadAllText(fullFeaturedPath);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid, $"Expected valid document but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InvalidSceneFixture_ProducesExpectedErrors()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var invalidPath = Path.Combine(examplesDir, "invalid-scene.agd.json");

        var json = File.ReadAllText(invalidPath);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var result = DocumentValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DOC001"); // Document ID is empty (all zeros)
        Assert.Contains(result.Errors, e => e.Code == "DOC002"); // Document name is empty
        Assert.Contains(result.Errors, e => e.Code == "DOC003"); // Version < 1
        Assert.Contains(result.Errors, e => e.Code == "DOC004"); // Revision < 1
        Assert.Contains(result.Errors, e => e.Code == "CANVAS001"); // Canvas width <= 0
        Assert.Contains(result.Errors, e => e.Code == "CANVAS002"); // Canvas height <= 0
        Assert.Contains(result.Errors, e => e.Code == "NODE001"); // Node ID empty (root and child both have Guid.Empty)
        Assert.Contains(result.Errors, e => e.Code == "NODE003"); // Node order empty (root has empty order)
        Assert.Contains(result.Errors, e => e.Code == "RECT001"); // Rectangle width < 0
        Assert.Contains(result.Errors, e => e.Code == "RECT003"); // Corner radius < 0
        Assert.Contains(result.Errors, e => e.Code == "STYLE001"); // Opacity out of range
        Assert.Contains(result.Errors, e => e.Code == "STYLE002"); // Invalid fill color
    }

    [Theory]
    [MemberData(nameof(AllErrorCaseKeys))]
    public void Validator_CoversAllErrorCodes(string errorCode, string expectedPath)
    {
        // Documents are not xUnit-serializable. Discover stable code/path keys and create the
        // same negative fixture during execution; TEXT006 has two distinct path cases.
        var doc = (Document)AllErrorCodes().Single(row => (string)row[0] == errorCode && (string)row[2] == expectedPath)[1];
        var result = DocumentValidator.Validate(doc);

        var error = result.Errors.FirstOrDefault(e => e.Code == errorCode);
        Assert.NotNull(error);
        if (expectedPath != null)
        {
            Assert.Contains(expectedPath, error.Path ?? "");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nonzero")]
    [InlineData("evenodd")]
    public void Validate_CanonicalFillRule_IsAccepted(string? fillRule)
    {
        var doc = CreateDocWithChild(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "path",
            Order = "a0",
            D = "M0,0 L10,0 L10,10 Z",
            Style = new Style { FillRule = fillRule }
        });

        var result = DocumentValidator.Validate(doc);

        Assert.DoesNotContain(result.Errors, e => e.Code == "STYLE012");
    }

    [Theory]
    [InlineData("EvenOdd")]
    [InlineData("")]
    public void Validate_NonCanonicalFillRule_IsRejected(string fillRule)
    {
        var doc = CreateDocWithChild(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "path",
            Order = "a0",
            D = "M0,0 L10,0 L10,10 Z",
            Style = new Style { FillRule = fillRule }
        });

        var result = DocumentValidator.Validate(doc);

        Assert.Contains(result.Errors, e => e.Code == "STYLE012" && e.Path!.EndsWith(".style.fillRule", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> AllErrorCaseKeys() => AllErrorCodes().Select(row => new[] { row[0], row[2] });

    public static IEnumerable<object[]> AllErrorCodes()
    {
        // DOC001: Document ID is empty
        yield return new object[]
        {
            "DOC001",
            CreateDoc(d => d.Id = Guid.Empty),
            "id"
        };

        // DOC002: Document name is empty or whitespace
        yield return new object[]
        {
            "DOC002",
            CreateDoc(d => d.Name = ""),
            "name"
        };

        // DOC003: Document version must be >= 1
        yield return new object[]
        {
            "DOC003",
            CreateDoc(d => d.Version = 0),
            "version"
        };

        // DOC004: Document revision must be >= 1
        yield return new object[]
        {
            "DOC004",
            CreateDoc(d => d.Revn = 0),
            "revn"
        };

        // CANVAS001: Canvas width must be > 0
        yield return new object[]
        {
            "CANVAS001",
            CreateDoc(d => d.Canvas.Width = 0),
            "canvas.width"
        };

        // CANVAS002: Canvas height must be > 0
        yield return new object[]
        {
            "CANVAS002",
            CreateDoc(d => d.Canvas.Height = -100),
            "canvas.height"
        };

        // NODE001: Node ID is empty
        yield return new object[]
        {
            "NODE001",
            CreateDoc(d => d.Root.Id = Guid.Empty),
            "root.id"
        };

        // NODE002: Duplicate node ID
        yield return new object[]
        {
            "NODE002",
            CreateDocWithDuplicateIds(),
            ".id"
        };

        // NODE003: Node order is empty
        yield return new object[]
        {
            "NODE003",
            CreateDoc(d => d.Root.Order = ""),
            "root.order"
        };

        // STYLE001: Opacity must be between 0 and 1
        yield return new object[]
        {
            "STYLE001",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Opacity = 1.5 }
            }),
            "style.opacity"
        };

        // STYLE002: Invalid fill color format
        yield return new object[]
        {
            "STYLE002",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Fill = new Fill { Color = "not-a-color" } }
            }),
            "style.fill.color"
        };

        // STYLE003: Invalid stroke color format
        yield return new object[]
        {
            "STYLE003",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "invalid" } }
            }),
            "style.stroke.color"
        };

        // STYLE004: Stroke width cannot be negative
        yield return new object[]
        {
            "STYLE004",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", Width = -1 } }
            }),
            "style.stroke.width"
        };

        // STYLE005: Stroke miter limit must be >= 1
        yield return new object[]
        {
            "STYLE005",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", MiterLimit = 0.5 } }
            }),
            "style.stroke.miterLimit"
        };

        // STYLE006: Dash array values cannot be negative
        yield return new object[]
        {
            "STYLE006",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", DashArray = [5, -2] } }
            }),
            "style.stroke.dashArray"
        };

        // GRADIENT002: Gradient stop offset must be between 0 and 1
        yield return new object[]
        {
            "GRADIENT002",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops = [new GradientStop { Offset = 1.5, Color = "#ff0000" }]
                        }
                    }
                }
            }),
            "style.fill.gradient.stops[0].offset"
        };

        // GRADIENT003: Invalid gradient stop color
        yield return new object[]
        {
            "GRADIENT003",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops = [new GradientStop { Offset = 0, Color = "invalid-color" }]
                        }
                    }
                }
            }),
            "style.fill.gradient.stops[0].color"
        };

        // GRADIENT004: Gradient stop opacity must be between 0 and 1
        yield return new object[]
        {
            "GRADIENT004",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops = [new GradientStop { Offset = 0, Color = "#ff0000", Opacity = -0.5 }]
                        }
                    }
                }
            }),
            "style.fill.gradient.stops[0].opacity"
        };

        // GRADIENT005: Radial gradient radius must be > 0
        yield return new object[]
        {
            "GRADIENT005",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new RadialGradient
                        {
                            R = 0,
                            Stops = [new GradientStop { Offset = 0, Color = "#ff0000" }, new GradientStop { Offset = 1, Color = "#0000ff" }]
                        }
                    }
                }
            }),
            "style.fill.gradient.r"
        };

        // FRAME001: Frame width cannot be negative
        yield return new object[]
        {
            "FRAME001",
            CreateDoc(d => d.Root.Width = -1),
            "root.width"
        };

        // FRAME002: Frame height cannot be negative
        yield return new object[]
        {
            "FRAME002",
            CreateDoc(d => d.Root.Height = -1),
            "root.height"
        };

        // RECT001: Rectangle width cannot be negative
        yield return new object[]
        {
            "RECT001",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = -50,
                Height = 100
            }),
            ".width"
        };

        // RECT002: Rectangle height cannot be negative
        yield return new object[]
        {
            "RECT002",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = -50
            }),
            ".height"
        };

        // RECT003: Corner radius cannot be negative
        yield return new object[]
        {
            "RECT003",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                CornerRadius = -5
            }),
            ".cornerRadius"
        };

        // ELLIPSE001: Ellipse width cannot be negative
        yield return new object[]
        {
            "ELLIPSE001",
            CreateDocWithChild(new EllipseNode
            {
                Id = Guid.NewGuid(),
                Name = "ellipse",
                Order = "a0",
                Width = -50,
                Height = 100
            }),
            ".width"
        };

        // ELLIPSE002: Ellipse height cannot be negative
        yield return new object[]
        {
            "ELLIPSE002",
            CreateDocWithChild(new EllipseNode
            {
                Id = Guid.NewGuid(),
                Name = "ellipse",
                Order = "a0",
                Width = 100,
                Height = -50
            }),
            ".height"
        };

        // TEXT001: Font size must be > 0
        yield return new object[]
        {
            "TEXT001",
            CreateDocWithChild(new TextNode
            {
                Id = Guid.NewGuid(),
                Name = "text",
                Order = "a0",
                Text = "Hello",
                FontSize = 0
            }),
            ".fontSize"
        };

        // IMAGE002: Image width cannot be negative
        yield return new object[]
        {
            "IMAGE002",
            CreateDocWithChild(new ImageNode
            {
                Id = Guid.NewGuid(),
                Name = "image",
                Order = "a0",
                Src = "https://example.com/image.png",
                Width = -100,
                Height = 100
            }),
            ".width"
        };

        // IMAGE003: Image height cannot be negative
        yield return new object[]
        {
            "IMAGE003",
            CreateDocWithChild(new ImageNode
            {
                Id = Guid.NewGuid(),
                Name = "image",
                Order = "a0",
                Src = "https://example.com/image.png",
                Width = 100,
                Height = -100
            }),
            ".height"
        };

        // CANVAS004: Canvas width is not finite
        yield return new object[]
        {
            "CANVAS004",
            CreateDoc(d => d.Canvas.Width = double.NaN),
            "canvas.width"
        };

        // CANVAS005: Canvas width exceeds max raster dimension
        yield return new object[]
        {
            "CANVAS005",
            CreateDoc(d => d.Canvas.Width = DocumentValidator.MaxRasterDimension + 1),
            "canvas.width"
        };

        // CANVAS006: Canvas height is not finite
        yield return new object[]
        {
            "CANVAS006",
            CreateDoc(d => d.Canvas.Height = double.PositiveInfinity),
            "canvas.height"
        };

        // CANVAS007: Canvas height exceeds max raster dimension
        yield return new object[]
        {
            "CANVAS007",
            CreateDoc(d => d.Canvas.Height = DocumentValidator.MaxRasterDimension + 1),
            "canvas.height"
        };

        // NODE004: Node X coordinate is not finite
        yield return new object[]
        {
            "NODE004",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Transform = new Transform { X = double.NaN }
            }),
            ".transform.x"
        };

        // NODE005: Node Y coordinate is not finite
        yield return new object[]
        {
            "NODE005",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Transform = new Transform { Y = double.PositiveInfinity }
            }),
            ".transform.y"
        };

        // RECT004: Rectangle width is not finite
        yield return new object[]
        {
            "RECT004",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = double.NaN,
                Height = 100
            }),
            ".width"
        };

        // ELLIPSE003: Ellipse width is not finite
        yield return new object[]
        {
            "ELLIPSE003",
            CreateDocWithChild(new EllipseNode
            {
                Id = Guid.NewGuid(),
                Name = "ellipse",
                Order = "a0",
                Width = double.NegativeInfinity,
                Height = 100
            }),
            ".width"
        };

        // TEXT003: Font size is not finite
        yield return new object[]
        {
            "TEXT003",
            CreateDocWithChild(new TextNode
            {
                Id = Guid.NewGuid(),
                Name = "text",
                Order = "a0",
                Text = "Hello",
                FontSize = double.NaN
            }),
            ".fontSize"
        };

        // TEXT004: Max width must be > 0
        yield return new object[]
        {
            "TEXT004",
            CreateDocWithChild(new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = "Hello", MaxWidth = 0 }),
            ".maxWidth"
        };

        // TEXT005: Line height must be > 0
        yield return new object[]
        {
            "TEXT005",
            CreateDocWithChild(new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = "Hello", LineHeight = -1 }),
            ".lineHeight"
        };

        // TEXT006: Invalid textAlign / verticalAlign
        yield return new object[]
        {
            "TEXT006",
            CreateDocWithChild(new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = "Hello", TextAlign = "justify" }),
            ".textAlign"
        };
        yield return new object[]
        {
            "TEXT006",
            CreateDocWithChild(new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = "Hello", VerticalAlign = "center" }),
            ".verticalAlign"
        };

        // TEXT007: Letter spacing must be finite
        yield return new object[]
        {
            "TEXT007",
            CreateDocWithChild(new TextNode { Id = Guid.NewGuid(), Name = "text", Order = "a0", Text = "Hello", LetterSpacing = double.NaN }),
            ".letterSpacing"
        };

        // LINE002: Line X1 is not finite
        yield return new object[]
        {
            "LINE002",
            CreateDocWithChild(new LineNode
            {
                Id = Guid.NewGuid(),
                Name = "line",
                Order = "a0",
                X1 = double.NaN,
                Y1 = 0,
                X2 = 100,
                Y2 = 100
            }),
            ".x1"
        };

        // IMAGE004: Image width is not finite
        yield return new object[]
        {
            "IMAGE004",
            CreateDocWithChild(new ImageNode
            {
                Id = Guid.NewGuid(),
                Name = "image",
                Order = "a0",
                Src = "https://example.com/image.png",
                Width = double.PositiveInfinity,
                Height = 100
            }),
            ".width"
        };

        // STYLE008: Stroke width is not finite
        yield return new object[]
        {
            "STYLE008",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", Width = double.NaN } }
            }),
            ".style.stroke.width"
        };

        // GRADIENT010: Gradient stop offset is not finite
        yield return new object[]
        {
            "GRADIENT010",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops =
                            [
                                new GradientStop { Offset = double.NaN, Color = "#ff0000" },
                                new GradientStop { Offset = 1, Color = "#0000ff" }
                            ]
                        }
                    }
                }
            }),
            ".offset"
        };

        // FRAME003: Frame width is not finite (NaN)
        yield return new object[]
        {
            "FRAME003",
            CreateDoc(d => d.Root.Width = double.NaN),
            "root.width"
        };

        // FRAME004: Frame height is not finite (Infinity)
        yield return new object[]
        {
            "FRAME004",
            CreateDoc(d => d.Root.Height = double.PositiveInfinity),
            "root.height"
        };

        // RECT005: Rectangle height is not finite (-Infinity)
        yield return new object[]
        {
            "RECT005",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = double.NegativeInfinity
            }),
            ".height"
        };

        // RECT006: Corner radius is not finite (NaN)
        yield return new object[]
        {
            "RECT006",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                CornerRadius = double.NaN
            }),
            ".cornerRadius"
        };

        // ELLIPSE004: Ellipse height is not finite (NaN)
        yield return new object[]
        {
            "ELLIPSE004",
            CreateDocWithChild(new EllipseNode
            {
                Id = Guid.NewGuid(),
                Name = "ellipse",
                Order = "a0",
                Width = 100,
                Height = double.NaN
            }),
            ".height"
        };

        // STYLE009: Stroke miter limit is not finite (Infinity)
        yield return new object[]
        {
            "STYLE009",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", MiterLimit = double.PositiveInfinity } }
            }),
            ".style.stroke.miterLimit"
        };

        // STYLE010: Dash array value is not finite (NaN)
        yield return new object[]
        {
            "STYLE010",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", DashArray = [5, double.NaN, 3] } }
            }),
            ".style.stroke.dashArray"
        };

        // STYLE011: Dash offset is not finite (NaN)
        yield return new object[]
        {
            "STYLE011",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Stroke = new Stroke { Color = "#000000", DashArray = [5, 3], DashOffset = double.NaN } }
            }),
            ".style.stroke.dashOffset"
        };

        // STYLE012: fillRule must be nonzero or evenodd
        yield return new object[]
        {
            "STYLE012",
            CreateDocWithChild(new PathNode
            {
                Id = Guid.NewGuid(),
                Name = "path",
                Order = "a0",
                D = "M0,0 L10,0 L10,10 Z",
                Style = new Style { FillRule = "even-odd" }
            }),
            ".style.fillRule"
        };

        // GRADIENT011: Gradient stop opacity is not finite (NaN)
        yield return new object[]
        {
            "GRADIENT011",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops =
                            [
                                new GradientStop { Offset = 0, Color = "#ff0000", Opacity = double.NaN },
                                new GradientStop { Offset = 1, Color = "#0000ff" }
                            ]
                        }
                    }
                }
            }),
            ".opacity"
        };

        // IMAGE005: Image height is not finite (NaN)
        yield return new object[]
        {
            "IMAGE005",
            CreateDocWithChild(new ImageNode
            {
                Id = Guid.NewGuid(),
                Name = "image",
                Order = "a0",
                Src = "https://example.com/image.png",
                Width = 100,
                Height = double.NaN
            }),
            ".height"
        };

        // NODE006: Transform rotation is not finite (NaN)
        yield return new object[]
        {
            "NODE006",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Transform = new Transform { Rotation = double.NaN }
            }),
            ".transform.rotation"
        };

        // NODE007: Transform scaleX is not finite (Infinity)
        yield return new object[]
        {
            "NODE007",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Transform = new Transform { ScaleX = double.PositiveInfinity }
            }),
            ".transform.scaleX"
        };

        // NODE008: Transform scaleY is not finite (-Infinity)
        yield return new object[]
        {
            "NODE008",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Transform = new Transform { ScaleY = double.NegativeInfinity }
            }),
            ".transform.scaleY"
        };

        // LINE003: Line Y1 is not finite (NaN)
        yield return new object[]
        {
            "LINE003",
            CreateDocWithChild(new LineNode
            {
                Id = Guid.NewGuid(),
                Name = "line",
                Order = "a0",
                X1 = 0,
                Y1 = double.NaN,
                X2 = 100,
                Y2 = 100
            }),
            ".y1"
        };

        // LINE004: Line X2 is not finite (Infinity)
        yield return new object[]
        {
            "LINE004",
            CreateDocWithChild(new LineNode
            {
                Id = Guid.NewGuid(),
                Name = "line",
                Order = "a0",
                X1 = 0,
                Y1 = 0,
                X2 = double.PositiveInfinity,
                Y2 = 100
            }),
            ".x2"
        };

        // LINE005: Line Y2 is not finite (-Infinity)
        yield return new object[]
        {
            "LINE005",
            CreateDocWithChild(new LineNode
            {
                Id = Guid.NewGuid(),
                Name = "line",
                Order = "a0",
                X1 = 0,
                Y1 = 0,
                X2 = 100,
                Y2 = double.NegativeInfinity
            }),
            ".y2"
        };
    }

    [Theory]
    [MemberData(nameof(AllWarningCaseKeys))]
    public void Validator_CoversAllWarningCodes(string warningCode)
    {
        var doc = (Document)AllWarningCodes().Single(row => (string)row[0] == warningCode)[1];
        var result = DocumentValidator.Validate(doc);

        Assert.Contains(result.Warnings, w => w.Code == warningCode);
    }

    public static IEnumerable<object[]> AllWarningCaseKeys() => AllWarningCodes().Select(row => new[] { row[0] });

    public static IEnumerable<object[]> AllWarningCodes()
    {
        // DOC005: Document version newer than supported
        yield return new object[]
        {
            "DOC005",
            CreateDoc(d => d.Version = Document.SupportedVersion + 1)
        };

        // CANVAS003: Canvas width exceeds 16384
        yield return new object[]
        {
            "CANVAS003",
            CreateDoc(d => d.Canvas.Width = 20000)
        };

        // CANVAS008: Canvas height exceeds 16384
        yield return new object[]
        {
            "CANVAS008",
            CreateDoc(d => d.Canvas.Height = 20000)
        };

        // PATH001: Path data is empty
        yield return new object[]
        {
            "PATH001",
            CreateDocWithChild(new PathNode
            {
                Id = Guid.NewGuid(),
                Name = "path",
                Order = "a0",
                D = ""
            })
        };

        // TEXT002: Font weight should be between 100 and 900
        yield return new object[]
        {
            "TEXT002",
            CreateDocWithChild(new TextNode
            {
                Id = Guid.NewGuid(),
                Name = "text",
                Order = "a0",
                Text = "Hello",
                FontSize = 12,
                FontWeight = 1000
            })
        };

        // STYLE007: Unsupported blend mode
        yield return new object[]
        {
            "STYLE007",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { BlendMode = "unsupported-mode" }
            })
        };

        // GRADIENT001: Gradient should have at least 2 stops
        yield return new object[]
        {
            "GRADIENT001",
            CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops = [new GradientStop { Offset = 0, Color = "#ff0000" }]
                        }
                    }
                }
            })
        };

        // LINE001: Line has zero length
        yield return new object[]
        {
            "LINE001",
            CreateDocWithChild(new LineNode
            {
                Id = Guid.NewGuid(),
                Name = "line",
                Order = "a0",
                X1 = 50,
                Y1 = 50,
                X2 = 50,
                Y2 = 50
            })
        };

        // POLYGON001: Polygon has fewer than 2 points
        yield return new object[]
        {
            "POLYGON001",
            CreateDocWithChild(new PolygonNode
            {
                Id = Guid.NewGuid(),
                Name = "polygon",
                Order = "a0",
                Points = [new Point { X = 0, Y = 0 }]
            })
        };

        // POLYGON002: Closed polygon has fewer than 3 points
        yield return new object[]
        {
            "POLYGON002",
            CreateDocWithChild(new PolygonNode
            {
                Id = Guid.NewGuid(),
                Name = "polygon",
                Order = "a0",
                Closed = true,
                Points = [new Point { X = 0, Y = 0 }, new Point { X = 100, Y = 100 }]
            })
        };

        // IMAGE001: Image source is empty
        yield return new object[]
        {
            "IMAGE001",
            CreateDocWithChild(new ImageNode
            {
                Id = Guid.NewGuid(),
                Name = "image",
                Order = "a0",
                Src = "",
                Width = 100,
                Height = 100
            })
        };
    }

    [Fact]
    public void ValidDocument_PassesValidation()
    {
        var doc = CreateValidDocument();

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidColor_HexFormats_AreAccepted()
    {
        var validColors = new[] { "#fff", "#ffffff", "#ffff", "#ffffffff", "#ABC", "#abcdef" };

        foreach (var color in validColors)
        {
            var doc = CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Fill = new Fill { Color = color } }
            });

            var result = DocumentValidator.Validate(doc);

            Assert.DoesNotContain(result.Errors, e => e.Code == "STYLE002");
        }
    }

    [Fact]
    public void ValidColor_RgbaFormats_AreAccepted()
    {
        var validColors = new[] { "rgb(255, 0, 0)", "rgba(255, 0, 0, 0.5)" };

        foreach (var color in validColors)
        {
            var doc = CreateDocWithChild(new RectangleNode
            {
                Id = Guid.NewGuid(),
                Name = "rect",
                Order = "a0",
                Width = 100,
                Height = 100,
                Style = new Style { Fill = new Fill { Color = color } }
            });

            var result = DocumentValidator.Validate(doc);

            Assert.DoesNotContain(result.Errors, e => e.Code == "STYLE002");
        }
    }

    [Fact]
    public void NestedGroups_AllChildrenValidated()
    {
        var invalidRect = new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "invalid-rect",
            Order = "a0",
            Width = -100,
            Height = 100
        };

        var group = new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "group",
            Order = "a0",
            Children = [invalidRect]
        };

        var doc = CreateDocWithChild(group);

        var result = DocumentValidator.Validate(doc);

        Assert.Contains(result.Errors, e => e.Code == "RECT001" && e.Path!.Contains("children[0]"));
    }

    [Fact]
    public void InvalidTokens_AreReported()
    {
        var document = CreateValidDocument();
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["bad color"] = "rgb(999, 0, 0)",
                ["a"] = "{b}",
                ["b"] = "{a}"
            },
            Typography = new Dictionary<string, TypographyToken>
            {
                ["bad-type"] = new()
                {
                    FontFamily = " ",
                    FontSize = double.NaN,
                    FontWeight = 999,
                    LineHeight = -1
                }
            }
        };

        var result = DocumentValidator.Validate(document);

        Assert.Contains(result.Errors, error => error.Code == "TOKEN001");
        Assert.Contains(result.Errors, error => error.Code == "TOKEN002");
        Assert.Contains(result.Errors, error => error.Code == "TOKEN003");
        Assert.Contains(result.Errors, error => error.Code == "TOKEN005");
    }

    [Fact]
    public void ColorTokenAliasDepth_IsBoundedWithoutRecursiveValidation()
    {
        var document = CreateValidDocument();
        var colors = new Dictionary<string, string>();
        for (var i = 0; i <= DocumentLimits.MaxTokenAliasDepth; i++)
            colors[$"level.{i}"] = i == DocumentLimits.MaxTokenAliasDepth ? "#000000" : $"level.{i + 1}";
        document.Tokens = new DesignTokens { Colors = colors };

        var result = DocumentValidator.Validate(document);

        Assert.Contains(result.Errors, error => error.Code == "TOKEN003" && error.Path == "tokens.colors.level.0");
    }

    [Fact]
    public void ExcessiveColorTokenCount_IsRejectedBeforeAliasValidation()
    {
        var document = CreateValidDocument();
        document.Tokens = new DesignTokens
        {
            Colors = Enumerable.Range(0, DocumentLimits.MaxDesignTokensPerCategory + 1)
                .ToDictionary(index => $"token.{index}", _ => "#000000")
        };

        var result = DocumentValidator.Validate(document);

        var error = Assert.Single(result.Errors, error => error.Code == "RESOURCE002");
        Assert.Equal("tokens.colors", error.Path);
    }

    [Fact]
    public void InvalidColorTokenDiagnostics_AreBounded()
    {
        var document = CreateValidDocument();
        document.Tokens = new DesignTokens
        {
            Colors = Enumerable.Range(0, DocumentLimits.MaxTokenValidationDiagnostics + 10)
                .ToDictionary(index => $"token.{index}", _ => "not a valid value")
        };

        var result = DocumentValidator.Validate(document);

        Assert.Equal(DocumentLimits.MaxTokenValidationDiagnostics, result.Errors.Count(error => error.Code == "TOKEN002"));
        Assert.Contains(result.Errors, error => error.Code == "RESOURCE003" && error.Path == "tokens");
    }

    [Fact]
    public void MissingNodeTokenReferenceDiagnostics_AreBounded()
    {
        var document = CreateValidDocument();
        document.Root.Children = Enumerable.Range(0, DocumentLimits.MaxTokenValidationDiagnostics + 10)
            .Select(index => (Node)new RectangleNode
            {
                Id = Guid.NewGuid(),
                Order = $"a{index}",
                Width = 1,
                Height = 1,
                Style = new Style { Fill = new Fill { TokenRef = "missing" } }
            })
            .ToList();

        var result = DocumentValidator.Validate(document);

        Assert.Equal(DocumentLimits.MaxTokenValidationDiagnostics, result.Errors.Count(error => error.Code == "TOKEN006"));
        Assert.Contains(result.Errors, error => error.Code == "RESOURCE003" && error.Path == "tokens");
    }

    [Fact]
    public void PersistedStringDiagnostics_AreBounded()
    {
        var document = CreateValidDocument();
        document.Tokens = new DesignTokens
        {
            Colors = Enumerable.Range(0, DocumentLimits.MaxPersistedStringDiagnostics + 10)
                .ToDictionary(
                    index => $"token.{index}",
                    _ => new string('x', DocumentLimits.MaxTokenReferenceLength + 1))
        };

        var result = DocumentValidator.Validate(document);

        Assert.Equal(DocumentLimits.MaxPersistedStringDiagnostics, result.Errors.Count(error => error.Code == "RESOURCE001"));
        Assert.Contains(result.Errors, error => error.Code == "RESOURCE004");
    }

    [Fact]
    public void StrictColorGrammar_RejectsMalformedRgb()
    {
        var document = CreateDocWithChild(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Width = 10,
            Height = 10,
            Style = new Style { Fill = new Fill { Color = "rgba(1, 2, 3, 4) trailing" } }
        });

        var result = DocumentValidator.Validate(document);

        Assert.Contains(result.Errors, error => error.Code == "STYLE002");
    }

    [Fact]
    public void PersistedStringAndSiblingOrderLimits_AreReported()
    {
        var document = CreateValidDocument();
        document.Name = new string('n', DocumentLimits.MaxNameLength + 1);
        document.Root.Children =
        [
            new TextNode { Id = Guid.NewGuid(), Order = "a0", Text = "one" },
            new TextNode { Id = Guid.NewGuid(), Order = "a0", Text = "two" }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.Contains(result.Errors, error => error.Code == "RESOURCE001" && error.Path == "$.name");
        Assert.Contains(result.Errors, error => error.Code == "NODE010");
    }

    [Fact]
    public void TextLayoutFields_ValidValues_ProduceNoDiagnostics()
    {
        var doc = CreateDocWithChild(new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Order = "a0",
            Text = "Hello",
            TextAlign = "center",
            VerticalAlign = "baseline",
            MaxWidth = 120,
            LineHeight = 1.5,
            LetterSpacing = -0.5
        });

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("TEXT", StringComparison.Ordinal));
    }

    [Fact]
    public void WrappedTextExample_ValidatesClean()
    {
        var examplesDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples");
        var doc = DocumentSerializer.Deserialize(File.ReadAllText(Path.Combine(examplesDir, "wrapped-text.agd.json")));
        Assert.NotNull(doc);

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Empty(result.Warnings);
    }

    private static Document CreateDoc(Action<Document> configure)
    {
        var doc = CreateValidDocument();
        configure(doc);
        return doc;
    }

    private static Document CreateDocWithChild(Node child)
    {
        var doc = CreateValidDocument();
        doc.Root.Children.Add(child);
        return doc;
    }

    private static Document CreateDocWithDuplicateIds()
    {
        var duplicateId = Guid.NewGuid();
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = "test",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 800, Height = 600 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Order = "a0",
                Width = 800,
                Height = 600,
                Children =
                [
                    new RectangleNode
                    {
                        Id = duplicateId,
                        Name = "rect1",
                        Order = "a0",
                        Width = 100,
                        Height = 100
                    },
                    new RectangleNode
                    {
                        Id = duplicateId,
                        Name = "rect2",
                        Order = "a1",
                        Width = 100,
                        Height = 100
                    }
                ]
            }
        };
    }

    private static Document CreateValidDocument()
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = "valid-test",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 800, Height = 600 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Order = "a0",
                Width = 800,
                Height = 600,
                Children = []
            }
        };
    }
}
