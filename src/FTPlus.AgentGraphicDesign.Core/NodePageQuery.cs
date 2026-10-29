using System.Collections.Immutable;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>A bounded, cancellation-aware document-order query; depth is relative to NodeId.</summary>
public sealed record NodePageQuery(Guid? NodeId = null, int Offset = 0, int Limit = 100, int? MaxDepth = null)
{
    public const int MaxScannedNodes = 100_000;
    public const int MaxPageSize = 1000;
    public const int MaxTraversalDepth = 128;
}

public sealed record NodePage(Guid RootId, int Total, int Offset, int Limit, bool HasMore, ImmutableArray<NodeInfo> Nodes);

internal static class NodePageTraversal
{
    public static NodePage Query(Document document, NodePageQuery query, CancellationToken cancellationToken,
        Action<int>? onNodeVisited = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.Root is null || document.Id == Guid.Empty || document.Root.Id == document.Id)
            throw new DocumentEngineException(EngineErrors.InvalidProperty, "Invalid document root in query");
        if (query.Offset is < 0 or > NodePageQuery.MaxScannedNodes || query.Limit is < 1 or > NodePageQuery.MaxPageSize ||
            query.MaxDepth is < 0 or > NodePageQuery.MaxTraversalDepth)
            throw new DocumentEngineException(EngineErrors.InvalidArguments, "Invalid node page offset, limit or depth");

        var pending = new Stack<(Node Node, int Depth, int Index)>();
        pending.Push((document.Root, 0, 0));
        var indices = new List<int>();
        var seen = new HashSet<Guid>();
        var page = ImmutableArray.CreateBuilder<NodeInfo>();
        var rootId = query.NodeId ?? document.Root.Id;
        int? selectedDepth = null;
        var scanned = 0;
        var total = 0;
        while (pending.TryPop(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Once the selected subtree is exited, unrelated content cannot affect
            // its result or consume its work budget.
            if (selectedDepth is { } selectedRootDepth && entry.Depth <= selectedRootDepth)
                break;
            if (++scanned > NodePageQuery.MaxScannedNodes || entry.Depth > NodePageQuery.MaxTraversalDepth)
                throw new DocumentEngineException(EngineErrors.ResourceLimit, "Node query scan/depth budget exceeded");
            var node = entry.Node;
            if (node is null || node.Id == Guid.Empty || !seen.Add(node.Id))
                throw new DocumentEngineException(EngineErrors.InvalidProperty, "Invalid or duplicate node in query");
            onNodeVisited?.Invoke(scanned);

            if (indices.Count > entry.Depth)
                indices.RemoveRange(entry.Depth, indices.Count - entry.Depth);
            if (entry.Depth > 0)
            {
                if (indices.Count < entry.Depth) indices.Add(entry.Index);
                else indices[entry.Depth - 1] = entry.Index;
            }
            if (node.Id == rootId) selectedDepth = entry.Depth;

            if (selectedDepth is { } rootDepth &&
                (query.MaxDepth is null || entry.Depth - rootDepth <= query.MaxDepth))
            {
                if (total >= query.Offset && page.Count < query.Limit)
                {
                    var path = "root" + string.Concat(indices.Select(index => $".children[{index}]"));
                    page.Add(new NodeInfo(node.Id, DocumentEngine.GetNodeTypeName(node), node.Name, node.Order, path, node.Visible, entry.Depth - rootDepth));
                }
                total++;
            }
            if (node is IContainerNode container)
            {
                if (selectedDepth is { } selected && query.MaxDepth is { } maximum && entry.Depth - selected >= maximum)
                    continue;
                if (container.Children is null)
                    throw new DocumentEngineException(EngineErrors.InvalidProperty, "Null children in query");
                // Refuse before sorting/materializing an oversized child collection.
                if (container.Children.Count > NodePageQuery.MaxScannedNodes - scanned - pending.Count)
                    throw new DocumentEngineException(EngineErrors.ResourceLimit, "Node query scan budget exceeded");
                foreach (var child in container.Children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (child is null) throw new DocumentEngineException(EngineErrors.InvalidProperty, "Null child in query");
                }
                cancellationToken.ThrowIfCancellationRequested();
                var ordered = container.Children.Select((child, index) => (Node: child, OriginalIndex: index)).ToArray();
                try
                {
                    Array.Sort(ordered, (left, right) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = StringComparer.Ordinal.Compare(left.Node.Order, right.Node.Order);
                        return result != 0 ? result : left.OriginalIndex.CompareTo(right.OriginalIndex);
                    });
                }
                catch (InvalidOperationException exception) when (exception.InnerException is OperationCanceledException)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                for (var index = ordered.Length - 1; index >= 0; index--)
                    pending.Push((ordered[index].Node, entry.Depth + 1, index));
            }
        }
        if (selectedDepth is null)
            throw new DocumentEngineException(EngineErrors.NodeNotFound, "Subtree node was not found");
        return new NodePage(rootId, total, query.Offset, query.Limit, query.Offset + page.Count < total, page.ToImmutable());
    }
}
