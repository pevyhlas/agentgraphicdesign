using System.Runtime.CompilerServices;
using FTPlus.AgentGraphicDesign.Core.Documents;

namespace FTPlus.AgentGraphicDesign.Core;

internal static class DocumentSynchronization
{
    private static readonly ConditionalWeakTable<Document, object> Locks = new();

    public static object For(Document document) => Locks.GetValue(document, static _ => new object());

    public static void EnsureSupportedVersion(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version > Document.SupportedVersion)
        {
            throw new DocumentEngineException(
                EngineErrors.UnsupportedVersion,
                $"Document version {document.Version} is newer than supported version {Document.SupportedVersion}");
        }
    }

    public static void EnsureCanMutate(Document document)
    {
        EnsureSupportedVersion(document);
        if (document.Revn == long.MaxValue)
        {
            throw new DocumentEngineException(EngineErrors.RevisionConflict,
                "Document revision cannot be incremented beyond Int64.MaxValue");
        }
    }

    public static void IncrementRevision(Document document)
    {
        EnsureCanMutate(document);
        document.Revn = checked(document.Revn + 1);
    }
}
