using System.Text.Json;
using FTPlus.AgentGraphicDesign.TestFixtures;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// Feature 002 T008: the <c>bounds</c>, <c>lint</c>, and <c>contact-sheet</c> commands expose the
/// same font options as <c>export</c> so measured/linted/composited text resolves fonts exactly
/// as a render would.
/// </summary>
public class FontPolicyCommandTests : CommandTestBase
{
    private static double TextNodeWidth(string json)
    {
        var nodes = JsonDocument.Parse(json).RootElement.GetProperty("data").GetProperty("nodes");
        foreach (var node in nodes.EnumerateArray())
        {
            if (string.Equals(node.GetProperty("type").GetString(), "text", StringComparison.OrdinalIgnoreCase))
                return node.GetProperty("local").GetProperty("width").GetDouble();
        }

        throw new Xunit.Sdk.XunitException("No text node in bounds output");
    }

    [Fact]
    public void Bounds_FontDirectory_ChangesMeasuredWidth()
    {
        var fontDirectory = PortableTestFonts.VerifiedDirectory();
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", PortableTestFonts.WidthProbe, "--font-family", PortableTestFonts.SansFamily, "--font-size", "24").ExitCode);

        var (substitutedExit, substituted, _) = RunCommand("bounds", docPath, "--no-system-fonts", "-f", "json");
        var (resolvedExit, resolved, _) = RunCommand("bounds", docPath, "--font-dir", fontDirectory, "--no-system-fonts", "-f", "json");

        Assert.Equal(0, substitutedExit);
        Assert.Equal(0, resolvedExit);
        Assert.True(Math.Abs(TextNodeWidth(resolved) - TextNodeWidth(substituted)) > 1.0,
            "Noto Sans and the bundled substitute should measure to different widths");
    }

    [Fact]
    public void Bounds_MissingFontDir_ReturnsInvalidArguments()
    {
        var docPath = CreateTestDocument();
        var (exitCode, output, _) = RunCommand("bounds", docPath, "--font-dir", Path.Combine(TestDir, "no-such-dir"), "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.Equal("INVALID_ARGUMENTS", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Lint_AcceptsFontFlags()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label").ExitCode);

        var (exitCode, _, _) = RunCommand("lint", docPath, "--no-system-fonts", "--strict-fonts", "-f", "json");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ContactSheet_StrictFonts_FailsWhenFamilyCannotResolve()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label", "--font-family", "No Such Family 9Z").ExitCode);

        var lenient = Path.Combine(TestDir, "sheet-lenient.png");
        Assert.Equal(0, RunCommand("contact-sheet", docPath, "-o", lenient, "-f", "json").ExitCode);
        Assert.True(File.Exists(lenient));

        var strict = Path.Combine(TestDir, "sheet-strict.png");
        var (exitCode, output, _) = RunCommand(
            "contact-sheet", docPath, "-o", strict, "--no-system-fonts", "--strict-fonts", "-f", "json");

        Assert.NotEqual(0, exitCode);
        Assert.False(File.Exists(strict));
        Assert.Equal("FONT_NOT_FOUND", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
