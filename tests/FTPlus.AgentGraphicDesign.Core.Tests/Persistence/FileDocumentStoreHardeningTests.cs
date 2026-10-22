using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Persistence;

public sealed class FileDocumentStoreHardeningTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"agd-store-hardening-{Guid.NewGuid():N}");
    private readonly string _outsideDir = Path.Combine(Path.GetTempPath(), $"agd-store-outside-{Guid.NewGuid():N}");

    public FileDocumentStoreHardeningTests()
    {
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_outsideDir);
    }

    public void Dispose()
    {
        Directory.Delete(_testDir, recursive: true);
        Directory.Delete(_outsideDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Save_TwoStoreInstancesAtSameRevision_OnlyOneSucceeds()
    {
        var firstStore = new FileDocumentStore(_testDir);
        var secondStore = new FileDocumentStore(_testDir);
        var original = CreateDocument("original");
        firstStore.Save(new DocumentStoreEntry(original, SourcePath: null));
        var first = Clone(original);
        var second = Clone(original);
        first.Name = "first";
        second.Name = "second";
        first.Revn = 2;
        second.Revn = 2;
        using var barrier = new Barrier(2);

        async Task<string?> WriteAsync(FileDocumentStore store, Document document)
        {
            return await Task.Run(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    store.Save(new DocumentStoreEntry(document, SourcePath: null), expectedRevn: 1);
                    return null;
                }
                catch (DocumentEngineException exception)
                {
                    return exception.Code;
                }
            });
        }

        var results = await Task.WhenAll(WriteAsync(firstStore, first), WriteAsync(secondStore, second));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result == EngineErrors.RevisionConflict);
        Assert.Equal(2, firstStore.Load(original.Id)!.Document.Revn);
    }

    [Fact]
    public async Task Create_TwoStoreInstances_AtomicallyRejectsOneWriter()
    {
        var firstStore = new FileDocumentStore(_testDir);
        var secondStore = new FileDocumentStore(_testDir);
        var document = CreateDocument("create");
        using var barrier = new Barrier(2);

        async Task<string?> CreateAsync(FileDocumentStore store)
        {
            return await Task.Run(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    store.Create(Clone(document));
                    return null;
                }
                catch (DocumentEngineException exception)
                {
                    return exception.Code;
                }
            });
        }

        var results = await Task.WhenAll(CreateAsync(firstStore), CreateAsync(secondStore));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result == DocumentStoreErrors.DocumentExists);
    }

    [Fact]
    public void Save_WithExpectedRevision_DoesNotReplaceCorruptTarget()
    {
        var store = new FileDocumentStore(_testDir);
        var document = CreateDocument("corrupt");
        var path = Path.Combine(_testDir, $"{document.Id}.agd.json");
        const string corrupt = "{ broken-json }";
        File.WriteAllText(path, corrupt);

        var exception = Assert.Throws<DocumentEngineException>(() =>
            store.Save(new DocumentStoreEntry(document, SourcePath: null), expectedRevn: 1));

        Assert.Equal(EngineErrors.ParseError, exception.Code);
        Assert.Equal(corrupt, File.ReadAllText(path));
    }

    [Fact]
    public void Load_CorruptDefaultPathFile_ReportsParseErrorInsteadOfNotFound()
    {
        var store = new FileDocumentStore(_testDir);
        var document = CreateDocument("corrupt-load");
        var path = Path.Combine(_testDir, $"{document.Id}.agd.json");
        File.WriteAllText(path, "{ broken-json }");

        var exception = Assert.Throws<DocumentEngineException>(() => store.Load(document.Id));

        Assert.Equal(EngineErrors.ParseError, exception.Code);
    }

    [Fact]
    public void Save_UsesLongExpectedRevision()
    {
        var store = new FileDocumentStore(_testDir);
        var document = CreateDocument("long");
        document.Revn = (long)int.MaxValue + 100;
        store.Save(new DocumentStoreEntry(document, SourcePath: null));
        var expected = document.Revn;
        document.Revn++;

        store.Save(new DocumentStoreEntry(document, SourcePath: null), expected);

        Assert.Equal(expected + 1, store.Load(document.Id)!.Document.Revn);
    }

    [Fact]
    public void OpenById_ArbitraryNestedFile_SavesBackToOriginalSource()
    {
        var nestedDirectory = Path.Combine(_testDir, "projects", "one");
        Directory.CreateDirectory(nestedDirectory);
        var sourcePath = Path.Combine(nestedDirectory, "friendly-name.agd.json");
        var document = CreateDocument("nested");
        File.WriteAllText(sourcePath, DocumentSerializer.Serialize(document));
        var store = new FileDocumentStore(_testDir);
        var engine = new DocumentEngine(store);

        var handle = engine.Open(document.Id.ToString());
        Assert.NotNull(handle);
        Assert.Equal(Path.GetFullPath(sourcePath), handle.SourcePath);
        engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle" });
        engine.Save(handle, expectedRevn: 1);

        Assert.Single(DocumentSerializer.Deserialize(File.ReadAllText(sourcePath))!.Root.Children);
        Assert.False(File.Exists(Path.Combine(_testDir, $"{document.Id}.agd.json")));
    }

    [Fact]
    public void IdOperations_RejectSymlinkDocumentTarget()
    {
        var store = new FileDocumentStore(_testDir);
        var document = CreateDocument("linked");
        var outside = Path.Combine(_outsideDir, "outside.agd.json");
        File.WriteAllText(outside, DocumentSerializer.Serialize(document));
        var link = Path.Combine(_testDir, $"{document.Id}.agd.json");
        if (!TryCreateFileLink(link, outside))
            throw Xunit.Sdk.SkipException.ForSkip("Symbolic links are unavailable on this platform/account");

        AssertPathRejected(() => store.Load(document.Id));
        AssertPathRejected(() => store.List().ToList());
        AssertPathRejected(() => store.Save(new DocumentStoreEntry(document, SourcePath: null)));
        AssertPathRejected(() => store.Delete(document.Id));
        Assert.Equal(DocumentSerializer.Serialize(document), File.ReadAllText(outside));
    }

    [Fact]
    public void List_RejectsSymlinkParentDirectory()
    {
        var store = new FileDocumentStore(_testDir);
        var realDirectory = Path.Combine(_outsideDir, "real");
        Directory.CreateDirectory(realDirectory);
        File.WriteAllText(Path.Combine(realDirectory, "nested.agd.json"),
            DocumentSerializer.Serialize(CreateDocument("nested")));
        var link = Path.Combine(_testDir, "linked-directory");
        if (!TryCreateDirectoryLink(link, realDirectory))
            throw Xunit.Sdk.SkipException.ForSkip("Directory symbolic links are unavailable on this platform/account");

        AssertPathRejected(() => store.List().ToList());
    }

    [Fact]
    public void Constructor_RejectsSymlinkBasePath()
    {
        var realDirectory = Path.Combine(_outsideDir, "real-base");
        Directory.CreateDirectory(realDirectory);
        var link = Path.Combine(_testDir, "linked-base");
        if (!TryCreateDirectoryLink(link, realDirectory))
            throw Xunit.Sdk.SkipException.ForSkip("Directory symbolic links are unavailable on this platform/account");

        AssertPathRejected(() => new FileDocumentStore(link));
    }

    [Fact]
    public void Save_ForgedNonDocumentSource_DoesNotOverwriteFile()
    {
        var source = Path.Combine(_testDir, "notes.txt");
        File.WriteAllText(source, "keep me");
        var store = new FileDocumentStore(_testDir);

        var exception = Assert.Throws<DocumentEngineException>(() =>
            store.Save(new DocumentStoreEntry(CreateDocument("forged"), source)));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal("keep me", File.ReadAllText(source));
    }

    [Fact]
    public void Save_ForgedCorruptAgdSource_DoesNotOverwriteFile()
    {
        var source = Path.Combine(_testDir, "corrupt.agd.json");
        File.WriteAllText(source, "{ corrupt }");
        var store = new FileDocumentStore(_testDir);

        var exception = Assert.Throws<DocumentEngineException>(() =>
            store.Save(new DocumentStoreEntry(CreateDocument("forged"), source)));

        Assert.Equal(EngineErrors.ParseError, exception.Code);
        Assert.Equal("{ corrupt }", File.ReadAllText(source));
    }

    [Fact]
    public void Save_SourceContainingDifferentDocument_IsRejectedWithoutOverwrite()
    {
        var sourceDocument = CreateDocument("source");
        var replacement = CreateDocument("replacement");
        var source = Path.Combine(_testDir, "source.agd.json");
        var original = DocumentSerializer.Serialize(sourceDocument);
        File.WriteAllText(source, original);
        var store = new FileDocumentStore(_testDir);

        var exception = Assert.Throws<DocumentEngineException>(() =>
            store.Save(new DocumentStoreEntry(replacement, source)));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal(original, File.ReadAllText(source));
    }

    [Fact]
    public void Save_MissingRememberedSource_DoesNotRecreateIt()
    {
        var document = CreateDocument("stale");
        var source = Path.Combine(_testDir, "stale.agd.json");
        File.WriteAllText(source, DocumentSerializer.Serialize(document));
        var store = new FileDocumentStore(_testDir);
        var loaded = store.Load(document.Id)!;
        File.Delete(source);

        var exception = Assert.Throws<DocumentEngineException>(() => store.Save(loaded));

        Assert.Equal(EngineErrors.DocumentNotFound, exception.Code);
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void List_OversizedDocument_StopsBeforeParsing()
    {
        const int maximumBytes = 512;
        var source = Path.Combine(_testDir, "oversized.agd.json");
        File.WriteAllText(source, new string('x', maximumBytes + 1));
        var store = new FileDocumentStore(_testDir, maximumBytes);

        var loadException = Assert.Throws<DocumentEngineException>(() => store.Load(Guid.NewGuid()));
        var listException = Assert.Throws<DocumentEngineException>(() => store.List().ToList());

        Assert.Equal(EngineErrors.ResourceLimit, loadException.Code);
        Assert.Equal(EngineErrors.ResourceLimit, listException.Code);
    }

    [Fact]
    public void Constructor_RejectsNestedStoreRoot()
    {
        _ = new FileDocumentStore(_testDir);
        var nested = Path.Combine(_testDir, "nested-store");

        var exception = Assert.Throws<DocumentEngineException>(() => new FileDocumentStore(nested));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
    }

    [Fact]
    public void Load_CancellationInterruptsLockWait()
    {
        var store = new FileDocumentStore(_testDir);
        using var heldLock = new FileStream(
            Path.Combine(_testDir, ".agd-store.lock"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.Throws<OperationCanceledException>(() => store.Load(Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public void Save_FutureVersion_IsRejectedWithoutCreatingFile()
    {
        var store = new FileDocumentStore(_testDir);
        var document = CreateDocument("future");
        document.Version = Document.SupportedVersion + 1;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            store.Save(new DocumentStoreEntry(document, SourcePath: null)));

        Assert.Equal(EngineErrors.UnsupportedVersion, exception.Code);
        Assert.Empty(Directory.GetFiles(_testDir, "*.agd.json", SearchOption.AllDirectories));
    }

    private static void AssertPathRejected(Action operation)
    {
        var exception = Assert.Throws<DocumentEngineException>(operation);
        Assert.Equal(EngineErrors.PathEscape, exception.Code);
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static Document Clone(Document document) =>
        DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))!;

    private static Document CreateDocument(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
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
