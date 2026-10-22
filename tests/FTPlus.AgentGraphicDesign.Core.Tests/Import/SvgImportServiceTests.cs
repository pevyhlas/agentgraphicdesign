using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Import;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Import;

/// <summary>
/// Tests for SVG import functionality.
/// </summary>
public class SvgImportServiceTests
{
    private readonly SvgImportService _service = new();

    // ---- Basic document structure ----

    [Fact]
    public void Import_ValidSvg_CreatesDocument()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Equal(100, result.Document.Canvas.Width);
        Assert.Equal(100, result.Document.Canvas.Height);
    }

    [Fact]
    public void Import_WithDimensions_UsesWidthHeight()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="150">
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Equal(200, result.Document!.Canvas.Width);
        Assert.Equal(150, result.Document.Canvas.Height);
    }

    [Fact]
    public void Import_WithOptions_SetsDocumentName()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>";
        var options = new SvgImportOptions { DocumentName = "my-logo" };

        var result = _service.Import(svg, options);

        Assert.True(result.Success);
        Assert.Equal("my-logo", result.Document!.Name);
    }

    // ---- Rectangle ----

    [Fact]
    public void Import_Rect_CreatesRectangleNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect x="10" y="20" width="100" height="50" fill="#ff0000"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
        Assert.Equal(10, rect.Transform?.X);
        Assert.Equal(20, rect.Transform?.Y);
        Assert.Equal("#ff0000", rect.Style?.Fill?.Color);
    }

    [Fact]
    public void Import_RectWithCornerRadius_SetsCornerRadius()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect x="0" y="0" width="100" height="100" rx="10"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(10, rect.CornerRadius);
    }

    // ---- Circle and Ellipse ----

    [Fact]
    public void Import_Circle_CreatesEllipseNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <circle cx="50" cy="50" r="25" fill="#00ff00"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var ellipse = result.Document!.Root.Children.OfType<EllipseNode>().Single();
        Assert.Equal(50, ellipse.Width);
        Assert.Equal(50, ellipse.Height);
        Assert.Equal(25, ellipse.Transform?.X); // cx - r
        Assert.Equal(25, ellipse.Transform?.Y); // cy - r
    }

    [Fact]
    public void Import_Ellipse_CreatesEllipseNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <ellipse cx="100" cy="50" rx="80" ry="30"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var ellipse = result.Document!.Root.Children.OfType<EllipseNode>().Single();
        Assert.Equal(160, ellipse.Width);
        Assert.Equal(60, ellipse.Height);
    }

    // ---- Line ----

    [Fact]
    public void Import_Line_CreatesLineNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <line x1="10" y1="20" x2="100" y2="150" stroke="#000"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var line = result.Document!.Root.Children.OfType<LineNode>().Single();
        Assert.Equal(10, line.X1);
        Assert.Equal(20, line.Y1);
        Assert.Equal(100, line.X2);
        Assert.Equal(150, line.Y2);
    }

    // ---- Polygon and Polyline ----

    [Fact]
    public void Import_Polygon_CreatesClosedPolygonNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <polygon points="50,0 100,100 0,100"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var polygon = result.Document!.Root.Children.OfType<PolygonNode>().Single();
        Assert.Equal(3, polygon.Points.Count);
        Assert.True(polygon.Closed);
        Assert.Equal(50, polygon.Points[0].X);
        Assert.Equal(0, polygon.Points[0].Y);
    }

    [Fact]
    public void Import_Polyline_CreatesOpenPolygonNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <polyline points="0,0 50,50 100,0"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var polygon = result.Document!.Root.Children.OfType<PolygonNode>().Single();
        Assert.Equal(3, polygon.Points.Count);
        Assert.False(polygon.Closed);
    }

    // ---- Path ----

    [Fact]
    public void Import_Path_CreatesPathNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <path d="M 10 10 L 90 10 L 90 90 L 10 90 Z" fill="#0000ff"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var path = result.Document!.Root.Children.OfType<PathNode>().Single();
        Assert.Equal("M 10 10 L 90 10 L 90 90 L 10 90 Z", path.D);
        Assert.Equal("#0000ff", path.Style?.Fill?.Color);
    }

    // ---- Text ----

    [Fact]
    public void Import_Text_CreatesTextNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="10" y="50" font-size="24" font-family="Arial">Hello World</text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal("Hello World", text.Text);
        Assert.Equal(24, text.FontSize);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal(10, text.Transform?.X);
        Assert.Equal(26, text.Transform?.Y);
    }

    [Fact]
    public void Import_TextWithTspan_FlattensText()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="10" y="50">
                    <tspan>Hello</tspan>
                    <tspan>World</tspan>
                </text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal("Hello World", text.Text);
        Assert.Null(text.LineHeight);
        // Plain tspans on one line map exactly, so no warning is raised.
        Assert.DoesNotContain(result.Warnings, w => w.Context == "tspan");
    }

    [Fact]
    public void Import_TextWithTspanDy_BecomesNewlineSeparatedLinesWithLineHeight()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="10" y="50" font-size="20">
                    <tspan x="10" dy="0">First line</tspan>
                    <tspan x="10" dy="1.5em">Second line</tspan>
                    <tspan x="10" dy="30">Third</tspan>
                </text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal("First line\nSecond line\nThird", text.Text);
        Assert.Equal(1.5, text.LineHeight);
        Assert.Equal(10, text.Transform!.X);
        Assert.Equal(30, text.Transform.Y);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "tspan");
    }

    [Fact]
    public void Import_TextWithTspanAbsoluteY_BreaksLines()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="0" y="20" font-size="10"><tspan x="0" y="20">a</tspan><tspan x="0" y="32">b</tspan><tspan x="0" y="44">c</tspan></text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal("a\nb\nc", text.Text);
        Assert.Equal(1.2, text.LineHeight);
    }

    [Fact]
    public void Import_TspanWithInconsistentPitch_KeepsLinesButNoLineHeight()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="0" y="20" font-size="10"><tspan dy="0">a</tspan><tspan dy="12">b</tspan><tspan dy="30">c</tspan></text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal("a\nb\nc", text.Text);
        Assert.Null(text.LineHeight);
    }

    [Fact]
    public void Import_StyledOrNestedTspan_FlattensWithWarning()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="0" y="20"><tspan fill="#ff0000" dx="4">Red</tspan> <tspan>outer <tspan>inner</tspan></tspan></text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal("Red outer inner", text.Text);
        Assert.Contains(result.Warnings, w => w.Code == ImportWarningCode.UnsupportedFeature && w.Context == "tspan");
    }

    [Theory]
    [InlineData("middle", "center")]
    [InlineData("end", "end")]
    [InlineData("start", null)]
    public void Import_TextAnchor_MapsToTextAlign(string anchor, string? expected)
    {
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <g text-anchor="{anchor}"><text x="100" y="50">Hi</text></g>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<GroupNode>().Single().Children.OfType<TextNode>().Single();
        Assert.Equal(expected, text.TextAlign);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "text-anchor");
    }

    [Fact]
    public void Import_UnknownTextAnchor_Warns()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="100" y="50" text-anchor="sideways">Hi</text>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.Null(result.Document!.Root.Children.OfType<TextNode>().Single().TextAlign);
        Assert.Contains(result.Warnings, w => w.Code == ImportWarningCode.UnsupportedFeature && w.Context == "text-anchor");
    }

    [Theory]
    [InlineData("2", 2d)]
    [InlineData("1.5px", 1.5d)]
    [InlineData("0.1em", 2d)]
    [InlineData("normal", null)]
    public void Import_LetterSpacing_MapsPixelsAndEms(string value, double? expected)
    {
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="0" y="50" font-size="20" letter-spacing="{value}">Hi</text>
            </svg>
            """;

        var result = _service.Import(svg);

        var text = result.Document!.Root.Children.OfType<TextNode>().Single();
        Assert.Equal(expected, text.LetterSpacing);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "letter-spacing");
    }

    [Fact]
    public void Import_LetterSpacingWithUnsupportedUnit_Warns()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <text x="0" y="50" letter-spacing="2pt">Hi</text>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.Null(result.Document!.Root.Children.OfType<TextNode>().Single().LetterSpacing);
        Assert.Contains(result.Warnings, w => w.Code == ImportWarningCode.UnsupportedFeature && w.Context == "letter-spacing");
    }

    // ---- Group ----

    [Fact]
    public void Import_Group_CreatesGroupNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <g id="my-group">
                    <rect x="0" y="0" width="50" height="50"/>
                    <circle cx="100" cy="100" r="25"/>
                </g>
            </svg>
            """;

        var result = _service.Import(svg);

        var group = result.Document!.Root.Children.OfType<GroupNode>().Single();
        Assert.Equal("my-group", group.Name);
        Assert.Equal(2, group.Children.Count);
    }

    [Fact]
    public void Import_GroupWithTransform_AppliesTransform()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <g transform="translate(50, 30)">
                    <rect x="0" y="0" width="50" height="50"/>
                </g>
            </svg>
            """;

        var result = _service.Import(svg);

        var group = result.Document!.Root.Children.OfType<GroupNode>().Single();
        Assert.Equal(50, group.Transform?.X);
        Assert.Equal(30, group.Transform?.Y);
    }

    // ---- Transforms ----

    [Fact]
    public void Import_TransformTranslate_ParsesCorrectly()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" transform="translate(10, 20)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(10, rect.Transform?.X);
        Assert.Equal(20, rect.Transform?.Y);
    }

    [Fact]
    public void Import_TransformRotate_ParsesCorrectly()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" transform="rotate(45)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(45, rect.Transform?.Rotation);
    }

    [Fact]
    public void Import_TransformScale_ParsesCorrectly()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" transform="scale(2, 1.5)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(2, rect.Transform?.ScaleX);
        Assert.Equal(1.5, rect.Transform?.ScaleY);
    }

    // ---- Styles ----

    [Fact]
    public void Import_StyleAttribute_ParsesFillAndStroke()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" style="fill: #ff0000; stroke: #0000ff; stroke-width: 2"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal("#ff0000", rect.Style?.Fill?.Color);
        Assert.Equal("#0000ff", rect.Style?.Stroke?.Color);
        Assert.Equal(2, rect.Style?.Stroke?.Width);
    }

    [Fact]
    public void Import_StrokeLinecap_ParsesCorrectly()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <line x1="0" y1="0" x2="100" y2="100" stroke="#000" stroke-linecap="round"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var line = result.Document!.Root.Children.OfType<LineNode>().Single();
        Assert.Equal(StrokeCap.Round, line.Style?.Stroke?.Cap);
    }

    [Fact]
    public void Import_StrokeDashArray_ParsesCorrectly()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <line x1="0" y1="0" x2="100" y2="100" stroke="#000" stroke-dasharray="5,3,2"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var line = result.Document!.Root.Children.OfType<LineNode>().Single();
        Assert.NotNull(line.Style?.Stroke?.DashArray);
        Assert.Equal([5, 3, 2], line.Style!.Stroke!.DashArray!);
    }

    [Fact]
    public void Import_FillNone_SetsNullFill()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" fill="none" stroke="#000"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Null(rect.Style?.Fill);
        Assert.NotNull(rect.Style?.Stroke);
    }

    [Fact]
    public void Import_Opacity_ParsesCorrectly()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" fill="#ff0000" opacity="0.5"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(0.5, rect.Style?.Opacity);
    }

    // ---- Gradients ----

    [Fact]
    public void Import_LinearGradient_ParsesGradient()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <defs>
                    <linearGradient id="grad1" x1="0%" y1="0%" x2="100%" y2="0%">
                        <stop offset="0%" stop-color="#ff0000"/>
                        <stop offset="100%" stop-color="#0000ff"/>
                    </linearGradient>
                </defs>
                <rect width="100" height="100" fill="url(#grad1)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        var gradient = rect.Style?.Fill?.Gradient as LinearGradient;
        Assert.NotNull(gradient);
        Assert.Equal(2, gradient.Stops.Count);
        Assert.Equal(0, gradient.X1);
        Assert.Equal(1, gradient.X2);
    }

    [Fact]
    public void Import_RadialGradient_ParsesGradient()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <defs>
                    <radialGradient id="grad1" cx="50%" cy="50%" r="50%">
                        <stop offset="0%" stop-color="#fff"/>
                        <stop offset="100%" stop-color="#000"/>
                    </radialGradient>
                </defs>
                <circle cx="50" cy="50" r="50" fill="url(#grad1)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var ellipse = result.Document!.Root.Children.OfType<EllipseNode>().Single();
        var gradient = ellipse.Style?.Fill?.Gradient as RadialGradient;
        Assert.NotNull(gradient);
        Assert.Equal(0.5, gradient.Cx);
        Assert.Equal(0.5, gradient.Cy);
        Assert.Equal(0.5, gradient.R);
    }

    // ---- Visibility ----

    [Fact]
    public void Import_VisibilityHidden_SetsVisibleFalse()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" visibility="hidden"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.False(rect.Visible);
    }

    [Fact]
    public void Import_DisplayNone_SetsVisibleFalse()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" display="none"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.False(rect.Visible);
    }

    // ---- Unsupported features with warnings ----

    [Fact]
    public void Import_UnsupportedElement_AddsWarning()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <foreignObject width="100" height="100">
                    <div>HTML content</div>
                </foreignObject>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedElement &&
            w.Context == "foreignObject");
    }

    [Fact]
    public void Import_Filter_AddsWarning()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <defs>
                    <filter id="blur">
                        <feGaussianBlur stdDeviation="5"/>
                    </filter>
                </defs>
                <rect width="100" height="100" filter="url(#blur)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedElement &&
            w.Context == "filter");
    }

    [Fact]
    public void Import_NonRectangularClipPath_AddsUnsupportedFeatureWarning()
    {
        // A non-rectangular clip is out of the bounded contract (D-11): the node imports
        // unclipped and a single clip-path feature warning is emitted.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <defs>
                    <clipPath id="clip">
                        <circle cx="50" cy="50" r="50"/>
                    </clipPath>
                </defs>
                <rect width="100" height="100" clip-path="url(#clip)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature &&
            w.Context == "clip-path");
        // The <clipPath> definition itself no longer warns as an unsupported element.
        Assert.DoesNotContain(result.Warnings, w => w.Context == "clipPath");
    }

    [Fact]
    public void Import_Use_ExpandsReferencedElement()
    {
        // <use> now expands the referenced element (D-11) rather than warning.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <defs>
                    <rect id="myRect" width="50" height="50"/>
                </defs>
                <use href="#myRect" x="10" y="10"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "use");
        var wrapper = result.Document!.Root.Children.OfType<GroupNode>().Single();
        Assert.Equal(10, wrapper.Transform!.X);
        Assert.Equal(10, wrapper.Transform.Y);
        var rectangle = wrapper.Children.OfType<RectangleNode>().Single();
        Assert.Equal(50, rectangle.Width);
        Assert.Equal(50, rectangle.Height);
    }

    [Fact]
    public void Import_MatrixTransform_AddsWarning()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" transform="matrix(1,0,0,1,10,20)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature &&
            w.Context == "matrix");
        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal(10, rect.Transform?.X);
        Assert.Equal(20, rect.Transform?.Y);
    }

    // ---- Error handling ----

    [Fact]
    public void Import_InvalidXml_ReturnsError()
    {
        var svg = "<svg><not closed";

        var result = _service.Import(svg);

        Assert.False(result.Success);
        Assert.Equal("SVG_PARSE_ERROR", result.ErrorCode);
    }

    [Fact]
    public void Import_NotSvgRoot_ReturnsError()
    {
        var svg = "<html><body>Not SVG</body></html>";

        var result = _service.Import(svg);

        Assert.False(result.Success);
        Assert.Equal("SVG_PARSE_ERROR", result.ErrorCode);
    }

    // ---- Image ----

    [Fact]
    public void Import_Image_CreatesImageNode()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 200 200">
                <image x="10" y="20" width="100" height="80" xlink:href="image.png"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var image = result.Document!.Root.Children.OfType<ImageNode>().Single();
        Assert.Equal("image.png", image.Src);
        Assert.Equal(100, image.Width);
        Assert.Equal(80, image.Height);
        Assert.Equal(10, image.Transform?.X);
        Assert.Equal(20, image.Transform?.Y);
    }

    [Fact]
    public void Import_ImageWithDataUri_KeepsDataUri()
    {
        var dataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <image width="100" height="100" href="{dataUri}"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var image = result.Document!.Root.Children.OfType<ImageNode>().Single();
        Assert.Equal(dataUri, image.Src);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("https://example.com/image.png")]
    [InlineData("file:///etc/passwd")]
    public void Import_ImageWithRemoteOrFileUrl_StripsHrefAndWarns(string remoteUrl)
    {
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <image width="100" height="100" href="{remoteUrl}"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var image = result.Document!.Root.Children.OfType<ImageNode>().Single();
        Assert.Equal("", image.Src);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature &&
            w.Context == "image-href-stripped");
    }

    // ---- Node ordering ----

    [Fact]
    public void Import_MultipleElements_PreservesOrder()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect id="first" width="50" height="50"/>
                <circle id="second" cx="100" cy="100" r="25"/>
                <text id="third" x="0" y="50">Text</text>
            </svg>
            """;

        var result = _service.Import(svg);

        var children = result.Document!.Root.Children.ToList();
        Assert.Equal(3, children.Count);
        Assert.Equal("first", children[0].Name);
        Assert.Equal("second", children[1].Name);
        Assert.Equal("third", children[2].Name);

        Assert.True(string.Compare(children[0].Order, children[1].Order, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(children[1].Order, children[2].Order, StringComparison.Ordinal) < 0);
    }

    // ---- Color normalization ----

    [Fact]
    public void Import_RgbColor_NormalizesToHex()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                <rect width="50" height="50" fill="rgb(255, 128, 0)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var rect = result.Document!.Root.Children.OfType<RectangleNode>().Single();
        Assert.Equal("#FF8000", rect.Style?.Fill?.Color);
    }

    // ---- Recursion depth limit ----

    [Fact]
    public void Import_DeeplyNestedGroups_WarnsAndStopsAtMaxDepth()
    {
        // Build SVG with 105 nested groups (exceeds 100 limit)
        var svg = new System.Text.StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 200\">");
        for (int i = 0; i < 105; i++)
        {
            svg.Append("<g>");
        }
        svg.Append("<rect id=\"deep\" width=\"10\" height=\"10\"/>");
        for (int i = 0; i < 105; i++)
        {
            svg.Append("</g>");
        }
        svg.Append("</svg>");

        var result = _service.Import(svg.ToString());

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature &&
            w.Context == "max-depth");

        // The deeply nested rect should not be imported due to depth limit
        static bool HasRectWithId(IContainerNode node, string id)
        {
            foreach (var child in node.Children)
            {
                if (child is RectangleNode rect && rect.Name == id)
                    return true;
                if (child is IContainerNode container && HasRectWithId(container, id))
                    return true;
            }
            return false;
        }
        Assert.False(HasRectWithId(result.Document!.Root, "deep"));
    }

    [Fact]
    public async Task Import_ConcurrentCalls_OnSameService_IsolatesWarningsAndGradients()
    {
        const string warnedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <defs>
                <linearGradient id="shared"><stop offset="0" stop-color="#ff0000"/></linearGradient>
                <filter id="unsupported"/>
              </defs>
              <rect width="10" height="10" fill="url(#shared)"/>
            </svg>
            """;
        const string cleanSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <defs><linearGradient id="shared"><stop offset="0" stop-color="#0000ff"/></linearGradient></defs>
              <rect width="10" height="10" fill="url(#shared)"/>
            </svg>
            """;
        using var gate = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 64).Select(index => Task.Run(() =>
        {
            gate.Wait();
            return (Warned: index % 2 == 0, Result: _service.Import(index % 2 == 0 ? warnedSvg : cleanSvg));
        })).ToArray();

        gate.Set();
        var results = await Task.WhenAll(tasks);

        foreach (var (warned, result) in results)
        {
            Assert.True(result.Success);
            var gradient = Assert.IsType<LinearGradient>(
                result.Document!.Root.Children.OfType<RectangleNode>().Single().Style!.Fill!.Gradient);
            Assert.Equal(warned ? "#ff0000" : "#0000ff", gradient.Stops.Single().Color);
            Assert.Equal(warned, result.Warnings.Any(warning => warning.Context == "filter"));
        }
    }

    [Fact]
    public void Import_OverInputCharacterBudget_ReturnsResourceFailure()
    {
        var result = _service.Import(new string(' ', SvgImportService.MaxInputCharacters + 1));

        Assert.False(result.Success);
        Assert.Equal("SVG_RESOURCE_LIMIT", result.ErrorCode);
    }

    [Fact]
    public void Import_OverElementBudget_ReturnsResourceFailure()
    {
        var svg = new System.Text.StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\">");
        for (var index = 0; index < SvgImportService.MaxElementCount; index++)
            svg.Append("<rect/>");
        svg.Append("</svg>");

        var result = _service.Import(svg.ToString());

        Assert.False(result.Success);
        Assert.Equal("SVG_RESOURCE_LIMIT", result.ErrorCode);
    }

    [Fact]
    public void Import_Dtd_IsRejectedWithoutEntityExpansion()
    {
        const string svg = "<!DOCTYPE svg [<!ENTITY x 'expanded'>]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&x;</text></svg>";

        var result = _service.Import(svg);

        Assert.False(result.Success);
        Assert.Equal("SVG_PARSE_ERROR", result.ErrorCode);
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("images/%2e%2e/secret.png")]
    [InlineData("%2fetc/passwd")]
    [InlineData("http%3a//example.test/image.png")]
    [InlineData("/rooted/image.png")]
    [InlineData("ftp://example.test/image.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.test/image.png")]
    public void Import_ImageHrefOutsideAllowlist_IsStripped(string href)
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"{href}\"/></svg>";

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Empty(result.Document!.Root.Children.OfType<ImageNode>().Single().Src);
        Assert.Contains(result.Warnings, warning => warning.Context == "image-href-stripped");
    }

    [Fact]
    public void Import_DataImageWithDisallowedMimeType_IsStripped()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"data:image/svg+xml;base64,PHN2Zy8+\"/></svg>";

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Empty(result.Document!.Root.Children.OfType<ImageNode>().Single().Src);
        Assert.Contains(result.Warnings, warning => warning.Context == "image-href-stripped");
    }

    [Fact]
    public void Import_NonzeroViewBoxOrigin_UsesClippedFrameAndTranslatedContentGroup()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"10 20 100 50\"><rect x=\"10\" y=\"20\" width=\"5\" height=\"5\"/></svg>";

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Null(result.Document!.Root.Transform);
        Assert.True(result.Document.Root.ClipContent);
        var content = Assert.IsType<GroupNode>(result.Document.Root.Children.Single());
        Assert.Equal(-10, content.Transform!.X);
        Assert.Equal(-20, content.Transform.Y);
        Assert.Single(content.Children.OfType<RectangleNode>());
        Assert.Equal(100, result.Document.Canvas.Width);
        Assert.Equal(50, result.Document.Canvas.Height);
    }

    [Fact]
    public void Import_TransformFunctions_ComposeInSourceOrder()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <rect id="first" width="1" height="1" transform="translate(10) scale(2)"/>
              <rect id="second" width="1" height="1" transform="scale(2) translate(10)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        var first = result.Document!.Root.Children.OfType<RectangleNode>().Single(node => node.Name == "first");
        var second = result.Document.Root.Children.OfType<RectangleNode>().Single(node => node.Name == "second");
        Assert.Equal(10, first.Transform!.X, 8);
        Assert.Equal(20, second.Transform!.X, 8);
        Assert.Equal(2, first.Transform.ScaleX, 8);
        Assert.Equal(2, second.Transform.ScaleX, 8);
    }

    [Fact]
    public void Import_InheritedInlineStyle_PreservesRgbaAndFillOpacity()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <g style="fill: rgba(255, 0, 0, 0.5); fill-opacity: 0.5">
                <rect width="10" height="10"/>
              </g>
            </svg>
            """;

        var result = _service.Import(svg);

        var group = result.Document!.Root.Children.OfType<GroupNode>().Single();
        var rectangle = group.Children.OfType<RectangleNode>().Single();
        Assert.Equal("#FF000040", rectangle.Style!.Fill!.Color);
    }

    [Fact]
    public void Import_InlineDisplayNone_IsNotVisible()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"display: none\" width=\"10\" height=\"10\"/></svg>";

        var result = _service.Import(svg);

        Assert.False(result.Document!.Root.Children.Single().Visible);
    }

    [Fact]
    public void Import_PreCancelled_ThrowsOperationCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _service.Import("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", null, cancellation.Token));
    }

    [Fact]
    public void Import_GradientAliasDepth_IsBounded()
    {
        var svg = new System.Text.StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs>");
        for (var index = 0; index <= SvgImportService.MaxGradientAliasDepth; index++)
        {
            var href = index == SvgImportService.MaxGradientAliasDepth
                ? string.Empty
                : $" href=\"#g{index + 1:D3}\"";
            svg.Append($"<linearGradient id=\"g{index:D3}\"{href}/>");
        }
        svg.Append("</defs><rect width=\"1\" height=\"1\" fill=\"url(#g000)\"/></svg>");

        var result = _service.Import(svg.ToString());

        Assert.False(result.Success);
        Assert.Equal("SVG_RESOURCE_LIMIT", result.ErrorCode);
    }

    [Theory]
    [InlineData(",translate(10)")]
    [InlineData("translate(10),")]
    [InlineData("translate(10),,scale(2)")]
    [InlineData("translate(10)scale(2)")]
    public void Import_InvalidTransformSeparator_RejectsWholeTransform(string transform)
    {
        var result = _service.Import($"<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\" transform=\"{transform}\"/></svg>");

        Assert.True(result.Success);
        Assert.Null(result.Document!.Root.Children.Single().Transform);
        Assert.Contains(result.Warnings, warning => warning.Context == "transform");
    }

    [Fact]
    public void Import_TransformCoefficientOverflow_ReturnsResourceFailure()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\" transform=\"scale(1e308) scale(1e308)\"/></svg>";

        var result = _service.Import(svg);

        Assert.False(result.Success);
        Assert.Equal("SVG_RESOURCE_LIMIT", result.ErrorCode);
    }

    [Fact]
    public void Import_DataImageMimeMismatch_IsStripped()
    {
        const string gifDeclaredAsPng = "data:image/png;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==";
        var result = _service.Import($"<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"{gifDeclaredAsPng}\"/></svg>");

        Assert.True(result.Success);
        Assert.Empty(Assert.IsType<ImageNode>(result.Document!.Root.Children.Single()).Src);
        Assert.Contains(result.Warnings, warning => warning.Context == "image-href-stripped");
    }


    // ---- fill-rule ----

    [Fact]
    public void Import_PathFillRuleAttribute_MapsToStyleFillRule()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <path d="M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z" fill="#000" fill-rule="evenodd"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var path = Assert.Single(Descendants(result.Document!.Root).OfType<PathNode>());
        Assert.Equal("evenodd", path.Style!.FillRule);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "fill-rule");
    }

    [Fact]
    public void Import_PathFillRuleInlineStyle_MapsToStyleFillRule()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <path d="M0,0 L100,0 L100,100 L0,100 Z" style="fill:#000;fill-rule:evenodd"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var path = Assert.Single(Descendants(result.Document!.Root).OfType<PathNode>());
        Assert.Equal("evenodd", path.Style!.FillRule);
    }

    [Fact]
    public void Import_PathFillRuleNonzero_LeavesFillRuleUnset()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <path d="M0,0 L100,0 L100,100 L0,100 Z" fill-rule="nonzero"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var path = Assert.Single(Descendants(result.Document!.Root).OfType<PathNode>());
        Assert.Null(path.Style?.FillRule);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "fill-rule");
    }

    [Fact]
    public void Import_PolygonFillRule_MapsToStyleFillRule()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120">
                <polygon points="60,10 89.39,100.45 12.45,44.55 107.55,44.55 30.61,100.45" fill-rule="evenodd"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var polygon = Assert.Single(Descendants(result.Document!.Root).OfType<PolygonNode>());
        Assert.Equal("evenodd", polygon.Style!.FillRule);
    }

    [Fact]
    public void Import_InheritedFillRule_CascadesFromGroup()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <g fill-rule="evenodd">
                    <path d="M0,0 L100,0 L100,100 L0,100 Z"/>
                </g>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var path = Assert.Single(Descendants(result.Document!.Root).OfType<PathNode>());
        Assert.Equal("evenodd", path.Style!.FillRule);
    }

    [Fact]
    public void Import_FillRuleOnRectangle_IsIgnored()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <rect width="50" height="50" fill-rule="evenodd"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var rect = Assert.Single(Descendants(result.Document!.Root).OfType<RectangleNode>());
        Assert.Null(rect.Style?.FillRule);
    }

    [Fact]
    public void Import_UnknownFillRule_WarnsAndFallsBackToNonzero()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <path d="M0,0 L100,0 L100,100 L0,100 Z" fill-rule="winding"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var path = Assert.Single(Descendants(result.Document!.Root).OfType<PathNode>());
        Assert.Null(path.Style?.FillRule);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature &&
            w.Context == "fill-rule");
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        var children = node switch
        {
            FrameNode frame => frame.Children,
            GroupNode group => group.Children,
            _ => []
        };
        foreach (var child in children)
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
