namespace FTPlus.AgentGraphicDesign.Core.Assets;

/// <summary>Paths are slash-separated and relative to the manifest directory.</summary>
public sealed record AssetFileResult(string Path, string Status);

/// <summary>Source and Output are slash-separated paths relative to the manifest directory.</summary>
public sealed record AssetPipelineDiagnostic(string Source, string Output, string Code, string Message);

public sealed record AssetPipelineResult(
    bool Check,
    IReadOnlyList<AssetFileResult> Files,
    IReadOnlyList<AssetPipelineDiagnostic> Diagnostics)
{
    public bool Success => Files.All(file => file.Status is "unchanged" or "written");
}
