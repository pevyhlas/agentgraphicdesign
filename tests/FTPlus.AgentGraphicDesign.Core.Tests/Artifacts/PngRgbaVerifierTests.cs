using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Artifacts;

public sealed class PngRgbaVerifierTests
{
    private const string Invalid = "ARTIFACT_PNG_INVALID";
    private static readonly Action NoBudget = static () => { };
    private static readonly byte[] RedMember = Convert.FromHexString("7801010500FAFF00FF0000FF050001FF");
    private static readonly byte[] ZeroPixel = new byte[5];
    private static readonly byte[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
    // A complete, deliberately uncomplicated test alphabet, including all three repeat instructions.
    private static readonly byte[] CodeLengthAlphabet = Enumerable.Range(0, 19).Select(i => (byte)(i < 13 ? 4 : 5)).ToArray();
    private delegate string VerifyPng(ReadOnlySpan<byte> png, long width, long height, Action checkBudget);
    private delegate void ValidatePng(ReadOnlySpan<byte> png, long width, long height, CancellationToken ct);
    private delegate object DecodePng(ReadOnlySpan<byte> png, int width, int height, Action checkBudget, Action<long> reserve);
    private static readonly MethodInfo VerifyMethod = typeof(ArtifactDescriptorContract).Assembly
        .GetType("FTPlus.AgentGraphicDesign.Core.Artifacts.PngRgbaVerifier", throwOnError: true)!
        .GetMethod("Verify", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly VerifyPng Verify = VerifyMethod.CreateDelegate<VerifyPng>();
    private static readonly DecodePng Decode = VerifyMethod.DeclaringType!
        .GetMethod("DecodeOwned", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<DecodePng>();
    private static readonly ValidatePng Validate = typeof(ArtifactDescriptorContract).Assembly
        .GetType("FTPlus.AgentGraphicDesign.Core.Artifacts.PngStructure", throwOnError: true)!
        .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<ValidatePng>();

    [Fact]
    public void Api_IsInternalStaticAndUsesTypedSpanDelegate()
    {
        var type = VerifyMethod.DeclaringType!;
        Assert.True(type.IsNotPublic && type.IsAbstract && type.IsSealed);
        Assert.True(VerifyMethod.IsAssembly && VerifyMethod.IsStatic);
        Assert.Equal(typeof(string), VerifyMethod.ReturnType);
        Assert.Equal(new[] { typeof(ReadOnlySpan<byte>), typeof(long), typeof(long), typeof(Action) },
            VerifyMethod.GetParameters().Select(p => p.ParameterType).ToArray());
    }

    [Fact]
    public void SharedLiteralRedFixture_HashesRawSamplesWithoutChangingInput()
    {
        var path = Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "tests",
            "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures", "preflight-rgba.png.base64");
        var png = Convert.FromBase64String(File.ReadAllText(path));
        var original = (byte[])png.Clone();
        Assert.Equal(73, png.Length);
        Validate(png, 1, 1, default);
        var hash = Verify(png, 1, 1, NoBudget);
        Assert.Equal(Hash([255, 0, 0, 255]), hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(original, png);
        Assert.Equal(RedMember, Stored([0, 255, 0, 0, 255]));
        // Literal fixed-Huffman zero scanline, including an overlapping distance-one match.
        Assert.Equal("df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119",
            Check(Convert.FromHexString("789C636000020000050001")));
    }

    [Theory]
    [InlineData(0, "0A141E0032281E01FF0080FF", "140A2801283C1E0001FF0080")]
    [InlineData(1, "0A141E0028140001CDD862FE", "140A28011432F6FFD9C3E280")]
    [InlineData(2, "0A141E0032281E01FF0080FF", "0AF60A01F61400FF02FF8081")]
    [InlineData(3, "0A141E002D1E0F01E6EC71FF", "0F0019010523FBFF6EE1B101")]
    [InlineData(4, "0A141E0028140001CDD862FE", "0AF60A01F614F6FF02FF8081")]
    public void AllFilters_LiteralBoundaryFloorWrapAndHiddenAlphaSamples(int filter, string first, string second)
    {
        byte[] raw = [10, 20, 30, 0, 50, 40, 30, 1, 255, 0, 128, 255,
            20, 10, 40, 1, 40, 60, 30, 0, 1, 255, 0, 128];
        byte[] filtered = [(byte)filter, .. Convert.FromHexString(first), (byte)filter, .. Convert.FromHexString(second)];
        foreach (var member in new[] { Stored(filtered), Fixed(filtered), Dynamic(filtered), Compress(filtered) })
        {
            Assert.Equal(Hash(raw), Check(member, 3, 2));
            var png = Png(3, 2, member);
            var original = (byte[])png.Clone();
            long reserved = 0;
            var decoded = Decode(png, 3, 2, NoBudget, bytes => reserved += bytes);
            Assert.Equal(raw, Samples(decoded));
            Assert.Equal(Hash(raw), decoded.GetType().GetProperty("RgbaSha256")!.GetValue(decoded));
            Assert.True(reserved >= raw.Length + 32768 + 2 * 3 * 4);
            Samples(decoded)[0] ^= 1;
            Assert.Equal(original, png);
            Assert.Equal(raw, Samples(Decode(png, 3, 2, NoBudget, _ => { })));
        }
    }

    [Fact]
    public void OwnedOutput_RejectsBadTrailerAndCancellationAfterFirstRowWithoutReturningSamples()
    {
        var member = Stored([0, 10, 20, 30, 0, 0, 1, 2, 3, 255]);
        member[^1] ^= 1; // Valid enclosing PNG CRC, invalid Adler after all samples have been emitted.
        var png = Png(1, 2, member);
        Validate(png, 1, 2, default);
        object? output = null;
        var failure = Assert.Throws<DocumentEngineException>(() => output = Decode(png, 1, 2, NoBudget, _ => { }));
        Assert.Equal(Invalid, failure.Code);
        Assert.Null(output);

        var good = Png(1, 2, Stored([0, 10, 20, 30, 0, 0, 1, 2, 3, 255]));
        using var cancellation = new CancellationTokenSource();
        int checks = 0;
        Assert.Throws<OperationCanceledException>(() => output = Decode(good, 1, 2, () =>
        {
            if (++checks == 5) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }, _ => { }));
        Assert.Null(output);
        Assert.Equal(new byte[] { 10, 20, 30, 0, 1, 2, 3, 255 }, Samples(Decode(good, 1, 2, NoBudget, _ => { })));
    }

    [Fact]
    public void OwnedOutput_NativeLimitAndReservationFailurePrecedeDecoderAllocationOrInputInterpretation()
    {
        int reserves = 0;
        var tooLarge = Assert.Throws<DocumentEngineException>(() => Decode([], 8192, 513, NoBudget, _ => reserves++));
        Assert.Equal("RESOURCE_LIMIT", tooLarge.Code);
        Assert.Equal(0, reserves);
        var refusal = new InvalidOperationException("reservation refused");
        Assert.Same(refusal, Assert.Throws<InvalidOperationException>(() => Decode([], 1, 1, NoBudget, _ => throw refusal)));
    }

    private static byte[] Samples(object decoded) => (byte[])decoded.GetType().GetProperty("Rgba")!.GetValue(decoded)!;

    [Theory]
    [InlineData(40, 10, 236)] // Left/corner tie must select left, not corner.
    [InlineData(10, 40, 10)] // Up/corner tie must select up, not corner.
    public void Paeth_DistinctPredictorTies(int up, int left, int firstDifference)
    {
        byte[] raw = [30, 30, 30, 30, (byte)up, (byte)up, (byte)up, (byte)up,
            (byte)left, (byte)left, (byte)left, (byte)left, 77, 77, 77, 77];
        byte[] filtered = [0, .. raw[..8], 4, (byte)firstDifference, (byte)firstDifference,
            (byte)firstDifference, (byte)firstDifference, 67, 67, 67, 67];
        Assert.Equal(Hash(raw), Check(Stored(filtered), 2, 2));
    }

    [Fact]
    public void Idat_AllByteSplitsAndEmptyChunksIncludingUnfedTrailingBytes()
    {
        foreach (var member in new[] { RedMember, Fixed([0, 255, 0, 0, 255]), Dynamic([0, 255, 0, 0, 255]) })
        {
            for (var split = 0; split <= member.Length; split++)
            {
                var png = Png(1, 1, [], member[..split], [], member[split..], []);
                Validate(png, 1, 1, default);
                Assert.Equal(Hash([255, 0, 0, 255]), Verify(png, 1, 1, NoBudget));
            }
            var individual = member.Select(b => new[] { b }).ToArray();
            Assert.Equal(Hash([255, 0, 0, 255]), CheckPng(Png(1, 1, individual), 1, 1));
            ErrorPng(Png(1, 1, member, [], [0], []));
            ErrorPng(Png(1, 1, member, [], Stored([]), []));
            ErrorPng(Png(1, 1, member, [], RedMember, []));
        }
    }

    [Fact]
    public void EveryTruncationOfStoredFixedAndDynamicMembers_IsInvalid()
    {
        foreach (var member in new[] { RedMember, Fixed(ZeroPixel), Dynamic(ZeroPixel) })
        {
            Assert.NotNull(Check(member));
            // Empty IDAT is outside the structure precondition; test all structurally admissible prefixes.
            for (var length = 1; length < member.Length; length++)
                Error(member[..length]);
            for (var i = 1; i <= 4; i++)
            {
                var corrupted = (byte[])member.Clone();
                corrupted[^i] ^= 1;
                Error(corrupted);
            }
            Error([.. member, 0]);
            Error([.. member, .. Stored([])]);
            Error([.. member, .. member]);
        }
    }

    [Theory]
    [InlineData(0x00, 0)] // CM is not DEFLATE, even with a matching FCHECK.
    [InlineData(0x79, 0)]
    [InlineData(0x88, 0)] // CINFO exceeds 7.
    [InlineData(0xf8, 0)]
    [InlineData(0x78, 0x20)] // Preset dictionary.
    public void ZlibHeader_RejectsUnsupportedFieldsWithValidFcheck(int cmf, int flags)
    {
        var member = (byte[])RedMember.Clone();
        member[0] = (byte)cmf;
        member[1] = HeaderFlags((byte)cmf, (byte)flags);
        Error(member);
    }

    [Fact]
    public void ZlibHeader_FcheckAndAllAdvertisedWindowsAndCompressionLevels()
    {
        var bad = (byte[])RedMember.Clone();
        bad[1] ^= 1;
        Error(bad);
        for (var cinfo = 0; cinfo <= 7; cinfo++)
            for (var level = 0; level < 4; level++)
            {
                var member = (byte[])RedMember.Clone();
                member[0] = (byte)((cinfo << 4) | 8);
                member[1] = HeaderFlags(member[0], (byte)(level << 6));
                Assert.Equal(Hash([255, 0, 0, 255]), Check(member));
            }
    }

    [Theory]
    [InlineData("789C03030000050001")] // Historical invalid match before any history, despite matching size/Adler.
    [InlineData("78010700000000")] // Reserved BTYPE.
    [InlineData("7801010500FBFF00FF0000FF050001FF")] // LEN/NLEN mismatch.
    [InlineData("7801000500FAFF00FF0000FF050001FF")] // Output filled, but no final block.
    public void LiteralMalformedMembers_AreNotSuccessfulStructuralProofs(string hex) => Error(Convert.FromHexString(hex));

    [Theory]
    [InlineData("00FF0000")]
    [InlineData("00FF0000FF00")]
    [InlineData("00FF0000FF00FF0000FF")]
    [InlineData("05FF0000FF")]
    [InlineData("FFFF0000FF")]
    public void FilterAndExactOutputLength_AreEnforced(string hex)
    {
        var filtered = Convert.FromHexString(hex);
        Error(Stored(filtered));
        Error(Fixed(filtered));
        Error(Dynamic(filtered));
    }

    [Fact]
    public void FinalAndStoredAlignmentBits_NeedNotBeZero()
    {
        var stored = (byte[])RedMember.Clone();
        stored[2] |= 0xf8;
        Assert.Equal(Hash([255, 0, 0, 255]), Check(stored));
        var bits = new Bits();
        bits.FixedHeader(true);
        foreach (var value in ZeroPixel)
            bits.FixedSymbol(value);
        bits.FixedSymbol(256);
        bits.Align(ones: true);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));
    }

    [Fact]
    public void OverlapAndFilteredHistory_ArePreservedAcrossBlockAndRowBoundaries()
    {
        var overlap = new Bits();
        overlap.FixedHeader(true);
        overlap.FixedSymbol(0);
        overlap.FixedSymbol(258); // Four bytes at distance one, with only one byte of history.
        overlap.Code(0, 5);
        overlap.FixedSymbol(256);
        Assert.Equal(Hash(new byte[4]), Check(Member(overlap, ZeroPixel)));

        byte[] row = [1, 10, 20, 30, 0, 0, 0, 0, 0];
        var bits = new Bits();
        bits.StoredBlock(false, row);
        bits.FixedHeader(false);
        bits.FixedSymbol(256); // Empty intervening block does not reset history.
        bits.FixedHeader(true);
        bits.FixedSymbol(263); // Nine bytes at distance nine.
        bits.Code(6, 5);
        bits.Write(0, 2);
        bits.FixedSymbol(256);
        byte[] raw = [10, 20, 30, 0, 10, 20, 30, 0, 10, 20, 30, 0, 10, 20, 30, 0];
        Assert.Equal(Hash(raw), Check(Member(bits, [.. row, .. row]), 2, 2));
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void SmallWindow_EnforcedEvenWhenDistanceIsWithinProducedHistory(int distance, bool valid)
    {
        var bits = new Bits();
        bits.StoredBlock(false, new byte[300]);
        bits.FixedHeader(true);
        bits.FixedSymbol(259); // Five bytes.
        bits.Code(distance == 256 ? 15 : 16, 5);
        bits.Write(distance == 256 ? 63 : 0, distance == 256 ? 6 : 7);
        bits.FixedSymbol(256);
        var member = Member(bits, new byte[305], cmf: 8);
        if (valid)
            Assert.Equal(Hash(new byte[304]), Check(member, 76, 1));
        else
            Error(member, 76, 1);
    }

    [Fact]
    public void FullWindowRing_WrapsAndSupportsDistance32768()
    {
        var history = Enumerable.Range(0, 32768).Select(i => unchecked((byte)(i * 73 + 19))).ToArray();
        history[0] = history[1] = 0; // Both row-filter positions in the repeated stream must be zero.
        var bits = new Bits();
        bits.StoredBlock(false, history);
        bits.FixedHeader(true);
        for (var i = 0; i < 128; i++)
        {
            bits.FixedSymbol(i == 127 ? 258 : 285); // 127 * 258 + 4 = 32770 additional bytes.
            bits.Code(29, 5);
            bits.Write(8191, 13);
        }
        bits.FixedSymbol(256);
        byte[] filtered = [.. history, .. history, history[0], history[1]];
        byte[] raw = [.. filtered[1..32769], .. filtered[32770..]];
        Assert.Equal(Hash(raw), Check(Member(bits, filtered), 8192, 2));
    }

    [Fact]
    public void LengthExtraBits_AllGroupsAtBothEnds()
    {
        for (var symbol = 257; symbol <= 285; symbol++)
        {
            var extraBits = symbol is <= 264 or 285 ? 0 : (symbol - 265) / 4 + 1;
            var baseLength = symbol <= 264 ? symbol - 254 : symbol == 285 ? 258
                : (1 << (extraBits + 2)) + 3 + ((symbol - 265) % 4 << extraBits);
            foreach (var extra in new[] { 0, symbol == 284 ? 30 : (1 << extraBits) - 1 })
            {
                var length = baseLength + extra;
                var prefix = 5 - length % 5;
                var height = (prefix + length) / 5;
                var bits = new Bits();
                bits.StoredBlock(false, new byte[prefix]);
                bits.FixedHeader(true);
                bits.FixedSymbol(symbol);
                bits.Write(extra, extraBits);
                bits.Code(0, 5); // Overlapping distance one.
                bits.FixedSymbol(256);
                Assert.Equal(Hash(new byte[height * 4]), Check(Member(bits, new byte[height * 5]), 1, height));
            }
        }
    }

    [Theory]
    [InlineData(284, "7801636018F9000001040001", false)]
    [InlineData(285, "7801636018050001040001", true)]
    public void Length258_RequiresSymbol285EvenWithMatchingOutputAndAdler(int symbol, string hex, bool valid)
    {
        var member = Convert.FromHexString(hex);
        var bits = new Bits();
        bits.FixedHeader(true);
        bits.FixedSymbol(0);
        bits.FixedSymbol(0);
        bits.FixedSymbol(symbol);
        if (symbol == 284)
            bits.Write(31, 5);
        bits.Code(0, 5); // Distance one; two literals plus a 258-byte match produce 260 zeros.
        bits.FixedSymbol(256);
        Assert.Equal(member, Member(bits, new byte[260]));
        if (valid)
            Assert.Equal(Hash(new byte[208]), Check(member, 1, 52));
        else
            Error(member, 1, 52);
    }

    [Fact]
    public void Distance_CannotExceedHistoryEvenWithinWindow()
    {
        var bits = new Bits();
        bits.FixedHeader(true);
        bits.FixedSymbol(0);
        bits.FixedSymbol(258);
        bits.Code(1, 5); // Distance two, only one produced byte.
        bits.FixedSymbol(256);
        Error(Member(bits, ZeroPixel));
    }

    [Theory]
    [InlineData(286)]
    [InlineData(287)]
    public void FixedReservedLiteralLengthSymbols_AreRejectedOnUse(int symbol)
    {
        var bits = new Bits();
        bits.FixedHeader(true);
        bits.FixedSymbol(symbol);
        bits.FixedSymbol(256);
        Error(Member(bits, ZeroPixel));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    public void ReservedDistanceSymbols_AreRejectedOnUse(int symbol, bool dynamic)
    {
        var bits = new Bits();
        var literals = DenseLiterals();
        var distances = Enumerable.Repeat((byte)5, 32).ToArray();
        if (dynamic)
        {
            DynamicHeader(bits, literals, distances);
            bits.Symbol(0, literals);
            bits.Symbol(258, literals);
            bits.Symbol(symbol, distances);
            bits.Symbol(256, literals);
        }
        else
        {
            bits.FixedHeader(true);
            bits.FixedSymbol(0);
            bits.FixedSymbol(258);
            bits.Code(symbol, 5);
            bits.FixedSymbol(256);
        }
        Error(Member(bits, ZeroPixel));
    }

    [Fact]
    public void Dynamic_SingletonEobAndEmptyDistancesAndSingletonMatchDistanceSucceed()
    {
        var bits = new Bits();
        bits.StoredBlock(false, ZeroPixel);
        var eob = new byte[257];
        eob[256] = 1;
        DynamicHeader(bits, eob, [0]);
        bits.Symbol(256, eob);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));

        var literals = new byte[257];
        literals[0] = literals[256] = 1;
        bits = new Bits();
        DynamicHeader(bits, literals, [0]);
        foreach (var value in ZeroPixel)
            bits.Symbol(value, literals);
        bits.Symbol(256, literals);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));

        var matches = MatchLiterals();
        bits = new Bits();
        DynamicHeader(bits, matches, [1]);
        bits.Symbol(0, matches);
        bits.Symbol(0, matches);
        bits.Symbol(257, matches);
        bits.Code(0, 1);
        bits.Symbol(256, matches);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyDistanceOrUnusedSingletonBranch_CannotExecuteMatch(bool singleton)
    {
        var bits = new Bits();
        var literals = MatchLiterals();
        DynamicHeader(bits, literals, singleton ? [1] : [0]);
        bits.Symbol(0, literals);
        bits.Symbol(0, literals);
        bits.Symbol(257, literals);
        bits.Code(1, 1);
        bits.Symbol(256, literals);
        Error(Member(bits, ZeroPixel));
    }

    [Fact]
    public void SingletonLiteral_UnusedBranchIsInvalidEvenAfterExactOutput()
    {
        var bits = new Bits();
        bits.StoredBlock(false, ZeroPixel);
        var literals = new byte[257];
        literals[256] = 1;
        DynamicHeader(bits, literals, [0]);
        bits.Code(1, 1);
        Error(Member(bits, ZeroPixel));
    }

    [Fact]
    public void CompleteFifteenBitTrees_AreAccepted()
    {
        var literals = new byte[257];
        var distances = new byte[16];
        for (var i = 0; i < 14; i++)
            literals[i] = distances[i] = (byte)(i + 1);
        literals[14] = literals[256] = distances[14] = distances[15] = 15;
        var bits = new Bits();
        DynamicHeader(bits, literals, distances);
        foreach (var value in ZeroPixel)
            bits.Symbol(value, literals);
        bits.Symbol(256, literals);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));
    }

    [Fact]
    public void Dynamic_ActuallyDecodesFifteenBitDistanceAndNonPalindromicExtra()
    {
        var history = new byte[20002];
        history[3326] = 17;
        history[3327] = 34;
        history[3328] = 51;
        var distances = new byte[30];
        for (var i = 0; i < 14; i++)
            distances[i] = (byte)(i + 1);
        distances[28] = distances[29] = 15;
        var bits = new Bits();
        bits.StoredBlock(false, history);
        DynamicHeader(bits, MatchLiterals(), distances);
        bits.Code(0b11, 2); // Literal/length symbol 257: copy three bytes.
        bits.Code(0x7ffe, 15); // Distance symbol 28: fifteen MSB-first bits, ending in zero.
        bits.Write(0x123, 13); // Thirteen LSB-first bits: distance = 16385 + 291 = 16676.
        bits.Code(0b10, 2); // EOB. Source offset is 20002 - 16676 = 3326, not the reversed-extra offset.
        byte[] filtered = [.. history, 17, 34, 51];
        byte[] raw = [.. history[1..], 17, 34, 51];
        Assert.Equal(Hash(raw), Check(Member(bits, filtered), 5001, 1));
    }

    [Theory]
    [InlineData(287)]
    [InlineData(288)]
    public void Dynamic_HlitCannotDeclareReservedSymbols(int count)
    {
        var bits = new Bits();
        DynamicPrefix(bits, count, 1, CodeLengthAlphabet);
        Error(Member(bits, ZeroPixel));
    }

    [Theory]
    [InlineData(0)] // Empty.
    [InlineData(1)] // Single one-bit symbol is not legal for a code-length alphabet.
    [InlineData(2)] // Two two-bit symbols: incomplete.
    [InlineData(3)] // Three one-bit symbols: oversubscribed.
    public void Dynamic_CodeLengthTreeMustBeComplete(int kind)
    {
        var alphabet = new byte[19];
        for (var i = 0; i < kind; i++)
            alphabet[i] = (byte)(kind == 2 ? 2 : 1);
        var bits = new Bits();
        DynamicPrefix(bits, 257, 1, alphabet);
        Error(Member(bits, ZeroPixel));
    }

    [Theory]
    [InlineData("missing-eob")]
    [InlineData("empty-literals")]
    [InlineData("oversubscribed-literals")]
    [InlineData("long-singleton-literals")]
    [InlineData("oversubscribed-distances")]
    [InlineData("incomplete-distances")]
    [InlineData("long-singleton-distances")]
    public void Dynamic_InvalidLiteralAndDistanceTrees(string kind)
    {
        var literals = new byte[257];
        literals[0] = literals[256] = 1;
        byte[] distances = [1];
        switch (kind)
        {
            case "missing-eob": literals[256] = 0; literals[1] = 1; break;
            case "empty-literals": Array.Clear(literals); break;
            case "oversubscribed-literals": literals[1] = 1; break;
            case "long-singleton-literals": literals[0] = 0; literals[256] = 2; break;
            case "oversubscribed-distances": distances = [1, 1, 1]; break;
            case "incomplete-distances": distances = [2, 2]; break;
            case "long-singleton-distances": distances = [2]; break;
        }
        var bits = new Bits();
        DynamicHeader(bits, literals, distances);
        Error(Member(bits, ZeroPixel));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Dynamic_IncompleteLiteralTreeRejectsCompletePayloadWithMatchingAdler(int length, bool valid)
    {
        var literals = new byte[257];
        literals[0] = literals[256] = (byte)length;
        var bits = new Bits();
        DynamicHeader(bits, literals, [0]); // Empty distances remain legal when no match is executed.
        foreach (var value in ZeroPixel)
            bits.Symbol(value, literals);
        bits.Symbol(256, literals);
        var member = Member(bits, ZeroPixel);
        Assert.Equal(Convert.FromHexString("00050001"), member[^4..]);
        if (valid)
            Assert.Equal(Hash(new byte[4]), Check(member));
        else
            Error(member);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    public void Dynamic_RepeatsMayCrossLiteralDistanceBoundary(int repeat)
    {
        var bits = new Bits();
        var literals = new byte[repeat == 16 ? 258 : repeat == 17 ? 258 : 286];
        var distances = new byte[repeat == 18 ? 32 : 4];
        DynamicPrefix(bits, literals.Length, distances.Length, CodeLengthAlphabet);
        if (repeat == 16)
        {
            literals[0] = literals[1] = literals[256] = literals[257] = 2;
            Array.Fill(distances, (byte)2);
            LengthToken(bits, 2);
            LengthToken(bits, 2);
            LengthToken(bits, 18, 138);
            LengthToken(bits, 18, 116);
            LengthToken(bits, 2);
            LengthToken(bits, 16, 5); // LENS[257] and all four DISTS.
        }
        else
        {
            literals[0] = literals[256] = 1;
            LengthToken(bits, 1);
            LengthToken(bits, 18, 138);
            LengthToken(bits, 18, 117);
            LengthToken(bits, 1);
            LengthToken(bits, repeat, literals.Length + distances.Length - 257);
        }
        foreach (var value in ZeroPixel)
            bits.Symbol(value, literals);
        bits.Symbol(256, literals);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    public void Dynamic_RepeatRequiresPreviousAndCannotOverrunCombinedCounts(int repeat, bool overrun)
    {
        var bits = new Bits();
        DynamicPrefix(bits, 257, 1, CodeLengthAlphabet);
        if (overrun)
            for (var i = 0; i < 257; i++)
                LengthToken(bits, 0);
        LengthToken(bits, repeat, repeat == 18 ? 11 : 3);
        Error(Member(bits, ZeroPixel));
    }

    [Fact]
    public void Dynamic_Repeat16MayRepeatAZeroLength()
    {
        var literals = new byte[257];
        literals[0] = literals[256] = 1;
        var bits = new Bits();
        DynamicPrefix(bits, 257, 1, CodeLengthAlphabet);
        LengthToken(bits, 1);
        LengthToken(bits, 0);
        LengthToken(bits, 16, 6);
        LengthToken(bits, 18, 138);
        LengthToken(bits, 18, 110);
        LengthToken(bits, 1);
        LengthToken(bits, 0);
        foreach (var value in ZeroPixel)
            bits.Symbol(value, literals);
        bits.Symbol(256, literals);
        Assert.Equal(Hash(new byte[4]), Check(Member(bits, ZeroPixel)));
    }

    [Fact]
    public void ManyAlternatingDynamicAndFixedTrees_DoNotRetainOldCodesOrAllocatePerBlock()
    {
        var bits = new Bits();
        var singleton = new byte[257];
        singleton[256] = 1;
        for (var i = 0; i < 256; i++)
        {
            DynamicHeader(bits, singleton, [0], final: false);
            bits.Symbol(256, singleton);
            bits.FixedHeader(false);
            bits.FixedSymbol(256);
        }
        bits.StoredBlock(true, ZeroPixel);
        var png = Png(1, 1, Member(bits, ZeroPixel));
        Validate(png, 1, 1, default);
        Check(RedMember);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var hash = Verify(png, 1, 1, NoBudget);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(Hash(new byte[4]), hash);
        Assert.InRange(allocated, 0, 48 * 1024L);
    }

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public void BlockCap_IsInclusiveAndCountsEmptyBlocks(int count, bool valid)
    {
        var bits = new Bits();
        for (var i = 1; i < count; i++)
            bits.StoredBlock(false, []);
        bits.StoredBlock(true, ZeroPixel);
        var member = Member(bits, ZeroPixel);
        if (valid)
            Assert.Equal(Hash(new byte[4]), Check(member));
        else
            Error(member, code: EngineErrors.ResourceLimit);
    }

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(1L, -1L)]
    [InlineData(8193L, 1L)]
    [InlineData(1L, 8193L)]
    [InlineData(8192L, 2049L)]
    [InlineData(long.MaxValue, long.MaxValue)]
    [InlineData(long.MinValue, 1L)]
    public void Dimensions_AreBoundedBeforeArrayAllocationOrOverflow(long width, long height)
    {
        var png = Png(1, 1, RedMember);
        var error = Assert.Throws<DocumentEngineException>(() => Verify(png, width, height, NoBudget));
        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
    }

    [Theory]
    [InlineData(8192, 16)] // Maximum sample-row storage.
    [InlineData(2048, 8192)] // Exact maximum 67,117,056 filtered bytes, not 64 MiB.
    public void StreamingAllocation_IsIndependentOfImageArea(int width, int height)
    {
        // Trusted test-only compressor and hash both receive rows; the fixture needs no full-image buffer.
        var row = new byte[4 * width + 1];
        using var encoded = new MemoryStream();
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var zlib = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
                expected.AppendData(row.AsSpan(1));
            }
        var expectedHash = Convert.ToHexStringLower(expected.GetHashAndReset());
        var png = Png(width, height, encoded.ToArray());
        Validate(png, width, height, default);
        Check(RedMember);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var hash = Verify(png, width, height, NoBudget);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expectedHash, hash);
        Assert.InRange(allocated, 0, 128 * 1024L);
    }

    [Fact]
    public void Budget_PropagatesCancellationBeforeAnyWorkIncludingInvalidInput()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        foreach (var png in new[] { Array.Empty<byte>(), Png(1, 1, RedMember) })
        {
            var error = Assert.Throws<OperationCanceledException>(() => Verify(png, long.MaxValue, 1, cts.Token.ThrowIfCancellationRequested));
            Assert.Equal(cts.Token, error.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Budget_InterruptsEmptyIdatTraversalBeforeNextByte(bool afterHeader)
    {
        // IHDR, sRGB, IEND and 4093 IDATs reach the unchanged 4096-chunk structural limit.
        var payloads = Enumerable.Repeat(Array.Empty<byte>(), 4093).ToArray();
        var dataIndex = afterHeader ? 0 : payloads.Length - 1;
        payloads[dataIndex] = afterHeader ? RedMember[..2] : [0];
        var png = Png(1, 1, payloads);
        ErrorPng(png); // Without cancellation this member must fail, not accidentally finish a row.
        var calls = 0;
        var stopAt = afterHeader ? 3 : 2; // Entry, optional first-block check, then chunk traversal.
        var cancellation = new OperationCanceledException();
        var error = Assert.Throws<OperationCanceledException>(() => Verify(png, 1, 1, () =>
        {
            if (++calls == stopAt)
                throw cancellation;
        }));
        Assert.Same(cancellation, error);
        Assert.Equal(stopAt, calls);
        payloads[dataIndex] = RedMember;
        Assert.Equal(Hash([255, 0, 0, 255]), CheckPng(Png(1, 1, payloads), 1, 1));
    }

    [Fact]
    public void Budget_ChecksEveryEmptyBlock()
    {
        var bits = new Bits();
        for (var i = 0; i < 10; i++)
        {
            bits.FixedHeader(false);
            bits.FixedSymbol(256);
        }
        bits.StoredBlock(true, ZeroPixel);
        var png = Png(1, 1, Member(bits, ZeroPixel));
        Validate(png, 1, 1, default);
        var calls = 0;
        var cancellation = new OperationCanceledException();
        var error = Assert.Throws<OperationCanceledException>(() => Verify(png, 1, 1, () =>
        {
            if (++calls == 12) // Entry and every block, before any row work.
                throw cancellation;
        }));
        Assert.Same(cancellation, error);
        Assert.Equal(12, calls);
    }

    [Theory]
    [InlineData("stored")]
    [InlineData("fixed-symbols")]
    [InlineData("dynamic-symbols")]
    [InlineData("match-bytes")]
    public void Budget_InterruptsAt1024SymbolsOrBytesBeforeMalformedRemainder(string kind)
    {
        var bits = new Bits();
        byte[] output;
        if (kind == "stored")
        {
            output = new byte[1024];
            bits.StoredBlock(true, output);
        }
        else if (kind == "dynamic-symbols")
        {
            var literals = DenseLiterals();
            DynamicHeader(bits, literals, Enumerable.Repeat((byte)5, 32).ToArray()); // 318 decoded length symbols.
            output = new byte[706];
            foreach (var value in output)
                bits.Symbol(value, literals);
            bits.Symbol(256, literals);
        }
        else
        {
            bits.FixedHeader(true);
            if (kind == "match-bytes")
            {
                output = new byte[1033];
                bits.FixedSymbol(0);
                for (var i = 0; i < 4; i++)
                {
                    bits.FixedSymbol(285);
                    bits.Code(0, 5);
                }
            }
            else
            {
                output = new byte[1024];
                foreach (var value in output)
                    bits.FixedSymbol(value);
            }
            bits.FixedSymbol(286);
        }
        var png = Png(8192, 1, Member(bits, output));
        Validate(png, 8192, 1, default);
        var calls = 0;
        var cancellation = new OperationCanceledException();
        var actual = Assert.Throws<OperationCanceledException>(() => Verify(png, 8192, 1, () =>
        {
            if (++calls == 3) // Entry, block, then threshold; no completed row yet.
                throw cancellation;
        }));
        Assert.Same(cancellation, actual);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(2)] // Block boundary.
    [InlineData(3)] // Row reconstruction/hash entry.
    [InlineData(4)] // Completed row hashing.
    [InlineData(5)] // Complete member, before returning a digest.
    public void Budget_PropagatesDeadlineExceptionAtBlockAndRowHashBoundaries(int stopAt)
    {
        var png = Png(1, 1, RedMember);
        var calls = 0;
        var deadline = new DocumentEngineException(EngineErrors.ResourceLimit, "Test deadline.");
        var error = Assert.Throws<DocumentEngineException>(() => Verify(png, 1, 1, () =>
        {
            if (++calls == stopAt)
                throw deadline;
        }));
        Assert.Same(deadline, error);
        Assert.Equal(stopAt, calls);
        Assert.Equal(Hash([255, 0, 0, 255]), CheckPng(png, 1, 1));
    }

    private static string Hash(byte[] raw) => Convert.ToHexStringLower(SHA256.HashData(raw));
    private static string Check(byte[] member, int width = 1, int height = 1) => CheckPng(Png(width, height, member), width, height);

    private static string CheckPng(byte[] png, int width, int height)
    {
        Validate(png, width, height, default);
        return Verify(png, width, height, NoBudget);
    }

    private static void Error(byte[] member, int width = 1, int height = 1, string code = Invalid) =>
        ErrorPng(Png(width, height, member), width, height, code);

    private static void ErrorPng(byte[] png, int width = 1, int height = 1, string code = Invalid)
    {
        Validate(png, width, height, default); // Bad IDAT is deliberately opaque to unchanged structural preflight.
        var error = Assert.Throws<DocumentEngineException>(() => Verify(png, width, height, NoBudget));
        Assert.Equal(code, error.Code);
        Assert.Equal(code == Invalid ? "Invalid PNG compressed samples." : "PNG verification resource limit exceeded.", error.Message);
    }

    private static byte[] Stored(byte[] filtered)
    {
        var bits = new Bits();
        bits.StoredBlock(true, filtered);
        return Member(bits, filtered);
    }

    private static byte[] Fixed(byte[] filtered)
    {
        var bits = new Bits();
        bits.FixedHeader(true);
        foreach (var value in filtered)
            bits.FixedSymbol(value);
        bits.FixedSymbol(256);
        return Member(bits, filtered);
    }

    private static byte[] DenseLiterals() => Enumerable.Range(0, 286).Select(i => (byte)(i < 226 ? 8 : 9)).ToArray();

    private static byte[] MatchLiterals()
    {
        var lengths = new byte[258];
        lengths[0] = 1;
        lengths[256] = lengths[257] = 2;
        return lengths;
    }

    private static byte[] Dynamic(byte[] filtered)
    {
        var bits = new Bits();
        var literals = DenseLiterals();
        DynamicHeader(bits, literals, Enumerable.Repeat((byte)5, 32).ToArray());
        foreach (var value in filtered)
            bits.Symbol(value, literals);
        bits.Symbol(256, literals);
        return Member(bits, filtered);
    }

    private static void DynamicHeader(Bits bits, byte[] literals, byte[] distances, bool final = true)
    {
        DynamicPrefix(bits, literals.Length, distances.Length, CodeLengthAlphabet, final);
        foreach (var length in literals.Concat(distances))
            LengthToken(bits, length);
    }

    private static void DynamicPrefix(Bits bits, int literalCount, int distanceCount, byte[] alphabet, bool final = true)
    {
        bits.Write(final ? 1 : 0, 1);
        bits.Write(2, 2);
        bits.Write(literalCount - 257, 5);
        bits.Write(distanceCount - 1, 5);
        bits.Write(15, 4); // HCLEN=19; no dependence on the decoder's Huffman construction.
        foreach (var symbol in CodeLengthOrder)
            bits.Write(alphabet[symbol], 3);
    }

    private static void LengthToken(Bits bits, int symbol, int repeat = 0)
    {
        bits.Symbol(symbol, CodeLengthAlphabet);
        if (symbol == 16)
            bits.Write(repeat - 3, 2);
        else if (symbol == 17)
            bits.Write(repeat - 3, 3);
        else if (symbol == 18)
            bits.Write(repeat - 11, 7);
    }

    private static byte[] Compress(byte[] filtered)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(filtered);
        return output.ToArray();
    }

    private static byte HeaderFlags(byte cmf, byte flags) => (byte)(flags + (31 - (((cmf << 8) | flags) % 31)) % 31);

    private static byte[] Member(Bits bits, byte[] filtered, byte cmf = 0x78)
    {
        // Independent weighted-sum definition of Adler, not the production incremental implementation.
        ulong a = 1;
        ulong b = (ulong)filtered.Length;
        for (var i = 0; i < filtered.Length; i++)
        {
            a += filtered[i];
            b += (ulong)(filtered.Length - i) * filtered[i];
        }
        var trailer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, (uint)(((b % 65521) << 16) | (a % 65521)));
        return [cmf, HeaderFlags(cmf, 0), .. bits.Finish(), .. trailer];
    }

    private static byte[] Png(int width, int height, params byte[][] payloads)
    {
        using var output = new MemoryStream();
        output.Write(Convert.FromHexString("89504E470D0A1A0A"));
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        Chunk(output, "sRGB", [0]); // Reader must skip validated non-IDAT before the first payload.
        foreach (var payload in payloads)
            Chunk(output, "IDAT", payload);
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] payload)
    {
        var bytes = new byte[payload.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)payload.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        payload.CopyTo(bytes, 8);
        uint crc = 0xffffffff;
        foreach (var value in bytes.AsSpan(4, payload.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(payload.Length + 8), ~crc);
        output.Write(bytes);
    }

    // Original test-only RFC bit writer. Symbol encoding sorts (length, symbol) directly;
    // it shares neither the production count/first decoder nor its filter/history algorithms.
    private sealed class Bits
    {
        private readonly List<byte> _bytes = [];
        private int _position;

        internal void Write(int value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if ((_position & 7) == 0)
                    _bytes.Add(0);
                if (((value >> i) & 1) != 0)
                    _bytes[^1] |= (byte)(1 << (_position & 7));
                _position++;
            }
        }

        internal void Code(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
                Write((value >> i) & 1, 1);
        }

        internal void Symbol(int symbol, byte[] lengths)
        {
            var ordered = Enumerable.Range(0, lengths.Length).Where(i => lengths[i] != 0)
                .OrderBy(i => lengths[i]).ThenBy(i => i);
            var code = 0;
            var previousLength = 0;
            foreach (var candidate in ordered)
            {
                code <<= lengths[candidate] - previousLength;
                if (candidate == symbol)
                {
                    Code(code, lengths[candidate]);
                    return;
                }
                code++;
                previousLength = lengths[candidate];
            }
            throw new InvalidOperationException("Test symbol is absent from its alphabet.");
        }

        internal void FixedHeader(bool final)
        {
            Write(final ? 1 : 0, 1);
            Write(1, 2);
        }

        internal void FixedSymbol(int symbol)
        {
            if (symbol <= 143) Code(0x30 + symbol, 8);
            else if (symbol <= 255) Code(0x190 + symbol - 144, 9);
            else if (symbol <= 279) Code(symbol - 256, 7);
            else Code(0xc0 + symbol - 280, 8);
        }

        internal void StoredBlock(bool final, byte[] filtered)
        {
            Assert.InRange(filtered.Length, 0, 65535);
            Write(final ? 1 : 0, 1);
            Write(0, 2);
            Align();
            Write(filtered.Length, 16);
            Write(filtered.Length ^ 65535, 16);
            foreach (var value in filtered)
                Write(value, 8);
        }

        internal void Align(bool ones = false)
        {
            while ((_position & 7) != 0)
                Write(ones ? 1 : 0, 1);
        }

        internal byte[] Finish()
        {
            Align();
            return _bytes.ToArray();
        }
    }
}
