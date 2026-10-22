using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Ordering;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Patching;

/// <summary>
/// Applies single patch operations to one in-memory document. Tree and token operations
/// reuse the engine's single-operation implementations so ordering, index maintenance and
/// error codes stay identical; property operations round-trip the node through the
/// document's polymorphic serializer. Failures throw <see cref="DocumentEngineException"/>
/// (or <see cref="JsonException"/> for malformed node JSON); the engine attributes them to
/// the operation index.
/// </summary>
internal sealed class DocumentPatcher
{
    private const string TokenNotFound = "TOKEN_NOT_FOUND";

    private readonly IDocumentEngine _engine;
    private readonly Document _document;

    public DocumentPatcher(IDocumentEngine engine, Document document)
    {
        _engine = engine;
        _document = document;
    }

    /// <summary>Apply one operation; returns the created node ID for <c>addNode</c>.</summary>
    public Guid? Apply(PatchOperation operation)
    {
        switch (operation)
        {
            case SetOperation set:
                ApplySet(set);
                return null;
            case UnsetOperation unset:
                ApplyUnset(unset);
                return null;
            case SetNodeOperation setNode:
                ApplySetNode(setNode);
                return null;
            case AddNodeOperation addNode:
                return ApplyAddNode(addNode);
            case DeleteNodeOperation delete:
                _engine.RemoveNode(_document, delete.NodeId);
                return null;
            case MoveNodeOperation move:
                _engine.MoveNode(_document, move.NodeId, move.ParentId);
                if (move.AfterId is Guid moveAfter)
                {
                    _engine.ReorderNode(_document, move.NodeId, moveAfter, null);
                }
                return null;
            case ReorderNodeOperation reorder:
                ApplyReorder(reorder);
                return null;
            case SetColorTokenOperation setToken:
                _engine.SetColorToken(_document, setToken.Name, setToken.Value);
                return null;
            case RemoveColorTokenOperation removeToken:
                if (!_engine.RemoveColorToken(_document, removeToken.Name))
                {
                    throw new DocumentEngineException(TokenNotFound, $"Token not found: {removeToken.Name}");
                }
                return null;
            case SetCanvasOperation canvas:
                ApplySetCanvas(canvas);
                return null;
            default:
                throw new DocumentEngineException(EngineErrors.InvalidArguments,
                    $"Unsupported op '{operation.Op}'");
        }
    }

    private void ApplySet(SetOperation operation)
    {
        var segments = ParseNodePath(operation.Path);
        if (operation.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw new DocumentEngineException(EngineErrors.InvalidArguments, "set requires a 'value'");
        }

        var (node, parent) = FindNode(operation.NodeId);
        var json = DocumentSerializer.SerializeNode(node);
        JsonPointer.Set(json, segments, JsonNode.Parse(operation.Value.GetRawText()), operation.Path);
        ReplaceNode(node, parent, Materialize(json, node, parent));
    }

    private void ApplyUnset(UnsetOperation operation)
    {
        var segments = ParseNodePath(operation.Path);
        var (node, parent) = FindNode(operation.NodeId);
        var json = DocumentSerializer.SerializeNode(node);
        JsonPointer.Remove(json, segments, operation.Path);
        ReplaceNode(node, parent, Materialize(json, node, parent));
    }

    private void ApplySetNode(SetNodeOperation operation)
    {
        var (node, parent) = FindNode(operation.NodeId);
        var json = operation.Node.DeepClone().AsObject();
        PrepareAuthoredNode(json, "$", assignMissingIds: false, fallbackOrder: node.Order);
        if (!TryGetGuid(json, "id", out var id) || id != operation.NodeId)
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"setNode requires the node 'id' to equal nodeId {operation.NodeId}");
        }

        var replacement = DocumentSerializer.DeserializeNode(json, "$", requireFrame: parent is null);
        ReplaceNode(node, parent, replacement);
    }

    private Guid ApplyAddNode(AddNodeOperation operation)
    {
        var container = ResolveContainer(operation.ParentId);
        var json = operation.Node.DeepClone().AsObject();
        var hasOrder = json.TryGetPropertyValue("order", out var orderNode) && orderNode is not null;
        if (hasOrder && operation.AfterId.HasValue)
        {
            throw new DocumentEngineException(EngineErrors.InvalidArguments,
                "addNode accepts either an explicit 'order' or 'afterId', not both");
        }

        var lastOrder = container.Children.Count == 0
            ? null
            : container.Children.Select(child => child.Order).Max(StringComparer.Ordinal);
        PrepareAuthoredNode(json, "$", assignMissingIds: true, fallbackOrder: hasOrder ? null : NextOrder(lastOrder));

        var node = DocumentSerializer.DeserializeNode(json, "$", requireFrame: false);
        if (container.Children.Any(child => child.Order == node.Order))
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Sibling order '{node.Order}' is already used under the parent");
        }

        container.Children.Add(node);
        DocumentIndex.Build(_document);
        if (operation.AfterId is Guid afterId)
        {
            _engine.ReorderNode(_document, node.Id, afterId, null);
        }

        return node.Id;
    }

    private void ApplyReorder(ReorderNodeOperation operation)
    {
        if (operation.AfterId is Guid afterId)
        {
            _engine.ReorderNode(_document, operation.NodeId, afterId, null);
            return;
        }

        var (node, parent) = FindNode(operation.NodeId);
        if (parent is null)
        {
            throw new DocumentEngineException(EngineErrors.CannotDeleteRoot, "Cannot reorder the root node");
        }

        var first = parent.Children
            .Where(child => child.Id != node.Id)
            .MinBy(child => child.Order, StringComparer.Ordinal);
        if (first is null || string.CompareOrdinal(node.Order, first.Order) < 0)
        {
            return;
        }

        _engine.ReorderNode(_document, operation.NodeId, null, first.Id);
    }

    private void ApplySetCanvas(SetCanvasOperation operation)
    {
        var background = operation.Background;
        if (operation.Width is null && operation.Height is null && background.ValueKind == JsonValueKind.Undefined)
        {
            throw new DocumentEngineException(EngineErrors.InvalidArguments,
                "setCanvas requires at least one of 'width', 'height' or 'background'");
        }

        if (operation.Width is double width && (!double.IsFinite(width) || width <= 0))
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Canvas width must be positive; got {width}");
        }

        if (operation.Height is double height && (!double.IsFinite(height) || height <= 0))
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Canvas height must be positive; got {height}");
        }

        string? color = null;
        var setBackground = true;
        switch (background.ValueKind)
        {
            case JsonValueKind.Undefined:
                setBackground = false;
                break;
            case JsonValueKind.Null:
                break;
            case JsonValueKind.String:
                color = background.GetString();
                if (!DocumentLimits.IsValidColor(color))
                {
                    throw new DocumentEngineException(EngineErrors.InvalidProperty,
                        $"Invalid canvas background color: '{color}'");
                }
                break;
            default:
                throw new DocumentEngineException(EngineErrors.InvalidArguments,
                    "setCanvas 'background' must be a color string or null");
        }

        if (operation.Width is double newWidth)
        {
            _document.Canvas.Width = newWidth;
        }

        if (operation.Height is double newHeight)
        {
            _document.Canvas.Height = newHeight;
        }

        if (setBackground)
        {
            _document.Canvas.Background = color;
        }
    }

    // ---- Helpers ----

    private static IReadOnlyList<string> ParseNodePath(string path)
    {
        var segments = JsonPointer.Parse(path);
        if (segments[0] is "id" or "type")
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Path '{path}' cannot change a node's {segments[0]}; use setNode, addNode or deleteNode instead");
        }

        if (segments[0] == "children")
        {
            // Descendants are addressed by their own nodeId; reaching through 'children' would bypass the
            // per-node id/type guard and any surface-level checks on node payloads (e.g. MCP image src confinement).
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Path '{path}' cannot address a node's children; target the child by its own nodeId or use addNode, setNode, deleteNode, moveNode or reorderNode");
        }

        return segments;
    }

    private (Node Node, IContainerNode? Parent) FindNode(Guid nodeId)
    {
        var index = DocumentIndex.Build(_document);
        var node = index.Find(nodeId)
            ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");
        return (node, index.ParentOf(nodeId));
    }

    private IContainerNode ResolveContainer(Guid? parentId)
    {
        if (parentId is null || parentId == _document.Root.Id)
        {
            return _document.Root;
        }

        var parent = DocumentIndex.Build(_document).Find(parentId.Value)
            ?? throw new DocumentEngineException(EngineErrors.ParentNotFound, $"Parent node not found: {parentId}");
        return parent as IContainerNode
            ?? throw new DocumentEngineException(EngineErrors.ParentNotContainer,
                $"Parent node {parent.Id} ({DocumentEngine.GetNodeTypeName(parent)}) cannot contain children");
    }

    private static Node Materialize(JsonObject json, Node original, IContainerNode? parent)
    {
        var node = DocumentSerializer.DeserializeNode(json, "$", requireFrame: parent is null);
        if (node.GetType() != original.GetType())
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"A property patch cannot change the node type of {original.Id}");
        }

        if (node.Id != original.Id)
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"A property patch cannot change the id of {original.Id}");
        }

        return node;
    }

    private void ReplaceNode(Node original, IContainerNode? parent, Node replacement)
    {
        if (parent is null)
        {
            _document.Root = replacement as FrameNode
                ?? throw new DocumentEngineException(EngineErrors.InvalidProperty, "The root node must remain a frame");
            return;
        }

        parent.Children[parent.Children.IndexOf(original)] = replacement;
    }

    /// <summary>
    /// Check an authored node subtree before it enters the serializer: canonical node types
    /// only, GUID ids (generated for the top node when allowed and for nested children
    /// always), a children array on every container, and sequential fallback orders for
    /// children that omit one.
    /// </summary>
    private static void PrepareAuthoredNode(JsonObject node, string path, bool assignMissingIds, string? fallbackOrder)
    {
        if (!node.TryGetPropertyValue("type", out var typeNode) ||
            typeNode is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type))
        {
            throw new DocumentEngineException(EngineErrors.InvalidNodeType,
                $"Node at {path} requires a string 'type'. Accepted: {string.Join(", ", NodeTypeParser.CanonicalTypes)}");
        }

        if (Array.IndexOf(NodeTypeParser.CanonicalTypes, type) < 0)
        {
            throw new DocumentEngineException(EngineErrors.InvalidNodeType,
                $"Unknown node type '{type}' at {path}. Accepted: {string.Join(", ", NodeTypeParser.CanonicalTypes)}");
        }

        if (!node.TryGetPropertyValue("id", out var idNode) || idNode is null)
        {
            if (!assignMissingIds)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty, $"Node at {path} requires an 'id'");
            }

            node["id"] = Guid.NewGuid().ToString("D");
        }
        else if (!TryGetGuid(node, "id", out _))
        {
            throw new DocumentEngineException(EngineErrors.InvalidId, $"Node 'id' at {path} must be a UUID");
        }

        if (fallbackOrder is not null &&
            (!node.TryGetPropertyValue("order", out var orderNode) || orderNode is null))
        {
            node["order"] = fallbackOrder;
        }

        if (type is not ("frame" or "group"))
        {
            return;
        }

        if (!node.TryGetPropertyValue("children", out var childrenNode) || childrenNode is not JsonArray children)
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Container node at {path} must include a 'children' array (use [] for none)");
        }

        string? lastOrder = null;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not JsonObject child)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Child at {path}.children[{i}] must be a node object");
            }

            var hasOrder = child.TryGetPropertyValue("order", out var childOrder) &&
                childOrder is JsonValue childOrderValue && childOrderValue.TryGetValue<string>(out _);
            var order = hasOrder ? childOrder!.GetValue<string>() : NextOrder(lastOrder);
            PrepareAuthoredNode(child, $"{path}.children[{i}]", assignMissingIds: true, hasOrder ? null : order);
            lastOrder = lastOrder is null || string.CompareOrdinal(order, lastOrder) > 0 ? order : lastOrder;
        }
    }

    private static string NextOrder(string? lastOrder)
    {
        try
        {
            return FractionalIndex.Between(lastOrder, null);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Cannot assign a valid sibling order: {ex.Message}");
        }
    }

    private static bool TryGetGuid(JsonObject node, string property, out Guid value)
    {
        value = Guid.Empty;
        return node.TryGetPropertyValue(property, out var raw) &&
            raw is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text) &&
            Guid.TryParse(text, out value);
    }
}
