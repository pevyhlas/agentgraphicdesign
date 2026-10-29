using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Platform;

internal static class PlatformTuples
{
    public static readonly Version Runtime10 = new(10, 0, 0);
    public static readonly (string Name, OSPlatform Os, Architecture Architecture, Version Runtime, Version OsVersion)[] All =
    [
        ("dotnet10-linux-x64", OSPlatform.Linux, Architecture.X64, Runtime10, new(6, 8)),
        ("dotnet10-linux-arm64", OSPlatform.Linux, Architecture.Arm64, Runtime10, new(6, 8)),
        ("dotnet10-osx-arm64-26.0", OSPlatform.OSX, Architecture.Arm64, Runtime10, new(26, 0)),
        ("dotnet10-osx-arm64-15.7", OSPlatform.OSX, Architecture.Arm64, Runtime10, new(15, 7)),
        ("dotnet10-osx-x64-26.0", OSPlatform.OSX, Architecture.X64, Runtime10, new(26, 0)),
        ("dotnet10-windows-x64", OSPlatform.Windows, Architecture.X64, Runtime10, new(10, 0)),
        ("dotnet10-windows-arm64", OSPlatform.Windows, Architecture.Arm64, Runtime10, new(10, 0)),
        ("dotnet9-linux-x64", OSPlatform.Linux, Architecture.X64, new(9, 0, 0), new(6, 8)),
    ];
    public static PlatformProfile Build(string name)
    {
        var t = All.Single(t => t.Name == name);
        return PlatformProfile.For(t.Os, t.Architecture, t.Runtime, t.OsVersion);
    }
    public static IEnumerable<PlatformProfile> Profiles() => All.Select(t => Build(t.Name));
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var t in All) data.Add(t.Name);
        return data;
    }
    public static byte[] FixtureBytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(
        new JsonSerializerOptions { WriteIndented = true, IndentSize = 2, NewLine = "\n" }) + "\n");
    public static string FixtureDirectory() => Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(),
        "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Fixtures", "platform");
}

public sealed class PlatformRegistryTests
{
    private static readonly PlatformCapability[] AllCapabilities = Enum.GetValues<PlatformCapability>();

    // Keep original S0 row arguments as discovery identities. The one S1 delta has its own
    // handwritten literal oracle below, independent of registry/fixture generation.
    public static TheoryData<string, string> StageS0Table() => new()
    {
        { "dotnet10-linux-x64", "qualified qualified qualified qualified qualified qualified" },
        { "dotnet10-linux-arm64", "unqualified unqualified refused refused refused refused" },
        { "dotnet10-osx-arm64-26.0", "unqualified unqualified refused refused refused refused" },
        { "dotnet10-osx-arm64-15.7", "unqualified unqualified refused refused refused refused" },
        { "dotnet10-osx-x64-26.0", "unqualified unqualified refused refused refused refused" },
        { "dotnet10-windows-x64", "unqualified unqualified unqualified unqualified refused refused" },
        { "dotnet10-windows-arm64", "unqualified unqualified refused refused refused refused" },
        { "dotnet9-linux-x64", "unqualified unqualified refused refused refused refused" },
    };
    public static TheoryData<string> TupleNames() => PlatformTuples.Names();

    [Theory]
    [MemberData(nameof(StageS0Table))]
    public void Registry_MatchesTheLiteralStageTable(string tuple, string qualifications)
    {
        var profile = PlatformTuples.Build(tuple);
        var expected = tuple == "dotnet10-osx-arm64-26.0"
            ? new[] { "unqualified", "unqualified", "unqualified", "unqualified", "refused", "refused" }
            : qualifications.Split(' ');
        Assert.Equal(expected, profile.Capabilities.Select(p => PlatformWireNames.Name(p.Qualification)));
        Assert.Equal(AllCapabilities, profile.Capabilities.Select(p => p.Capability));
        for (var i = 0; i < AllCapabilities.Length; i++)
        {
            Assert.Equal(expected[i], PlatformWireNames.Name(profile.Policy(AllCapabilities[i]).Qualification));
            Assert.Equal(expected[i] != "refused", profile.GateAdmits(AllCapabilities[i]));
            Assert.Empty(profile.Policy(AllCapabilities[i]).Requires);
        }
    }

    [Fact]
    public void LiteralTable_CoversEveryTuple() => Assert.Equal(PlatformTuples.All.Select(t => t.Name).Order(StringComparer.Ordinal),
        StageS0Table().Select(row => (string)row[0]).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(TupleNames))]
    public void CapabilitiesJson_MatchesTheCommittedGoldenFixtureByteForByte(string tuple) =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(PlatformTuples.FixtureDirectory(), $"platform-{tuple}.json")),
            PlatformTuples.FixtureBytes(PlatformTuples.Build(tuple).ToCapabilitiesJson()));

    [Fact]
    public void GoldenFixtureSet_EqualsTheTupleTable() => Assert.Equal(
        PlatformTuples.All.Select(t => $"platform-{t.Name}.json").Order(StringComparer.Ordinal),
        Directory.EnumerateFiles(PlatformTuples.FixtureDirectory(), "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(TupleNames))]
    public void CapabilitiesJson_IsOrdinalSortedStableAndHasNoTopLevelQualifiedFlag(string tuple)
    {
        var profile = PlatformTuples.Build(tuple);
        var first = profile.ToCapabilitiesJson();
        Assert.Equal(first.ToJsonString(), profile.ToCapabilitiesJson().ToJsonString());
        Assert.Equal(first.ToJsonString(), PlatformTuples.Build(tuple).ToCapabilitiesJson().ToJsonString());
        AssertOrdinalKeys(first);
        Assert.Equal(["architecture", "capabilities", "os", "osVersion", "profile", "qualifiedProfiles", "runtimeVersion"], first.Select(p => p.Key));
        Assert.False(first.ContainsKey("qualified"));
        Assert.Equal(profile.Profile, first["profile"]!.GetValue<string>());
        Assert.Equal(profile.Os, first["os"]!.GetValue<string>());
        Assert.Equal(profile.Architecture, first["architecture"]!.GetValue<string>());
        Assert.Equal(profile.OsVersion, first["osVersion"]!.GetValue<string>());
        Assert.Equal(profile.RuntimeVersion, first["runtimeVersion"]!.GetValue<string>());
        Assert.Equal(["vector", "rasterExport", "assetPipeline", "artifactVerify", "stillImport", "nativePixels"],
            first["capabilities"]!.AsArray().Select(n => n!["capability"]!.GetValue<string>()));
        var linux = Assert.Single(first["qualifiedProfiles"]!.AsArray());
        Assert.Null(linux!["minOsVersion"]);
        Assert.True(linux.AsObject().ContainsKey("minOsVersion"));
    }

    [Theory]
    [InlineData("dotnet10-linux-x64", "dotnet10-linux-x64", "linux", "x64", "6.8", "10.0.0")]
    [InlineData("dotnet10-osx-x64-26.0", "dotnet10-osx-x64", "osx", "x64", "26.0", "10.0.0")]
    [InlineData("dotnet10-osx-arm64-15.7", "dotnet10-osx-arm64", "osx", "arm64", "15.7", "10.0.0")]
    [InlineData("dotnet10-windows-arm64", "dotnet10-windows-arm64", "windows", "arm64", "10.0", "10.0.0")]
    [InlineData("dotnet9-linux-x64", "dotnet9-linux-x64", "linux", "x64", "6.8", "9.0.0")]
    public void Identity_IsBuiltFromTheTupleFacts(string tuple, string profile, string os, string architecture, string osVersion, string runtimeVersion)
    {
        var p = PlatformTuples.Build(tuple);
        Assert.Equal((profile, os, architecture, osVersion, runtimeVersion), (p.Profile, p.Os, p.Architecture, p.OsVersion, p.RuntimeVersion));
    }

    [Fact]
    public void Identity_FormatsVersionsAndUnknownOperatingSystems()
    {
        var detailed = PlatformProfile.For(OSPlatform.OSX, Architecture.Arm64, new(10, 0, 12, 7), new(26, 2, 1, 4));
        Assert.Equal("26.2", detailed.OsVersion);
        Assert.Equal("10.0.12", detailed.RuntimeVersion);
        Assert.Equal(PlatformTuples.Build("dotnet10-linux-x64").ToCapabilitiesJson().ToJsonString(),
            PlatformProfile.For(OSPlatform.Linux, Architecture.X64, new(10, 0), new(6, 8)).ToCapabilitiesJson().ToJsonString());
        var freebsd = PlatformProfile.For(OSPlatform.FreeBSD, Architecture.X64, PlatformTuples.Runtime10, new(14, 1));
        var unknown = PlatformProfile.For(OSPlatform.Create("PLAN9"), Architecture.RiscV64, PlatformTuples.Runtime10, new(1, 0));
        Assert.Equal("dotnet10-freebsd-x64", freebsd.Profile);
        Assert.Equal("dotnet10-unknown-riscv64", unknown.Profile);
        foreach (var p in new[] { freebsd, unknown }) Assert.Equal(["unqualified", "unqualified", "refused", "refused", "refused", "refused"],
            p.Capabilities.Select(c => PlatformWireNames.Name(c.Qualification)));
        Assert.Throws<ArgumentNullException>(() => PlatformProfile.For(OSPlatform.Linux, Architecture.X64, null!, new(6, 8)));
        Assert.Throws<ArgumentNullException>(() => PlatformProfile.For(OSPlatform.Linux, Architecture.X64, PlatformTuples.Runtime10, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => detailed.Policy((PlatformCapability)99));
    }

    [Theory]
    [InlineData(26, 0, "26.0")]
    [InlineData(26, 2, "26.0")]
    [InlineData(27, 0, "26.0")]
    [InlineData(25, 9, null)]
    [InlineData(15, 7, null)]
    public void For_DecidesTheOsFloorFromItsOsVersionArgument(int major, int minor, string? floor)
    {
        var p = PlatformProfile.For(OSPlatform.OSX, Architecture.Arm64, PlatformTuples.Runtime10, new(major, minor));
        Assert.Equal(floor, p.MatchedOsFloor);
        Assert.Equal($"{major}.{minor}", p.OsVersion);
        Assert.Equal(major >= 26, p.GateAdmits(PlatformCapability.AssetPipeline));
        Assert.Equal(major >= 26, p.GateAdmits(PlatformCapability.ArtifactVerify));
        Assert.False(p.GateAdmits(PlatformCapability.NativePixels));
        Assert.False(p.GateAdmits(PlatformCapability.StillImport));
        Assert.True(p.GateAdmits(PlatformCapability.Vector));
    }

    [Fact]
    public void Current_EqualsForOverTheHostFacts()
    {
        var os = OperatingSystem.IsLinux() ? OSPlatform.Linux : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsFreeBSD() ? OSPlatform.FreeBSD : OSPlatform.Create("UNKNOWN");
        var expected = PlatformProfile.For(os, RuntimeInformation.ProcessArchitecture, Environment.Version, Environment.OSVersion.Version);
        var current = PlatformProfile.Current;
        Assert.Same(current, PlatformProfile.Current);
        Assert.Equal(expected.ToCapabilitiesJson().ToJsonString(), current.ToCapabilitiesJson().ToJsonString());
        Assert.Equal(expected.MatchedOsFloor, current.MatchedOsFloor);
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), current.Architecture);
        Assert.Equal($"{Environment.Version.Major}.{Environment.Version.Minor}.{Environment.Version.Build}", current.RuntimeVersion);
    }

    [Fact]
    public void Invariants_HoldForEveryTupleAndTheHost()
    {
        foreach (var p in PlatformTuples.Profiles().Append(PlatformProfile.Current))
        {
            if (p.GateAdmits(PlatformCapability.StillImport)) Assert.True(p.GateAdmits(PlatformCapability.NativePixels));
            foreach (var c in p.Capabilities)
            {
                if (c.Qualification == CapabilityQualification.Refused) Assert.Empty(c.Requires);
                var listed = PlatformProfile.QualifiedProfiles.Any(q => q.Profile == p.Profile && q.Capabilities.Contains(c.Capability)
                    && (q.MinOsVersion is null || Version.Parse(p.OsVersion) >= Version.Parse(q.MinOsVersion)));
                Assert.Equal(listed, c.Qualification == CapabilityQualification.Qualified);
            }
        }
    }

    [Fact]
    public void QualifiedProfiles_AreTheStageS0Table()
    {
        var q = Assert.Single(PlatformProfile.QualifiedProfiles);
        Assert.Equal("dotnet10-linux-x64", q.Profile);
        Assert.Null(q.MinOsVersion);
        Assert.Equal(AllCapabilities, q.Capabilities);
        Assert.Empty(q.Requires);
    }

    [Fact]
    public void EvidenceFile_EntriesEqualTheQualifiedPairs()
    {
        var evidence = JsonNode.Parse(File.ReadAllText(Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), ".config", "qualification-evidence.json")))!;
        Assert.Equal(1, evidence["version"]!.GetValue<int>());
        var entries = evidence["entries"]!.AsArray().Select(n => n!.AsObject()).ToArray();
        var pairs = entries.Select(e => (e["profile"]!.GetValue<string>(), e["capability"]!.GetValue<string>())).Order().ToArray();
        Assert.Equal(PlatformProfile.QualifiedProfiles.SelectMany(q => q.Capabilities.Select(c => (q.Profile, PlatformWireNames.Name(c)))).Order(), pairs);
        Assert.Equal(pairs.Length, pairs.Distinct().Count());
        Assert.Equal(entries.Length, entries.Select(e => e["attestationId"]!.GetValue<string>()).Distinct().Count());
        foreach (var entry in entries)
        {
            Assert.Equal("pre-d10", entry["kind"]!.GetValue<string>());
            Assert.Equal("specs/records.md", entry["path"]!.GetValue<string>());
            Assert.Equal("dotnet10-linux-x64", entry["profile"]!.GetValue<string>());
            var source = entry["sourceCommit"]!.GetValue<string>();
            Assert.True(source == "PENDING" || System.Text.RegularExpressions.Regex.IsMatch(source, "^[0-9a-f]{40}$"));
            Assert.NotEmpty(entry["passPatterns"]!.AsArray());
            Assert.All(entry["passPatterns"]!.AsArray(), p => { Assert.StartsWith("^", p!.GetValue<string>()); Assert.EndsWith("$", p.GetValue<string>()); });
        }
    }

    [Fact]
    public void Vocabulary_IsStable()
    {
        Assert.Equal(["vector", "rasterExport", "assetPipeline", "artifactVerify", "stillImport", "nativePixels"], AllCapabilities.Select(PlatformWireNames.Name));
        Assert.Equal(["qualified", "unqualified", "refused"], Enum.GetValues<CapabilityQualification>().Select(PlatformWireNames.Name));
        Assert.Equal("filesystem", PlatformWireNames.FilesystemRequirement);
        Assert.Equal("apfs", PlatformWireNames.Apfs);
        Assert.Equal(["GATE_REFUSED", "FILESYSTEM_UNQUALIFIED", "FILE_LOCKING_DISABLED", "ROOT_UNAVAILABLE"], AvailabilityReasons.All);
        Assert.Equal(["filesystem", "caseSensitive", "fileLocking", "rootCanonical"], AvailabilityFacts.All);
        Assert.Equal("ASSET_PLATFORM_UNSUPPORTED", EngineErrors.AssetPlatformUnsupported);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformWireNames.Name((PlatformCapability)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformWireNames.Name((CapabilityQualification)42));
        foreach (var requires in PlatformTuples.Profiles().Append(PlatformProfile.Current).SelectMany(p => p.Capabilities.Select(c => c.Requires))
            .Concat(PlatformProfile.QualifiedProfiles.SelectMany(q => q.Requires.Values)))
            foreach (var pair in requires) Assert.Equal(("filesystem", "apfs"), (pair.Key, pair.Value));
    }

    [Fact]
    public void RefusalMessage_ListsTheQualifiedProfilesAndNamesNoPath()
    {
        foreach (var p in PlatformTuples.Profiles().Append(PlatformProfile.Current))
            foreach (var c in AllCapabilities)
            {
                var message = p.RefusalMessage(c);
                Assert.DoesNotContain('/', message);
                Assert.DoesNotContain('\\', message);
                Assert.Contains(p.Profile, message);
                Assert.Contains(PlatformWireNames.Name(c), message);
                foreach (var q in PlatformProfile.QualifiedProfiles.Where(q => q.Capabilities.Contains(c)))
                {
                    Assert.Contains(q.Profile, message);
                    if (q.MinOsVersion is not null) Assert.Contains($"on OS version {q.MinOsVersion} or later", message);
                }
                Assert.Equal((EngineErrors.AssetPlatformUnsupported, message), (p.Refusal(c).Code, p.Refusal(c).Message));
            }
        Assert.Equal("Capability assetPipeline is unavailable on platform dotnet10-osx-arm64 (OS version 26.0, runtime 10.0.0). Qualified profiles for assetPipeline: dotnet10-linux-x64.",
            PlatformTuples.Build("dotnet10-osx-arm64-26.0").RefusalMessage(PlatformCapability.AssetPipeline));
    }

    [Fact]
    public void NarrowForTests_OnlyLowersCurrentAndRestoresOnDispose()
    {
        var current = PlatformProfile.Current;
        var baseline = current.Capabilities.ToArray();
        using (PlatformProfile.NarrowForTests(PlatformCapability.Vector, CapabilityQualification.Refused))
        {
            Assert.False(current.GateAdmits(PlatformCapability.Vector));
            Assert.Empty(current.Policy(PlatformCapability.Vector).Requires);
            Assert.Equal("refused", current.ToCapabilitiesJson()["capabilities"]![0]!["qualification"]!.GetValue<string>());
            Assert.True(PlatformTuples.Build("dotnet10-linux-x64").GateAdmits(PlatformCapability.Vector));
            using (PlatformProfile.NarrowForTests(PlatformCapability.Vector, CapabilityQualification.Qualified)) Assert.False(current.GateAdmits(PlatformCapability.Vector));
        }
        Assert.Equal(baseline, current.Capabilities);
        using (PlatformProfile.NarrowForTests(PlatformCapability.Vector, CapabilityQualification.Qualified)) Assert.Equal(baseline[0], current.Policy(PlatformCapability.Vector));
        var scope = PlatformProfile.NarrowForTests(PlatformCapability.RasterExport, CapabilityQualification.Unqualified);
        Assert.Equal(baseline[1].Qualification > CapabilityQualification.Unqualified ? baseline[1].Qualification : CapabilityQualification.Unqualified,
            current.Policy(PlatformCapability.RasterExport).Qualification);
        scope.Dispose();
        scope.Dispose();
        Assert.Equal(baseline, current.Capabilities);
    }

    [Fact]
    public async Task NarrowForTests_FlowsIntoAsyncWorkAndDoesNotLeakToOtherFlows()
    {
        var baseline = PlatformProfile.Current.Policy(PlatformCapability.AssetPipeline).Qualification;
        using (PlatformProfile.NarrowForTests(PlatformCapability.AssetPipeline, CapabilityQualification.Refused))
        {
            Assert.False(await Task.Run(() => PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline)));
            Task<CapabilityQualification> other;
            using (ExecutionContext.SuppressFlow()) other = Task.Run(() => PlatformProfile.Current.Policy(PlatformCapability.AssetPipeline).Qualification);
            Assert.Equal(baseline, await other);
        }
    }

    private static void AssertOrdinalKeys(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            Assert.Equal(obj.Select(p => p.Key).Order(StringComparer.Ordinal), obj.Select(p => p.Key));
            foreach (var p in obj) AssertOrdinalKeys(p.Value);
        }
        if (node is JsonArray array) foreach (var item in array) AssertOrdinalKeys(item);
    }
}
