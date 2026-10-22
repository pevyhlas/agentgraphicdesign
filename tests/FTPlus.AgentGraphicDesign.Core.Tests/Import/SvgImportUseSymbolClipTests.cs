using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Import;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Import;

/// <summary>
/// Bounded local <c>&lt;use&gt;</c> / <c>&lt;symbol&gt;</c> / rectangular <c>clip-path</c> import
/// (feature 002 T011, D-11).
/// </summary>
public class SvgImportUseSymbolClipTests
{
    private readonly SvgImportService _service = new();

    [Fact]
    public void Use_ReferencingSymbol_InstantiatesClippedFrameViewport()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
              <defs>
                <symbol id="badge" viewBox="0 0 20 20">
                  <rect width="20" height="20"/>
                </symbol>
              </defs>
              <use href="#badge" x="30" y="40" width="60" height="60"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var wrapper = result.Document!.Root.Children.OfType<GroupNode>().Single();
        Assert.Equal(30, wrapper.Transform!.X);
        Assert.Equal(40, wrapper.Transform.Y);
        var frame = wrapper.Children.OfType<FrameNode>().Single();
        // The use's width/height define the instance size; the symbol clips its content.
        Assert.Equal(60, frame.Width);
        Assert.Equal(60, frame.Height);
        Assert.True(frame.ClipContent);
        Assert.Single(frame.Children.OfType<RectangleNode>());
    }

    [Fact]
    public void Use_ReferencingGroup_ImportsAndOffsetsTheSubtree()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
              <defs>
                <g id="pair">
                  <rect width="10" height="10"/>
                  <circle cx="20" cy="20" r="5"/>
                </g>
              </defs>
              <use href="#pair" x="5" y="5"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        var wrapper = result.Document!.Root.Children.OfType<GroupNode>().Single();
        Assert.Equal(5, wrapper.Transform!.X);
        var inner = wrapper.Children.OfType<GroupNode>().Single();
        Assert.Single(inner.Children.OfType<RectangleNode>());
        Assert.Single(inner.Children.OfType<EllipseNode>());
    }

    [Fact]
    public void Use_UnknownReference_WarnsMissingReferenceAndSkips()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <use href="#nope" x="1" y="1"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.MissingReference && w.Context == "use");
        Assert.Empty(result.Document!.Root.Children);
    }

    [Fact]
    public void Use_SelfReferenceCycle_IsBrokenWithWarning()
    {
        // A symbol whose content uses itself: the cycle is detected and broken, import still succeeds.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <defs>
                <symbol id="loop" viewBox="0 0 10 10">
                  <use href="#loop"/>
                </symbol>
              </defs>
              <use href="#loop"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.MissingReference && w.Context == "use");
    }

    [Fact]
    public void Use_NestingPastDepthBound_IsSkippedWithWarning()
    {
        // A non-cyclic chain of groups, each instancing the next, nests deeper than the
        // <use> depth bound: the over-deep reference is skipped with a warning, import succeeds.
        const int chainLength = SvgImportService.MaxUseExpansionDepth + 5;
        var defs = string.Concat(Enumerable.Range(0, chainLength).Select(i =>
            i == chainLength - 1
                ? $"<g id=\"g{i}\"><rect width=\"1\" height=\"1\"/></g>"
                : $"<g id=\"g{i}\"><use href=\"#g{i + 1}\"/></g>"));
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <defs>{defs}</defs>
              <use href="#g0"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature
            && w.Context == "use"
            && w.Message.Contains("nesting exceeds maximum", StringComparison.Ordinal));
        // The innermost rectangle lies past the bound, so it is not imported.
        Assert.DoesNotContain(Flatten(result.Document!.Root.Children), n => n is RectangleNode);
    }

    [Fact]
    public void Use_ExpansionsPastCountBound_FailWithResourceLimit()
    {
        // Four levels of tenfold fan-out stay within the depth bound but request 11,110
        // <use> expansions, past the total expansion count bound.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <defs>
                <rect id="leaf" width="1" height="1"/>
                <g id="l1">{{FAN:leaf}}</g>
                <g id="l2">{{FAN:l1}}</g>
                <g id="l3">{{FAN:l2}}</g>
                <g id="l4">{{FAN:l3}}</g>
              </defs>
              <use href="#l4"/>
            </svg>
            """;
        foreach (var target in new[] { "leaf", "l1", "l2", "l3" })
            svg = svg.Replace($"{{{{FAN:{target}}}}}", string.Concat(Enumerable.Repeat($"<use href=\"#{target}\"/>", 10)), StringComparison.Ordinal);

        var result = _service.Import(svg);

        Assert.False(result.Success);
        Assert.Equal("SVG_RESOURCE_LIMIT", result.ErrorCode);
        Assert.Contains(SvgImportService.MaxUseExpansions.ToString(System.Globalization.CultureInfo.InvariantCulture), result.ErrorMessage, StringComparison.Ordinal);
    }

    private static IEnumerable<Node> Flatten(IEnumerable<Node> nodes) =>
        nodes.SelectMany(n => n is IContainerNode container ? Flatten(container.Children).Prepend(n) : [n]);

    [Fact]
    public void RectangularClipPath_MapsToClippingFrame()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
              <defs>
                <clipPath id="box">
                  <rect x="10" y="20" width="80" height="40"/>
                </clipPath>
              </defs>
              <rect width="200" height="200" clip-path="url(#box)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Context == "clip-path");
        var clip = result.Document!.Root.Children.OfType<FrameNode>().Single();
        Assert.True(clip.ClipContent);
        Assert.Equal(80, clip.Width);
        Assert.Equal(40, clip.Height);
        Assert.Equal(10, clip.Transform!.X);
        Assert.Equal(20, clip.Transform.Y);
        // The clipped content is shifted back into the frame's local space.
        var content = clip.Children.OfType<GroupNode>().Single();
        Assert.Equal(-10, content.Transform!.X);
        Assert.Equal(-20, content.Transform.Y);
        Assert.Single(content.Children.OfType<RectangleNode>());
    }

    [Fact]
    public void RoundedRectClipPath_IsNotModeled_AndWarns()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
              <defs>
                <clipPath id="round">
                  <rect x="0" y="0" width="80" height="40" rx="8"/>
                </clipPath>
              </defs>
              <rect width="200" height="200" clip-path="url(#round)"/>
            </svg>
            """;

        var result = _service.Import(svg);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w =>
            w.Code == ImportWarningCode.UnsupportedFeature && w.Context == "clip-path");
        // The node is imported unclipped: a plain rectangle, no clipping frame.
        Assert.Empty(result.Document!.Root.Children.OfType<FrameNode>());
        Assert.Single(result.Document.Root.Children.OfType<RectangleNode>());
    }
}
