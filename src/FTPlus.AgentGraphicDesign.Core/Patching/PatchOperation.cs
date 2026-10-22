using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Patching;

/// <summary>
/// One step of an atomic document patch. The JSON form carries an <c>op</c>
/// discriminator plus the fields of the matching record; the whole list is applied
/// by <see cref="IDocumentEngine.ApplyPatch"/> and persisted with a single revision bump.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(SetOperation), SetOperation.OpName)]
[JsonDerivedType(typeof(UnsetOperation), UnsetOperation.OpName)]
[JsonDerivedType(typeof(SetNodeOperation), SetNodeOperation.OpName)]
[JsonDerivedType(typeof(AddNodeOperation), AddNodeOperation.OpName)]
[JsonDerivedType(typeof(DeleteNodeOperation), DeleteNodeOperation.OpName)]
[JsonDerivedType(typeof(MoveNodeOperation), MoveNodeOperation.OpName)]
[JsonDerivedType(typeof(ReorderNodeOperation), ReorderNodeOperation.OpName)]
[JsonDerivedType(typeof(SetColorTokenOperation), SetColorTokenOperation.OpName)]
[JsonDerivedType(typeof(RemoveColorTokenOperation), RemoveColorTokenOperation.OpName)]
[JsonDerivedType(typeof(SetCanvasOperation), SetCanvasOperation.OpName)]
public abstract record PatchOperation
{
    /// <summary>Every accepted <c>op</c> discriminator, in documentation order.</summary>
    public static readonly string[] OpNames =
    [
        SetOperation.OpName,
        UnsetOperation.OpName,
        SetNodeOperation.OpName,
        AddNodeOperation.OpName,
        DeleteNodeOperation.OpName,
        MoveNodeOperation.OpName,
        ReorderNodeOperation.OpName,
        SetColorTokenOperation.OpName,
        RemoveColorTokenOperation.OpName,
        SetCanvasOperation.OpName
    ];

    /// <summary>The JSON <c>op</c> discriminator of this operation.</summary>
    [JsonIgnore]
    public abstract string Op { get; }
}

/// <summary>
/// Set one property of a node. <paramref name="Path"/> is a JSON Pointer relative to the
/// node's JSON form (for example <c>/style/fill/color</c>, <c>/transform/rotation</c>,
/// <c>/points/1/x</c>). Missing intermediate objects are created; arrays accept an
/// existing index or <c>-</c> to append. <c>/id</c> and <c>/type</c> are rejected.
/// </summary>
public sealed record SetOperation(Guid NodeId, string Path, JsonElement Value) : PatchOperation
{
    public const string OpName = "set";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>Remove one property (or array element) of a node so it reverts to its default.</summary>
public sealed record UnsetOperation(Guid NodeId, string Path) : PatchOperation
{
    public const string OpName = "unset";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>
/// Replace a node in place with a complete node JSON object. The <c>id</c> inside
/// <paramref name="Node"/> must equal <paramref name="NodeId"/>; a container's
/// children are replaced by the supplied <c>children</c> array as well. A missing
/// <c>order</c> keeps the node's current sibling order.
/// </summary>
public sealed record SetNodeOperation(Guid NodeId, JsonObject Node) : PatchOperation
{
    public const string OpName = "setNode";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>
/// Insert a node JSON object under <paramref name="ParentId"/> (null = root). A missing
/// <c>id</c> receives a new GUID (nested children too); a missing <c>order</c> places the
/// node last, or directly after <paramref name="AfterId"/> when given.
/// </summary>
public sealed record AddNodeOperation(JsonObject Node, Guid? ParentId = null, Guid? AfterId = null) : PatchOperation
{
    public const string OpName = "addNode";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>Delete a node (never the root) and its subtree.</summary>
public sealed record DeleteNodeOperation(Guid NodeId) : PatchOperation
{
    public const string OpName = "deleteNode";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>
/// Move a node under <paramref name="ParentId"/> (null = root), placing it last or
/// directly after <paramref name="AfterId"/> when given.
/// </summary>
public sealed record MoveNodeOperation(Guid NodeId, Guid? ParentId = null, Guid? AfterId = null) : PatchOperation
{
    public const string OpName = "moveNode";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>Reorder a node among its siblings: after <paramref name="AfterId"/>, or first when null.</summary>
public sealed record ReorderNodeOperation(Guid NodeId, Guid? AfterId = null) : PatchOperation
{
    public const string OpName = "reorderNode";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>Add or update a color token.</summary>
public sealed record SetColorTokenOperation(string Name, string Value) : PatchOperation
{
    public const string OpName = "setColorToken";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>Remove a color token; fails when the token does not exist.</summary>
public sealed record RemoveColorTokenOperation(string Name) : PatchOperation
{
    public const string OpName = "removeColorToken";

    [JsonIgnore]
    public override string Op => OpName;
}

/// <summary>
/// Change canvas dimensions and/or background. Omit a field to leave it unchanged;
/// a JSON <c>null</c> background clears it.
/// </summary>
public sealed record SetCanvasOperation(double? Width = null, double? Height = null, JsonElement Background = default) : PatchOperation
{
    public const string OpName = "setCanvas";

    [JsonIgnore]
    public override string Op => OpName;
}
