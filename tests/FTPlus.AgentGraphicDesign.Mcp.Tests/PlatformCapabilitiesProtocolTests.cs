using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public partial class ProtocolTests
{
    [Fact]
    public async Task GetCapabilities_PlatformMemberEqualsTheRegistryAndReplacesTheOldFields()
    {
        await StartServerAsync();
        await InitializeAsync();
        var capabilities = NativeData(await CallToolAsync("get_capabilities", new { }));
        var platform = capabilities.GetProperty("platform");
        Assert.Equal(PlatformProfile.Current.ToCapabilitiesJson().ToJsonString(), JsonNode.Parse(platform.GetRawText())!.ToJsonString());
        Assert.Equal(["architecture", "capabilities", "os", "osVersion", "profile", "qualifiedProfiles", "runtimeVersion"], platform.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, platform.GetProperty("qualifiedProfiles")[0].GetProperty("minOsVersion").ValueKind);
        Assert.Equal($"{Environment.Version.Major}.{Environment.Version.Minor}.{Environment.Version.Build}", platform.GetProperty("runtimeVersion").GetString());
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), platform.GetProperty("architecture").GetString());
        Assert.False(capabilities.TryGetProperty("supportedPlatform", out _));
        Assert.False(capabilities.TryGetProperty("currentPlatformMatches", out _));
        Assert.False(platform.TryGetProperty("qualified", out _));
    }
    [Fact]
    public async Task PreflightArtifact_HostGateAndRefusalTextAgreeWithTheRegistry()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "bundle"));
        File.WriteAllText(Path.Combine(_testDir, "bundle", "descriptor.json"), "{}");
        await StartServerAsync();
        await InitializeAsync();
        var result = await CallToolAsync("preflight_artifact", new { descriptorPath = "bundle/descriptor.json" });
        var error = NativeEnvelope(result).GetProperty("error");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var platform = PlatformProfile.Current;
        if (platform.GateAdmits(PlatformCapability.ArtifactVerify)) Assert.NotEqual(EngineErrors.AssetPlatformUnsupported, error.GetProperty("code").GetString());
        else
        {
            Assert.Equal(EngineErrors.AssetPlatformUnsupported, error.GetProperty("code").GetString());
            Assert.Equal(platform.RefusalMessage(PlatformCapability.ArtifactVerify), error.GetProperty("message").GetString());
        }
    }
    [Fact]
    public void ToolResultFail_PassesEveryRegistryRefusalTextThroughUnchanged()
    {
        var hosts = new[]
        {
            PlatformProfile.Current,
            PlatformProfile.For(OSPlatform.OSX, Architecture.Arm64, new(10, 0, 12), new(26, 2)),
            PlatformProfile.For(OSPlatform.OSX, Architecture.X64, new(10, 0, 12), new(15, 7)),
            PlatformProfile.For(OSPlatform.Windows, Architecture.Arm64, new(10, 0, 12), new(10, 0)),
            PlatformProfile.For(OSPlatform.Linux, Architecture.Arm64, new(9, 0, 4), new(6, 8))
        };
        foreach (var p in hosts)
            foreach (var c in Enum.GetValues<PlatformCapability>())
            {
                var refusal = p.Refusal(c);
                var error = ToolResult.Fail(refusal.Code, refusal.Message).StructuredContent!.Value.GetProperty("error");
                Assert.Equal(EngineErrors.AssetPlatformUnsupported, error.GetProperty("code").GetString());
                Assert.Equal(p.RefusalMessage(c), error.GetProperty("message").GetString());
                Assert.Equal(p.RefusalMessage(c), ToolResult.FromException(refusal, "UNEXPECTED").StructuredContent!.Value.GetProperty("error").GetProperty("message").GetString());
            }
    }
}
