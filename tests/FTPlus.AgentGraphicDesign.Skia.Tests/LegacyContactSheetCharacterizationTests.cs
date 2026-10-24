using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Tests.Characterization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// Feature 012 T001 (AC-09 baseline, plan D3/D29/D32): today's legacy grid, recorded before any
/// compositor change. The PNG SHA-256 is compared on Linux x64 only (the platform the golden was
/// recorded on); the cell map is compared everywhere.
/// </summary>
public sealed class LegacyContactSheetCharacterizationTests : IDisposable
{
    private const string Surface = "skia";
    private readonly string _directory;

    public LegacyContactSheetCharacterizationTests()
    {
        _directory = Directory.CreateTempSubdirectory("agd-legacy-sheet-").FullName;
        LegacyContactSheetFixture.CopyInputs(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public void LegacyGrid_MatchesThePreChangeCharacterization()
    {
        var actual = new JsonObject();
        foreach (var sheet in LegacyContactSheetFixture.Cases)
            actual[sheet.Name] = Describe(Render(sheet));

        if (LegacyContactSheetFixture.TryRecord(Surface, actual))
            return;

        var golden = LegacyContactSheetFixture.LoadGolden(Surface);
        foreach (var sheet in LegacyContactSheetFixture.Cases)
            LegacyContactSheetFixture.AssertCase(golden, sheet.Name, actual[sheet.Name]!.AsObject(), "pngSha256");
    }

    [Fact]
    public void FontDirectorySheet_DiffersFromTheSameSheetWithoutIt()
    {
        // Sensitivity check for the AC-25 anchor: the directory's Inter-Bold face must change
        // the sheet bytes, so the anchor detects a font set that is not loaded.
        var withDirectory = Render(LegacyContactSheetFixture.Cases.Single(c => c.Name == "font-dir"));
        var without = Render(LegacyContactSheetFixture.Cases.Single(c => c.Name == "font-dir-control"));

        Assert.Equal(withDirectory.Width, without.Width);
        Assert.Equal(withDirectory.Height, without.Height);
        Assert.NotEqual(LegacyContactSheetFixture.Sha256(withDirectory.Data), LegacyContactSheetFixture.Sha256(without.Data));
    }

    [Fact]
    public void WideCanvases_RenderNarrowerThanTheirFittedBox()
    {
        // The D3 premise the golden pins: the legacy recipe re-derives the scale from the rounded box,
        // so these tiles render one pixel narrower than the box and are stretched into it.
        var renderer = new SkiaRenderer();
        AssertNarrower(renderer, "wide-10x3", 128, expectedBox: (128, 38), expectedRender: (127, 38));
        AssertNarrower(renderer, "wide-100x30", 64, expectedBox: (64, 19), expectedRender: (63, 19));
    }

    private static void AssertNarrower(SkiaRenderer renderer, string name, int cell, (int W, int H) expectedBox, (int W, int H) expectedRender)
    {
        var document = Load(LegacyContactSheetFixture.DocumentPath(name));
        var box = ContactSheetLayout.Fit(document.Canvas.Width, document.Canvas.Height, new PixelBox(0, 0, cell, cell));
        Assert.Equal(expectedBox, (box.Width, box.Height));
        var scale = Math.Min(box.Width / document.Canvas.Width, box.Height / document.Canvas.Height);
        var rendered = renderer.Render(document, new RenderOptions { Scale = scale });
        Assert.Equal(expectedRender, (rendered.Width, rendered.Height));
    }

    private ContactSheetResult Render(LegacySheetCase sheet)
    {
        var entries = sheet.Documents
            .Select(name => Load(Path.Combine(_directory, name + ".agd.json")))
            .Select(document => new ContactSheetEntry(document, document.Name))
            .ToList();
        var options = new ContactSheetOptions
        {
            Columns = sheet.Columns,
            CellSize = sheet.Cell,
            Gap = sheet.Gap ?? ContactSheetOptions.DefaultGap,
            Background = sheet.Background,
            Labels = sheet.Labels,
            FontDirectories = sheet.FontDirectory ? [Path.Combine(_directory, LegacyContactSheetFixture.FontDirectoryName)] : []
        };
        return new SkiaContactSheetRenderer().Render(entries, options, CancellationToken.None);
    }

    private static JsonObject Describe(ContactSheetResult result)
    {
        var cells = new JsonArray();
        foreach (var cell in result.Cells)
        {
            cells.Add(new JsonObject
            {
                ["index"] = cell.Index,
                ["label"] = cell.Label,
                ["column"] = cell.Column,
                ["row"] = cell.Row,
                ["cell"] = Box(cell.Cell),
                ["image"] = Box(cell.Image)
            });
        }
        return new JsonObject
        {
            ["pngSha256"] = LegacyContactSheetFixture.Sha256(result.Data),
            ["mimeType"] = result.MimeType,
            ["width"] = result.Width,
            ["height"] = result.Height,
            ["columns"] = result.Layout.Columns,
            ["rows"] = result.Layout.Rows,
            ["cells"] = cells
        };
    }

    private static JsonObject Box(PixelBox box) => new()
    {
        ["x"] = box.X,
        ["y"] = box.Y,
        ["width"] = box.Width,
        ["height"] = box.Height
    };

    private static Core.Documents.Document Load(string path) =>
        DocumentSerializer.Deserialize(File.ReadAllText(path))
        ?? throw new DocumentEngineException(EngineErrors.ParseError, $"Cannot parse fixture {path}");
}
