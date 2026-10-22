using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using FTPlus.AgentGraphicDesign.Core.Tokens;

namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// Computes local and world bounds for one document. Transform order matches
/// <c>SkiaRenderer.ApplyTransform</c>: a local point is scaled, then rotated, then
/// translated by its own node, and then by each ancestor in turn. Frames with
/// <c>clipContent</c> intersect their descendants' world boxes with their own world
/// box; that clip is the frame's axis-aligned world box, so a rotated clipping frame
/// clips conservatively.
/// </summary>
internal sealed class BoundsCalculator
{
    public const int MaxNodeCount = 10_000;
    public const int MaxDepth = 128;
    public const int OutputDecimals = 4;

    private readonly Document _document;
    private readonly DocumentIndex _index;
    private readonly ITextMeasurer _measurer;
    private readonly TokenService _tokens;
    private readonly PathOperationsService _pathOps;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<Guid, OwnGeometry> _own = [];
    private readonly Dictionary<Guid, Affine> _worldMatrices = [];
    private readonly Dictionary<Guid, Clip> _clips = [];

    public BoundsCalculator(
        Document document,
        DocumentIndex index,
        ITextMeasurer measurer,
        TokenService tokens,
        PathOperationsService pathOps,
        CancellationToken cancellationToken)
    {
        _document = document;
        _index = index;
        _measurer = measurer;
        _tokens = tokens;
        _pathOps = pathOps;
        _cancellationToken = cancellationToken;

        if (index.Count > MaxNodeCount)
        {
            throw new DocumentEngineException(EngineErrors.ResourceLimit,
                $"Bounds computation supports at most {MaxNodeCount} nodes; the document has {index.Count}");
        }

        WalkMatrices(document.Root, Affine.Identity, Clip.None, 0);
    }

    public DocumentIndex Index => _index;

    /// <summary>Bounds for one node, or every node in document order when <paramref name="nodeId"/> is null.</summary>
    public IReadOnlyList<NodeBounds> Compute(Guid? nodeId)
    {
        if (nodeId is { } id)
        {
            var node = _index.Find(id)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {id}");
            return [Describe(node)];
        }

        var list = new List<NodeBounds>(_index.Count);
        Collect(_document.Root, list);
        return list;
    }

    /// <summary>World box after all transforms, ignoring clipping. Null when the node has no geometry.</summary>
    public BoundingBox? WorldBounds(Node node) =>
        Aabb(node, _worldMatrices[node.Id], Clip.None, 0).Unclipped;

    public Affine WorldMatrix(Node node) => _worldMatrices[node.Id];

    /// <summary>
    /// The box a container stands for when nodes align to their parent: a frame's own
    /// rectangle in world space, or the union of a group's children.
    /// </summary>
    public BoundingBox? ReferenceBounds(IContainerNode parent) => parent switch
    {
        FrameNode frame => _worldMatrices[frame.Id].TransformBox(SizeBox(frame.Width, frame.Height)),
        Node node => WorldBounds(node),
        _ => null
    };

    /// <summary>Union of the children's boxes in the container's own coordinate space.</summary>
    public BoundingBox? ChildrenLocalBounds(IContainerNode container)
    {
        BoundingBox? union = null;
        foreach (var child in container.Children)
        {
            var result = Aabb(child, Affine.FromTransform(child.Transform), Clip.None, 1);
            union = BoundingBox.Union(union, result.Unclipped);
        }
        return union;
    }

    private void Collect(Node node, List<NodeBounds> list)
    {
        list.Add(Describe(node));
        if (node is IContainerNode container)
        {
            foreach (var child in container.Children.OrderBy(child => child.Order, StringComparer.Ordinal))
                Collect(child, list);
        }
    }

    private NodeBounds Describe(Node node)
    {
        var own = OwnGeometryOf(node);
        var local = Aabb(node, Affine.Identity, Clip.None, 0);
        var world = Aabb(node, _worldMatrices[node.Id], _clips[node.Id], 0);
        var diagnostics = new List<string>(own.Diagnostics);
        var measured = own.Measured;

        if (node is IContainerNode)
        {
            if (local.Unclipped is null)
                diagnostics.Add("Container has no children with geometry");
            if (world.EstimatedTexts > 0)
            {
                measured = false;
                diagnostics.Add($"{world.EstimatedTexts} text node(s) contribute estimated extents");
            }
        }

        var clipped = world.Unclipped is not null && world.Clipped != world.Unclipped;
        if (world.Unclipped is not null && world.Clipped is null)
            diagnostics.Add("World bounds are fully clipped by an ancestor frame");
        else if (clipped)
            diagnostics.Add("World bounds are clipped by an ancestor frame");

        return new NodeBounds
        {
            Id = node.Id,
            Type = DocumentEngine.GetNodeTypeName(node),
            Name = node.Name,
            Visible = node.Visible,
            Local = local.Unclipped?.Round(OutputDecimals),
            World = world.Clipped?.Round(OutputDecimals),
            Measured = measured,
            Clipped = clipped,
            Diagnostics = diagnostics
        };
    }

    private void WalkMatrices(Node node, Affine parentMatrix, Clip clip, int depth)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (depth > MaxDepth)
        {
            throw new DocumentEngineException(EngineErrors.ResourceLimit,
                $"Bounds computation supports a tree depth of at most {MaxDepth}");
        }

        var matrix = parentMatrix.Multiply(Affine.FromTransform(node.Transform));
        if (!IsFinite(matrix))
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Node {node.Id} has a non-finite transform");
        }

        _worldMatrices[node.Id] = matrix;
        _clips[node.Id] = clip;

        if (node is not IContainerNode container)
            return;

        var childClip = node is FrameNode { ClipContent: true } frame
            ? clip.Intersect(matrix.TransformBox(SizeBox(frame.Width, frame.Height)))
            : clip;
        foreach (var child in container.Children)
            WalkMatrices(child, matrix, childClip, depth + 1);
    }

    /// <summary>
    /// Tight axis-aligned box of <paramref name="node"/> in the space that
    /// <paramref name="matrix"/> maps its local coordinates into. Containers union
    /// their descendants through the composed matrices; a clipping frame contributes
    /// only its own rectangle because its children never extend beyond it.
    /// </summary>
    private AabbResult Aabb(Node node, Affine matrix, Clip clip, int depth)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var own = OwnGeometryOf(node);
        var estimated = own.Measured == false ? 1 : 0;
        BoundingBox? ownBox = own.Rect is { } rect ? matrix.TransformBox(rect) : null;

        if (node is not IContainerNode container || node is FrameNode { ClipContent: true })
            return new AabbResult(ownBox, clip.Apply(ownBox), estimated);

        var unclipped = ownBox;
        var clipped = clip.Apply(ownBox);
        foreach (var child in container.Children)
        {
            var result = Aabb(child, matrix.Multiply(Affine.FromTransform(child.Transform)), clip, depth + 1);
            unclipped = BoundingBox.Union(unclipped, result.Unclipped);
            clipped = BoundingBox.Union(clipped, result.Clipped);
            estimated += result.EstimatedTexts;
        }

        return new AabbResult(unclipped, clipped, estimated);
    }

    private OwnGeometry OwnGeometryOf(Node node)
    {
        if (_own.TryGetValue(node.Id, out var cached))
            return cached;

        var geometry = ComputeOwnGeometry(node);
        if (geometry.Rect is { } rect && !rect.IsFinite)
            geometry = new OwnGeometry(null, [.. geometry.Diagnostics, "Geometry is non-finite"], geometry.Measured);
        _own[node.Id] = geometry;
        return geometry;
    }

    private OwnGeometry ComputeOwnGeometry(Node node) => node switch
    {
        RectangleNode rectangle => new OwnGeometry(SizeBox(rectangle.Width, rectangle.Height), [], null),
        EllipseNode ellipse => new OwnGeometry(SizeBox(ellipse.Width, ellipse.Height), [], null),
        FrameNode frame => new OwnGeometry(SizeBox(frame.Width, frame.Height), [], null),
        ImageNode image => new OwnGeometry(SizeBox(image.Width, image.Height), [], null),
        LineNode line => new OwnGeometry(
            BoundingBox.FromEdges(
                Math.Min(line.X1, line.X2),
                Math.Min(line.Y1, line.Y2),
                Math.Max(line.X1, line.X2),
                Math.Max(line.Y1, line.Y2)),
            [],
            null),
        PolygonNode polygon => PolygonGeometry(polygon),
        PathNode path => PathGeometry(path),
        TextNode text => TextGeometry(text),
        GroupNode => new OwnGeometry(null, [], null),
        UnknownNode => new OwnGeometry(null, ["Unknown node type has no geometry"], null),
        _ => new OwnGeometry(null, [$"Node type '{DocumentEngine.GetNodeTypeName(node)}' has no geometry"], null)
    };

    private static OwnGeometry PolygonGeometry(PolygonNode polygon)
    {
        if (polygon.Points.Count == 0)
            return new OwnGeometry(null, ["Polygon has no points"], null);

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var point in polygon.Points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        return new OwnGeometry(BoundingBox.FromEdges(minX, minY, maxX, maxY), [], null);
    }

    private OwnGeometry PathGeometry(PathNode path)
    {
        if (string.IsNullOrWhiteSpace(path.D))
            return new OwnGeometry(null, ["Path data is empty"], null);

        var result = _pathOps.GetBounds(path.D, _cancellationToken);
        return result.Success && result.Bounds is { } bounds
            ? new OwnGeometry(bounds, [], null)
            : new OwnGeometry(null, [$"Path bounds are unavailable: {result.Error}"], null);
    }

    private OwnGeometry TextGeometry(TextNode text)
    {
        var diagnostics = new List<string>();
        TypographyToken? token = null;
        if (text.TypographyTokenRef is { } reference)
        {
            var resolved = _tokens.ResolveTypographyRefWithDiagnostics(_document, reference);
            if (resolved is { Success: true, Value: { } typography })
                token = typography;
            else
                diagnostics.Add($"Typography token reference '{reference}' did not resolve: {resolved.ErrorMessage}");
        }

        var style = TextLayout.Resolve(text, token);
        if (!double.IsFinite(style.FontSize) || style.FontSize <= 0)
        {
            diagnostics.Add("Font size must be positive and finite");
            return new OwnGeometry(null, diagnostics, null);
        }

        var measured = true;
        TextMeasurement Measure(string line)
        {
            var measurement = _measurer.MeasureSafely(style.ToMeasurementRequest(line), _cancellationToken);
            measured &= measurement.Measured;
            if (measurement.Diagnostic is { } diagnostic && !diagnostics.Contains(diagnostic))
                diagnostics.Add(diagnostic);
            return measurement;
        }

        var lines = TextLayout.BreakLines(text.Text, style.MaxWidth, line => Measure(line).Width);
        var maxWidth = 0d;
        TextMeasurement? first = null;
        foreach (var line in lines)
        {
            var measurement = Measure(line);
            first ??= measurement;
            maxWidth = Math.Max(maxWidth, measurement.Width);
        }

        // Line i sits with its baseline at fontSize + i × pitch; the box spans from the
        // first line's ascent to the last line's descent, shifted by the vertical anchor.
        var metrics = first!;
        var pitch = TextLayout.LinePitch(style.FontSize, style.LineHeight);
        var top = style.FontSize - metrics.Ascent + TextLayout.VerticalOffset(style.VerticalAlign, lines.Count, style.FontSize, style.LineHeight);
        var height = (lines.Count - 1) * pitch + metrics.LineHeight;
        var left = TextLayout.AlignOffset(style.TextAlign, maxWidth);
        var rect = new BoundingBox(left, top, maxWidth, height);
        return new OwnGeometry(rect, diagnostics, measured);
    }

    private static BoundingBox SizeBox(double width, double height) =>
        BoundingBox.FromEdges(Math.Min(0, width), Math.Min(0, height), Math.Max(0, width), Math.Max(0, height));

    private static bool IsFinite(Affine matrix) =>
        double.IsFinite(matrix.A) && double.IsFinite(matrix.B) && double.IsFinite(matrix.C) &&
        double.IsFinite(matrix.D) && double.IsFinite(matrix.Tx) && double.IsFinite(matrix.Ty);

    private sealed record OwnGeometry(BoundingBox? Rect, IReadOnlyList<string> Diagnostics, bool? Measured);

    private readonly record struct AabbResult(BoundingBox? Unclipped, BoundingBox? Clipped, int EstimatedTexts);

    /// <summary>The accumulated clip of every clipping-frame ancestor, in world space.</summary>
    private readonly record struct Clip(BoundingBox? Box, bool Empty)
    {
        public static readonly Clip None = new(null, false);

        public Clip Intersect(BoundingBox box)
        {
            if (Empty)
                return this;
            if (Box is null)
                return new Clip(box, false);
            var overlap = BoundingBox.Intersect(Box.Value, box);
            return overlap is null ? new Clip(null, true) : new Clip(overlap, false);
        }

        public BoundingBox? Apply(BoundingBox? box)
        {
            if (box is null || Empty)
                return null;
            return Box is null ? box : BoundingBox.Intersect(Box.Value, box.Value);
        }
    }
}
