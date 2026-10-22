using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class ValidateCommandTests : CommandTestBase
{
    [Fact]
    public void Validate_LateNodeDiscriminator_LoadsLiteralDocument()
    {
        var path = Path.Combine(TestDir, "late-type.agd.json");
        File.WriteAllText(path, """
            {"version":1,"id":"11111111-1111-1111-1111-111111111111","name":"literal","revn":1,
             "canvas":{"width":128,"height":64},
             "root":{"id":"22222222-2222-2222-2222-222222222222","width":128,"height":64,
               "children":[{"id":"33333333-3333-3333-3333-333333333333","width":16,"height":8,"type":"rectangle"}],"type":"frame"}}
            """);

        var result = RunCommand("validate", path, "--strict", "-f", "json");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.True(JsonDocument.Parse(result.Output).RootElement.GetProperty("data").GetProperty("valid").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_InsideMcpStore_DoesNotCreateNestedStore(bool valid)
    {
        // MCP uses its working directory as the store root. File validation should
        // not try to register a second store at <cwd>/.agd during DI resolution.
        _ = new Core.Persistence.FileDocumentStore(TestDir);
        var path = CreateInvalidJsonFile();
        if (valid)
            File.WriteAllText(path, """
                {"version":1,"id":"11111111-1111-1111-1111-111111111111","name":"literal","revn":1,
                 "canvas":{"width":128,"height":64},
                 "root":{"type":"frame","id":"22222222-2222-2222-2222-222222222222","width":128,"height":64,"children":[]}}
                """);
        var before = File.ReadAllBytes(path);
        var result = RunCommand("validate", path, "-f", "json");
        Assert.True(result.ExitCode == (valid ? 0 : 1), result.Output + result.Error);
        var envelope = JsonDocument.Parse(result.Output).RootElement;
        if (valid)
            Assert.True(envelope.GetProperty("data").GetProperty("valid").GetBoolean());
        else
            Assert.Equal("PARSE_ERROR", envelope.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(Path.Combine(TestDir, ".agd")));
    }

    // Exit code contract from ValidateCommand.cs:97-107
    // 0 = valid
    // 1 = file not found or parse error
    // 2 = validation errors
    // 3 = strict mode with warnings

    [Fact]
    public void Validate_ValidDocument_ReturnsZero()
    {
        var docPath = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("validate", docPath);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Validate_FileNotFound_ReturnsOne()
    {
        var (exitCode, output, _) = RunCommand("validate", "/nonexistent/path.agd.json", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("FILE_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Validate_ParseError_ReturnsOne()
    {
        var invalidPath = CreateInvalidJsonFile();

        var (exitCode, output, _) = RunCommand("validate", invalidPath, "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("PARSE_ERROR", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Validate_ValidationErrors_ReturnsTwo()
    {
        var malformedPath = CreateMalformedDocument();

        var (exitCode, output, _) = RunCommand("validate", malformedPath, "-f", "json");

        Assert.Equal(2, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("valid").GetBoolean());
        Assert.True(data.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact]
    public void Validate_StrictModeWithWarnings_ReturnsThree()
    {
        var docPath = CreateDocumentWithWarnings();

        var (exitCode, output, _) = RunCommand("validate", docPath, "--strict", "-f", "json");

        Assert.Equal(3, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("valid").GetBoolean());
        Assert.True(data.GetProperty("warnings").GetArrayLength() > 0);
    }

    [Fact]
    public void Validate_StrictModeNoWarnings_ReturnsZero()
    {
        var docPath = CreateTestDocument();

        var (exitCode, _, _) = RunCommand("validate", docPath, "--strict");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Validate_WebIconProfileRejectsTextWithStableDiagnostic()
    {
        var docPath = CreateTestDocument();
        Assert.Equal(0, RunCommand("add", docPath, "text", "--text", "label").ExitCode);

        var (exitCode, output, _) = RunCommand(
            "validate", docPath, "--profile", "web-icon", "-f", "json");

        Assert.Equal(2, exitCode);
        var errors = JsonDocument.Parse(output).RootElement.GetProperty("data").GetProperty("errors");
        var error = Assert.Single(errors.EnumerateArray());
        Assert.Equal("WEBICON002", error.GetProperty("code").GetString());
        Assert.Equal("root.children[0].type", error.GetProperty("path").GetString());
    }

    [Fact]
    public void Validate_UnknownProfileReturnsInvalidArguments()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand(
            "validate", docPath, "--profile", "unknown", "-f", "json");

        Assert.Equal(4, exitCode);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Validate_JsonFormat_ReturnsStructuredOutput()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("validate", docPath, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        var data = result.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("valid").GetBoolean());
        Assert.Equal(0, data.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public void Validate_TextFormat_OutputsHumanReadable()
    {
        var docPath = CreateTestDocument();

        var (exitCode, output, _) = RunCommand("validate", docPath, "-f", "text");

        Assert.Equal(0, exitCode);
        Assert.Contains("Document is valid", output);
    }

    private string CreateDocumentWithWarnings()
    {
        var path = Path.Combine(TestDir, "warnings.agd.json");
        File.WriteAllText(path, """
        {
          "version": 1,
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "warnings-doc",
          "revn": 1,
          "canvas": { "width": 20000, "height": 600 },
          "root": {
            "type": "frame",
            "id": "22222222-2222-2222-2222-222222222222",
            "name": "root",
            "width": 20000,
            "height": 600,
            "children": []
          }
        }
        """);
        return path;
    }
}
