using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Platform;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>Shared, bounded pre-pass. Does not open files or decide the caller's size policy.</summary>
internal static class FontDirectoryInspection
{
    internal static IReadOnlyList<string> Inspect(string directory, CancellationToken ct,
        Func<string, IEnumerable<string>>? enumerate = null)
    {
        AssetPaths.Inspect(directory, directory: true, required: true, PlatformCapability.AssetPipeline, ct);
        var entries = new List<string>();
        foreach (var path in (enumerate ?? Directory.EnumerateFileSystemEntries)(directory))
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count == FontDirectoryRules.MaxFilesPerDirectory)
                throw new DocumentEngineException(EngineErrors.ResourceLimit, "Font directory contains more than 256 entries");
            var isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            AssetPaths.Inspect(path, isDirectory, required: true, PlatformCapability.AssetPipeline, ct);
            entries.Add(path);
        }
        return entries;
    }
}
