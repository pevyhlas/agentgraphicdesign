using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

public sealed class NativePixelFormatTests
{
    private const string AssetId = "11111111-1111-4111-8111-111111111111";
    private const string LayerId = "22222222-2222-4222-8222-222222222222";
    private const string TransparentHash = "dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b";
    private const string HiddenHash = "b6a504dc8edff031f684f4a317efd10f6626ec3d9802c0291e12711c1a7677cf";
    private const string FullMaskHash = "acaa4a126cbcef28d29bf07df0b4ca22fe3561515f79145914717a7d58bb5a77";
    private const string SourceHash = "5183bc41cda41700726ef8584f6be9977b2f59a1602e101ef9e20554b46a6b91";
    private const string Canonical = """
        {"schema":"agd.native-pixels","version":1,"profile":"agd.pixels.rgba8-linear-over.v1","assetId":"11111111-1111-4111-8111-111111111111","revn":1,"name":"Pixel","canvas":{"width":1,"height":1},"layers":[{"id":"22222222-2222-4222-8222-222222222222","name":"Layer 1","visible":true,"opacity":1,"pixelsSha256":"dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b","maskSha256":null}]}
        """;
    private const string CanonicalSource = """
        {"schema":"agd.native-pixels","version":1,"profile":"agd.pixels.rgba8-linear-over.v1","assetId":"11111111-1111-4111-8111-111111111111","name":"Pixel","canvas":{"width":1,"height":1},"layers":[{"id":"22222222-2222-4222-8222-222222222222","name":"Layer 1","visible":true,"opacity":1,"pixelsSha256":"dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b","maskSha256":null}]}
        """;
    private const string SnapshotJson = """
        {"assetId":"11111111-1111-4111-8111-111111111111","revn":1,"sourceDigest":"5183bc41cda41700726ef8584f6be9977b2f59a1602e101ef9e20554b46a6b91"}
        """;
    private static readonly JsonSerializerOptions FactsOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void LiteralCanonicalMetadataAndSourceMatchIndependentContractBytes()
    {
        var document = NativePixelFormat.Parse(Utf8(Canonical));
        Assert.Equal(392, Utf8(Canonical).Length);
        Assert.Equal(383, Utf8(CanonicalSource).Length);
        Assert.Equal(Utf8(Canonical), NativePixelFormat.Serialize(document));
        Assert.Equal(Utf8(CanonicalSource), NativePixelFormat.Serialize(document, source: true));
        Assert.Equal(SourceHash, NativePixelFormat.SourceDigest(document));
        Assert.Equal(SourceHash, Convert.ToHexStringLower(SHA256.HashData(Utf8("agd.native-pixels.source.v1\n" + CanonicalSource))));
    }

    [Theory]
    [InlineData("opacity", "dc5a3216fb4da08ac9e9a5a6203cb2055b368eb978c4c9f34e3bf5b2fbd8b0cd")]
    [InlineData("mask", "e77ebd1bf294c40179233e2a82fe196a85eea3bf530185e20db8c0f4c5195754")]
    [InlineData("hidden", "80de8ffac45996eeedbc18e0f4ddfaa5b4d4f97c4193232889d0e5e14783a78c")]
    public void RenderNeutralChangesMatchIndependentSourceHashVectors(string change, string expected)
    {
        var document = BaseDocument();
        var layer = document.Layers[0];
        layer = change switch
        {
            "opacity" => layer with { Opacity = 0.5000000m },
            "mask" => layer with { MaskSha256 = FullMaskHash },
            _ => layer with { PixelsSha256 = HiddenHash }
        };
        Assert.Equal(expected, NativePixelFormat.SourceDigest(document with { Layers = [layer] }));
    }

    [Theory]
    [InlineData(2L)]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void RevisionRemainsExactButIsOmittedOnlyFromSource(long revision)
    {
        string input = Canonical.Replace("\"revn\":1", "\"revn\":" + revision.ToString(CultureInfo.InvariantCulture));
        var document = NativePixelFormat.Parse(Utf8(input));
        Assert.Equal(revision, document.Revn);
        Assert.Equal(Utf8(input), NativePixelFormat.Serialize(document));
        Assert.Equal(Utf8(CanonicalSource), NativePixelFormat.Serialize(document, true));
        Assert.Equal(SourceHash, NativePixelFormat.SourceDigest(document));
    }

    [Fact]
    public void CanonicalizationIgnoresInputOrderWhitespaceEscapesAndPermittedScale()
    {
        const string input = """
            {
              "layers": [{"maskSha256":null,"pixelsSha256":"dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b","opacity":1.000000,"visible":true,"name":"Layer\u00201","id":"22222222-2222-4222-8222-222222222222"}],
              "canvas":{"height":1,"width":1},"name":"P\u0069xel","revn":1,
              "assetId":"11111111-1111-4111-8111-111111111111",
              "profile":"agd.pixels.rgba8-linear-over.v1","version":1,"schema":"agd.native-pixels"
            }
            """;
        var document = NativePixelFormat.Parse(Utf8(input));
        Assert.Equal(Utf8(Canonical), NativePixelFormat.Serialize(document));
        Assert.Equal(SourceHash, NativePixelFormat.SourceDigest(document));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void CanonicalBytesDoNotDependOnCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var document = BaseDocument();
            document = document with { Layers = [document.Layers[0] with { Opacity = 0.5m }] };
            Assert.Equal(Utf8(Canonical.Replace("\"opacity\":1", "\"opacity\":0.5")), NativePixelFormat.Serialize(document));
            Assert.Equal("dc5a3216fb4da08ac9e9a5a6203cb2055b368eb978c4c9f34e3bf5b2fbd8b0cd", NativePixelFormat.SourceDigest(document));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("\\u00e9", "22c3a922")]
    [InlineData("e\\u0301", "2265cc8122")]
    [InlineData("\\u002f", "222f22")]
    [InlineData("a\\\"b\\\\c/d", "22615c22625c5c632f6422")]
    [InlineData("\\ud83d\\udc69\\u200d\\ud83d\\udcbb", "22f09f91a9e2808df09f92bb22")]
    [InlineData("<>&+", "223c3e262b22")]
    [InlineData("\\u200d", "22e2808d22")]
    public void CanonicalNamesUseLiteralShortestUtf8AndOnlyRequiredEscapes(string input, string expectedHex)
    {
        var document = NativePixelFormat.Parse(Utf8(WithName(input)));
        string expectedNameToken = Encoding.UTF8.GetString(Convert.FromHexString(expectedHex));
        Assert.Equal(Utf8(Canonical.Replace("\"Pixel\"", expectedNameToken)), NativePixelFormat.Serialize(document));
    }

    [Fact]
    public void UnicodeIsNeitherNormalizedNorTrimmed()
    {
        var precomposed = NativePixelFormat.Parse(Utf8(WithName("\\u00e9")));
        var decomposed = NativePixelFormat.Parse(Utf8(WithName("e\\u0301")));
        Assert.NotEqual(NativePixelFormat.SourceDigest(precomposed), NativePixelFormat.SourceDigest(decomposed));
        var spaces = BaseDocument() with { Name = "  Pixel  " };
        Assert.Equal("  Pixel  ", NativePixelFormat.Parse(NativePixelFormat.Serialize(spaces)).Name);
    }

    public static IEnumerable<object[]> ProhibitedNameScalars()
    {
        foreach (int value in Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33))
                     .Concat(Enumerable.Range(0x2028, 7)).Concat(Enumerable.Range(0x2066, 4)).Append(0xfeff))
            yield return [value];
    }

    [Theory]
    [MemberData(nameof(ProhibitedNameScalars))]
    public void EveryExplicitlyProhibitedNameScalarIsRejectedEvenEscaped(int scalar)
    {
        string escaped = "A\\u" + scalar.ToString("x4", CultureInfo.InvariantCulture);
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Parse(Utf8(WithName(escaped))));
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.ValidateDocument(BaseDocument() with { Name = "A" + (char)scalar }));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\\u00a0")]
    [InlineData("\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u202f\\u205f\\u3000")]
    [InlineData("")]
    public void ExplicitWhitespaceOnlyAndEmptyNamesAreInvalid(string name)
        => AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Parse(Utf8(WithName(name))));

    [Theory]
    [InlineData("\\ud800")]
    [InlineData("\\udc00")]
    [InlineData("\\ud800x")]
    [InlineData("\\ud800\\u0041")]
    [InlineData("\\ud800\\ud800")]
    public void InvalidSurrogateEscapesAreRejectedWithoutReplacement(string name)
        => AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(WithName(name))));

    [Fact]
    public void ScalarUtf8AndRawTokenBoundsAreInclusive()
    {
        string escaped = string.Concat(Enumerable.Repeat("\\ud83d\\udc69", 128));
        Assert.Equal(1536, escaped.Length);
        var document = NativePixelFormat.Parse(Utf8(WithName(escaped)));
        Assert.Equal(512, Encoding.UTF8.GetByteCount(document.Name));
        Assert.Equal(128, document.Name.EnumerateRunes().Count());
        NativePixelFormat.ValidateDocument(document);
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.ValidateDocument(document with { Name = document.Name + "a" }));
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.ValidateDocument(document with { Name = new string('a', 129) }));
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.ValidateDocument(document with { Name = "x\ud800" }));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.Parse(Utf8(WithName(new string('a', 1537)))));
    }

    [Fact]
    public void InvalidUtf8BomCommentsTrailingCommasAndAdditionalDocumentsFailClosed()
    {
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse((byte[])[0xef, 0xbb, 0xbf, .. Utf8(Canonical)]));
        byte[] valid = Utf8(Canonical);
        int start = Canonical.IndexOf("Pixel", StringComparison.Ordinal);
        foreach (byte[] invalid in new byte[][] { [0xc0, 0xaf], [0xed, 0xa0, 0x80], [0xf4, 0x90, 0x80, 0x80], [0xff], [0x80] })
            AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse((byte[])[.. valid.AsSpan(0, start), .. invalid, .. valid.AsSpan(start + 5)]));
        foreach (string suffix in new[] { "{}", "/*comment*/", "," })
            AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical + suffix)));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"width\":1,", "\"width\":1,/*x*/"))));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"height\":1}", "\"height\":1,}"))));
    }

    [Theory]
    [InlineData("\"schema\":", "\"extra\":0,\"schema\":")]
    [InlineData("\"schema\":", "\"schema\":\"agd.native-pixels\",\"schema\":")]
    [InlineData("\"name\":\"Pixel\"", "\"name\":\"Pixel\",\"na\\u006de\":\"Pixel\"")]
    [InlineData("\"width\":1", "\"width\":1,\"w\\u0069dth\":1")]
    [InlineData("\"opacity\":1", "\"opacity\":1,\"opac\\u0069ty\":1")]
    [InlineData("\"canvas\":", "\"unknownCanvas\":")]
    [InlineData(",\"maskSha256\":null", "")]
    [InlineData("\"visible\":true", "\"visible\":null")]
    [InlineData("\"visible\":true", "\"visible\":1")]
    [InlineData("\"name\":\"Pixel\"", "\"name\":null")]
    [InlineData("\"maskSha256\":null", "\"maskSha256\":false")]
    [InlineData("\"revn\":1", "\"revn\":\"1\"")]
    [InlineData("\"opacity\":1", "\"opacity\":\"0.5\"")]
    [InlineData("\"width\":1", "\"width\":\"1\"")]
    [InlineData("\"assetId\":\"11111111-1111-4111-8111-111111111111\"", "\"assetId\":1")]
    public void MetadataObjectsAreClosedRequiredAndUncoerced(string from, string to)
        => AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(from, to))));

    [Theory]
    [InlineData("-0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("9223372036854775808")]
    [InlineData("0")]
    public void SourceRevisionRejectsNonpositiveLossyOrNonintegerLexemes(string token)
        => AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"revn\":1", "\"revn\":" + token))));

    [Theory]
    [InlineData("-0")]
    [InlineData("-0.000000")]
    [InlineData("5e-1")]
    [InlineData("0.0000001")]
    [InlineData("0.5000000")]
    [InlineData("1.000001")]
    [InlineData("2")]
    public void OpacityLexicalGrammarIsStricterThanTypedDecimalScale(string token)
        => AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"opacity\":1", "\"opacity\":" + token))));

    [Theory]
    [InlineData("0", "0", 0)]
    [InlineData("0.000001", "0.000001", 1)]
    [InlineData("0.500000", "0.5", 500000)]
    [InlineData("1.000000", "1", 1000000)]
    [InlineData("0.0", "0", 0)]
    public void ExactOpacityEndpointsAndCanonicalMillionths(string token, string canonical, int millionths)
    {
        var document = NativePixelFormat.Parse(Utf8(Canonical.Replace("\"opacity\":1", "\"opacity\":" + token)));
        Assert.Equal(millionths, NativePixelFormat.OpacityMillionths(document.Layers[0].Opacity));
        Assert.Equal(Utf8(Canonical.Replace("\"opacity\":1", "\"opacity\":" + canonical)), NativePixelFormat.Serialize(document));
    }

    [Fact]
    public void ProgrammaticOpacityIsJudgedByValueAndInvalidModelsHaveNoCanonicalBytes()
    {
        Assert.Equal(500000, NativePixelFormat.OpacityMillionths(0.5000000000000000000000000000m));
        Assert.Equal(0, NativePixelFormat.OpacityMillionths(new decimal(0, 0, 0, true, 28)));
        var zero = BaseDocument();
        zero = zero with { Layers = [zero.Layers[0] with { Opacity = new decimal(0, 0, 0, true, 28) }] };
        Assert.Equal(Utf8(Canonical.Replace("\"opacity\":1", "\"opacity\":0")), NativePixelFormat.Serialize(zero));
        foreach (decimal opacity in new[] { -0.1m, 1.1m, 0.0000001m, decimal.MaxValue })
        {
            var document = BaseDocument();
            document = document with { Layers = [document.Layers[0] with { Opacity = opacity }] };
            AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Serialize(document));
            AssertCode("INVALID_PROPERTY", () => NativePixelFormat.SourceDigest(document));
        }
    }

    [Fact]
    public void IdsHashesAndVersionProfilesRemainExact()
    {
        foreach (string id in new[] { "00000000-0000-0000-0000-000000000000", "A1111111-1111-4111-8111-111111111111", "{" + AssetId + "}", AssetId.Replace("-", "") })
            AssertCode("INVALID_ID", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(AssetId, id))));
        AssertCode("INVALID_ID", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(LayerId, AssetId))));
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(TransparentHash, TransparentHash.ToUpperInvariant()))));
        AssertCode("INVALID_PROPERTY", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(TransparentHash, TransparentHash[..63]))));
        AssertCode("UNSUPPORTED_VERSION", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"version\":1", "\"version\":2"))));
        AssertCode("UNSUPPORTED_VERSION", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("agd.native-pixels", "other"))));
        AssertCode("PIXEL_PROFILE_UNSUPPORTED", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("agd.pixels.rgba8-linear-over.v1", "other"))));
        var arbitraryGuid = "12345678-1234-0000-0000-123456789abc";
        Assert.Equal(Guid.Parse(arbitraryGuid), NativePixelFormat.Parse(Utf8(Canonical.Replace(AssetId, arbitraryGuid))).AssetId);
    }

    [Fact]
    public void SourceIdentityIncludesAllSemanticMetadataAndLayerOrder()
    {
        var document = BaseDocument();
        var layer = document.Layers[0];
        foreach (var changed in new[]
        {
            document with { AssetId = Guid.Parse("33333333-3333-4333-8333-333333333333") },
            document with { Name = "Other" },
            document with { Width = 2 },
            document with { Height = 2 },
            document with { Layers = [layer with { Id = Guid.Parse("33333333-3333-4333-8333-333333333333") }] },
            document with { Layers = [layer with { Name = "Other" }] },
            document with { Layers = [layer with { Visible = false }] },
            document with { Layers = [layer with { Opacity = 0 }] },
            document with { Layers = [layer with { PixelsSha256 = HiddenHash }] },
            document with { Layers = [layer with { MaskSha256 = FullMaskHash }] }
        }) Assert.NotEqual(SourceHash, NativePixelFormat.SourceDigest(changed));
        var second = layer with { Id = Guid.Parse("33333333-3333-4333-8333-333333333333") };
        Assert.NotEqual(NativePixelFormat.SourceDigest(document with { Layers = [layer, second] }),
            NativePixelFormat.SourceDigest(document with { Layers = [second, layer] }));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"canvas\":", "\"sourceDigest\":\"" + SourceHash + "\",\"canvas\":"))));
    }

    [Fact]
    public void EveryLogicalSurfaceCountsEvenWhenHiddenAndHashDeduplicated()
    {
        var document = WithLayers(16, false) with { Width = 1024, Height = 1024 };
        NativePixelFormat.ValidateDocument(document);
        NativePixelFormat.ValidateDocument(WithLayers(3, true) with { Width = 2048, Height = 2048 });
        NativePixelFormat.ValidateDocument(WithLayers(4, false) with { Width = 2048, Height = 2048 });
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.ValidateDocument(WithLayers(4, true) with { Width = 2048, Height = 2048 }));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.ValidateDocument(document with
        {
            Layers = document.Layers.SetItem(0, document.Layers[0] with { MaskSha256 = FullMaskHash })
        }));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.ValidateDocument(WithLayers(17, false)));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.ValidateDocument(document with { Layers = [] }));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.ValidateDocument(document with { Layers = default }));
        AssertCode("INVALID_ID", () => NativePixelFormat.ValidateDocument(document with { Layers = [document.Layers[0], document.Layers[0]] }));
    }

    [Theory]
    [InlineData(8192, 512)]
    [InlineData(512, 8192)]
    [InlineData(37, 23)]
    [InlineData(32, 32)]
    [InlineData(64, 64)]
    [InlineData(1, 8192)]
    [InlineData(8192, 1)]
    public void ArbitraryCanvasAndInclusiveAxisAreaBounds(int width, int height)
        => NativePixelFormat.ValidateDocument(BaseDocument() with { Width = width, Height = height });

    [Theory]
    [InlineData(0, 1, "INVALID_DIMENSIONS")]
    [InlineData(1, -1, "INVALID_DIMENSIONS")]
    [InlineData(8193, 1, "RESOURCE_LIMIT")]
    [InlineData(8192, 513, "RESOURCE_LIMIT")]
    [InlineData(int.MaxValue, int.MaxValue, "RESOURCE_LIMIT")]
    public void InvalidCanvasCannotOverflowOrAllocate(int width, int height, string code)
        => AssertCode(code, () => NativePixelFormat.Serialize(BaseDocument() with { Width = width, Height = height }));

    [Fact]
    public void ContainerFieldsRejectMismatchedJsonTypes()
    {
        var layers = Canonical[(Canonical.IndexOf("\"layers\":", StringComparison.Ordinal) + 9)..^1];
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(layers, "5"))));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace(layers, "{\"0\":1}"))));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("{\"width\":1,\"height\":1}", "5"))));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("{\"width\":1,\"height\":1}", "[1,1]"))));
    }

    [Fact]
    public void PayloadFramingRejectsOverLimitDimensions()
    {
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.FramePayload(1, 8193, 1, new byte[4]));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.FramePayload(1, 8192, 513, new byte[4]));
        var frame = NativePixelFormat.FramePayload(1, 1, 1, new byte[4]);
        var hash = Convert.ToHexStringLower(SHA256.HashData(frame));
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload(frame, hash, 1, 8193, 1));
    }

    [Fact]
    public void SerializeAndSourceDigestPropagateTheOptionalBudget()
    {
        var document = BaseDocument();
        var cancellation = new OperationCanceledException();
        Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(
            () => NativePixelFormat.Serialize(document, source: false, () => throw cancellation)));
        Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(
            () => NativePixelFormat.SourceDigest(document, () => throw cancellation)));
    }

    [Fact]
    public void WireParsersCheckTheOptionalBudgetAndPropagateCancellation()
    {
        var operations = string.Join(",", Enumerable.Repeat("{\"op\":\"addLayer\",\"name\":\"valid\"}", 4));
        var edit = Utf8(EditJson(operations));
        var editChecks = 0;
        NativePixelWire.ParseEdit(edit, "/workspace/asset", "/workspace", "/workspace", () => editChecks++);
        Assert.True(editChecks >= 4);
        var cancellation = new OperationCanceledException();
        var attempted = 0;
        Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(() =>
            NativePixelWire.ParseEdit(edit, "/workspace/asset", "/workspace", "/workspace", () =>
            {
                if (++attempted == 3) throw cancellation;
            })));
        var snapshot = Utf8(SnapshotJson);
        var snapshotChecks = 0;
        NativePixelWire.ParseSnapshot(snapshot, () => snapshotChecks++);
        Assert.True(snapshotChecks >= 1);
        Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(
            () => NativePixelWire.ParseSnapshot(snapshot, () => throw cancellation)));
    }

    [Fact]
    public void InputNumberAndDepthCapsAreEnforcedBeforeModelConstruction()
    {
        var exact = Utf8(Canonical + new string(' ', 1_048_576 - Utf8(Canonical).Length));
        Assert.Equal(SourceHash, NativePixelFormat.SourceDigest(NativePixelFormat.Parse(exact)));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.Parse((byte[])[.. exact, (byte)' ']));
        AssertCode("RESOURCE_LIMIT", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"revn\":1", "\"revn\":10000000000000000000"))));
        AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8("{\"x\":[[[[]]]]}")));
        foreach (string token in new[] { "+1", "01", ".5", "1." })
            AssertCode("PARSE_ERROR", () => NativePixelFormat.Parse(Utf8(Canonical.Replace("\"revn\":1", "\"revn\":" + token))));
    }

    [Theory]
    [InlineData(1, "00000000", "4147445049584c0a0000000100000001000000010000000100000000", TransparentHash)]
    [InlineData(1, "11223300", "4147445049584c0a0000000100000001000000010000000111223300", HiddenHash)]
    [InlineData(2, "ff", "4147445049584c0a00000001000000020000000100000001ff", FullMaskHash)]
    [InlineData(2, "00", "4147445049584c0a0000000100000002000000010000000100", "270d14708db43fb170ef64ce3d984f7c60781198a2875149e4f4df9c5f2edb29")]
    public void CompletePayloadFramesMatchIndependentLiteralVectors(int kind, string samples, string framed, string hash)
    {
        byte[] expected = Convert.FromHexString(framed);
        Assert.Equal(expected, NativePixelFormat.FramePayload(kind, 1, 1, Convert.FromHexString(samples)));
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(expected)));
        NativePixelFormat.ValidatePayload(expected, hash, kind, 1, 1);
        Assert.Equal(expected, Convert.FromHexString(framed));
        Assert.NotEqual(hash, Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(samples))));
        for (int size = 0; size < expected.Length; size++)
            AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload(expected.AsSpan(0, size), hash, kind, 1, 1));
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload([.. expected, 0], hash, kind, 1, 1));
    }

    [Theory]
    [InlineData(0, 0, "PIXEL_CONTENT_MISMATCH")]
    [InlineData(8, 1, "UNSUPPORTED_VERSION")]
    [InlineData(11, 2, "UNSUPPORTED_VERSION")]
    [InlineData(15, 3, "PIXEL_PROFILE_UNSUPPORTED")]
    [InlineData(15, 2, "PIXEL_CONTENT_MISMATCH")]
    [InlineData(16, 1, "PIXEL_CONTENT_MISMATCH")]
    [InlineData(19, 2, "PIXEL_CONTENT_MISMATCH")]
    [InlineData(23, 0, "PIXEL_CONTENT_MISMATCH")]
    [InlineData(24, 1, "PIXEL_CONTENT_MISMATCH")]
    public void PayloadHeaderAndSamplesCannotBeReinterpreted(int offset, byte value, string code)
    {
        var framed = Convert.FromHexString("4147445049584c0a0000000100000001000000010000000100000000");
        framed[offset] = value;
        AssertCode(code, () => NativePixelFormat.ValidatePayload(framed, TransparentHash, 1, 1, 1));
    }

    [Fact]
    public void PayloadReferenceAndExactExpectedDimensionsAreBound()
    {
        var framed = Convert.FromHexString("4147445049584c0a0000000100000001000000010000000111223300");
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload(framed, TransparentHash, 1, 1, 1));
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload(framed, HiddenHash.ToUpperInvariant(), 1, 1, 1));
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload(framed, HiddenHash, 2, 1, 1));
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.ValidatePayload(framed, HiddenHash, 1, 1, 2));
        AssertCode("PIXEL_CONTENT_MISMATCH", () => NativePixelFormat.FramePayload(1, 1, 1, [0]));
        AssertCode("PIXEL_PROFILE_UNSUPPORTED", () => NativePixelFormat.FramePayload(3, 1, 1, [0]));
    }

    [Theory]
    [InlineData(37, 23, "0000002500000017")]
    [InlineData(1, 23, "0000000100000017")]
    [InlineData(37, 1, "0000002500000001")]
    public void FramingRetainsRawRowsCornersAndHiddenRgb(int width, int height, string dimensionsHex)
    {
        var samples = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = 4 * (y * width + x);
                samples[offset] = (byte)x;
                samples[offset + 1] = (byte)y;
                samples[offset + 2] = 0x33;
            }
        var framed = NativePixelFormat.FramePayload(1, width, height, samples);
        Assert.Equal(Convert.FromHexString("4147445049584c0a0000000100000001" + dimensionsHex), framed[..24]);
        Assert.Equal(samples, framed[24..]);
        NativePixelFormat.ValidatePayload(framed, Convert.ToHexStringLower(SHA256.HashData(framed)), 1, width, height);
    }

    [Fact]
    public void LongCodecWorkChecksTheOptionalBudgetAndPropagatesCancellation()
    {
        byte[] input = Utf8(Canonical + new string(' ', 262144));
        int checks = 0;
        NativePixelFormat.Parse(input, () => checks++);
        Assert.True(checks >= 5);
        var cancellation = new OperationCanceledException();
        int attempted = 0;
        Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(() => NativePixelFormat.Parse(input, () =>
        {
            if (++attempted == 3) throw cancellation;
        })));
        byte[] samples = new byte[256 * 256 * 4];
        checks = 0;
        byte[] framed = NativePixelFormat.FramePayload(1, 256, 256, samples, () => checks++);
        Assert.True(checks >= 5);
        checks = 0;
        NativePixelFormat.ValidatePayload(framed, Convert.ToHexStringLower(SHA256.HashData(framed)), 1, 256, 256, () => checks++);
        Assert.True(checks >= 6);
    }

    [Fact]
    public void EveryEditVariantIsBoundToItsTypedContractWithoutFileIo()
    {
        const string operations = """
            {"op":"addLayer","name":"Highlights","alias":"highlights"},
            {"op":"fill","layer":"@highlights","target":"pixels","rect":{"x":0,"y":0,"width":3,"height":2},"rgba":"#ffffff80"},
            {"op":"attachMask","layer":"@highlights"},
            {"op":"fill","layer":"@highlights","target":"mask","rect":{"x":0,"y":0,"width":1,"height":1},"value":255},
            {"op":"replace","layer":"@highlights","target":"mask","rect":{"x":1,"y":0,"width":2,"height":1},"inputPath":"inputs/not-present.raw"},
            {"op":"clear","layer":"@highlights","target":"pixels","rect":{"x":0,"y":0,"width":1,"height":1}},
            {"op":"setVisibility","layer":"@highlights","visible":false},
            {"op":"setOpacity","layer":"@highlights","opacity":0.500000},
            {"op":"reorderLayer","layer":"@highlights","index":0},
            {"op":"removeMask","layer":"@highlights"},
            {"op":"removeLayer","layer":"@highlights"}
            """;
        var request = ParseEdit(operations);
        Assert.Equal("/workspace/asset", request.AssetDirectory);
        Assert.Equal(new PixelSnapshot(Guid.Parse(AssetId), 1, SourceHash), request.ExpectedSnapshot);
        Assert.Equal(new PixelEdit.AddLayer("Highlights", "highlights"), Assert.IsType<PixelEdit.AddLayer>(request.Operations[0]));
        Assert.IsType<PixelEdit.FillPixels>(request.Operations[1]);
        Assert.IsType<PixelEdit.AttachMask>(request.Operations[2]);
        Assert.Equal((byte)255, Assert.IsType<PixelEdit.FillMask>(request.Operations[3]).Value);
        var replace = Assert.IsType<PixelEdit.Replace>(request.Operations[4]);
        Assert.Equal("/workspace/inputs/not-present.raw", Assert.IsType<RawSampleInput.File>(replace.Input).Path);
        Assert.IsType<PixelEdit.Clear>(request.Operations[5]);
        Assert.IsType<PixelEdit.SetVisibility>(request.Operations[6]);
        Assert.Equal(0.5m, Assert.IsType<PixelEdit.SetOpacity>(request.Operations[7]).Opacity);
        Assert.IsType<PixelEdit.ReorderLayer>(request.Operations[8]);
        Assert.IsType<PixelEdit.RemoveMask>(request.Operations[9]);
        Assert.IsType<PixelEdit.RemoveLayer>(request.Operations[10]);
    }

    [Theory]
    [InlineData("{\"op\":\"addLayer\",\"name\":null}")]
    [InlineData("{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":null}")]
    [InlineData("{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":\"@x\"}")]
    [InlineData("{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":\"X\"}")]
    [InlineData("{\"op\":\"addLayer\",\"name\":\"x\",\"id\":\"22222222-2222-4222-8222-222222222222\"}")]
    [InlineData("{\"op\":\"addLayer\",\"name\":\"x\",\"na\\u006de\":\"y\"}")]
    [InlineData("{\"op\":\"addLayer\",\"o\\u0070\":\"addLayer\",\"name\":\"x\"}")]
    [InlineData("{\"op\":\"addLayer\",\"name\":\"\\ud800\"}")]
    [InlineData("{\"op\":\"unknown\"}")]
    [InlineData("null")]
    [InlineData("{}")]
    public void InvalidOperationReportsBoundedIndexedRequestError(string operation)
    {
        var error = AssertCode("INVALID_ARGUMENTS", () => ParseEdit("{\"op\":\"addLayer\",\"name\":\"valid\"}," + operation));
        Assert.Equal(1, error.OperationIndex);
        Assert.StartsWith("$.operations[1]", error.Path);
        Assert.DoesNotContain("/workspace", error.Message);
    }

    [Theory]
    [InlineData("forward-reference", 0)]
    [InlineData("duplicate", 1)]
    [InlineData("removed-reference", 2)]
    [InlineData("removed-reuse", 2)]
    public void AliasesAreOrderedUniqueAndNeverReusedAfterRemoval(string scenario, int index)
    {
        var operations = scenario switch
        {
            "forward-reference" => "{\"op\":\"removeLayer\",\"layer\":\"@later\"},{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":\"later\"}",
            "duplicate" => "{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":\"x\"},{\"op\":\"addLayer\",\"name\":\"y\",\"alias\":\"x\"}",
            "removed-reference" => "{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":\"x\"},{\"op\":\"removeLayer\",\"layer\":\"@x\"},{\"op\":\"removeMask\",\"layer\":\"@x\"}",
            "removed-reuse" => "{\"op\":\"addLayer\",\"name\":\"x\",\"alias\":\"x\"},{\"op\":\"removeLayer\",\"layer\":\"@x\"},{\"op\":\"addLayer\",\"name\":\"y\",\"alias\":\"x\"}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Assert.Equal(index, AssertCode("INVALID_ARGUMENTS", () => ParseEdit(operations)).OperationIndex);
    }

    [Theory]
    [InlineData("\"target\":\"pixels\"", "\"target\":\"Pixels\"")]
    [InlineData("\"target\":\"pixels\"", "\"target\":0")]
    [InlineData("\"x\":0", "\"x\":-0")]
    [InlineData("\"width\":1", "\"width\":1e0")]
    [InlineData("\"height\":1", "\"height\":0")]
    [InlineData("\"rgba\":\"#11223300\"", "\"rgba\":\"#AABBCC00\"")]
    [InlineData("\"rgba\":\"#11223300\"", "\"rgba\":\"#fff\"")]
    [InlineData("\"rgba\":\"#11223300\"", "\"value\":0")]
    [InlineData("\"rgba\":\"#11223300\"", "\"rgba\":\"#11223300\",\"value\":0")]
    [InlineData("\"rect\":{", "\"rect\":{\"unknown\":0,")]
    public void RectangleTargetAndFillFieldsAreLexicallyClosed(string from, string to)
    {
        string fill = "{\"op\":\"fill\",\"layer\":\"" + LayerId + "\",\"target\":\"pixels\",\"rect\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1},\"rgba\":\"#11223300\"}";
        var error = AssertCode("INVALID_ARGUMENTS", () => ParseEdit(fill.Replace(from, to)));
        Assert.Equal(0, error.OperationIndex);
        Assert.StartsWith("$.operations[0]", error.Path);
    }

    [Theory]
    [InlineData("-0")]
    [InlineData("0.5000000")]
    [InlineData("5e-1")]
    [InlineData("1.000001")]
    public void WireOpacityFailuresUseRequestNotSourceCodes(string opacity)
    {
        string operation = "{\"op\":\"setOpacity\",\"layer\":\"" + LayerId + "\",\"opacity\":" + opacity + "}";
        var error = AssertCode("INVALID_ARGUMENTS", () => ParseEdit(operation));
        Assert.Equal("$.operations[0].opacity", error.Path);
        Assert.Equal(0, error.OperationIndex);
    }

    [Fact]
    public void OriginalJsonElementHelpersPreserveLexemesAndCheckBoundsBeforeConversion()
    {
        using var json = JsonDocument.Parse("{\"n\":1e0,\"opacity\":0.5000000,\"large\":9223372036854775807,\"rect\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}");
        AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.Integer(json.RootElement.GetProperty("n")));
        AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.Opacity(json.RootElement.GetProperty("opacity")));
        Assert.Equal(long.MaxValue, NativePixelFormat.Integer(json.RootElement.GetProperty("large"), "$.revn", "INVALID_ARGUMENTS"));
        Assert.Equal(new PixelRect(0, 0, 1, 1), NativePixelWire.Rect(json.RootElement.GetProperty("rect")));
        using var oversizedNumber = JsonDocument.Parse("0." + new string('0', 65536));
        AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.Opacity(oversizedNumber.RootElement));
        using var escapedDuplicate = JsonDocument.Parse("{\"x\":0,\"\\u0078\":0,\"y\":0,\"width\":1,\"height\":1}");
        AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.Rect(escapedDuplicate.RootElement));
    }

    [Fact]
    public void InvalidUnicodeInsideAnOperationRetainsItsOperationIndex()
    {
        string input = EditJson("{\"op\":\"addLayer\",\"name\":\"valid\"},{\"op\":\"addLayer\",\"name\":\"bad\"}");
        byte[] bytes = Utf8(input);
        int offset = input.IndexOf("bad", StringComparison.Ordinal);
        bytes[offset] = 0xff;
        var error = AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.ParseEdit(bytes, "/workspace/asset", "/workspace"));
        Assert.Equal(1, error.OperationIndex);
        Assert.Equal("$.operations[1]", error.Path);
        var primitive = AssertCode("INVALID_ARGUMENTS", () => ParseEdit("{\"op\":\"addLayer\",\"name\":\"valid\"},\"\\ud800\""));
        Assert.Equal(1, primitive.OperationIndex);
    }

    [Theory]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void SnapshotParsingNeverUsesFloatingPoint(long revn)
    {
        var snapshot = NativePixelWire.ParseSnapshot(Utf8(SnapshotJson.Replace("\"revn\":1", "\"revn\":" + revn.ToString(CultureInfo.InvariantCulture))));
        Assert.Equal(revn, snapshot.Revn);
        NativePixelWire.ValidateSnapshot(snapshot);
    }

    [Fact]
    public void SnapshotObjectAndOneKiBLimitAreStrict()
    {
        foreach (string input in new[]
        {
            SnapshotJson.Replace("\"revn\":1", "\"revn\":1e0"),
            SnapshotJson.Replace("\"revn\":1", "\"revn\":0"),
            SnapshotJson.Replace("\"revn\":1", "\"revn\":1,\"re\\u0076n\":1"),
            SnapshotJson.Replace("\"revn\":1,", ""),
            SnapshotJson.Replace("\"sourceDigest\":", "\"other\":"),
            SnapshotJson.Replace("\"revn\":1", "\"revn\":null"),
            "\ufeff" + SnapshotJson
        }) AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.ParseSnapshot(Utf8(input)));
        Assert.Equal(1, NativePixelWire.ParseSnapshot(Utf8(SnapshotJson + new string(' ', 1024 - SnapshotJson.Length))).Revn);
        AssertCode("RESOURCE_LIMIT", () => NativePixelWire.ParseSnapshot(Utf8(SnapshotJson + new string(' ', 1025 - SnapshotJson.Length))));
    }

    [Fact]
    public void RequestBatchDepthBytesAndCumulativeRawWorkAreBounded()
    {
        const string add = "{\"op\":\"addLayer\",\"name\":\"x\"}";
        Assert.Equal(64, ParseEdit(string.Join(',', Enumerable.Repeat(add, 64))).Operations.Length);
        AssertCode("RESOURCE_LIMIT", () => ParseEdit(string.Join(',', Enumerable.Repeat(add, 65))));
        AssertCode("INVALID_ARGUMENTS", () => ParseEdit(""));
        AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.ParseEdit(Utf8("{\"x\":" + new string('[', 16) + "0" + new string(']', 16) + "}"), "/workspace/asset", "/workspace"));
        string request = EditJson(add);
        Assert.Single(NativePixelWire.ParseEdit(Utf8(request + new string(' ', 1_048_576 - request.Length)), "/workspace/asset", "/workspace").Operations);
        AssertCode("RESOURCE_LIMIT", () => NativePixelWire.ParseEdit(new byte[1_048_577], "/workspace/asset", "/workspace"));
        string replace = "{\"op\":\"replace\",\"layer\":\"" + LayerId + "\",\"target\":\"pixels\",\"rect\":{\"x\":0,\"y\":0,\"width\":2048,\"height\":2048},\"inputPath\":\"missing.raw\"}";
        Assert.Single(ParseEdit(replace).Operations);
        Assert.Equal(1, AssertCode("RESOURCE_LIMIT", () => ParseEdit(replace + "," + replace)).OperationIndex);
        string clear = replace.Replace("\"op\":\"replace\"", "\"op\":\"clear\"").Replace(",\"inputPath\":\"missing.raw\"", "");
        Assert.Equal(4, ParseEdit(string.Join(',', Enumerable.Repeat(clear, 4))).Operations.Length);
        Assert.Equal(4, AssertCode("RESOURCE_LIMIT", () => ParseEdit(string.Join(',', Enumerable.Repeat(clear, 5)))).OperationIndex);
    }

    [Fact]
    public void RawPathsResolveAgainstInvocationBaseAndRejectRootEscapesWithoutProbes()
    {
        Assert.Equal("/workspace/inputs/a.raw", NativePixelWire.NormalizePath("inputs/./a.raw", "/workspace", "/workspace"));
        Assert.Equal("/workspace/a.raw", NativePixelWire.NormalizePath("/workspace/a.raw", "/elsewhere", "/workspace"));
        Assert.Equal("/elsewhere/a.raw", NativePixelWire.NormalizePath("../elsewhere/a.raw", "/workspace"));
        Assert.Equal("/a.raw", NativePixelWire.NormalizePath("a.raw", "/", "/"));
        foreach (string path in new[] { "../escape.raw", "/workspace-other/a.raw", "/elsewhere/a.raw" })
            AssertCode("PATH_ESCAPE", () => NativePixelWire.NormalizePath(path, "/workspace", "/workspace"));
        foreach (string path in new[] { "https://host/a", "file:///workspace/a", "//host/a", "a\0b", "a\nb", "\\\\host\\a", new string('a', 4097) })
            AssertCode("INVALID_ARGUMENTS", () => NativePixelWire.NormalizePath(path, "/workspace"));
        string operation = "{\"op\":\"replace\",\"layer\":\"" + LayerId + "\",\"target\":\"mask\",\"rect\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1},\"inputPath\":\"../escape.raw\"}";
        var error = AssertCode("PATH_ESCAPE", () => ParseEdit(operation));
        Assert.Equal(0, error.OperationIndex);
        Assert.Equal("$.operations[0].inputPath", error.Path);
    }

    [Fact]
    public void RawBufferFactoryCopiesAndBoundsMutableInput()
    {
        byte[] caller = [0x11, 0x22, 0x33, 0];
        var owned = RawSampleInput.FromBytes(caller);
        caller[0] = 0xff;
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0 }, owned.Bytes.ToArray());
        Assert.Equal(4, owned.Length);
        AssertCode("RESOURCE_LIMIT", () => RawSampleInput.FromBytes(new byte[16_777_217]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => RawSampleInput.FromBytes(caller, cancelled.Token)).CancellationToken);
        Assert.DoesNotContain(typeof(RawSampleInput.Buffer).GetProperties(), p => p.PropertyType == typeof(byte[]) || p.PropertyType == typeof(ReadOnlyMemory<byte>));
    }

    [Fact]
    public void RecordCopyConstructorsCannotIntroduceNewUnionVariants()
    {
        var layer = new PixelLayerRef.ById(Guid.Parse(LayerId));
        var edit = new PixelEdit.AddLayer("Layer");
        var delivery = new PixelPreviewDelivery.File("/workspace/output.png");
        Assert.Equal(layer, layer with { });
        Assert.Equal(edit, edit with { });
        Assert.Equal(delivery, delivery with { });
        AssertCode("INVALID_ARGUMENTS", () => new UnsupportedLayerRef(layer));
        AssertCode("INVALID_ARGUMENTS", () => new UnsupportedEdit(edit));
        AssertCode("INVALID_ARGUMENTS", () => new UnsupportedDelivery(delivery));
    }

    [Fact]
    public void ResultJsonPreservesExplicitNullMaskAndActualTypedSampleArrays()
    {
        var state = State();
        using var info = Facts(state);
        AssertFields(info.RootElement, "snapshot", "profile", "limitsProfile", "name", "width", "height", "logicalSourceBytes", "layers");
        AssertFields(info.RootElement.GetProperty("layers")[0], "id", "name", "visible", "opacity", "pixelsSha256", "maskSha256");
        Assert.Equal(JsonValueKind.Null, info.RootElement.GetProperty("layers")[0].GetProperty("maskSha256").ValueKind);
        PixelRegionResult rgba = new RgbaRegionResult(state.Snapshot, Guid.Parse(LayerId), new PixelRect(0, 0, 1, 1), ["#11223300"]);
        using var pixels = Facts(rgba);
        AssertFields(pixels.RootElement, "snapshot", "profile", "limitsProfile", "layerId", "target", "rect", "samples");
        Assert.Equal("pixels", pixels.RootElement.GetProperty("target").GetString());
        Assert.Equal("#11223300", pixels.RootElement.GetProperty("samples")[0].GetString());
        PixelRegionResult mask = new MaskRegionResult(state.Snapshot, Guid.Parse(LayerId), new PixelRect(0, 0, 2, 1), [0, 255]);
        using var masks = Facts(mask);
        Assert.Equal("mask", masks.RootElement.GetProperty("target").GetString());
        Assert.Equal(JsonValueKind.Array, masks.RootElement.GetProperty("samples").ValueKind);
        Assert.Equal(255, masks.RootElement.GetProperty("samples")[1].GetInt32());
    }

    [Fact]
    public void ValidationAndEditResultsContainOnlyTheReviewedOutcomeFields()
    {
        using var valid = Facts(new ValidPixelAssetResult(State()));
        AssertFields(valid.RootElement, "valid", "state");
        Assert.True(valid.RootElement.GetProperty("valid").GetBoolean());
        using var invalid = Facts(new InvalidPixelAssetResult([new PixelDiagnostic("PARSE_ERROR", "Invalid metadata.", "$")]));
        AssertFields(invalid.RootElement, "valid", "errors");
        Assert.False(invalid.RootElement.GetProperty("valid").GetBoolean());
        AssertFields(invalid.RootElement.GetProperty("errors")[0], "code", "message", "path");
        using var created = Facts(new CreatePixelAssetResult(State(), Guid.Parse(LayerId)));
        AssertFields(created.RootElement, "state", "initialLayerId");
        var state = State();
        using var edit = Facts(new EditPixelAssetResult(state.Snapshot, state, true,
            [new PixelCreatedLayer(0, Guid.Parse(LayerId))], [Guid.Parse(LayerId)],
            [new PixelLayerChange(Guid.Parse(LayerId), [PixelLayerChangeKind.Created])],
            [new PixelChangedRegion(Guid.Parse(LayerId), PixelTarget.Pixels, new PixelRect(0, 0, 1, 1))]));
        AssertFields(edit.RootElement, "beforeSnapshot", "state", "changed", "createdLayers", "affectedLayerIds", "layerChanges", "changedRegions");
        AssertFields(edit.RootElement.GetProperty("createdLayers")[0], "operationIndex", "layerId");
        Assert.Equal("created", edit.RootElement.GetProperty("layerChanges")[0].GetProperty("changes")[0].GetString());
    }

    [Fact]
    public void PreviewAndExportFactsHaveExactSettingsAndNeverSerializePngBytesOrPaths()
    {
        var token = State().Snapshot;
        var rect = new PixelRect(0, 0, 1, 1);
        using var inline = Facts(new PixelPreviewResult(token, rect, TransparentHash, PixelPreviewDeliveryKind.Inline, 4, HiddenHash, [1, 2, 3, 4]));
        using var file = Facts(new PixelPreviewResult(token, rect, TransparentHash, PixelPreviewDeliveryKind.File, 4, HiddenHash, []));
        foreach (var result in new[] { inline, file })
        {
            AssertFields(result.RootElement, "sourceToken", "profile", "limitsProfile", "evaluatorProfile", "settings", "rect", "width", "height", "rgbaSha256",
                "delivery", "encoderProfile", "encodedBytes", "fileSha256", "mediaType");
            AssertEvidence(result.RootElement);
            Assert.Equal("agd.pixels.png.v1", result.RootElement.GetProperty("encoderProfile").GetString());
            Assert.Equal("image/png", result.RootElement.GetProperty("mediaType").GetString());
        }
        Assert.Equal("inline", inline.RootElement.GetProperty("delivery").GetString());
        Assert.Equal("file", file.RootElement.GetProperty("delivery").GetString());
        using var export = Facts(new PixelExportResult(token, rect, TransparentHash, 4, HiddenHash));
        AssertFields(export.RootElement, "sourceToken", "profile", "limitsProfile", "evaluatorProfile", "settings", "rect", "width", "height", "rgbaSha256",
            "encoderProfile", "encodedBytes", "fileSha256", "mediaType");
        AssertEvidence(export.RootElement);
    }

    [Fact]
    public void CompletedCheckMismatchIsTypedEvidenceNotAnExceptionOrEncoderClaim()
    {
        var result = new PixelExportCheckResult(State().Snapshot, new PixelRect(0, 0, 1, 1), TransparentHash, false,
            new PixelObservedPng(2, 1, 100, HiddenHash, FullMaskHash), [PixelExportDifference.Dimensions, PixelExportDifference.Samples]);
        using var json = Facts(result);
        AssertFields(json.RootElement, "sourceToken", "profile", "limitsProfile", "evaluatorProfile", "settings", "rect", "width", "height", "rgbaSha256",
            "matches", "comparison", "comparatorProfile", "pngPolicy", "decoderProfile", "execution", "pixelsDecoded", "observed", "differences");
        AssertEvidence(json.RootElement);
        Assert.False(json.RootElement.GetProperty("matches").GetBoolean());
        Assert.Equal("dimensions-and-decoded-rgba8", json.RootElement.GetProperty("comparison").GetString());
        Assert.Equal("agd.pixels.png-samples.v1", json.RootElement.GetProperty("comparatorProfile").GetString());
        Assert.Equal("agd.png-rgba8-structure.v1", json.RootElement.GetProperty("pngPolicy").GetString());
        Assert.Equal("agd.png-rgba8-managed.v1", json.RootElement.GetProperty("decoderProfile").GetString());
        Assert.Equal("managed-in-process", json.RootElement.GetProperty("execution").GetString());
        Assert.True(json.RootElement.GetProperty("pixelsDecoded").GetBoolean());
        AssertFields(json.RootElement.GetProperty("observed"), "width", "height", "encodedBytes", "fileSha256", "rgbaSha256");
        Assert.Equal(new[] { "dimensions", "samples" }, json.RootElement.GetProperty("differences").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void DiagnosticsAreBoundedSafeAndDoNotEchoFilesystemPaths()
    {
        var error = new NativePixelException("INVALID_ARGUMENTS", new string('a', 5000), "$.operations[63].rect", 63);
        Assert.Equal(1024, error.Message.EnumerateRunes().Count());
        Assert.True(Encoding.UTF8.GetByteCount(error.Message) <= 4096);
        Assert.Equal("$.operations[63].rect", error.Path);
        Assert.Equal(63, error.OperationIndex);
        var unicode = new NativePixelException("INVALID_PROPERTY", string.Concat(Enumerable.Repeat("\U0001f469", 1100)));
        Assert.Equal(4096, Encoding.UTF8.GetByteCount(unicode.Message));
        var unsafeError = new NativePixelException("bad\ncode", "Failed /home/private/file.", "/home/private/file", 64);
        Assert.Equal("INVALID_ARGUMENTS", unsafeError.Code);
        Assert.DoesNotContain("/home", unsafeError.Message);
        Assert.Null(unsafeError.Path);
        Assert.Null(unsafeError.OperationIndex);
        Assert.DoesNotContain('\n', new NativePixelException("PARSE_ERROR", "Invalid\nmetadata.").Message);
        Assert.Null(new NativePixelException("PARSE_ERROR", "Invalid metadata.", "$" + new string('a', 512)).Path);
    }

    private static void AssertEvidence(JsonElement result)
    {
        Assert.Equal("agd.pixels.rgba8-linear-over.v1", result.GetProperty("profile").GetString());
        Assert.Equal("agd.pixels.rgba8-linear-over.v1", result.GetProperty("evaluatorProfile").GetString());
        Assert.Equal("agd.pixels.runtime.v1", result.GetProperty("limitsProfile").GetString());
        var settings = result.GetProperty("settings");
        AssertFields(settings, "fractionalBits", "tableSha256", "rounding");
        Assert.Equal(24, settings.GetProperty("fractionalBits").GetInt32());
        Assert.Equal("353442b2a3b85e1030cc800349cc40d4d0b7e2bdf063a7a9766298f4dabaa7b8", settings.GetProperty("tableSha256").GetString());
        Assert.Equal("nearest-ties-up", settings.GetProperty("rounding").GetString());
    }

    private static void AssertFields(JsonElement value, params string[] names)
        => Assert.Equal(names.Order(StringComparer.Ordinal), value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

    private static JsonDocument Facts(object value) => JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), FactsOptions));
    private static PixelAssetInfo State() => new(new PixelSnapshot(Guid.Parse(AssetId), 1, SourceHash), "Pixel", 1, 1, 4, BaseDocument().Layers);
    private static NativePixelDocument BaseDocument() => new(Guid.Parse(AssetId), 1, "Pixel", 1, 1,
        [new PixelLayerInfo(Guid.Parse(LayerId), "Layer 1", true, 1m, TransparentHash, null)]);

    private static NativePixelDocument WithLayers(int count, bool mask)
    {
        var layer = BaseDocument().Layers[0];
        return BaseDocument() with
        {
            Layers = Enumerable.Range(1, count).Select(i => layer with
            {
                Id = new Guid(i, 0, 0, new byte[8]),
                Visible = false,
                Opacity = 0,
                MaskSha256 = mask ? FullMaskHash : null
            }).ToImmutableArray()
        };
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static string WithName(string escapedName) => Canonical.Replace("\"Pixel\"", "\"" + escapedName + "\"");
    private static string EditJson(string operations) => "{\"expectedSnapshot\":" + SnapshotJson + ",\"operations\":[" + operations + "]}";
    private static EditPixelAssetRequest ParseEdit(string operations)
        => NativePixelWire.ParseEdit(Utf8(EditJson(operations)), "/workspace/asset", "/workspace", "/workspace");

    private static NativePixelException AssertCode(string code, Action action)
    {
        var error = Assert.Throws<NativePixelException>(action);
        Assert.Equal(code, error.Code);
        return error;
    }

    private sealed record UnsupportedLayerRef : PixelLayerRef
    {
        public UnsupportedLayerRef(PixelLayerRef Original) : base(Original) { }
    }

    private sealed record UnsupportedEdit : PixelEdit
    {
        public UnsupportedEdit(PixelEdit Original) : base(Original) { }
    }

    private sealed record UnsupportedDelivery : PixelPreviewDelivery
    {
        public UnsupportedDelivery(PixelPreviewDelivery Original) : base(Original) { }
    }
}
