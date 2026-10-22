using System.Diagnostics;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Persistence;

public sealed class FileDocumentStore : IDocumentStore
{
    public const long DefaultMaxDocumentBytes = 16 * 1024 * 1024;

    private readonly string _basePath;
    private readonly string _storeLockPath;
    private readonly long _maxDocumentBytes;

    public FileDocumentStore(string basePath, long maxDocumentBytes = DefaultMaxDocumentBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        if (maxDocumentBytes is <= 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxDocumentBytes));

        _basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        _maxDocumentBytes = maxDocumentBytes;

        // Portable .NET does not expose no-follow handles for every operation. Rejecting
        // reparse points before and after lock acquisition narrows, but cannot eliminate,
        // the residual check-to-use race against an attacker who can rewrite the tree.
        EnsureNoReparsePoints(_basePath);
        // Reject an enclosing store before creating anything, so a refused store leaves no residue.
        EnsureNoAncestorStoreMarkers();
        Directory.CreateDirectory(_basePath);
        EnsureNoReparsePoints(_basePath);

        _storeLockPath = Path.Combine(_basePath, ".agd-store.lock");
        EnsureNoOverlappingStoreMarkers();
        using var storeMarker = AtomicDocumentFile.AcquireExclusiveLock(_storeLockPath);
        EnsureRootSafe();
        EnsureNoOverlappingStoreMarkers();
    }

    public DocumentStoreEntry? Load(Guid id) => Load(id, CancellationToken.None);

    public DocumentStoreEntry? Load(Guid id, CancellationToken cancellationToken)
    {
        EnsureRootSafe();
        using var storeLock = AtomicDocumentFile.AcquireExclusiveLock(_storeLockPath, cancellationToken);
        EnsureRootSafe();
        EnsureNoNestedStoreMarkers(cancellationToken);

        var match = FindById(id, cancellationToken);
        if (match is not null)
        {
            return new DocumentStoreEntry(match.Value.Document, match.Value.Path);
        }

        // FindById silently skips files it cannot read or deserialize. When the document's own
        // default-path file is present but unreadable or corrupt, surface that distinctly rather
        // than reporting the document as simply absent.
        ThrowIfDefaultPathUnreadable(id, cancellationToken);
        return null;
    }

    public void Save(DocumentStoreEntry entry, long? expectedRevn = null) =>
        Save(entry, expectedRevn, CancellationToken.None);

    public void Save(DocumentStoreEntry entry, long? expectedRevn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Document);
        DocumentSynchronization.EnsureSupportedVersion(entry.Document);

        EnsureRootSafe();
        using var storeLock = AtomicDocumentFile.AcquireExclusiveLock(_storeLockPath, cancellationToken);
        EnsureRootSafe();
        EnsureNoNestedStoreMarkers(cancellationToken);

        string path;
        var requireExistingSource = entry.SourcePath is not null;
        if (entry.SourcePath is not null)
        {
            path = ValidateDocumentSourcePath(entry.SourcePath);
        }
        else
        {
            var existing = FindById(entry.Document.Id, cancellationToken);
            requireExistingSource = existing is not null;
            path = existing?.Path ?? GetDefaultPath(entry.Document.Id);
            path = ValidateDocumentSourcePath(path);
        }

        AtomicDocumentFile.Save(
            path,
            entry.Document,
            expectedRevn,
            expectedDocumentId: entry.Document.Id,
            requireValidExisting: true,
            requireExisting: requireExistingSource,
            maxReadBytes: _maxDocumentBytes,
            validatePath: EnsureConfinedSafePath,
            cancellationToken: cancellationToken);
    }

    public void Create(Document document) => Create(document, CancellationToken.None);

    public void Create(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentSynchronization.EnsureSupportedVersion(document);

        EnsureRootSafe();
        using var storeLock = AtomicDocumentFile.AcquireExclusiveLock(_storeLockPath, cancellationToken);
        EnsureRootSafe();
        EnsureNoNestedStoreMarkers(cancellationToken);

        if (FindById(document.Id, cancellationToken) is not null)
        {
            throw DocumentExists(document.Id.ToString());
        }

        var path = ValidateDocumentSourcePath(GetDefaultPath(document.Id));
        AtomicDocumentFile.Create(path, document, EnsureConfinedSafePath, cancellationToken);
    }

    public void Delete(Guid id) => Delete(id, CancellationToken.None);

    public void Delete(Guid id, CancellationToken cancellationToken)
    {
        EnsureRootSafe();
        using var storeLock = AtomicDocumentFile.AcquireExclusiveLock(_storeLockPath, cancellationToken);
        EnsureRootSafe();
        EnsureNoNestedStoreMarkers(cancellationToken);

        var match = FindById(id, cancellationToken);
        if (match is null)
        {
            return;
        }

        var path = EnsureConfinedSafePath(match.Value.Path);
        using var fileLock = AtomicDocumentFile.AcquireExclusiveLock(path + ".lock", cancellationToken);
        EnsureConfinedSafePath(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IEnumerable<DocumentInfo> List() => List(CancellationToken.None);

    public IEnumerable<DocumentInfo> List(CancellationToken cancellationToken)
    {
        EnsureRootSafe();
        using var storeLock = AtomicDocumentFile.AcquireExclusiveLock(_storeLockPath, cancellationToken);
        EnsureRootSafe();
        EnsureNoNestedStoreMarkers(cancellationToken);

        var documents = new List<DocumentInfo>();
        foreach (var file in EnumerateDocumentFiles(cancellationToken))
        {
            try
            {
                var result = DocumentSerializer.TryDeserialize(
                    AtomicDocumentFile.ReadAllBytesBounded(file, _maxDocumentBytes, cancellationToken));
                if (result.Success && result.Document is not null)
                {
                    documents.Add(new DocumentInfo(
                        result.Document.Id,
                        result.Document.Name,
                        new FileInfo(file).LastWriteTimeUtc));
                }
            }
            catch (IOException)
            {
                // A file can disappear independently of this store's cooperative lock.
            }
        }

        return documents;
    }

    private (Document Document, string Path)? FindById(Guid id, CancellationToken cancellationToken)
    {
        (Document Document, string Path)? match = null;
        foreach (var file in EnumerateDocumentFiles(cancellationToken))
        {
            DeserializeResult result;
            try
            {
                result = DocumentSerializer.TryDeserialize(
                    AtomicDocumentFile.ReadAllBytesBounded(file, _maxDocumentBytes, cancellationToken));
            }
            catch (IOException)
            {
                continue;
            }

            if (!result.Success || result.Document?.Id != id)
            {
                continue;
            }

            if (match is not null)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Multiple documents in the store have ID {id}");
            }

            match = (result.Document, file);
        }

        return match;
    }

    private void ThrowIfDefaultPathUnreadable(Guid id, CancellationToken cancellationToken)
    {
        var defaultPath = GetDefaultPath(id);
        if (!File.Exists(defaultPath))
        {
            return;
        }

        DeserializeResult result;
        try
        {
            result = DocumentSerializer.TryDeserialize(
                AtomicDocumentFile.ReadAllBytesBounded(defaultPath, _maxDocumentBytes, cancellationToken));
        }
        catch (IOException ex)
        {
            throw new DocumentEngineException(EngineErrors.StoreUnavailable,
                $"Document {id} exists at {Path.GetFileName(defaultPath)} but could not be read: {ex.Message}");
        }

        if (!result.Success)
        {
            throw new DocumentEngineException(EngineErrors.ParseError,
                $"Document {id} exists at {Path.GetFileName(defaultPath)} but could not be parsed");
        }
    }

    private IEnumerable<string> EnumerateDocumentFiles(CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(_basePath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            EnsureConfinedSafePath(directory);

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AtomicDocumentFile.IsReparsePoint(entry))
                {
                    throw UnsafePath(entry);
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(EnsureConfinedSafePath(entry));
                }
                else if (string.Equals(Path.GetFileName(entry), ".agd-store.lock", StringComparison.Ordinal) &&
                    !string.Equals(directory, _basePath, PathComparison))
                {
                    throw ContainedStore(entry);
                }
                else if (entry.EndsWith(".agd.json", StringComparison.OrdinalIgnoreCase))
                {
                    yield return EnsureConfinedSafePath(entry);
                }
            }
        }
    }

    private string ValidateDocumentSourcePath(string path)
    {
        var safePath = EnsureConfinedSafePath(path);
        if (!safePath.EndsWith(".agd.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(safePath), ".agd-store.lock", StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Document source must be an .agd.json file and cannot be a reserved store path: {path}");
        }

        return safePath;
    }

    private string GetDefaultPath(Guid id) => Path.Combine(_basePath, $"{id}.agd.json");

    private void EnsureRootSafe()
    {
        EnsureNoReparsePoints(_basePath);
        EnsureNoReparsePoints(_storeLockPath);
    }

    private void EnsureNoOverlappingStoreMarkers()
    {
        EnsureNoAncestorStoreMarkers();
        EnsureNoNestedStoreMarkers(CancellationToken.None);
    }

    private void EnsureNoAncestorStoreMarkers()
    {
        var parent = Directory.GetParent(_basePath);
        while (parent is not null)
        {
            if (File.Exists(Path.Combine(parent.FullName, ".agd-store.lock")))
                throw EnclosingStore(parent.FullName);
            parent = parent.Parent;
        }
    }

    private void EnsureNoNestedStoreMarkers(CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(_basePath);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AtomicDocumentFile.IsReparsePoint(entry))
                    throw UnsafePath(entry);
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(EnsureConfinedSafePath(entry));
                }
                else if (string.Equals(Path.GetFileName(entry), ".agd-store.lock", StringComparison.Ordinal) &&
                    !string.Equals(directory, _basePath, PathComparison))
                {
                    throw ContainedStore(entry);
                }
            }
        }
    }

    private StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private string EnsureConfinedSafePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var basePrefix = Path.EndsInDirectorySeparator(_basePath)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(basePrefix, comparison) &&
            !string.Equals(fullPath, _basePath, comparison))
        {
            throw UnsafePath(path);
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw UnsafePath(path);
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;

        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if (AtomicDocumentFile.IsReparsePoint(current))
                {
                    throw UnsafePath(current);
                }
            }
            catch (FileNotFoundException)
            {
                // Remaining path components do not exist yet.
            }
            catch (DirectoryNotFoundException)
            {
                // Remaining path components do not exist yet.
            }
        }
    }

    private static DocumentEngineException UnsafePath(string path) =>
        new(EngineErrors.PathEscape, $"Reparse points and paths outside the store are not allowed: {path}");

    private static DocumentEngineException DocumentExists(string target) =>
        new(DocumentStoreErrors.DocumentExists, $"Document target already exists: {target}");

    private static DocumentEngineException EnclosingStore(string storeRoot) =>
        new(EngineErrors.StoreConflict,
            "Nested document stores are not supported: this store would be inside another store. " +
            $"Use file-only commands, or run from a directory outside the existing store rooted at {storeRoot}");

    // Reported relative to the directory that holds this store, so the location survives surface
    // path sanitization (which keeps only the last segment of absolute paths).
    private DocumentEngineException ContainedStore(string lockPath)
    {
        var holder = Path.GetDirectoryName(_basePath) ?? _basePath;
        var innerRoot = Path.GetRelativePath(holder, Path.GetDirectoryName(lockPath)!);
        return new(EngineErrors.StoreConflict,
            $"Nested document stores are not supported: this store would contain the existing store at '{innerRoot}'. " +
            "Move or remove that inner store, or run from a directory that does not contain it.");
    }
}

internal static class AtomicDocumentFile
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    public static void Save(
        string path,
        Document document,
        long? expectedRevn,
        Guid? expectedDocumentId = null,
        bool requireValidExisting = false,
        bool requireExisting = false,
        long maxReadBytes = FileDocumentStore.DefaultMaxDocumentBytes,
        Func<string, string>? validatePath = null,
        CancellationToken cancellationToken = default)
    {
        DocumentSynchronization.EnsureSupportedVersion(document);
        EnsureDirectory(path);
        validatePath?.Invoke(path);
        using var fileLock = AcquireExclusiveLock(path + ".lock", cancellationToken);
        validatePath?.Invoke(path);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            if (expectedRevn.HasValue)
            {
                throw new DocumentEngineException(EngineErrors.RevisionConflict,
                    $"Revision conflict: expected {expectedRevn.Value}, but the document does not exist");
            }
            if (requireExisting)
            {
                throw new DocumentEngineException(EngineErrors.DocumentNotFound,
                    $"Document source no longer exists: {path}");
            }
        }
        else if (expectedRevn.HasValue || expectedDocumentId.HasValue || requireValidExisting)
        {
            var result = DocumentSerializer.TryDeserialize(ReadAllBytesBounded(path, maxReadBytes, cancellationToken));
            if (!result.Success || result.Document is null)
            {
                throw new DocumentEngineException(EngineErrors.ParseError,
                    $"Cannot replace an invalid existing document: {result.Error?.Message}");
            }

            if (expectedDocumentId.HasValue && result.Document.Id != expectedDocumentId.Value)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Source document ID {result.Document.Id} does not match {expectedDocumentId.Value}");
            }

            if (expectedRevn.HasValue && result.Document.Revn != expectedRevn.Value)
            {
                throw new DocumentEngineException(EngineErrors.RevisionConflict,
                    $"Revision conflict: expected {expectedRevn.Value}, but document is at revision {result.Document.Revn}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        WriteAtomic(path, DocumentSerializer.Serialize(document), overwrite: true, cancellationToken);
        validatePath?.Invoke(path);
    }

    public static void Create(
        string path,
        Document document,
        Func<string, string>? validatePath = null,
        CancellationToken cancellationToken = default)
    {
        DocumentSynchronization.EnsureSupportedVersion(document);
        EnsureDirectory(path);
        validatePath?.Invoke(path);
        using var fileLock = AcquireExclusiveLock(path + ".lock", cancellationToken);
        validatePath?.Invoke(path);

        if (File.Exists(path) || Directory.Exists(path))
        {
            throw DocumentExists(path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        WriteAtomic(path, DocumentSerializer.Serialize(document), overwrite: false, cancellationToken);
        validatePath?.Invoke(path);
    }

    public static FileStream AcquireExclusiveLock(string lockPath, CancellationToken cancellationToken = default)
    {
        EnsureDirectory(lockPath);
        var timer = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (IsReparsePoint(lockPath))
                {
                    throw new DocumentEngineException(EngineErrors.PathEscape,
                        $"Lock path cannot be a reparse point: {lockPath}");
                }

                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                if (IsReparsePoint(lockPath))
                {
                    stream.Dispose();
                    throw new DocumentEngineException(EngineErrors.PathEscape,
                        $"Lock path cannot be a reparse point: {lockPath}");
                }

                return stream;
            }
            catch (IOException) when (timer.Elapsed < LockTimeout)
            {
                if (cancellationToken.WaitHandle.WaitOne(10))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    internal static byte[] ReadAllBytesBounded(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
            throw ResourceLimit(path, maximumBytes);

        using var content = new MemoryStream((int)Math.Min(stream.Length, maximumBytes));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            if (content.Length + read > maximumBytes)
                throw ResourceLimit(path, maximumBytes);
            content.Write(buffer, 0, read);
        }
        return content.ToArray();
    }

    internal static bool IsReparsePoint(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void WriteAtomic(
        string targetPath,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var tempPath = targetPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempPath, targetPath, overwrite);
            }
            catch (IOException) when (!overwrite && (File.Exists(targetPath) || Directory.Exists(targetPath)))
            {
                throw DocumentExists(targetPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; the target was never partially written.
                }
            }
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static DocumentEngineException DocumentExists(string path) =>
        new(DocumentStoreErrors.DocumentExists, $"Document target already exists: {path}");

    private static DocumentEngineException ResourceLimit(string path, long maximumBytes) =>
        new(EngineErrors.ResourceLimit, $"Document exceeds maximum size ({maximumBytes} bytes): {path}");
}
