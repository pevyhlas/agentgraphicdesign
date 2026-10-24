using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Skia;
using FTPlus.AgentGraphicDesign.Tests.Characterization;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// Feature 012 T001 (AC-09 baseline per surface, plan D5/D25/D29): today's `render_contact_sheet`
/// result (JSON text, image as a SHA-256) through `paths` for the shared legacy fixtures, and today's
/// refusal of 257 paths, recorded before the `refs` cutover so T010 can compare `refs` without
/// conditions with it. Image hashes and the PNG <c>size</c> are compared on Linux x64 only.
/// </summary>
public sealed class LegacyContactSheetCharacterizationTests : IDisposable
{
    private const string Surface = "mcp";
    private readonly string _directory;
    private readonly DocumentTools _tools;

    public LegacyContactSheetCharacterizationTests()
    {
        _directory = Directory.CreateTempSubdirectory("agd-legacy-sheet-mcp-").FullName;
        LegacyContactSheetFixture.CopyInputs(_directory);
        var renderer = new SkiaRenderer();
        var engine = new DocumentEngine(new FileDocumentStore(_directory), null, renderer, null, new SkiaContactSheetRenderer(renderer));
        _tools = new DocumentTools(engine, new PathConfinement(_directory), renderer);
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
    public void RenderContactSheet_ResultMatchesThePreChangeCharacterization()
    {
        var actual = new JsonObject();
        foreach (var sheet in LegacyContactSheetFixture.Cases.Where(c => c.Mcp))
        {
            var result = _tools.RenderContactSheet(
                string.Join(',', sheet.Documents.Select(name => name + ".agd.json")),
                columns: sheet.Columns,
                cell: sheet.Cell,
                labels: sheet.Labels,
                fontDirs: sheet.FontDirectory ? LegacyContactSheetFixture.FontDirectoryName : null);
            Assert.True(result.IsError != true, $"{sheet.Name}: {Text(result)}");
            actual[sheet.Name] = Describe(result);
        }

        var overCap = JsonSerializer.Serialize(
            Enumerable.Range(0, LegacyContactSheetFixture.OverCapReferenceCount).Select(i => $"d{i:D3}.agd.json"));
        actual["refs-257"] = Describe(_tools.RenderContactSheet(overCap));

        LegacyContactSheetFixture.RecordIfRequested(Surface, actual);

        var golden = LegacyContactSheetFixture.LoadGolden(Surface);
        foreach (var (name, value) in actual)
            LegacyContactSheetFixture.AssertCase(golden, name, value!.AsObject(), "imageSha256");
    }

    private static JsonObject Describe(CallToolResult result)
    {
        var description = new JsonObject
        {
            ["isError"] = result.IsError == true,
            ["text"] = Text(result)
        };
        if (result.Content.OfType<ImageContentBlock>().SingleOrDefault() is { } image)
        {
            description["imageMimeType"] = image.MimeType;
            description["imageSha256"] = LegacyContactSheetFixture.Sha256(ImageBytes(image));
        }
        return description;
    }

    private static byte[] ImageBytes(ImageContentBlock image) => image.DecodedData.ToArray();

    private static string Text(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
