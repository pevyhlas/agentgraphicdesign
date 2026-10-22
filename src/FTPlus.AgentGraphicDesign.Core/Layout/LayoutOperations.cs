using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// Align, distribute, and fit. Every operation works on unclipped world bounds so a
/// node that currently overflows a clipping frame is still brought into place, and
/// only changes <c>transform.x</c>/<c>transform.y</c> (plus frame size for fit).
/// World-space offsets are converted into each node's parent space through the
/// inverse of the parent's linear transform.
/// </summary>
internal static class LayoutOperations
{
    private const int Decimals = BoundsCalculator.OutputDecimals;

    public static AlignResult Align(Document document, BoundsCalculator calculator, AlignRequest request)
    {
        if (!Enum.IsDefined(request.Mode))
            throw InvalidArguments($"Unknown align mode. Supported: {string.Join(", ", LayoutOptions.AlignModes)}");
        if (request.RelativeTo is { } explicitReference && !Enum.IsDefined(explicitReference))
            throw InvalidArguments($"Unknown align reference. Supported: {string.Join(", ", LayoutOptions.AlignReferences)}");

        var index = calculator.Index;
        var nodes = ResolveNodes(document, index, request.NodeIds, "align");
        var relativeTo = request.RelativeTo ?? (nodes.Count == 1 ? AlignReference.Parent : AlignReference.Selection);
        var sharedParent = relativeTo == AlignReference.Canvas ? null : RequireSharedParent(index, nodes);
        var boxes = nodes.Select(node => RequireWorldBounds(calculator, node, "align")).ToList();

        var reference = relativeTo switch
        {
            AlignReference.Canvas => new BoundingBox(0, 0, document.Canvas.Width, document.Canvas.Height),
            AlignReference.Selection => boxes.Aggregate(BoundingBox.Union),
            _ => calculator.ReferenceBounds(sharedParent!)
                ?? throw InvalidArguments("The parent has no geometry to align to")
        };

        var moves = new List<Move>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var box = boxes[i];
            var (dx, dy) = request.Mode switch
            {
                AlignMode.Left => (reference.X - box.X, 0d),
                AlignMode.CenterX => (reference.CenterX - box.CenterX, 0d),
                AlignMode.Right => (reference.Right - box.Right, 0d),
                AlignMode.Top => (0d, reference.Y - box.Y),
                AlignMode.CenterY => (0d, reference.CenterY - box.CenterY),
                _ => (0d, reference.Bottom - box.Bottom)
            };
            moves.Add(ToLocalMove(calculator, index, nodes[i], dx, dy));
        }

        var placements = moves.Select(Apply).ToList();
        DocumentSynchronization.IncrementRevision(document);
        return new AlignResult(request.Mode, relativeTo, reference.Round(Decimals), placements);
    }

    public static DistributeResult Distribute(Document document, BoundsCalculator calculator, DistributeRequest request)
    {
        if (!Enum.IsDefined(request.Axis))
            throw InvalidArguments($"Unknown distribute axis. Supported: {string.Join(", ", LayoutOptions.DistributeAxes)}");
        if (!Enum.IsDefined(request.Mode))
            throw InvalidArguments($"Unknown distribute mode. Supported: {string.Join(", ", LayoutOptions.DistributeModes)}");
        if (request.Gap is { } explicitGap && !double.IsFinite(explicitGap))
            throw InvalidProperty("Gap must be finite");

        var index = calculator.Index;
        var nodes = ResolveNodes(document, index, request.NodeIds, "distribute");
        if (nodes.Count < 3 && !(nodes.Count == 2 && request.Gap.HasValue))
            throw InvalidArguments("Distribute requires at least three nodes, or two nodes with an explicit gap");

        var horizontal = request.Axis == DistributeAxis.Horizontal;
        var items = nodes
            .Select((node, order) =>
            {
                var box = RequireWorldBounds(calculator, node, "distribute");
                return new Item(
                    node,
                    Start: horizontal ? box.X : box.Y,
                    Size: horizontal ? box.Width : box.Height,
                    Center: horizontal ? box.CenterX : box.CenterY,
                    order);
            })
            .ToList();

        items = request.Mode == DistributeMode.Gap
            ? items.OrderBy(item => item.Start).ThenBy(item => item.Center).ThenBy(item => item.Order).ToList()
            : items.OrderBy(item => item.Center).ThenBy(item => item.Start).ThenBy(item => item.Order).ToList();

        var first = items[0];
        var last = items[^1];
        double gap;
        var deltas = new double[items.Count];
        if (request.Mode == DistributeMode.Gap)
        {
            gap = request.Gap
                ?? (last.Start + last.Size - first.Start - items.Sum(item => item.Size)) / (items.Count - 1);
            var position = first.Start;
            for (var i = 0; i < items.Count; i++)
            {
                deltas[i] = position - items[i].Start;
                position += items[i].Size + gap;
            }
        }
        else
        {
            gap = request.Gap ?? (last.Center - first.Center) / (items.Count - 1);
            for (var i = 0; i < items.Count; i++)
                deltas[i] = first.Center + i * gap - items[i].Center;
        }

        if (!double.IsFinite(gap))
            throw InvalidProperty("Distribute produced a non-finite spacing");

        var moves = new List<Move>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            moves.Add(horizontal
                ? ToLocalMove(calculator, index, items[i].Node, deltas[i], 0)
                : ToLocalMove(calculator, index, items[i].Node, 0, deltas[i]));
        }

        var placements = moves.Select(Apply).ToList();
        DocumentSynchronization.IncrementRevision(document);
        return new DistributeResult(request.Axis, request.Mode, Math.Round(gap, Decimals), placements);
    }

    public static FitResult Fit(Document document, BoundsCalculator calculator, FitRequest request)
    {
        if (!double.IsFinite(request.Padding) || request.Padding < 0)
            throw InvalidProperty("Padding must be finite and non-negative");

        var node = calculator.Index.Find(request.FrameId)
            ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {request.FrameId}");
        if (node is not FrameNode frame)
            throw InvalidArguments($"Node {request.FrameId} is a {DocumentEngine.GetNodeTypeName(node)}, not a frame");
        if (ReferenceEquals(frame, document.Root))
            throw InvalidArguments("Cannot fit the root frame; its size mirrors the canvas");

        var union = calculator.ChildrenLocalBounds(frame)
            ?? throw InvalidArguments("Frame has no children with geometry to fit");

        var padding = request.Padding;
        var offsetX = union.X - padding;
        var offsetY = union.Y - padding;
        var width = Math.Round(union.Width + 2 * padding, Decimals);
        var height = Math.Round(union.Height + 2 * padding, Decimals);
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY) || !double.IsFinite(width) || !double.IsFinite(height))
            throw InvalidProperty("Fit produced a non-finite frame");

        // The frame origin moves by (offsetX, offsetY) in its own space; its translation
        // absorbs that shift through its own rotation/scale so children stay put visually.
        var (shiftX, shiftY) = Affine.FromTransform(frame.Transform).ApplyVector(offsetX, offsetY);
        var previous = new BoundingBox(frame.Transform?.X ?? 0, frame.Transform?.Y ?? 0, frame.Width, frame.Height);

        var framePlacement = Apply(new Move(frame, shiftX, shiftY));
        var children = frame.Children.Select(child => Apply(new Move(child, -offsetX, -offsetY))).ToList();
        frame.Width = width;
        frame.Height = height;

        DocumentSynchronization.IncrementRevision(document);
        return new FitResult(
            frame.Id,
            frame.Name,
            previous,
            new BoundingBox(framePlacement.X, framePlacement.Y, width, height),
            children);
    }

    private static List<Node> ResolveNodes(Document document, DocumentIndex index, IReadOnlyList<Guid> ids, string operation)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            throw InvalidArguments("At least one node ID is required");
        if (ids.Count > NodeIdList.MaxCount)
            throw InvalidArguments($"At most {NodeIdList.MaxCount} nodes can be {operation}ed at once");

        var seen = new HashSet<Guid>();
        var nodes = new List<Node>(ids.Count);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                throw InvalidArguments($"Duplicate node ID: {id}");
            var node = index.Find(id)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {id}");
            if (ReferenceEquals(node, document.Root))
                throw InvalidArguments($"Cannot {operation} the root node");
            nodes.Add(node);
        }
        return nodes;
    }

    private static IContainerNode RequireSharedParent(DocumentIndex index, IReadOnlyList<Node> nodes)
    {
        var parent = index.ParentOf(nodes[0].Id)
            ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node {nodes[0].Id} has no parent link");
        foreach (var node in nodes.Skip(1))
        {
            if (!ReferenceEquals(index.ParentOf(node.Id), parent))
            {
                throw new DocumentEngineException(EngineErrors.MixedParents,
                    "Nodes must share the same parent for parent and selection alignment; use relativeTo=canvas for nodes with different parents");
            }
        }
        return parent;
    }

    private static BoundingBox RequireWorldBounds(BoundsCalculator calculator, Node node, string operation) =>
        calculator.WorldBounds(node)
            ?? throw InvalidArguments($"Node {node.Id} has no geometry to {operation}");

    private static Move ToLocalMove(BoundsCalculator calculator, DocumentIndex index, Node node, double worldDx, double worldDy)
    {
        var parent = index.ParentOf(node.Id) as Node
            ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node {node.Id} has no parent link");
        if (!calculator.WorldMatrix(parent).TryInvertVector(worldDx, worldDy, out var localDx, out var localDy))
        {
            throw InvalidProperty(
                $"The parent of node {node.Id} has a non-invertible transform (zero scale), so a world offset cannot be applied");
        }
        return new Move(node, localDx, localDy);
    }

    private static NodePlacement Apply(Move move)
    {
        var node = move.Node;
        var previousX = node.Transform?.X ?? 0;
        var previousY = node.Transform?.Y ?? 0;
        var x = Math.Round(previousX + move.Dx, Decimals);
        var y = Math.Round(previousY + move.Dy, Decimals);
        if (x != previousX || y != previousY)
        {
            node.Transform ??= new Transform();
            node.Transform.X = x;
            node.Transform.Y = y;
        }
        return new NodePlacement(node.Id, node.Name, previousX, previousY, x, y);
    }

    private static DocumentEngineException InvalidArguments(string message) =>
        new(EngineErrors.InvalidArguments, message);

    private static DocumentEngineException InvalidProperty(string message) =>
        new(EngineErrors.InvalidProperty, message);

    private sealed record Move(Node Node, double Dx, double Dy);

    private sealed record Item(Node Node, double Start, double Size, double Center, int Order);
}
