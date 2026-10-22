namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// Options for deterministic document creation. When Seed is provided, IDs are
/// derived deterministically; explicit IDs take precedence over seed-derived ones.
/// </summary>
public sealed record CreateDocumentOptions
{
    /// <summary>
    /// Seed for deterministic ID generation. When set, DocumentId and RootId
    /// are derived unless explicitly provided.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>Explicit document ID; overrides seed-derived ID.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>Explicit root node ID; overrides seed-derived ID.</summary>
    public Guid? RootId { get; init; }
}
