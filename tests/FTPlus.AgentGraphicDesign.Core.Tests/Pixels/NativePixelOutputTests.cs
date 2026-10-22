using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

/// <summary>
/// Preview / export / check (P6, archived plan slice 5). Golden cross-checks tie the whole output pipeline
/// back to the renderer's independent 1x1 transparent composite hash, and the output-boundary,
/// overwrite and snapshot-precondition rules are exercised on the real Linux x64 backend.
/// </summary>
[Collection(NativePixelCollection.Name)]
public sealed class NativePixelOutputTests : NativePixelTestBase
{
    // The canonical composite RGBA SHA-256 of a 1x1 transparent canvas (alpha-zero RGB cleaned to
    // zero), taken verbatim from NativePixelRendererTests; never derived here by production code.
    private const string TransparentCompositeHash = "df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119";

    // An independent literal 1x1 opaque-red PNG (decodes to [255,0,0,255]).
    private const string RedPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/AP8AAP8FAAH/+lyI0QAAAABJRU5ErkJggg==";

    private readonly NativePixelService _service = new();

    private (string Dir, PixelSnapshot Snapshot) BaseAsset()
    {
        string dir = Path.Combine(NewWorkspace(), "hero");
        PixelFixture.WriteAsset(dir, PixelFixture.BaseMetadata, (PixelFixture.TransparentHash, PixelFixture.TransparentHex));
        return (dir, new PixelSnapshot(Guid.Parse(PixelFixture.AssetId), 1, PixelFixture.BaseDigest));
    }

    // ---- Golden preview -----------------------------------------------------------------

    [Fact]
    public async Task PreviewInlineReproducesGoldenCompositeAndDecodableBytes()
    {
        var (dir, _) = BaseAsset();

        var result = await _service.PreviewAsync(new PreviewPixelAssetRequest(dir, new PixelPreviewDelivery.Inline()));

        Assert.Equal(PixelPreviewDeliveryKind.Inline, result.Delivery);
        Assert.Equal(TransparentCompositeHash, result.RgbaSha256);
        Assert.Equal(new PixelRect(0, 0, 1, 1), result.Rect);
        Assert.Equal("image/png", result.MediaType);
        Assert.Equal("agd.pixels.png.v1", result.EncoderProfile);
        Assert.False(result.PngBytes.IsDefaultOrEmpty);
        Assert.Equal(result.PngBytes.Length, result.EncodedBytes);
        Assert.Equal(Sha256Hex(result.PngBytes.ToArray()), result.FileSha256);

        // The inline bytes are a real PNG that independently decodes to the same composite.
        var observed = DecodePng(result.PngBytes.ToArray());
        Assert.Equal((1, 1, TransparentCompositeHash), observed);
    }

    [Fact]
    public async Task PreviewFileDeliveryWritesTheFileAndReturnsNoBytes()
    {
        var (dir, _) = BaseAsset();
        string output = Path.Combine(NewWorkspace(), "preview.png");

        var result = await _service.PreviewAsync(
            new PreviewPixelAssetRequest(dir, new PixelPreviewDelivery.File(output)));

        Assert.Equal(PixelPreviewDeliveryKind.File, result.Delivery);
        Assert.True(result.PngBytes.IsDefaultOrEmpty);
        Assert.True(File.Exists(output));
        Assert.Equal((1, 1, TransparentCompositeHash), DecodePng(File.ReadAllBytes(output)));
    }

    [Fact]
    public async Task PreviewWholeCanvasOverThePixelCapIsResourceLimit_ButAnExplicitRegionSucceeds()
    {
        string dir = Path.Combine(NewWorkspace(), "big");
        await _service.CreateAsync(new CreatePixelAssetRequest(dir, "Big", 1100, 1000)); // 1,100,000 > 1,048,576

        var over = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.PreviewAsync(new PreviewPixelAssetRequest(dir, new PixelPreviewDelivery.Inline())));
        Assert.Equal("RESOURCE_LIMIT", over.Code);

        var region = await _service.PreviewAsync(new PreviewPixelAssetRequest(
            dir, new PixelPreviewDelivery.Inline(), new PixelRect(0, 0, 16, 16)));
        Assert.Equal(new PixelRect(0, 0, 16, 16), region.Rect);
        Assert.False(region.PngBytes.IsDefaultOrEmpty);
    }

    // ---- Golden export ------------------------------------------------------------------

    [Fact]
    public async Task ExportWritesADecodablePngMatchingTheGoldenComposite()
    {
        var (dir, _) = BaseAsset();
        string output = Path.Combine(NewWorkspace(), "out.png");

        var result = await _service.ExportAsync(new ExportPixelAssetRequest(dir, output));

        Assert.Equal(TransparentCompositeHash, result.RgbaSha256);
        Assert.Equal(new PixelRect(0, 0, 1, 1), result.Rect);
        byte[] onDisk = File.ReadAllBytes(output);
        Assert.Equal(onDisk.Length, result.EncodedBytes);
        Assert.Equal(Sha256Hex(onDisk), result.FileSha256);
        Assert.Equal((1, 1, TransparentCompositeHash), DecodePng(onDisk));
    }

    [Fact]
    public async Task ExportRefusesAnExistingDestinationUnlessOverwrite()
    {
        var (dir, _) = BaseAsset();
        string output = Path.Combine(NewWorkspace(), "out.png");
        await File.WriteAllBytesAsync(output, [1, 2, 3]);

        var conflict = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.ExportAsync(new ExportPixelAssetRequest(dir, output)));
        Assert.Equal("PIXEL_DESTINATION_EXISTS", conflict.Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(output)); // Untouched.

        await _service.ExportAsync(new ExportPixelAssetRequest(dir, output, Overwrite: true));
        Assert.Equal((1, 1, TransparentCompositeHash), DecodePng(File.ReadAllBytes(output)));
    }

    // ---- Golden check -------------------------------------------------------------------

    [Fact]
    public async Task CheckOfAFaithfulExportMatches()
    {
        var (dir, _) = BaseAsset();
        string output = Path.Combine(NewWorkspace(), "out.png");
        await _service.ExportAsync(new ExportPixelAssetRequest(dir, output));

        var check = await _service.CheckExportAsync(new CheckPixelExportRequest(dir, output));

        Assert.True(check.Matches);
        Assert.Empty(check.Differences);
        Assert.Equal(TransparentCompositeHash, check.RgbaSha256);
        Assert.Equal(TransparentCompositeHash, check.Observed.RgbaSha256);
        Assert.Equal(1, check.Observed.Width);
        Assert.Equal(1, check.Observed.Height);
        Assert.Equal("dimensions-and-decoded-rgba8", check.Comparison);
        Assert.True(check.PixelsDecoded);
    }

    [Fact]
    public async Task CheckOfDifferentSamplesIsDriftWithoutAGreenResult()
    {
        var (dir, _) = BaseAsset();
        string red = Path.Combine(NewWorkspace(), "red.png");
        await File.WriteAllBytesAsync(red, Convert.FromBase64String(RedPngBase64));

        var check = await _service.CheckExportAsync(new CheckPixelExportRequest(dir, red));

        Assert.False(check.Matches);
        Assert.Equal(new[] { PixelExportDifference.Samples }, check.Differences.ToArray()); // Dimensions match (1x1), samples do not.
    }

    [Fact]
    public async Task CheckOfHiddenRgbUnderZeroAlphaIsDriftEvenThoughItLooksIdentical()
    {
        var (dir, _) = BaseAsset();
        string hidden = Path.Combine(NewWorkspace(), "hidden.png");
        await File.WriteAllBytesAsync(hidden, Png1x1(17, 34, 51, 0)); // Nonzero RGB, alpha 0.

        var check = await _service.CheckExportAsync(new CheckPixelExportRequest(dir, hidden));

        Assert.False(check.Matches);
        Assert.Equal(new[] { PixelExportDifference.Samples }, check.Differences.ToArray());
        Assert.NotEqual(TransparentCompositeHash, check.Observed.RgbaSha256);
    }

    [Fact]
    public async Task CheckReportsDimensionsAndSamplesInOrderForADifferentlySizedCandidate()
    {
        var (dir, _) = BaseAsset();
        string otherDir = Path.Combine(NewWorkspace(), "wide");
        await _service.CreateAsync(new CreatePixelAssetRequest(otherDir, "Wide", 2, 1));
        string wide = Path.Combine(NewWorkspace(), "wide.png");
        await _service.ExportAsync(new ExportPixelAssetRequest(otherDir, wide));

        var check = await _service.CheckExportAsync(new CheckPixelExportRequest(dir, wide));

        Assert.False(check.Matches);
        Assert.Equal(new[] { PixelExportDifference.Dimensions, PixelExportDifference.Samples }, check.Differences.ToArray());
        Assert.Equal(2, check.Observed.Width);
        Assert.Equal(1, check.Observed.Height);
    }

    [Fact]
    public async Task CheckOfAMissingCandidateIsInputUnavailable()
    {
        var (dir, _) = BaseAsset();
        var error = await Assert.ThrowsAsync<NativePixelException>(() =>
            _service.CheckExportAsync(new CheckPixelExportRequest(dir, Path.Combine(NewWorkspace(), "absent.png"))));
        Assert.Equal("PIXEL_INPUT_UNAVAILABLE", error.Code); // Absent PNG input, not a missing asset.
    }

    // ---- Read precondition --------------------------------------------------------------

    [Fact]
    public async Task AWrongExpectedReadSnapshotIsRevisionConflictForAllThree()
    {
        var (dir, _) = BaseAsset();
        var stale = new PixelSnapshot(Guid.Parse(PixelFixture.AssetId), 1, new string('a', 64));
        string output = Path.Combine(NewWorkspace(), "out.png");
        await _service.ExportAsync(new ExportPixelAssetRequest(dir, Path.Combine(NewWorkspace(), "ref.png")));

        var preview = await Assert.ThrowsAsync<NativePixelException>(() => _service.PreviewAsync(
            new PreviewPixelAssetRequest(dir, new PixelPreviewDelivery.Inline(), ExpectedReadSnapshot: stale)));
        var export = await Assert.ThrowsAsync<NativePixelException>(() => _service.ExportAsync(
            new ExportPixelAssetRequest(dir, output, ExpectedReadSnapshot: stale)));
        var check = await Assert.ThrowsAsync<NativePixelException>(() => _service.CheckExportAsync(
            new CheckPixelExportRequest(dir, Path.Combine(NewWorkspace(), "ref.png"), ExpectedReadSnapshot: stale)));

        Assert.Equal("REVISION_CONFLICT", preview.Code);
        Assert.Equal("REVISION_CONFLICT", export.Code);
        Assert.Equal("REVISION_CONFLICT", check.Code);
        Assert.False(File.Exists(output)); // The conflict was raised before any output write.
    }

    // ---- Output boundary ----------------------------------------------------------------

    [Fact]
    public async Task ExportRejectsAPathInsideOrEqualToTheAssetDirectory()
    {
        var (dir, _) = BaseAsset();
        foreach (string bad in new[] { Path.Combine(dir, "x.png"), Path.Combine(dir, "payloads", "x.png"), dir })
        {
            var error = await Assert.ThrowsAsync<NativePixelException>(
                () => _service.ExportAsync(new ExportPixelAssetRequest(dir, bad)));
            Assert.Equal("PATH_ESCAPE", error.Code);
        }
    }

    [Fact]
    public async Task ExportRejectsTheReservedSiblingLockAndStagingNamespace()
    {
        var (dir, _) = BaseAsset();
        string parent = Path.GetDirectoryName(dir)!;
        foreach (string bad in new[]
        {
            PixelFixture.LockPath(parent, Path.GetFileName(dir)),
            Path.Combine(parent, ".agd-pixels-stage-anything"),
            Path.Combine(parent, ".agd-pixels-out-x.tmp"),
        })
        {
            var error = await Assert.ThrowsAsync<NativePixelException>(
                () => _service.ExportAsync(new ExportPixelAssetRequest(dir, bad)));
            Assert.Equal("PATH_ESCAPE", error.Code);
        }
    }

    [Fact]
    public async Task ExportRejectsADirectorySymlinkOrSpecialLeaf()
    {
        var (dir, _) = BaseAsset();
        string workspace = NewWorkspace();

        string asDir = Path.Combine(workspace, "isdir.png");
        Directory.CreateDirectory(asDir);
        string asLink = Path.Combine(workspace, "islink.png");
        File.CreateSymbolicLink(asLink, Path.Combine(workspace, "target.png"));

        foreach (string bad in new[] { asDir, asLink })
        {
            var error = await Assert.ThrowsAsync<NativePixelException>(
                () => _service.ExportAsync(new ExportPixelAssetRequest(dir, bad, Overwrite: true)));
            Assert.Equal("PATH_ESCAPE", error.Code);
        }
    }

    [Fact]
    public async Task ExportRejectsAMultiplyLinkedDestination()
    {
        var (dir, _) = BaseAsset();
        string workspace = NewWorkspace();
        string existing = Path.Combine(workspace, "existing.png");
        await File.WriteAllBytesAsync(existing, [1, 2, 3]);
        string aliased = Path.Combine(workspace, "aliased.png");
        Assert.True(TryHardLink(existing, aliased)); // aliased and existing now share an inode (nlink=2).

        var error = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.ExportAsync(new ExportPixelAssetRequest(dir, aliased, Overwrite: true)));
        Assert.Equal("PATH_ESCAPE", error.Code);
    }

    [Fact]
    public async Task ExportRejectsAnOutputHardLinkedToTheWriterLock()
    {
        var (dir, _) = BaseAsset();
        string parent = Path.GetDirectoryName(dir)!;
        string lockPath = PixelFixture.LockPath(parent, Path.GetFileName(dir));
        await File.WriteAllBytesAsync(lockPath, []);
        string output = Path.Combine(NewWorkspace(), "loot.png");
        Assert.True(TryHardLink(lockPath, output)); // A valid-named PNG that aliases the lock file.

        var error = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.ExportAsync(new ExportPixelAssetRequest(dir, output, Overwrite: true)));
        Assert.Equal("PATH_ESCAPE", error.Code);
    }

    [Fact]
    public async Task CheckAlsoRejectsASourceCollidingOutputEvenThoughItNeverWrites()
    {
        var (dir, _) = BaseAsset();
        var error = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.CheckExportAsync(new CheckPixelExportRequest(dir, Path.Combine(dir, "payloads", "x.png"))));
        Assert.Equal("PATH_ESCAPE", error.Code);
    }

    [Fact]
    public async Task ExportRejectsANonAbsoluteOutputPath()
    {
        var (dir, _) = BaseAsset();
        var error = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.ExportAsync(new ExportPixelAssetRequest(dir, "relative/out.png")));
        Assert.Equal("INVALID_ARGUMENTS", error.Code);
    }

    [Fact]
    public async Task ExportRejectsAMissingOutputParentAsInvalidArguments()
    {
        var (dir, _) = BaseAsset();
        string missingParent = Path.Combine(NewWorkspace(), "nope", "out.png");
        var error = await Assert.ThrowsAsync<NativePixelException>(
            () => _service.ExportAsync(new ExportPixelAssetRequest(dir, missingParent)));
        Assert.Equal("INVALID_ARGUMENTS", error.Code); // A missing output directory is a bad location, not a missing asset.
    }

    [Fact]
    public async Task ExportSurfacesWriteFailedWhenTheOutputDirectoryIsUnwritable()
    {
        if (OperatingSystem.IsWindows()) return;
        var (dir, _) = BaseAsset();
        string outDir = Path.Combine(NewWorkspace(), "locked");
        Directory.CreateDirectory(outDir);
        File.SetUnixFileMode(outDir, UnixFileMode.UserRead | UnixFileMode.UserExecute); // r-x, no write.
        try
        {
            // If this user can still create files here (e.g. running as root), the negative test is moot.
            string probe = Path.Combine(outDir, ".probe");
            bool enforced;
            try { await File.WriteAllTextAsync(probe, "x"); File.Delete(probe); enforced = false; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { enforced = true; }
            if (!enforced) return;

            var error = await Assert.ThrowsAsync<NativePixelException>(
                () => _service.ExportAsync(new ExportPixelAssetRequest(dir, Path.Combine(outDir, "out.png"))));
            Assert.Equal("PIXEL_WRITE_FAILED", error.Code);
        }
        finally
        {
            File.SetUnixFileMode(outDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ---- Independent helpers ------------------------------------------------------------

    private static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>Independently decodes a minimal RGBA8 PNG to (width, height, raw-sample SHA-256).</summary>
    private static (int Width, int Height, string RgbaSha256) DecodePng(byte[] png)
    {
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));
        using var idat = new MemoryStream();
        int offset = 8;
        while (offset < png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") idat.Write(png, offset + 8, length);
            offset += 12 + length;
        }
        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        int stride = width * 4;
        var filtered = new byte[(stride + 1) * height];
        int read = 0;
        while (read < filtered.Length)
        {
            int n = inflate.Read(filtered, read, filtered.Length - read);
            if (n == 0) break;
            read += n;
        }
        var raw = new byte[stride * height];
        for (int y = 0; y < height; y++) // Filter type 0 only (the reference writer never emits others).
            Array.Copy(filtered, y * (stride + 1) + 1, raw, y * stride, stride);
        return (width, height, Convert.ToHexStringLower(SHA256.HashData(raw)));
    }

    /// <summary>Builds a minimal filter-0 RGBA8 PNG carrying one pixel, without alpha-zero cleanup.</summary>
    private static byte[] Png1x1(byte r, byte g, byte b, byte a)
    {
        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write([0, r, g, b, a]);
            compressed = buffer.ToArray();
        }
        var png = new List<byte>([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, 1);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8;  // Bit depth.
        ihdr[9] = 6;  // RGBA color type.
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", []);
        return [.. png];

        static void WriteChunk(List<byte> png, string type, byte[] data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            png.AddRange(length);
            byte[] body = [.. Encoding.ASCII.GetBytes(type), .. data];
            png.AddRange(body);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
            png.AddRange(crc);
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)(-(int)(crc & 1)));
        }
        return crc ^ 0xFFFFFFFF;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "link")]
    private static extern int Link(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string link);

    private static bool TryHardLink(string existing, string link) => Link(existing, link) == 0;
}
