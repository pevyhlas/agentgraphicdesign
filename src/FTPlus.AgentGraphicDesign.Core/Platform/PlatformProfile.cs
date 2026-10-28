using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Platform;

/// <summary>The I/O-free D-28 S1 registry. No filesystem, native calls or live availability probes.</summary>
public sealed class PlatformProfile
{
    private const CapabilityQualification Q = CapabilityQualification.Qualified;
    private const CapabilityQualification U = CapabilityQualification.Unqualified;
    private const CapabilityQualification R = CapabilityQualification.Refused;
    private static readonly IReadOnlyDictionary<string, string> NoRequirements =
        ReadOnlyDictionary<string, string>.Empty;
    private static readonly RegistryRow[] Rows =
    [
        new("linux", "x64", 10, null, [Q, Q, Q, Q, Q, Q]),
        new("osx", "arm64", 10, new Version(26, 0), [U, U, U, U, R, R]),
        new("windows", "x64", 10, null, [U, U, U, U, R, R]),
    ];
    private static readonly CapabilityQualification[] FallbackRow = [U, U, R, R, R, R];
    private static readonly AsyncLocal<IReadOnlyDictionary<PlatformCapability, CapabilityQualification>?> Narrowing = new();
    private static readonly Lazy<PlatformProfile> CurrentProfile = new(() => For(
        HostOs(), RuntimeInformation.ProcessArchitecture, Environment.Version, Environment.OSVersion.Version));
    private readonly CapabilityPolicy[] _policies;

    private PlatformProfile(string os, Architecture architecture, Version runtime, Version osVersion,
        CapabilityPolicy[] policies, Version? floor)
    {
        Os = os;
        Architecture = ArchitectureName(architecture);
        RuntimeVersion = FormattableString.Invariant($"{runtime.Major}.{runtime.Minor}.{Math.Max(0, runtime.Build)}");
        OsVersion = FormattableString.Invariant($"{osVersion.Major}.{osVersion.Minor}");
        Profile = FormattableString.Invariant($"dotnet{runtime.Major}-{os}-{Architecture}");
        MatchedOsFloor = floor is null ? null : FormattableString.Invariant($"{floor.Major}.{floor.Minor}");
        _policies = policies;
    }

    /// <summary>Computed once from process (not OS) architecture; Rosetta x64 remains x64.</summary>
    public static PlatformProfile Current => CurrentProfile.Value;
    public string Profile { get; }
    public string Os { get; }
    public string Architecture { get; }
    /// <summary>Major.minor; kernel version on Linux.</summary>
    public string OsVersion { get; }
    public string RuntimeVersion { get; }
    internal string? MatchedOsFloor { get; }
    public IReadOnlyList<CapabilityPolicy> Capabilities => Enum.GetValues<PlatformCapability>().Select(Policy).ToArray();
    public static IReadOnlyList<QualifiedProfile> QualifiedProfiles { get; } = Array.AsReadOnly(new[]
    {
        new QualifiedProfile("dotnet10-linux-x64", null, Array.AsReadOnly(Enum.GetValues<PlatformCapability>()),
            ReadOnlyDictionary<PlatformCapability, IReadOnlyDictionary<string, string>>.Empty)
    });

    public static PlatformProfile For(OSPlatform os, Architecture processArchitecture, Version runtimeVersion, Version osVersion)
    {
        ArgumentNullException.ThrowIfNull(runtimeVersion);
        ArgumentNullException.ThrowIfNull(osVersion);
        var osName = OsName(os);
        var arch = ArchitectureName(processArchitecture);
        var values = FallbackRow;
        Version? floor = null;
        foreach (var row in Rows)
        {
            if (row.Os == osName && row.Architecture == arch && row.RuntimeMajor == runtimeVersion.Major
                && (row.MinOsVersion is null || new Version(osVersion.Major, osVersion.Minor) >= row.MinOsVersion))
            {
                values = row.Values;
                floor = row.MinOsVersion;
                break;
            }
        }
        var policies = values.Select((value, index) => new CapabilityPolicy((PlatformCapability)index, value, NoRequirements)).ToArray();
        return new PlatformProfile(osName, processArchitecture, runtimeVersion, osVersion, policies, floor);
    }

    public CapabilityPolicy Policy(PlatformCapability capability)
    {
        if ((uint)capability >= (uint)_policies.Length) throw new ArgumentOutOfRangeException(nameof(capability));
        var policy = _policies[(int)capability];
        if (ReferenceEquals(this, CurrentProfile.Value) && Narrowing.Value is { } narrowed
            && narrowed.TryGetValue(capability, out var lowered) && lowered > policy.Qualification)
            return new CapabilityPolicy(capability, lowered, lowered == R ? NoRequirements : policy.Requires);
        return policy;
    }

    public bool GateAdmits(PlatformCapability capability) => Policy(capability).Qualification != R;

    /// <summary>Path-free registry text, preserved unchanged by every surface and storage wrapper.</summary>
    public string RefusalMessage(PlatformCapability capability)
    {
        var name = PlatformWireNames.Name(capability);
        var text = new StringBuilder().Append("Capability ").Append(name).Append(" is unavailable on platform ")
            .Append(Profile).Append(" (OS version ").Append(OsVersion).Append(", runtime ").Append(RuntimeVersion)
            .Append("). Qualified profiles for ").Append(name).Append(": ");
        var listed = 0;
        foreach (var qualified in QualifiedProfiles.Where(p => p.Capabilities.Contains(capability)))
        {
            if (listed++ > 0) text.Append("; ");
            text.Append(qualified.Profile);
            if (qualified.MinOsVersion is not null) text.Append(" on OS version ").Append(qualified.MinOsVersion).Append(" or later");
            if (qualified.Requires.TryGetValue(capability, out var requires) && requires.Count > 0)
                text.Append(" (requires ").AppendJoin(", ", requires.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => p.Key + " " + p.Value)).Append(')');
        }
        if (listed == 0) text.Append("none");
        return text.Append('.').ToString();
    }

    public DocumentEngineException Refusal(PlatformCapability capability) =>
        new(EngineErrors.AssetPlatformUnsupported, RefusalMessage(capability));

    /// <summary>Ordinal object keys at every level, enum/table array order, fresh JSON on every call.</summary>
    public JsonObject ToCapabilitiesJson()
    {
        var capabilities = new JsonArray();
        foreach (var policy in Capabilities)
            capabilities.Add(new JsonObject
            {
                ["capability"] = PlatformWireNames.Name(policy.Capability),
                ["qualification"] = PlatformWireNames.Name(policy.Qualification),
                ["requires"] = SortedObject(policy.Requires)
            });
        var qualifiedProfiles = new JsonArray();
        foreach (var qualified in QualifiedProfiles)
        {
            var requires = new JsonObject();
            foreach (var pair in qualified.Requires.Where(p => p.Value.Count > 0)
                .OrderBy(p => PlatformWireNames.Name(p.Key), StringComparer.Ordinal))
                requires[PlatformWireNames.Name(pair.Key)] = SortedObject(pair.Value);
            qualifiedProfiles.Add(new JsonObject
            {
                ["capabilities"] = new JsonArray(qualified.Capabilities.Select(c => (JsonNode?)JsonValue.Create(PlatformWireNames.Name(c))).ToArray()),
                ["minOsVersion"] = qualified.MinOsVersion,
                ["profile"] = qualified.Profile,
                ["requires"] = requires
            });
        }
        return new JsonObject
        {
            ["architecture"] = Architecture,
            ["capabilities"] = capabilities,
            ["os"] = Os,
            ["osVersion"] = OsVersion,
            ["profile"] = Profile,
            ["qualifiedProfiles"] = qualifiedProfiles,
            ["runtimeVersion"] = RuntimeVersion
        };
    }

    /// <summary>Async-flow-local, internal test seam; never admits more than the registry.</summary>
    internal static IDisposable NarrowForTests(PlatformCapability capability, CapabilityQualification qualification)
    {
        _ = Current.Policy(capability);
        _ = PlatformWireNames.Name(qualification);
        var previous = Narrowing.Value;
        var next = previous is null ? new Dictionary<PlatformCapability, CapabilityQualification>() : new(previous);
        next[capability] = next.TryGetValue(capability, out var existing) && existing > qualification ? existing : qualification;
        Narrowing.Value = next;
        return new NarrowingScope(previous);
    }

    private static JsonObject SortedObject(IReadOnlyDictionary<string, string> values)
    {
        var result = new JsonObject();
        foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal)) result[pair.Key] = pair.Value;
        return result;
    }

    private static string ArchitectureName(Architecture architecture) => architecture.ToString().ToLowerInvariant();
    private static string OsName(OSPlatform os) => os == OSPlatform.Linux ? "linux" : os == OSPlatform.OSX ? "osx"
        : os == OSPlatform.Windows ? "windows" : os == OSPlatform.FreeBSD ? "freebsd" : "unknown";
    private static OSPlatform HostOs() => OperatingSystem.IsLinux() ? OSPlatform.Linux : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsFreeBSD() ? OSPlatform.FreeBSD : OSPlatform.Create("UNKNOWN");
    private sealed record RegistryRow(string Os, string Architecture, int RuntimeMajor, Version? MinOsVersion, CapabilityQualification[] Values);
    private sealed class NarrowingScope(IReadOnlyDictionary<PlatformCapability, CapabilityQualification>? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Narrowing.Value = previous;
        }
    }
}
