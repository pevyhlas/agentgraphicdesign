using System.Text;
using FTPlus.AgentGraphicDesign.Core.Platform;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

/// <summary>
/// Managed asset collision keys are not I/O paths or a complete APFS collation implementation.
/// Existing names must also match the stored spelling. One instance belongs to one operation.
/// </summary>
internal sealed class AssetPathPolicy
{
    internal const int MaxScannedEntries = 65_536;
    internal static readonly StringComparer MacComparer = new DecomposedComparer();
    internal static StringComparer Comparer => OperatingSystem.IsMacOS() ? MacComparer : StringComparer.OrdinalIgnoreCase;
    private readonly bool _mac;
    private readonly Func<string, IEnumerable<string>> _enumerate;
    private readonly Func<string, bool> _exists;
    private readonly Action<string, CancellationToken> _inspectDirectory;
    private readonly Dictionary<string, string[]> _parents = new(StringComparer.Ordinal);
    private int _scanned;

    internal AssetPathPolicy() : this(OperatingSystem.IsMacOS(), Directory.EnumerateFileSystemEntries,
        Exists, (path, ct) => AssetPaths.Inspect(path, true, true, PlatformCapability.AssetPipeline, ct))
    { }

    internal AssetPathPolicy(bool mac, Func<string, IEnumerable<string>> enumerate,
        Func<string, bool> exists, Action<string, CancellationToken> inspectDirectory)
    {
        _mac = mac;
        _enumerate = enumerate;
        _exists = exists;
        _inspectDirectory = inspectDirectory;
    }

    internal void Check(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_mac) return;
        var parent = Path.GetPathRoot(path)!;
        foreach (var part in path[parent.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            if (!_parents.TryGetValue(parent, out var names))
            {
                _inspectDirectory(parent, ct);
                var captured = new List<string>();
                foreach (var entry in _enumerate(parent))
                {
                    ct.ThrowIfCancellationRequested();
                    if (captured.Count == AssetPaths.MaxInventoryEntries || ++_scanned > MaxScannedEntries)
                        throw AssetManifest.Limit();
                    captured.Add(Path.GetFileName(entry));
                }
                names = captured.ToArray();
                _parents.Add(parent, names);
            }
            var current = Path.Combine(parent, part);
            if (names.Any(name => !name.Equals(part, StringComparison.Ordinal) && MacComparer.Equals(name, part)))
                throw AssetPaths.Unsafe(current);
            var exact = names.Contains(part, StringComparer.Ordinal);
            // OS resolution catches existing aliases outside the conservative comparison key too.
            if (!exact)
            {
                if (_exists(current)) throw AssetPaths.Unsafe(current);
                return;
            }
            parent = current;
        }
    }

    // AGD's mkdir/rename changes parent inventories, even in an otherwise quiescent workspace.
    internal void Invalidate() => _parents.Clear();

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    internal static string Key(string path) => OperatingSystem.IsMacOS() ? path.Normalize(NormalizationForm.FormD) : path;

    private sealed class DecomposedComparer : StringComparer
    {
        private static string? Normalize(string? value) => value?.Normalize(NormalizationForm.FormD);
        public override int Compare(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Compare(Normalize(x), Normalize(y));
        public override bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(Normalize(x), Normalize(y));
        public override int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(obj)!);
    }
}
