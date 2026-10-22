using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class AddCommandTests : CommandTestBase
{
    [Fact]
    public void Add_Rectangle_ReturnsZeroAndAddsNode()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "rectangle", "--width", "100", "--height", "50", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("rectangle", result.RootElement.GetProperty("data").GetProperty("nodeType").GetString());
    }

    [Fact]
    public void Add_Ellipse_ReturnsZeroAndAddsNode()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "ellipse", "--width", "80", "--height", "80", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ellipse", result.RootElement.GetProperty("data").GetProperty("nodeType").GetString());
    }

    [Fact]
    public void Add_Text_ReturnsZeroAndAddsNode()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "text", "--text", "Hello World", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("text", result.RootElement.GetProperty("data").GetProperty("nodeType").GetString());
    }

    [Fact]
    public void Add_WithPosition_SetsTransform()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-x", "100", "-y", "200", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Add_WithStyle_SetsFillAndStroke()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "--fill", "#ff0000", "--stroke", "#000000", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Add_WithTokenReferences_AuthorsFillStrokeAndTypographyTokens()
    {
        var docPath = CreateTokenDocument();

        var (exitCode, _, _) = RunCommand(
            "add", docPath, "text", "--text", "Hello",
            "--fill-token", "primary", "--stroke-token", "{primary}",
            "--typography-token", "body", "-f", "json");

        Assert.Equal(0, exitCode);
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        var text = Assert.IsType<TextNode>(Assert.Single(document!.Root.Children));
        Assert.Equal("primary", text.Style!.Fill!.TokenRef);
        Assert.Equal("{primary}", text.Style.Stroke!.TokenRef);
        Assert.Equal("body", text.TypographyTokenRef);
    }

    [Fact]
    public void Add_TextWithLayoutOptions_PersistsLayoutFields()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, stderr) = RunCommand(
            "add", docPath, "text", "--text", "Hello wrapped world",
            "--text-align", "center", "--vertical-align", "middle",
            "--max-width", "80", "--line-height", "1.5", "--letter-spacing", "0.5", "-f", "json");

        Assert.True(exitCode == 0, $"exit {exitCode}: {stderr}\n{output}");
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        var text = Assert.IsType<TextNode>(Assert.Single(document!.Root.Children));
        Assert.Equal("center", text.TextAlign);
        Assert.Equal("middle", text.VerticalAlign);
        Assert.Equal(80, text.MaxWidth);
        Assert.Equal(1.5, text.LineHeight);
        Assert.Equal(0.5, text.LetterSpacing);
    }

    [Fact]
    public void Add_TextWithInvalidLayoutOptions_IsRejectedWithoutSaving()
    {
        var docPath = CreateTestDocument();

        var (numericExit, numericOutput, _) = RunCommand("add", docPath, "text", "--text", "x", "--line-height", "0", "-f", "json");
        var (alignExit, alignOutput, _) = RunCommand("add", docPath, "text", "--text", "x", "--text-align", "justify", "-f", "json");

        Assert.NotEqual(0, numericExit);
        Assert.NotEqual(0, alignExit);
        Assert.Equal("INVALID_PROPERTY", JsonDocument.Parse(numericOutput).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("INVALID_PROPERTY", JsonDocument.Parse(alignOutput).RootElement.GetProperty("error").GetProperty("code").GetString());
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        Assert.Empty(document!.Root.Children);
    }

    [Fact]
    public void Add_ConflictingLiteralAndTokenReference_IsRejectedWithoutSaving()
    {
        var docPath = CreateTokenDocument();

        var (exitCode, output, _) = RunCommand(
            "add", docPath, "rectangle", "--fill", "#ffffff", "--fill-token", "primary", "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.Equal("INVALID_PROPERTY", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        Assert.Empty(document!.Root.Children);
    }

    [Fact]
    public void Add_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("add", "/nonexistent/path.agd.json", "rectangle", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Add_InvalidType_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "invalid-type", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("INVALID_NODE_TYPE", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Add_NegativeDimensions_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "rectangle", "--width", "-50", "--height", "50", "-f", "json");

        Assert.Equal(4, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("INVALID_DIMENSIONS", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Add_WithName_SetsNodeName()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "rectangle", "-n", "my-rect", "--width", "50", "--height", "50", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.Equal("my-rect", result.RootElement.GetProperty("data").GetProperty("nodeName").GetString());
    }

    private string CreateTokenDocument()
    {
        var docPath = CreateTestDocument();
        var tokensPath = Path.Combine(TestDir, "tokens.json");
        File.WriteAllText(tokensPath, """
            {
              "color": {
                "$type": "color",
                "primary": { "$value": "#123456" }
              },
              "typography": {
                "$type": "typography",
                "body": { "$value": { "fontFamily": "Inter", "fontSize": "18px" } }
              }
            }
            """);
        var (exitCode, _, error) = RunCommand("tokens", "import", docPath, tokensPath);
        Assert.True(exitCode == 0, error);
        return docPath;
    }


    // ---- fill rule ----

    [Fact]
    public void Add_PathWithFillRule_PersistsNormalizedValue()
    {
        var docPath = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("add", docPath, "path",
            "--path-data", "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z",
            "--fill-rule", "EvenOdd", "-f", "json");

        Assert.Equal(0, exitCode);
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        var path = Assert.IsType<PathNode>(Assert.Single(document!.Root.Children));
        Assert.Equal("evenodd", path.Style!.FillRule);
    }

    [Fact]
    public void Add_RectangleWithFillRule_ReturnsInvalidProperty()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("add", docPath, "rectangle", "--width", "10", "--height", "10",
            "--fill-rule", "evenodd", "-f", "json");

        Assert.NotEqual(0, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        Assert.Empty(document!.Root.Children);
    }
}
