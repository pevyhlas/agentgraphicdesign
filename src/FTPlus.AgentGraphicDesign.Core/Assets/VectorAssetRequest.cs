using System.Collections.Immutable;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

public enum VectorAssetFormat
{
    Svg,
    Png,
    Jpeg,
    Webp
}

/// <summary>
/// Effective vector options, with no publication authority. Direct callers supply a
/// loader-equivalent structurally bounded, acyclic document and at most 16 vetted,
/// nonblank font directories, and keep all inputs stable during production.
/// Semantic validation is performed by the producer after charging nodes.
/// </summary>
public sealed record VectorAssetRequest
{
    public required Document Document { get; init; }
    public required string SourceName { get; init; }
    public VectorAssetFormat Format { get; init; } = VectorAssetFormat.Svg;
    public bool AsSymbol { get; init; }
    public bool Strict { get; init; } = true;
    public ValidationProfile Profile { get; init; } = ValidationProfile.Default;
    public string? CurrentColor { get; init; }
    public double Scale { get; init; } = 1;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public ImmutableArray<string> FontDirectories { get; init; } = [];
    public bool SystemFonts { get; init; }
    public bool StrictFonts { get; init; } = true;
}
