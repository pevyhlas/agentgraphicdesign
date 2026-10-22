using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

public sealed class NativePixelRendererTests
{
    private const string TableHash = "353442b2a3b85e1030cc800349cc40d4d0b7e2bdf063a7a9766298f4dabaa7b8";
    private const string InvalidPng = "ARTIFACT_PNG_INVALID";
    private const string UnsupportedPng = "ARTIFACT_PROFILE_UNSUPPORTED";
    private const long Q = 1 << 24;
    private static readonly Action NoBudget = static () => { };
    private static readonly byte[] RedMember = Convert.FromHexString("7801010500FAFF00FF0000FF050001FF");

    [Fact]
    public void Helpers_AreInternalAndObservationHasOnlyCapturedFacts()
    {
        var renderer = typeof(NativePixelRenderer);
        Assert.True(renderer.IsNotPublic && renderer.IsAbstract && renderer.IsSealed);
        Assert.Equal(new[] { "CompositeHash", "Encode", "InspectPng" }, renderer
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.IsAssembly)
            .Select(m => m.Name).Order().ToArray());
        Assert.True(typeof(NativePngObservation).IsNotPublic);
        Assert.Equal(new[] { "EncodedBytes", "FileSha256", "Height", "RgbaSha256", "Width" },
            typeof(NativePngObservation).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public void LiteralTables_HaveApprovedBigEndianDomainSeparatedIdentity()
    {
        Assert.Equal(256, NativePixelTables.F.Length);
        Assert.Equal(255, NativePixelTables.T.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("agd.pixel-evaluation.q24-tables.v1\n"u8);
        Span<byte> word = stackalloc byte[4];
        foreach (var value in NativePixelTables.F)
        {
            BinaryPrimitives.WriteUInt32BigEndian(word, value);
            hash.AppendData(word);
        }
        foreach (var value in NativePixelTables.T)
        {
            BinaryPrimitives.WriteUInt32BigEndian(word, value);
            hash.AppendData(word);
        }
        Assert.Equal(TableHash, Convert.ToHexStringLower(hash.GetHashAndReset()));
        Assert.Equal(TableHash, NativePixelTables.Sha256);
        Assert.Equal(0u, NativePixelTables.F[0]);
        Assert.Equal(16_777_216u, NativePixelTables.F[255]);
    }

    [Fact]
    public void EveryLiteralTableEntry_IsCertifiedAgainstIndependentExactRationals()
    {
        // No floating point or expected values derived by calling production math.
        // Fifth powers certify the rounding interval directly, without root finding.
        for (var table = 0; table < 2; table++)
        {
            var values = table == 0 ? NativePixelTables.F : NativePixelTables.T;
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (i > 0)
                    Assert.True(value > values[i - 1]);
                if (table == 0 && i <= 10)
                    Assert.Equal((Q * i * 25 + (255 * 323) / 2) / (255 * 323), (long)value);
                else if (table == 1 && i <= 9)
                    Assert.Equal((Q * (2 * i + 1) * 25 + 510 * 323 - 1) / (510 * 323), (long)value);
                else
                {
                    var n = table == 0 ? 200 * i + 2805 : 400 * i + 5810;
                    var d = table == 0 ? 53805 : 107610;
                    var scale = table == 0 ? 2 : 1;
                    var target = BigInteger.Pow(scale * Q, 5) * BigInteger.Pow(n, 12);
                    var lower = table == 0 ? 2L * value - 1 : value - 1L;
                    var upper = table == 0 ? 2L * value + 1 : value;
                    var lo = BigInteger.Pow(lower, 5) * BigInteger.Pow(d, 12);
                    var hi = BigInteger.Pow(upper, 5) * BigInteger.Pow(d, 12);
                    Assert.True(table == 0 ? lo <= target && target < hi : lo < target && target <= hi);
                }
            }
        }
    }

    [Fact]
    public void ThresholdEqualityAndAllQ24Inputs_MatchIndependentMonotoneScan()
    {
        for (var c = 0; c < 256; c++)
            Assert.Equal((byte)c, NativePixelTables.Encode(NativePixelTables.F[c]));
        for (var k = 0; k < 255; k++)
        {
            Assert.Equal((byte)k, NativePixelTables.Encode(NativePixelTables.T[k] - 1));
            Assert.Equal((byte)(k + 1), NativePixelTables.Encode(NativePixelTables.T[k]));
        }
        var expected = 0;
        var thresholds = NativePixelTables.T;
        for (uint q = 0; q <= Q; q++)
        {
            if (expected < 255 && q >= thresholds[expected])
                expected++;
            // Avoid millions of assertion allocations/formatting on the success path.
            if (NativePixelTables.Encode(q) != expected)
                Assert.Fail($"Incorrect threshold encoding at Q24 value {q}.");
        }
    }

    [Theory]
    [InlineData("FF0000FF", "FF0000FF")]
    [InlineData("11223300", "00000000")]
    [InlineData("000000FF FFFFFF80", "BCBCBCFF")]
    [InlineData("FFFFFFFF 00000080", "BBBBBBFF")]
    [InlineData("0000FFFF FF000080", "BC00BBFF")]
    [InlineData("000000FF FFFFFFFF/255/500000", "BCBCBCFF")]
    [InlineData("000000FF FFFFFFFF/128/500000", "898989FF")]
    [InlineData("FF0000FF FFFFFFFF/0", "FF0000FF")]
    [InlineData("FF0000FF FFFFFFFF/255/1000000/hidden", "FF0000FF")]
    [InlineData("0000FFFF FFFFFFFF/255/0", "0000FFFF")]
    [InlineData("FFFFFF80 FFFFFF80", "FFFFFFC0")]
    [InlineData("FFFFFF01/255/499999", "00000000")]
    [InlineData("FFFFFF01/255/500000", "FFFFFF01")]
    [InlineData("FFFFFFFF/255/1960", "00000000")]
    [InlineData("FFFFFFFF/255/1961", "FFFFFF01")]
    [InlineData("336699C0/173/700000", "3366995B")]
    // Additional independent scratch-reference vectors, not production-generated goldens.
    [InlineData("18395AFF D24B9381/173/700000", "703E6BFF")]
    [InlineData("214365A7/231/830000 D24B9381/173/700000 38C17F49/91/375000", "88557AA0")]
    [InlineData("FF0000FF/0 00FF00FF/255/1000000/hidden", "00000000")]
    [InlineData("FF000080 0000FF80", "9C00D5C0")]
    [InlineData("0000FF80 FF000080", "D5009CC0")]
    public void LiteralEvaluationVectors_HashAndEncodeExactSamples(string layers, string expected)
    {
        var input = layers.Split(' ').Select(spec =>
        {
            var fields = spec.Split('/');
            return new TestLayer(Convert.FromHexString(fields[0]),
                fields.Length > 1 ? [(byte)int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture)] : null,
                fields.Length > 2 ? int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture) / 1_000_000m : 1m,
                fields.Length < 4);
        }).ToArray();
        var (doc, payloads) = Create(1, 1, input);
        AssertSamples(doc, payloads, new PixelRect(0, 0, 1, 1), Convert.FromHexString(expected));
    }

    [Theory]
    [InlineData(128, 996090)]
    [InlineData(128, 996093)]
    [InlineData(129, 988369)]
    public void CoverageFactors_AreRoundedTogetherOnce_NotInStages(int mask, int opacity)
    {
        static long Round(BigInteger n, long d) => (long)((n + d / 2) / d);
        var exact = Round((BigInteger)mask * opacity * Q, 65_025_000_000);
        var staged = Round((BigInteger)Round((BigInteger)mask * Q, 65_025) * opacity, 1_000_000);
        Assert.Equal(32896, exact);
        Assert.Equal(32897, staged);
        var (doc, payloads) = Create(1, 1, new TestLayer([255, 255, 255, 1], [(byte)mask], opacity / 1_000_000m));
        AssertSamples(doc, payloads, new PixelRect(0, 0, 1, 1), [0, 0, 0, 0]);
    }

    [Fact]
    public void SrgbEncoding_IsNotByteSpaceBlendingOrNearestLinearTableEntry()
    {
        var (doc, payloads) = Create(1, 1,
            new TestLayer([0, 0, 0, 255]), new TestLayer([255, 255, 255, 128]));
        var actual = NativePixelRenderer.CompositeHash(doc, payloads, new PixelRect(0, 0, 1, 1), NoBudget);
        Assert.Equal(Hash([188, 188, 188, 255]), actual);
        Assert.NotEqual(Hash([128, 128, 128, 255]), actual);
        var threshold = NativePixelTables.T[128];
        Assert.True(threshold - NativePixelTables.F[128] < NativePixelTables.F[129] - threshold);
        Assert.Equal(129, NativePixelTables.Encode(threshold));
    }

    [Fact]
    public void All256OpaqueChannelValues_RoundTripWithoutSourceChanges()
    {
        var raw = new byte[256 * 4];
        for (var c = 0; c < 256; c++)
        {
            raw[c * 4] = (byte)c;
            raw[c * 4 + 1] = (byte)(255 - c);
            raw[c * 4 + 2] = (byte)((c * 37) % 256);
            raw[c * 4 + 3] = 255;
        }
        var (doc, payloads) = Create(256, 1, new TestLayer(raw));
        AssertSamples(doc, payloads, new PixelRect(0, 0, 256, 1), raw);
        Assert.Equal(raw, payloads[doc.Layers[0].PixelsSha256].AsSpan(24).ToArray());
    }

    [Fact]
    public void SourceIdentityAndHiddenRgb_AreNotFlattenedOrRehashedAsRgba()
    {
        var (hidden, hiddenPayloads) = Create(1, 1, new TestLayer([17, 34, 51, 0]));
        var (clear, clearPayloads) = Create(1, 1, new TestLayer([0, 0, 0, 0]));
        const string hiddenFrameHash = "b6a504dc8edff031f684f4a317efd10f6626ec3d9802c0291e12711c1a7677cf";
        const string clearFrameHash = "dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b";
        Assert.Equal(hiddenFrameHash, hidden.Layers[0].PixelsSha256);
        Assert.Equal(clearFrameHash, clear.Layers[0].PixelsSha256);
        var rect = new PixelRect(0, 0, 1, 1);
        var rawHash = NativePixelRenderer.CompositeHash(hidden, hiddenPayloads, rect, NoBudget);
        Assert.Equal("df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119", rawHash);
        Assert.Equal(rawHash, NativePixelRenderer.CompositeHash(clear, clearPayloads, rect, NoBudget));
        Assert.NotEqual(hiddenFrameHash, rawHash);
        foreach (var layer in new[]
        {
            hidden.Layers[0] with { Visible = false },
            hidden.Layers[0] with { Opacity = 0.5m },
            hidden.Layers[0] with { Name = "Changed control metadata" }
        })
            Assert.Equal(rawHash, NativePixelRenderer.CompositeHash(hidden with { Layers = [layer] }, hiddenPayloads, rect, NoBudget));
        AssertSamples(hidden, hiddenPayloads, rect, [0, 0, 0, 0]);
        Assert.Equal(hiddenFrameHash, Hash(hiddenPayloads[hiddenFrameHash]));
        Assert.Equal(new byte[] { 17, 34, 51, 0 }, hiddenPayloads[hiddenFrameHash].AsSpan(24).ToArray());
    }

    [Fact]
    public void MaximumProductsAndSixteenLayers_PreserveUnityAndPremultipliedInvariants()
    {
        Assert.Equal(1_090_938_502_912_500_000L, checked(255L * 255 * 1_000_000 * Q + 65_025_000_000L / 2));
        Assert.Equal(281_474_976_710_656L, checked(Q * Q));
        Assert.True((BigInteger)255 * 255 * 1_000_000 * Q + 65_025_000_000L / 2 < long.MaxValue);
        var layers = Enumerable.Range(0, 16).Select(i => new TestLayer([255, 255, 255, 255],
            [(byte)(i == 0 ? 255 : i * 17)], i == 0 ? 1m : i / 16m)).ToArray();
        var (doc, payloads) = Create(1, 1, layers);
        AssertSamples(doc, payloads, new PixelRect(0, 0, 1, 1), [255, 255, 255, 255]);
        var opaque = doc.Layers[0];
        AssertSamples(doc with { Layers = Enumerable.Range(0, 16).Select(_ => opaque with { Id = Guid.NewGuid() }).ToImmutableArray() }, payloads,
            new PixelRect(0, 0, 1, 1), [255, 255, 255, 255]);
    }

    [Theory]
    [InlineData(32, 32)]
    [InlineData(64, 64)]
    [InlineData(37, 23)]
    [InlineData(1, 37)]
    [InlineData(37, 1)]
    [InlineData(8192, 1)]
    [InlineData(1, 8192)]
    public void ArbitraryCanvasesAndCrops_KeepOriginalRowCoordinates(int width, int height)
    {
        var raw = Pattern(width, height);
        var (doc, payloads) = Create(width, height, new TestLayer(raw));
        AssertSamples(doc, payloads, new PixelRect(0, 0, width, height), raw);
        var rect = new PixelRect(width > 1 ? 1 : 0, height > 1 ? 1 : 0,
            width > 1 ? width - 1 : 1, height > 1 ? height - 1 : 1);
        AssertSamples(doc, payloads, rect, Crop(raw, width, rect));
        AssertSamples(doc, payloads, new PixelRect(width - 1, height - 1, 1, 1), raw[^4..]);
    }

    [Fact]
    public void MaskedCrop_IsNotRebasedOrResampled_AndLeavesAllPayloadsUntouched()
    {
        const int width = 37, height = 23;
        var black = new byte[width * height * 4];
        var white = new byte[black.Length];
        var mask = new byte[width * height];
        var expected = new byte[black.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                var phase = (x + 2 * y) % 3;
                mask[p] = phase == 0 ? (byte)0 : phase == 1 ? (byte)128 : (byte)255;
                black[p * 4 + 3] = 255;
                white.AsSpan(p * 4, 4).Fill(255);
                expected.AsSpan(p * 4, 3).Fill(phase == 0 ? (byte)0 : phase == 1 ? (byte)137 : (byte)188);
                expected[p * 4 + 3] = 255;
            }
        var (doc, payloads) = Create(width, height, new TestLayer(black), new TestLayer(white, mask, 0.5m));
        var rect = new PixelRect(3, 7, 29, 13);
        AssertSamples(doc, payloads, rect, Crop(expected, width, rect));
        foreach (var (sha, bytes) in payloads)
            Assert.Equal(sha, Hash(bytes));
    }

    [Fact]
    public void Writer_UsesRgba8SrgbFilterZeroBoundedConsecutiveIdatAndExactCrcs()
    {
        const int width = 257, height = 131;
        var raw = Noise(width, height);
        var (doc, payloads) = Create(width, height, new TestLayer(raw));
        var result = NativePixelRenderer.Encode(doc, payloads, new PixelRect(0, 0, width, height), 1024 * 1024, NoBudget);
        PngStructure.Validate(result.Data, width, height);
        Assert.Equal(Hash(raw), PngRgbaVerifier.Verify(result.Data, width, height, NoBudget));
        var chunks = ReadChunks(result.Data);
        Assert.Equal("IHDR", chunks[0].Kind);
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, chunks[0].Body[8..]);
        Assert.Equal("sRGB", chunks[1].Kind);
        Assert.Equal(new byte[] { 0 }, chunks[1].Body);
        Assert.Equal("IEND", chunks[^1].Kind);
        Assert.Empty(chunks[^1].Body);
        Assert.InRange(chunks.Count, 5, 4096);
        foreach (var chunk in chunks.Skip(2).SkipLast(1))
        {
            Assert.Equal("IDAT", chunk.Kind);
            Assert.InRange(chunk.Body.Length, 1, 65536);
        }
        Assert.Equal(65536, chunks[2].Body.Length);
        Assert.Equal(FilterZero(raw, width, height), Inflate(chunks));
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression, 1, false)]
    [InlineData(CompressionLevel.Fastest, 7, true)]
    [InlineData(CompressionLevel.Optimal, 65536, false)]
    [InlineData(CompressionLevel.SmallestSize, 13, true)]
    public void Observation_AcceptsDifferentCompressionSplitsAndCompatibleTags(
        CompressionLevel level, int split, bool tagged)
    {
        var raw = Pattern(37, 23);
        var member = Compress(FilterZero(raw, 37, 23), level);
        var middle = new List<(string Kind, byte[] Body)>();
        if (tagged)
        {
            middle.Add(("sRGB", [0]));
            middle.Add(("gAMA", [0, 0, 177, 143]));
            middle.Add(("cHRM", Convert.FromHexString("00007A26000080840000FA00000080E8000075300000EA6000003A9800001770")));
        }
        middle.Add(("IDAT", []));
        for (var offset = 0; offset < member.Length; offset += split)
            middle.Add(("IDAT", member.AsSpan(offset, Math.Min(split, member.Length - offset)).ToArray()));
        middle.Add(("IDAT", []));
        var png = Png(37, 23, middle.ToArray());
        PngStructure.Validate(png, 37, 23);
        var observed = NativePixelRenderer.InspectPng(png, NoBudget);
        Assert.Equal(new NativePngObservation(37, 23, png.Length, Hash(png), Hash(raw)), observed);
    }

    [Fact]
    public void Observation_UsesObservedDimensionsAndHashesActualHiddenRgbWithoutCleanup()
    {
        byte[] raw = [17, 34, 51, 0, 255, 0, 0, 255];
        var horizontal = Png(2, 1, ("IDAT", Compress(FilterZero(raw, 2, 1))));
        var vertical = Png(1, 2, ("IDAT", Compress(FilterZero(raw, 1, 2))));
        var first = NativePixelRenderer.InspectPng(horizontal, NoBudget);
        var second = NativePixelRenderer.InspectPng(vertical, NoBudget);
        Assert.Equal((2, 1), (first.Width, first.Height));
        Assert.Equal((1, 2), (second.Width, second.Height));
        Assert.Equal(Hash(raw), first.RgbaSha256);
        Assert.Equal(first.RgbaSha256, second.RgbaSha256);
        Assert.NotEqual(first.FileSha256, second.FileSha256);
        Assert.NotEqual(Hash([0, 0, 0, 0, 255, 0, 0, 255]), first.RgbaSha256);
        Assert.Equal(Hash(horizontal), first.FileSha256);
    }

    [Theory]
    [InlineData(0, "0A141E0032281E01FF0080FF", "140A2801283C1E0001FF0080")]
    [InlineData(1, "0A141E0028140001CDD862FE", "140A28011432F6FFD9C3E280")]
    [InlineData(2, "0A141E0032281E01FF0080FF", "0AF60A01F61400FF02FF8081")]
    [InlineData(3, "0A141E002D1E0F01E6EC71FF", "0F0019010523FBFF6EE1B101")]
    [InlineData(4, "0A141E0028140001CDD862FE", "0AF60A01F614F6FF02FF8081")]
    public void Observation_AcceptsAllFiltersUsingIndependentSampleFixtures(byte filter, string first, string second)
    {
        byte[] raw = [10, 20, 30, 0, 50, 40, 30, 1, 255, 0, 128, 255,
            20, 10, 40, 1, 40, 60, 30, 0, 1, 255, 0, 128];
        byte[] filtered = [filter, .. Convert.FromHexString(first), filter, .. Convert.FromHexString(second)];
        var png = Png(3, 2, ("IDAT", Compress(filtered)));
        PngStructure.Validate(png, 3, 2);
        Assert.Equal(Hash(raw), NativePixelRenderer.InspectPng(png, NoBudget).RgbaSha256);
    }

    [Fact]
    public void Observation_AcceptsIndependentLiteralPng_AndEveryTruncationFails()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/AP8AAP8FAAH/+lyI0QAAAABJRU5ErkJggg==");
        Assert.Equal(Hash([255, 0, 0, 255]), NativePixelRenderer.InspectPng(png, NoBudget).RgbaSha256);
        for (var length = 0; length < png.Length; length++)
            Assert.Equal(InvalidPng, Assert.Throws<NativePixelException>(() =>
                NativePixelRenderer.InspectPng(png.AsMemory(0, length), NoBudget)).Code);
        var corrupt = (byte[])png.Clone();
        corrupt[29] ^= 1; // IHDR CRC.
        AssertPngError(InvalidPng, corrupt);
        AssertPngError(InvalidPng, [.. png, 0]);
        Assert.Equal(Hash([255, 0, 0, 255]), NativePixelRenderer.InspectPng(png, NoBudget).RgbaSha256);
    }

    [Theory]
    [InlineData("789C03030000050001")]
    [InlineData("78010700000000")]
    [InlineData("7801010500FBFF00FF0000FF050001FF")]
    [InlineData("7801000500FAFF00FF0000FF050001FF")]
    [InlineData("7801010500FAFF00FF0000FF050001FE")]
    public void StructurallyValidBrokenStreams_PreserveExistingDecoderError(string memberHex)
    {
        var png = Png(1, 1, ("IDAT", Convert.FromHexString(memberHex)));
        PngStructure.Validate(png, 1, 1);
        var existing = Assert.Throws<DocumentEngineException>(() => PngRgbaVerifier.Verify(png, 1, 1, NoBudget));
        var actual = Assert.Throws<NativePixelException>(() => NativePixelRenderer.InspectPng(png, NoBudget));
        Assert.Equal(existing.Code, actual.Code);
        Assert.Equal(InvalidPng, actual.Code);
        Assert.Null(actual.Path);
        Assert.Null(actual.OperationIndex);
    }

    [Theory]
    [InlineData(8, 2, 0, 0, 0, UnsupportedPng)]
    [InlineData(16, 6, 0, 0, 0, UnsupportedPng)]
    [InlineData(8, 6, 0, 0, 1, UnsupportedPng)]
    [InlineData(4, 6, 0, 0, 0, InvalidPng)]
    [InlineData(8, 6, 1, 0, 0, InvalidPng)]
    [InlineData(8, 6, 0, 1, 0, InvalidPng)]
    [InlineData(8, 6, 0, 0, 2, InvalidPng)]
    public void HeaderAcceptance_MatchesExistingStructuralPolicy(
        byte depth, byte color, byte compression, byte filter, byte interlace, string code)
    {
        var png = Png(1, 1, ("IDAT", RedMember));
        png[24] = depth;
        png[25] = color;
        png[26] = compression;
        png[27] = filter;
        png[28] = interlace;
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), TestCrc(png.AsSpan(12, 17)));
        Assert.Equal(code, Assert.Throws<DocumentEngineException>(() => PngStructure.Validate(png, 1, 1)).Code);
        AssertPngError(code, png);
    }

    [Fact]
    public void ChunkMetadataAndOrderingFailures_MatchExistingStructuralPolicy()
    {
        (string Code, (string Kind, byte[] Body)[] Chunks)[] cases =
        [
            (UnsupportedPng, [("tEXt", [65, 0, 66]), ("IDAT", RedMember)]),
            (UnsupportedPng, [("iCCP", []), ("IDAT", RedMember)]),
            (UnsupportedPng, [("acTL", new byte[8]), ("IDAT", RedMember)]),
            (UnsupportedPng, [("gAMA", [0, 0, 0, 1]), ("IDAT", RedMember)]),
            (UnsupportedPng, [("cHRM", new byte[32]), ("IDAT", RedMember)]),
            (InvalidPng, [("sRGB", [4]), ("IDAT", RedMember)]),
            (InvalidPng, [("sRGB", [0]), ("sRGB", [0]), ("IDAT", RedMember)]),
            (InvalidPng, [("IDAT", RedMember), ("sRGB", [0])]),
            (InvalidPng, [("IDAT", RedMember), ("gAMA", [0, 0, 177, 143])]),
            (InvalidPng, [("IDAT", RedMember), ("cHRM", new byte[32])]),
            (InvalidPng, [("IHDR", new byte[13]), ("IDAT", RedMember)]),
            (InvalidPng, [("IDaT", RedMember)]),
            (InvalidPng, [("IDA1", RedMember)]),
            (InvalidPng, [("IDAT", [])]),
            (InvalidPng, [])
        ];
        foreach (var item in cases)
        {
            var png = Png(1, 1, item.Chunks);
            Assert.Equal(item.Code, Assert.Throws<DocumentEngineException>(() => PngStructure.Validate(png, 1, 1)).Code);
            AssertPngError(item.Code, png);
        }
        var oversizedLength = Png(1, 1, ("IDAT", RedMember));
        BinaryPrimitives.WriteUInt32BigEndian(oversizedLength.AsSpan(33), uint.MaxValue);
        AssertPngError(InvalidPng, oversizedLength);
    }

    [Fact]
    public void Observation_EnforcesChunkAndNativeDimensionAreaAndEncodedBounds()
    {
        var chunks = Enumerable.Repeat(("IDAT", Array.Empty<byte>()), 4094).ToArray();
        chunks[0] = ("IDAT", RedMember);
        Assert.Equal(Hash([255, 0, 0, 255]), NativePixelRenderer.InspectPng(Png(1, 1, chunks), NoBudget).RgbaSha256);
        AssertPngError(EngineErrors.ResourceLimit, Png(1, 1, [.. chunks, ("IDAT", Array.Empty<byte>())]));
        AssertPngError(InvalidPng, Png(0, 1, ("IDAT", RedMember)));
        AssertPngError(EngineErrors.ResourceLimit, Png(8193, 1, ("IDAT", RedMember)));
        AssertPngError(EngineErrors.ResourceLimit, Png(1, 8193, ("IDAT", RedMember)));
        AssertPngError(EngineErrors.ResourceLimit, Png(8192, 513, ("IDAT", RedMember)));
        AssertPngError(EngineErrors.ResourceLimit, Png(int.MaxValue, int.MaxValue, ("IDAT", RedMember)));
        // The inclusive area passes header bounds, but its incomplete sample stream still fails.
        AssertPngError(InvalidPng, Png(8192, 512, ("IDAT", RedMember)));
        var bytes = new byte[32 * 1024 * 1024 + 1];
        AssertPngError(EngineErrors.ResourceLimit, bytes);
        Assert.Equal(InvalidPng, Assert.Throws<NativePixelException>(() =>
            NativePixelRenderer.InspectPng(bytes.AsMemory(0, bytes.Length - 1), NoBudget)).Code);
    }

    [Theory]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Observation_PreservesManagedDeflateBlockBudget(int emptyBlocks, bool valid)
    {
        var member = new byte[RedMember.Length + emptyBlocks * 5];
        member[0] = 0x78;
        member[1] = 0x01;
        for (var block = 0; block < emptyBlocks; block++)
        {
            // Literal nonfinal stored empty block: BFINAL=0, LEN=0, NLEN=65535.
            member[2 + block * 5 + 3] = 0xff;
            member[2 + block * 5 + 4] = 0xff;
        }
        RedMember.AsSpan(2).CopyTo(member.AsSpan(2 + emptyBlocks * 5));
        var png = Png(1, 1, ("IDAT", member));
        PngStructure.Validate(png, 1, 1);
        if (valid)
            Assert.Equal(Hash([255, 0, 0, 255]), NativePixelRenderer.InspectPng(png, NoBudget).RgbaSha256);
        else
            AssertPngError(EngineErrors.ResourceLimit, png);
    }

    [Fact]
    public void EncoderLimit_IncludesAllFramingAndFailsBeforeAnyBufferGrowth()
    {
        var (doc, payloads) = Create(257, 131, new TestLayer(Noise(257, 131)));
        var rect = new PixelRect(0, 0, doc.Width, doc.Height);
        var full = NativePixelRenderer.Encode(doc, payloads, rect, 1024 * 1024, NoBudget);
        var exact = NativePixelRenderer.Encode(doc, payloads, rect, full.Data.Length, NoBudget);
        Assert.Equal(full.Data, exact.Data);
        foreach (var maximum in new[] { int.MinValue, 0, 69, 70, 65536, full.Data.Length - 1, 32 * 1024 * 1024 + 1 })
            Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<NativePixelException>(() =>
                NativePixelRenderer.Encode(doc, payloads, rect, maximum, NoBudget)).Code);
        // Checking only the reference never needs an encoder or encoded-byte allowance.
        Assert.Equal(full.RgbaSha256, NativePixelRenderer.CompositeHash(doc, payloads, rect, NoBudget));
    }

    [Theory]
    [InlineData(0, 1, EngineErrors.InvalidDimensions)]
    [InlineData(1, -1, EngineErrors.InvalidDimensions)]
    [InlineData(8193, 1, EngineErrors.ResourceLimit)]
    [InlineData(8192, 513, EngineErrors.ResourceLimit)]
    [InlineData(int.MaxValue, int.MaxValue, EngineErrors.ResourceLimit)]
    public void InternalCanvasGuards_RunBeforeAllocationsOrOffsets(int width, int height, string code)
    {
        var (valid, payloads) = Create(1, 1, new TestLayer([0, 0, 0, 0]));
        var doc = valid with { Width = width, Height = height };
        var rect = new PixelRect(0, 0, 1, 1);
        Assert.Equal(code, Assert.Throws<NativePixelException>(() => NativePixelRenderer.CompositeHash(doc, payloads, rect, NoBudget)).Code);
        Assert.Equal(code, Assert.Throws<NativePixelException>(() => NativePixelRenderer.Encode(doc, payloads, rect, 1024, NoBudget)).Code);
    }

    [Fact]
    public void InvalidRectanglesLayerListsOpacityAndCapturedLengths_FailClosed()
    {
        var (doc, payloads) = Create(1, 1, new TestLayer([17, 34, 51, 255]));
        var rect = new PixelRect(0, 0, 1, 1);
        foreach (var bad in new[]
        {
            new PixelRect(-1, 0, 1, 1), new PixelRect(0, -1, 1, 1), new PixelRect(0, 0, 0, 1),
            new PixelRect(0, 0, 1, 0), new PixelRect(1, 0, 1, 1), new PixelRect(0, 1, 1, 1),
            new PixelRect(int.MaxValue, 0, int.MaxValue, 1), new PixelRect(0, 0, 1, int.MaxValue)
        })
            Assert.Equal(EngineErrors.InvalidArguments, Assert.Throws<NativePixelException>(() =>
                NativePixelRenderer.CompositeHash(doc, payloads, bad, NoBudget)).Code);
        foreach (var layers in new[] { default(ImmutableArray<PixelLayerInfo>), ImmutableArray<PixelLayerInfo>.Empty })
            Assert.Equal(EngineErrors.InvalidProperty, Assert.Throws<NativePixelException>(() =>
                NativePixelRenderer.CompositeHash(doc with { Layers = layers }, payloads, rect, NoBudget)).Code);
        Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<NativePixelException>(() =>
            NativePixelRenderer.CompositeHash(doc with { Layers = Enumerable.Repeat(doc.Layers[0], 17).ToImmutableArray() }, payloads, rect, NoBudget)).Code);
        foreach (var opacity in new[] { -0.1m, 1.1m, 0.0000001m, decimal.MaxValue })
            Assert.Equal(EngineErrors.InvalidProperty, Assert.Throws<NativePixelException>(() =>
                NativePixelRenderer.CompositeHash(doc with { Layers = [doc.Layers[0] with { Opacity = opacity }] }, payloads, rect, NoBudget)).Code);
        var key = doc.Layers[0].PixelsSha256;
        foreach (var bytes in new[] { new byte[23], new byte[27], new byte[29] })
        {
            var badPayloads = new Dictionary<string, byte[]> { [key] = bytes };
            Assert.Equal("PIXEL_CONTENT_MISMATCH", Assert.Throws<NativePixelException>(() =>
                NativePixelRenderer.CompositeHash(doc, badPayloads, rect, NoBudget)).Code);
        }
        Assert.Equal("PIXEL_CONTENT_MISMATCH", Assert.Throws<NativePixelException>(() =>
            NativePixelRenderer.CompositeHash(doc, new Dictionary<string, byte[]>(), rect, NoBudget)).Code);
        var masked = doc with { Layers = [doc.Layers[0] with { MaskSha256 = key }] };
        Assert.Equal("PIXEL_CONTENT_MISMATCH", Assert.Throws<NativePixelException>(() =>
            NativePixelRenderer.CompositeHash(masked, payloads, rect, NoBudget)).Code);
    }

    [Fact]
    public void HashOnlyPath_HasRowSizedAllocationAndPixelCheckpoints_EvenWhenHidden()
    {
        var raw = Pattern(8192, 16);
        var (doc, payloads) = Create(8192, 16, new TestLayer(raw));
        var rect = new PixelRect(0, 0, 8192, 16);
        NativePixelRenderer.CompositeHash(doc, payloads, rect, NoBudget); // Warm up type/JIT/hash state.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var hash = NativePixelRenderer.CompositeHash(doc, payloads, rect, NoBudget);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 128 * 1024); // Excludes a copied payload, image canvas or PNG staging.
        Assert.Equal(Hash(raw), hash);
        var checks = 0;
        NativePixelRenderer.CompositeHash(doc, payloads, rect, () => checks++);
        Assert.True(checks >= 2 * 8192 * 16 / 1024);
        var hidden = doc with { Layers = [doc.Layers[0] with { Visible = false }] };
        checks = 0;
        NativePixelRenderer.CompositeHash(hidden, payloads, rect, () => checks++);
        Assert.True(checks >= 8192 * 16 / 1024);
        var many = doc with
        {
            Layers = Enumerable.Range(0, 16).Select(_ => doc.Layers[0] with { Id = Guid.NewGuid() }).ToImmutableArray()
        };
        checks = 0;
        NativePixelRenderer.CompositeHash(many, payloads, new PixelRect(0, 0, 8192, 1), () => checks++);
        Assert.True(checks >= 16 * 8192 / 1024 + 8192 / 1024);
    }

    [Fact]
    public void InclusiveMaximumCanvasArea_CanBeHashedWithoutAnImageSizedOutputBuffer()
    {
        var raw = new byte[8192 * 512 * 4];
        var (doc, payloads) = Create(8192, 512, new TestLayer(raw));
        var checks = 0;
        Assert.Equal(Hash(raw), NativePixelRenderer.CompositeHash(doc, payloads,
            new PixelRect(0, 0, 8192, 512), () => checks++));
        Assert.True(checks >= 2 * 4_194_304 / 1024);
    }

    [Fact]
    public void CancellationAndBudgetFailures_PropagateAtEveryEncoderAndCheckerBoundary()
    {
        var (doc, payloads) = Create(1, 1, new TestLayer([255, 0, 0, 255]));
        var rect = new PixelRect(0, 0, 1, 1);
        var checks = 0;
        var result = NativePixelRenderer.Encode(doc, payloads, rect, 1024, () => checks++);
        for (var stop = 1; stop <= checks; stop++)
        {
            var calls = 0;
            using var cancellation = new CancellationTokenSource();
            var error = Assert.Throws<OperationCanceledException>(() =>
                NativePixelRenderer.Encode(doc, payloads, rect, 1024, () =>
                {
                    if (++calls == stop) cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        checks = 0;
        NativePixelRenderer.InspectPng(result.Data, () => checks++);
        for (var stop = 1; stop <= checks; stop++)
        {
            var calls = 0;
            var sentinel = new NativePixelException(EngineErrors.ResourceLimit, "Test deadline exhausted.");
            Assert.Same(sentinel, Assert.Throws<NativePixelException>(() => NativePixelRenderer.InspectPng(result.Data, () =>
            {
                if (++calls == stop) throw sentinel;
            })));
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var canceledError = Assert.Throws<OperationCanceledException>(() =>
            NativePixelRenderer.InspectPng(ReadOnlyMemory<byte>.Empty, () => throw new Exception("Must not run."), canceled.Token));
        Assert.Equal(canceled.Token, canceledError.CancellationToken);
        using var duringCheck = new CancellationTokenSource();
        var callbackCount = 0;
        var duringError = Assert.Throws<OperationCanceledException>(() =>
            NativePixelRenderer.InspectPng(result.Data, () =>
            {
                if (++callbackCount == 3) duringCheck.Cancel();
            }, duringCheck.Token));
        Assert.Equal(duringCheck.Token, duringError.CancellationToken);
        var failure = new NativePixelException(EngineErrors.ResourceLimit, "Test deadline exhausted.");
        Assert.Same(failure, Assert.Throws<NativePixelException>(() =>
            NativePixelRenderer.CompositeHash(doc, payloads, rect, () => throw failure)));
        Assert.Equal(Hash([255, 0, 0, 255]), NativePixelRenderer.CompositeHash(doc, payloads, rect, NoBudget));
    }

    private sealed record TestLayer(byte[] Pixels, byte[]? Mask = null, decimal Opacity = 1m, bool Visible = true);

    private static (NativePixelDocument Doc, Dictionary<string, byte[]> Payloads) Create(
        int width, int height, params TestLayer[] input)
    {
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var layers = ImmutableArray.CreateBuilder<PixelLayerInfo>();
        foreach (var source in input)
        {
            var frame = Frame(width, height, 1, source.Pixels);
            var hash = Hash(frame);
            payloads[hash] = frame;
            string? maskHash = null;
            if (source.Mask is not null)
            {
                var mask = Frame(width, height, 2, source.Mask);
                maskHash = Hash(mask);
                payloads[maskHash] = mask;
            }
            layers.Add(new PixelLayerInfo(Guid.NewGuid(), "Layer", source.Visible, source.Opacity, hash, maskHash));
        }
        return (new NativePixelDocument(Guid.NewGuid(), 1, "Test", width, height, layers.ToImmutable()), payloads);
    }

    private static byte[] Frame(int width, int height, int kind, byte[] raw)
    {
        var result = new byte[24 + raw.Length];
        "AGDPIXL\n"u8.CopyTo(result);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12), kind);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(20), height);
        raw.CopyTo(result, 24);
        return result;
    }

    private static void AssertSamples(NativePixelDocument doc, Dictionary<string, byte[]> payloads, PixelRect rect, byte[] expected)
    {
        var expectedHash = Hash(expected);
        Assert.Equal(expectedHash, NativePixelRenderer.CompositeHash(doc, payloads, rect, NoBudget));
        var encoded = NativePixelRenderer.Encode(doc, payloads, rect, 1024 * 1024, NoBudget);
        Assert.Equal(expectedHash, encoded.RgbaSha256);
        Assert.Equal(FilterZero(expected, rect.Width, rect.Height), Inflate(ReadChunks(encoded.Data)));
        PngStructure.Validate(encoded.Data, rect.Width, rect.Height);
        Assert.Equal(expectedHash, PngRgbaVerifier.Verify(encoded.Data, rect.Width, rect.Height, NoBudget));
        Assert.Equal(new NativePngObservation(rect.Width, rect.Height, encoded.Data.Length, Hash(encoded.Data), expectedHash),
            NativePixelRenderer.InspectPng(encoded.Data, NoBudget));
    }

    private static byte[] Pattern(int width, int height)
    {
        var raw = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                raw[i] = (byte)(x % 256);
                raw[i + 1] = (byte)(y % 256);
                raw[i + 2] = (byte)((x * 17 + y * 29) % 256);
                raw[i + 3] = 255;
            }
        return raw;
    }

    private static byte[] Noise(int width, int height)
    {
        var raw = new byte[width * height * 4];
        uint state = 0x19483725;
        for (var i = 0; i < raw.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            raw[i] = i % 4 == 3 ? (byte)255 : unchecked((byte)state);
        }
        return raw;
    }

    private static byte[] Crop(byte[] raw, int width, PixelRect rect)
    {
        var result = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
            raw.AsSpan(((rect.Y + y) * width + rect.X) * 4, rect.Width * 4).CopyTo(result.AsSpan(y * rect.Width * 4));
        return result;
    }

    private static byte[] FilterZero(byte[] raw, int width, int height)
    {
        var row = width * 4;
        var filtered = new byte[(row + 1) * height];
        for (var y = 0; y < height; y++)
            raw.AsSpan(y * row, row).CopyTo(filtered.AsSpan(y * (row + 1) + 1));
        return filtered;
    }

    private static byte[] Compress(byte[] filtered, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, level, leaveOpen: true))
            stream.Write(filtered);
        return output.ToArray();
    }

    private static byte[] Png(int width, int height, params (string Kind, byte[] Body)[] middle)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        foreach (var (kind, body) in middle)
            Chunk(output, kind, body);
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string kind, byte[] body)
    {
        var bytes = new byte[body.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(bytes, body.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(bytes, 4);
        body.CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 4), TestCrc(bytes.AsSpan(4, body.Length + 4)));
        output.Write(bytes);
    }

    private static List<(string Kind, byte[] Body)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string Kind, byte[] Body)>();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            Assert.Equal(TestCrc(png.AsSpan(offset + 4, length + 4)),
                BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + length + 8)));
            chunks.Add((Encoding.ASCII.GetString(png, offset + 4, 4), png.AsSpan(offset + 8, length).ToArray()));
            offset += length + 12;
        }
        return chunks;
    }

    private static byte[] Inflate(List<(string Kind, byte[] Body)> chunks)
    {
        using var compressed = new MemoryStream();
        foreach (var chunk in chunks.Where(c => c.Kind == "IDAT"))
            compressed.Write(chunk.Body);
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static uint TestCrc(ReadOnlySpan<byte> bytes)
    {
        // Independent bit-at-a-time IEEE CRC, not the production lookup table.
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        return ~crc;
    }

    private static void AssertPngError(string code, byte[] png) => Assert.Equal(code,
        Assert.Throws<NativePixelException>(() => NativePixelRenderer.InspectPng(png, NoBudget)).Code);

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
