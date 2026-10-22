using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using SkiaSharp;

namespace FTPlus.AgentGraphicDesign.Skia.TextShaping;

/// <summary>
/// Bounded index of the font files in caller-supplied directories. Reads only the
/// directory itself (no recursion), ignores symbolic links, skips files above
/// <see cref="FontResolutionOptions.MaxFontFileBytes"/>, and refuses a directory holding
/// more than <see cref="FontResolutionOptions.MaxFilesPerDirectory"/> font files. Files
/// are indexed in ordinal name order so matching is deterministic.
/// </summary>
internal sealed class FontDirectoryCatalog : IDisposable
{
    public const int MaxFilesPerDirectory = FontResolutionOptions.MaxFilesPerDirectory;
    public const long MaxFontFileBytes = FontResolutionOptions.MaxFontFileBytes;
    public const int MaxSkipDiagnostics = 32;

    private static readonly string[] FontExtensions = [".ttf", ".otf"];

    private readonly List<Face> _faces = [];
    private readonly List<RenderDiagnostic> _diagnostics = [];
    private bool _disposed;

    private FontDirectoryCatalog()
    {
    }

    public IReadOnlyList<RenderDiagnostic> Diagnostics => _diagnostics;

    public int FaceCount => _faces.Count;

    public static FontDirectoryCatalog Load(IReadOnlyList<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        if (directories.Count > FontResolutionOptions.MaxFontDirectories)
        {
            throw new DocumentEngineException(EngineErrors.ResourceLimit,
                $"At most {FontResolutionOptions.MaxFontDirectories} font directories are allowed");
        }

        var catalog = new FontDirectoryCatalog();
        try
        {
            foreach (var directory in directories)
            {
                catalog.LoadDirectory(directory);
            }
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
        return catalog;
    }

    /// <summary>
    /// Finds the face whose family equals <paramref name="family"/> (case-insensitive) with
    /// the weight nearest to <paramref name="weight"/>; upright faces win ties, then the
    /// lighter weight, then the ordinal file name.
    /// </summary>
    public Face? Match(string family, int weight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Face? best = null;
        foreach (var face in _faces)
        {
            if (!string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || Compare(face, best, weight) < 0)
                best = face;
        }
        return best;
    }

    private static int Compare(Face candidate, Face incumbent, int weight)
    {
        var distance = Math.Abs(candidate.Weight - weight).CompareTo(Math.Abs(incumbent.Weight - weight));
        if (distance != 0)
            return distance;
        var slant = candidate.Italic.CompareTo(incumbent.Italic);
        if (slant != 0)
            return slant;
        var lighter = candidate.Weight.CompareTo(incumbent.Weight);
        return lighter != 0 ? lighter : string.CompareOrdinal(candidate.Path, incumbent.Path);
    }

    private void LoadDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Font directory path cannot be empty", nameof(directory));
        var fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"Font directory not found: {directory}", nameof(directory));
        if (File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            throw new ArgumentException($"Font directory cannot be a symbolic link or reparse point: {directory}", nameof(directory));

        var candidates = new DirectoryInfo(fullPath)
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => FontExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count > MaxFilesPerDirectory)
        {
            throw new DocumentEngineException(EngineErrors.ResourceLimit,
                $"Font directory contains more than {MaxFilesPerDirectory} font files: {directory}");
        }

        foreach (var file in candidates)
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Skip(file, "it is a symbolic link or reparse point");
                continue;
            }
            if (file.Length > MaxFontFileBytes)
            {
                Skip(file, $"it exceeds {MaxFontFileBytes} bytes");
                continue;
            }

            var typeface = SKTypeface.FromFile(file.FullName);
            if (typeface is null || string.IsNullOrEmpty(typeface.FamilyName))
            {
                typeface?.Dispose();
                Skip(file, "it is not a readable TrueType/OpenType font");
                continue;
            }

            _faces.Add(new Face(file.FullName, typeface.FamilyName, typeface.FontWeight, typeface.IsItalic, typeface));
        }
    }

    private void Skip(FileInfo file, string reason)
    {
        if (_diagnostics.Count == MaxSkipDiagnostics)
        {
            _diagnostics.Add(new RenderDiagnostic(RenderDiagnosticCodes.DiagnosticsTruncated,
                $"Further skipped font files are not reported after {MaxSkipDiagnostics} entries"));
            return;
        }
        if (_diagnostics.Count > MaxSkipDiagnostics)
            return;
        _diagnostics.Add(new RenderDiagnostic(RenderDiagnosticCodes.FontFileSkipped,
            $"Font file '{file.Name}' was ignored because {reason}"));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var face in _faces)
            face.Typeface.Dispose();
        _faces.Clear();
    }

    internal sealed record Face(string Path, string FamilyName, int Weight, bool Italic, SKTypeface Typeface);
}
