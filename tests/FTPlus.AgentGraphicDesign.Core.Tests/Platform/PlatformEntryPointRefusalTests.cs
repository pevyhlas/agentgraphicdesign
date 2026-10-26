using System.Collections.Immutable;
using System.Security.Cryptography;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Tests.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Platform;

public abstract class PlatformRefusalTestBase : IDisposable
{
    protected string Root { get; } = Path.Combine(Path.GetTempPath(), $"agd-refusal-{Guid.NewGuid():N}");
    protected PlatformRefusalTestBase() => Directory.CreateDirectory(Root);
    public void Dispose() { Directory.Delete(Root, recursive: true); GC.SuppressFinalize(this); }
    protected string Full(string relative) => Path.Combine(Root, relative);
    protected void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.WriteAllText(Full(relative), text);
    }
    protected string[] Snapshot() => Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => Directory.Exists(p) ? $"directory:{Path.GetRelativePath(Root, p)}"
            : $"file:{Path.GetRelativePath(Root, p)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}").ToArray();
    protected async Task AssertRefusedAsync(PlatformCapability capability, Func<Task> action)
    {
        var before = Snapshot();
        Exception error;
        using (PlatformProfile.NarrowForTests(capability, CapabilityQualification.Refused)) error = await Assert.ThrowsAnyAsync<Exception>(action);
        var actual = error switch
        {
            DocumentEngineException e => (e.Code, e.Message),
            NativePixelException e => (e.Code, e.Message),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected {error}")
        };
        Assert.Equal((EngineErrors.AssetPlatformUnsupported, PlatformProfile.Current.RefusalMessage(capability)), actual);
        Assert.Equal(before, Snapshot());
    }
}

public sealed class PlatformAssetEntryPointRefusalTests : PlatformRefusalTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssetsBuildAndCheck_RefuseAssetPipelineBeforeAnyWrite(bool check)
    {
        var engine = new DocumentEngine();
        Write("source.json", engine.ToJson(engine.CreateDocument("icon", 24, 24)));
        Write("manifest.json", """{"version":1,"outputRoot":"generated","assets":[{"source":"source.json","output":"generated/icon.svg","options":{}}]}""");
        var pipeline = new AssetPipeline(engine, new VectorAssetProducer(engine));
        await AssertRefusedAsync(PlatformCapability.AssetPipeline, () => pipeline.RunAsync(Full("manifest.json"), check));
        Assert.False(Directory.Exists(Full("generated")));
    }
    [Fact]
    public async Task ArtifactPreflight_RefusesArtifactVerifyWithTheRegistryTextUnchanged()
    {
        Write("bundle/descriptor.json", "{}");
        await AssertRefusedAsync(PlatformCapability.ArtifactVerify, () => ArtifactPreflight.CheckAsync(Full("bundle/descriptor.json")));
    }
}

[Collection("Artifact verification")]
public sealed class PlatformArtifactVerifyRefusalTests : PlatformRefusalTestBase
{
    [Fact]
    public async Task ArtifactVerify_RefusesArtifactVerifyWithTheRegistryTextUnchanged()
    {
        Write("bundle/descriptor.json", "{}");
        await AssertRefusedAsync(PlatformCapability.ArtifactVerify, () => ArtifactVerification.CheckAsync(Full("bundle/descriptor.json")));
    }
}

[Collection(NativePixelCollection.Name)]
public sealed class PlatformPixelEntryPointRefusalTests : PlatformRefusalTestBase
{
    private readonly NativePixelService _service = new();
    private string FixtureAsset()
    {
        var asset = Full("pixel");
        PixelFixture.WriteAsset(asset, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        return asset;
    }
    [Theory]
    [InlineData("inspect")]
    [InlineData("validate")]
    [InlineData("region")]
    public async Task PixelRead_RefusesNativePixelsBeforeAnyWrite(string operation)
    {
        // Pins the actual Windows native-read narrowing, not only native writes.
        var asset = FixtureAsset();
        await AssertRefusedAsync(PlatformCapability.NativePixels, () => operation switch
        {
            "inspect" => _service.InspectAsync(new InspectPixelAssetRequest(asset)),
            "validate" => _service.ValidateAsync(new ValidatePixelAssetRequest(asset)),
            "region" => _service.InspectRegionAsync(new InspectPixelRegionRequest(asset, Guid.Parse(PixelFixture.LayerId), PixelTarget.Pixels, new(0, 0, 1, 1))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });
    }
    [Fact]
    public async Task StillImport_RefusesStillImportBeforeAnyWrite()
    {
        Write("input/descriptor.json", "{}");
        await AssertRefusedAsync(PlatformCapability.StillImport, () => _service.ImportAsync(new ImportPixelAssetRequest(Full("imported"), Full("input/descriptor.json"), "Imported", "Layer 1")));
        Assert.False(Directory.Exists(Full("imported")));
    }
    [Fact]
    public async Task StillImportLayer_RefusesStillImportBeforeAnyWrite()
    {
        var asset = FixtureAsset();
        Write("input/descriptor.json", "{}");
        await AssertRefusedAsync(PlatformCapability.StillImport, () => _service.ImportLayerAsync(new ImportPixelLayerRequest(asset,
            Full("input/descriptor.json"), new(Guid.Parse(PixelFixture.AssetId), 1, PixelFixture.BaseDigest), "Layer 2")));
    }
    [Fact]
    public async Task PixelCreate_RefusesNativePixelsBeforeAnyWrite()
    {
        await AssertRefusedAsync(PlatformCapability.NativePixels, () => _service.CreateAsync(new CreatePixelAssetRequest(Full("created"), "Created", 1, 1)));
        Assert.False(Directory.Exists(Full("created")));
    }
    [Fact]
    public async Task PixelEdit_RefusesNativePixelsBeforeAnyWrite()
    {
        var asset = FixtureAsset();
        await AssertRefusedAsync(PlatformCapability.NativePixels, () => _service.EditAsync(new EditPixelAssetRequest(asset,
            new(Guid.Parse(PixelFixture.AssetId), 1, PixelFixture.BaseDigest), ImmutableArray.Create<PixelEdit>(new PixelEdit.AddLayer("Layer 2")))));
    }
    [Fact]
    public async Task PixelExport_RefusesNativePixelsBeforeAnyWrite()
    {
        var asset = FixtureAsset();
        await AssertRefusedAsync(PlatformCapability.NativePixels, () => _service.ExportAsync(new ExportPixelAssetRequest(asset, Full("out/pixel.png"))));
        Assert.False(Directory.Exists(Full("out")));
    }
}
