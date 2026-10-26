using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Platform;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public sealed class PlatformGateCommandTests : CommandTestBase
{
    [Fact]
    public void AssetsPreflight_HostGateAndRefusalTextAgreeWithTheRegistry()
    {
        var bundle = Path.Combine(TestDir, "bundle");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "descriptor.json"), "{}");
        AssertGate(RunCommand("assets", "preflight", Path.Combine(bundle, "descriptor.json"), "-f", "json"), PlatformCapability.ArtifactVerify);
    }
    [Fact]
    public void AssetsCheck_HostGateAndRefusalTextAgreeWithTheRegistry()
    {
        var manifest = Path.Combine(TestDir, "manifest.json");
        File.WriteAllText(manifest, """{"version":1,"outputRoot":"generated","assets":[]}""");
        AssertGate(RunCommand("assets", "check", manifest, "-f", "json"), PlatformCapability.AssetPipeline);
        Assert.False(Directory.Exists(Path.Combine(TestDir, "generated")));
    }
    private static void AssertGate((int ExitCode, string Output, string Error) result, PlatformCapability capability)
    {
        var platform = PlatformProfile.Current;
        using var json = JsonDocument.Parse(result.Output);
        var code = json.RootElement.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;
        if (platform.GateAdmits(capability)) Assert.NotEqual(EngineErrors.AssetPlatformUnsupported, code);
        else
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal(EngineErrors.AssetPlatformUnsupported, code);
            Assert.Equal(platform.RefusalMessage(capability), error.GetProperty("message").GetString());
        }
    }
}
