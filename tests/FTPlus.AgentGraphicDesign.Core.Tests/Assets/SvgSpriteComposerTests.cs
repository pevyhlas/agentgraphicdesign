using System.Text;
using System.Xml.Linq;
using FTPlus.AgentGraphicDesign.Core.Export;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class SvgSpriteComposerTests
{
    [Theory]
    [InlineData("<symbol id='one'><path id='shared'/></symbol>", "<symbol id='two'><path id='shar&#101;d'/></symbol>")]
    [InlineData("<symbol id='one'><path id='two'/></symbol>", "<symbol id='two'/>")]
    [InlineData("<symbol id='one'/>", "<symbol id='two'><path id='one'/></symbol>")]
    [InlineData("<symbol id='one'><path id='one'/></symbol>", "<symbol id='two'/>")]
    [InlineData("<symbol id='one'><path xml:id='shared'/></symbol>", "<symbol id='two'><path id='shared'/></symbol>")]
    public void DecodedXmlIds_AreUniqueAcrossAllSymbolsAndDefinitions(string first, string second)
    {
        var error = Assert.Throws<DocumentEngineException>(() =>
            SvgSpriteComposer.Compose([Encoding.UTF8.GetBytes(first), Encoding.UTF8.GetBytes(second)]));
        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
    }

    [Theory]
    [InlineData("<!DOCTYPE symbol [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><symbol id='one'>&x;</symbol>")]
    [InlineData("<svg id='one'/>")]
    [InlineData("<symbol id='1invalid'/>")]
    [InlineData("<symbol id='one'>")]
    [InlineData("<symbol id='one'/><symbol id='two'/>")]
    [InlineData("<?xml version='1.0'?><symbol id='one'/>")]
    public void InvalidOrUnsafeXml_FailsWithStructuredError(string fragment)
    {
        var error = Assert.Throws<DocumentEngineException>(() => SvgSpriteComposer.Compose([Encoding.UTF8.GetBytes(fragment)]));
        Assert.Equal("ASSET_EXPORT_FAILED", error.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidFragmentEncodings))]
    public void NonUtf8BomAndMalformedEncoding_AreRejected(byte[] fragment)
    {
        var error = Assert.Throws<DocumentEngineException>(() => SvgSpriteComposer.Compose([fragment]));
        Assert.Equal("ASSET_EXPORT_FAILED", error.Code);
        Assert.Contains("BOM-free UTF-8", error.Message);
    }

    public static IEnumerable<object[]> InvalidFragmentEncodings()
    {
        const string fragment = "<symbol id='one'><desc>text</desc></symbol>";
        foreach (var encoding in new Encoding[]
        {
            new UnicodeEncoding(false, true), new UnicodeEncoding(true, true),
            new UTF32Encoding(false, true), new UTF32Encoding(true, true), new UTF8Encoding(true)
        })
        {
            yield return [encoding.GetPreamble().Concat(encoding.GetBytes(fragment)).ToArray()];
            if (encoding.CodePage != Encoding.UTF8.CodePage)
                yield return [encoding.GetBytes(fragment)];
        }
        foreach (var invalid in new byte[][]
        {
            [0x80], [0xc0, 0xaf], [0xed, 0xa0, 0x80], [0xf4, 0x90, 0x80, 0x80], [0xe2, 0x82]
        })
        {
            yield return ["<symbol id='one'><desc>"u8.ToArray().Concat(invalid)
                .Concat("</desc></symbol>"u8.ToArray()).ToArray()];
        }
    }

    [Fact]
    public void ValidNonAsciiUtf8_IsPreservedWithoutReplacement()
    {
        var fragment = "<symbol id='one'><desc>caf\u00e9 \U0001f3a8</desc></symbol>";
        var bytes = Encoding.UTF8.GetBytes(fragment);

        var result = SvgSpriteComposer.Compose([bytes]);

        var xml = new UTF8Encoding(false, true).GetString(result);
        Assert.Contains(fragment, xml);
        Assert.Equal("caf\u00e9 \U0001f3a8", XDocument.Parse(xml).Descendants()
            .Single(element => element.Name.LocalName == "desc").Value);
    }

    [Fact]
    public void ValidFragments_ArePreservedInExplicitOrder()
    {
        var first = "<symbol id='z'><path id='UPPER'/></symbol>\n"u8.ToArray();
        var second = "<symbol id='a'><path id='upper'/></symbol>\n"u8.ToArray();

        var bytes = SvgSpriteComposer.Compose([first, second]);

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains(Encoding.UTF8.GetString(first) + Encoding.UTF8.GetString(second), text);
        var document = XDocument.Parse(text);
        XNamespace svg = "http://www.w3.org/2000/svg";
        Assert.Equal(["z", "a"], document.Descendants(svg + "symbol").Select(symbol => (string?)symbol.Attribute("id")));
        Assert.Equal(bytes, SvgSpriteComposer.Compose([first, second]));
    }
}
