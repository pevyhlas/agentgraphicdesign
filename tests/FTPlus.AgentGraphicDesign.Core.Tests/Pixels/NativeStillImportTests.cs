using System.Text;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Pixels.Storage;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

[Collection(NativePixelCollection.Name)]
public sealed class NativeStillImportTests : NativePixelTestBase
{
    private readonly NativePixelService _service = new();

    [Theory]
    [InlineData(3, 2)]
    [InlineData(1, 17)]
    [InlineData(17, 1)]
    public async Task ImportPreservesExactSourceHistoryAndHiddenRgbAcrossSupplierDeletion(int width, int height)
    {
        string root = NewWorkspace(), bundle = Path.Combine(root, "supplier"), asset = Path.Combine(root, "chosen");
        byte[] raw = Enumerable.Range(0, width * height).SelectMany(i => new byte[] { (byte)(i + 17), 34, 51, (byte)(i % 2 == 0 ? 0 : 128) }).ToArray();
        string descriptor = StillImportFixture.Write(bundle, width, height, raw, "pixels/rest.png");
        byte[] originalMetadata = File.ReadAllBytes(descriptor), originalPng = File.ReadAllBytes(Path.Combine(bundle, "pixels/rest.png"));
        var result = await _service.ImportAsync(new(asset, descriptor, "Chosen", "Imported"));
        Assert.Equal(1, result.State.Snapshot.Revn);
        Assert.NotEqual(result.State.Snapshot.AssetId, result.InitialLayerId);
        Assert.Equal(result.InitialLayerId, Assert.Single(result.State.Layers).Id);
        Assert.False(result.Import.Admitted);
        Assert.False(result.Import.ProvenanceVerified);
        Assert.Equal(StillImportFixture.Hash(raw), result.Import.RgbaSha256);
        Assert.Equal(StillImportFixture.Hash(originalMetadata), result.Import.DescriptorSha256);
        Assert.Equal(StillImportFixture.Hash(originalPng), result.Import.EncodedSha256);
        string recordPath = Path.Combine(asset, result.Import.RecordPath);
        string history = Path.GetDirectoryName(recordPath)!;
        Assert.Equal(originalMetadata, File.ReadAllBytes(Path.Combine(history, "bundle/descriptor.json")));
        Assert.Equal(originalPng, File.ReadAllBytes(Path.Combine(history, "bundle/pixels/rest.png")));
        byte[] recordBytes = File.ReadAllBytes(recordPath);
        Assert.Equal(result.Import.RecordSha256, StillImportFixture.Hash([.. Encoding.ASCII.GetBytes("agd.pixel-still-import.record.v1\n"), .. recordBytes]));
        using var record = JsonDocument.Parse(recordBytes);
        Assert.Equal("tiny", record.RootElement.GetProperty("source").GetProperty("assetId").GetString());
        Assert.Equal(result.InitialLayerId, record.RootElement.GetProperty("mapping").GetProperty("layerId").GetGuid());
        Assert.Equal(0m, record.RootElement.GetProperty("source").GetProperty("pivot").GetProperty("y").GetDecimal());
        Assert.DoesNotContain("unverified-maker", Encoding.UTF8.GetString(recordBytes));
        Assert.DoesNotContain(root, Encoding.UTF8.GetString(recordBytes));
        var framed = File.ReadAllBytes(Path.Combine(asset, "payloads", result.Import.PixelsSha256 + ".bin"));
        Assert.Equal(raw, framed[24..]);
        Assert.Equal(StillImportFixture.Hash(framed), result.Import.PixelsSha256);
        Assert.NotEqual(result.Import.RgbaSha256, result.Import.PixelsSha256);
        Directory.Delete(bundle, recursive: true);
        var samples = Assert.IsType<RgbaRegionResult>(await new NativePixelService().InspectRegionAsync(
            new(asset, result.InitialLayerId, PixelTarget.Pixels, new(0, 0, width, height))));
        Assert.Equal(Enumerable.Range(0, width * height).Select(i => "#" + Convert.ToHexStringLower(raw.AsSpan(i * 4, 4))), samples.Samples);
        Assert.IsType<ValidPixelAssetResult>(await _service.ValidateAsync(new(asset)));
        Assert.Empty(Directory.GetDirectories(root, ".agd-pixels-stage-*"));
        Assert.Empty(Directory.GetDirectories(asset, ".agd-pixels-import-stage-*"));
    }

    [Fact]
    public async Task TwoLayerImportEditMaskExportAndExactRestoreKeepNativeAuthority()
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        string red = StillImportFixture.Write(Path.Combine(root, "red"), 1, 1, [255, 0, 0, 255]);
        string green = StillImportFixture.Write(Path.Combine(root, "green"), 1, 1, [0, 255, 0, 128]);
        var initial = await _service.ImportAsync(new(asset, red, "Color", "Red"));
        var originalFiles = Snapshot(asset);
        var appended = await _service.ImportLayerAsync(new(asset, green, initial.State.Snapshot, "Green"));
        Assert.Equal(initial.State.Snapshot, appended.PreviousSnapshot);
        Assert.Equal(2, appended.State.Snapshot.Revn);
        Assert.Equal(initial.InitialLayerId, appended.State.Layers[0].Id);
        Assert.Equal(appended.LayerId, appended.State.Layers[1].Id);
        string output = Path.Combine(root, "linear-over.png");
        var exported = await _service.ExportAsync(new(asset, output));
        Assert.Equal(StillImportFixture.Hash([187, 188, 0, 255]), exported.RgbaSha256);
        Assert.True((await _service.CheckExportAsync(new(asset, output))).Matches);
        Assert.Equal(exported.RgbaSha256, (await _service.PreviewAsync(new(asset, new PixelPreviewDelivery.Inline()))).RgbaSha256);
        var edited = await _service.EditAsync(new(asset, appended.State.Snapshot,
            [new PixelEdit.AttachMask(new PixelLayerRef.ById(appended.LayerId)),
             new PixelEdit.FillMask(new PixelLayerRef.ById(appended.LayerId), new(0, 0, 1, 1), 0)]));
        Assert.Equal(StillImportFixture.Hash([255, 0, 0, 255]), (await _service.PreviewAsync(new(asset, new PixelPreviewDelivery.Inline()))).RgbaSha256);
        Assert.False((await _service.CheckExportAsync(new(asset, output))).Matches);
        Assert.Equal(3, edited.State.Snapshot.Revn);
        foreach (var (path, bytes) in originalFiles) File.WriteAllBytes(Path.Combine(asset, path), bytes);
        Assert.Equal(initial.State.Snapshot, (await new NativePixelService().InspectAsync(new(asset))).Snapshot);
        // Exactly restored tokens are eligible again; no history authority or anti-rollback cache.
        var restoredAppend = await new NativePixelService().ImportLayerAsync(new(asset, green, initial.State.Snapshot, "Restored green"));
        Assert.Equal(2, restoredAppend.State.Snapshot.Revn);
        Assert.NotEqual(appended.LayerId, restoredAppend.LayerId);
        foreach (var (path, bytes) in originalFiles.Where(entry => entry.Key != "asset.agd-pixels.json"))
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(asset, path)));
    }

    [Fact]
    public void CanonicalRecordMatchesIndependentLiteralByteAndDomainHashVector()
    {
        string path = Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "tests/FTPlus.AgentGraphicDesign.Core.Tests/Pixels/Fixtures/import-record-v1.json");
        byte[] fixture = File.ReadAllBytes(path);
        var model = new ProvidedArtifactDescriptor
        {
            Schema = ArtifactDescriptorContract.Schema,
            Version = 1,
            Profile = ArtifactDescriptorContract.Profile,
            AssetId = "tiny",
            Canvas = new(1, 1, ArtifactDescriptorContract.Coordinates),
            CropOrigin = new(0, 0),
            Files = [new(new string('c', 64), "image.png", 73)],
            Still = new("rest", new string('c', 64), new(0.500000m, 0.500000m))
        };
        // Synthetic hashes/bytes qualify format spelling, not ingestion or a verified artifact.
        var input = new CapturedStill([], model, new byte[73], [], new string('b', 64), new string('a', 64), new string('d', 64));
        using var scope = NativeOperationScope.EnterUnadmitted(default);
        byte[] record = NativeImportRecord.Serialize(input, Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"), new string('f', 64), scope);
        Assert.Equal(fixture[..^1], record);
        Assert.Equal(1020, record.Length);
        Assert.Equal("aa4104fdea22ccef905a57a44228b7206f693602e4a4384f180307c6eb6185b5", NativeImportRecord.Identity(record));
    }

    [Theory]
    [InlineData("duplicate", "PARSE_ERROR")]
    [InlineData("unknown", "PARSE_ERROR")]
    [InlineData("version", "UNSUPPORTED_VERSION")]
    [InlineData("limit", "RESOURCE_LIMIT")]
    [InlineData("prefix", "ARTIFACT_PROFILE_UNSUPPORTED")]
    [InlineData("decode", "ARTIFACT_PNG_INVALID")]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("extra", "ARTIFACT_INVENTORY_MISMATCH")]
    public async Task FailedIntakeLeavesPriorSourceHistoryAndReferencesIntact(string fault, string code)
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        string good = StillImportFixture.Write(Path.Combine(root, "good"), 1, 1, [17, 34, 51, 0]);
        var initial = await _service.ImportAsync(new(asset, good, "Asset", "Initial"));
        string bundle = Path.Combine(root, "fault");
        string path = StillImportFixture.Write(bundle, 1, 1, [1, 2, 3, 255], fault == "prefix" ? "descriptor.json/image.png" : "image.png", fault == "decode");
        string metadata = File.ReadAllText(path);
        metadata = fault switch
        {
            "duplicate" => metadata.Replace("\"version\":1", "\"version\":1,\"\\u0076ersion\":1"),
            "unknown" => metadata.Replace("\"version\":1", "\"version\":1,\"extra\":true"),
            "version" => metadata.Replace("\"version\":1", "\"version\":2"),
            "limit" => metadata.Replace("\"width\":1,\"height\":1", "\"width\":8192,\"height\":513"),
            _ => metadata
        };
        File.WriteAllText(path, metadata, new UTF8Encoding(false));
        if (fault == "hash") File.WriteAllBytes(Path.Combine(bundle, "image.png"), new byte[File.ReadAllBytes(Path.Combine(bundle, "image.png")).Length]);
        if (fault == "extra") File.WriteAllText(Path.Combine(bundle, "unexpected"), "unexpected");
        var before = Snapshot(asset);
        var error = await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, path, initial.State.Snapshot, "Fault")));
        Assert.Equal(code, error.Code);
        AssertSnapshots(before, Snapshot(asset));
        Assert.Equal(initial.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        Assert.DoesNotContain(root, error.Message);
        Assert.Empty(Directory.GetDirectories(asset, ".agd-pixels-import-stage-*"));
        string absent = Path.Combine(root, "not-created");
        await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportAsync(new(absent, path, "Failure", "Failure")));
        Assert.False(Directory.Exists(absent));
        Assert.Empty(Directory.GetDirectories(root, ".agd-pixels-stage-*"));
    }

    [Fact]
    public async Task ConflictAndCapacityRejectBeforeSupplierReadsAndBusyCancellationReleaseSlot()
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        var created = await _service.CreateAsync(new(asset, "Asset", 1, 1));
        var wrong = created.State.Snapshot with { SourceDigest = new string('a', 64) };
        var conflict = await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, Path.Combine(root, "missing"), wrong, "New")));
        Assert.Equal("REVISION_CONFLICT", conflict.Code);
        var full = await _service.EditAsync(new(asset, created.State.Snapshot, [.. Enumerable.Range(1, 15).Select(i => new PixelEdit.AddLayer("Layer" + i))]));
        Assert.Equal("RESOURCE_LIMIT", (await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, Path.Combine(root, "missing"), full.State.Snapshot, "New")))).Code);
        using (NativeOperationScope.Enter(default))
            Assert.Equal("PIXEL_BUSY", (await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportAsync(null!))).Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ImportAsync(null!, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(full.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
    }

    [Fact]
    public void AddressedHistoryReusesOnlyCompleteExactClosureAndNeverRepairsCorruption()
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "asset");
        Directory.CreateDirectory(asset);
        string path = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [1, 2, 3, 0]);
        using var scope = NativeOperationScope.EnterUnadmitted(default);
        var input = NativeStillInput.Capture(path, scope);
        byte[] record = NativeImportRecord.Serialize(input, Guid.NewGuid(), Guid.NewGuid(), PixelFixture.HiddenHash, scope);
        string identity = NativeImportRecord.Identity(record);
        NativePixelStore.PreserveImport(asset, input, record, identity, scope);
        var before = Snapshot(asset);
        NativePixelStore.PreserveImport(asset, input, record, identity, scope);
        AssertSnapshots(before, Snapshot(asset));
        string target = Path.Combine(asset, "imports", identity);
        File.WriteAllText(Path.Combine(target, "unexpected"), "sentinel");
        var unknown = Assert.Throws<NativePixelException>(() => NativePixelStore.PreserveImport(asset, input, record, identity, scope));
        Assert.Equal("PIXEL_INVENTORY_MISMATCH", unknown.Code);
        Assert.Equal("sentinel", File.ReadAllText(Path.Combine(target, "unexpected")));
        File.Delete(Path.Combine(target, "unexpected"));
        File.AppendAllText(Path.Combine(target, "bundle/descriptor.json"), " ");
        var mismatch = Assert.Throws<NativePixelException>(() => NativePixelStore.PreserveImport(asset, input, record, identity, scope));
        Assert.Equal("PIXEL_CONTENT_MISMATCH", mismatch.Code);
        Assert.Empty(Directory.GetDirectories(asset, ".agd-pixels-import-stage-*"));
    }

    private static Dictionary<string, byte[]> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertSnapshots(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

    [Fact]
    public void CapturedInputsRemainPrivateWhenSupplierFilesAreMutatedBeforeRetention()
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        Directory.CreateDirectory(asset);
        string path = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        byte[] originalMetadata = File.ReadAllBytes(path), originalPng = File.ReadAllBytes(Path.Combine(root, "supplier/image.png"));
        using var scope = NativeOperationScope.EnterUnadmitted(default);
        var captured = NativeStillInput.Capture(path, scope);
        File.WriteAllText(path, "replaced supplier metadata");
        File.WriteAllText(Path.Combine(root, "supplier/image.png"), "replaced supplier bytes");
        byte[] record = NativeImportRecord.Serialize(captured, Guid.NewGuid(), Guid.NewGuid(), PixelFixture.HiddenHash, scope);
        string identity = NativeImportRecord.Identity(record);
        NativePixelStore.PreserveImport(asset, captured, record, identity, scope);
        string history = Path.Combine(asset, "imports", identity, "bundle");
        Assert.Equal(originalMetadata, File.ReadAllBytes(Path.Combine(history, "descriptor.json")));
        Assert.Equal(originalPng, File.ReadAllBytes(Path.Combine(history, "image.png")));
        Assert.Equal(new byte[] { 17, 34, 51, 0 }, captured.Rgba);
    }

    [Theory]
    [InlineData("metadata-link")]
    [InlineData("png-link")]
    [InlineData("metadata-fifo")]
    [InlineData("png-fifo")]
    [InlineData("imports-link")]
    [InlineData("imports-file")]
    public async Task UnsafeInputAndReservedHistoryTypesRejectWithoutChangingChosenSources(string fault)
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        var created = await _service.CreateAsync(new(asset, "Asset", 1, 1));
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        string png = Path.Combine(root, "supplier/image.png");
        if (fault.StartsWith("metadata", StringComparison.Ordinal) || fault.StartsWith("png", StringComparison.Ordinal))
        {
            string leaf = fault.StartsWith("metadata", StringComparison.Ordinal) ? descriptor : png;
            string saved = Path.Combine(root, "saved-file");
            File.Move(leaf, saved);
            if (fault.EndsWith("link", StringComparison.Ordinal)) File.CreateSymbolicLink(leaf, saved);
            else Assert.True(PixelFixture.TryMakeFifo(leaf));
        }
        else if (fault == "imports-link") Directory.CreateSymbolicLink(Path.Combine(asset, "imports"), root);
        else File.WriteAllText(Path.Combine(asset, "imports"), "unrelated sentinel");
        byte[] before = File.ReadAllBytes(Path.Combine(asset, "asset.agd-pixels.json"));
        var error = await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Fault")));
        Assert.Equal("PATH_ESCAPE", error.Code);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(asset, "asset.agd-pixels.json")));
        Assert.Equal(created.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
    }

    [Fact]
    public async Task ActualPayloadPublicationPermissionFailureLeavesOldReferencesAndCompleteHistoricalOrphan()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        var created = await _service.CreateAsync(new(asset, "Asset", 1, 1));
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        var before = Snapshot(asset);
        string payloads = Path.Combine(asset, "payloads");
        File.SetUnixFileMode(payloads, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // Actual no-replace rename failure after complete history publication, not an injected throw.
            var error = await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Fault")));
            Assert.Equal("STORE_UNAVAILABLE", error.Code);
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(asset, path)));
            Assert.Equal(created.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
            string orphan = Assert.Single(Directory.GetDirectories(Path.Combine(asset, "imports")));
            byte[] record = File.ReadAllBytes(Path.Combine(orphan, "record.json"));
            Assert.Equal(Path.GetFileName(orphan), NativeImportRecord.Identity(record));
            Assert.Equal(File.ReadAllBytes(descriptor), File.ReadAllBytes(Path.Combine(orphan, "bundle/descriptor.json")));
            Assert.Empty(Directory.GetDirectories(asset, ".agd-pixels-import-stage-*"));
            Assert.Empty(Directory.GetFiles(asset, ".agd-pixels-tmp-*"));
        }
        finally
        {
            File.SetUnixFileMode(payloads, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.Equal(2, (await _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Retry as a new edit"))).State.Snapshot.Revn);
    }

    [Fact]
    public async Task ClipDeclaredEncodedSizeCanvasMismatchAndLexicalEscapeFailClosed()
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        var created = await _service.CreateAsync(new(asset, "Asset", 1, 1));
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        var model = ArtifactDescriptorContract.Parse(File.ReadAllBytes(descriptor)).Descriptor!;
        var clip = model with { Still = null, Clip = new("walk", "front", "normal", 1, "forward-once-hold", [new("first", model.Files[0].Sha256, 1, new(0, 0))]) };
        File.WriteAllBytes(descriptor, ArtifactDescriptorContract.SerializeCanonical(clip));
        Assert.Equal("ARTIFACT_PROFILE_UNSUPPORTED", (await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Clip")))).Code);
        File.WriteAllBytes(descriptor, ArtifactDescriptorContract.SerializeCanonical(model with { Files = [model.Files[0] with { ByteLength = 33_554_433 }] }));
        Assert.Equal("RESOURCE_LIMIT", (await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Big")))).Code);
        StillImportFixture.Write(Path.Combine(root, "supplier"), 2, 1, [1, 2, 3, 0, 4, 5, 6, 255]);
        Assert.Equal("INVALID_ARGUMENTS", (await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Mismatch")))).Code);
        string confined = Path.Combine(root, "other-root");
        Directory.CreateDirectory(confined);
        Assert.Equal("PATH_ESCAPE", (await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, created.State.Snapshot, "Escape"), pathRoot: confined))).Code);
        Assert.Equal(created.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        Assert.False(Directory.Exists(Path.Combine(asset, "imports")));
    }

    [Fact]
    public void ReservationAndDeadlineRefusalOccurBeforeInputCaptureAndPreserveCallerToken()
    {
        string root = NewWorkspace();
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        using (var full = NativeOperationScope.EnterUnadmitted(default))
        {
            full.Reserve(NativeOperationScope.MaxReservationBytes);
            Assert.Equal("RESOURCE_LIMIT", Assert.Throws<NativePixelException>(() => NativeStillInput.Capture(descriptor, full)).Code);
        }
        using (var expired = NativeOperationScope.EnterUnadmitted(default, TimeSpan.Zero))
        {
            Thread.Sleep(20);
            Assert.Equal("RESOURCE_LIMIT", Assert.Throws<NativePixelException>(() => NativeStillInput.Capture(descriptor, expired)).Code);
        }
        using var cancellation = new CancellationTokenSource();
        using var canceled = NativeOperationScope.EnterUnadmitted(cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => NativeStillInput.Capture(descriptor, canceled)).CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreexistingFifoLockRejectsBeforeMissingSupplierReadAndPreservesSlotAndSource(bool append)
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        CreatePixelAssetResult? existing = append ? await _service.CreateAsync(new(asset, "Asset", 1, 1)) : null;
        string lockPath = PixelFixture.LockPath(root, "chosen");
        if (File.Exists(lockPath)) File.Delete(lockPath); // paused fixture, not an AGD cleanup operation.
        Assert.True(PixelFixture.TryMakeFifo(lockPath));
        var before = append ? Snapshot(asset) : null;
        string missing = Path.Combine(root, "missing-supplier.json");
        var error = append
            ? await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, missing, existing!.State.Snapshot, "New")))
            : await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportAsync(new(asset, missing, "Asset", "New")));
        Assert.Equal("PATH_ESCAPE", error.Code);
        Assert.True(File.Exists(lockPath));
        if (append) AssertSnapshots(before!, Snapshot(asset));
        else Assert.False(Directory.Exists(asset));
        File.Delete(lockPath); // only the fixture replaces its preexisting FIFO after proving retention.
        if (append) Assert.Equal(existing!.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        else Assert.Equal(1, (await _service.CreateAsync(new(asset, "Slot usable", 1, 1))).State.Snapshot.Revn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OccupiedStagingCollisionNeverDeletesUnownedSentinelForEitherImportPath(bool append)
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        CreatePixelAssetResult? existing = append ? await _service.CreateAsync(new(asset, "Asset", 1, 1)) : null;
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        const string nonce = "11111111111111111111111111111111";
        string staging = Path.Combine(append ? asset : root, append ? ".agd-pixels-import-stage-" + nonce : ".agd-pixels-stage-" + nonce);
        Directory.CreateDirectory(staging);
        string sentinel = Path.Combine(staging, "unrelated-sentinel");
        File.WriteAllText(sentinel, "never owned by this operation");
        byte[]? before = append ? File.ReadAllBytes(Path.Combine(asset, "asset.agd-pixels.json")) : null;
        using (var scope = NativeOperationScope.EnterUnadmitted(default))
        {
            var error = append
                ? Assert.Throws<NativePixelException>(() => NativePixelStore.ImportLayer(new(asset, descriptor, existing!.State.Snapshot, "New"), scope, nonce))
                : Assert.Throws<NativePixelException>(() => NativePixelStore.Import(new(asset, descriptor, "Asset", "New"), scope, nonce));
            Assert.Equal("STORE_UNAVAILABLE", error.Code);
        }
        Assert.Equal("never owned by this operation", File.ReadAllText(sentinel));
        if (append)
        {
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(asset, "asset.agd-pixels.json")));
            Assert.Equal(existing!.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        }
        else Assert.False(Directory.Exists(asset));
    }

    [Fact]
    public async Task RawNativeSourceReadAccessFailureUsesStoreUnavailableAndLeavesStateRecoverable()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        var existing = await _service.CreateAsync(new(asset, "Asset", 1, 1));
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        string metadata = Path.Combine(asset, "asset.agd-pixels.json");
        var before = Snapshot(asset);
        var mode = File.GetUnixFileMode(metadata);
        File.SetUnixFileMode(metadata, UnixFileMode.None);
        try
        {
            var error = await Assert.ThrowsAsync<NativePixelException>(() => _service.ImportLayerAsync(new(asset, descriptor, existing.State.Snapshot, "New")));
            Assert.Equal("STORE_UNAVAILABLE", error.Code);
        }
        finally { File.SetUnixFileMode(metadata, mode); }
        AssertSnapshots(before, Snapshot(asset));
        Assert.Equal(existing.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        Assert.Equal(2, (await _service.ImportLayerAsync(new(asset, descriptor, existing.State.Snapshot, "Recovered"))).State.Snapshot.Revn);
    }
}
