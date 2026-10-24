using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Tests.Characterization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// Feature 012 T001 (AC-09 baseline per surface, plan D25/D29): today's `agd contact-sheet -f json`
/// stdout, exit code and PNG hash for the shared legacy fixtures, plus today's refusal of 257
/// references, recorded before the S05 cutover. PNG hashes and the PNG <c>size</c> are compared on
/// Linux x64 only; everything else on every platform.
/// </summary>
public sealed class LegacyContactSheetCharacterizationTests : CommandTestBase
{
    private const string Surface = "cli";

    public LegacyContactSheetCharacterizationTests()
    {
        LegacyContactSheetFixture.CopyInputs(TestDir);
    }

    [Fact]
    public void LegacyContactSheet_JsonOutputMatchesThePreChangeCharacterization()
    {
        var actual = new JsonObject();
        foreach (var sheet in LegacyContactSheetFixture.Cases)
            actual[sheet.Name] = Run(sheet);
        actual["refs-257"] = RunOverCap();

        if (LegacyContactSheetFixture.TryRecord(Surface, actual))
            return;

        var golden = LegacyContactSheetFixture.LoadGolden(Surface);
        foreach (var (name, value) in actual)
            LegacyContactSheetFixture.AssertCase(golden, name, value!.AsObject(), "pngSha256");
    }

    private JsonObject Run(LegacySheetCase sheet)
    {
        var output = $"{sheet.Name}.png";
        var args = new List<string> { "contact-sheet" };
        args.AddRange(sheet.Documents.Select(name => name + ".agd.json"));
        args.AddRange(["-o", output, "--cell", sheet.Cell.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "json"]);
        if (sheet.Columns is { } columns)
            args.AddRange(["--columns", columns.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (sheet.Gap is { } gap)
            args.AddRange(["--gap", gap.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (sheet.Background is { } background)
            args.AddRange(["--background", background]);
        if (sheet.Labels)
            args.Add("--labels");
        if (sheet.FontDirectory)
            args.AddRange(["--font-dir", LegacyContactSheetFixture.FontDirectoryName]);

        var (exitCode, stdout, stderr) = RunCommand([.. args]);
        Assert.True(exitCode == 0, $"{sheet.Name}: exit {exitCode}\n{stdout}\n{stderr}");
        return new JsonObject
        {
            ["exitCode"] = exitCode,
            ["stdout"] = Normalize(stdout),
            ["pngSha256"] = LegacyContactSheetFixture.Sha256(File.ReadAllBytes(Path.Combine(TestDir, output)))
        };
    }

    private JsonObject RunOverCap()
    {
        // Today the CLI refuses before reading any file, so the 257 documents need not exist.
        var args = new List<string> { "contact-sheet" };
        args.AddRange(Enumerable.Range(0, LegacyContactSheetFixture.OverCapReferenceCount).Select(i => $"d{i:D3}.agd.json"));
        args.AddRange(["-o", "over.png", "-f", "json"]);
        var (exitCode, stdout, stderr) = RunCommand([.. args]);
        Assert.False(File.Exists(Path.Combine(TestDir, "over.png")));
        return new JsonObject
        {
            ["exitCode"] = exitCode,
            ["stdout"] = Normalize(stdout),
            ["stderr"] = Normalize(stderr)
        };
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
