using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class ExportCommandTests : CommandTestBase
{
    // Exit code contract from ExportCommand.cs:104-115
    // 0 = success
    // 1 = file not found, parse error, or export failed

    [Fact]
    public async Task Export_ValidDocument_ReturnsZeroAndCreatesSvg()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "output.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<svg", svg);
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("png")]
    public void Export_StrictInvalidDocument_ReturnsValidationErrorWithoutWritingOutput(string exportType)
    {
        var docPath = CreateTestDocument();
        RunCommand("add", docPath, "rectangle");
        SetMissingFillToken(docPath);
        var outputPath = Path.Combine(TestDir, $"strict-invalid.{exportType}");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--strict", "-t", exportType, "-o", outputPath, "-f", "json");

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("TOKEN006", error.GetProperty("code").GetString());
        Assert.Equal("root.children[0].style.fill.tokenRef", error.GetProperty("path").GetString());
    }

    [Fact]
    public void Export_StrictValidDocument_WritesOutput()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "strict-valid.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "--strict", "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void Export_WebIconProfileRejectsTextWithoutStrictAndWritesNoOutput()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label").ExitCode);
        var outputPath = Path.Combine(TestDir, "profile-invalid.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--profile", "web-icon", "-o", outputPath, "-f", "json");

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("WEBICON002", error.GetProperty("code").GetString());
        Assert.Equal("root.children[0].type", error.GetProperty("path").GetString());
    }

    [Fact]
    public void Export_WebIconProfileAllowsWarningsUnlessStrict()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "path").ExitCode);
        var profileOutput = Path.Combine(TestDir, "profile-warning.svg");
        var strictOutput = Path.Combine(TestDir, "profile-strict-warning.svg");

        var profileResult = RunCommand(
            "export", docPath, "--profile", "web-icon", "-o", profileOutput, "-f", "json");
        var strictResult = RunCommand(
            "export", docPath, "--profile", "web-icon", "--strict", "-o", strictOutput, "-f", "json");

        Assert.Equal(0, profileResult.ExitCode);
        Assert.True(File.Exists(profileOutput));
        Assert.Equal(3, strictResult.ExitCode);
        Assert.False(File.Exists(strictOutput));
        var error = JsonDocument.Parse(strictResult.Output).RootElement.GetProperty("error");
        Assert.Equal("PATH001", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_WebIconProfileRejectsOpaqueBackgroundOverride()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "profile-background.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--profile", "web-icon", "--background", "#ffffff", "-o", outputPath, "-f", "json");

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("WEBICON001", error.GetProperty("code").GetString());
        Assert.Equal("export.background", error.GetProperty("path").GetString());
    }

    [Fact]
    public void Export_UnknownProfileReturnsInvalidArgumentsAndWritesNoOutput()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "unknown-profile.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--profile", "unknown", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_NonStrictInvalidColor_PreservesSafeFallback()
    {
        var docPath = CreateTestDocument();
        RunCommand("add", docPath, "rectangle");
        SetMissingFillToken(docPath);
        var outputPath = Path.Combine(TestDir, "fallback.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("fill=\"#000000\"", File.ReadAllText(outputPath));
    }

    private static void SetMissingFillToken(string documentPath)
    {
        var document = DocumentSerializer.Deserialize(File.ReadAllText(documentPath))!;
        var rectangle = Assert.IsType<RectangleNode>(Assert.Single(document.Root.Children));
        rectangle.Style = new Style { Fill = new Fill { TokenRef = "{missing}" } };
        File.WriteAllText(documentPath, DocumentSerializer.Serialize(document));
    }

    [Fact]
    public async Task Export_WithSymbol_WritesBareSymbolFragment()
    {
        var docPath = CreateTestDocument("ic-power", 32, 32);
        var outputPath = Path.Combine(TestDir, "ic-power.symbol.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "--symbol", "ic-power", "-o", outputPath);

        Assert.Equal(0, exitCode);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.StartsWith("<symbol id=\"ic-power\" viewBox=\"0 0 32 32\">", svg);
        Assert.EndsWith("</symbol>\n", svg);
        Assert.DoesNotContain("<?xml", svg);
        Assert.DoesNotContain("<svg", svg);
    }

    [Fact]
    public void Export_SymbolWithRasterType_ReturnsInvalidArguments()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "symbol.png");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--symbol", "ic-x", "-t", "png", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_SymbolWithScale_ReturnsInvalidArguments()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "symbol-scaled.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--symbol", "ic-x", "-s", "2.0", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_SymbolWithMalformedId_ReturnsInvalidProperty()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "symbol-bad-id.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--symbol", "1bad id", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Export_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("export", "/nonexistent/path.agd.json", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("FILE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Export_ParseError_ReturnsOne()
    {
        var invalidPath = CreateInvalidJsonFile();

        var (exitCode, output, _) = RunCommand("export", invalidPath, "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("PARSE_ERROR", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Export_UnsupportedFormat_ReturnsOne()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("export", docPath, "-t", "pdf", "-f", "json");

        Assert.Equal(4, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("EXPORT_UNSUPPORTED", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Export_WithScale_AppliesScale()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "scaled.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "-o", outputPath, "-s", "2.0");

        Assert.Equal(0, exitCode);
        var svg = File.ReadAllText(outputPath);
        Assert.Contains("width=\"1600\"", svg);
        Assert.Contains("height=\"1200\"", svg);
    }

    [Fact]
    public void Export_WithBackground_IncludesBackground()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "bg.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "-o", outputPath, "--background", "#ff0000");

        Assert.Equal(0, exitCode);
        var svg = File.ReadAllText(outputPath);
        Assert.Contains("fill=\"#ff0000\"", svg);
    }

    [Fact]
    public void Export_JsonFormat_ReturnsStructuredOutput()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "json-export.svg");

        var (exitCode, output, _) = RunCommand("export", docPath, "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.Equal("svg", data.GetProperty("type").GetString());
        Assert.True(data.GetProperty("size").GetInt32() > 0);
    }

    [Fact]
    public void Export_DefaultOutput_DerivesFromInput()
    {
        var docPath = CreateTestDocument("my-design");
        var expectedOutput = Path.Combine(TestDir, "my-design.svg");

        var (exitCode, _, _) = RunCommand("export", docPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(expectedOutput));
    }

    [Fact]
    public void Export_Png_CreatesPngFile()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "output.png");

        var (exitCode, output, _) = RunCommand("export", docPath, "-t", "png", "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));

        var bytes = File.ReadAllBytes(outputPath);
        Assert.True(bytes.Length > 8);
        Assert.Equal((byte)0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);

        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("image/png", result.RootElement.GetProperty("data").GetProperty("mimeType").GetString());
    }

    // ---- Fonts ----

    [Fact]
    public void Export_Png_MissingFontFamily_SubstitutesAndReportsDiagnostic()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label", "--font-family", "Nonexistent Family").ExitCode);
        var outputPath = Path.Combine(TestDir, "substituted.png");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "-t", "png", "--no-system-fonts", "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        var diagnostic = Assert.Single(data.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("FONT_SUBSTITUTED", diagnostic.GetProperty("code").GetString());
        Assert.Contains("Nonexistent Family", diagnostic.GetProperty("message").GetString());
    }

    [Fact]
    public void Export_Png_MissingFontFamily_StrictFonts_FailsWithoutWritingOutput()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label", "--font-family", "Nonexistent Family").ExitCode);
        var outputPath = Path.Combine(TestDir, "strict-fonts.png");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "-t", "png", "--no-system-fonts", "--strict-fonts", "-o", outputPath, "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("FONT_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Contains("Nonexistent Family", error.GetProperty("message").GetString());
    }

    [Fact]
    public void Export_Png_FontDirAndBundledInter_SatisfyStrictFonts()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label", "--font-family", "Inter", "--font-weight", "700").ExitCode);
        var fontDir = Directory.CreateDirectory(Path.Combine(TestDir, "fonts")).FullName;
        var outputPath = Path.Combine(TestDir, "strict-ok.png");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "-t", "png", "--font-dir", fontDir, "--font-dir", TestDir,
            "--no-system-fonts", "--strict-fonts", "-o", outputPath, "-f", "json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Empty(data.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void Export_Png_WithoutSystemFonts_IsByteIdenticalAcrossRuns()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "Deterministic", "--font-size", "32").ExitCode);
        var first = Path.Combine(TestDir, "first.png");
        var second = Path.Combine(TestDir, "second.png");

        Assert.Equal(0, RunCommand("export", docPath, "-t", "png", "--no-system-fonts", "-o", first).ExitCode);
        Assert.Equal(0, RunCommand("export", docPath, "-t", "png", "--no-system-fonts", "-o", second).ExitCode);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void Export_MissingFontDir_ReturnsInvalidArguments()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "missing-font-dir.png");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "-t", "png", "--font-dir", Path.Combine(TestDir, "no-such-dir"), "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("--strict-fonts")]
    [InlineData("--no-system-fonts")]
    public void Export_FontOptionsWithSvg_ReturnsInvalidArguments(string option)
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "font-options.svg");

        var (exitCode, output, _) = RunCommand("export", docPath, option, "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Contains("raster", error.GetProperty("message").GetString());
    }

    [Fact]
    public void Export_Jpeg_CreatesJpegFile()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "output.jpeg");

        var (exitCode, _, _) = RunCommand("export", docPath, "-t", "jpeg", "-o", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));

        var bytes = File.ReadAllBytes(outputPath);
        Assert.True(bytes.Length > 2);
        Assert.Equal((byte)0xFF, bytes[0]);
        Assert.Equal((byte)0xD8, bytes[1]);
    }


    // ---- currentColor ----

    [Fact]
    public async Task Export_WithCurrentColor_RewritesMatchingPaintOnly()
    {
        var docPath = CreateTestDocument("ic-mono", 32, 32);
        Assert.Equal(0, RunCommand("add", docPath, "rectangle", "--width", "10", "--height", "10",
            "--fill", "#000000", "--stroke", "#ff0000").ExitCode);
        Assert.Equal(0, RunCommand("add", docPath, "rectangle", "--width", "10", "--height", "10",
            "--fill", "#123456", "--stroke", "#000000").ExitCode);
        var outputPath = Path.Combine(TestDir, "ic-mono.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "--current-color", "#000000", "-o", outputPath);

        Assert.Equal(0, exitCode);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.Contains("stroke=\"currentColor\"", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
        Assert.Contains("fill=\"#123456\"", svg);
        Assert.DoesNotContain("#000000", svg);
    }

    [Fact]
    public async Task Export_SymbolWithCurrentColor_RewritesFragmentPaint()
    {
        var docPath = CreateTestDocument("ic-sym", 32, 32);
        Assert.Equal(0, RunCommand("add", docPath, "path", "--path-data", "M0,0 L10,0 L10,10 Z", "--fill", "#000").ExitCode);
        var outputPath = Path.Combine(TestDir, "ic-sym.symbol.svg");

        var (exitCode, _, _) = RunCommand(
            "export", docPath, "--symbol", "ic-sym", "--current-color", "#000000", "-o", outputPath);

        Assert.Equal(0, exitCode);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.StartsWith("<symbol id=\"ic-sym\"", svg);
        Assert.Contains("fill=\"currentColor\"", svg);
    }

    [Fact]
    public void Export_CurrentColorWithRasterType_ReturnsInvalidArguments()
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "current-color.png");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--current-color", "#000000", "-t", "png", "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Contains("svg", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("black")]
    [InlineData("#12")]
    [InlineData("rgb(0,0)")]
    public void Export_InvalidCurrentColor_ReturnsInvalidProperty(string currentColor)
    {
        var docPath = CreateTestDocument();
        var outputPath = Path.Combine(TestDir, "current-color-bad.svg");

        var (exitCode, output, _) = RunCommand(
            "export", docPath, "--current-color", currentColor, "-o", outputPath, "-f", "json");

        Assert.Equal(4, exitCode);
        Assert.False(File.Exists(outputPath));
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_PROPERTY", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Export_EvenOddPath_EmitsFillRule()
    {
        var docPath = CreateTestDocument("ring", 100, 100);
        Assert.Equal(0, RunCommand("add", docPath, "path",
            "--path-data", "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z",
            "--fill-rule", "evenodd").ExitCode);
        var outputPath = Path.Combine(TestDir, "ring.svg");

        var (exitCode, _, _) = RunCommand("export", docPath, "-o", outputPath);

        Assert.Equal(0, exitCode);
        var svg = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
    }
}
