using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Persistence;

public class FileDocumentStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileDocumentStore _store;

    public FileDocumentStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-test-{Guid.NewGuid()}");
        _store = new FileDocumentStore(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        var result = _store.List().ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void List_ValidDocument_ReturnsDocumentInfo()
    {
        var doc = CreateTestDocument("test-doc");
        Save(doc);

        var result = _store.List().ToList();

        Assert.Single(result);
        Assert.Equal(doc.Id, result[0].Id);
        Assert.Equal(doc.Name, result[0].Name);
    }

    [Fact]
    public void List_CorruptDocument_SkipsCorruptFile()
    {
        var validDoc = CreateTestDocument("valid-doc");
        Save(validDoc);

        var corruptPath = Path.Combine(_testDir, $"{Guid.NewGuid()}.agd.json");
        File.WriteAllText(corruptPath, "{ invalid json }");

        var result = _store.List().ToList();

        Assert.Single(result);
        Assert.Equal(validDoc.Id, result[0].Id);
    }

    [Fact]
    public void List_MultipleValidDocuments_WithOneCorrupt_ReturnsOnlyValid()
    {
        var doc1 = CreateTestDocument("doc-1");
        var doc2 = CreateTestDocument("doc-2");
        Save(doc1);
        Save(doc2);

        var corruptPath = Path.Combine(_testDir, $"{Guid.NewGuid()}.agd.json");
        File.WriteAllText(corruptPath, "not json at all");

        var result = _store.List().ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Id == doc1.Id);
        Assert.Contains(result, r => r.Id == doc2.Id);
    }

    [Fact]
    public void List_EmptyJsonFile_SkipsFile()
    {
        var validDoc = CreateTestDocument("valid-doc");
        Save(validDoc);

        var emptyPath = Path.Combine(_testDir, $"{Guid.NewGuid()}.agd.json");
        File.WriteAllText(emptyPath, "");

        var result = _store.List().ToList();

        Assert.Single(result);
    }

    [Fact]
    public void List_NullJsonFile_SkipsFile()
    {
        var validDoc = CreateTestDocument("valid-doc");
        Save(validDoc);

        var nullPath = Path.Combine(_testDir, $"{Guid.NewGuid()}.agd.json");
        File.WriteAllText(nullPath, "null");

        var result = _store.List().ToList();

        Assert.Single(result);
    }

    [Fact]
    public void Save_WithMatchingExpectedRevn_Succeeds()
    {
        var doc = CreateTestDocument("revn-test");
        Save(doc);

        doc.Revn = 2;
        Save(doc, expectedRevn: 1);

        var loaded = _store.Load(doc.Id)?.Document;
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Revn);
    }

    [Fact]
    public void Save_WithMismatchedExpectedRevn_ThrowsRevisionConflict()
    {
        var doc = CreateTestDocument("revn-conflict");
        doc.Revn = 5;
        Save(doc);

        doc.Revn = 6;

        var ex = Assert.Throws<DocumentEngineException>(() =>
            Save(doc, expectedRevn: 3));

        Assert.Equal(EngineErrors.RevisionConflict, ex.Code);
        Assert.Contains("5", ex.Message);
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void Save_WithoutExpectedRevn_DoesNotCheckRevision()
    {
        var doc = CreateTestDocument("no-check");
        doc.Revn = 1;
        Save(doc);

        doc.Revn = 100;
        Save(doc);

        var loaded = _store.Load(doc.Id)?.Document;
        Assert.NotNull(loaded);
        Assert.Equal(100, loaded.Revn);
    }

    [Fact]
    public void Load_AfterExternalSnapshotRestore_UsesRestoredContentAndRevision()
    {
        var doc = CreateTestDocument("tracked-original");
        Save(doc);
        var path = _store.Load(doc.Id)!.SourcePath!;
        var originalBytes = File.ReadAllBytes(path);

        doc.Name = "later-edit";
        doc.Revn = 2;
        Save(doc, expectedRevn: 1);
        Assert.Equal("later-edit", _store.Load(doc.Id)!.Document.Name);

        // Model a completed user-managed restore while no writer is active, without requiring Git.
        File.WriteAllBytes(path, originalBytes);
        var restored = _store.Load(doc.Id)!;
        Assert.Equal("tracked-original", restored.Document.Name);
        Assert.Equal(1, restored.Document.Revn);
        Assert.Equal("tracked-original", Assert.Single(_store.List()).Name);

        var conflict = Assert.Throws<DocumentEngineException>(() => Save(doc, expectedRevn: 2));
        Assert.Equal(EngineErrors.RevisionConflict, conflict.Code);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));

        restored.Document.Name = "edit-after-restore";
        restored.Document.Revn = 2;
        _store.Save(restored, expectedRevn: 1);
        Assert.Equal("edit-after-restore", _store.Load(doc.Id)!.Document.Name);
        Assert.Equal(2, _store.Load(doc.Id)!.Document.Revn);
    }

    [Fact]
    public void Save_NewDocument_WithExpectedRevn_ReportsConflict()
    {
        var doc = CreateTestDocument("new-doc");

        var exception = Assert.Throws<DocumentEngineException>(() => Save(doc, expectedRevn: 1));

        Assert.Equal(EngineErrors.RevisionConflict, exception.Code);
        Assert.Null(_store.Load(doc.Id));
    }

    [Fact]
    public void Save_AtomicWrite_NoTempFileLeft()
    {
        var doc = CreateTestDocument("atomic-test");
        Save(doc);

        var files = Directory.GetFiles(_testDir, "*.tmp");
        Assert.Empty(files);
    }

    [Fact]
    public void Delete_ExistingDocument_RemovesFile()
    {
        var doc = CreateTestDocument("delete-test");
        Save(doc);

        Assert.NotNull(_store.Load(doc.Id)?.Document);

        _store.Delete(doc.Id);

        Assert.Null(_store.Load(doc.Id));
    }

    [Fact]
    public void Delete_NonExistentDocument_DoesNotThrow()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = Record.Exception(() => _store.Delete(nonExistentId));

        Assert.Null(exception);
    }

    [Fact]
    public void Delete_ExistingDocument_RemovesFromList()
    {
        var doc1 = CreateTestDocument("doc-1");
        var doc2 = CreateTestDocument("doc-2");
        Save(doc1);
        Save(doc2);

        Assert.Equal(2, _store.List().Count());

        _store.Delete(doc1.Id);

        var remaining = _store.List().ToList();
        Assert.Single(remaining);
        Assert.Equal(doc2.Id, remaining[0].Id);
    }

    [Fact]
    public void Delete_LastDocument_ListReturnsEmpty()
    {
        var doc = CreateTestDocument("last-doc");
        Save(doc);

        _store.Delete(doc.Id);

        Assert.Empty(_store.List());
    }

    [Fact]
    public void Delete_ThenSave_WorksCorrectly()
    {
        var doc = CreateTestDocument("resave-test");
        Save(doc);

        _store.Delete(doc.Id);

        doc.Revn = 2;
        Save(doc);

        var loaded = _store.Load(doc.Id)?.Document;
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Revn);
    }

    private static Document CreateTestDocument(string name)
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = name,
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 100, Height = 100 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = 100,
                Height = 100,
                Children = []
            }
        };
    }

    private void Save(Document document, long? expectedRevn = null) =>
        _store.Save(new DocumentStoreEntry(document, SourcePath: null), expectedRevn);
}
