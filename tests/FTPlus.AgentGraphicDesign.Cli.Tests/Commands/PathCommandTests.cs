using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class PathCommandTests : CommandTestBase
{
    private const string SameWindingRing = "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z";
    private const string OuterSquare = "M0,0 L100,0 L100,100 L0,100 Z";
    private const string InnerSquare = "M25,25 L75,25 L75,75 L25,75 Z";

    [Fact]
    public void PathBool_DefaultFillRule_IsNonZero()
    {
        var (exitCode, output, _) = RunCommand("path", "bool", "intersect", SameWindingRing, OuterSquare, "-f", "json");

        Assert.Equal(0, exitCode);
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal("nonzero", data.GetProperty("fillRule").GetString());
        Assert.Equal(1, CountSubpaths(data.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathBool_EvenOdd_KeepsHole()
    {
        var (exitCode, output, _) = RunCommand(
            "path", "bool", "intersect", SameWindingRing, OuterSquare, "--fill-rule", "evenodd", "-f", "json");

        Assert.Equal(0, exitCode);
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal("evenodd", data.GetProperty("fillRule").GetString());
        Assert.Equal(2, CountSubpaths(data.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathBool_UnknownFillRule_ReturnsInvalidFillRule()
    {
        var (exitCode, output, _) = RunCommand(
            "path", "bool", "union", OuterSquare, OuterSquare, "--fill-rule", "even-odd", "-f", "json");

        Assert.Equal(4, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_FILL_RULE", error.GetProperty("code").GetString());
        Assert.Contains("nonzero, evenodd", error.GetProperty("message").GetString());
    }

    [Fact]
    public void PathOffset_EvenOdd_OffsetsHoleInward()
    {
        var evenOdd = RunCommand("path", "offset", SameWindingRing, "5", "--fill-rule", "evenodd", "-f", "json");
        var nonZero = RunCommand("path", "offset", SameWindingRing, "5", "-f", "json");

        Assert.Equal(0, evenOdd.ExitCode);
        Assert.Equal(0, nonZero.ExitCode);
        var evenOddData = JsonDocument.Parse(evenOdd.Output).RootElement.GetProperty("data");
        var nonZeroData = JsonDocument.Parse(nonZero.Output).RootElement.GetProperty("data");
        Assert.Equal("evenodd", evenOddData.GetProperty("fillRule").GetString());
        Assert.Equal("nonzero", nonZeroData.GetProperty("fillRule").GetString());
        Assert.Equal(2, CountSubpaths(evenOddData.GetProperty("pathData").GetString()!));
        Assert.Equal(1, CountSubpaths(nonZeroData.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathOffset_UnknownFillRule_ReturnsInvalidFillRule()
    {
        var (exitCode, output, _) = RunCommand("path", "offset", OuterSquare, "5", "--fill-rule", "winding", "-f", "json");

        Assert.Equal(4, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_FILL_RULE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void PathUnion_EvenOdd_TurnsNestedSquareIntoHole()
    {
        var evenOdd = RunCommand("path", "union", OuterSquare, InnerSquare, "--fill-rule", "evenodd", "-f", "json");
        var nonZero = RunCommand("path", "union", OuterSquare, InnerSquare, "-f", "json");

        Assert.Equal(0, evenOdd.ExitCode);
        Assert.Equal(0, nonZero.ExitCode);
        var evenOddData = JsonDocument.Parse(evenOdd.Output).RootElement.GetProperty("data");
        var nonZeroData = JsonDocument.Parse(nonZero.Output).RootElement.GetProperty("data");
        Assert.Equal("evenodd", evenOddData.GetProperty("fillRule").GetString());
        Assert.Equal(2, CountSubpaths(evenOddData.GetProperty("pathData").GetString()!));
        Assert.Equal(1, CountSubpaths(nonZeroData.GetProperty("pathData").GetString()!));
    }

    [Fact]
    public void PathUnion_UnknownFillRule_ReturnsInvalidFillRule()
    {
        var (exitCode, output, _) = RunCommand("path", "union", OuterSquare, "--fill-rule", "", "-f", "json");

        Assert.Equal(4, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_FILL_RULE", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("bool", 25, 50)]
    [InlineData("offset", -5, 110)]
    [InlineData("union", 0, 100)]
    public void PathCommands_CompactRelativeArcs_ReturnDeterministicGeometry(string command, double edge, double size)
    {
        const string circle = "M100,50 a50,50 0 01-100,0 50,50 0 01100,0Z";
        string[] arguments = command switch
        {
            "bool" => ["path", command, "intersect", circle, InnerSquare, "-f", "json"],
            "offset" => ["path", command, circle, "5", "-f", "json"],
            _ => ["path", command, circle, InnerSquare, "-f", "json"]
        };

        var (exitCode, output, _) = RunCommand(arguments);

        Assert.Equal(0, exitCode);
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal("nonzero", data.GetProperty("fillRule").GetString());
        var path = data.GetProperty("pathData").GetString()!;
        Assert.Equal(1, CountSubpaths(path));
        var bounds = new PathOperationsService().GetBounds(path);
        Assert.True(bounds.Success, bounds.Error);
        Assert.InRange(bounds.Bounds!.Value.X, edge - 0.05, edge + 0.05);
        Assert.InRange(bounds.Bounds.Value.Y, edge - 0.05, edge + 0.05);
        Assert.InRange(bounds.Bounds.Value.Width, size - 0.05, size + 0.05);
        Assert.InRange(bounds.Bounds.Value.Height, size - 0.05, size + 0.05);
        Assert.Equal(output, RunCommand(arguments).Output);
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("offset")]
    [InlineData("union")]
    public void PathCommands_InvalidArcFlag_ReturnsStructuredFailureIncludingZeroOffset(string command)
    {
        const string malformed = "M0,0 A10,10 0 02 20,20Z";
        string[] arguments = command switch
        {
            "bool" => ["path", command, "union", malformed, OuterSquare, "-f", "json"],
            "offset" => ["path", command, malformed, "0", "-f", "json"],
            _ => ["path", command, malformed, "-f", "json"]
        };

        var (exitCode, output, _) = RunCommand(arguments);

        Assert.Equal(1, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("PATH_OP_FAILED", error.GetProperty("code").GetString());
        Assert.Contains("Arc flag must be '0' or '1'", error.GetProperty("message").GetString());
    }

    private static int CountSubpaths(string pathData) => pathData.Count(c => c == 'M');
}
