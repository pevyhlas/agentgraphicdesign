using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Layout;

/// <summary>
/// Bounds, alignment, distribution, and fit semantics of the core engine.
/// World bounds follow the renderer's transform order (translate, rotate, scale;
/// scale applies first to local geometry) and are clipped by clipping frames.
/// Text uses the fallback estimator here because no measurer is registered.
/// </summary>
public class BoundsAndAlignmentTests
{
    private readonly DocumentEngine _engine = new();

    // ---- Bounds: local and world ----

    [Fact]
    public void GetBounds_Rectangle_ReportsLocalAndTranslatedWorld()
    {
        var doc = NewDocument();
        var rect = Add(doc.Root, Rect(100, 50, x: 10, y: 20));

        var bounds = Single(_engine.GetBounds(doc, rect.Id));

        Assert.Equal("rectangle", bounds.Type);
        Assert.Equal(new BoundingBox(0, 0, 100, 50), bounds.Local);
        Assert.Equal(new BoundingBox(10, 20, 100, 50), bounds.World);
        Assert.Null(bounds.Measured);
        Assert.False(bounds.Clipped);
        Assert.Empty(bounds.Diagnostics);
    }

    [Fact]
    public void GetBounds_RotatedRectangle_WorldIsAabbOfFourCorners()
    {
        var doc = NewDocument();
        var rect = Add(doc.Root, Rect(100, 50, x: 100, y: 100, rotation: 90));

        var bounds = Single(_engine.GetBounds(doc, rect.Id));

        // Clockwise rotation in y-down space: (100,0) -> (0,100), (0,50) -> (-50,0).
        Assert.Equal(new BoundingBox(50, 100, 50, 100), bounds.World);
        Assert.Equal(new BoundingBox(0, 0, 100, 50), bounds.Local);
    }

    [Fact]
    public void GetBounds_Rotated45_WorldExtentsMatchTrigonometry()
    {
        var doc = NewDocument();
        var rect = Add(doc.Root, Rect(100, 100, x: 200, y: 200, rotation: 45));

        var world = Single(_engine.GetBounds(doc, rect.Id)).World!.Value;

        var half = 100 * Math.Sqrt(2) / 2;
        Assert.Equal(200 - half, world.X, 3);
        Assert.Equal(200, world.Y, 3);
        Assert.Equal(2 * half, world.Width, 3);
        Assert.Equal(2 * half, world.Height, 3);
    }

    [Fact]
    public void GetBounds_ScaledGroup_ScalesChildBeforeTranslating()
    {
        var doc = NewDocument();
        var group = Add(doc.Root, new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "group",
            Transform = new Transform { X = 20, Y = 30, ScaleX = 2, ScaleY = 3 },
            Children = []
        });
        var rect = Add(group, Rect(10, 10, x: 5, y: 5));

        var all = _engine.GetBounds(doc);
        var rectBounds = all.Single(b => b.Id == rect.Id);
        var groupBounds = all.Single(b => b.Id == group.Id);

        Assert.Equal(new BoundingBox(30, 45, 20, 30), rectBounds.World);
        Assert.Equal(new BoundingBox(5, 5, 10, 10), groupBounds.Local);
        Assert.Equal(new BoundingBox(30, 45, 20, 30), groupBounds.World);
    }

    [Fact]
    public void GetBounds_NestedTranslations_Accumulate()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, Frame(200, 200, x: 10, y: 10, clip: false));
        var group = Add(frame, new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "group",
            Transform = new Transform { X = 5, Y = 5 },
            Children = []
        });
        var rect = Add(group, Rect(10, 10, x: 1, y: 1));

        var all = _engine.GetBounds(doc);

        Assert.Equal(new BoundingBox(16, 16, 10, 10), all.Single(b => b.Id == rect.Id).World);
        // A non-clipping frame reports the union of its own rectangle and its children.
        Assert.Equal(new BoundingBox(10, 10, 200, 200), all.Single(b => b.Id == frame.Id).World);
    }

    [Fact]
    public void GetBounds_Polygon_UsesPointExtents()
    {
        var doc = NewDocument();
        var polygon = Add(doc.Root, new PolygonNode
        {
            Id = Guid.NewGuid(),
            Name = "polygon",
            Transform = new Transform { X = 100, Y = 100 },
            Points = [new Point { X = 0, Y = 0 }, new Point { X = 50, Y = 10 }, new Point { X = 20, Y = 40 }]
        });

        var bounds = Single(_engine.GetBounds(doc, polygon.Id));

        Assert.Equal(new BoundingBox(0, 0, 50, 40), bounds.Local);
        Assert.Equal(new BoundingBox(100, 100, 50, 40), bounds.World);
    }

    [Fact]
    public void GetBounds_Line_UsesEndpointExtents()
    {
        var doc = NewDocument();
        var line = Add(doc.Root, new LineNode { Id = Guid.NewGuid(), Name = "line", X1 = 10, Y1 = 40, X2 = 60, Y2 = 5 });

        var bounds = Single(_engine.GetBounds(doc, line.Id));

        Assert.Equal(new BoundingBox(10, 5, 50, 35), bounds.Local);
    }

    [Fact]
    public void GetBounds_Path_UsesParsedGeometry()
    {
        var doc = NewDocument();
        var path = Add(doc.Root, new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "path",
            D = "M10,10 L110,10 C110,60 60,60 60,110 Z"
        });

        var local = Single(_engine.GetBounds(doc, path.Id)).Local!.Value;

        Assert.Equal(10, local.X, 2);
        Assert.Equal(10, local.Y, 2);
        Assert.Equal(100, local.Width, 2);
        Assert.Equal(100, local.Height, 2);
    }

    [Fact]
    public void GetBounds_PathWithArc_ReportsLocalAndTranslatedGeometry()
    {
        var doc = NewDocument();
        var path = Add(doc.Root, new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "arc",
            D = "M0,0 a5,5 0 01 20,0",
            Transform = new Transform { X = 100, Y = 100 }
        });

        var bounds = Single(_engine.GetBounds(doc, path.Id));

        Assert.Equal(new BoundingBox(0, -10, 20, 10), bounds.Local);
        Assert.Equal(new BoundingBox(100, 90, 20, 10), bounds.World);
        Assert.Empty(bounds.Diagnostics);
    }

    [Fact]
    public void GetBounds_PathWithMalformedArc_ReportsDiagnosticInsteadOfGeometry()
    {
        var doc = NewDocument();
        var path = Add(doc.Root, new PathNode { Id = Guid.NewGuid(), Name = "arc", D = "M0,0 A10,10 0 0 2 20,20" });

        var bounds = Single(_engine.GetBounds(doc, path.Id));

        Assert.Null(bounds.Local);
        Assert.Null(bounds.World);
        Assert.Contains(bounds.Diagnostics, d => d.StartsWith("Path bounds are unavailable", StringComparison.Ordinal));
        Assert.Contains(bounds.Diagnostics, d => d.Contains("Arc flag", StringComparison.Ordinal));
    }

    [Fact]
    public void GetBounds_ClippingFrame_ClipsChildWorldBounds()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, Frame(100, 100, x: 50, y: 50, clip: true));
        var rect = Add(frame, Rect(50, 50, x: 80, y: 80));

        var all = _engine.GetBounds(doc);
        var rectBounds = all.Single(b => b.Id == rect.Id);
        var frameBounds = all.Single(b => b.Id == frame.Id);

        Assert.Equal(new BoundingBox(130, 130, 20, 20), rectBounds.World);
        Assert.True(rectBounds.Clipped);
        Assert.Contains(rectBounds.Diagnostics, d => d.Contains("clipped", StringComparison.Ordinal));
        Assert.Equal(new BoundingBox(50, 50, 100, 100), frameBounds.World);
        Assert.Equal(new BoundingBox(0, 0, 100, 100), frameBounds.Local);
    }

    [Fact]
    public void GetBounds_NonClippingFrame_LeavesChildWorldBoundsIntact()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, Frame(100, 100, x: 50, y: 50, clip: false));
        var rect = Add(frame, Rect(50, 50, x: 80, y: 80));

        var rectBounds = _engine.GetBounds(doc).Single(b => b.Id == rect.Id);

        Assert.Equal(new BoundingBox(130, 130, 50, 50), rectBounds.World);
        Assert.False(rectBounds.Clipped);
    }

    [Fact]
    public void GetBounds_ChildFullyOutsideClippingFrame_ReportsFullyClipped()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, Frame(100, 100, clip: true));
        var rect = Add(frame, Rect(10, 10, x: 500, y: 500));

        var rectBounds = _engine.GetBounds(doc).Single(b => b.Id == rect.Id);

        Assert.Null(rectBounds.World);
        Assert.Equal(new BoundingBox(0, 0, 10, 10), rectBounds.Local);
        Assert.True(rectBounds.Clipped);
        Assert.Contains(rectBounds.Diagnostics, d => d.Contains("fully clipped", StringComparison.Ordinal));
    }

    [Fact]
    public void GetBounds_RootFrame_ClipsToCanvas()
    {
        var doc = NewDocument();
        var rect = Add(doc.Root, Rect(100, 100, rotation: 45));

        var bounds = Single(_engine.GetBounds(doc, rect.Id));

        Assert.True(bounds.Clipped);
        Assert.Equal(0, bounds.World!.Value.X);
        Assert.Equal(0, bounds.World.Value.Y);
    }

    [Fact]
    public void GetBounds_TextWithoutMeasurer_UsesEstimateAndFlagsUnmeasured()
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "Hello", FontSize = 20 });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        Assert.Equal(new BoundingBox(0, 0, 60, 24), bounds.Local);
        Assert.Equal(false, bounds.Measured);
        Assert.Contains(bounds.Diagnostics, d => d.Contains("estimated", StringComparison.Ordinal));
    }

    [Fact]
    public void GetBounds_ContainerWithEstimatedText_PropagatesUnmeasuredFlag()
    {
        var doc = NewDocument();
        var group = Add(doc.Root, new GroupNode { Id = Guid.NewGuid(), Name = "group", Children = [] });
        Add(group, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "Hi", FontSize = 10 });
        Add(group, Rect(100, 100), index: 1);

        var groupBounds = _engine.GetBounds(doc).Single(b => b.Id == group.Id);

        Assert.Equal(false, groupBounds.Measured);
        Assert.Equal(new BoundingBox(0, 0, 100, 100), groupBounds.Local);
        Assert.Contains(groupBounds.Diagnostics, d => d.Contains("estimated", StringComparison.Ordinal));
    }

    [Fact]
    public void GetBounds_AllNodes_ReturnsDocumentOrderIncludingRoot()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10), index: 0);
        var b = Add(doc.Root, Rect(10, 10), index: 1);

        var all = _engine.GetBounds(doc);

        Assert.Equal([doc.Root.Id, a.Id, b.Id], all.Select(x => x.Id));
        Assert.Equal("frame", all[0].Type);
    }

    [Fact]
    public void GetBounds_UnknownNode_Throws()
    {
        var doc = NewDocument();

        var ex = Assert.Throws<DocumentEngineException>(() => _engine.GetBounds(doc, Guid.NewGuid()));

        Assert.Equal(EngineErrors.NodeNotFound, ex.Code);
    }

    [Fact]
    public void GetBounds_DoesNotChangeRevision()
    {
        var doc = NewDocument();
        Add(doc.Root, Rect(10, 10));

        _engine.GetBounds(doc);

        Assert.Equal(1, doc.Revn);
    }

    // ---- Align ----

    [Theory]
    [InlineData(AlignMode.Left, 10, 10, 10, 100)]
    [InlineData(AlignMode.CenterX, 75, 10, 105, 100)]
    [InlineData(AlignMode.Right, 140, 10, 200, 100)]
    [InlineData(AlignMode.Top, 10, 10, 200, 10)]
    [InlineData(AlignMode.CenterY, 10, 40, 200, 55)]
    [InlineData(AlignMode.Bottom, 10, 70, 200, 100)]
    public void AlignNodes_SelectionReference_EachMode(AlignMode mode, double ax, double ay, double bx, double by)
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(100, 50, x: 10, y: 10), index: 0);
        var b = Add(doc.Root, Rect(40, 20, x: 200, y: 100), index: 1);

        var result = _engine.AlignNodes(doc, new AlignRequest { NodeIds = [a.Id, b.Id], Mode = mode });

        Assert.Equal(AlignReference.Selection, result.RelativeTo);
        Assert.Equal(new BoundingBox(10, 10, 230, 110), result.Reference);
        Assert.Equal((ax, ay), (a.Transform!.X, a.Transform.Y));
        Assert.Equal((bx, by), (b.Transform!.X, b.Transform.Y));
        Assert.Equal(2, doc.Revn);
    }

    [Fact]
    public void AlignNodes_SingleNode_DefaultsToParentReference()
    {
        var doc = NewDocument(800, 600);
        var rect = Add(doc.Root, Rect(100, 50, x: 10, y: 10));

        var result = _engine.AlignNodes(doc, new AlignRequest { NodeIds = [rect.Id], Mode = AlignMode.CenterX });

        Assert.Equal(AlignReference.Parent, result.RelativeTo);
        Assert.Equal(350, rect.Transform!.X);
        Assert.Equal(10, rect.Transform.Y);
        var placement = Assert.Single(result.Nodes);
        Assert.Equal((10d, 350d), (placement.PreviousX, placement.X));
    }

    [Fact]
    public void AlignNodes_CanvasReference_UsesWorldSpaceInsideTranslatedGroup()
    {
        var doc = NewDocument(800, 600);
        var group = Add(doc.Root, new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "group",
            Transform = new Transform { X = 100 },
            Children = []
        });
        var rect = Add(group, Rect(100, 50));

        _engine.AlignNodes(doc, new AlignRequest { NodeIds = [rect.Id], Mode = AlignMode.CenterX, RelativeTo = AlignReference.Canvas });

        Assert.Equal(250, rect.Transform!.X);
        Assert.Equal(new BoundingBox(350, 0, 100, 50), Single(_engine.GetBounds(doc, rect.Id)).World);
    }

    [Fact]
    public void AlignNodes_RotatedParent_ConvertsWorldDeltaIntoParentSpace()
    {
        var doc = NewDocument();
        var group = Add(doc.Root, new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "rotated",
            Transform = new Transform { X = 100, Y = 100, Rotation = 90 },
            Children = []
        });
        var rect = Add(group, Rect(10, 10));
        Assert.Equal(new BoundingBox(90, 100, 10, 10), Single(_engine.GetBounds(doc, rect.Id)).World);

        _engine.AlignNodes(doc, new AlignRequest { NodeIds = [rect.Id], Mode = AlignMode.Left, RelativeTo = AlignReference.Canvas });

        // A world delta of (-90, 0) maps to a local delta of (0, +90) under the parent's 90-degree rotation.
        Assert.Equal(new BoundingBox(0, 100, 10, 10), Single(_engine.GetBounds(doc, rect.Id)).World);
        Assert.Equal(90, rect.Transform!.Y);
        Assert.Equal(0, rect.Transform.X);
    }

    [Fact]
    public void AlignNodes_MixedParents_FailsUnlessCanvas()
    {
        var doc = NewDocument();
        var group = Add(doc.Root, new GroupNode { Id = Guid.NewGuid(), Name = "group", Children = [] }, index: 0);
        var inGroup = Add(group, Rect(10, 10, x: 50));
        var inRoot = Add(doc.Root, Rect(10, 10, x: 200), index: 1);

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.AlignNodes(doc, new AlignRequest { NodeIds = [inGroup.Id, inRoot.Id], Mode = AlignMode.Left }));
        Assert.Equal(EngineErrors.MixedParents, ex.Code);
        Assert.Equal(1, doc.Revn);

        _engine.AlignNodes(doc, new AlignRequest { NodeIds = [inGroup.Id, inRoot.Id], Mode = AlignMode.Left, RelativeTo = AlignReference.Canvas });

        Assert.Equal(0, inGroup.Transform!.X);
        Assert.Equal(0, inRoot.Transform!.X);
    }

    [Fact]
    public void AlignNodes_RootOrMissingNode_Fails()
    {
        var doc = NewDocument();
        Add(doc.Root, Rect(10, 10));

        Assert.Equal(EngineErrors.NodeNotFound, Assert.Throws<DocumentEngineException>(() =>
            _engine.AlignNodes(doc, new AlignRequest { NodeIds = [Guid.NewGuid()], Mode = AlignMode.Left })).Code);
        Assert.Equal(EngineErrors.InvalidArguments, Assert.Throws<DocumentEngineException>(() =>
            _engine.AlignNodes(doc, new AlignRequest { NodeIds = [doc.Root.Id], Mode = AlignMode.Left })).Code);
        Assert.Equal(EngineErrors.InvalidArguments, Assert.Throws<DocumentEngineException>(() =>
            _engine.AlignNodes(doc, new AlignRequest { NodeIds = [], Mode = AlignMode.Left })).Code);
    }

    [Fact]
    public void AlignNodes_AlreadyAligned_DoesNotCreateTransforms()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, new RectangleNode { Id = Guid.NewGuid(), Name = "a", Width = 10, Height = 10 }, index: 0);
        var b = Add(doc.Root, new RectangleNode { Id = Guid.NewGuid(), Name = "b", Width = 30, Height = 30 }, index: 1);

        _engine.AlignNodes(doc, new AlignRequest { NodeIds = [a.Id, b.Id], Mode = AlignMode.Left });

        Assert.Null(a.Transform);
        Assert.Null(b.Transform);
    }

    // ---- Distribute ----

    [Fact]
    public void DistributeNodes_GapMode_KeepsFirstAndLastFixed()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, x: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, x: 15), index: 1);
        var c = Add(doc.Root, Rect(10, 10, x: 100), index: 2);

        var result = _engine.DistributeNodes(doc, new DistributeRequest { NodeIds = [c.Id, a.Id, b.Id], Axis = DistributeAxis.Horizontal });

        Assert.Equal(40, result.Gap);
        Assert.Equal(0, a.Transform!.X);
        Assert.Equal(50, b.Transform!.X);
        Assert.Equal(100, c.Transform!.X);
        Assert.Equal(2, doc.Revn);
    }

    [Fact]
    public void DistributeNodes_CentersMode_SpacesCentersEvenly()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, x: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, x: 15), index: 1);
        var c = Add(doc.Root, Rect(10, 10, x: 100), index: 2);

        var result = _engine.DistributeNodes(doc, new DistributeRequest { NodeIds = [a.Id, b.Id, c.Id], Axis = DistributeAxis.Horizontal, Mode = DistributeMode.Centers });

        Assert.Equal(50, result.Gap);
        Assert.Equal(50, b.Transform!.X);
        Assert.Equal(100, c.Transform!.X);
    }

    [Fact]
    public void DistributeNodes_ExplicitGap_PacksFromFirst()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, y: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, y: 15), index: 1);
        var c = Add(doc.Root, Rect(10, 10, y: 100), index: 2);

        _engine.DistributeNodes(doc, new DistributeRequest { NodeIds = [a.Id, b.Id, c.Id], Axis = DistributeAxis.Vertical, Gap = 5 });

        Assert.Equal(0, a.Transform!.Y);
        Assert.Equal(15, b.Transform!.Y);
        Assert.Equal(30, c.Transform!.Y);
    }

    [Fact]
    public void DistributeNodes_TwoNodesWithGap_Allowed()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, x: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, x: 90), index: 1);

        _engine.DistributeNodes(doc, new DistributeRequest { NodeIds = [a.Id, b.Id], Axis = DistributeAxis.Horizontal, Gap = 20 });

        Assert.Equal(30, b.Transform!.X);
    }

    [Fact]
    public void DistributeNodes_TwoNodesWithoutGap_Fails()
    {
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, x: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, x: 90), index: 1);

        var ex = Assert.Throws<DocumentEngineException>(() =>
            _engine.DistributeNodes(doc, new DistributeRequest { NodeIds = [a.Id, b.Id], Axis = DistributeAxis.Horizontal }));

        Assert.Equal(EngineErrors.InvalidArguments, ex.Code);
        Assert.Contains("three", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, doc.Revn);
    }

    // ---- Fit ----

    [Fact]
    public void FitFrame_WithPadding_ResizesFrameAndKeepsChildrenInPlace()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, Frame(50, 50, x: 100, y: 100, clip: true));
        var a = Add(frame, Rect(30, 30, x: -10, y: 20), index: 0);
        var b = Add(frame, Rect(10, 10, x: 40, y: 40), index: 1);
        var beforeA = new BoundingBox(90, 120, 30, 30);
        var beforeB = new BoundingBox(140, 140, 10, 10);

        var result = _engine.FitFrame(doc, new FitRequest { FrameId = frame.Id, Padding = 5 });

        Assert.Equal(new BoundingBox(100, 100, 50, 50), result.Previous);
        Assert.Equal(new BoundingBox(85, 115, 70, 40), result.Frame);
        Assert.Equal((85d, 115d), (frame.Transform!.X, frame.Transform.Y));
        Assert.Equal((70d, 40d), (frame.Width, frame.Height));
        Assert.Equal((5d, 5d), (a.Transform!.X, a.Transform.Y));
        Assert.Equal((55d, 25d), (b.Transform!.X, b.Transform.Y));
        var after = _engine.GetBounds(doc);
        Assert.Equal(beforeA, after.Single(x => x.Id == a.Id).World);
        Assert.Equal(beforeB, after.Single(x => x.Id == b.Id).World);
        Assert.Equal(2, doc.Revn);
    }

    [Fact]
    public void FitFrame_RotatedFrame_KeepsChildWorldPosition()
    {
        var doc = NewDocument();
        var frame = Add(doc.Root, Frame(100, 100, x: 200, y: 200, clip: false));
        frame.Transform!.Rotation = 90;
        var child = Add(frame, Rect(20, 20, x: 10, y: 10));
        var before = Single(_engine.GetBounds(doc, child.Id)).World;

        _engine.FitFrame(doc, new FitRequest { FrameId = frame.Id });

        Assert.Equal((190d, 210d), (frame.Transform.X, frame.Transform.Y));
        Assert.Equal((20d, 20d), (frame.Width, frame.Height));
        Assert.Equal((0d, 0d), (child.Transform!.X, child.Transform.Y));
        Assert.Equal(before, Single(_engine.GetBounds(doc, child.Id)).World);
    }

    [Fact]
    public void FitFrame_InvalidTargets_Fail()
    {
        var doc = NewDocument();
        var rect = Add(doc.Root, Rect(10, 10), index: 0);
        var empty = Add(doc.Root, Frame(10, 10), index: 1);

        Assert.Equal(EngineErrors.InvalidArguments, Assert.Throws<DocumentEngineException>(() =>
            _engine.FitFrame(doc, new FitRequest { FrameId = doc.Root.Id })).Code);
        Assert.Equal(EngineErrors.InvalidArguments, Assert.Throws<DocumentEngineException>(() =>
            _engine.FitFrame(doc, new FitRequest { FrameId = rect.Id })).Code);
        Assert.Equal(EngineErrors.InvalidArguments, Assert.Throws<DocumentEngineException>(() =>
            _engine.FitFrame(doc, new FitRequest { FrameId = empty.Id })).Code);
        Assert.Equal(EngineErrors.InvalidProperty, Assert.Throws<DocumentEngineException>(() =>
            _engine.FitFrame(doc, new FitRequest { FrameId = empty.Id, Padding = -1 })).Code);
        Assert.Equal(1, doc.Revn);
    }

    // ---- Persistence and revisions ----

    [Fact]
    public void LayoutMutations_RespectExpectedRevisionOnSave()
    {
        var store = new InMemoryStore();
        var engine = new DocumentEngine(store);
        var doc = NewDocument();
        var a = Add(doc.Root, Rect(10, 10, x: 0), index: 0);
        var b = Add(doc.Root, Rect(10, 10, x: 90), index: 1);
        store.Save(new DocumentStoreEntry(doc, SourcePath: null));

        engine.AlignNodes(doc, new AlignRequest { NodeIds = [a.Id, b.Id], Mode = AlignMode.Left });

        var stale = Assert.Throws<DocumentEngineException>(() => engine.SaveDocument(doc, expectedRevn: 99));
        Assert.Equal(EngineErrors.RevisionConflict, stale.Code);
        engine.SaveDocument(doc, expectedRevn: 1);
        Assert.Equal(2, store.Load(doc.Id)!.Document.Revn);
    }

    // ---- Parsing helpers used by both surfaces ----

    [Fact]
    public void NodeIdList_AcceptsCommaSeparatedAndJsonArray()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.True(NodeIdList.TryParse($"{a}, {b}", out var fromCsv, out _));
        Assert.True(NodeIdList.TryParse($"[\"{a}\",\"{b}\"]", out var fromJson, out _));
        Assert.Equal([a, b], fromCsv);
        Assert.Equal([a, b], fromJson);
    }

    [Theory]
    [InlineData("", "At least one node ID is required")]
    [InlineData("not-a-guid", "Invalid node ID: not-a-guid")]
    [InlineData("[1, 2]", "Node ID list must be a JSON array of strings")]
    [InlineData("[", "Node ID list is not valid JSON")]
    public void NodeIdList_RejectsMalformedInput(string input, string expectedError)
    {
        Assert.False(NodeIdList.TryParse(input, out _, out var error));
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void NodeIdList_RejectsDuplicates()
    {
        var id = Guid.NewGuid();

        Assert.False(NodeIdList.TryParse($"{id},{id}", out _, out var error));
        Assert.StartsWith("Duplicate node ID", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("centerX", AlignMode.CenterX)]
    [InlineData("center-x", AlignMode.CenterX)]
    [InlineData("LEFT", AlignMode.Left)]
    [InlineData("bottom", AlignMode.Bottom)]
    public void LayoutOptions_ParseAlignModes(string input, AlignMode expected)
    {
        Assert.True(LayoutOptions.TryParseAlignMode(input, out var mode));
        Assert.Equal(expected, mode);
        Assert.False(LayoutOptions.TryParseAlignMode("diagonal", out _));
    }

    [Fact]
    public void LayoutOptions_NamesRoundTrip()
    {
        Assert.Equal("centerX", LayoutOptions.Name(AlignMode.CenterX));
        Assert.Equal("selection", LayoutOptions.Name(AlignReference.Selection));
        Assert.Equal("horizontal", LayoutOptions.Name(DistributeAxis.Horizontal));
        Assert.Equal("centers", LayoutOptions.Name(DistributeMode.Centers));
        Assert.True(LayoutOptions.TryParseDistributeAxis("y", out var axis));
        Assert.Equal(DistributeAxis.Vertical, axis);
    }

    // ---- Helpers ----

    // ---- Bounds: multi-line and aligned text (estimator: 0.6 × fontSize per glyph, ascent = fontSize, descent = 0.2 × fontSize) ----

    [Fact]
    public void GetBounds_WrappedText_ReportsBlockOfMaxLineWidthAndStackedLines()
    {
        var doc = NewDocument();
        // 10 px glyphs at fontSize 10 wrap at 55 px: "hello" | "world" | "again".
        var text = Add(doc.Root, new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Text = "hello world again",
            FontSize = 10,
            MaxWidth = 55,
            Transform = new Transform { X = 100, Y = 50 }
        });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        // Width = widest line (5 glyphs × 6 px); height = 2 pitches (12 px each) + ascent + descent.
        Assert.Equal(new BoundingBox(0, 0, 30, 2 * 12 + 12), bounds.Local);
        Assert.Equal(new BoundingBox(100, 50, 30, 36), bounds.World);
    }

    [Fact]
    public void GetBounds_ExplicitNewline_BreaksWithoutMaxWidth()
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "ab\ncdef", FontSize = 10 });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        Assert.Equal(new BoundingBox(0, 0, 24, 24), bounds.Local);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("start", 0)]
    [InlineData("center", -15)]
    [InlineData("end", -30)]
    public void GetBounds_TextAlign_ShiftsBlockRelativeToAnchor(string? align, double expectedLeft)
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Text = "hello\nhi",
            FontSize = 10,
            TextAlign = align,
            Transform = new Transform { X = 200, Y = 0 }
        });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        Assert.Equal(expectedLeft, bounds.Local!.Value.X);
        Assert.Equal(30, bounds.Local.Value.Width);
        Assert.Equal(200 + expectedLeft, bounds.World!.Value.X);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("top", 0)]
    [InlineData("baseline", -10)]
    [InlineData("middle", -12)]
    [InlineData("bottom", -24)]
    public void GetBounds_VerticalAlign_ShiftsBlockByLinesTimesPitch(string? align, double expectedShift)
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "a\nb", FontSize = 10, VerticalAlign = align });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        // Two 12 px line boxes; the box top is fontSize - ascent = 0 before the shift.
        Assert.Equal(expectedShift, bounds.Local!.Value.Y, 9);
        Assert.Equal(24, bounds.Local.Value.Height, 9);
    }

    [Fact]
    public void GetBounds_LineHeight_NodeBeatsTokenBeatsDefault()
    {
        var doc = NewDocument();
        doc.Tokens = new DesignTokens
        {
            Typography = new Dictionary<string, TypographyToken> { ["body"] = new() { LineHeight = 2 } }
        };
        var fromDefault = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "default", Text = "a\nb", FontSize = 10 }, 0);
        var fromToken = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "token", Text = "a\nb", FontSize = 10, TypographyTokenRef = "body" }, 1);
        var fromNode = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "node", Text = "a\nb", FontSize = 10, TypographyTokenRef = "body", LineHeight = 3 }, 2);

        var all = _engine.GetBounds(doc);

        Assert.Equal(12 + 12, all.Single(b => b.Id == fromDefault.Id).Local!.Value.Height, 9);
        Assert.Equal(20 + 12, all.Single(b => b.Id == fromToken.Id).Local!.Value.Height, 9);
        Assert.Equal(30 + 12, all.Single(b => b.Id == fromNode.Id).Local!.Value.Height, 9);
    }

    [Fact]
    public void GetBounds_LetterSpacing_WidensEachLine()
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "abcd", FontSize = 10, LetterSpacing = 2 });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        Assert.Equal(4 * 6 + 4 * 2, bounds.Local!.Value.Width);
    }

    [Fact]
    public void GetBounds_SingleLineWithoutLayoutFields_IsUnchanged()
    {
        var doc = NewDocument();
        var text = Add(doc.Root, new TextNode { Id = Guid.NewGuid(), Name = "text", Text = "Hello", FontSize = 20 });

        var bounds = Single(_engine.GetBounds(doc, text.Id));

        Assert.Equal(new BoundingBox(0, 0, 60, 24), bounds.Local);
    }

    [Fact]
    public void AlignNodes_CenterX_WorksOnWrappedCenteredText()
    {
        var doc = NewDocument();
        var rect = Add(doc.Root, Rect(200, 40, x: 100, y: 0), 0);
        var text = Add(doc.Root, new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Text = "hello world again",
            FontSize = 10,
            MaxWidth = 55,
            TextAlign = "center",
            Transform = new Transform { X = 20, Y = 100 }
        }, 1);

        var result = _engine.AlignNodes(doc, new AlignRequest
        {
            NodeIds = [rect.Id, text.Id],
            Mode = AlignMode.CenterX,
            RelativeTo = AlignReference.Selection
        });

        Assert.NotEmpty(result.Nodes);
        var world = Single(_engine.GetBounds(doc, text.Id)).World!.Value;
        var rectWorld = Single(_engine.GetBounds(doc, rect.Id)).World!.Value;
        Assert.Equal(rectWorld.CenterX, world.CenterX, 6);
        // RelativeTo.Selection centres on the union of both nodes' world bounds: the centred
        // text spans [5, 35] (block width 30 centred on x=20) and the rect spans [100, 300], so
        // the selection centre is (5 + 300) / 2 = 152.5. The centred text's anchor lands there.
        Assert.Equal(152.5, text.Transform!.X, 6);
    }

    private static NodeBounds Single(IReadOnlyList<NodeBounds> bounds) => Assert.Single(bounds);

    private static Document NewDocument(double width = 800, double height = 600) => new()
    {
        Schema = Document.SchemaUrl,
        Id = Guid.NewGuid(),
        Name = "layout",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = width, Height = height },
        Root = new FrameNode
        {
            Id = Guid.NewGuid(),
            Name = "root",
            Width = width,
            Height = height,
            Children = []
        }
    };

    private static T Add<T>(IContainerNode parent, T node, int index = 0) where T : Node
    {
        node.Order = $"a{index}";
        parent.Children.Add(node);
        return node;
    }

    private static RectangleNode Rect(double width, double height, double x = 0, double y = 0, double rotation = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "rect",
        Width = width,
        Height = height,
        Transform = new Transform { X = x, Y = y, Rotation = rotation }
    };

    private static FrameNode Frame(double width, double height, double x = 0, double y = 0, bool clip = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = "frame",
        Width = width,
        Height = height,
        ClipContent = clip,
        Transform = new Transform { X = x, Y = y },
        Children = []
    };

    private sealed class InMemoryStore : IDocumentStore
    {
        private readonly Dictionary<Guid, string> _documents = [];

        public DocumentStoreEntry? Load(Guid id) =>
            _documents.TryGetValue(id, out var json)
                ? new DocumentStoreEntry(Core.Serialization.DocumentSerializer.Deserialize(json)!, SourcePath: null)
                : null;

        public void Save(DocumentStoreEntry entry, long? expectedRevn = null)
        {
            var document = entry.Document;
            if (expectedRevn.HasValue)
            {
                var existing = Load(document.Id)?.Document;
                if (existing is not null && existing.Revn != expectedRevn.Value)
                {
                    throw new DocumentEngineException(EngineErrors.RevisionConflict,
                        $"Revision conflict: expected {expectedRevn.Value}, got {existing.Revn}");
                }
            }

            _documents[document.Id] = Core.Serialization.DocumentSerializer.Serialize(document);
        }

        public void Create(Document document)
        {
            if (!_documents.TryAdd(document.Id, Core.Serialization.DocumentSerializer.Serialize(document)))
            {
                throw new DocumentEngineException(DocumentStoreErrors.DocumentExists, "Document exists");
            }
        }

        public void Delete(Guid id) => _documents.Remove(id);

        public IEnumerable<DocumentInfo> List() => [];
    }
}
