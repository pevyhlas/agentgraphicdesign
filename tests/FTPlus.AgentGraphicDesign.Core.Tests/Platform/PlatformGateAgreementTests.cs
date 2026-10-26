using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Tests.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Platform;

[Collection(NativePixelCollection.Name)]
public sealed class PlatformGateAgreementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agd-gates-{Guid.NewGuid():N}");
    public PlatformGateAgreementTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);
    [Fact]
    public void HostGates_AgreeWithTheRegistry()
    {
        var platform = PlatformProfile.Current;
        File.WriteAllText(Path.Combine(_root, "input.txt"), "x");
        foreach (var capability in new[] { PlatformCapability.AssetPipeline, PlatformCapability.ArtifactVerify, PlatformCapability.StillImport, PlatformCapability.NativePixels })
        {
            var error = Record.Exception(() => AssetPaths.Inspect(Path.Combine(_root, "input.txt"), false, true, capability, CancellationToken.None));
            if (platform.GateAdmits(capability)) Assert.Null(error);
            else
            {
                var refusal = Assert.IsType<DocumentEngineException>(error);
                Assert.Equal((EngineErrors.AssetPlatformUnsupported, platform.RefusalMessage(capability)), (refusal.Code, refusal.Message));
            }
        }
        var import = Record.Exception(() => new NativePixelService().ImportAsync(new ImportPixelAssetRequest(
            Path.Combine(_root, "asset"), Path.Combine(_root, "missing.descriptor.json"), "Asset", "Layer")).GetAwaiter().GetResult());
        var importError = Assert.IsType<NativePixelException>(import);
        if (platform.GateAdmits(PlatformCapability.StillImport)) Assert.NotEqual(EngineErrors.AssetPlatformUnsupported, importError.Code);
        else Assert.Equal((EngineErrors.AssetPlatformUnsupported, platform.RefusalMessage(PlatformCapability.StillImport)), (importError.Code, importError.Message));
        Assert.False(Directory.Exists(Path.Combine(_root, "asset")));
        Assert.True(platform.GateAdmits(PlatformCapability.Vector));
        Assert.True(platform.GateAdmits(PlatformCapability.RasterExport));
    }
}
