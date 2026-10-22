using System.Text.Json;
using FTPlus.AgentGraphicDesign.Cli.Commands;
using FTPlus.AgentGraphicDesign.Core;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class CreateCommandTests : CommandTestBase
{
    [Fact]
    public void Create_ValidName_ReturnsZeroAndCreatesFile()
    {
        var outputPath = Path.Combine(TestDir, "new-doc.agd.json");

        var (exitCode, output, _) = RunCommand("create", "new-doc", "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void Create_WithDimensions_SetsCanvasSize()
    {
        var outputPath = Path.Combine(TestDir, "sized.agd.json");

        var (exitCode, _, _) = RunCommand("create", "sized", "-o", outputPath, "-w", "1024", "-h", "768");

        Assert.Equal(0, exitCode);
        var json = File.ReadAllText(outputPath);
        Assert.Contains("\"width\": 1024", json);
        Assert.Contains("\"height\": 768", json);
    }

    [Fact]
    public void Create_WithBackground_SetsCanvasBackground()
    {
        var outputPath = Path.Combine(TestDir, "bg.agd.json");

        var (exitCode, _, _) = RunCommand("create", "bg", "-o", outputPath, "--background", "#ffffff");

        Assert.Equal(0, exitCode);
        var json = File.ReadAllText(outputPath);
        Assert.Contains("\"background\": \"#ffffff\"", json);
    }

    [Fact]
    public void Create_JsonFormat_ReturnsStructuredOutput()
    {
        var outputPath = Path.Combine(TestDir, "json-out.agd.json");

        var (exitCode, output, _) = RunCommand("create", "json-out", "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotNull(result.RootElement.GetProperty("data").GetProperty("documentId").GetString());
        Assert.Equal("json-out.agd.json", result.RootElement.GetProperty("data").GetProperty("locator").GetString());
        Assert.Equal("json-out", result.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public void Create_InvalidDimensions_ReturnsOne()
    {
        var outputPath = Path.Combine(TestDir, "bad-dims.agd.json");

        var (exitCode, output, _) = RunCommand("create", "bad", "-o", outputPath, "-w", "0", "-h", "100", "-f", "json");

        Assert.Equal(4, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("INVALID_DIMENSIONS", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Create_DefaultOutput_UsesNameAsFileName()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(TestDir);

            var (exitCode, _, _) = RunCommand("create", "auto-named");

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(TestDir, "auto-named.agd.json")));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void Create_WithSeed_ProducesDeterministicIds()
    {
        var path1 = Path.Combine(TestDir, "seeded1.agd.json");
        var path2 = Path.Combine(TestDir, "seeded2.agd.json");

        var (exit1, _, _) = RunCommand("create", "seeded", "-o", path1, "--seed", "42");
        var (exit2, _, _) = RunCommand("create", "seeded", "-o", path2, "--seed", "42");

        Assert.Equal(0, exit1);
        Assert.Equal(0, exit2);

        var json1 = File.ReadAllText(path1);
        var json2 = File.ReadAllText(path2);

        var doc1 = JsonDocument.Parse(json1);
        var doc2 = JsonDocument.Parse(json2);

        Assert.Equal(
            doc1.RootElement.GetProperty("id").GetString(),
            doc2.RootElement.GetProperty("id").GetString());
        Assert.Equal(
            doc1.RootElement.GetProperty("root").GetProperty("id").GetString(),
            doc2.RootElement.GetProperty("root").GetProperty("id").GetString());
    }

    [Fact]
    public void Create_DifferentSeeds_ProduceDifferentIds()
    {
        var path1 = Path.Combine(TestDir, "seed1.agd.json");
        var path2 = Path.Combine(TestDir, "seed2.agd.json");

        var (exit1, _, _) = RunCommand("create", "test", "-o", path1, "--seed", "1");
        var (exit2, _, _) = RunCommand("create", "test", "-o", path2, "--seed", "2");

        Assert.Equal(0, exit1);
        Assert.Equal(0, exit2);

        var json1 = File.ReadAllText(path1);
        var json2 = File.ReadAllText(path2);

        var doc1 = JsonDocument.Parse(json1);
        var doc2 = JsonDocument.Parse(json2);

        Assert.NotEqual(
            doc1.RootElement.GetProperty("id").GetString(),
            doc2.RootElement.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("web-icon-32", 32, 32, null)]
    [InlineData("web-icon-24", 24, 24, null)]
    [InlineData("social-1200x630", 1200, 630, "#ffffff")]
    public void Create_WithTemplate_SeedsCanvasFromCatalog(string templateId, double width, double height, string? background)
    {
        var outputPath = Path.Combine(TestDir, $"{templateId}.agd.json");

        var (exitCode, output, _) = RunCommand("create", "from-template", "-o", outputPath, "--template", templateId, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output).RootElement;
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(templateId, result.GetProperty("data").GetProperty("template").GetString());

        var document = JsonDocument.Parse(File.ReadAllText(outputPath)).RootElement;
        var canvas = document.GetProperty("canvas");
        Assert.Equal(width, canvas.GetProperty("width").GetDouble());
        Assert.Equal(height, canvas.GetProperty("height").GetDouble());
        if (background is null)
        {
            Assert.False(canvas.TryGetProperty("background", out _), "icon templates must not set a background");
        }
        else
        {
            Assert.Equal(background, canvas.GetProperty("background").GetString());
        }
        Assert.Equal(width, document.GetProperty("root").GetProperty("width").GetDouble());
        Assert.Equal(height, document.GetProperty("root").GetProperty("height").GetDouble());
    }

    [Fact]
    public void Create_WithTemplate_ExplicitOptionsOverrideTemplateValues()
    {
        var outputPath = Path.Combine(TestDir, "override.agd.json");

        var (exitCode, _, _) = RunCommand("create", "override", "-o", outputPath, "--template", "social-1200x630", "-w", "600", "--background", "#000000");

        Assert.Equal(0, exitCode);
        var canvas = JsonDocument.Parse(File.ReadAllText(outputPath)).RootElement.GetProperty("canvas");
        Assert.Equal(600, canvas.GetProperty("width").GetDouble());
        Assert.Equal(630, canvas.GetProperty("height").GetDouble());
        Assert.Equal("#000000", canvas.GetProperty("background").GetString());
    }

    [Fact]
    public void Create_TemplatedIcon_PassesWebIconProfile()
    {
        var outputPath = Path.Combine(TestDir, "icon.agd.json");
        Assert.Equal(0, RunCommand("create", "icon", "-o", outputPath, "--template", "web-icon-32").ExitCode);

        var (exitCode, output, _) = RunCommand("validate", outputPath, "--profile", "web-icon", "--strict", "-f", "json");

        Assert.Equal(0, exitCode);
        Assert.True(JsonDocument.Parse(output).RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Create_UnknownTemplate_ReturnsInvalidArgumentsListingValidIds()
    {
        var outputPath = Path.Combine(TestDir, "unknown.agd.json");

        var (exitCode, output, _) = RunCommand("create", "unknown", "-o", outputPath, "--template", "poster", "-f", "json");

        Assert.Equal(4, exitCode);
        var result = JsonDocument.Parse(output).RootElement;
        Assert.False(result.GetProperty("ok").GetBoolean());
        var error = result.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        var message = error.GetProperty("message").GetString()!;
        Assert.Contains("'poster'", message);
        foreach (var template in DocumentTemplates.All)
        {
            Assert.Contains(template.Id, message);
        }
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Create_Help_DocumentsEveryTemplateId()
    {
        foreach (var template in DocumentTemplates.All)
        {
            Assert.Contains(template.Id, CreateCommand.TemplateDescription);
        }

        var (exitCode, output, _) = RunCommand("create", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--template", output);
        foreach (var template in DocumentTemplates.All)
        {
            Assert.Contains(template.Id, output);
        }
    }
}
