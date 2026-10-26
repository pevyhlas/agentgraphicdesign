using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Platform;

namespace FTPlus.AgentGraphicDesign.Core.Pixels.Storage;

/// <summary>
/// The qualified .NET 10 Linux x64 filesystem backend for native pixel storage: a real
/// cross-process advisory writer lock, bounded exact-EOF reads, private staged writes and
/// atomic directory publication with no replacement. The one atomic no-replace primitive is
/// the libc <c>renameat2(..., RENAME_NOREPLACE)</c> syscall wrapper; there is no weaker
/// fallback. Unsupported primitives/filesystems fail <c>ASSET_PLATFORM_UNSUPPORTED</c>.
/// </summary>
internal static class NativePixelFileSystem
{
    private const int AtFdCwd = -100;
    private const uint RenameNoReplace = 1;

    // Linux x86-64 errno values.
    private const int ENOENT = 2;
    private const int EEXIST = 17;
    private const int EINVAL = 22;
    private const int ENOSYS = 38;
    private const int ENOTEMPTY = 39;

    // statx(2) flags/mask and architecture-stable struct offsets for the qualified output probe.
    private const int AtSymlinkNofollow = 0x100;
    private const int AtEmptyPath = 0x1000;
    private const uint StatxBasicStats = 0x000007ff;
    private const uint StatxTypeModeNlink = 0x0000_0007; // STATX_TYPE | STATX_MODE | STATX_NLINK.
    private const int StatxMaskOffset = 0;    // stx_mask  (uint32)
    private const int StatxNlinkOffset = 16;  // stx_nlink (uint32)
    private const int StatxModeOffset = 28;   // stx_mode  (uint16)
    private const int StatxBufferBytes = 256; // sizeof(struct statx)
    private const uint StatxInode = 0x100;
    private const int SIfmt = 0xF000;
    private const int SIfreg = 0x8000;

    private static readonly TimeSpan LockWaitBudget = TimeSpan.FromSeconds(5);

    [DllImport("libc", SetLastError = true, EntryPoint = "renameat2")]
    private static extern int RenameAt2(
        int oldDirFd, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        int newDirFd, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, uint flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "statx")]
    private static extern int StatX(
        int dirFd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte[] buffer);

    [DllImport("libc", SetLastError = true, EntryPoint = "mkdir")]
    private static extern int MakeDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    /// <summary>The result of probing an output candidate leaf without following a final symlink.</summary>
    internal readonly record struct OutputTarget(bool Exists);

    /// <summary>
    /// Probes an output candidate leaf with the qualified identity syscall: whether it exists and,
    /// if so, that it is a regular file with a link count of exactly one, without following a final
    /// symlink. A multiply-linked or wrong-type existing entry is <c>PATH_ESCAPE</c>; an unavailable
    /// syscall fails closed as <c>PATH_ESCAPE</c>; a missing entry returns <c>Exists=false</c>. This
    /// is the output-boundary rule from the API contract, not a global alias search.
    /// </summary>
    internal static OutputTarget InspectOutputTarget(string path)
    {
        var buffer = new byte[StatxBufferBytes];
        int result;
        try
        {
            result = StatX(AtFdCwd, path, AtSymlinkNofollow, StatxBasicStats, buffer);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new NativePixelException(EngineErrors.PathEscape, "The output link-count identity probe is unavailable.");
        }
        if (result != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno == ENOENT) return new OutputTarget(false);
            // An unsupported syscall/filesystem makes the identity check impossible: fail closed.
            if (errno is ENOSYS or EINVAL)
                throw new NativePixelException(EngineErrors.PathEscape, "The output link-count identity probe is unavailable.");
            throw new NativePixelException(EngineErrors.StoreUnavailable, "The output path could not be inspected.");
        }
        // Fail closed if the backend did not actually report the type/mode/link-count fields.
        uint provided = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(StatxMaskOffset));
        if ((provided & StatxTypeModeNlink) != StatxTypeModeNlink)
            throw new NativePixelException(EngineErrors.PathEscape, "The output link-count identity probe returned incomplete fields.");
        uint links = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(StatxNlinkOffset));
        ushort mode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(StatxModeOffset));
        if ((mode & SIfmt) != SIfreg || links != 1)
            throw new NativePixelException(EngineErrors.PathEscape, "The output path is not an exclusive single-link regular file.");
        return new OutputTarget(true);
    }

    /// <summary>
    /// Acquires the stable sibling writer lock via an OS exclusive advisory lock
    /// (a <see cref="FileStream"/> opened with <see cref="FileShare.None"/>, which applies
    /// <c>flock(LOCK_EX)</c> on Linux). The wait is cancellation-aware with a five-second
    /// monotonic contention budget measured from the first attempt; expiry is <c>PIXEL_BUSY</c>
    /// with the fixed lock-wait message. The marker is never deleted on release.
    /// </summary>
    internal static FileStream AcquireWriterLock(string lockPath, NativeOperationScope scope)
    {
        InspectLockPath(lockPath, scope);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            scope.Check();
            try
            {
                InspectLockPath(lockPath, scope);
                var stream = new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.None);
                try
                {
                    InspectLockPath(lockPath, scope);
                    VerifyOpenedLock(stream, lockPath, scope);
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            // A sharing violation surfaces as a plain IOException; a genuinely missing path or a
            // permission failure is a distinct type and is not retried as contention.
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                if (timer.Elapsed >= LockWaitBudget)
                    throw new NativePixelException(NativePixelErrors.PixelBusy, NativePixelErrors.LockWaitExpiredMessage);
                scope.Caller.WaitHandle.WaitOne(15);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new NativePixelException(EngineErrors.StoreUnavailable, "The native writer lock could not be accessed.");
            }
        }
    }

    /// <summary>Reads up to <paramref name="maxBytes"/> and requires EOF; a longer file is <c>RESOURCE_LIMIT</c>.</summary>
    internal static byte[] ReadMetadata(string path, int maxBytes, NativeOperationScope scope)
    {
        scope.Reserve(maxBytes);
        using var stream = OpenRead(path);
        var buffer = new byte[maxBytes];
        int total = 0;
        while (total < maxBytes)
        {
            scope.Check();
            int read = stream.Read(buffer, total, Math.Min(65536, maxBytes - total));
            if (read == 0) break;
            total += read;
        }
        scope.Check();
        if (total == maxBytes && stream.ReadByte() != -1)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Native metadata exceeds its byte limit.");
        return total == maxBytes ? buffer : buffer[..total];
    }

    /// <summary>
    /// Reads exactly <paramref name="expectedLength"/> bytes and requires EOF. A short or longer
    /// file (a trailing byte) is <c>PIXEL_CONTENT_MISMATCH</c>. Reads in at most 64 KiB chunks.
    /// </summary>
    internal static byte[] ReadPayload(string path, int expectedLength, NativeOperationScope scope)
    {
        scope.Reserve(expectedLength);
        using var stream = OpenRead(path);
        var buffer = new byte[expectedLength];
        int total = 0;
        while (total < expectedLength)
        {
            scope.Check();
            int read = stream.Read(buffer, total, Math.Min(65536, expectedLength - total));
            if (read == 0)
                throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "A referenced payload is truncated.");
            total += read;
        }
        scope.Check();
        if (stream.ReadByte() != -1)
            throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "A referenced payload has trailing bytes.");
        return buffer;
    }

    /// <summary>
    /// Captures an existing candidate PNG once into private bounded bytes from a regular non-reparse
    /// file, reading in at most 64 KiB chunks. A file larger than <paramref name="maxBytes"/> is
    /// <c>RESOURCE_LIMIT</c>; the buffer is used verbatim for structure, decode and hashing.
    /// </summary>
    internal static byte[] ReadCandidatePng(string path, int maxBytes, NativeOperationScope scope)
    {
        scope.Reserve(maxBytes);
        RejectReparse(path);
        using var stream = OpenRead(path);
        var buffer = new byte[maxBytes];
        int total = 0;
        while (total < maxBytes)
        {
            scope.Check();
            int read = stream.Read(buffer, total, Math.Min(65536, maxBytes - total));
            if (read == 0) break;
            total += read;
        }
        scope.Check();
        if (total == maxBytes && stream.ReadByte() != -1)
            throw new NativePixelException(EngineErrors.ResourceLimit, "The candidate PNG exceeds its byte limit.");
        return total == maxBytes ? buffer : buffer[..total];
    }

    /// <summary>Writes a complete owned file in private staging, then flushes it to disk.</summary>
    internal static void WriteStagedFile(string path, ReadOnlySpan<byte> bytes, NativeOperationScope scope)
    {
        using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 65536, FileOptions.WriteThrough);
        for (int offset = 0; offset < bytes.Length; offset += 65536)
        {
            scope.Check();
            stream.Write(bytes.Slice(offset, Math.Min(65536, bytes.Length - offset)));
        }
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Creates a fresh private directory, failing if any entry already occupies it.</summary>
    internal static void CreatePrivateDirectory(string path)
    {
        int result;
        try { result = MakeDirectory(path, 0x1c0); } // 0700; atomic exclusive creation, never reuse an occupied path.
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw PlatformUnsupported();
        }
        if (result == 0) return;
        if (Marshal.GetLastPInvokeError() == EEXIST)
            throw new NativePixelException(EngineErrors.StoreUnavailable, "Native staging directory already exists.");
        throw new NativePixelException(EngineErrors.StoreUnavailable, "Native staging directory could not be created.");
    }

    private static void InspectLockPath(string path, NativeOperationScope scope)
    {
        scope.Check();
        try { AssetPaths.Inspect(path, false, required: false, PlatformCapability.NativePixels, scope.Caller); }
        catch (DocumentEngineException ex)
        {
            throw new NativePixelException(ex.Code, ex.Code == NativePixelErrors.AssetPlatformUnsupported
                ? ex.Message : "The native writer lock path has an unsafe or unavailable entry type.");
        }
    }

    private static void VerifyOpenedLock(FileStream stream, string path, NativeOperationScope scope)
    {
        scope.Reserve(512);
        byte[] opened = new byte[StatxBufferBytes], named = new byte[StatxBufferBytes];
        int first, second;
        try
        {
            first = StatX(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), "", AtEmptyPath,
                StatxBasicStats, opened);
            second = StatX(AtFdCwd, path, AtSymlinkNofollow, StatxBasicStats, named);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new NativePixelException(EngineErrors.PathEscape, "The native lock identity probe is unavailable.");
        }
        if (first != 0 || second != 0)
            throw new NativePixelException(EngineErrors.PathEscape, "The native lock identity could not be established.");
        uint required = StatxTypeModeNlink | StatxInode;
        foreach (byte[] status in new[] { opened, named })
            if ((BinaryPrimitives.ReadUInt32LittleEndian(status) & required) != required
                || (BinaryPrimitives.ReadUInt16LittleEndian(status.AsSpan(StatxModeOffset)) & SIfmt) != SIfreg
                || BinaryPrimitives.ReadUInt32LittleEndian(status.AsSpan(StatxNlinkOffset)) != 1)
                throw new NativePixelException(EngineErrors.PathEscape, "The native writer lock is not a single-link regular file.");
        // Linux statx ABI: inode @32, filesystem device major/minor @136/140.
        if (!opened.AsSpan(32, 8).SequenceEqual(named.AsSpan(32, 8))
            || !opened.AsSpan(136, 8).SequenceEqual(named.AsSpan(136, 8)))
            throw new NativePixelException(EngineErrors.PathEscape, "The opened writer lock differs from its named entry.");
        scope.Check();
    }

    /// <summary>
    /// Publishes a whole staged directory to the target with the atomic no-replace primitive.
    /// An occupied target (including one that appeared between check and publication) is
    /// <c>PIXEL_ALREADY_EXISTS</c>; a missing primitive or filesystem is
    /// <c>ASSET_PLATFORM_UNSUPPORTED</c> with no fallback.
    /// </summary>
    internal static void PublishDirectoryNoReplace(string stagingPath, string targetPath)
    {
        int result;
        try
        {
            result = RenameAt2(AtFdCwd, stagingPath, AtFdCwd, targetPath, RenameNoReplace);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw PlatformUnsupported();
        }
        if (result == 0) return;

        int errno = Marshal.GetLastPInvokeError();
        throw errno switch
        {
            EEXIST or ENOTEMPTY => new NativePixelException(
                NativePixelErrors.PixelAlreadyExists, "A directory entry already occupies the create target."),
            EINVAL or ENOSYS => PlatformUnsupported(),
            ENOENT => new NativePixelException(EngineErrors.StoreUnavailable, "Native staging or parent directory disappeared."),
            _ => new NativePixelException(EngineErrors.StoreUnavailable, "Native asset publication failed."),
        };
    }

    /// <summary>
    /// Publishes one complete owned temporary file to a content-addressed payload name with the
    /// atomic no-replace primitive. Returns <c>true</c> when this call published the object and
    /// <c>false</c> when the addressed name already exists (the caller must then verify and
    /// byte-compare the existing object and reuse only an exact match). A missing primitive or
    /// filesystem is <c>ASSET_PLATFORM_UNSUPPORTED</c> with no fallback.
    /// </summary>
    internal static bool PublishFileNoReplace(string tempPath, string targetPath)
    {
        int result;
        try
        {
            result = RenameAt2(AtFdCwd, tempPath, AtFdCwd, targetPath, RenameNoReplace);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw PlatformUnsupported();
        }
        if (result == 0) return true;

        int errno = Marshal.GetLastPInvokeError();
        return errno switch
        {
            EEXIST or ENOTEMPTY => false,
            EINVAL or ENOSYS => throw PlatformUnsupported(),
            ENOENT => throw new NativePixelException(EngineErrors.StoreUnavailable, "Native payload staging or asset directory disappeared."),
            _ => throw new NativePixelException(EngineErrors.StoreUnavailable, "Native payload publication failed."),
        };
    }

    /// <summary>
    /// Atomically replaces the metadata file with a complete owned temporary, with no
    /// delete-then-move visibility gap (libc <c>rename</c> semantics via <c>renameat2</c> with no
    /// flags). This is the single edit visibility point. A definite errno failure means the target
    /// still holds the prior metadata (a safe failure, not an ambiguous outcome).
    /// </summary>
    internal static void ReplaceFileAtomic(string tempPath, string targetPath)
    {
        int result;
        try
        {
            result = RenameAt2(AtFdCwd, tempPath, AtFdCwd, targetPath, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw PlatformUnsupported();
        }
        if (result == 0) return;

        int errno = Marshal.GetLastPInvokeError();
        throw errno switch
        {
            EINVAL or ENOSYS => PlatformUnsupported(),
            ENOENT => new NativePixelException(EngineErrors.StoreUnavailable, "Native metadata temporary or asset directory disappeared."),
            _ => new NativePixelException(EngineErrors.StoreUnavailable, "Native metadata replacement failed."),
        };
    }

    /// <summary>
    /// Captures a replacement input file once: exactly <paramref name="expectedLength"/> raw
    /// tightly packed samples with required EOF, from a regular non-reparse file. A short, excess,
    /// empty or growing capture is <c>INVALID_ARGUMENTS</c>. Never framed, decoded or sniffed.
    /// </summary>
    internal static byte[] ReadRawInput(string path, int expectedLength, NativeOperationScope scope)
    {
        scope.Reserve(expectedLength);
        RejectReparse(path);
        using var stream = OpenRead(path);
        var buffer = new byte[expectedLength];
        int total = 0;
        while (total < expectedLength)
        {
            scope.Check();
            int read = stream.Read(buffer, total, Math.Min(65536, expectedLength - total));
            if (read == 0)
                throw new NativePixelException(EngineErrors.InvalidArguments, "A replacement input is shorter than its rectangle.");
            total += read;
        }
        scope.Check();
        if (stream.ReadByte() != -1)
            throw new NativePixelException(EngineErrors.InvalidArguments, "A replacement input is larger than its rectangle.");
        return buffer;
    }

    /// <summary>Best-effort removal of one owned, unpublished temporary file. Never touches a published object.</summary>
    internal static void TryRemoveFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup failure must never conceal the primary write outcome.
        }
    }

    /// <summary>Best-effort removal of an owned, unpublished staging directory. Never touches the target.</summary>
    internal static void TryRemoveStaging(string stagingPath)
    {
        try
        {
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup failure must never conceal the primary write outcome.
        }
    }

    private static FileStream OpenRead(string path) => new(
        path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, bufferSize: 65536, FileOptions.SequentialScan);

    private static void RejectReparse(string path)
    {
        if (AtomicDocumentFile.IsReparsePoint(path))
            throw new NativePixelException(EngineErrors.PathEscape, "The writer lock path is not a regular file.");
    }

    private static NativePixelException PlatformUnsupported() => new(
        NativePixelErrors.AssetPlatformUnsupported, "Atomic no-replace directory publication is unavailable.");
}
