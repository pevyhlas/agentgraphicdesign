using System.Collections.Immutable;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>Ordered font candidates. Only Load entries contribute bytes or may later be opened.</summary>
public abstract record FontEntry(string Path)
{
    public sealed record Load(string FullPath, long Length) : FontEntry(FullPath);
    public sealed record Skip(string FullPath, FontSkipReason Reason) : FontEntry(FullPath);
}

public enum FontSkipReason { SymbolicLink, Oversize }

/// <summary>
/// Immutable result of the shared directory rules, not a user-constructed list of load paths.
/// Inspection assumes a trusted, quiescent filesystem; this is not a handle-based TOCTOU control.
/// </summary>
public sealed class InspectedFontSet
{
    private InspectedFontSet(ImmutableArray<string> directories, ImmutableArray<FontEntry> entries)
    {
        Directories = directories;
        Entries = entries;
        CountedBytes = entries.OfType<FontEntry.Load>().Sum(entry => entry.Length);
    }

    public ImmutableArray<string> Directories { get; }
    public ImmutableArray<FontEntry> Entries { get; }
    public long CountedBytes { get; }

    internal static InspectedFontSet Inspect(IReadOnlyList<string> directories, string baseDirectory,
        bool inspect, CancellationToken ct)
    {
        var resolved = FontDirectoryRules.ResolveDirectories(directories, baseDirectory);
        return new InspectedFontSet(resolved, FontDirectoryRules.Enumerate(resolved, inspect, ct));
    }
}

/// <summary>Catalog ordering/skip rules, independent of renderer and caller size/refusal policy.</summary>
internal static class FontDirectoryRules
{
    internal const int MaxFontDirectories = 16;
    internal const int MaxFilesPerDirectory = 256;
    internal const long MaxFontFileBytes = 8 * 1024 * 1024;

    internal static ImmutableArray<string> ResolveDirectories(IReadOnlyList<string> directories, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(directories);
        if (directories.Count > MaxFontDirectories)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "At most 16 font directories are allowed");
        return directories.Select(directory =>
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Font directory path cannot be empty", nameof(directories));
            // Preserve unnormalised components for the inspection pre-pass (link/.. must not hide a link).
            return Path.IsPathFullyQualified(directory) ? directory : Path.Combine(baseDirectory, directory);
        }).ToImmutableArray();
    }

    internal static ImmutableArray<FontEntry> Enumerate(ImmutableArray<string> directories, bool inspect, CancellationToken ct)
    {
        var entries = ImmutableArray.CreateBuilder<FontEntry>();
        foreach (var directory in directories)
        {
            ct.ThrowIfCancellationRequested();
            var inspected = inspect ? FontDirectoryInspection.Inspect(directory, ct) : null;
            if (!Directory.Exists(directory))
                throw new ArgumentException("Font directory not found", nameof(directories));
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Font directory cannot be a symbolic link or reparse point", nameof(directories));

            var candidates = new List<FileInfo>();
            var files = inspected is null
                ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                : inspected.Where(path => !File.GetAttributes(path).HasFlag(FileAttributes.Directory)).Select(path => new FileInfo(path));
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsFont(file.FullName))
                    continue;
                if (candidates.Count == MaxFilesPerDirectory)
                    throw new DocumentEngineException(EngineErrors.ResourceLimit, "Font directory contains more than 256 font files");
                candidates.Add(file);
            }
            candidates.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            foreach (var file in candidates)
            {
                ct.ThrowIfCancellationRequested();
                // Never query length through a link, including a dangling link.
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    entries.Add(new FontEntry.Skip(file.FullName, FontSkipReason.SymbolicLink));
                else if (file.Length > MaxFontFileBytes)
                    entries.Add(new FontEntry.Skip(file.FullName, FontSkipReason.Oversize));
                else
                    entries.Add(new FontEntry.Load(file.FullName, file.Length));
            }
        }
        return entries.ToImmutable();
    }

    internal static bool IsFont(string path) => Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase);
}
