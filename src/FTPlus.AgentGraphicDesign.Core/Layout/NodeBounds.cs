namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// Bounds of one node as reported by <see cref="IDocumentEngine.GetBounds"/>.
/// <see cref="Local"/> is the untransformed extent in the node's own coordinate space;
/// <see cref="World"/> is the axis-aligned box after the node's transform and every
/// ancestor transform, clipped by clipping-frame ancestors. Either is null when the
/// node has no geometry or is fully clipped; <see cref="Diagnostics"/> says why.
/// </summary>
public sealed record NodeBounds
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public string? Name { get; init; }

    public required bool Visible { get; init; }

    public BoundingBox? Local { get; init; }

    public BoundingBox? World { get; init; }

    /// <summary>
    /// For text: whether a shaping engine measured the extents. For containers: false when
    /// any descendant text is estimated. Null for nodes without text.
    /// </summary>
    public bool? Measured { get; init; }

    /// <summary>True when a clipping ancestor reduced or removed the world bounds.</summary>
    public bool Clipped { get; init; }

    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
