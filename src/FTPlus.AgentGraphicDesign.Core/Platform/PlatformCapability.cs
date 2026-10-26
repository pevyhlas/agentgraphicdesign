namespace FTPlus.AgentGraphicDesign.Core.Platform;

/// <summary>Platform-sensitive capabilities, in stable wire-array order (D-28).</summary>
public enum PlatformCapability { Vector, RasterExport, AssetPipeline, ArtifactVerify, StillImport, NativePixels }

/// <summary>Declaration order is the narrowing order; later values are never more permissive.</summary>
public enum CapabilityQualification { Qualified, Unqualified, Refused }

public sealed record CapabilityPolicy(PlatformCapability Capability, CapabilityQualification Qualification,
    IReadOnlyDictionary<string, string> Requires);

public sealed record QualifiedProfile(string Profile, string? MinOsVersion,
    IReadOnlyList<PlatformCapability> Capabilities,
    IReadOnlyDictionary<PlatformCapability, IReadOnlyDictionary<string, string>> Requires);

/// <summary>Stable wire vocabulary. Renaming a value is a contract cutover.</summary>
public static class PlatformWireNames
{
    public const string FilesystemRequirement = "filesystem";
    public const string Apfs = "apfs";

    public static string Name(PlatformCapability capability) => capability switch
    {
        PlatformCapability.Vector => "vector",
        PlatformCapability.RasterExport => "rasterExport",
        PlatformCapability.AssetPipeline => "assetPipeline",
        PlatformCapability.ArtifactVerify => "artifactVerify",
        PlatformCapability.StillImport => "stillImport",
        PlatformCapability.NativePixels => "nativePixels",
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    public static string Name(CapabilityQualification qualification) => qualification switch
    {
        CapabilityQualification.Qualified => "qualified",
        CapabilityQualification.Unqualified => "unqualified",
        CapabilityQualification.Refused => "refused",
        _ => throw new ArgumentOutOfRangeException(nameof(qualification))
    };
}
