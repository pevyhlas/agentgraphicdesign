using FTPlus.AgentGraphicDesign.Core.Documents;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// Pluggable document storage abstraction using ID-based addressing. A loaded entry
/// carries an optional implementation-specific source so subsequent saves return to
/// the exact object that was opened.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Load a document and its source by unique ID. Returns null if not found.</summary>
    DocumentStoreEntry? Load(Guid id);

    /// <summary>Cancellation-aware load. Implementations should cancel lock waits and scans.</summary>
    DocumentStoreEntry? Load(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Load(id);
    }

    /// <summary>
    /// Persist an entry to the store. A null source resolves the existing object by ID
    /// or uses the implementation's default source for a new object.
    /// If <paramref name="expectedRevn"/> is provided, the save will fail with
    /// <see cref="EngineErrors.RevisionConflict"/> if the stored document's
    /// revision doesn't match.
    /// </summary>
    void Save(DocumentStoreEntry entry, long? expectedRevn = null);

    /// <summary>Cancellation-aware save.</summary>
    void Save(DocumentStoreEntry entry, long? expectedRevn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(entry, expectedRevn);
    }

    /// <summary>
    /// Atomically create a document at the implementation's default source. Fails with
    /// <see cref="DocumentStoreErrors.DocumentExists"/> if the ID or target already exists.
    /// </summary>
    void Create(Document document);

    /// <summary>Cancellation-aware create.</summary>
    void Create(Document document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Create(document);
    }

    /// <summary>Delete a document by ID. No-op if not found.</summary>
    void Delete(Guid id);

    /// <summary>Cancellation-aware delete.</summary>
    void Delete(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delete(id);
    }

    /// <summary>Enumerate all documents in the store.</summary>
    IEnumerable<DocumentInfo> List();

    /// <summary>Cancellation-aware enumeration.</summary>
    IEnumerable<DocumentInfo> List(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return List();
    }
}

/// <summary>A stored document and the implementation-specific source it was loaded from.</summary>
public sealed record DocumentStoreEntry(Document Document, string? SourcePath);

/// <summary>Store error codes that cannot yet be added to the shared EngineErrors file.</summary>
public static class DocumentStoreErrors
{
    public const string DocumentExists = "DOCUMENT_EXISTS";
}
