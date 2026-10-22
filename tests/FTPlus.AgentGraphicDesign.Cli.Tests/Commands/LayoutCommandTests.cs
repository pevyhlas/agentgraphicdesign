using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// CLI plumbing for bounds, align, distribute, and fit: argument validation,
/// JSON envelopes, exit codes, and expected-revision handling.
/// </summary>
public class LayoutCommandTests : CommandTestBase
{
    [Fact]
    public void Bounds_Json_ReportsRootAndChildren()
    {
        var doc = CreateTestDocument();
        var (rectId, _) = AddNode(doc, "rectangle", "--width", "100", "--height", "50", "-x", "10", "-y", "10");

        var (exitCode, output, _) = RunCommand("bounds", doc, "-f", "json");

        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("nodeCount").GetInt32());
        var rect = data.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == rectId);
        Assert.Equal("rectangle", rect.GetProperty("type").GetString());
        Assert.Equal(0, rect.GetProperty("local").GetProperty("x").GetDouble());
        Assert.Equal(10, rect.GetProperty("world").GetProperty("x").GetDouble());
        Assert.Equal(100, rect.GetProperty("world").GetProperty("width").GetDouble());
        Assert.False(rect.GetProperty("clipped").GetBoolean());
    }

    [Fact]
    public void Bounds_SingleNode_TextOutputShowsBoxes()
    {
        var doc = CreateTestDocument();
        var (rectId, _) = AddNode(doc, "rectangle", "--width", "100", "--height", "50", "-x", "10", "-y", "20");

        var (exitCode, output, _) = RunCommand("bounds", doc, "--node", rectId);

        Assert.Equal(0, exitCode);
        Assert.Contains("rectangle", output);
        Assert.Contains("10,20 100x50", output);
    }

    [Fact]
    public void Bounds_TextNode_IsMeasuredByRegisteredMeasurer()
    {
        var doc = CreateTestDocument();
        var (textId, _) = AddNode(doc, "text", "--text", "Hello", "--font-size", "20");

        var (exitCode, output, _) = RunCommand("bounds", doc, "--node", textId, "-f", "json");

        Assert.Equal(0, exitCode);
        var node = JsonDocument.Parse(output).RootElement.GetProperty("data").GetProperty("nodes")[0];
        Assert.True(node.GetProperty("measured").GetBoolean());
        Assert.True(node.GetProperty("world").GetProperty("width").GetDouble() > 0);
    }

    [Fact]
    public void Bounds_UnknownNode_ReturnsError()
    {
        var doc = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("bounds", doc, "--node", Guid.NewGuid().ToString(), "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.Equal("NODE_NOT_FOUND", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Align_Left_MovesNodesAndIncrementsRevision()
    {
        var doc = CreateTestDocument();
        var (a, _) = AddNode(doc, "rectangle", "--width", "100", "--height", "50", "-x", "10", "-y", "10");
        var (b, revn) = AddNode(doc, "rectangle", "--width", "40", "--height", "20", "-x", "200", "-y", "100");

        var (exitCode, output, _) = RunCommand("align", doc, "--nodes", $"{a},{b}", "--mode", "left", "-f", "json");

        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal("left", data.GetProperty("mode").GetString());
        Assert.Equal("selection", data.GetProperty("relativeTo").GetString());
        Assert.Equal(revn + 1, data.GetProperty("documentRevn").GetInt64());
        var moved = data.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == b);
        Assert.Equal(200, moved.GetProperty("previousX").GetDouble());
        Assert.Equal(10, moved.GetProperty("x").GetDouble());

        var (_, boundsOutput, _) = RunCommand("bounds", doc, "--node", b, "-f", "json");
        var world = JsonDocument.Parse(boundsOutput).RootElement.GetProperty("data").GetProperty("nodes")[0].GetProperty("world");
        Assert.Equal(10, world.GetProperty("x").GetDouble());
    }

    [Fact]
    public void Align_JsonArrayNodes_AndParentReference()
    {
        var doc = CreateTestDocument(width: 800, height: 600);
        var (a, _) = AddNode(doc, "rectangle", "--width", "100", "--height", "50", "-x", "10", "-y", "10");

        var (exitCode, output, _) = RunCommand("align", doc, "--nodes", $"[\"{a}\"]", "--mode", "centerX", "--relative-to", "parent", "-f", "json");

        Assert.Equal(0, exitCode);
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal("parent", data.GetProperty("relativeTo").GetString());
        Assert.Equal(350, data.GetProperty("nodes")[0].GetProperty("x").GetDouble());
    }

    [Fact]
    public void Align_MissingRequiredOptions_ExitsInvalidArguments()
    {
        var doc = CreateTestDocument();

        var (noNodes, output1, _) = RunCommand("align", doc, "--mode", "left", "-f", "json");
        var (noMode, output2, _) = RunCommand("align", doc, "--nodes", Guid.NewGuid().ToString(), "-f", "json");
        var (badMode, output3, _) = RunCommand("align", doc, "--nodes", Guid.NewGuid().ToString(), "--mode", "diagonal", "-f", "json");

        Assert.Equal(4, noNodes);
        Assert.Equal(4, noMode);
        Assert.Equal(4, badMode);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output1).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output2).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output3).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Align_StaleExpectedRevision_ReturnsRevisionConflict()
    {
        var doc = CreateTestDocument();
        var (a, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "10");
        var (b, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "50");

        var (exitCode, output, _) = RunCommand("align", doc, "--nodes", $"{a},{b}", "--mode", "left", "--expected-revn", "999", "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.Equal("REVISION_CONFLICT", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Distribute_Horizontal_DerivesGap()
    {
        var doc = CreateTestDocument();
        var (a, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "0");
        var (b, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "15");
        var (c, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "100");

        var (exitCode, output, _) = RunCommand("distribute", doc, "--nodes", $"{a},{b},{c}", "--axis", "horizontal", "-f", "json");

        Assert.Equal(0, exitCode);
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal("horizontal", data.GetProperty("axis").GetString());
        Assert.Equal("gap", data.GetProperty("mode").GetString());
        Assert.Equal(40, data.GetProperty("gap").GetDouble());
        var middle = data.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == b);
        Assert.Equal(50, middle.GetProperty("x").GetDouble());
    }

    [Fact]
    public void Distribute_ExplicitGapCenters_Works()
    {
        var doc = CreateTestDocument();
        var (a, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-y", "0");
        var (b, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-y", "90");

        var (exitCode, output, _) = RunCommand("distribute", doc, "--nodes", $"{a},{b}", "--axis", "vertical", "--mode", "centers", "--gap", "25", "-f", "json");

        Assert.Equal(0, exitCode);
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal("centers", data.GetProperty("mode").GetString());
        var moved = data.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == b);
        Assert.Equal(25, moved.GetProperty("y").GetDouble());
    }

    [Fact]
    public void Distribute_TwoNodesWithoutGap_ReturnsError()
    {
        var doc = CreateTestDocument();
        var (a, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "0");
        var (b, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10", "-x", "90");

        var (exitCode, output, _) = RunCommand("distribute", doc, "--nodes", $"{a},{b}", "--axis", "horizontal", "-f", "json");
        var (missingAxis, output2, _) = RunCommand("distribute", doc, "--nodes", $"{a},{b}", "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(4, missingAxis);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output2).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Fit_Frame_WithPadding_ResizesAndShiftsChildren()
    {
        var doc = CreateTestDocument();
        var (frame, _) = AddNode(doc, "frame", "--width", "50", "--height", "50", "-x", "100", "-y", "100");
        var (childA, _) = AddNode(doc, "rectangle", "-p", frame, "--width", "30", "--height", "30", "-x", "5", "-y", "20");
        AddNode(doc, "rectangle", "-p", frame, "--width", "10", "--height", "10", "-x", "40", "-y", "40");

        var (exitCode, output, _) = RunCommand("fit", doc, "--frame", frame, "--padding", "5", "-f", "json");

        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal(frame, data.GetProperty("frameId").GetString());
        Assert.Equal(100, data.GetProperty("x").GetDouble());
        Assert.Equal(115, data.GetProperty("y").GetDouble());
        Assert.Equal(55, data.GetProperty("width").GetDouble());
        Assert.Equal(40, data.GetProperty("height").GetDouble());
        Assert.Equal(50, data.GetProperty("previous").GetProperty("width").GetDouble());
        var movedA = data.GetProperty("children").EnumerateArray().Single(n => n.GetProperty("id").GetString() == childA);
        Assert.Equal(5, movedA.GetProperty("x").GetDouble());
        Assert.Equal(5, movedA.GetProperty("y").GetDouble());
    }

    [Fact]
    public void Fit_InvalidInputs_ReturnErrors()
    {
        var doc = CreateTestDocument();
        var (rect, _) = AddNode(doc, "rectangle", "--width", "10", "--height", "10");

        var (missing, output1, _) = RunCommand("fit", doc, "-f", "json");
        var (notFrame, output2, _) = RunCommand("fit", doc, "--frame", rect, "-f", "json");
        var (badPadding, output3, _) = RunCommand("fit", doc, "--frame", rect, "--padding", "-3", "-f", "json");

        Assert.Equal(4, missing);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output1).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, notFrame);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output2).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(4, badPadding);
        Assert.Equal("INVALID_PROPERTY", JsonDocument.Parse(output3).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private (string NodeId, long Revn) AddNode(string doc, string type, params string[] extra)
    {
        string[] args = ["add", doc, type, "-f", "json", .. extra];
        var (exitCode, output, stderr) = RunCommand(args);
        Assert.True(exitCode == 0, $"add {type} failed with exit {exitCode}: {stderr}\n{output}");
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        return (data.GetProperty("nodeId").GetString()!, data.GetProperty("documentRevn").GetInt64());
    }
}
