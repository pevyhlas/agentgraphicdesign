using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Patching;

/// <summary>Bounds shared by every surface that accepts a patch.</summary>
public static class PatchLimits
{
    /// <summary>Maximum operations in one patch.</summary>
    public const int MaxOperations = 500;

    /// <summary>Maximum JSON nesting depth of the operations array (matches the document reader).</summary>
    public const int MaxJsonDepth = 64;

    /// <summary>Maximum UTF-8 size of the operations JSON (matches the document input limit).</summary>
    public const long MaxJsonBytes = 16 * 1024 * 1024;
}

/// <summary>
/// The reason a patch was rejected. <see cref="OperationIndex"/> is the zero-based index of
/// the failing operation, or null when the failure is not attributable to one operation
/// (for example a revision conflict or a pre-existing validation error).
/// </summary>
public sealed record PatchError(string Code, string Message, int? OperationIndex = null, string? Path = null);

/// <summary>Outcome of <see cref="IDocumentEngine.ApplyPatch"/>. Nothing is persisted unless <see cref="Success"/>.</summary>
public sealed class PatchResult
{
    private static readonly IReadOnlyDictionary<int, Guid> NoCreatedIds = new Dictionary<int, Guid>();

    private PatchResult(
        bool success,
        long revn,
        int applied,
        IReadOnlyDictionary<int, Guid> createdIds,
        IReadOnlyList<ValidationWarning> warnings,
        PatchError? error)
    {
        Success = success;
        Revn = revn;
        Applied = applied;
        CreatedIds = createdIds;
        Warnings = warnings;
        Error = error;
    }

    public bool Success { get; }

    /// <summary>The document revision after the patch, or the unchanged revision on failure.</summary>
    public long Revn { get; }

    /// <summary>Number of operations applied and persisted; zero on failure.</summary>
    public int Applied { get; }

    /// <summary>IDs created by <c>addNode</c> operations, keyed by operation index.</summary>
    public IReadOnlyDictionary<int, Guid> CreatedIds { get; }

    /// <summary>Validation warnings for the persisted document.</summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; }

    public PatchError? Error { get; }

    public static PatchResult Ok(
        long revn,
        int applied,
        IReadOnlyDictionary<int, Guid> createdIds,
        IReadOnlyList<ValidationWarning> warnings) =>
        new(true, revn, applied, createdIds, warnings, null);

    public static PatchResult Fail(PatchError error, long revn) =>
        new(false, revn, 0, NoCreatedIds, [], error);
}

/// <summary>Outcome of parsing an operations JSON array.</summary>
public sealed class PatchParseResult
{
    private PatchParseResult(IReadOnlyList<PatchOperation>? operations, PatchError? error)
    {
        Operations = operations;
        Error = error;
    }

    public IReadOnlyList<PatchOperation>? Operations { get; }

    public PatchError? Error { get; }

    public bool Success => Operations is not null && Error is null;

    public static PatchParseResult Ok(IReadOnlyList<PatchOperation> operations) => new(operations, null);

    public static PatchParseResult Fail(PatchError error) => new(null, error);
}
