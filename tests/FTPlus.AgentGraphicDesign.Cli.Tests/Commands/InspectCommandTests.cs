using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class InspectCommandTests : CommandTestBase
{
    [Fact]
    public void Inspect_ValidDocument_ReturnsZero()
    {
        var docPath = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("inspect", docPath);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Inspect_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("inspect", "/nonexistent/path.agd.json", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("FILE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Inspect_ParseError_ReturnsOne()
    {
        var invalidPath = CreateInvalidJsonFile();

        var (exitCode, output, _) = RunCommand("inspect", invalidPath, "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("PARSE_ERROR", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Inspect_JsonFormat_ReturnsDocumentInfo()
    {
        var docPath = CreateTestDocument("my-doc");

        var (exitCode, output, _) = RunCommand("inspect", docPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.Equal("my-doc", data.GetProperty("name").GetString());
        Assert.True(data.TryGetProperty("canvas", out _));
        Assert.True(data.TryGetProperty("nodeCount", out _));
    }

    [Fact]
    public void Inspect_TreeFlag_ShowsNodeHierarchy()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("inspect", docPath, "--tree");

        Assert.Equal(0, exitCode);
        Assert.Contains("root", output);
    }
}
