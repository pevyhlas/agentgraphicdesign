using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class DeleteCommandTests : CommandTestBase
{
    [Fact]
    public void Delete_ExistingNode_ReturnsZeroAndDeletes()
    {
        var docPath = CreateTestDocument();
        var (_, addOutput, _) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (exitCode, output, _) = RunCommand("delete", docPath, nodeId, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(nodeId, result.RootElement.GetProperty("data").GetProperty("deleted").GetString());
    }

    [Fact]
    public void Delete_VerifyNodeRemoved_ListShowsFewerNodes()
    {
        var docPath = CreateTestDocument();
        RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50");
        var (_, addOutput, _) = RunCommand("add", docPath, "ellipse", "--width", "80", "--height", "80", "-f", "json");
        var nodeId = JsonDocument.Parse(addOutput).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;

        var (_, listBefore, _) = RunCommand("list", docPath, "-f", "json");
        var beforeCount = JsonDocument.Parse(listBefore).RootElement.GetProperty("data").GetProperty("nodes").GetArrayLength();

        RunCommand("delete", docPath, nodeId);

        var (_, listAfter, _) = RunCommand("list", docPath, "-f", "json");
        var afterCount = JsonDocument.Parse(listAfter).RootElement.GetProperty("data").GetProperty("nodes").GetArrayLength();

        Assert.Equal(beforeCount - 1, afterCount);
    }

    [Fact]
    public void Delete_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("delete", "/nonexistent/path.agd.json", "11111111-1111-1111-1111-111111111111", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Delete_InvalidNodeId_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("delete", docPath, "not-a-guid", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("NODE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Delete_NodeNotFound_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("delete", docPath, "99999999-9999-9999-9999-999999999999", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("NODE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Delete_CannotDeleteRoot_ReturnsOne()
    {
        var docPath = CreateTestDocument();
        var (_, inspectOutput, _) = RunCommand("inspect", docPath, "-f", "json");
        var rootId = JsonDocument.Parse(inspectOutput).RootElement.GetProperty("data").GetProperty("nodes").GetProperty("id").GetString()!;

        var (exitCode, output, _) = RunCommand("delete", docPath, rootId, "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
    }
}
