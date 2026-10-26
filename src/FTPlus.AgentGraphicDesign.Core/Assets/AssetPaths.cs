using System.Runtime.InteropServices;
using FTPlus.AgentGraphicDesign.Core.Platform;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

internal static class AssetPaths
{
    internal const int MaxInventoryEntries = 4096;

    internal static string Resolve(string directory, string relative)
    {
        if (relative.Length > 1024 || Path.IsPathRooted(relative) ||
            relative.Any(c => char.IsControl(c) || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            throw Unsafe(relative);
        var parts = relative.Split('/');
        if (parts.Length > 64 || parts.Any(part => part is ".." ||
            part.Length > 0 && part != "." && (part.EndsWith('.') || part.EndsWith(' '))))
            throw Unsafe(relative);
        foreach (var part in parts)
        {
            var name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
                name[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3')
                throw Unsafe(relative);
        }
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(relative, directory));
        if (!Within(path, directory))
            throw Unsafe(relative);
        return path;
    }

    internal static bool Within(string path, string directory) =>
        path.StartsWith(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    internal static bool Overlaps(string first, string second) =>
        first.Equals(second, StringComparison.OrdinalIgnoreCase) || Within(first, second) || Within(second, first);

    internal static void RejectCaseAliases(string directory, string path, PlatformCapability capability, CancellationToken ct)
    {
        var parts = Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar);
        var current = directory;
        foreach (var part in parts)
        {
            if (!Inspect(current, directory: true, required: false, capability, ct))
                return;
            var count = 0;
            var found = false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > MaxInventoryEntries)
                    throw AssetManifest.Limit();
                var name = Path.GetFileName(entry);
                if (!name.Equals(part, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!name.Equals(part, StringComparison.Ordinal))
                    throw AssetManifest.Invalid($"On-disk case alias for asset output '{path}': {entry}");
                found = true;
            }
            if (!found)
                return;
            current = Path.Combine(current, part);
        }
    }

    /// <summary>Checks every component before opening anything, including broken links.</summary>
    internal static bool Inspect(string path, bool directory, bool required, PlatformCapability capability, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var platform = PlatformProfile.Current;
        if (!platform.GateAdmits(capability))
            throw platform.Refusal(capability);
        var root = Path.GetPathRoot(path)!;
        var current = root;
        var parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            current = Path.Combine(current, parts[index]);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // LinkTarget works for dangling links even on filesystems where Exists is false.
                if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
                    throw Unsafe(current);
                if (required)
                    throw new DocumentEngineException(EngineErrors.DocumentNotFound, $"Required asset path not found: {path}");
                return false;
            }
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw Unsafe(current);
            var wantsDirectory = index != parts.Length - 1 || directory;
            if (((attributes & FileAttributes.Directory) != 0) != wantsDirectory)
                throw Unsafe(current);
            if (!wantsDirectory && !OperatingSystem.IsWindows())
            {
                try
                {
                    if (LStat(current, out var status) != 0)
                        throw new IOException($"Cannot inspect asset file: {current}");
                    if ((status.Mode & 0xF000) != 0x8000)
                        throw Unsafe(current);
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    throw platform.Refusal(capability);
                }
            }
        }
        return true;
    }

    internal static List<string> Inventory(string root, PlatformCapability capability, CancellationToken ct)
    {
        var files = new List<string>();
        if (!Inspect(root, directory: true, required: false, capability, ct))
            return files;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var count = 0;
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > MaxInventoryEntries || directory.Depth >= 64)
                    throw AssetManifest.Limit();
                var isDirectory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
                Inspect(entry, isDirectory, required: true, capability, ct);
                if (isDirectory)
                    pending.Push((entry, directory.Depth + 1));
                else
                    files.Add(entry);
            }
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    internal static DocumentEngineException Unsafe(string path) =>
        new(EngineErrors.PathEscape, $"Asset path must be confined, free of symlinks/reparse points, and of the required file type: {path}");

    // Private PAL ABI behind the capability gate (S0: .NET 10 Linux x64; Windows never reaches it).
    // FileAttributes alone cannot distinguish Unix regular files from FIFOs/devices.
    [DllImport("System.Native", EntryPoint = "SystemNative_LStat", SetLastError = true)]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out UnixFileStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnixFileStatus
    {
        public int Flags;
        public int Mode;
        public uint Uid;
        public uint Gid;
        public long Size;
        public long ATime;
        public long ATimeNsec;
        public long MTime;
        public long MTimeNsec;
        public long CTime;
        public long CTimeNsec;
        public long BirthTime;
        public long BirthTimeNsec;
        public long Dev;
        public long RDev;
        public long Ino;
        public uint UserFlags;
    }
}
