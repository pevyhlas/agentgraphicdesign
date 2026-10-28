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

    private readonly List<Face> _faces = [];
    private readonly List<RenderDiagnostic> _diagnostics = [];
    private bool _disposed;

    private FontDirectoryCatalog()
    {
    }

    public IReadOnlyList<RenderDiagnostic> Diagnostics => _diagnostics;

    public int FaceCount => _faces.Count;

    public static FontDirectoryCatalog Load(IReadOnlyList<string> directories) =>
        FromInspected(InspectedFontSet.Inspect(directories, Directory.GetCurrentDirectory(), inspect: false, default));

    /// <summary>Consumes the counted set without enumeration. The optional loader is a test-only instance seam.</summary>
    internal static FontDirectoryCatalog FromInspected(InspectedFontSet fonts,
        CancellationToken cancellationToken = default, Func<string, SKTypeface?>? loadTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        cancellationToken.ThrowIfCancellationRequested();
        loadTypeface ??= path => SKTypeface.FromFile(path);
        var catalog = new FontDirectoryCatalog();
        try
        {
            foreach (var entry in fonts.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is FontEntry.Skip skip)
                {
                    catalog.Skip(entry.Path, skip.Reason == FontSkipReason.SymbolicLink
                        ? "it is a symbolic link or reparse point" : $"it exceeds {MaxFontFileBytes} bytes");
                    continue;
                }

                // Keep the last-component reparse recheck. This is not handle-based TOCTOU protection.
                if (File.GetAttributes(entry.Path).HasFlag(FileAttributes.ReparsePoint))
                {
                    catalog.Skip(entry.Path, "it is a symbolic link or reparse point");
                    continue;
                }
                var typeface = loadTypeface(entry.Path);
                try
                {
                    if (typeface is null || string.IsNullOrEmpty(typeface.FamilyName))
                    {
                        catalog.Skip(entry.Path, "it is not a readable TrueType/OpenType font");
                        continue;
                    }
                    catalog._faces.Add(new Face(entry.Path, typeface.FamilyName, typeface.FontWeight, typeface.IsItalic, typeface));
                    typeface = null; // Ownership transferred to the catalog.
                }
                finally
                {
                    typeface?.Dispose();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
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

    private void Skip(string path, string reason)
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
            $"Font file '{Path.GetFileName(path)}' was ignored because {reason}"));
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
