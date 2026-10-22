using System.Collections.Immutable;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Artifacts;

/// <summary>
/// Immutable, unverified metadata claims for a still or one clip/view/variant tuple.
/// Metadata validity does not establish file existence, image identity, rights or admission.
/// </summary>
public sealed record ProvidedArtifactDescriptor
{
    public required string Schema { get; init; }
    public required long Version { get; init; }
    public required string Profile { get; init; }
    public required string AssetId { get; init; }
    public required ArtifactCanvas Canvas { get; init; }
    public required ArtifactCropOrigin CropOrigin { get; init; }
    public required ImmutableArray<ArtifactFileClaim> Files { get; init; }
    public ArtifactStill? Still { get; init; }
    public ArtifactClip? Clip { get; init; }
    public ArtifactProvenance? Provenance { get; init; }
}

/// <summary>Claimed fixed canvas and edge-space coordinate convention.</summary>
public sealed record ArtifactCanvas(long Width, long Height, string Coordinates);

/// <summary>Claimed crop origin; both coordinates must be zero in this profile.</summary>
public sealed record ArtifactCropOrigin(long X, long Y);

/// <summary>A portable lexical path and unverified payload hash/length claims; never opened.</summary>
public sealed record ArtifactFileClaim(string Sha256, string Path, long ByteLength);

/// <summary>A still's logical name, claimed file reference and continuous edge-space pivot.</summary>
public sealed record ArtifactStill(string Name, string FileSha256, ArtifactPivot Pivot);

/// <summary>Exact tick timing and ordered logical frames, not an animation evaluator.</summary>
public sealed record ArtifactClip(
    string Id, string ViewId, string VariantId, long TicksPerSecond, string Playback,
    ImmutableArray<ArtifactFrame> Frames);

/// <summary>A logical frame; repeated file references do not imply linked native cels.</summary>
public sealed record ArtifactFrame(string Name, string FileSha256, long DurationTicks, ArtifactPivot Pivot);

/// <summary>Exact decimal coordinates in the untrimmed canvas, possibly outside it.</summary>
public sealed record ArtifactPivot(decimal X, decimal Y);

/// <summary>Optional unverified provenance, not execution authority or source closure.</summary>
public sealed record ArtifactProvenance
{
    public ArtifactProducer? Producer { get; init; }
    public string? RecipeSnapshotSha256 { get; init; }
}

/// <summary>Producer identity/version as inert data, not a registered or executable producer.</summary>
public sealed record ArtifactProducer(string Id, string Version);

/// <summary>
/// Either an owned immutable descriptor passing all metadata checks (including canonical size),
/// or the first bounded error. Success does not verify any image or asset identity.
/// </summary>
public sealed class ArtifactDescriptorParseResult
{
    internal ArtifactDescriptorParseResult(ProvidedArtifactDescriptor? descriptor, ValidationError? error)
    {
        Descriptor = descriptor;
        Error = error;
    }

    public ProvidedArtifactDescriptor? Descriptor { get; }
    public ValidationError? Error { get; }
    public bool Success => Descriptor is not null && Error is null;
}
