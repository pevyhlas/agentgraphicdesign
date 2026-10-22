using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class ListCommandTests : CommandTestBase
{
    [Fact]
    public void List_ValidDocument_ReturnsZero()
    {
        var docPath = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("list", docPath);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void List_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("list", "/nonexistent/path.agd.json", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void List_JsonFormat_ReturnsNodeArray()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("list", docPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("nodes", out var nodes));
        Assert.True(nodes.GetArrayLength() >= 1);
    }

    [Fact]
    public void List_TreeFlag_ShowsHierarchy()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("list", docPath, "--tree");

        Assert.Equal(0, exitCode);
        Assert.Contains("root", output);
    }

    [Fact]
    public void List_WithChildren_ShowsAllNodes()
    {
        var docPath = CreateTestDocument();
        RunCommand("add", docPath, "rectangle", "--width", "100", "--height", "50");
        RunCommand("add", docPath, "ellipse", "--width", "80", "--height", "80");

        var (exitCode, output, _) = RunCommand("list", docPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var nodes = result.RootElement.GetProperty("data").GetProperty("nodes");
        Assert.Equal(3, nodes.GetArrayLength());
    }

    [Fact]
    public void List_NoArgument_ListsDocumentsInDirectory()
    {
        CreateTestDocument("doc1");
        CreateTestDocument("doc2");

        var (exitCode, output, _) = RunCommand("list", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("documents", out var docs));
        Assert.True(docs.GetArrayLength() >= 2);
    }

    [Fact]
    public void List_NoDocuments_ReturnsEmptyArray()
    {
        var (exitCode, output, _) = RunCommand("list", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("documents", out var docs));
        Assert.True(data.TryGetProperty("count", out var count));
        Assert.Equal(0, count.GetInt32());
    }
}
