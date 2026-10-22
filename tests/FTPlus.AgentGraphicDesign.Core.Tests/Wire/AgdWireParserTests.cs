using System.Text;
using FTPlus.AgentGraphicDesign.Core.Wire;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Wire;

/// <summary>
/// Unit fixtures over hand-built UTF-8 <c>byte[]</c> proving the wire seam rejects the
/// lexical violations the native pixel contract names, and that it measures the ORIGINAL
/// bytes rather than a re-serialized/canonical form.
/// </summary>
public class AgdWireParserTests
{
    private const string ValidSnapshot =
        "{\"assetId\":\"2a4b8fde-468e-4bbb-a11f-a2b20169060a\",\"revn\":1," +
        "\"sourceDigest\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}";

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] ToolCall(string toolName, string argumentsJson) => Utf8(
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" +
        toolName + "\",\"arguments\":" + argumentsJson + "}}");

    private static string Request(string body) =>
        "{\"expectedSnapshot\":" + ValidSnapshot + ",\"operations\":" + body + "}";

    // -- Happy paths: the seam must not over-reject. --

    [Fact]
    public void AdmitRequest_WellFormedRequest_Passes()
    {
        var request = Request("[{\"op\":\"setOpacity\",\"layer\":\"@x\",\"opacity\":0.5}]");
        AgdWireParser.AdmitRequest(Utf8(request)); // does not throw
    }

    [Fact]
    public void AdmitToolCall_WellFormedEnvelope_Passes()
    {
        var message = ToolCall("edit_pixel_asset",
            "{\"assetDirectory\":\"art\",\"request\":" + Request("[{\"op\":\"attachMask\",\"layer\":\"@x\"}]") + "}");
        AgdWireParser.AdmitToolCall(message); // does not throw
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("0.500000")] // exactly six fractional digits is allowed
    [InlineData("1")]
    [InlineData("0")]
    public void AdmitRequest_AcceptedOpacitySpellings_Pass(string opacity)
    {
        var request = Request("[{\"op\":\"setOpacity\",\"layer\":\"@x\",\"opacity\":" + opacity + "}]");
        AgdWireParser.AdmitRequest(Utf8(request));
    }

    // -- Duplicate / escaped-duplicate detection. --

    [Fact]
    public void AdmitToolCall_OuterEscapedDuplicateArgument_Rejected()
    {
        // "assetDirectory" and "assetDirecto\u0072y" both decode to the same name.
        var message = ToolCall("create_pixel_asset",
            "{\"assetDirectory\":\"a\",\"assetDirecto\\u0072y\":\"b\"}");
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitToolCall(message));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    [Fact]
    public void AdmitRequest_NestedDuplicateInsideRequest_Rejected()
    {
        var request = "{\"expectedSnapshot\":" + ValidSnapshot + ",\"operations\":[]," +
                      "\"operations\":[]}"; // duplicate top-level member inside the request
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    [Fact]
    public void AdmitRequest_NestedDuplicateInsideRect_Rejected()
    {
        // Escaped-duplicate "x"/"\u0078" inside a rect object, several levels deep.
        var request = Request(
            "[{\"op\":\"fill\",\"layer\":\"@x\",\"target\":\"pixels\"," +
            "\"rect\":{\"x\":0,\"\\u0078\":0,\"y\":0,\"width\":1,\"height\":1},\"rgba\":\"#ffffffff\"}]");
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    // -- Original-byte size bounds. --

    [Fact]
    public void AdmitToolCall_RequestOriginalSliceOverOneMiB_Rejected_EvenWhenTrimmedIsSmall()
    {
        // Inflate the request object with >1 MiB of internal whitespace. The canonical/trimmed
        // content is a few hundred bytes; only the ORIGINAL slice (with whitespace) exceeds 1 MiB.
        var whitespace = new string(' ', (1 << 20) + 4096);
        var inflatedRequest = "{\"expectedSnapshot\":" + ValidSnapshot + whitespace +
                              ",\"operations\":[{\"op\":\"attachMask\",\"layer\":\"@x\"}]}";
        Assert.True(Encoding.UTF8.GetByteCount(inflatedRequest) > (1 << 20));

        var message = ToolCall("edit_pixel_asset",
            "{\"assetDirectory\":\"a\",\"request\":" + inflatedRequest + "}");
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitToolCall(message));
        Assert.Equal("RESOURCE_LIMIT", ex.Code);
    }

    [Fact]
    public void AdmitSnapshot_JustOverOneKiB_Rejected()
    {
        // Only the three closed members, but a long sourceDigest pushes the object past 1 KiB.
        var longDigest = new string('a', 1100);
        var snapshot = "{\"assetId\":\"2a4b8fde-468e-4bbb-a11f-a2b20169060a\",\"revn\":1," +
                       "\"sourceDigest\":\"" + longDigest + "\"}";
        Assert.True(Encoding.UTF8.GetByteCount(snapshot) > 1024);
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitSnapshot(Utf8(snapshot)));
        Assert.Equal("RESOURCE_LIMIT", ex.Code);
    }

    // -- revn precision above 2^53 survives (accepted, not corrupted). --

    [Fact]
    public void AdmitRequest_RevnAboveTwoTo53_Accepted()
    {
        var snapshot = "{\"assetId\":\"2a4b8fde-468e-4bbb-a11f-a2b20169060a\"," +
                       "\"revn\":9007199254740993," +
                       "\"sourceDigest\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}";
        var request = "{\"expectedSnapshot\":" + snapshot + ",\"operations\":[]}";
        AgdWireParser.AdmitRequest(Utf8(request)); // 2^53+1 is a valid integer token; accepted, never rounded
    }

    // -- Numeric token spelling. --

    [Fact]
    public void AdmitRequest_ExponentToken_Rejected()
    {
        var request = Request("[{\"op\":\"setOpacity\",\"layer\":\"@x\",\"opacity\":1e0}]");
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_FORMAT", ex.Code);
    }

    [Fact]
    public void AdmitRequest_SignedInteger_Rejected()
    {
        var request = Request(
            "[{\"op\":\"fill\",\"layer\":\"@x\",\"target\":\"mask\"," +
            "\"rect\":{\"x\":-5,\"y\":0,\"width\":1,\"height\":1},\"value\":0}]");
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_FORMAT", ex.Code);
    }

    [Fact]
    public void AdmitRequest_LeadingPlusInteger_Rejected()
    {
        // "+5" is not valid JSON; the parser reports it as a malformed-format failure.
        var request = Request(
            "[{\"op\":\"fill\",\"layer\":\"@x\",\"target\":\"mask\"," +
            "\"rect\":{\"x\":+5,\"y\":0,\"width\":1,\"height\":1},\"value\":0}]");
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_FORMAT", ex.Code);
    }

    [Fact]
    public void AdmitRequest_ExcessOpacityFractionalDigits_Rejected()
    {
        var request = Request("[{\"op\":\"setOpacity\",\"layer\":\"@x\",\"opacity\":0.5000000}]"); // 7 digits
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_FORMAT", ex.Code);
    }

    // -- Closed-object rule: unknown members are rejected deterministically. --

    [Fact]
    public void AdmitRequest_UnknownTopLevelMember_Rejected()
    {
        var request = "{\"expectedSnapshot\":" + ValidSnapshot + ",\"operations\":[],\"bogus\":1}";
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8(request)));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
        Assert.Equal("$.bogus", ex.Path);
    }

    [Fact]
    public void AdmitSnapshot_UnknownMember_Rejected()
    {
        var snapshot = "{\"assetId\":\"2a4b8fde-468e-4bbb-a11f-a2b20169060a\",\"revn\":1," +
                       "\"sourceDigest\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"," +
                       "\"extra\":true}";
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitSnapshot(Utf8(snapshot)));
        Assert.Equal("INVALID_ARGUMENTS", ex.Code);
    }

    // -- Malformed JSON. --

    [Fact]
    public void AdmitRequest_MalformedJson_RejectedAsInvalidFormat()
    {
        var ex = Assert.Throws<WireFidelityException>(() => AgdWireParser.AdmitRequest(Utf8("{ not json")));
        Assert.Equal("INVALID_FORMAT", ex.Code);
    }
}
