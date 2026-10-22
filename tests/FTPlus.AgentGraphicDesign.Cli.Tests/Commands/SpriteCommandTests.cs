using System.Text.Json;
using FTPlus.AgentGraphicDesign.Cli.Commands;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class SpriteCommandTests : CommandTestBase
{
    [Fact]
    public void Sprite_OversizedComposition_PreservesOutputTooLargeEnvelope()
    {
        var document = CreateTestDocument("large", 32, 32);
        var destination = Path.Combine(TestDir, "large.svg");
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentEngine>(new DocumentEngine(exportAdapters: [new OversizedExporter()]));
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config => config.AddCommand<SpriteCommand>("sprite"));
        using var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(1, app.Run(["sprite", document, "-o", destination, "-f", "json"]));
        }
        finally
        {
            Console.SetOut(original);
        }
        Assert.False(File.Exists(destination));
        var error = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("error");
        Assert.Equal("OUTPUT_TOO_LARGE", error.GetProperty("code").GetString());
    }

    private sealed class OversizedExporter : IExportAdapter
    {
        public string Id => "svg";
        public ExportCapabilities Capabilities => ExportCapabilities.Vector;
        public Task<ExportResult> ExportAsync(Document document, ExportOptions options, CancellationToken ct = default) =>
            Task.FromResult(new ExportResult { Data = new byte[SvgSpriteComposer.MaxOutputBytes], MimeType = "image/svg+xml" });
    }

    [Fact]
    public async Task Sprite_TwoDocuments_ProducesOneSheetWithBothSymbols()
    {
        var first = CreateTestDocument("ic-alpha", 32, 32);
        var second = CreateTestDocument("ic-beta", 32, 32);
        var outputPath = Path.Combine(TestDir, "sprite.svg");

        var (exitCode, output, _) = RunCommand("sprite", first, second, "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", svg);
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\">", svg);
        Assert.Contains("<defs>", svg);
        Assert.Contains("<symbol id=\"ic-alpha\" viewBox=\"0 0 32 32\">", svg);
        Assert.Contains("<symbol id=\"ic-beta\" viewBox=\"0 0 32 32\">", svg);
        Assert.EndsWith("</defs>\n</svg>\n", svg);

        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        var symbols = data.GetProperty("symbols").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(new[] { "ic-alpha", "ic-beta" }, symbols);
    }

    [Fact]
    public void Sprite_DuplicateDocumentNames_FailsWithoutWritingOutput()
    {
        var first = CreateTestDocument("ic-dup", 32, 32);
        var copyPath = Path.Combine(TestDir, "ic-dup-copy.agd.json");
        // Same document name inside, different file name: the symbol id collides.
        File.Copy(first, copyPath);
        var outputPath = Path.Combine(TestDir, "sprite.svg");

        var (exitCode, output, _) = RunCommand("sprite", first, copyPath, "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Contains("duplicate symbol id", error.GetProperty("message").GetString());
    }

    [Fact]
    public void Sprite_DocumentNameNotAValidSymbolId_Fails()
    {
        var docPath = CreateTestDocument("ic-ok", 32, 32);
        var badPath = Path.Combine(TestDir, "bad.agd.json");
        File.WriteAllText(badPath, File.ReadAllText(docPath).Replace("\"ic-ok\"", "\"1 bad name\""));
        var outputPath = Path.Combine(TestDir, "sprite.svg");

        var (exitCode, output, _) = RunCommand("sprite", badPath, "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Sprite_NodeGuidCollisionAcrossDocuments_FailsInsteadOfCrossWiringReferences()
    {
        // grad-/clip- element ids derive from node GUIDs; a cross-document collision
        // would make one symbol's fill reference the other's gradient.
        var sharedNodeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var first = CreateTestDocument("ic-g1", 32, 32);
        var second = CreateTestDocument("ic-g2", 32, 32);
        SetGradientRectangle(first, sharedNodeId);
        SetGradientRectangle(second, sharedNodeId);
        var outputPath = Path.Combine(TestDir, "sprite.svg");

        var (exitCode, output, _) = RunCommand("sprite", first, second, "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Contains("duplicate element id", error.GetProperty("message").GetString());
    }

    private void SetGradientRectangle(string documentPath, Guid nodeId)
    {
        Assert.Equal(0, RunCommand("add", documentPath, "rectangle").ExitCode);
        var document = DocumentSerializer.Deserialize(File.ReadAllText(documentPath))!;
        var rectangle = Assert.IsType<RectangleNode>(Assert.Single(document.Root.Children));
        rectangle.Id = nodeId;
        rectangle.Style = new Style
        {
            Fill = new Fill
            {
                Gradient = new LinearGradient
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = 1,
                    Y2 = 0,
                    Stops =
                    [
                        new GradientStop { Offset = 0, Color = "#000000" },
                        new GradientStop { Offset = 1, Color = "#ffffff" }
                    ]
                }
            }
        };
        File.WriteAllText(documentPath, DocumentSerializer.Serialize(document));
    }

    [Fact]
    public void Sprite_MissingFile_FailsWithoutWritingOutput()
    {
        var docPath = CreateTestDocument("ic-present", 32, 32);
        var outputPath = Path.Combine(TestDir, "sprite.svg");

        var (exitCode, _, _) = RunCommand("sprite", docPath, Path.Combine(TestDir, "absent.agd.json"), "-o", outputPath);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Sprite_WebIconProfileRejectsTextNodes()
    {
        var docPath = CreateTestDocument("ic-text", 32, 32);
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label").ExitCode);
        var outputPath = Path.Combine(TestDir, "sprite.svg");

        var (exitCode, output, _) = RunCommand("sprite", docPath, "-o", outputPath, "--profile", "web-icon", "-f", "json");

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("WEBICON002", error.GetProperty("code").GetString());
    }


    // ---- currentColor ----

    [Fact]
    public async Task Sprite_WithCurrentColor_RewritesMatchingPaintInEverySymbol()
    {
        var first = CreateTestDocument("ic-one", 32, 32);
        var second = CreateTestDocument("ic-two", 32, 32);
        Assert.Equal(0, RunCommand("add", first, "rectangle", "--width", "8", "--height", "8", "--fill", "#000000").ExitCode);
        Assert.Equal(0, RunCommand("add", second, "rectangle", "--width", "8", "--height", "8", "--fill", "#000000", "--stroke", "#ff0000").ExitCode);
        var outputPath = Path.Combine(TestDir, "sprite-mono.svg");

        var (exitCode, _, _) = RunCommand("sprite", first, second, "--current-color", "#000000", "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<symbol id=\"ic-one\"", svg);
        Assert.Contains("<symbol id=\"ic-two\"", svg);
        Assert.Equal(2, svg.Split("fill=\"currentColor\"").Length - 1);
        Assert.Contains("stroke=\"#ff0000\"", svg);
        Assert.DoesNotContain("#000000", svg);
    }

    [Fact]
    public void Sprite_InvalidCurrentColor_FailsWithoutWritingOutput()
    {
        var docPath = CreateTestDocument("ic-bad-color", 32, 32);
        var outputPath = Path.Combine(TestDir, "sprite-bad-color.svg");

        var (exitCode, output, _) = RunCommand("sprite", docPath, "--current-color", "black", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }
}
