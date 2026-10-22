using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Ordering;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// An id→node index with parent links, built by one walk of the tree. Gives the
/// engine O(1) node lookup and direct-parent removal instead of recursive scans.
/// Rebuild after out-of-band tree mutations; engine operations build it per call.
/// </summary>
public sealed class DocumentIndex
{
    private readonly Dictionary<Guid, Node> _byId = [];
    private readonly Dictionary<Guid, IContainerNode?> _parents = [];

    private DocumentIndex()
    {
    }

    public static DocumentIndex Build(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Root is null)
        {
            throw Malformed("Document root cannot be null");
        }
        if (document.Id == Guid.Empty)
            throw Malformed("Document ID cannot be empty");
        if (document.Root.Id == document.Id)
            throw Malformed("Document and root IDs must be distinct");

        var index = new DocumentIndex();
        index.Walk(document.Root, null);
        return index;
    }

    /// <summary>Find a node by ID, or null.</summary>
    public Node? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The container holding this node, or null for the root.</summary>
    public IContainerNode? ParentOf(Guid id) => _parents.GetValueOrDefault(id);

    public bool Contains(Guid id) => _byId.ContainsKey(id);

    public int Count => _byId.Count;

    private void Walk(Node node, IContainerNode? parent)
    {
        if (node is null)
        {
            throw Malformed("Document contains a null node");
        }

        if (string.IsNullOrEmpty(node.Order))
        {
            throw Malformed($"Node {node.Id} has an empty order");
        }
        if (node.Id == Guid.Empty)
            throw Malformed("Node ID cannot be empty");
        if (node.Order.Length > DocumentLimits.MaxNodeOrderLength || !FractionalIndex.IsValid(node.Order))
            throw Malformed($"Node {node.Id} has an invalid fractional order '{node.Order}'");

        if (!_byId.TryAdd(node.Id, node))
        {
            throw Malformed($"Duplicate node ID: {node.Id}");
        }

        _parents[node.Id] = parent;

        if (node is IContainerNode container)
        {
            var children = container.Children;
            if (children is null)
            {
                throw Malformed($"Node {node.Id} has a null children collection");
            }

            var siblingOrders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in children)
            {
                if (child is null)
                {
                    throw Malformed($"Node {node.Id} contains a null child");
                }
                if (!siblingOrders.Add(child.Order))
                    throw Malformed($"Node {node.Id} contains duplicate sibling order '{child.Order}'");
                Walk(child, container);
            }
        }
    }

    private static DocumentEngineException Malformed(string message) =>
        new(EngineErrors.InvalidProperty, message);
}
