using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

/// <summary>
/// A node that can hold child nodes. Implemented by <see cref="FrameNode"/> and
/// <see cref="GroupNode"/>; the engine uses this to traverse and mutate the tree
/// without per-type switches.
/// </summary>
public interface IContainerNode
{
    List<Node> Children { get; }
}

public sealed class FrameNode : Node, IContainerNode
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("clipContent")]
    public bool ClipContent { get; set; } = true;

    [JsonPropertyName("children")]
    public List<Node> Children { get; set; } = [];
}

public sealed class GroupNode : Node, IContainerNode
{
    [JsonPropertyName("children")]
    public List<Node> Children { get; set; } = [];
}
