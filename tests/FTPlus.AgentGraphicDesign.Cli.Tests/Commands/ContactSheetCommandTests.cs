using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// CLI plumbing for `agd contact-sheet`: it composes several documents into one
/// PNG file and fails cleanly when an input is missing.
/// </summary>
public class ContactSheetCommandTests : CommandTestBase
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void ContactSheet_TwoDocuments_WritesPng()
    {
        CreateTestDocument("a", 100, 100);
        CreateTestDocument("b", 100, 100);
        var outPath = Path.Combine(TestDir, "sheet.png");

        var (exitCode, output, stderr) = RunCommand(
            "contact-sheet", "a.agd.json", "b.agd.json", "-o", "sheet.png", "--cell", "64");

        Assert.True(exitCode == 0, $"exit {exitCode}: {stderr}\n{output}");
        Assert.True(File.Exists(outPath));
        var bytes = File.ReadAllBytes(outPath);
        Assert.True(bytes.Length > PngMagic.Length);
        Assert.Equal(PngMagic, bytes.Take(PngMagic.Length));
    }

    [Fact]
    public void ContactSheet_MissingInput_ReturnsError()
    {
        CreateTestDocument("a", 100, 100);

        var (exitCode, _, _) = RunCommand(
            "contact-sheet", "a.agd.json", "missing.agd.json", "-o", "sheet.png");

        Assert.NotEqual(0, exitCode);
    }
}
