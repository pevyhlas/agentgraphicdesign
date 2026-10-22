using System.Collections.Immutable;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

[Collection(NativePixelCollection.Name)]
public sealed class NativePixelEditTests : NativePixelTestBase
{
    private readonly NativePixelService _service = new();

    private string CreateTarget() => Path.Combine(NewWorkspace(), "hero");

    private async Task<(string Dir, PixelSnapshot Snapshot, Guid LayerId)> NewAsset(int width = 2, int height = 2, string name = "Hero")
    {
        string dir = CreateTarget();
        var created = await _service.CreateAsync(new CreatePixelAssetRequest(dir, name, width, height));
        return (dir, created.State.Snapshot, created.InitialLayerId);
    }

    private Task<EditPixelAssetResult> Edit(string dir, PixelSnapshot snapshot, params PixelEdit[] operations) =>
        _service.EditAsync(new EditPixelAssetRequest(dir, snapshot, [.. operations]));

    private static PixelLayerRef Id(Guid id) => new PixelLayerRef.ById(id);
    private static PixelLayerRef Alias(string alias) => new PixelLayerRef.ByAlias(alias);

    private (string Dir, PixelSnapshot Snapshot, Guid LayerId) BaseFixture()
    {
        string dir = CreateTarget();
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        return (dir, new PixelSnapshot(Guid.Parse(PixelFixture.AssetId), 1, PixelFixture.BaseDigest), Guid.Parse(PixelFixture.LayerId));
    }

    // ---- Golden cross-checks: an edit reproduces the independent §Literal Vectors --------

    [Fact]
    public async Task FillingTheBasePixelReproducesTheHiddenGoldenVector()
    {
        var (dir, snap, layerId) = BaseFixture();

        var result = await Edit(dir, snap, new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 1, 1), "#11223300"));

        Assert.True(result.Changed);
        Assert.Equal(snap.SourceDigest, result.BeforeSnapshot.SourceDigest);
        Assert.Equal(PixelFixture.HiddenDigest, result.State.Snapshot.SourceDigest);
        Assert.Equal(2, result.State.Snapshot.Revn);
        Assert.Equal(PixelFixture.HiddenHash, Assert.Single(result.State.Layers).PixelsSha256);
        Assert.True(File.Exists(Path.Combine(dir, "payloads", PixelFixture.HiddenHash + ".bin")));
        // Old payload object is retained as a safe orphan, never deleted as implicit GC.
        Assert.True(File.Exists(Path.Combine(dir, "payloads", PixelFixture.TransparentHash + ".bin")));
        var region = Assert.Single(result.ChangedRegions);
        Assert.Equal(layerId, region.LayerId);
        Assert.Equal(PixelTarget.Pixels, region.Target);
        Assert.Equal(new PixelRect(0, 0, 1, 1), region.Rect);
        var change = Assert.Single(result.LayerChanges);
        Assert.Equal(new[] { PixelLayerChangeKind.Pixels }, change.Changes);
    }

    [Fact]
    public async Task AttachingAMaskReproducesTheFullMaskGoldenVector()
    {
        var (dir, snap, layerId) = BaseFixture();

        var result = await Edit(dir, snap, new PixelEdit.AttachMask(Id(layerId)));

        Assert.True(result.Changed);
        Assert.Equal(PixelFixture.MaskDigest, result.State.Snapshot.SourceDigest);
        Assert.Equal(PixelFixture.FullMaskHash, Assert.Single(result.State.Layers).MaskSha256);
        var change = Assert.Single(result.LayerChanges);
        Assert.Equal(new[] { PixelLayerChangeKind.MaskPresence }, change.Changes);
        var region = Assert.Single(result.ChangedRegions);
        Assert.Equal(PixelTarget.Mask, region.Target);
        Assert.Equal(new PixelRect(0, 0, 1, 1), region.Rect);
    }

    [Fact]
    public async Task SettingOpacityReproducesTheOpacityGoldenVector()
    {
        var (dir, snap, layerId) = BaseFixture();

        var result = await Edit(dir, snap, new PixelEdit.SetOpacity(Id(layerId), 0.5m));

        Assert.True(result.Changed);
        Assert.Equal(PixelFixture.OpacityDigest, result.State.Snapshot.SourceDigest);
        Assert.Empty(result.ChangedRegions); // control-only change has no changed region
        Assert.Equal(new[] { PixelLayerChangeKind.Opacity }, Assert.Single(result.LayerChanges).Changes);
    }

    // ---- Guard, no-op, revision continuity ----------------------------------------------

    [Fact]
    public async Task WrongExpectedDigestIsRevisionConflict()
    {
        var (dir, snap, layerId) = await NewAsset();
        var wrong = snap with { SourceDigest = new string('a', 64) };

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, wrong, new PixelEdit.SetVisibility(Id(layerId), false)));
        Assert.Equal("REVISION_CONFLICT", error.Code);
    }

    [Fact]
    public async Task WrongExpectedRevisionIsRevisionConflict()
    {
        var (dir, snap, layerId) = await NewAsset();
        var wrong = snap with { Revn = 2 };

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, wrong, new PixelEdit.SetVisibility(Id(layerId), false)));
        Assert.Equal("REVISION_CONFLICT", error.Code);
    }

    [Fact]
    public async Task NoOpControlAssignmentReturnsUnchangedWithoutAdvancingRevision()
    {
        var (dir, snap, layerId) = await NewAsset();

        // The created layer is already visible=true; re-asserting it is a canonical no-op.
        var result = await Edit(dir, snap, new PixelEdit.SetVisibility(Id(layerId), true));

        Assert.False(result.Changed);
        Assert.Equal(snap, result.BeforeSnapshot);
        Assert.Equal(snap, result.State.Snapshot);
        Assert.Empty(result.CreatedLayers);
        Assert.Empty(result.AffectedLayerIds);
        Assert.Empty(result.LayerChanges);
        Assert.Empty(result.ChangedRegions);
    }

    [Fact]
    public async Task RevertingChangesWithinOneBatchIsANoOp()
    {
        var (dir, snap, layerId) = await NewAsset();

        var result = await Edit(dir, snap,
            new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 2, 2), "#01020304"),
            new PixelEdit.Clear(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 2, 2)));

        Assert.False(result.Changed);
        Assert.Equal(snap.Revn, result.State.Snapshot.Revn);
    }

    [Fact]
    public async Task GuardStillAppliesToANoOpBatch()
    {
        var (dir, snap, layerId) = await NewAsset();
        var wrong = snap with { SourceDigest = new string('b', 64) };

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, wrong, new PixelEdit.SetVisibility(Id(layerId), true)));
        Assert.Equal("REVISION_CONFLICT", error.Code);
    }

    [Fact]
    public async Task AfterTokenGuardsTheFollowUpEdit()
    {
        var (dir, snap, layerId) = await NewAsset();
        var first = await Edit(dir, snap, new PixelEdit.SetVisibility(Id(layerId), false));

        // The stale original token no longer guards; the returned after-token does.
        var stale = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.SetVisibility(Id(layerId), true)));
        Assert.Equal("REVISION_CONFLICT", stale.Code);

        var second = await Edit(dir, first.State.Snapshot, new PixelEdit.SetVisibility(Id(layerId), true));
        Assert.True(second.Changed);
        Assert.Equal(3, second.State.Snapshot.Revn);
    }

    // ---- Layers: add, remove, reorder ---------------------------------------------------

    [Fact]
    public async Task AddLayerAppendsAtTopAndReportsCreatedRecord()
    {
        var (dir, snap, _) = await NewAsset();

        var result = await Edit(dir, snap, new PixelEdit.AddLayer("Highlights", "hi"));

        Assert.True(result.Changed);
        Assert.Equal(2, result.State.Layers.Length);
        Assert.Equal("Highlights", result.State.Layers[^1].Name);
        var created = Assert.Single(result.CreatedLayers);
        Assert.Equal(0, created.OperationIndex);
        Assert.Equal("hi", created.Alias);
        Assert.Equal(result.State.Layers[^1].Id, created.LayerId);
        Assert.Equal(new[] { PixelLayerChangeKind.Created }, Assert.Single(result.LayerChanges).Changes);
        // A brand-new layer reports its whole canvas as changed pixels.
        Assert.Equal(new PixelRect(0, 0, 2, 2), Assert.Single(result.ChangedRegions).Rect);
    }

    [Fact]
    public async Task RemovingTheOnlyLayerIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.RemoveLayer(Id(layerId))));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
        Assert.Single((await _service.InspectAsync(new InspectPixelAssetRequest(dir))).Layers);
    }

    [Fact]
    public async Task RemovingALayerShiftsAndReportsOrderForSurvivors()
    {
        var (dir, snap, bottomId) = await NewAsset();
        var added = await Edit(dir, snap, new PixelEdit.AddLayer("Top", "top"));
        Guid topId = added.State.Layers[^1].Id;

        var result = await Edit(dir, added.State.Snapshot, new PixelEdit.RemoveLayer(Id(bottomId)));

        Assert.True(result.Changed);
        Assert.Equal(topId, Assert.Single(result.State.Layers).Id);
        // Affected = survivor with an order change (now index 0), then the removed layer.
        Assert.Equal(new[] { topId, bottomId }, result.AffectedLayerIds);
        Assert.Equal(new[] { PixelLayerChangeKind.Order }, result.LayerChanges[0].Changes);
        Assert.Equal(new[] { PixelLayerChangeKind.Removed }, result.LayerChanges[1].Changes);
        Assert.DoesNotContain(result.ChangedRegions, r => r.LayerId == bottomId);
    }

    [Fact]
    public async Task ReorderMovesToTheGivenIndex()
    {
        var (dir, snap, bottomId) = await NewAsset();
        var added = await Edit(dir, snap, new PixelEdit.AddLayer("Top"));
        Guid topId = added.State.Layers[^1].Id;

        var result = await Edit(dir, added.State.Snapshot, new PixelEdit.ReorderLayer(Id(topId), 0));

        Assert.True(result.Changed);
        Assert.Equal(new[] { topId, bottomId }, result.State.Layers.Select(l => l.Id).ToArray());
        Assert.Contains(result.LayerChanges, c => c.LayerId == topId && c.Changes.Contains(PixelLayerChangeKind.Order));
        Assert.Contains(result.LayerChanges, c => c.LayerId == bottomId && c.Changes.Contains(PixelLayerChangeKind.Order));
    }

    [Fact]
    public async Task CreatedLayersAreOrderedByAddIndexEvenAfterReorder()
    {
        var (dir, snap, _) = await NewAsset(1, 1);

        // Add A (op0), add B (op1), then move B below A so final order is B, A, base.
        var result = await Edit(dir, snap,
            new PixelEdit.AddLayer("A", "a"),
            new PixelEdit.AddLayer("B", "b"),
            new PixelEdit.ReorderLayer(Alias("b"), 0));

        Assert.Equal(2, result.CreatedLayers.Length);
        Assert.Equal(new[] { 0, 1 }, result.CreatedLayers.Select(c => c.OperationIndex).ToArray());
        Assert.Equal(new[] { "a", "b" }, result.CreatedLayers.Select(c => c.Alias).ToArray());
    }

    [Fact]
    public async Task ReorderIndexOutOfRangeIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.ReorderLayer(Id(layerId), 1)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    // ---- Masks --------------------------------------------------------------------------

    [Fact]
    public async Task AttachMaskTwiceIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset();
        var attached = await Edit(dir, snap, new PixelEdit.AttachMask(Id(layerId)));

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, attached.State.Snapshot, new PixelEdit.AttachMask(Id(layerId))));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task RemoveMaskWhenAbsentIsAGuardedNoOp()
    {
        var (dir, snap, layerId) = await NewAsset();

        var result = await Edit(dir, snap, new PixelEdit.RemoveMask(Id(layerId)));
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task FillMaskRequiresAnAttachedMask()
    {
        var (dir, snap, layerId) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.FillMask(Id(layerId), new PixelRect(0, 0, 1, 1), 128)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task FillMaskThenRegionReportsCoverageBytes()
    {
        var (dir, snap, layerId) = await NewAsset();
        var attached = await Edit(dir, snap, new PixelEdit.AttachMask(Id(layerId)));
        var filled = await Edit(dir, attached.State.Snapshot,
            new PixelEdit.FillMask(Id(layerId), new PixelRect(0, 0, 1, 1), 42));

        Assert.True(filled.Changed);
        Assert.Contains(filled.LayerChanges, c => c.Changes.Contains(PixelLayerChangeKind.Mask));
        var region = await _service.InspectRegionAsync(new InspectPixelRegionRequest(dir, layerId, PixelTarget.Mask, new PixelRect(0, 0, 2, 2)));
        var mask = Assert.IsType<MaskRegionResult>(region);
        Assert.Equal(new byte[] { 42, 255, 255, 255 }, mask.Samples);
    }

    // ---- Fill / clear / replace pixels --------------------------------------------------

    [Fact]
    public async Task FillPixelsReportsATightChangedRegion()
    {
        var (dir, snap, layerId) = await NewAsset(4, 4);

        var result = await Edit(dir, snap, new PixelEdit.FillPixels(Id(layerId), new PixelRect(1, 1, 2, 2), "#0a141e28"));

        var region = Assert.Single(result.ChangedRegions);
        Assert.Equal(new PixelRect(1, 1, 2, 2), region.Rect);
        var sampled = await _service.InspectRegionAsync(new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(1, 1, 1, 1)));
        Assert.Equal("#0a141e28", Assert.Single(Assert.IsType<RgbaRegionResult>(sampled).Samples));
    }

    [Fact]
    public async Task ReplacePixelsFromBufferAssignsExactSamples()
    {
        var (dir, snap, layerId) = await NewAsset(2, 1);
        byte[] raw = [1, 2, 3, 4, 5, 6, 7, 8];
        var input = RawSampleInput.FromBytes(raw);

        var result = await Edit(dir, snap, new PixelEdit.Replace(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 2, 1), input));

        Assert.True(result.Changed);
        var sampled = Assert.IsType<RgbaRegionResult>(
            await _service.InspectRegionAsync(new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(0, 0, 2, 1))));
        Assert.Equal(new[] { "#01020304", "#05060708" }, sampled.Samples);
    }

    [Fact]
    public async Task ReplaceWithWrongLengthBufferIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset(2, 1);
        var input = RawSampleInput.FromBytes([1, 2, 3]); // needs 8 bytes for 2x1 pixels

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.Replace(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 2, 1), input)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task ReplacePixelsFromFileAssignsExactSamples()
    {
        var (dir, snap, layerId) = await NewAsset(1, 1);
        string inputPath = Path.Combine(Path.GetDirectoryName(dir)!, "sample.raw");
        await File.WriteAllBytesAsync(inputPath, [0x11, 0x22, 0x33, 0x44]);
        var input = new RawSampleInput.File(inputPath);

        var result = await Edit(dir, snap, new PixelEdit.Replace(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1), input));

        Assert.True(result.Changed);
        var sampled = Assert.IsType<RgbaRegionResult>(
            await _service.InspectRegionAsync(new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(0, 0, 1, 1))));
        Assert.Equal("#11223344", Assert.Single(sampled.Samples));
    }

    [Fact]
    public async Task ReplaceFromShortFileIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset(1, 1);
        string inputPath = Path.Combine(Path.GetDirectoryName(dir)!, "short.raw");
        await File.WriteAllBytesAsync(inputPath, [0x11, 0x22]); // needs 4 bytes
        var input = new RawSampleInput.File(inputPath);

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.Replace(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1), input)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    // ---- Alias / reference resolution ---------------------------------------------------

    [Fact]
    public async Task AliasResolvesToTheLayerMintedEarlierInTheBatch()
    {
        var (dir, snap, _) = await NewAsset(2, 2);

        var result = await Edit(dir, snap,
            new PixelEdit.AddLayer("Overlay", "ov"),
            new PixelEdit.FillPixels(Alias("ov"), new PixelRect(0, 0, 2, 2), "#ffffffff"));

        Assert.True(result.Changed);
        Guid newId = result.CreatedLayers.Single().LayerId;
        Assert.Contains(result.LayerChanges, c => c.LayerId == newId && c.Changes.Contains(PixelLayerChangeKind.Created));
    }

    [Fact]
    public async Task ForwardAliasReferenceIsRejected()
    {
        var (dir, snap, _) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap,
                new PixelEdit.SetVisibility(Alias("later"), false),
                new PixelEdit.AddLayer("Later", "later")));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task ReferenceToARemovedAliasedLayerIsRejected()
    {
        var (dir, snap, _) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap,
                new PixelEdit.AddLayer("Temp", "temp"),
                new PixelEdit.RemoveLayer(Alias("temp")),
                new PixelEdit.SetVisibility(Alias("temp"), false)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task DuplicateAliasDeclarationIsRejected()
    {
        var (dir, snap, _) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap,
                new PixelEdit.AddLayer("A", "dup"),
                new PixelEdit.AddLayer("B", "dup")));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task GuidReferenceToAnAbsentLayerIsRejected()
    {
        var (dir, snap, _) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.SetVisibility(Id(Guid.NewGuid()), false)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    // ---- Atomicity ----------------------------------------------------------------------

    [Fact]
    public async Task AnInvalidLateOperationRejectsTheWholeBatchWithNoWrite()
    {
        var (dir, snap, layerId) = await NewAsset(2, 2);
        int payloadsBefore = Directory.GetFiles(Path.Combine(dir, "payloads")).Length;

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap,
                new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 2, 2), "#01020304"),
                new PixelEdit.RemoveLayer(Id(Guid.NewGuid()))));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);

        var after = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(1, after.Snapshot.Revn);
        Assert.Equal(snap.SourceDigest, after.Snapshot.SourceDigest);
        Assert.Equal(payloadsBefore, Directory.GetFiles(Path.Combine(dir, "payloads")).Length);
    }

    // ---- Payload reuse on the no-replace publish path -----------------------------------

    [Fact]
    public async Task EditReusesAnExactExistingOrphanPayload()
    {
        var (dir, snap, layerId) = BaseFixture();
        // First edit references a new hash and leaves the transparent payload as an orphan on disk.
        var first = await Edit(dir, snap, new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 1, 1), "#11223300"));
        Assert.True(File.Exists(Path.Combine(dir, "payloads", PixelFixture.TransparentHash + ".bin")));

        // Clearing back to transparent re-addresses the retained orphan; it must be verified and reused.
        var second = await Edit(dir, first.State.Snapshot, new PixelEdit.Clear(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1)));

        Assert.True(second.Changed);
        Assert.Equal(3, second.State.Snapshot.Revn);
        Assert.Equal(PixelFixture.BaseDigest, second.State.Snapshot.SourceDigest);
        Assert.Equal(PixelFixture.TransparentHash, Assert.Single(second.State.Layers).PixelsSha256);
    }

    [Fact]
    public async Task EditRejectsReusingASymlinkedPayloadName()
    {
        var (dir, snap, layerId) = BaseFixture();
        var first = await Edit(dir, snap, new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 1, 1), "#11223300"));

        // Replace the retained orphan with a symlink whose target holds the exact same bytes.
        string orphan = Path.Combine(dir, "payloads", PixelFixture.TransparentHash + ".bin");
        File.Delete(orphan);
        string real = Path.Combine(Path.GetDirectoryName(dir)!, "real.bin");
        await File.WriteAllBytesAsync(real, PixelFixture.Bytes(PixelFixture.TransparentHex));
        File.CreateSymbolicLink(orphan, real);

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, first.State.Snapshot, new PixelEdit.Clear(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1))));
        Assert.Equal("PATH_ESCAPE", error.Code);
        // The just-attempted edit did not commit: the asset stays at the first edit's revision.
        Assert.Equal(2, (await _service.InspectAsync(new InspectPixelAssetRequest(dir))).Snapshot.Revn);
    }

    // ---- Budgets ------------------------------------------------------------------------

    [Fact]
    public async Task SeventeenthLayerExceedsTheLayerLimit()
    {
        var (dir, snap, _) = await NewAsset(1, 1);
        var ops = new List<PixelEdit>();
        for (int i = 0; i < 16; i++) ops.Add(new PixelEdit.AddLayer("L" + i)); // 1 + 16 = 17 layers

        var error = await Assert.ThrowsAsync<NativePixelException>(() => Edit(dir, snap, [.. ops]));
        Assert.Equal("RESOURCE_LIMIT", error.Code);
    }

    [Fact]
    public async Task LogicalSourceOverSixtyFourMebibytesIsRejected()
    {
        var (dir, snap, _) = await NewAsset(2048, 2048); // one layer = 16 MiB logical
        // Each added layer adds 16 MiB; a fifth total layer reaches 80 MiB and fails.
        var ops = new PixelEdit[] { new PixelEdit.AddLayer("a"), new PixelEdit.AddLayer("b"), new PixelEdit.AddLayer("c"), new PixelEdit.AddLayer("d") };

        var error = await Assert.ThrowsAsync<NativePixelException>(() => Edit(dir, snap, ops));
        Assert.Equal("RESOURCE_LIMIT", error.Code);
    }

    [Fact]
    public async Task CumulativeEditWorkOverTheLimitIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset(1024, 1024); // area = 1,048,576
        // 17 full-canvas fills = 17,825,792 targeted samples > 16,777,216 (overlap is not refunded).
        var ops = new List<PixelEdit>();
        for (int i = 0; i < 17; i++) ops.Add(new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 1024, 1024), "#01020304"));

        var error = await Assert.ThrowsAsync<NativePixelException>(() => Edit(dir, snap, [.. ops]));
        Assert.Equal("RESOURCE_LIMIT", error.Code);
    }

    [Fact]
    public async Task ReplacementInputOverTheAggregateLimitIsRejected()
    {
        var (dir, snap, layerId) = await NewAsset(2048, 2048);
        var full = RawSampleInput.FromBytes(new byte[2048 * 2048 * 4]); // exactly 16 MiB
        var extra = RawSampleInput.FromBytes(new byte[4]);

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap,
                new PixelEdit.Replace(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 2048, 2048), full),
                new PixelEdit.Replace(Id(layerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1), extra)));
        Assert.Equal("RESOURCE_LIMIT", error.Code);
    }

    // ---- Revision overflow --------------------------------------------------------------

    [Fact]
    public async Task RealChangeAtMaxRevisionFailsBeforeWriting()
    {
        // The source digest omits the revision, so the base digest is valid at any revision.
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace("\"revn\":1", "\"revn\":9223372036854775807");
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        var snap = new PixelSnapshot(Guid.Parse(PixelFixture.AssetId), long.MaxValue, PixelFixture.BaseDigest);

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            Edit(dir, snap, new PixelEdit.FillPixels(Id(Guid.Parse(PixelFixture.LayerId)), new PixelRect(0, 0, 1, 1), "#11223300")));
        Assert.Equal("RESOURCE_LIMIT", error.Code);
        Assert.Equal(long.MaxValue, (await _service.InspectAsync(new InspectPixelAssetRequest(dir))).Snapshot.Revn);
    }

    [Fact]
    public async Task GuardedNoOpAtMaxRevisionSucceeds()
    {
        string dir = CreateTarget();
        string metadata = PixelFixture.BaseMetadata.Replace("\"revn\":1", "\"revn\":9223372036854775807");
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        var snap = new PixelSnapshot(Guid.Parse(PixelFixture.AssetId), long.MaxValue, PixelFixture.BaseDigest);

        var result = await Edit(dir, snap, new PixelEdit.SetVisibility(Id(Guid.Parse(PixelFixture.LayerId)), true));
        Assert.False(result.Changed);
        Assert.Equal(long.MaxValue, result.State.Snapshot.Revn);
    }

    [Fact]
    public async Task HiddenRgbUnderAlphaZeroSurvivesAControlChange()
    {
        var (dir, snap, layerId) = await NewAsset(1, 1);
        var filled = await Edit(dir, snap, new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 1, 1), "#aabbcc00"));

        // A control-only change must not bake or touch the hidden RGB samples.
        await Edit(dir, filled.State.Snapshot, new PixelEdit.SetOpacity(Id(layerId), 0.25m));

        var sampled = Assert.IsType<RgbaRegionResult>(
            await _service.InspectRegionAsync(new InspectPixelRegionRequest(dir, layerId, PixelTarget.Pixels, new PixelRect(0, 0, 1, 1))));
        Assert.Equal("#aabbcc00", Assert.Single(sampled.Samples));
    }

    // ---- Batch count bounds -------------------------------------------------------------

    [Fact]
    public async Task EmptyBatchIsRejected()
    {
        var (dir, snap, _) = await NewAsset();

        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            _service.EditAsync(new EditPixelAssetRequest(dir, snap, ImmutableArray<PixelEdit>.Empty)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    // ---- Write failures on the source-commit path --------------------------------------

    [Fact]
    public async Task EditSurfacesStoreUnavailableWhenThePayloadDirectoryIsUnwritable()
    {
        if (OperatingSystem.IsWindows()) return;
        var (dir, snap, layerId) = BaseFixture();
        string payloads = Path.Combine(dir, "payloads");
        File.SetUnixFileMode(payloads, UnixFileMode.UserRead | UnixFileMode.UserExecute); // r-x, no write.
        try
        {
            // If this user can still create files here (e.g. running as root), the negative test is moot.
            string probe = Path.Combine(payloads, ".probe");
            bool enforced;
            try { await File.WriteAllTextAsync(probe, "x"); File.Delete(probe); enforced = false; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { enforced = true; }
            if (!enforced) return;

            // Publishing the new payload fails; the source path reports STORE_UNAVAILABLE (the output
            // path is what remaps a write failure to PIXEL_WRITE_FAILED).
            var error = await Assert.ThrowsAsync<NativePixelException>(
                () => Edit(dir, snap, new PixelEdit.FillPixels(Id(layerId), new PixelRect(0, 0, 1, 1), "#11223300")));
            Assert.Equal("STORE_UNAVAILABLE", error.Code);
            // Metadata is untouched: the failed commit never advanced the revision or the digest.
            Assert.Equal(PixelFixture.BaseMetadata, await File.ReadAllTextAsync(Path.Combine(dir, "asset.agd-pixels.json")));
        }
        finally
        {
            File.SetUnixFileMode(payloads, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
