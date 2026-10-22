using FTPlus.AgentGraphicDesign.Core.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

[Collection(NativePixelCollection.Name)]
public sealed class NativePixelServiceTests : NativePixelTestBase
{
    private readonly NativePixelService _service = new();

    private string CreateTarget()
    {
        string workspace = NewWorkspace();
        return Path.Combine(workspace, "hero");
    }

    // ---- Golden vectors: read digests match the independent §Literal Vectors -------------

    [Fact]
    public async Task InspectReturnsBaseLiteralSourceDigest()
    {
        string dir = CreateTarget();
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));

        Assert.Equal(PixelFixture.AssetId, info.Snapshot.AssetId.ToString());
        Assert.Equal(1, info.Snapshot.Revn);
        Assert.Equal(PixelFixture.BaseDigest, info.Snapshot.SourceDigest);
        Assert.Equal("Pixel", info.Name);
        Assert.Equal(1, info.Width);
        Assert.Equal(1, info.Height);
        Assert.Equal(4, info.LogicalSourceBytes);
        var layer = Assert.Single(info.Layers);
        Assert.Equal(PixelFixture.LayerId, layer.Id.ToString());
        Assert.Equal("Layer 1", layer.Name);
        Assert.True(layer.Visible);
        Assert.Equal(1m, layer.Opacity);
        Assert.Null(layer.MaskSha256);
    }

    [Fact]
    public async Task InspectReturnsOpacitySemanticChangeDigest()
    {
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace("\"opacity\":1", "\"opacity\":0.5");
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(PixelFixture.OpacityDigest, info.Snapshot.SourceDigest);
    }

    [Fact]
    public async Task InspectReturnsAttachedMaskSemanticChangeDigest()
    {
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace("\"maskSha256\":null", "\"maskSha256\":\"" + PixelFixture.FullMaskHash + "\"");
        PixelFixture.WriteAsset(dir, metadata,
            (PixelFixture.TransparentHash, PixelFixture.TransparentHex),
            (PixelFixture.FullMaskHash, PixelFixture.FullMaskHex));

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(PixelFixture.MaskDigest, info.Snapshot.SourceDigest);
        Assert.Equal(PixelFixture.FullMaskHash, Assert.Single(info.Layers).MaskSha256);
        Assert.Equal(1 * (4 + 1), info.LogicalSourceBytes);
    }

    [Fact]
    public async Task InspectReturnsHiddenRgbSemanticChangeDigest()
    {
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace(PixelFixture.TransparentHash, PixelFixture.HiddenHash);
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.HiddenHash, PixelFixture.HiddenHex));

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(PixelFixture.HiddenDigest, info.Snapshot.SourceDigest);
    }

    [Fact]
    public async Task HiddenRgbUnderAlphaZeroIsPreservedInRegionSamples()
    {
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace(PixelFixture.TransparentHash, PixelFixture.HiddenHash);
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.HiddenHash, PixelFixture.HiddenHex));

        var region = await _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, Guid.Parse(PixelFixture.LayerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1)));
        var rgba = Assert.IsType<RgbaRegionResult>(region);
        Assert.Equal("#11223300", Assert.Single(rgba.Samples));
    }

    [Fact]
    public async Task MaskRegionReturnsCoverageBytes()
    {
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace("\"maskSha256\":null", "\"maskSha256\":\"" + PixelFixture.FullMaskHash + "\"");
        PixelFixture.WriteAsset(dir, metadata,
            (PixelFixture.TransparentHash, PixelFixture.TransparentHex),
            (PixelFixture.FullMaskHash, PixelFixture.FullMaskHex));

        var region = await _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, Guid.Parse(PixelFixture.LayerId), PixelTarget.Mask, new PixelRect(0, 0, 1, 1)));
        var mask = Assert.IsType<MaskRegionResult>(region);
        Assert.Equal((byte)255, Assert.Single(mask.Samples));
    }

    // ---- Create + round-trips ------------------------------------------------------------

    [Fact]
    public async Task CreateOneByOneProducesTheTransparentGoldenPayload()
    {
        string dir = CreateTarget();
        var created = await _service.CreateAsync(new CreatePixelAssetRequest(dir, "Icon", 1, 1));

        Assert.NotEqual(Guid.Empty, created.State.Snapshot.AssetId);
        Assert.NotEqual(created.State.Snapshot.AssetId, created.InitialLayerId);
        Assert.Equal(1, created.State.Snapshot.Revn);
        var layer = Assert.Single(created.State.Layers);
        Assert.Equal(created.InitialLayerId, layer.Id);
        Assert.Equal("Layer 1", layer.Name);
        Assert.True(layer.Visible);
        Assert.Equal(1m, layer.Opacity);
        Assert.Null(layer.MaskSha256);
        // A 1x1 all-zero RGBA surface is exactly the independent transparent vector.
        Assert.Equal(PixelFixture.TransparentHash, layer.PixelsSha256);

        byte[] onDisk = File.ReadAllBytes(Path.Combine(dir, "payloads", PixelFixture.TransparentHash + ".bin"));
        Assert.Equal(PixelFixture.Bytes(PixelFixture.TransparentHex), onDisk);
        Assert.True(File.Exists(Path.Combine(dir, "asset.agd-pixels.json")));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(32, 32)]
    [InlineData(64, 64)]
    [InlineData(37, 23)]
    [InlineData(1, 40)]
    [InlineData(40, 1)]
    public async Task CreateInspectValidateRegionRoundTrip(int width, int height)
    {
        string dir = CreateTarget();
        var created = await _service.CreateAsync(new CreatePixelAssetRequest(dir, "Round", width, height));
        Guid assetId = created.State.Snapshot.AssetId;
        Guid layerId = created.InitialLayerId;

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(created.State.Snapshot, info.Snapshot);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal((long)width * height * 4, info.LogicalSourceBytes);
        Assert.Equal(assetId, info.Snapshot.AssetId);
        Assert.Equal(layerId, Assert.Single(info.Layers).Id);

        var validate = await _service.ValidateAsync(new ValidatePixelAssetRequest(dir));
        var valid = Assert.IsType<ValidPixelAssetResult>(validate);
        Assert.Equal(info.Snapshot, valid.State.Snapshot);

        // Sample the four corners: a freshly created surface is fully transparent everywhere.
        var region = await _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(0, 0, width, Math.Min(height, 4096 / width))));
        var rgba = Assert.IsType<RgbaRegionResult>(region);
        Assert.All(rgba.Samples, sample => Assert.Equal("#00000000", sample));
    }

    [Fact]
    public async Task InspectRegionInclusiveMaxEntriesSucceedsAndOverLimitFails()
    {
        string dir = CreateTarget();
        await _service.CreateAsync(new CreatePixelAssetRequest(dir, "Region", 100, 100));
        Guid layerId = (await _service.InspectAsync(new InspectPixelAssetRequest(dir))).Layers[0].Id;

        var ok = await _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(0, 0, 64, 64)));
        Assert.Equal(4096, ((RgbaRegionResult)ok).Samples.Length);

        var tooBig = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(0, 0, 65, 64))));
        Assert.Equal("RESOURCE_LIMIT", tooBig.Code);
    }

    // ---- Create rejections ---------------------------------------------------------------

    [Fact]
    public async Task CreateRejectsAnExistingTarget()
    {
        string dir = CreateTarget();
        await _service.CreateAsync(new CreatePixelAssetRequest(dir, "First", 2, 2));

        var again = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "Second", 2, 2)));
        Assert.Equal("PIXEL_ALREADY_EXISTS", again.Code);
    }

    [Fact]
    public async Task CreateRejectsAnEmptyExistingDirectory()
    {
        string dir = CreateTarget();
        Directory.CreateDirectory(dir); // empty, occupies the target
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "X", 2, 2)));
        Assert.Equal("PIXEL_ALREADY_EXISTS", ex.Code);
    }

    [Fact]
    public async Task CreateRejectsACorruptExistingTargetWithoutRepair()
    {
        string dir = CreateTarget();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "asset.agd-pixels.json"), "{ not valid");
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "X", 2, 2)));
        Assert.Equal("PIXEL_ALREADY_EXISTS", ex.Code);
        Assert.Equal("{ not valid", File.ReadAllText(Path.Combine(dir, "asset.agd-pixels.json")));
    }

    [Fact]
    public async Task CreateRejectsAMissingParent()
    {
        string dir = Path.Combine(NewWorkspace(), "missing", "hero");
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "X", 2, 2)));
        Assert.Equal("DOCUMENT_NOT_FOUND", ex.Code);
    }

    [Theory]
    [InlineData(0, 4, "INVALID_DIMENSIONS")]
    [InlineData(4, 0, "INVALID_DIMENSIONS")]
    [InlineData(8193, 1, "RESOURCE_LIMIT")]
    [InlineData(2049, 2048, "RESOURCE_LIMIT")]
    public async Task CreateEnforcesCanvasBounds(int width, int height, string code)
    {
        string dir = CreateTarget();
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "X", width, height)));
        Assert.Equal(code, ex.Code);
    }

    [Theory]
    [InlineData(8192, 1)]
    [InlineData(2048, 2048)]
    public async Task CreateAcceptsInclusiveAxisAndAreaBoundaries(int width, int height)
    {
        string dir = CreateTarget();
        var created = await _service.CreateAsync(new CreatePixelAssetRequest(dir, "Boundary", width, height));
        Assert.Equal((long)width * height * 4, created.State.LogicalSourceBytes);
    }

    [Fact]
    public async Task CreateRejectsABlankName()
    {
        string dir = CreateTarget();
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "   ", 2, 2)));
        Assert.Equal("INVALID_PROPERTY", ex.Code);
    }

    // ---- Region rejections ---------------------------------------------------------------

    [Fact]
    public async Task InspectRegionRejectsAnUnknownLayer()
    {
        string dir = CreateTarget();
        await _service.CreateAsync(new CreatePixelAssetRequest(dir, "R", 4, 4));
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, Guid.NewGuid(), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1))));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    [Fact]
    public async Task InspectRegionRejectsMaskTargetWithoutMask()
    {
        string dir = CreateTarget();
        var created = await _service.CreateAsync(new CreatePixelAssetRequest(dir, "R", 4, 4));
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, created.InitialLayerId, PixelTarget.Mask, new PixelRect(0, 0, 1, 1))));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    [Fact]
    public async Task InspectRegionRejectsAnOutOfBoundsRectangle()
    {
        string dir = CreateTarget();
        var created = await _service.CreateAsync(new CreatePixelAssetRequest(dir, "R", 4, 4));
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectRegionAsync(
            new InspectPixelRegionRequest(dir, created.InitialLayerId, PixelTarget.Pixels, new PixelRect(2, 2, 4, 4))));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    // ---- Missing / absent targets --------------------------------------------------------

    [Fact]
    public async Task InspectMissingAssetThrowsDocumentNotFound()
    {
        string dir = CreateTarget(); // never created
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("DOCUMENT_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task InspectMissingMetadataThrowsDocumentNotFound()
    {
        string dir = CreateTarget();
        Directory.CreateDirectory(dir); // directory exists, no metadata file
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("DOCUMENT_NOT_FOUND", ex.Code);
    }

    // ---- Admission -----------------------------------------------------------------------

    [Fact]
    public async Task OverlappingNativeCallFailsImmediatelyBusy()
    {
        string dir = CreateTarget();
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));

        using (NativeOperationScope.Enter(default)) // hold the single host slot
        {
            var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
            Assert.Equal("PIXEL_BUSY", ex.Code);
            Assert.Equal("Native operation slot is busy.", ex.Message);
        }
    }

    // ---- Preview / export / check are wired ----------------------------------------------

    [Fact]
    public async Task PreviewExportCheckAreImplementedAndAdmitAndReadFirst()
    {
        string missing = Path.Combine(NewWorkspace(), "absent");

        var preview = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.PreviewAsync(new PreviewPixelAssetRequest(missing, new PixelPreviewDelivery.Inline())));
        var export = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.ExportAsync(new ExportPixelAssetRequest(missing, Path.Combine(NewWorkspace(), "out.png"))));
        var check = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.CheckExportAsync(new CheckPixelExportRequest(missing, Path.Combine(NewWorkspace(), "out.png"))));

        Assert.Equal("DOCUMENT_NOT_FOUND", preview.Code);
        Assert.Equal("DOCUMENT_NOT_FOUND", export.Code);
        Assert.Equal("DOCUMENT_NOT_FOUND", check.Code);
    }
}
