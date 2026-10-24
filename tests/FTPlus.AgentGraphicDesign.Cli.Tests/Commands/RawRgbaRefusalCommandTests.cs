using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// Feature 012 AC-02 (FR-001): the internal Rgba8 buffer is never a CLI file format. `export`
/// refuses raw-RGBA format names with EXPORT_UNSUPPORTED (exit 4) and `contact-sheet` refuses a
/// non-PNG output with INVALID_ARGUMENTS (exit 4); neither writes a file.
/// </summary>
public sealed class RawRgbaRefusalCommandTests : CommandTestBase
{
    [Theory]
    [InlineData("rgba8")]
    [InlineData("Rgba8")]
    [InlineData("rgba")]
    [InlineData("raw")]
    public void Export_RawRgbaFormat_IsRefused(string type)
    {
        var document = CreateTestDocument("doc", 16, 16);
        var output = Path.Combine(TestDir, "doc.rgba");

        var (exitCode, stdout, _) = RunCommand("export", document, "-t", type, "-o", output, "-f", "json");

        Assert.Equal(4, exitCode);
        AssertError(stdout, "EXPORT_UNSUPPORTED");
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("sheet.rgba8")]
    [InlineData("sheet.rgba")]
    [InlineData("sheet.raw")]
    public void ContactSheet_RawRgbaOutput_IsRefused(string output)
    {
        CreateTestDocument("doc", 16, 16);

        var (exitCode, stdout, _) = RunCommand("contact-sheet", "doc.agd.json", "-o", output, "-f", "json");

        Assert.Equal(4, exitCode);
        AssertError(stdout, "INVALID_ARGUMENTS");
        Assert.False(File.Exists(Path.Combine(TestDir, output)));
    }

    private static void AssertError(string stdout, string code)
    {
        using var json = JsonDocument.Parse(stdout);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
