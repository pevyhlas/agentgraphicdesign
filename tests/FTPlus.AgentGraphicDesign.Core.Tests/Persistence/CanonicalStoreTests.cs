using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Persistence;

public sealed class CanonicalStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agd-canonical-store-" + Guid.NewGuid().ToString("N"));

    public CanonicalStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task OriginalAndCanonicalRoots_ShareOneRevisionLockAndSourcePath()
    {
        var original = new FileDocumentStore(_root);
        var canonical = new FileDocumentStore(PlatformPaths.FullPath(_root));
        var document = new DocumentEngine().CreateDocument("original", 16, 16);
        original.Create(document);
        var loaded = canonical.Load(document.Id)!;
        Assert.Equal(PlatformPaths.FullPath(Path.Combine(_root, document.Id + ".agd.json")), loaded.SourcePath);
        using var barrier = new Barrier(2);
        Task<string?> Update(FileDocumentStore store, string name) => Task.Run(() =>
        {
            var changed = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))!;
            changed.Name = name;
            changed.Revn = document.Revn + 1;
            barrier.SignalAndWait();
            try
            {
                store.Save(new DocumentStoreEntry(changed, loaded.SourcePath), expectedRevn: document.Revn);
                return (string?)null;
            }
            catch (DocumentEngineException ex)
            {
                return ex.Code;
            }
        });
        var results = await Task.WhenAll(Update(original, "first"), Update(canonical, "second"));
        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result == EngineErrors.RevisionConflict);
        Assert.Equal(document.Revn + 1, original.Load(document.Id)!.Document.Revn);
        Assert.Single(Directory.EnumerateFiles(_root, ".agd-store.lock"));
        Assert.Single(Directory.EnumerateFiles(_root, "*.agd.json"));
    }
}
