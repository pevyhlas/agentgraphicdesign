namespace FTPlus.AgentGraphicDesign.Core.Platform;

/// <summary>Read-only live availability contract. Implementation remains deferred to 008/T019.</summary>
public interface IPlatformAvailabilityProbe
{
    AvailabilityReport Probe(PlatformCapability capability, string root, CancellationToken cancellationToken);
}

/// <summary>Available is null only for vector/rasterExport; reason and facts use the stable vocabulary.</summary>
public sealed record AvailabilityReport(PlatformCapability Capability, bool? Available, string? Reason,
    IReadOnlyDictionary<string, string?> Facts);

public static class AvailabilityReasons
{
    public const string GateRefused = "GATE_REFUSED";
    public const string FilesystemUnqualified = "FILESYSTEM_UNQUALIFIED";
    public const string FileLockingDisabled = "FILE_LOCKING_DISABLED";
    public const string RootUnavailable = "ROOT_UNAVAILABLE";
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[] { GateRefused, FilesystemUnqualified, FileLockingDisabled, RootUnavailable });
}

public static class AvailabilityFacts
{
    public const string Filesystem = "filesystem";
    public const string CaseSensitive = "caseSensitive";
    public const string FileLocking = "fileLocking";
    public const string RootCanonical = "rootCanonical";
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[] { Filesystem, CaseSensitive, FileLocking, RootCanonical });
}
