using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Lint;

/// <summary>
/// The seven advisory design-lint rules and the lint result metadata. Uses the
/// bounds estimator (no text measurer registered), which is enough for the box
/// geometry every rule works from.
/// </summary>
public class LintServiceTests
{
    private readonly DocumentEngine _engine = new();

    // ---- clean baseline ----

    [Fact]
    public void Lint_CleanDocument_HasNoFindings()
    {
        var doc = NewDocument();
        Add(doc.Root, Rect(100, 100, x: 10, y: 20, fill: "#000000"));

        var result = _engine.Lint(doc);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.WarningCount);
        Assert.Equal(0, result.InfoCount);
    }

    // ---- LINT001 text contrast ----

    [Fact]
    public void Lint_WhiteTextOnWhiteCanvas_WarnsLowContrast()
    {
        var doc = NewDocument();
        Add(doc.Root, Text("Hello", fill: "#ffffff", fontSize: 16));

        var result = _engine.Lint(doc);

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.TextContrast);
        Assert.Equal(LintSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Lint_BlackTextOnWhiteCanvas_HasNoContrastWarning()
    {
        var doc = NewDocument();
        Add(doc.Root, Text("Hello", fill: "#000000", fontSize: 16));

        var result = _engine.Lint(doc);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == LintCodes.TextContrast);
    }

    [Fact]
    public void Lint_ContrastUsesRectangleBackgroundBehindText()
    {
        // A black background rectangle under white text is fine; the canvas is ignored.
        var doc = NewDocument(width: 200, height: 200);
        Add(doc.Root, Rect(200, 200, x: 0, y: 0, fill: "#000000"), 0);
        Add(doc.Root, Text("Hello", fill: "#ffffff", fontSize: 16, x: 0, y: 0), 1);

        var result = _engine.Lint(doc);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == LintCodes.TextContrast);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lint_WhiteTextInsideBlackFilledFrame_UsesWhiteCanvas(bool isRoot)
    {
        var doc = NewDocument(width: 200, height: 200);
        doc.Canvas.Background = "#ffffff";
        var frame = isRoot ? doc.Root : Add(doc.Root, new FrameNode { Width = 200, Height = 200 });
        frame.Style = new Style { Fill = new Fill { Color = "#000000" } };
        var text = Add(frame, Text("Hello", fill: "#ffffff", fontSize: 16, x: 10, y: 10));

        var finding = Assert.Single(_engine.Lint(doc).Diagnostics, d => d.Code == LintCodes.TextContrast);

        Assert.Equal(text.Id, finding.NodeId);
        Assert.Equal(LintSeverity.Warning, finding.Severity);
        Assert.Equal(1.0, finding.Details["ratio"]);
        Assert.Equal("#ffffff", finding.Details["background"]);
        Assert.Equal("canvas", finding.Details["backgroundSource"]);
        Assert.False(finding.Details.ContainsKey("backgroundNodeId"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lint_WhiteTextInsideFrame_UsesPaintedRectangle(bool inheritedFill)
    {
        var doc = NewDocument(width: 200, height: 200);
        doc.Canvas.Background = "#ffffff";
        var frame = Add(doc.Root, new FrameNode
        {
            Width = 200,
            Height = 200,
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });
        Add(frame, Rect(200, 200, fill: inheritedFill ? null : "#000000"), 0);
        Add(frame, Text("Hello", fill: "#ffffff", fontSize: 16, x: 10, y: 10), 1);

        Assert.DoesNotContain(_engine.Lint(doc).Diagnostics, d => d.Code == LintCodes.TextContrast);
    }

    [Theory]
    [InlineData("#000000", 1, false)]
    [InlineData("#ffffff", 1, true)]
    [InlineData("#000000", 0.5, true)]
    public void Lint_TextInheritsFrameFillAndOpacity_NotBackground(string fill, double opacity, bool warns)
    {
        var doc = NewDocument();
        doc.Canvas.Background = "#ffffff";
        doc.Root.Style = new Style { Fill = new Fill { Color = fill }, Opacity = opacity };
        var group = Add(doc.Root, new GroupNode());
        var text = Add(group, Text("Hello", fill: "#ffffff", fontSize: 16));
        text.Style = null;

        var findings = _engine.Lint(doc).Diagnostics.Where(d => d.Code == LintCodes.TextContrast).ToList();

        if (warns)
        {
            var finding = Assert.Single(findings);
            Assert.Equal("canvas", finding.Details["backgroundSource"]);
            Assert.Equal(opacity == 1 ? fill : "#808080", finding.Details["foreground"]);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lint_GradientFrameFill_SkipsContrastOnlyWhenPaintedByRectangle(bool painted)
    {
        var doc = NewDocument(width: 200, height: 200);
        doc.Root.Style = new Style
        {
            Fill = new Fill
            {
                Gradient = new LinearGradient
                {
                    Stops = [new() { Offset = 0, Color = "#000000" }, new() { Offset = 1, Color = "#ffffff" }]
                }
            }
        };
        if (painted)
            Add(doc.Root, Rect(200, 200), 0);
        Add(doc.Root, Text("Hello", fill: "#ffffff", fontSize: 16, x: 10, y: 10), 1);

        var findings = _engine.Lint(doc).Diagnostics.Where(d => d.Code == LintCodes.TextContrast).ToList();

        if (painted)
            Assert.Empty(findings);
        else
            Assert.Equal("default", Assert.Single(findings).Details["backgroundSource"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lint_InheritedRectangleBackground_RespectsFrameClipping(bool clipContent)
    {
        var doc = NewDocument(width: 200, height: 200);
        var frame = Add(doc.Root, new FrameNode
        {
            Width = 50,
            Height = 100,
            ClipContent = clipContent,
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        }, 0);
        Add(frame, Rect(200, 100));
        Add(doc.Root, Text("Hello", fill: "#ffffff", fontSize: 16, x: 80, y: 10), 1);

        var findings = _engine.Lint(doc).Diagnostics.Where(d => d.Code == LintCodes.TextContrast).ToList();

        if (clipContent)
            Assert.Equal("default", Assert.Single(findings).Details["backgroundSource"]);
        else
            Assert.Empty(findings);
    }

    [Theory]
    [InlineData(0.5, 1, "#808080")]
    [InlineData(1, 0.5, "#808080")]
    [InlineData(0.5, 0.5, "#bfbfbf")]
    public void Lint_RectangleBackground_CompositesFrameAndRectangleOpacity(
        double frameOpacity, double rectangleOpacity, string background)
    {
        var doc = NewDocument(width: 200, height: 200);
        var frame = Add(doc.Root, new FrameNode
        {
            Width = 200,
            Height = 200,
            Style = new Style { Fill = new Fill { Color = "#000000" }, Opacity = frameOpacity }
        }, 0);
        var rectangle = Add(frame, Rect(200, 200, fill: "#000000"));
        rectangle.Style!.Opacity = rectangleOpacity;
        Add(doc.Root, Text("Hello", fill: "#ffffff", fontSize: 16, x: 10, y: 10), 1);

        var finding = Assert.Single(_engine.Lint(doc).Diagnostics, d => d.Code == LintCodes.TextContrast);

        Assert.Equal(background, finding.Details["background"]);
        Assert.Equal("rectangle", finding.Details["backgroundSource"]);
        Assert.Equal(rectangle.Id, finding.Details["backgroundNodeId"]);
    }

    [Theory]
    [InlineData("frame", false)]
    [InlineData("group", false)]
    [InlineData("root", false)]
    [InlineData("frame", true)]
    [InlineData("group", true)]
    public void Lint_SharedTranslucentAncestor_SkipsContrast(string containerType, bool nested)
    {
        var doc = NewDocument(width: 200, height: 100);
        doc.Canvas.Background = "#ffffff";
        Node ancestor = containerType switch
        {
            "root" => doc.Root,
            "group" => Add(doc.Root, new GroupNode()),
            _ => Add(doc.Root, new FrameNode { Width = 200, Height = 100 })
        };
        ancestor.Style = new Style { Opacity = 0.5 };
        var container = (IContainerNode)ancestor;
        IContainerNode backgroundParent = nested ? Add(container, new GroupNode(), 0) : container;
        IContainerNode textParent = nested ? Add(container, new GroupNode(), 1) : container;
        Add(backgroundParent, Rect(200, 100, fill: "#000000"), 0);
        Add(textParent, Text("Hello", fill: "#ffffff", fontSize: 24, x: 10, y: 10), 1);

        Assert.DoesNotContain(_engine.Lint(doc).Diagnostics, d => d.Code == LintCodes.TextContrast);
    }

    [Fact]
    public void Lint_SeparateTranslucentAncestors_StillChecksContrast()
    {
        var doc = NewDocument(width: 200, height: 100);
        doc.Canvas.Background = "#ffffff";
        var backgroundGroup = Add(doc.Root, new GroupNode { Style = new Style { Opacity = 0.5 } }, 0);
        var rectangle = Add(backgroundGroup, Rect(200, 100, fill: "#000000"));
        var textGroup = Add(doc.Root, new GroupNode { Style = new Style { Opacity = 0.5 } }, 1);
        Add(textGroup, Text("Hello", fill: "#ffffff", fontSize: 24, x: 10, y: 10));

        var finding = Assert.Single(_engine.Lint(doc).Diagnostics, d => d.Code == LintCodes.TextContrast);

        Assert.Equal("#bfbfbf", finding.Details["foreground"]);
        Assert.Equal("#808080", finding.Details["background"]);
        Assert.Equal(2.2, finding.Details["ratio"]);
        Assert.Equal(rectangle.Id, finding.Details["backgroundNodeId"]);
    }

    // ---- LINT002 hairline strokes ----

    [Fact]
    public void Lint_SubPixelStroke_WarnsHairline()
    {
        var doc = NewDocument();
        Add(doc.Root, Rect(100, 100, x: 10, y: 10, fill: "#000000", stroke: "#ff0000", strokeWidth: 0.5));

        var result = _engine.Lint(doc);

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.HairlineStroke);
        Assert.Equal(LintSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Lint_WholePixelStroke_HasNoHairlineWarning()
    {
        var doc = NewDocument();
        Add(doc.Root, Rect(100, 100, x: 10, y: 10, fill: "#000000", stroke: "#ff0000", strokeWidth: 3));

        var result = _engine.Lint(doc);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == LintCodes.HairlineStroke);
    }

    [Fact]
    public void Lint_TargetSize_ScalesTheHairlineCheck()
    {
        // A 2 px stroke on a 512 px canvas shown at 32 px renders at 0.125 px.
        var doc = NewDocument(width: 512, height: 512);
        Add(doc.Root, Rect(100, 100, x: 10, y: 10, fill: "#000000", stroke: "#ff0000", strokeWidth: 2));

        var native = _engine.Lint(doc);
        Assert.DoesNotContain(native.Diagnostics, d => d.Code == LintCodes.HairlineStroke);

        var shrunk = _engine.Lint(doc, new LintOptions { TargetSize = 32 });
        Assert.Contains(shrunk.Diagnostics, d => d.Code == LintCodes.HairlineStroke);
    }

    // ---- LINT004 outside the canvas ----

    [Fact]
    public void Lint_NodeFullyOutsideCanvas_Warns()
    {
        var doc = NewDocument(width: 100, height: 100);
        Add(doc.Root, Rect(20, 20, x: 1000, y: 1000, fill: "#000000"));

        var result = _engine.Lint(doc);

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.OutsideCanvas);
        Assert.Equal(LintSeverity.Warning, finding.Severity);
        Assert.Equal(true, finding.Details["fullyOutside"]);
    }

    [Fact]
    public void Lint_NodeCrossingCanvasEdge_ReportsInfo()
    {
        var doc = NewDocument(width: 100, height: 100);
        Add(doc.Root, Rect(50, 20, x: 80, y: 10, fill: "#000000"));

        var result = _engine.Lint(doc);

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.OutsideCanvas);
        Assert.Equal(LintSeverity.Info, finding.Severity);
    }

    // ---- LINT005 unused tokens ----

    [Fact]
    public void Lint_UnusedColorToken_ReportsInfo()
    {
        var doc = NewDocument();
        doc.Tokens = new DesignTokens { Colors = new() { ["brand"] = "#3366ff" } };
        Add(doc.Root, Rect(100, 100, x: 10, y: 10, fill: "#000000"));

        var result = _engine.Lint(doc);

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.UnusedToken);
        Assert.Equal(LintSeverity.Info, finding.Severity);
    }

    // ---- LINT006 invisible content ----

    [Fact]
    public void Lint_ZeroOpacityNode_ReportsInvisible()
    {
        var doc = NewDocument();
        var rect = Rect(100, 100, x: 10, y: 10, fill: "#000000");
        rect.Style = new Style { Fill = new Fill { Color = "#000000" }, Opacity = 0 };
        Add(doc.Root, rect);

        var result = _engine.Lint(doc);

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.InvisibleContent);
        Assert.Equal(LintSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Lint_HiddenContainerWithChildren_ReportsInvisible()
    {
        var doc = NewDocument();
        var group = new GroupNode { Id = Guid.NewGuid(), Name = "group", Visible = false, Children = [] };
        Add(doc.Root, group);
        Add(group, Rect(10, 10, x: 1, y: 1, fill: "#000000"));

        var result = _engine.Lint(doc);

        Assert.Contains(result.Diagnostics,
            d => d.Code == LintCodes.InvisibleContent && d.NodeId == group.Id);
    }

    // ---- LINT003 off-grid geometry ----

    [Fact]
    public void Lint_OffGridOnIconCanvas_ReportsInfo()
    {
        var doc = NewDocument(width: 32, height: 32);
        Add(doc.Root, Rect(10, 10, x: 10.25, y: 10, fill: "#000000"));

        var result = _engine.Lint(doc);

        Assert.True(result.GridChecked);
        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.OffGrid);
        Assert.Equal(LintSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Lint_StrictGrid_PromotesOffGridToWarning()
    {
        var doc = NewDocument(width: 32, height: 32);
        Add(doc.Root, Rect(10, 10, x: 10.25, y: 10, fill: "#000000"));

        var result = _engine.Lint(doc, new LintOptions { StrictGrid = true });

        var finding = Assert.Single(result.Diagnostics, d => d.Code == LintCodes.OffGrid);
        Assert.Equal(LintSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Lint_LargeCanvas_DoesNotCheckGrid()
    {
        var doc = NewDocument(width: 800, height: 600);
        Add(doc.Root, Rect(10, 10, x: 10.25, y: 10.25, fill: "#000000"));

        var result = _engine.Lint(doc);

        Assert.False(result.GridChecked);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == LintCodes.OffGrid);
    }

    [Fact]
    public void Lint_WebIconProfile_ForcesGridCheck()
    {
        var doc = NewDocument(width: 800, height: 600);

        var result = _engine.Lint(doc, new LintOptions { Profile = ValidationProfile.WebIcon });

        Assert.True(result.GridChecked);
        Assert.Equal(ValidationProfile.WebIcon, result.Profile);
    }

    // ---- LINT007 symbol readiness ----

    [Fact]
    public void Lint_MixedIconFills_SilentWithoutWebIconProfile()
    {
        var doc = NewDocument(width: 32, height: 32);
        Add(doc.Root, Rect(10, 10, x: 0, y: 0, fill: "#ff0000"), 0);
        Add(doc.Root, Rect(10, 10, x: 12, y: 0, fill: "#00ff00"), 1);

        var result = _engine.Lint(doc);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == LintCodes.SymbolReadiness);
    }

    [Fact]
    public void Lint_MixedIconFills_WarnsSymbolReadinessUnderWebIcon()
    {
        var doc = NewDocument(width: 32, height: 32);
        Add(doc.Root, Rect(10, 10, x: 0, y: 0, fill: "#000000"), 0);
        Add(doc.Root, Rect(10, 10, x: 12, y: 0, fill: "#ff0000"), 1);

        var result = _engine.Lint(doc, new LintOptions { Profile = ValidationProfile.WebIcon });

        Assert.Contains(result.Diagnostics, d => d.Code == LintCodes.SymbolReadiness);
    }

    // ---- result metadata ----

    [Fact]
    public void Lint_ResultEchoesOptionsAndCounts()
    {
        var doc = NewDocument();
        Add(doc.Root, Text("Hello", fill: "#ffffff", fontSize: 16));

        var result = _engine.Lint(doc, new LintOptions { StrictGrid = true });

        Assert.True(result.StrictGrid);
        Assert.Equal(ValidationProfile.Default, result.Profile);
        Assert.Equal(result.Diagnostics.Count(d => d.Severity == LintSeverity.Warning), result.WarningCount);
        Assert.Equal(result.Diagnostics.Count(d => d.Severity == LintSeverity.Info), result.InfoCount);
    }

    [Fact]
    public void Lint_InvalidTargetSize_Throws()
    {
        var doc = NewDocument();
        Assert.Throws<DocumentEngineException>(() => _engine.Lint(doc, new LintOptions { TargetSize = -5 }));
    }

    // ---- helpers ----

    private static Document NewDocument(double width = 800, double height = 600) => new()
    {
        Schema = Document.SchemaUrl,
        Id = Guid.NewGuid(),
        Name = "lint",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = width, Height = height },
        Root = new FrameNode { Id = Guid.NewGuid(), Name = "root", Width = width, Height = height, Children = [] }
    };

    private static RectangleNode Rect(
        double width, double height, double x = 0, double y = 0,
        string? fill = null, string? stroke = null, double strokeWidth = 1) => new()
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = width,
            Height = height,
            Transform = new Transform { X = x, Y = y },
            Style = fill is null && stroke is null
            ? null
            : new Style
            {
                Fill = fill is null ? null : new Fill { Color = fill },
                Stroke = stroke is null ? null : new Stroke { Color = stroke, Width = strokeWidth }
            }
        };

    private static TextNode Text(string text, string fill, double fontSize, double x = 0, double y = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "text",
        Text = text,
        FontSize = fontSize,
        Transform = new Transform { X = x, Y = y },
        Style = new Style { Fill = new Fill { Color = fill } }
    };

    private static T Add<T>(IContainerNode parent, T node, int index = 0) where T : Node
    {
        node.Order = $"a{index}";
        parent.Children.Add(node);
        return node;
    }
}
