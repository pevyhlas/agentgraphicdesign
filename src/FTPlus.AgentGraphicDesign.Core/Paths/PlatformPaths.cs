using System.Security;

namespace FTPlus.AgentGraphicDesign.Core.Paths;

/// <summary>
/// Lexical full paths with only the three verified macOS system aliases expanded.
/// This does not replace the caller's confinement, file-type or strict-link inspection.
/// </summary>
public static class PlatformPaths
{
    private static readonly CanonicalPathResolver Resolver = new(OperatingSystem.IsMacOS(), ReadAliasMetadata);

    public static string FullPath(string path, string? basePath = null) => Resolver.FullPath(path, basePath);

    public static string RelativePath(string basePath, string path) =>
        Path.GetRelativePath(FullPath(basePath), FullPath(path));

    // Only existing inspected reference/font callers use the additional lexical precheck.
    internal static string FullPath(string path, string? basePath, Action<string> inspect) =>
        Resolver.FullPath(path, basePath, inspect);

    private static AliasMetadata? ReadAliasMetadata(string path)
    {
        // The PAL is a private runtime ABI. An unknown major never grants an alias.
        if (Environment.Version.Major != 10 || UnixFileMetadata.LStat(path, out var status) != 0)
            return null;
        var target = (status.Mode & UnixFileMetadata.TypeMask) == UnixFileMetadata.LinkType
            ? new FileInfo(path).LinkTarget : null;
        return new AliasMetadata(status.Mode, status.Uid, target);
    }

    internal readonly record struct AliasMetadata(int Mode, uint Uid, string? LinkTarget);

    /// <summary>Instance-only seams; none can replace the production resolver.</summary>
    internal sealed class CanonicalPathResolver
    {
        private readonly bool _isMacOs;
        private readonly Lazy<HashSet<string>> _aliases;
        private readonly Action<string, bool> _inspectPrefix;

        internal CanonicalPathResolver(bool isMacOs, Func<string, AliasMetadata?> metadata,
            Action<string, bool>? inspectPrefix = null)
        {
            _isMacOs = isMacOs;
            _inspectPrefix = inspectPrefix ?? InspectPrefix;
            _aliases = new Lazy<HashSet<string>>(() => new[] { "var", "tmp", "etc" }
                .Where(name => VerifyAlias(name, metadata)).ToHashSet(StringComparer.Ordinal));
        }

        internal string FullPath(string path, string? basePath = null)
        {
            var full = basePath is null ? Path.GetFullPath(path) : Path.GetFullPath(path, basePath);
            if (!_isMacOs)
                return full;
            foreach (var name in _aliases.Value)
            {
                var alias = "/" + name;
                if (full.Equals(alias, StringComparison.Ordinal) || full.StartsWith(alias + "/", StringComparison.Ordinal))
                    return "/private" + full;
            }
            return full;
        }

        internal string FullPath(string path, string? basePath, Action<string> inspect)
        {
            ArgumentNullException.ThrowIfNull(inspect);
            // Always compute identity from the original inputs. Rewriting before normalizing
            // would incorrectly turn /tmp/../file into /private/file instead of /file.
            var full = FullPath(path, basePath);
            var spelling = AbsoluteSpelling(path, basePath);
            var root = Path.GetPathRoot(spelling)!;
            var prefix = root;
            var parts = spelling[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                prefix = Path.Combine(prefix, parts[i]);
                // Keep prefix in its original spelling: each link is checked before a later
                // '..' can hide it, while dots crossing an alias retain lexical semantics.
                _inspectPrefix(FullPath(prefix), i != parts.Length - 1);
            }
            inspect(full);
            return full;
        }

        private static string AbsoluteSpelling(string path, string? basePath)
        {
            if (Path.IsPathFullyQualified(path))
                return path;
            var basis = basePath ?? Environment.CurrentDirectory;
            if (!Path.IsPathRooted(path))
                return Path.Combine(basis, path);
            // Windows root-relative or drive-relative spelling: resolve only the root, so
            // links in the remaining components cannot disappear through normalization.
            var root = Path.GetPathRoot(path)!;
            if (root.EndsWith(':') && root.Equals(Path.GetPathRoot(basis)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return Path.Combine(basis, path[root.Length..]);
            var resolvedRoot = basePath is null ? Path.GetFullPath(root) : Path.GetFullPath(root, basePath);
            return Path.Combine(resolvedRoot, path[root.Length..]);
        }

        private static bool VerifyAlias(string name, Func<string, AliasMetadata?> metadata)
        {
            try
            {
                var link = metadata("/" + name);
                if (link is not { Uid: 0 } || (link.Value.Mode & UnixFileMetadata.TypeMask) != UnixFileMetadata.LinkType ||
                    link.Value.LinkTarget != "private/" + name)
                    return false;
                var target = metadata("/private/" + name);
                return target is { Uid: 0 } && (target.Value.Mode & UnixFileMetadata.TypeMask) == UnixFileMetadata.DirectoryType;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or
                NotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return false;
            }
        }

        private static void InspectPrefix(string path, bool directory)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
                    throw Unsafe(path);
                throw new DocumentEngineException(EngineErrors.DocumentNotFound, $"Required path not found: {path}");
            }
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 ||
                directory && !attributes.HasFlag(FileAttributes.Directory))
                throw Unsafe(path);
        }

        private static DocumentEngineException Unsafe(string path) =>
            new(EngineErrors.PathEscape, $"Path must be free of symbolic links and invalid traversal components: {path}");
    }
}
