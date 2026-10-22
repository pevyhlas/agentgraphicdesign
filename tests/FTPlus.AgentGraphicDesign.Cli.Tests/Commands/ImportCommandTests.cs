using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class ImportCommandTests : CommandTestBase
{
    [Fact]
    public void Import_ValidSvg_ReturnsZeroAndCreatesDocument()
    {
        var svgPath = CreateTestSvg();
        var outputPath = Path.Combine(TestDir, "output.agd.json");

        var (exitCode, _, _) = RunCommand("import", svgPath, "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        var json = File.ReadAllText(outputPath);
        Assert.Contains("\"$schema\"", json);
        Assert.Contains("\"root\"", json);
    }

    [Fact]
    public void Import_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("import", "/nonexistent/path.svg", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("FILE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Import_UnsupportedFormat_ReturnsInvalidArguments()
    {
        var pngPath = Path.Combine(TestDir, "test.png");
        File.WriteAllText(pngPath, "not a png");

        var (exitCode, output, _) = RunCommand("import", pngPath, "-f", "json");

        Assert.Equal(4, exitCode); // InvalidArguments
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("UNSUPPORTED_FORMAT", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Import_WithName_SetsDocumentName()
    {
        var svgPath = CreateTestSvg();
        var outputPath = Path.Combine(TestDir, "named.agd.json");

        var (exitCode, output, _) = RunCommand("import", svgPath, "-o", outputPath, "-n", "my-logo", "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.Equal("my-logo", result.RootElement.GetProperty("data").GetProperty("documentName").GetString());
    }

    [Fact]
    public void Import_DefaultOutput_DerivesFromInput()
    {
        var svgPath = CreateTestSvg("test-icon");
        var expectedOutput = Path.Combine(TestDir, "test-icon.agd.json");

        var (exitCode, _, _) = RunCommand("import", svgPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(expectedOutput));
    }

    [Fact]
    public void Import_JsonFormat_ReturnsStructuredOutput()
    {
        var svgPath = CreateTestSvg();
        var outputPath = Path.Combine(TestDir, "json-import.agd.json");

        var (exitCode, output, _) = RunCommand("import", svgPath, "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("nodeCount").GetInt32() > 0);
        Assert.True(data.GetProperty("canvas").GetProperty("width").GetDouble() > 0);
    }

    [Fact]
    public void Import_WithUnsupportedFeatures_ReturnsWarnings()
    {
        var svgPath = CreateSvgWithUnsupportedFeatures();
        var outputPath = Path.Combine(TestDir, "warnings.agd.json");

        var (exitCode, output, _) = RunCommand("import", svgPath, "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        var data = result.RootElement.GetProperty("data");
        var warnings = data.GetProperty("warnings");
        Assert.True(warnings.GetArrayLength() > 0);
    }

    [Fact]
    public void Import_InvalidSvg_ReturnsOne()
    {
        var invalidPath = CreateInvalidSvg();

        var (exitCode, output, _) = RunCommand("import", invalidPath, "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
    }

    private string CreateTestSvg(string name = "test")
    {
        var path = Path.Combine(TestDir, $"{name}.svg");
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <rect x="10" y="10" width="80" height="80" fill="#ff0000"/>
            </svg>
            """;
        File.WriteAllText(path, svg);
        return path;
    }

    private string CreateSvgWithUnsupportedFeatures()
    {
        var path = Path.Combine(TestDir, "unsupported.svg");
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <defs>
                    <filter id="blur">
                        <feGaussianBlur stdDeviation="5"/>
                    </filter>
                </defs>
                <rect x="10" y="10" width="80" height="80" fill="#ff0000"/>
            </svg>
            """;
        File.WriteAllText(path, svg);
        return path;
    }

    private string CreateInvalidSvg()
    {
        var path = Path.Combine(TestDir, "invalid.svg");
        File.WriteAllText(path, "<svg><not closed");
        return path;
    }
}
