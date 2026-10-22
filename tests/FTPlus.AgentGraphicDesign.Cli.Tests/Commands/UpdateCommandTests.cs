using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class UpdateCommandTests : CommandTestBase
{
    [Fact]
    public void Update_NodeName_ReturnsZeroAndUpdates()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "-n", "renamed-rect", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("renamed-rect", result.RootElement.GetProperty("data").GetProperty("nodeName").GetString());
    }

    [Fact]
    public void Update_Dimensions_ReturnsZeroAndUpdates()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "-w", "200", "-h", "100", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Update_Position_ReturnsZeroAndUpdates()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "-x", "100", "-y", "200", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Update_Style_ReturnsZeroAndUpdates()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "--fill", "#00ff00", "--stroke", "#0000ff", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Update_WithTokenReferences_AuthorsFillStrokeAndTypographyTokens()
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
                "body": { "$value": { "fontFamily": "Inter" } }
              }
            }
            """);
        Assert.Equal(0, RunCommand("tokens", "import", docPath, tokensPath).ExitCode);
        var (_, addOutput, _) = RunCommand("add", docPath, "text", "--text", "Hello", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, _, _) = RunCommand(
            "update", docPath, nodeId, "--fill-token", "{primary}",
            "--stroke-token", "primary", "--typography-token", "{body}", "-f", "json");

        Assert.Equal(0, exitCode);
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        var text = Assert.IsType<TextNode>(Assert.Single(document!.Root.Children));
        Assert.Equal("{primary}", text.Style!.Fill!.TokenRef);
        Assert.Equal("primary", text.Style.Stroke!.TokenRef);
        Assert.Equal("{body}", text.TypographyTokenRef);

        Assert.Equal(0, RunCommand(
            "update", docPath, nodeId, "--typography-token", "__clear__", "-f", "json").ExitCode);
        document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        text = Assert.IsType<TextNode>(Assert.Single(document!.Root.Children));
        Assert.Null(text.TypographyTokenRef);
    }

    [Fact]
    public void Update_TextLayoutOptions_UpdateAndClear()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "text", "--text", "Hello", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, stderr) = RunCommand(
            "update", docPath, nodeId, "--text-align", "end", "--vertical-align", "baseline",
            "--max-width", "200", "--line-height", "2", "--letter-spacing", "1", "-f", "json");

        Assert.True(exitCode == 0, $"exit {exitCode}: {stderr}\n{output}");
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        var text = Assert.IsType<TextNode>(Assert.Single(document!.Root.Children));
        Assert.Equal("end", text.TextAlign);
        Assert.Equal("baseline", text.VerticalAlign);
        Assert.Equal(200, text.MaxWidth);
        Assert.Equal(2, text.LineHeight);
        Assert.Equal(1, text.LetterSpacing);

        Assert.Equal(0, RunCommand(
            "update", docPath, nodeId, "--text-align", "__clear__", "--vertical-align", "__clear__", "-f", "json").ExitCode);
        document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        text = Assert.IsType<TextNode>(Assert.Single(document!.Root.Children));
        Assert.Null(text.TextAlign);
        Assert.Null(text.VerticalAlign);
        Assert.Equal(200, text.MaxWidth);

        var (invalidExit, invalidOutput, _) = RunCommand("update", docPath, nodeId, "--max-width", "-5", "-f", "json");
        Assert.NotEqual(0, invalidExit);
        Assert.Equal("INVALID_PROPERTY", JsonDocument.Parse(invalidOutput).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Update_Visibility_ReturnsZeroAndUpdates()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "--visible", "false", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Update_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("update", "/nonexistent/path.agd.json", "11111111-1111-1111-1111-111111111111", "-n", "new-name", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Update_InvalidNodeId_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("update", docPath, "not-a-guid", "-n", "new-name", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("NODE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Update_NodeNotFound_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("update", docPath, "99999999-9999-9999-9999-999999999999", "-n", "new-name", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("NODE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }


    // ---- fill rule ----

    [Fact]
    public void Update_FillRule_SetsAndClears()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "path", "--path-data", "M0,0 L10,0 L10,10 Z", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        Assert.Equal(0, RunCommand("update", docPath, nodeId, "--fill-rule", "EvenOdd", "-f", "json").ExitCode);
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        var path = Assert.IsType<PathNode>(Assert.Single(document!.Root.Children));
        Assert.Equal("evenodd", path.Style!.FillRule);

        Assert.Equal(0, RunCommand("update", docPath, nodeId, "--fill-rule", "__clear__", "-f", "json").ExitCode);
        document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        path = Assert.IsType<PathNode>(Assert.Single(document!.Root.Children));
        Assert.Null(path.Style?.FillRule);
    }

    [Fact]
    public void Update_UnknownFillRule_ReturnsInvalidProperty()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "path", "--path-data", "M0,0 L10,0 L10,10 Z", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "--fill-rule", "even-odd", "-f", "json");

        Assert.NotEqual(0, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath));
        Assert.Null(Assert.Single(document!.Root.Children).Style?.FillRule);
    }

    [Fact]
    public void Update_FillRuleOnRectangle_ReturnsInvalidProperty()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "10", "--height", "10", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("update", docPath, nodeId, "--fill-rule", "evenodd", "-f", "json");

        Assert.NotEqual(0, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }
}
