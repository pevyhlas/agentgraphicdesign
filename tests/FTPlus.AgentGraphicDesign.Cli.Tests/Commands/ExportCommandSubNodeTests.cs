using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// CLI surface for sub-node export (D-10): <c>agd export --node &lt;ID&gt;</c>.
/// </summary>
public class ExportCommandSubNodeTests : CommandTestBase
{
    [Theory]
    [InlineData("svg")]
    [InlineData("png")]
    public void ExportNodeReportsBoundsDiagnostics(string type)
    {
        var path = CreateTestDocument();
        var document = Core.Serialization.DocumentSerializer.Deserialize(File.ReadAllText(path))!;
        var text = new Core.Documents.Nodes.TextNode { Text = "Hello", TypographyTokenRef = "{missing}" };
        document.Root.Children = [text];
        File.WriteAllText(path, Core.Serialization.DocumentSerializer.Serialize(document));
        var before = File.ReadAllBytes(path);
        var outputPath = Path.Combine(TestDir, "diagnostic." + type);
        var result = RunCommand("export", path, "--node", text.Id.ToString(), "--type", type, "-o", outputPath, "-f", "json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains(json.RootElement.GetProperty("data").GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "BOUNDS_DIAGNOSTIC" && d.GetProperty("message").GetString()!.Contains("missing"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Export_WithNode_CropsSvgToSubtreeBounds()
    {
        var docPath = CreateTestDocument(width: 800, height: 600);
        Assert.Equal(0, RunCommand("add", docPath, "rectangle").ExitCode);
        var nodeId = FirstChildId(docPath);
        var outputPath = Path.Combine(TestDir, "node.svg");

        var (exitCode, _, stderr) = RunCommand("export", docPath, "--node", nodeId, "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath), stderr);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<svg", svg);
        Assert.Contains("viewBox=\"0 0 ", svg);
        // Cropped to the node, not the 800x600 canvas.
        Assert.DoesNotContain("viewBox=\"0 0 800 600\"", svg);
    }

    [Fact]
    public void Export_NodeWithSymbol_IsRejected()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "rectangle").ExitCode);
        var nodeId = FirstChildId(docPath);
        var outputPath = Path.Combine(TestDir, "node-symbol.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--node", nodeId, "--symbol", "icon", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_NodeInvalidGuid_IsRejected()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "node-badid.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--node", "not-a-guid", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_UnknownNode_ReturnsNodeNotFound()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "node-missing.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--node", Guid.NewGuid().ToString(), "-o", outputPath, "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("NODE_NOT_FOUND", error.GetProperty("code").GetString());
    }

    private static string FirstChildId(string docPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(docPath));
        return doc.RootElement.GetProperty("root").GetProperty("children")[0].GetProperty("id").GetString()!;
    }
}
