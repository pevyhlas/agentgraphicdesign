using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Pixels.Storage;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

[Collection(NativePixelCollection.Name)]
public sealed class NativePixelStorageTests : NativePixelTestBase
{
    private readonly NativePixelService _service = new();

    private string NewAssetDir(out string parent)
    {
        parent = NewWorkspace();
        return Path.Combine(parent, "hero");
    }

    // ---- Intrinsic invalid source -> validate invalid report -----------------------------

    [Theory]
    [InlineData("\"name\":\"Pixel\"", "\"name\":\"Pixel\",\"name\":\"Dup\"", "PARSE_ERROR")]       // duplicate key
    [InlineData("\"revn\":1", "\"revn\":1,\"extra\":1", "PARSE_ERROR")]                             // unknown member
    [InlineData("\"name\":\"Pixel\"", "\"name\":\"Pixel\",\"\\u006eame\":\"Dup\"", "PARSE_ERROR")]  // escaped-duplicate key
    [InlineData(PixelFixture.AssetId, "11111111-1111-4111-8111-11111111111A", "INVALID_ID")]        // uppercase ID
    [InlineData("\"opacity\":1", "\"opacity\":2", "INVALID_PROPERTY")]                              // out-of-range opacity
    public async Task ValidateReportsIntrinsicMetadataFailure(string find, string replace, string code)
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata.Replace(find, replace),
            (PixelFixture.TransparentHash, PixelFixture.TransparentHex));

        var result = await _service.ValidateAsync(new ValidatePixelAssetRequest(dir));
        var invalid = Assert.IsType<InvalidPixelAssetResult>(result);
        Assert.False(invalid.Valid);
        Assert.Equal(code, Assert.Single(invalid.Errors).Code);
    }

    [Fact]
    public async Task ValidateReportsInvalidHashSpelling()
    {
        string dir = NewAssetDir(out _);
        // A 64-length hash with an uppercase digit is invalid spelling; parse fails before payload reads.
        string metadata = PixelFixture.BaseMetadata.Replace(PixelFixture.TransparentHash, PixelFixture.TransparentHash.ToUpperInvariant());
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));

        var invalid = Assert.IsType<InvalidPixelAssetResult>(await _service.ValidateAsync(new ValidatePixelAssetRequest(dir)));
        Assert.Equal("INVALID_PROPERTY", Assert.Single(invalid.Errors).Code);
    }

    [Fact]
    public async Task ValidateReportsMissingPayloadDirectory()
    {
        string dir = NewAssetDir(out _);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "asset.agd-pixels.json"), PixelFixture.BaseMetadata);
        // No payloads directory at all.
        var invalid = Assert.IsType<InvalidPixelAssetResult>(await _service.ValidateAsync(new ValidatePixelAssetRequest(dir)));
        Assert.Equal("PIXEL_INVENTORY_MISMATCH", Assert.Single(invalid.Errors).Code);
    }

    [Fact]
    public async Task ValidateReportsMissingReferencedPayload()
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata); // payloads dir exists but is empty
        var invalid = Assert.IsType<InvalidPixelAssetResult>(await _service.ValidateAsync(new ValidatePixelAssetRequest(dir)));
        Assert.Equal("PIXEL_INVENTORY_MISMATCH", Assert.Single(invalid.Errors).Code);
    }

    [Theory]
    [InlineData("0047445049584c0a0000000100000001000000010000000100000000")]                  // wrong magic
    [InlineData("4147445049584c0a00000001000000010000000100000001")]                          // truncated (no samples)
    [InlineData("4147445049584c0a0000000100000001000000010000000100000000ff")]                // one trailing byte
    [InlineData(PixelFixture.HiddenHex)]                                                       // bytes hash != referenced hash
    [InlineData("4147445049584c0a00000001000000010000000200000001" + "00000000")]             // header width 2 != canvas 1
    public async Task ValidateReportsCorruptPayloadContent(string badHex)
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, badHex));
        var invalid = Assert.IsType<InvalidPixelAssetResult>(await _service.ValidateAsync(new ValidatePixelAssetRequest(dir)));
        Assert.Equal("PIXEL_CONTENT_MISMATCH", Assert.Single(invalid.Errors).Code);
    }

    // ---- Execution failures (NOT an invalid report) --------------------------------------

    [Theory]
    [InlineData("4147445049584c0a00000002000000010000000100000001" + "00000000", "UNSUPPORTED_VERSION")]        // payload version 2
    [InlineData("4147445049584c0a00000001000000030000000100000001" + "00000000", "PIXEL_PROFILE_UNSUPPORTED")]  // unsupported kind 3
    public async Task ValidateThrowsOnUnsupportedPayload(string badHex, string code)
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, badHex));
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.ValidateAsync(new ValidatePixelAssetRequest(dir)));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task InspectThrowsOnCorruptPayloadRatherThanReporting()
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata,
            (PixelFixture.TransparentHash, "0047445049584c0a0000000100000001000000010000000100000000"));
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("PIXEL_CONTENT_MISMATCH", ex.Code);
    }

    // ---- Closure-only reads: never enumerate, repair or delete ---------------------------

    [Fact]
    public async Task ReadValidatesOnlyTheReferencedClosureAndTouchesNothingElse()
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        // Unrelated and stale entries that must be ignored, never opened, repaired or deleted.
        File.WriteAllText(Path.Combine(dir, "README.md"), "notes");
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        File.WriteAllText(Path.Combine(dir, "unknown.txt"), "x");
        File.WriteAllText(Path.Combine(dir, ".agd-pixels-tmp-" + new string('a', 32) + ".tmp"), "stale");
        File.WriteAllText(Path.Combine(dir, "payloads", "deadbeef.bin"), "not a payload");            // unreferenced junk
        File.WriteAllText(Path.Combine(dir, "payloads", new string('f', 64) + ".bin"), "corrupt");    // corrupt but unreferenced

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(PixelFixture.BaseDigest, info.Snapshot.SourceDigest);

        // No repair/deletion of unrelated entries.
        Assert.True(File.Exists(Path.Combine(dir, "README.md")));
        Assert.True(Directory.Exists(Path.Combine(dir, ".git")));
        Assert.True(File.Exists(Path.Combine(dir, "payloads", "deadbeef.bin")));
        Assert.True(File.Exists(Path.Combine(dir, "payloads", new string('f', 64) + ".bin")));
    }

    [Fact]
    public async Task DuplicatePayloadReferenceAcrossLayersIsAllowed()
    {
        string dir = NewAssetDir(out _);
        string metadata = PixelFixture.MultiLayerMetadata(2, 1, 1, PixelFixture.TransparentHash);
        PixelFixture.WriteAsset(dir, metadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));

        var info = await _service.InspectAsync(new InspectPixelAssetRequest(dir));
        Assert.Equal(2, info.Layers.Length);
        Assert.Equal(1 * 1 * 4 * 2, info.LogicalSourceBytes);
        Assert.All(info.Layers, l => Assert.Equal(PixelFixture.TransparentHash, l.PixelsSha256));
    }

    // ---- Resource bounds via on-disk fixtures --------------------------------------------

    [Fact]
    public async Task SixteenLayersIsInclusiveAndSeventeenExceedsTheLimit()
    {
        string ok = NewAssetDir(out _);
        PixelFixture.WriteAsset(ok, PixelFixture.MultiLayerMetadata(16, 1, 1, PixelFixture.TransparentHash),
            (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        Assert.Equal(64, (await _service.InspectAsync(new InspectPixelAssetRequest(ok))).LogicalSourceBytes);

        string over = Path.Combine(NewWorkspace(), "hero");
        PixelFixture.WriteAsset(over, PixelFixture.MultiLayerMetadata(17, 1, 1, PixelFixture.TransparentHash),
            (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(over)));
        Assert.Equal("RESOURCE_LIMIT", ex.Code);
    }

    [Fact]
    public async Task LogicalSourceBoundaryAtSixtyVersusEightyMebibytes()
    {
        string dummyMask = new('a', 64);
        // Three masked 2048x2048 layers = 60 MiB: passes the logical cap, then fails only on the
        // absent referenced payloads (proving the logical check was cleared).
        string sixty = NewAssetDir(out _);
        PixelFixture.WriteAsset(sixty, PixelFixture.MultiLayerMetadata(3, 2048, 2048, PixelFixture.TransparentHash, dummyMask));
        var passed = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(sixty)));
        Assert.Equal("PIXEL_INVENTORY_MISMATCH", passed.Code);

        // Four masked layers = 80 MiB: rejected at the logical cap before any payload read.
        string eighty = Path.Combine(NewWorkspace(), "hero");
        PixelFixture.WriteAsset(eighty, PixelFixture.MultiLayerMetadata(4, 2048, 2048, PixelFixture.TransparentHash, dummyMask));
        var rejected = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(eighty)));
        Assert.Equal("RESOURCE_LIMIT", rejected.Code);
    }

    // ---- Filesystem boundary -------------------------------------------------------------

    [Fact]
    public async Task RejectsAnAssetDirectorySymlink()
    {
        string parent = NewWorkspace();
        string real = Path.Combine(parent, "real");
        PixelFixture.WriteAsset(real, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        string link = Path.Combine(parent, "hero");
        Directory.CreateSymbolicLink(link, real);

        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(link)));
        Assert.Equal("PATH_ESCAPE", ex.Code);
    }

    [Fact]
    public async Task RejectsAMetadataSymlink()
    {
        string dir = NewAssetDir(out string parent);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        string metadata = Path.Combine(dir, "asset.agd-pixels.json");
        string elsewhere = Path.Combine(parent, "elsewhere.json");
        File.WriteAllText(elsewhere, PixelFixture.BaseMetadata);
        File.Delete(metadata);
        File.CreateSymbolicLink(metadata, elsewhere);

        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("PATH_ESCAPE", ex.Code);
    }

    [Fact]
    public async Task RejectsAPayloadsPathThatIsNotADirectory()
    {
        string dir = NewAssetDir(out _);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "asset.agd-pixels.json"), PixelFixture.BaseMetadata);
        File.WriteAllText(Path.Combine(dir, "payloads"), "not a directory");
        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("PATH_ESCAPE", ex.Code);
    }

    [Fact]
    public async Task RejectsAReferencedPayloadSymlink()
    {
        string dir = NewAssetDir(out string parent);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata);
        string payload = Path.Combine(dir, "payloads", PixelFixture.TransparentHash + ".bin");
        string elsewhere = Path.Combine(parent, "real.bin");
        File.WriteAllBytes(elsewhere, PixelFixture.Bytes(PixelFixture.TransparentHex));
        File.CreateSymbolicLink(payload, elsewhere);

        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("PATH_ESCAPE", ex.Code);
    }

    [Fact]
    public async Task RejectsAReferencedPayloadThatIsAFifo()
    {
        string dir = NewAssetDir(out _);
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata);
        string payload = Path.Combine(dir, "payloads", PixelFixture.TransparentHash + ".bin");
        if (!PixelFixture.TryMakeFifo(payload))
            return; // mkfifo unavailable on this host; the symlink/type-mismatch cases still cover the boundary.

        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.InspectAsync(new InspectPixelAssetRequest(dir)));
        Assert.Equal("PATH_ESCAPE", ex.Code);
    }

    // ---- Create arbitration --------------------------------------------------------------

    [Fact]
    public async Task ConcurrentCreatesSerializeAndOnlyOneWins()
    {
        string dir = NewAssetDir(out _);
        using var barrier = new Barrier(2);
        var succeeded = new bool[2];
        var failures = new NativePixelException?[2];

        void Attempt(int index)
        {
            barrier.SignalAndWait();
            try
            {
                using var scope = NativeOperationScope.EnterUnadmitted(default);
                NativePixelStore.Create(dir, "Race", 4, 4, scope);
                succeeded[index] = true;
            }
            catch (NativePixelException error)
            {
                failures[index] = error;
            }
        }

        await Task.WhenAll(Task.Run(() => Attempt(0)), Task.Run(() => Attempt(1)));

        Assert.Equal(1, succeeded.Count(x => x));
        var loser = Assert.Single(failures, f => f is not null);
        Assert.Equal("PIXEL_ALREADY_EXISTS", loser!.Code);
        Assert.True(File.Exists(Path.Combine(dir, "asset.agd-pixels.json")));
    }

    [Fact]
    public async Task CreateWaitsForAContendedWriterLockThenPublishes()
    {
        string dir = NewAssetDir(out string parent);
        string lockPath = PixelFixture.LockPath(parent, "hero");

        // Hold the exact sibling writer lock from "another writer". CreateAsync runs its work
        // synchronously, so Task.Run keeps it off the test thread while it blocks on the lock.
        var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var create = Task.Run(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "Waited", 2, 2)));
        await Task.Delay(400);
        Assert.False(create.IsCompleted); // blocked on the contended lock, not expired

        held.Dispose(); // release -> the waiting create acquires and publishes
        var result = await create;
        Assert.Equal(1, result.State.Snapshot.Revn);
        Assert.True(File.Exists(Path.Combine(dir, "asset.agd-pixels.json")));
    }

    [Fact]
    public async Task WriterLockWaitExpiryReportsBusyWithTheFixedMessage()
    {
        string dir = NewAssetDir(out string parent);
        string lockPath = PixelFixture.LockPath(parent, "hero");
        using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var ex = await Assert.ThrowsAsync<NativePixelException>(() => _service.CreateAsync(new CreatePixelAssetRequest(dir, "Blocked", 2, 2)));
        Assert.Equal("PIXEL_BUSY", ex.Code);
        Assert.Equal("Asset writer lock wait expired.", ex.Message);
    }

    // ---- Operation scope / admission unit checks -----------------------------------------

    [Fact]
    public void DeadlineExhaustionIsResourceLimitButCallerCancellationTakesPrecedence()
    {
        using (var expired = NativeOperationScope.EnterUnadmitted(default, TimeSpan.Zero))
        {
            var ex = Assert.Throws<NativePixelException>(expired.Check);
            Assert.Equal("RESOURCE_LIMIT", ex.Code);
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var scope = NativeOperationScope.EnterUnadmitted(cancelled.Token, TimeSpan.Zero);
        Assert.Throws<OperationCanceledException>(scope.Check);
    }

    [Fact]
    public void EnterHonorsCallerCancellationBeforeTakingTheSlot()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => NativeOperationScope.Enter(cancelled.Token));
    }

    [Fact]
    public void SecondConcurrentAdmissionIsImmediatelyBusy()
    {
        using var first = NativeOperationScope.Enter(default);
        var ex = Assert.Throws<NativePixelException>(() => NativeOperationScope.Enter(default));
        Assert.Equal("PIXEL_BUSY", ex.Code);
        Assert.Equal("Native operation slot is busy.", ex.Message);
    }

    [Fact]
    public void WorkingReservationRejectsOverTwoHundredFiftySixMebibytes()
    {
        using var scope = NativeOperationScope.EnterUnadmitted(default);
        scope.Reserve(NativeOperationScope.MaxReservationBytes); // inclusive boundary is fine
        var ex = Assert.Throws<NativePixelException>(() => scope.Reserve(1));
        Assert.Equal("RESOURCE_LIMIT", ex.Code);
    }
}
