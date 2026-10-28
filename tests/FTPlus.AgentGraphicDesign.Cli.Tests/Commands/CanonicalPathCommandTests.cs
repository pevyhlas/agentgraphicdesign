using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Paths;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public sealed class CanonicalPathCommandTests : CommandTestBase
{
    [Theory]
    [InlineData("export", "svg")]
    [InlineData("sprite", "svg")]
    [InlineData("contact-sheet", "png")]
    public void MixedRootSpellings_WriteAndReportTheCanonicalDestination(string command, string extension)
    {
        var input = CreateTestDocument("input", 16, 16);
        var output = Path.Combine(TestDir, "result." + extension);
        var (exit, text, stderr) = RunCommandIn(PlatformPaths.FullPath(TestDir),
            command, input, "-o", output, "-f", "json");
        Assert.True(exit == 0, $"{exit}: {stderr}\n{text}");
        using var json = JsonDocument.Parse(text);
        Assert.Equal("result." + extension, json.RootElement.GetProperty("data").GetProperty("path").GetString());
        Assert.True(new FileInfo(PlatformPaths.FullPath(output)).Length > 0);
        Assert.Single(Directory.EnumerateFiles(TestDir, "result.*"));
    }
}
