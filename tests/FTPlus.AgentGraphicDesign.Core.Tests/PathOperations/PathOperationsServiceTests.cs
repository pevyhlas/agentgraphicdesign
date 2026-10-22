using FTPlus.AgentGraphicDesign.Core.PathOperations;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.PathOperations;

public class PathOperationsServiceTests
{
    private readonly PathOperationsService _service = new();

    // ---- Boolean operations: basic shapes ----

    [Fact]
    public void BooleanOp_Union_TwoOverlappingSquares_ReturnsCombinedPath()
    {
        var square1 = "M0,0 L100,0 L100,100 L0,100 Z";
        var square2 = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _service.BooleanOp(square1, square2, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
        Assert.Contains("M", result.PathData);
        Assert.Contains("Z", result.PathData);
    }

    [Fact]
    public void BooleanOp_Intersect_TwoOverlappingSquares_ReturnsIntersection()
    {
        var square1 = "M0,0 L100,0 L100,100 L0,100 Z";
        var square2 = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _service.BooleanOp(square1, square2, BooleanOperation.Intersect);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
    }

    [Fact]
    public void BooleanOp_Difference_SquareMinusSquare_ReturnsRemainder()
    {
        var outer = "M0,0 L200,0 L200,200 L0,200 Z";
        var inner = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _service.BooleanOp(outer, inner, BooleanOperation.Difference);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
    }

    [Fact]
    public void BooleanOp_Xor_TwoOverlappingSquares_ReturnsSymmetricDifference()
    {
        var square1 = "M0,0 L100,0 L100,100 L0,100 Z";
        var square2 = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _service.BooleanOp(square1, square2, BooleanOperation.Xor);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
    }

    // ---- Boolean operations: edge cases ----

    [Fact]
    public void BooleanOp_NonOverlappingSquares_UnionReturnsBothPaths()
    {
        var square1 = "M0,0 L50,0 L50,50 L0,50 Z";
        var square2 = "M100,100 L150,100 L150,150 L100,150 Z";

        var result = _service.BooleanOp(square1, square2, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        var mCount = result.PathData.Count(c => c == 'M');
        Assert.Equal(2, mCount);
    }

    [Fact]
    public void BooleanOp_NonOverlappingSquares_IntersectReturnsEmpty()
    {
        var square1 = "M0,0 L50,0 L50,50 L0,50 Z";
        var square2 = "M100,100 L150,100 L150,150 L100,150 Z";

        var result = _service.BooleanOp(square1, square2, BooleanOperation.Intersect);

        Assert.True(result.Success);
        Assert.Empty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_IdenticalSquares_UnionReturnsSamePath()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.BooleanOp(square, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.Single(result.PathData.Split('M', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void BooleanOp_IdenticalSquares_XorReturnsEmpty()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.BooleanOp(square, square, BooleanOperation.Xor);

        Assert.True(result.Success);
        Assert.Empty(result.PathData!);
    }

    // ---- Boolean operations: error handling ----

    [Fact]
    public void BooleanOp_EmptySubject_ReturnsError()
    {
        var result = _service.BooleanOp("", "M0,0 L100,0 L100,100 Z", BooleanOperation.Union);

        Assert.False(result.Success);
        Assert.Contains("Subject path is empty", result.Error);
    }

    [Fact]
    public void BooleanOp_EmptyClip_ReturnsError()
    {
        var result = _service.BooleanOp("M0,0 L100,0 L100,100 Z", "", BooleanOperation.Union);

        Assert.False(result.Success);
        Assert.Contains("Clip path is empty", result.Error);
    }

    [Fact]
    public void BooleanOp_InvalidPath_ReturnsError()
    {
        var result = _service.BooleanOp("not a path", "M0,0 L100,0 L100,100 Z", BooleanOperation.Union);

        Assert.False(result.Success);
        Assert.Contains("Unsupported SVG path command", result.Error);
    }

    // ---- Stroke offset: basic ----

    [Fact]
    public void StrokeOffset_SquareOutward_ReturnsLargerPath()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.StrokeOffset(square, 10);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
    }

    [Fact]
    public void StrokeOffset_SquareInward_ReturnsSmallerPath()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.StrokeOffset(square, -10);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
    }

    [Fact]
    public void StrokeOffset_ZeroOffset_ReturnsSamePath()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.StrokeOffset(square, 0);

        Assert.True(result.Success);
        Assert.Equal(square, result.PathData);
    }

    [Fact]
    public void StrokeOffset_WithMiterJoin_Succeeds()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.StrokeOffset(square, 5, OffsetJoinType.Miter);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void StrokeOffset_WithSquareJoin_Succeeds()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result = _service.StrokeOffset(square, 5, OffsetJoinType.Square);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void StrokeOffset_EmptyPath_ReturnsError()
    {
        var result = _service.StrokeOffset("", 10);

        Assert.False(result.Success);
        Assert.Contains("Path is empty", result.Error);
    }

    [Fact]
    public void StrokeOffset_TooMuchInward_ReturnsEmpty()
    {
        var smallSquare = "M0,0 L10,0 L10,10 L0,10 Z";

        var result = _service.StrokeOffset(smallSquare, -20);

        Assert.True(result.Success);
        Assert.Empty(result.PathData!);
    }

    // ---- UnionAll ----

    [Fact]
    public void UnionAll_MultiplePaths_ReturnsUnifiedPath()
    {
        var paths = new[]
        {
            "M0,0 L50,0 L50,50 L0,50 Z",
            "M25,25 L75,25 L75,75 L25,75 Z",
            "M50,50 L100,50 L100,100 L50,100 Z"
        };

        var result = _service.UnionAll(paths);

        Assert.True(result.Success);
        Assert.NotNull(result.PathData);
        Assert.NotEmpty(result.PathData);
    }

    [Fact]
    public void UnionAll_SinglePath_ReturnsSamePath()
    {
        var paths = new[] { "M0,0 L100,0 L100,100 L0,100 Z" };

        var result = _service.UnionAll(paths);

        Assert.True(result.Success);
        Assert.Equal("M0,0L100,0L100,100L0,100Z", result.PathData);
    }

    [Fact]
    public void UnionAll_EmptyArray_ReturnsError()
    {
        var result = _service.UnionAll(Array.Empty<string>());

        Assert.False(result.Success);
        Assert.Contains("No valid paths", result.Error);
    }

    // ---- SVG path parsing: complex paths ----

    [Fact]
    public void BooleanOp_PathWithCubicBezier_Succeeds()
    {
        var bezierPath = "M0,0 C25,50 75,50 100,0 L100,100 L0,100 Z";
        var square = "M25,25 L75,25 L75,75 L25,75 Z";

        var result = _service.BooleanOp(bezierPath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithQuadraticBezier_Succeeds()
    {
        var quadPath = "M0,0 Q50,100 100,0 L100,100 L0,100 Z";
        var square = "M25,25 L75,25 L75,75 L25,75 Z";

        var result = _service.BooleanOp(quadPath, square, BooleanOperation.Intersect);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithRelativeCommands_Succeeds()
    {
        var relativePath = "M0,0 l100,0 l0,100 l-100,0 z";
        var square = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _service.BooleanOp(relativePath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithHorizontalVerticalLines_Succeeds()
    {
        var hvPath = "M0,0 H100 V100 H0 Z";
        var square = "M50,50 L150,50 L150,150 L50,150 Z";

        var result = _service.BooleanOp(hvPath, square, BooleanOperation.Difference);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    // ---- SVG path parsing: relative commands ----

    [Fact]
    public void BooleanOp_PathWithRelativeMove_Succeeds()
    {
        // Relative 'm' after implicit start at origin
        var relativePath = "m10,10 l20,0 l0,20 l-20,0 z";
        var square = "M0,0 L50,0 L50,50 L0,50 Z";

        var result = _service.BooleanOp(relativePath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithRelativeHorizontal_Succeeds()
    {
        // Relative 'h' for horizontal line
        var relativePath = "M0,0 h50 v50 h-50 z";
        var square = "M25,25 L75,25 L75,75 L25,75 Z";

        var result = _service.BooleanOp(relativePath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithRelativeVertical_Succeeds()
    {
        // Relative 'v' for vertical line
        var relativePath = "M0,0 h50 v50 h-50 v-50";
        var square = "M25,25 L75,25 L75,75 L25,75 Z";

        var result = _service.BooleanOp(relativePath, square, BooleanOperation.Intersect);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithRelativeCubicBezier_Succeeds()
    {
        // Relative 'c' for cubic bezier curve
        var relativePath = "M0,0 c10,0 20,10 20,20 l0,30 l-20,0 z";
        var square = "M5,5 L30,5 L30,40 L5,40 Z";

        var result = _service.BooleanOp(relativePath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_PathWithRelativeQuadraticBezier_Succeeds()
    {
        // Relative 'q' for quadratic bezier curve
        var relativePath = "M0,0 q10,10 20,0 l0,30 l-20,0 z";
        var square = "M5,5 L25,5 L25,30 L5,30 Z";

        var result = _service.BooleanOp(relativePath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    [Fact]
    public void BooleanOp_MultiSubpathWithRelativeMove_Succeeds()
    {
        // Multi-subpath with relative 'm' to start a new subpath
        var multiPath = "M0,0 L10,0 L10,10 L0,10 Z m15,15 l10,0 l0,10 l-10,0 z";
        var square = "M5,5 L30,5 L30,30 L5,30 Z";

        var result = _service.BooleanOp(multiPath, square, BooleanOperation.Union);

        Assert.True(result.Success);
        Assert.NotEmpty(result.PathData!);
    }

    // ---- Output format: deterministic ----

    [Fact]
    public void BooleanOp_SameInputs_ProducesSameOutput()
    {
        var square1 = "M0,0 L100,0 L100,100 L0,100 Z";
        var square2 = "M50,50 L150,50 L150,150 L50,150 Z";

        var result1 = _service.BooleanOp(square1, square2, BooleanOperation.Union);
        var result2 = _service.BooleanOp(square1, square2, BooleanOperation.Union);

        Assert.Equal(result1.PathData, result2.PathData);
    }

    [Fact]
    public void StrokeOffset_SameInputs_ProducesSameOutput()
    {
        var square = "M0,0 L100,0 L100,100 L0,100 Z";

        var result1 = _service.StrokeOffset(square, 5, OffsetJoinType.Round);
        var result2 = _service.StrokeOffset(square, 5, OffsetJoinType.Round);

        Assert.Equal(result1.PathData, result2.PathData);
    }

    [Theory]
    [InlineData("M0,0 L10")]
    [InlineData("M0,0 C1,2,3,4,5")]
    [InlineData("M0,0 Q1,2,3")]
    [InlineData("M0,0 L10,10 trailing")]
    [InlineData("M0,0,")]
    public void StrokeOffset_MalformedOrPartiallyParsedPath_FailsBeforeNoOp(string path)
    {
        var result = _service.StrokeOffset(path, 0);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
    }

    private const string ArcCircle = "M100,50 A50,50 0 1 1 0,50 50,50 0 1 1 100,50 Z";

    [Theory]
    [InlineData("M10,0 A10,10 0 0 0 0,10", 0, 0, 10, 10)]
    [InlineData("M10,0 A10,10 0 0 1 0,10", 0, 0, 10, 10)]
    [InlineData("M10,0 A10,10 0 1 0 0,10", -10, -10, 20, 20)]
    [InlineData("M10,0 A10,10 0 1 1 0,10", 0, 0, 20, 20)]
    [InlineData("M0,0 A5,5 0 0 1 20,0", 0, -10, 20, 10)]
    [InlineData("M0,0 A1e-200,1e-200 0 0 1 20,0", 0, -10, 20, 10)]
    [InlineData("M0,0 A5,10 0 0 1 20,0", 0, -20, 20, 20)]
    [InlineData("M0,20 A20,10 90 0 1 0,-20", -10, -20, 10, 40)]
    [InlineData("M30,40 a10,20 0 0 1 20,0 10,20 0 0 1 -20,0", 30, 20, 20, 40)]
    public void GetBounds_Arc_UsesEllipseNotEndpointChord(string path, double x, double y, double width, double height)
    {
        AssertPathBounds(path, x, y, width, height);
    }

    [Fact]
    public void GetBounds_RotatedEllipse_MatchesAnalyticExtentsWithinFlatteningTolerance()
    {
        // Ellipse centered at (50,50), radii (40,20), rotated 45 degrees.
        const string path = "M78.2842712474619,78.2842712474619 A40,20 45 0 1 21.7157287525381,21.7157287525381 40,20 45 0 1 78.2842712474619,78.2842712474619";
        var radius = Math.Sqrt(1000);

        AssertPathBounds(path, 50 - radius, 50 - radius, 2 * radius, 2 * radius);
    }

    [Theory]
    [InlineData("M0,0 A1e-320,1000000000 0 01 0,100", 0, 0, 0, 100)]
    [InlineData("M0,0 A1000000000,1e-320 0 01 100,0", 0, 0, 100, 0)]
    [InlineData("M0,0 A1,1 0 11 5e-324,0", -1, -2, 2, 2)]
    [InlineData("M0,0 A1,1 0 01 5e-324,0", 0, 0, 0, 0)]
    [InlineData("M0,0 A1,1 0 10 5e-324,0", -1, 0, 2, 2)]
    [InlineData("M0,0 A1,1 0 11 0,5e-324", 0, -1, 2, 2)]
    [InlineData("M0,0 a1,2 0 11 5e-324,5e-324", -0.5527864045, -3.788854382, 2, 4)]
    [InlineData("M0,0 A1,2 45 11 5e-324,0", -0.632455532, -3.162277660, 3.162277660, 3.162277660)]
    [InlineData("M0,0 A2,2 45 11 5e-324,0", -2, -4, 4, 4)]
    [InlineData("M0,0 A1e-320,1e-320 0 01 20,0", 0, -10, 20, 10)]
    [InlineData("M0,0 A5e-324,5e-324 0 01 20,0", 0, -10, 20, 10)]
    public void Arc_ExtremeFiniteValues_PreserveBoundsAndPassZeroOffset(
        string path, double x, double y, double width, double height)
    {
        var offset = _service.StrokeOffset(path, 0);
        Assert.True(offset.Success, offset.Error);
        Assert.Equal(path, offset.PathData);
        AssertPathBounds(path, x, y, width, height);

        var flattened = _service.UnionAll([path]);
        Assert.True(flattened.Success, flattened.Error);
        AssertPathBounds(flattened.PathData!, x, y, width, height);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(45)]
    [InlineData(-270)]
    public void Arc_BoundaryCircle_IgnoresAxisRotation(double rotation)
    {
        var path = FormattableString.Invariant($"M1000000000,0 A1000000000,1000000000 {rotation} 01 -1000000000,0");
        const string unrotated = "M1000000000,0 A1000000000,1000000000 0 01 -1000000000,0";

        var offset = _service.StrokeOffset(path, 0);
        Assert.True(offset.Success, offset.Error);
        Assert.Equal(path, offset.PathData);
        AssertPathBounds(path, -1000000000, 0, 2000000000, 1000000000);
        var flattened = _service.UnionAll([path]);
        var expected = _service.UnionAll([unrotated]);
        Assert.True(flattened.Success, flattened.Error);
        Assert.True(expected.Success, expected.Error);
        Assert.Equal(expected.PathData, flattened.PathData);
    }

    [Theory]
    [InlineData(1.0000000000000002, true)]
    [InlineData(1.000000001, false)]
    public void Arc_RadiusCorrection_IgnoresOnlyRoundoffSizedExcess(double endpoint, bool success)
    {
        var path = FormattableString.Invariant($"M{endpoint},0 A1,1000000000 0 01 {-endpoint},0");

        var bounds = _service.GetBounds(path);
        var offset = _service.StrokeOffset(path, 0);

        Assert.Equal(success, bounds.Success);
        Assert.Equal(success, offset.Success);
        if (success)
        {
            Assert.Equal(path, offset.PathData);
            AssertPathBounds(path, -1, 0, 2, 1000000000);
        }
        else
        {
            Assert.Contains("exceeds the coordinate limit", bounds.Error);
            Assert.Equal(bounds.Error, offset.Error);
        }
    }

    [Fact]
    public void Arc_SubnormalChord_StillEnforcesCoordinateLimit()
    {
        const string path = "M0,0 A1000000000,1000000000 0 11 5e-324,0";

        var bounds = _service.GetBounds(path);
        var offset = _service.StrokeOffset(path, 0);

        Assert.False(bounds.Success);
        Assert.False(offset.Success);
        Assert.Contains("exceeds the coordinate limit", bounds.Error);
        Assert.Equal(bounds.Error, offset.Error);
    }

    [Fact]
    public void Arc_SubnormalChord_StillChargesFlatteningBudget()
    {
        var path = "M0,0" + string.Concat(Enumerable.Repeat(" a1,1 0 11 5e-324,0", 4000));

        var bounds = _service.GetBounds(path);
        var offset = _service.StrokeOffset(path, 0);

        Assert.False(bounds.Success);
        Assert.False(offset.Success);
        Assert.Contains("flattened path complexity exceeds", bounds.Error);
        Assert.Equal(bounds.Error, offset.Error);
    }

    [Theory]
    [InlineData("M10,20 a30,15 35 0 1 60,20 30,15 35 1 0 -60,-20Z", "M10,20 A30,15 35 0 1 70,40 A30,15 35 1 0 10,20Z")]
    [InlineData("M10,20 A30 15 35 0170 40 30 15 35 1010 20Z", "M10,20 A30,15 35 0 1 70,40 A30,15 35 1 0 10,20Z")]
    [InlineData("M10,20 a30 15 35 01.5-.5 30 15 35 10-.5.5Z", "M10,20 a30,15 35 0 1 .5,-.5 a30,15 35 1 0 -.5,.5Z")]
    [InlineData("M10,20 A-30,-15 35 0 1 70,40", "M10,20 A30,15 35 0 1 70,40")]
    [InlineData("M0,20 A20,10 450 0 1 0,-20", "M0,20 A20,10 90 0 1 0,-20")]
    [InlineData("M0,20 A20,10 -270 0 1 0,-20", "M0,20 A20,10 90 0 1 0,-20")]
    [InlineData("M10,20 A0,15 35 1 1 70,40", "M10,20 L70,40")]
    [InlineData("M10,20 a30,0 35 1 1 60,20", "M10,20 L70,40")]
    [InlineData("M10,20 A30,15 35 1 1 10,20", "M10,20")]
    [InlineData("M10,20 a0,0 35 1 1 0,0 l20,10", "M10,20 l20,10")]
    public void UnionAll_EquivalentArcForms_ProduceIdenticalFlattenedGeometry(string path, string equivalent)
    {
        var result = _service.UnionAll([path]);
        var expected = _service.UnionAll([equivalent]);

        Assert.True(result.Success, result.Error);
        Assert.True(expected.Success, expected.Error);
        Assert.Equal(expected.PathData, result.PathData);
        Assert.Equal(result.PathData, _service.UnionAll([path]).PathData);
    }

    [Theory]
    [InlineData("A10,10 0 0 1 30,0")]
    [InlineData("A0,10 0 0 1 30,0")]
    [InlineData("a10,10 0 1 1 0,0")]
    public void UnionAll_ArcResetsSmoothControlState_EvenWhenDegenerate(string arc)
    {
        foreach (var (previous, smooth, explicitCurve) in new[]
        {
            ("C5,10 10,10 10,0", "s10,10 20,0", "c0,0 10,10 20,0"),
            ("Q10,10 10,0", "t20,0", "q0,0 20,0")
        })
        {
            var result = _service.UnionAll([$"M0,0 {previous} {arc} {smooth}"]);
            var expected = _service.UnionAll([$"M0,0 {previous} {arc} {explicitCurve}"]);

            Assert.True(result.Success, result.Error);
            Assert.True(expected.Success, expected.Error);
            Assert.Equal(expected.PathData, result.PathData);
        }
    }

    [Theory]
    [InlineData(BooleanOperation.Union, 0, 0, 100, 100, 1)]
    [InlineData(BooleanOperation.Intersect, 40, 40, 20, 20, 1)]
    [InlineData(BooleanOperation.Difference, 0, 0, 100, 100, 2)]
    [InlineData(BooleanOperation.Xor, 0, 0, 100, 100, 2)]
    public void BooleanOp_ArcCircle_HasExpectedGeometry(
        BooleanOperation operation, double x, double y, double width, double height, int subpaths)
    {
        var result = _service.BooleanOp(ArcCircle, "M40,40h20v20h-20Z", operation);

        Assert.True(result.Success, result.Error);
        Assert.Equal(subpaths, CountSubpaths(result.PathData!));
        AssertPathBounds(result.PathData!, x, y, width, height);
        Assert.Equal(result.PathData, _service.BooleanOp(ArcCircle, "M40,40h20v20h-20Z", operation).PathData);
    }

    [Theory]
    [InlineData(0, "M3,3h1v1h-1Z", true)]
    [InlineData(0, "M6,6h1v1h-1Z", false)]
    [InlineData(1, "M3,3h1v1h-1Z", false)]
    [InlineData(1, "M6,6h1v1h-1Z", true)]
    public void BooleanOp_ArcSweep_SelectsCorrectSideOfChord(int sweep, string probe, bool overlaps)
    {
        var result = _service.BooleanOp($"M10,0 A10,10 0 0 {sweep} 0,10Z", probe, BooleanOperation.Intersect);

        Assert.True(result.Success, result.Error);
        Assert.Equal(overlaps, !string.IsNullOrEmpty(result.PathData));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    public void StrokeOffset_ArcCircle_ChangesRadius(double offset)
    {
        var result = _service.StrokeOffset(ArcCircle, offset);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, CountSubpaths(result.PathData!));
        AssertPathBounds(result.PathData!, -offset, -offset, 100 + 2 * offset, 100 + 2 * offset);
        Assert.Equal(result.PathData, _service.StrokeOffset(ArcCircle, offset).PathData);
    }

    [Fact]
    public void StrokeOffset_ZeroOffset_ValidatesAndPreservesOpenArc()
    {
        const string path = "M10,20 a30 15 35 0170 40";
        var result = _service.StrokeOffset(path, 0);

        Assert.True(result.Success, result.Error);
        Assert.Equal(path, result.PathData);
    }

    [Fact]
    public void UnionAll_ArcCircles_ReturnsBothContours()
    {
        var result = _service.UnionAll([ArcCircle, "M250,50 a50,50 0 01-100,0 50,50 0 01100,0Z"]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, CountSubpaths(result.PathData!));
        AssertPathBounds(result.PathData!, 0, 0, 250, 100);
    }

    [Theory]
    [InlineData(PathFillRule.NonZero, 1)]
    [InlineData(PathFillRule.EvenOdd, 2)]
    public void ArcRing_AllOperations_PreserveFillRule(PathFillRule fillRule, int subpaths)
    {
        const string ring = ArcCircle + " M75,50 A25,25 0 1 1 25,50 25,25 0 1 1 75,50Z";
        var boolean = _service.BooleanOp(ring, OuterSquare, BooleanOperation.Intersect, fillRule, CancellationToken.None);
        var union = _service.UnionAll([ring, OuterSquare], fillRule, CancellationToken.None);
        var offset = _service.StrokeOffset(ring, 5, OffsetJoinType.Round, OffsetEndType.Polygon, 2, fillRule, CancellationToken.None);

        Assert.True(boolean.Success, boolean.Error);
        Assert.True(union.Success, union.Error);
        Assert.True(offset.Success, offset.Error);
        Assert.Equal(subpaths, CountSubpaths(boolean.PathData!));
        Assert.Equal(subpaths, CountSubpaths(offset.PathData!));
        var center = _service.BooleanOp(union.PathData!, "M45,45h10v10h-10Z", BooleanOperation.Intersect);
        Assert.True(center.Success, center.Error);
        Assert.NotEmpty(center.PathData!);
        var middle = _service.BooleanOp(union.PathData!, "M80,45h10v10h-10Z", BooleanOperation.Intersect);
        Assert.True(middle.Success, middle.Error);
        Assert.Equal(fillRule == PathFillRule.EvenOdd, string.IsNullOrEmpty(middle.PathData));
    }

    [Theory]
    [InlineData("M0,0 A10,10 0 2 0 20,20")]
    [InlineData("M0,0 A10,10 0 0 2 20,20")]
    [InlineData("M0,0 A10,10 0 -1 0 20,20")]
    [InlineData("M0,0 A10,10 0 0 +1 20,20")]
    [InlineData("M0,0 A10,10 0 0.0 1 20,20")]
    [InlineData("M0,0 A10,10 0 0 1.0 20,20")]
    [InlineData("M0,0 A10,10 0 0 1e0 20,20")]
    [InlineData("M0,0 A10,10 0 02 20,20")]
    [InlineData("M0,0 A10,10 0 01 20")]
    [InlineData("M0,0 A10,10 0 01 20,20 10,10 0 01 30")]
    [InlineData("M0,0 A10,10 0 01 20,20,")]
    [InlineData("M0,0 A10,10 0 0,,1 20,20")]
    [InlineData("M0,0 A10,10 0 0 Z")]
    [InlineData("A10,10 0 01 20,20")]
    [InlineData("M0,0 A1e309,10 0 01 20,20")]
    [InlineData("M0,0 A10,10 1e309 01 20,20")]
    [InlineData("M0,0 A10,10 0 01 1e309,20")]
    [InlineData("M0,0 A1000000001,10 0 01 20,20")]
    [InlineData("M0,0 A0,10 0 01 1000000001,20")]
    [InlineData("M1000000000,0 a0,10 0 01 1,0")]
    [InlineData("M1000000000,0 A1000000000,1000000000 0 11 1000000000,1000000000")]
    public void Arc_MalformedOrOutOfRangeInput_FailsAcrossSharedParserConsumers(string path)
    {
        var bounds = _service.GetBounds(path);
        var boolean = _service.BooleanOp(path, OuterSquare, BooleanOperation.Union);
        var offset = _service.StrokeOffset(path, 0);
        var union = _service.UnionAll([path]);

        Assert.False(bounds.Success);
        Assert.False(boolean.Success);
        Assert.False(offset.Success);
        Assert.False(union.Success);
        Assert.NotEmpty(bounds.Error!);
        Assert.Equal(bounds.Error, boolean.Error);
        Assert.Equal(bounds.Error, offset.Error);
        Assert.Equal(bounds.Error, union.Error);
    }

    [Fact]
    public void Arc_RepeatedDegenerateGroups_ChargePerPathAndAggregateCommandBudgets()
    {
        var path = "M0,0 A" + string.Concat(Enumerable.Repeat("1,1 0 01 0,0 ", PathOperationsService.MaxCommandCount));
        var result = _service.StrokeOffset(path, 0);
        Assert.False(result.Success);
        Assert.Contains("segment count exceeds", result.Error);

        var component = "M0,0 A" + string.Concat(Enumerable.Repeat("1,1 0 01 0,0 ", 8000));
        Assert.True(_service.GetBounds(component).Success);
        var aggregate = _service.UnionAll([component, component, component]);
        Assert.False(aggregate.Success);
        Assert.Contains($"segment count exceeds maximum ({PathOperationsService.MaxAggregateCommandCount})", aggregate.Error);
    }

    [Fact]
    public void Arc_Subdivision_ChargesSharedFlattenedPointBudget()
    {
        var component = "M10,0" + string.Concat(Enumerable.Repeat(" A10,10 0 11 0,10 10,10 0 11 10,0", 1400));
        Assert.True(_service.GetBounds(component).Success);

        var boolean = _service.BooleanOp(component, component, BooleanOperation.Union);
        var union = _service.UnionAll([component, component]);
        var offset = _service.StrokeOffset(component + component, 0);
        var bounds = _service.GetBounds(component + component);
        Assert.False(boolean.Success);
        Assert.False(union.Success);
        Assert.False(offset.Success);
        Assert.False(bounds.Success);
        Assert.Contains("flattened path complexity exceeds", boolean.Error);
        Assert.Equal(boolean.Error, union.Error);
        Assert.Equal(boolean.Error, offset.Error);
        Assert.Equal(boolean.Error, bounds.Error);
    }

    [Fact]
    public void Arc_SourceLengthBudgets_AreEnforced()
    {
        var component = ArcCircle.PadRight(PathOperationsService.MaxPathDataCharacters);
        var result = _service.GetBounds(component + " ");
        Assert.False(result.Success);
        Assert.Contains("Path data exceeds maximum length", result.Error);

        var aggregate = _service.UnionAll([component, component, ArcCircle]);
        Assert.False(aggregate.Success);
        Assert.Contains("Combined path data exceeds", aggregate.Error);
    }

    [Fact]
    public void Arc_PreCancelled_ThrowsAcrossSharedParserConsumers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _service.GetBounds(ArcCircle, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => _service.BooleanOp(ArcCircle, OuterSquare, BooleanOperation.Union, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => _service.UnionAll([ArcCircle], cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => _service.StrokeOffset(ArcCircle, 0, OffsetJoinType.Round, OffsetEndType.Polygon, 2, cancellation.Token));
    }

    [Fact]
    public void BooleanOp_SmoothCubicAndQuadraticCommands_AreFlattened()
    {
        const string smoothPath = "M0,50 C20,0 30,0 50,50 S80,100 100,50 L100,100 L0,100 Z";
        const string smoothQuadratic = "M0,50 Q25,0 50,50 T100,50 L100,100 L0,100 Z";
        const string clip = "M0,0 L100,0 L100,100 L0,100 Z";

        var cubicResult = _service.BooleanOp(smoothPath, clip, BooleanOperation.Intersect);
        var quadraticResult = _service.BooleanOp(smoothQuadratic, clip, BooleanOperation.Intersect);

        Assert.True(cubicResult.Success);
        Assert.True(quadraticResult.Success);
        Assert.Contains("50,", cubicResult.PathData!);
        Assert.Contains("100,50", quadraticResult.PathData!);
    }

    [Theory]
    [InlineData("MNaN,0 L1,1 Z")]
    [InlineData("M1e309,0 L1,1 Z")]
    [InlineData("M1000000001,0 L1,1 Z")]
    public void UnionAll_NonFiniteOrOutOfRangeCoordinate_FailsEvenForSingleInput(string path)
    {
        var result = _service.UnionAll([path]);

        Assert.False(result.Success);
    }

    [Fact]
    public void UnionAll_EmptyEntry_DoesNotSilentlyFilterIt()
    {
        var result = _service.UnionAll(["M0,0L1,0L1,1Z", " "]);

        Assert.False(result.Success);
        Assert.Contains("empty path", result.Error);
    }

    [Fact]
    public void StrokeOffset_RejectsNonFiniteOptions()
    {
        const string square = "M0,0L10,0L10,10L0,10Z";

        Assert.False(_service.StrokeOffset(square, double.NaN).Success);
        Assert.False(_service.StrokeOffset(square, 1, miterLimit: double.PositiveInfinity).Success);
    }

    [Fact]
    public void StrokeOffset_ImplicitSegmentsCountTowardCommandBudget()
    {
        var path = new System.Text.StringBuilder("M0,0");
        for (var index = 0; index <= PathOperationsService.MaxCommandCount; index++)
            path.Append(" 1,1");

        var result = _service.StrokeOffset(path.ToString(), 0);

        Assert.False(result.Success);
        Assert.Contains("segment count exceeds", result.Error);
    }

    [Fact]
    public void UnionAll_BoundsEnumerationBeforeMaterializingEverything()
    {
        static IEnumerable<string> TooManyPaths()
        {
            for (var index = 0; index <= PathOperationsService.MaxInputPathCount; index++)
                yield return "M0,0L1,0L1,1Z";
        }

        var result = _service.UnionAll(TooManyPaths());

        Assert.False(result.Success);
        Assert.Contains("Path count exceeds", result.Error);
    }

    [Fact]
    public void BooleanOp_PreCancelled_ThrowsWithoutNativeWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _service.BooleanOp(
            "M0,0L10,0L10,10Z",
            "M0,0L5,0L5,5Z",
            BooleanOperation.Union,
            cancellation.Token));
    }

    [Fact]
    public void UnionAll_AggregateSourceBudget_IsEnforced()
    {
        var component = $"M0,0L1,0L1,1Z {new string(' ', PathOperationsService.MaxPathDataCharacters - 16)}";

        var result = _service.UnionAll(Enumerable.Repeat(component, 9));

        Assert.False(result.Success);
        Assert.Contains("Combined path data exceeds", result.Error);
    }


    // ---- Fill rules ----

    private const string SameWindingRing = "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z";
    private const string OuterSquare = "M0,0 L100,0 L100,100 L0,100 Z";
    private const string InnerSquare = "M25,25 L75,25 L75,75 L25,75 Z";

    [Fact]
    public void BooleanOp_EvenOdd_KeepsHoleOfSameWindingRing()
    {
        var evenOdd = _service.BooleanOp(SameWindingRing, OuterSquare, BooleanOperation.Intersect, PathFillRule.EvenOdd, CancellationToken.None);
        var nonZero = _service.BooleanOp(SameWindingRing, OuterSquare, BooleanOperation.Intersect, PathFillRule.NonZero, CancellationToken.None);

        Assert.True(evenOdd.Success, evenOdd.Error);
        Assert.True(nonZero.Success, nonZero.Error);
        Assert.Equal(2, CountSubpaths(evenOdd.PathData!));
        Assert.Equal(1, CountSubpaths(nonZero.PathData!));
    }

    [Fact]
    public void BooleanOp_DefaultOverload_UsesNonZero()
    {
        var implicitRule = _service.BooleanOp(SameWindingRing, OuterSquare, BooleanOperation.Intersect);
        var explicitRule = _service.BooleanOp(SameWindingRing, OuterSquare, BooleanOperation.Intersect, PathFillRule.NonZero, CancellationToken.None);

        Assert.True(implicitRule.Success);
        Assert.Equal(explicitRule.PathData, implicitRule.PathData);
    }

    [Fact]
    public void UnionAll_EvenOdd_TurnsNestedSquareIntoHole()
    {
        var evenOdd = _service.UnionAll([OuterSquare, InnerSquare], PathFillRule.EvenOdd, CancellationToken.None);
        var nonZero = _service.UnionAll([OuterSquare, InnerSquare], PathFillRule.NonZero, CancellationToken.None);

        Assert.True(evenOdd.Success, evenOdd.Error);
        Assert.True(nonZero.Success, nonZero.Error);
        Assert.Equal(2, CountSubpaths(evenOdd.PathData!));
        Assert.Equal(1, CountSubpaths(nonZero.PathData!));
    }

    [Fact]
    public void UnionAll_SinglePath_NonZeroPassesThroughButEvenOddResolvesOverlaps()
    {
        var evenOdd = _service.UnionAll([SameWindingRing], PathFillRule.EvenOdd, CancellationToken.None);
        var nonZero = _service.UnionAll([SameWindingRing], PathFillRule.NonZero, CancellationToken.None);
        var legacy = _service.UnionAll([SameWindingRing]);

        Assert.True(evenOdd.Success, evenOdd.Error);
        Assert.True(nonZero.Success, nonZero.Error);
        Assert.Equal(2, CountSubpaths(evenOdd.PathData!));
        Assert.Equal(legacy.PathData, nonZero.PathData);
        var evenOddIntersect = _service.BooleanOp(evenOdd.PathData!, OuterSquare, BooleanOperation.Intersect);
        Assert.Equal(2, CountSubpaths(evenOddIntersect.PathData!));
    }

    [Fact]
    public void StrokeOffset_EvenOdd_OffsetsHoleInward()
    {
        var evenOdd = _service.StrokeOffset(SameWindingRing, 5, OffsetJoinType.Miter, OffsetEndType.Polygon, 2.0, PathFillRule.EvenOdd, CancellationToken.None);
        var nonZero = _service.StrokeOffset(SameWindingRing, 5, OffsetJoinType.Miter, OffsetEndType.Polygon, 2.0, PathFillRule.NonZero, CancellationToken.None);

        Assert.True(evenOdd.Success, evenOdd.Error);
        Assert.True(nonZero.Success, nonZero.Error);
        Assert.Equal(2, CountSubpaths(evenOdd.PathData!));
        Assert.Equal(1, CountSubpaths(nonZero.PathData!));
    }

    [Theory]
    [InlineData("nonzero", PathFillRule.NonZero)]
    [InlineData("NonZero", PathFillRule.NonZero)]
    [InlineData("evenodd", PathFillRule.EvenOdd)]
    [InlineData("EVENODD", PathFillRule.EvenOdd)]
    public void PathFillRules_TryParse_IsCaseInsensitive(string value, PathFillRule expected)
    {
        Assert.True(PathFillRules.TryParse(value, out var parsed));
        Assert.Equal(expected, parsed);
        Assert.Equal(expected == PathFillRule.NonZero ? "nonzero" : "evenodd", PathFillRules.ToName(parsed));
    }

    [Theory]
    [InlineData("even-odd")]
    [InlineData("")]
    [InlineData(null)]
    public void PathFillRules_TryParse_RejectsUnknownValues(string? value)
    {
        Assert.False(PathFillRules.TryParse(value, out _));
    }

    private void AssertPathBounds(string path, double x, double y, double width, double height)
    {
        var result = _service.GetBounds(path);
        Assert.True(result.Success, result.Error);
        var bounds = result.Bounds!.Value;
        Assert.InRange(bounds.X, x - 0.05, x + 0.05);
        Assert.InRange(bounds.Y, y - 0.05, y + 0.05);
        Assert.InRange(bounds.Width, width - 0.05, width + 0.05);
        Assert.InRange(bounds.Height, height - 0.05, height + 0.05);
    }

    private static int CountSubpaths(string pathData) => pathData.Count(c => c == 'M');
}
