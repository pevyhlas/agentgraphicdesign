using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// CLI plumbing for `agd lint`: JSON envelope, the advisory exit code, --fail-on,
/// and profile validation. Lint never blocks unless --fail-on names a severity.
/// </summary>
public class LintCommandTests : CommandTestBase
{
    [Fact]
    public void Lint_Json_ReportsContrastWarning()
    {
        var doc = CreateTestDocument();
        AddWhiteText(doc);

        var (exitCode, output, _) = RunCommand("lint", doc, "-f", "json");

        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        var codes = data.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString()).ToList();
        Assert.Contains("LINT001", codes);
        Assert.False(data.GetProperty("gridChecked").GetBoolean());
    }

    [Fact]
    public void Lint_FailOnWarning_ReturnsLintFailedExitCode()
    {
        var doc = CreateTestDocument();
        AddWhiteText(doc);

        var (exitCode, _, _) = RunCommand("lint", doc, "--fail-on", "warning");

        Assert.Equal(5, exitCode);
    }

    [Fact]
    public void Lint_CleanDocument_FailOnWarning_ExitsZero()
    {
        var doc = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("lint", doc, "--fail-on", "warning");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Lint_UnknownProfile_ReturnsInvalidArguments()
    {
        var doc = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("lint", doc, "--profile", "bogus");

        Assert.Equal(4, exitCode);
    }

    private void AddWhiteText(string doc)
    {
        var (exitCode, output, stderr) = RunCommand(
            "add", doc, "text", "-f", "json", "--text", "Hello", "--font-size", "16", "--fill", "#ffffff");
        Assert.True(exitCode == 0, $"add text failed with exit {exitCode}: {stderr}\n{output}");
    }
}
