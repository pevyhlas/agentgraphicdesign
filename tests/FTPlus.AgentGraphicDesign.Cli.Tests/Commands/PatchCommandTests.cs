using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class PatchCommandTests : CommandTestBase
{
    [Fact]
    public void Patch_FromFile_JsonOutputReportsAppliedRevisionAndCreatedIds()
    {
        var docPath = CreateTestDocument();
        var nodeId = AddRectangle(docPath);
        var opsPath = WriteOps("ops.json", $$"""
            [
              { "op": "set", "nodeId": "{{nodeId}}", "path": "/style/fill/color", "value": "#ff0000" },
              { "op": "addNode", "node": { "type": "ellipse", "width": 10, "height": 10 } },
              { "op": "setCanvas", "width": 640 }
            ]
            """);

        var (exitCode, output, _) = RunCommand("patch", docPath, "--file", opsPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output).RootElement;
        Assert.True(result.GetProperty("ok").GetBoolean());
        var data = result.GetProperty("data");
        Assert.Equal(3, data.GetProperty("applied").GetInt32());
        Assert.Equal(3, data.GetProperty("revn").GetInt64());
        var createdId = data.GetProperty("createdIds").GetProperty("1").GetString();
        Assert.True(Guid.TryParse(createdId, out _));
        Assert.Empty(data.GetProperty("warnings").EnumerateArray());

        var document = DocumentSerializer.Deserialize(File.ReadAllText(docPath))!;
        Assert.Equal(3, document.Revn);
        Assert.Equal(640, document.Canvas.Width);
        Assert.Equal(2, document.Root.Children.Count);
        Assert.Equal("#ff0000", document.Root.Children[0].Style!.Fill!.Color);
    }

    [Fact]
    public void Patch_InlineOps_TextOutputListsCreatedIds()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand(
            "patch", docPath, "--ops", """[{ "op": "addNode", "node": { "type": "rectangle" } }]""");

        Assert.Equal(0, exitCode);
        Assert.Contains("Applied 1 operation(s); revision 2", output);
        Assert.Contains("created by operation 0:", output);
    }

    [Fact]
    public void Patch_FromStdin_Works()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommandWithInput(
            """[{ "op": "setCanvas", "height": 99 }]""",
            "patch", docPath, "--stdin", "-f", "json");

        Assert.Equal(0, exitCode);
        Assert.True(JsonDocument.Parse(output).RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(99, DocumentSerializer.Deserialize(File.ReadAllText(docPath))!.Canvas.Height);
    }

    [Fact]
    public void Patch_FailingOperation_ReportsIndexInJsonAndLeavesFileUntouched()
    {
        var docPath = CreateTestDocument();
        var nodeId = AddRectangle(docPath);
        var before = File.ReadAllBytes(docPath);

        var (exitCode, output, _) = RunCommand("patch", docPath, "--ops", $$"""
            [
              { "op": "set", "nodeId": "{{nodeId}}", "path": "/name", "value": "renamed" },
              { "op": "deleteNode", "nodeId": "{{Guid.NewGuid()}}" }
            ]
            """, "-f", "json");

        Assert.Equal(1, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("NODE_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal(1, error.GetProperty("operationIndex").GetInt32());
        Assert.StartsWith("Operation 1 (deleteNode):", error.GetProperty("message").GetString());
        Assert.Equal(before, File.ReadAllBytes(docPath));
    }

    [Fact]
    public void Patch_FailingOperation_TextOutputShowsIndex()
    {
        var docPath = CreateTestDocument();
        var nodeId = AddRectangle(docPath);

        var (exitCode, output, _) = RunCommand("patch", docPath, "--ops", $$"""
            [{ "op": "set", "nodeId": "{{nodeId}}", "path": "/width", "value": -1 }]
            """);

        Assert.Equal(1, exitCode);
        Assert.Contains("[RECT001]", output);
        Assert.Contains("at operation: 0", output);
        Assert.Contains("at: root.children[0].width", output);
    }

    [Fact]
    public void Patch_MalformedOperations_ReturnsInvalidArgumentsWithIndex()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("patch", docPath, "--ops", """[{ "op": "explode" }]""", "-f", "json");

        Assert.Equal(4, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Equal(0, error.GetProperty("operationIndex").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("--file ops.json --ops []")]
    [InlineData("--stdin --ops []")]
    public void Patch_RequiresExactlyOneSource(string extra)
    {
        var docPath = CreateTestDocument();
        var args = new List<string> { "patch", docPath, "-f", "json" };
        args.AddRange(extra.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var (exitCode, output, _) = RunCommand([.. args]);

        Assert.Equal(4, exitCode);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Patch_MissingOperationsFile_ReturnsFileNotFound()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("patch", docPath, "--file", "missing.json", "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.Equal("FILE_NOT_FOUND", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Patch_ExpectedRevisionMismatch_ReturnsRevisionConflict()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand(
            "patch", docPath, "--ops", """[{ "op": "setCanvas", "width": 10 }]""", "--expected-revn", "9", "-f", "json");

        Assert.Equal(1, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("REVISION_CONFLICT", error.GetProperty("code").GetString());
        Assert.False(error.TryGetProperty("operationIndex", out _));
    }

    [Fact]
    public void Patch_DocumentNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand(
            "patch", "/nonexistent/path.agd.json", "--ops", """[{ "op": "setCanvas", "width": 10 }]""", "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.Equal("DOCUMENT_NOT_FOUND", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private string AddRectangle(string docPath)
    {
        var (exitCode, output, stderr) = RunCommand("add", docPath, "rectangle", "--width", "50", "--height", "50", "-f", "json");
        Assert.True(exitCode == 0, stderr);
        return JsonDocument.Parse(output).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;
    }

    private string WriteOps(string name, string json)
    {
        var path = Path.Combine(TestDir, name);
        File.WriteAllText(path, json);
        return path;
    }
}
