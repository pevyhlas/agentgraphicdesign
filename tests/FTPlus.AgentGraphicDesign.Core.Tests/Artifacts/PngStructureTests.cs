using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Artifacts;

public sealed class PngStructureTests
{
    private const string Invalid = "ARTIFACT_PNG_INVALID";
    private const string Unsupported = "ARTIFACT_PROFILE_UNSUPPORTED";
    private const int MaxBytes = 64 * 1024 * 1024;
    private const string FixtureHash = "18f4fa5bba0f3aee92c9c6c7b85367a2247e976a9ff46056a06338685487cfc1";
    private const string HeaderHex = "0000000D49484452000000010000000108060000001F15C489";
    // zlib 78 01, final stored block, LEN=5/NLEN=FFFA, filter=0, red RGBA pixel, Adler=050001FF.
    private const string IdatHex = "00000010494441547801010500FAFF00FF0000FF050001FFFA5C88D1";
    private const string EndHex = "0000000049454E44AE426082";
    private static readonly byte[] Signature = Convert.FromHexString("89504E470D0A1A0A");
    private static readonly byte[] Header = Convert.FromHexString(HeaderHex);
    private static readonly byte[] Idat = Convert.FromHexString(IdatHex);
    private static readonly byte[] End = Convert.FromHexString(EndHex);
    private static readonly byte[] LiteralPng = Convert.FromHexString("89504E470D0A1A0A" + HeaderHex + IdatHex + EndHex);

    // Core has no friend assembly. A typed delegate preserves spans, cancellation and original exceptions.
    private delegate void ValidatePng(ReadOnlySpan<byte> data, long width, long height, CancellationToken ct = default);
    private static readonly MethodInfo ValidateMethod = typeof(ArtifactDescriptorContract).Assembly
        .GetType("FTPlus.AgentGraphicDesign.Core.Artifacts.PngStructure", throwOnError: true)!
        .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ValidatePng Validate = ValidateMethod.CreateDelegate<ValidatePng>();

    [Fact]
    public void Api_IsInternalStaticVoidWithOptionalCt()
    {
        var type = ValidateMethod.DeclaringType!;
        Assert.True(type.IsNotPublic && type.IsAbstract && type.IsSealed);
        Assert.True(ValidateMethod.IsAssembly && ValidateMethod.IsStatic);
        Assert.Equal(typeof(void), ValidateMethod.ReturnType);
        var parameters = ValidateMethod.GetParameters();
        Assert.Equal(new[] { typeof(ReadOnlySpan<byte>), typeof(long), typeof(long), typeof(CancellationToken) },
            parameters.Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal("ct", parameters[3].Name);
        Assert.True(parameters[3].HasDefaultValue);
    }

    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("313233343536373839", 0xcbf43926u)]
    [InlineData("4948445200000001000000010806000000", 0x1f15c489u)]
    [InlineData("49454E44", 0xae426082u)]
    [InlineData("494441547801010500FAFF00FF0000FF050001FF", 0xfa5c88d1u)]
    public void IndependentBitwiseWriter_MatchesLiteralIeeeCrcVectors(string hex, uint crc) =>
        Assert.Equal(crc, TestCrc(Convert.FromHexString(hex)));

    [Fact]
    public void SharedLiteralFixture_IsDecodableRedRgbaAndMatchesDescriptorClaims()
    {
        var png = Convert.FromBase64String(File.ReadAllText(FixturePath("preflight-rgba.png.base64")));
        Assert.Equal(LiteralPng, png);
        Assert.Equal(73, png.Length);
        Assert.Equal(FixtureHash, Convert.ToHexStringLower(SHA256.HashData(png)));
        var parsed = ArtifactDescriptorContract.Parse(File.ReadAllBytes(FixturePath("preflight-bundle.json")));
        Assert.True(parsed.Success, parsed.Error?.ToString());
        var descriptor = Assert.IsType<ProvidedArtifactDescriptor>(parsed.Descriptor);
        Assert.Equal(1L, descriptor.Canvas.Width);
        Assert.Equal(1L, descriptor.Canvas.Height);
        var file = Assert.Single(descriptor.Files);
        Assert.Equal("image.png", file.Path);
        Assert.Equal(73L, file.ByteLength);
        Assert.Equal(FixtureHash, file.Sha256);
        Assert.Equal(FixtureHash, descriptor.Still!.FileSha256);
        Validate(png, descriptor.Canvas.Width, descriptor.Canvas.Height);

        // Test-only inflation independently verifies this literal fixture, not the production policy.
        using var compressed = new MemoryStream(png, 41, 16);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var scanline = new byte[5];
        zlib.ReadExactly(scanline);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255 }, scanline);
        Assert.Equal(-1, zlib.ReadByte());
        Assert.Equal(0x050001ffu, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(53, 4)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AllowedColorTags_AllSubsetsAndOrdersAndIntentsSucceed(int intent)
    {
        var tags = new[] { Chunk("sRGB", [(byte)intent]), ColorTag("gAMA"), ColorTag("cHRM") };
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var order in orders)
            for (var subset = 0; subset < 8; subset++)
            {
                var chunks = new List<byte[]> { Header };
                chunks.AddRange(order.Where(index => (subset & (1 << index)) != 0).Select(index => tags[index]));
                chunks.AddRange([Idat, End]);
                Validate(Png(chunks.ToArray()), 1, 1);
            }
    }

    [Theory]
    [InlineData("sRGB")]
    [InlineData("gAMA")]
    [InlineData("cHRM")]
    public void ColorTags_MustBeUniqueAndBeforeAllIdatIncludingEmptyChunks(string type)
    {
        var tag = ColorTag(type);
        var other = ColorTag(type == "sRGB" ? "gAMA" : "sRGB");
        AssertError(Png(Header, tag, tag, Idat, End));
        AssertError(Png(Header, tag, other, tag, Idat, End));
        AssertError(Png(Header, Idat, tag, End));
        AssertError(Png(Header, Idat, tag, Idat, End));
        AssertError(Png(Header, Chunk("IDAT"), tag, Idat, End));
    }

    [Theory]
    [InlineData("sRGB", 0)]
    [InlineData("sRGB", 2)]
    [InlineData("gAMA", 0)]
    [InlineData("gAMA", 3)]
    [InlineData("gAMA", 5)]
    [InlineData("cHRM", 0)]
    [InlineData("cHRM", 31)]
    [InlineData("cHRM", 33)]
    public void ColorTags_WrongLengthsAreInvalid(string type, int length) =>
        AssertError(Png(Header, Chunk(type, new byte[length]), Idat, End));

    [Theory]
    [InlineData(4)]
    [InlineData(255)]
    public void Srgb_UndefinedIntentIsInvalid(int intent) =>
        AssertError(Png(Header, Chunk("sRGB", [(byte)intent]), Idat, End));

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(45454u)]
    [InlineData(45456u)]
    [InlineData(100000u)]
    [InlineData(0x8000b18fu)]
    [InlineData(uint.MaxValue)]
    public void Gamma_NonPolicyValuesAreUnsupportedAloneAndWithSrgb(uint gamma)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(body, gamma);
        var tag = Chunk("gAMA", body);
        AssertError(Png(Header, tag, Idat, End), Unsupported);
        AssertError(Png(Header, ColorTag("sRGB"), tag, Idat, End), Unsupported);
        AssertError(Png(Header, tag, ColorTag("sRGB"), Idat, End), Unsupported);
    }

    [Fact]
    public void Chromaticities_EveryNonPolicyCoordinateIsUnsupportedWithOrWithoutOtherTags()
    {
        for (var index = 0; index < 8; index++)
            foreach (var value in new[] { 0u, 1u, 100000u, uint.MaxValue })
            {
                var body = ColorTag("cHRM")[8..^4];
                BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(index * 4, 4), value);
                var tag = Chunk("cHRM", body);
                AssertError(Png(Header, tag, Idat, End), Unsupported);
                AssertError(Png(Header, ColorTag("sRGB"), ColorTag("gAMA"), tag, Idat, End), Unsupported);
                AssertError(Png(Header, tag, ColorTag("sRGB"), ColorTag("gAMA"), Idat, End), Unsupported);
            }
    }

    [Theory]
    [InlineData("IHDR")]
    [InlineData("sRGB")]
    [InlineData("gAMA")]
    [InlineData("cHRM")]
    [InlineData("IDAT")]
    [InlineData("IEND")]
    public void EveryAcceptedChunkType_RequiresCrcOverBothTypeAndData(string type)
    {
        var original = type switch
        {
            "IHDR" => Header,
            "IDAT" => Idat,
            "IEND" => End,
            _ => ColorTag(type)
        };
        for (var crcByte = original.Length - 4; crcByte < original.Length; crcByte++)
        {
            var damaged = (byte[])original.Clone();
            damaged[crcByte] ^= 0x80;
            AssertError(WithChunk(type, damaged));
        }
        for (var coveredByte = 4; coveredByte < original.Length - 4; coveredByte++)
        {
            var damaged = (byte[])original.Clone();
            damaged[coveredByte] ^= 1;
            AssertError(WithChunk(type, damaged));
        }
        // A data-only checksum is not the PNG checksum.
        var wrongCoverage = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(wrongCoverage.AsSpan(wrongCoverage.Length - 4),
            TestCrc(original.AsSpan(8, original.Length - 12)));
        AssertError(WithChunk(type, wrongCoverage));
    }

    [Fact]
    public void Signature_IsExactAndEveryProperFixturePrefixIsTruncated()
    {
        for (var index = 0; index < Signature.Length; index++)
        {
            var png = (byte[])LiteralPng.Clone();
            png[index] ^= 1;
            AssertError(png);
        }
        for (var length = 0; length < LiteralPng.Length; length++)
            AssertError(LiteralPng[..length]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(12u)]
    [InlineData(14u)]
    [InlineData(100u)]
    [InlineData(0x7ffffff4u)]
    [InlineData(0x7fffffffu)]
    [InlineData(0x80000000u)]
    [InlineData(0xfffffff4u)]
    [InlineData(uint.MaxValue)]
    public void UntrustedChunkLengths_CannotWrapOffsetsOrSpanCasts(uint length)
    {
        foreach (var offset in new[] { 8, 33 })
        {
            var png = (byte[])LiteralPng.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(offset, 4), length);
            AssertError(png);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(26)]
    public void Ihdr_WrongLengthWithCorrectCrcIsInvalid(int length) =>
        AssertError(Png(Chunk("IHDR", new byte[length]), Idat, End));

    [Fact]
    public void Ihdr_MustBeFirstAndUnique()
    {
        foreach (var chunk in new[] { ColorTag("sRGB"), ColorTag("gAMA"), ColorTag("cHRM"), Idat, End })
            AssertError(Png(chunk, Header, Idat, End));
        AssertError(Png(Header, Header, Idat, End));
        AssertError(Png(Header, ColorTag("sRGB"), Header, Idat, End));
        AssertError(Png(Header, Idat, Header, End));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x80000000u)]
    [InlineData(uint.MaxValue)]
    public void Ihdr_DimensionsMustBePositivePngIntegersEvenWhenClaimsMatch(uint dimension)
    {
        AssertError(Png(Ihdr(width: dimension), Idat, End), width: dimension);
        AssertError(Png(Ihdr(height: dimension), Idat, End), height: dimension);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(2L)]
    [InlineData(4294967297L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void ActualDimensions_MustMatchLongClaimsWithoutNarrowing(long claim)
    {
        AssertError(LiteralPng, width: claim);
        AssertError(LiteralPng, height: claim);
    }

    [Fact]
    public void Dimensions_AreReadBigEndianOnBothAxesWithoutInspectingRows()
    {
        var png = Png(Ihdr(width: 258, height: 513), Idat, End);
        Validate(png, 258, 513);
        AssertError(png, width: 513, height: 258);
        AssertError(png, width: 258, height: 1);
        AssertError(png, width: 1, height: 513);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 1)]
    [InlineData(8, 4)]
    [InlineData(8, 7)]
    [InlineData(8, 255)]
    [InlineData(9, 1)]
    [InlineData(9, 5)]
    [InlineData(9, 7)]
    [InlineData(9, 255)]
    [InlineData(10, 1)]
    [InlineData(10, 255)]
    [InlineData(11, 1)]
    [InlineData(11, 255)]
    [InlineData(12, 2)]
    [InlineData(12, 255)]
    public void Ihdr_IllegalFieldsAreInvalidNotUnsupported(int index, int value)
    {
        var body = Header[8..^4];
        body[index] = (byte)value;
        AssertError(Png(Chunk("IHDR", body), Idat, End));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 4)]
    [InlineData(0, 8)]
    [InlineData(0, 16)]
    [InlineData(2, 8)]
    [InlineData(2, 16)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(4, 16)]
    [InlineData(6, 16)]
    public void Ihdr_OtherLegalColorAndDepthProfilesAreUnsupported(int color, int depth) =>
        AssertError(Png(Ihdr(color: (byte)color, depth: (byte)depth), Idat, End), Unsupported);

    [Fact]
    public void Ihdr_Adam7IsUnsupportedButIllegalDepthColorCombinationsAreInvalid()
    {
        AssertError(Png(Ihdr(interlace: 1), Idat, End), Unsupported);
        AssertError(Png(Ihdr(color: 2, depth: 4), Idat, End));
        AssertError(Png(Ihdr(color: 3, depth: 16), Idat, End));
        AssertError(Png(Ihdr(color: 4, depth: 1), Idat, End));
    }

    [Theory]
    [InlineData(2, 8, 0, "00FF0000")] // RGB red.
    [InlineData(0, 8, 0, "00FF")] // Grayscale white.
    [InlineData(4, 8, 0, "00FFFF")] // Grayscale with alpha.
    [InlineData(6, 16, 0, "00FFFF00000000FFFF")] // 16-bit RGBA red.
    [InlineData(6, 8, 1, "00FF0000FF")] // 1x1 Adam7: only the first pass has a pixel.
    public void CompleteLegalNonPolicyPngs_AreUnsupported(int color, int depth, int interlace, string scanlineHex)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
            zlib.Write(Convert.FromHexString(scanlineHex));
        AssertError(Png(Ihdr(color: (byte)color, depth: (byte)depth, interlace: (byte)interlace),
            Chunk("IDAT", output.ToArray()), End), Unsupported);
    }

    [Theory]
    [InlineData("acTL")]
    [InlineData("fcTL")]
    [InlineData("fdAT")]
    [InlineData("iCCP")]
    [InlineData("cICP")]
    [InlineData("mDCV")]
    [InlineData("cLLI")]
    [InlineData("tEXt")]
    [InlineData("zTXt")]
    [InlineData("iTXt")]
    [InlineData("eXIf")]
    [InlineData("sPLT")]
    [InlineData("PLTE")]
    [InlineData("tRNS")]
    [InlineData("pHYs")]
    [InlineData("tIME")]
    [InlineData("vpAg")]
    [InlineData("ABCD")]
    public void NonAllowlistedChunkTypes_AreUnsupportedAndCannotSplitIdat(string type)
    {
        var chunk = Chunk(type, Encoding.ASCII.GetBytes("private-input-marker"));
        AssertError(Png(Header, chunk, Idat, End), Unsupported);
        AssertError(Png(Header, Idat, chunk, Idat, End), Unsupported);
        chunk[^1] ^= 1;
        AssertError(Png(Header, chunk, Idat, End));
    }

    [Theory]
    [InlineData("IDaT")]
    [InlineData("1234")]
    [InlineData("ID T")]
    [InlineData("ID\0T")]
    [InlineData("ID[T")]
    public void IllegalChunkTypeBytesOrReservedBit_AreInvalid(string type) =>
        AssertError(Png(Header, Chunk(type), Idat, End));

    [Fact]
    public void Idat_RequiresNonemptyAggregateButAllowsEmptyChunksAnywhereInRun()
    {
        var empty = Chunk("IDAT");
        AssertError(Png(Header, End));
        AssertError(Png(Header, empty, End));
        AssertError(Png(Header, empty, empty, End));
        Validate(Png(Header, empty, Idat, empty, End), 1, 1);
        var compressed = Idat[8..^4];
        for (var split = 0; split <= compressed.Length; split++)
            Validate(Png(Header, empty, Chunk("IDAT", compressed[..split]), empty,
                Chunk("IDAT", compressed[split..]), empty, End), 1, 1);
    }

    [Fact]
    public void Iend_MustBeEmptyPresentAndTerminalAtExactEof()
    {
        AssertError(Png(Header, Idat));
        AssertError(Png(Header, Idat, Chunk("IEND", [0])));
        AssertError([.. LiteralPng, 0]);
        foreach (var chunk in new[] { Header, ColorTag("sRGB"), ColorTag("gAMA"), ColorTag("cHRM"), Idat, End })
            AssertError(Png(Header, Idat, End, chunk));
        AssertError([.. LiteralPng, .. LiteralPng]);
    }

    [Fact]
    public void ChunkLimit_IsInclusiveAndCountsEmptyIdatAndIend()
    {
        var chunks = new List<byte[]> { Header, Idat };
        chunks.AddRange(Enumerable.Repeat(Chunk("IDAT"), 4093));
        chunks.Add(End);
        Assert.Equal(4096, chunks.Count);
        Validate(Png(chunks.ToArray()), 1, 1);
        chunks.Insert(chunks.Count - 1, Chunk("IDAT"));
        AssertError(Png(chunks.ToArray()), EngineErrors.ResourceLimit);
    }

    [Theory]
    [InlineData("00")] // Not even a zlib header.
    [InlineData("0000000000000000")] // Invalid zlib CMF/FLG.
    [InlineData("7801")] // No DEFLATE stream or Adler checksum.
    [InlineData("78010700000000")] // Reserved DEFLATE block type.
    [InlineData("7801010500FAFF00FF0000FF050001FE")] // Wrong Adler checksum.
    [InlineData("7801010500FAFF00FF0000FF0500")] // Truncated checksum.
    public void InvalidCompressedData_WithValidOuterCrcDeliberatelyPasses(string compressedHex)
    {
        var compressed = Convert.FromHexString(compressedHex);
        var png = Png(Header, Chunk("IDAT", compressed), End);
        Assert.Equal(TestCrc(png.AsSpan(37, compressed.Length + 4)),
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(41 + compressed.Length, 4)));
        Validate(png, 1, 1);
    }

    [Theory]
    [InlineData("05FF0000FF")] // Undefined row filter.
    [InlineData("00FF0000")] // Short scanline.
    [InlineData("00FF0000FF00FF0000FF")] // Extra row.
    [InlineData("00FF000000")] // Hidden red in a transparent pixel.
    public void OpaqueIdat_DoesNotInspectRowFiltersRowCountsOrPixelSamples(string scanlineHex)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
            zlib.Write(Convert.FromHexString(scanlineHex));
        Validate(Png(Header, Chunk("IDAT", output.ToArray()), End), 1, 1);
    }

    [Fact]
    public void ByteLimit_IsInclusiveAndLargeCrcIsCancellableWithoutPayloadAllocation()
    {
        // One allocation for a maximum-size PNG and an extra byte to exercise the early bound.
        var png = new byte[MaxBytes + 1];
        Signature.CopyTo(png, 0);
        Header.CopyTo(png, 8);
        var bodyLength = MaxBytes - 57;
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33, 4), (uint)bodyLength);
        "IDAT"u8.CopyTo(png.AsSpan(37, 4));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(41 + bodyLength, 4), TestCrc(png.AsSpan(37, bodyLength + 4)));
        End.CopyTo(png, MaxBytes - End.Length);
        Validate(LiteralPng, 1, 1); // Warm the delegate and static CRC table before measuring.
        var before = GC.GetAllocatedBytesForCurrentThread();
        Validate(png.AsSpan(0, MaxBytes), 1, 1);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0L, 1024L);
        AssertError(png, EngineErrors.ResourceLimit);

        // checkBudget runs at every cancellation checkpoint, right after the token check. Checkpoints
        // 1-5 are the entry, IHDR (chunk, one CRC block, post-CRC) and the IDAT chunk; the IDAT CRC
        // then checks before each of its 1024 64 KiB blocks. Cancelling at the checkpoint before
        // block 512 must throw at the very next one, before block 513, after 32 MiB of CRC. Without
        // the per-block check the whole walk has only 7 checkpoints, so nothing would be cancelled.
        const int cancelAt = 5 + 512;
        using var cts = new CancellationTokenSource();
        var checkpoints = 0;
        var cancelled = Assert.Throws<OperationCanceledException>(() => PngStructure.ValidateBounded(
            png.AsSpan(0, MaxBytes), 1, 1, cts.Token,
            checkBudget: () => { if (++checkpoints == cancelAt) cts.Cancel(); }, maxBytes: MaxBytes));
        Assert.Equal(cts.Token, cancelled.CancellationToken);
        Assert.Equal(cancelAt, checkpoints);

        // Mutation beyond several 64 KiB blocks must still affect the final CRC.
        png[MaxBytes - 17] ^= 0x80;
        Assert.Equal(Invalid, Assert.Throws<DocumentEngineException>(() => Validate(png.AsSpan(0, MaxBytes), 1, 1)).Code);
    }

    [Fact]
    public void PreCancelledToken_PropagatesBeforeValidOrInvalidInputChecks()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        foreach (var png in new[] { LiteralPng, Array.Empty<byte>() })
        {
            var error = Assert.Throws<OperationCanceledException>(() => Validate(png, 1, 1, ct: cts.Token));
            Assert.Equal(cts.Token, error.CancellationToken);
        }
    }

    private static void AssertError(byte[] png, string code = Invalid, long width = 1, long height = 1)
    {
        var error = Assert.Throws<DocumentEngineException>(() => Validate(png, width, height));
        Assert.Equal(code, error.Code);
        Assert.InRange(error.Message.Length, 1, 1024);
        Assert.DoesNotContain("private-input-marker", error.Message);
        if (code == Invalid)
            Assert.Equal("Invalid PNG structure.", error.Message);
        else if (code == Unsupported)
            Assert.Equal("PNG profile is unsupported by the structural policy.", error.Message);
        else
            Assert.Contains(error.Message, new[] { "PNG input exceeds 64 MiB.", "PNG input exceeds 4096 chunks." });
    }

    private static byte[] WithChunk(string type, byte[] chunk) => type switch
    {
        "IHDR" => Png(chunk, Idat, End),
        "IDAT" => Png(Header, chunk, End),
        "IEND" => Png(Header, Idat, chunk),
        _ => Png(Header, chunk, Idat, End)
    };

    private static byte[] Ihdr(uint width = 1, uint height = 1, byte depth = 8, byte color = 6, byte interlace = 0)
    {
        var body = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(body, width);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), height);
        body[8] = depth;
        body[9] = color;
        body[12] = interlace;
        return Chunk("IHDR", body);
    }

    private static byte[] ColorTag(string type) => Chunk(type, type switch
    {
        "sRGB" => [0],
        "gAMA" => Convert.FromHexString("0000B18F"),
        "cHRM" => Convert.FromHexString("00007A26000080840000FA00000080E8000075300000EA6000003A9800001770"),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    });

    private static byte[] Png(params byte[][] chunks)
    {
        var png = new byte[Signature.Length + chunks.Sum(chunk => chunk.Length)];
        Signature.CopyTo(png, 0);
        var offset = Signature.Length;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(png, offset);
            offset += chunk.Length;
        }
        return png;
    }

    private static byte[] Chunk(string type, byte[]? body = null)
    {
        body ??= [];
        var chunk = new byte[body.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)body.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        body.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(body.Length + 8), TestCrc(chunk.AsSpan(4, body.Length + 4)));
        return chunk;
    }

    // Independent test-only bitwise IEEE CRC; deliberately does not use the production table or helpers.
    private static uint TestCrc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
        }
        return ~crc;
    }

    private static string FixturePath(string name) => Path.Combine(
        JsonSchemaContractTests.FindRepositoryRoot(), "tests", "FTPlus.AgentGraphicDesign.Core.Tests",
        "Artifacts", "Fixtures", name);
}
