using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class HardeningCommandTests : CommandTestBase
{
    [Fact]
    public void UnknownCreateOption_ReturnsInvalidArgumentsJsonWithoutCreatingDefault()
    {
        var result = RunCommand("create", "typo-target", "--ouptut", "wrong.agd.json", "-f", "json");

        Assert.Equal(4, result.ExitCode);
        AssertJsonError(result.Output, "INVALID_ARGUMENTS");
        Assert.False(File.Exists(Path.Combine(TestDir, "typo-target.agd.json")));
        Assert.False(File.Exists(Path.Combine(TestDir, "wrong.agd.json")));
    }

    [Theory]
    [InlineData("create", "missing-width", "--width", "--format", "json")]
    [InlineData("create", "bad-width", "--width", "not-a-number", "--format", "json")]
    public void MissingOrInvalidOptionValue_ReturnsInvalidArguments(params string[] args)
    {
        var result = RunCommand(args);

        Assert.Equal(4, result.ExitCode);
        AssertJsonError(result.Output, "INVALID_ARGUMENTS");
    }

    [Fact]
    public void NonFiniteDimensions_ReturnInvalidArguments()
    {
        var result = RunCommand("create", "non-finite", "--width", "NaN", "-f", "json");

        Assert.Equal(4, result.ExitCode);
        AssertJsonError(result.Output, "INVALID_DIMENSIONS");
    }

    [Fact]
    public void OversizedDocumentInput_IsRejectedBeforeParsing()
    {
        var path = Path.Combine(TestDir, "oversized.agd.json");
        using (var stream = File.Create(path))
        {
            stream.SetLength(16 * 1024 * 1024 + 1);
        }

        var result = RunCommand("inspect", path, "-f", "json");

        Assert.Equal(4, result.ExitCode);
        AssertJsonError(result.Output, "INPUT_TOO_LARGE");
    }

    [Fact]
    public void DeterministicCreateRepeat_DoesNotEraseEditsWithoutForce()
    {
        var path = Path.Combine(TestDir, "stable.agd.json");
        Assert.Equal(0, RunCommand("create", "stable", "-o", path, "--seed", "42").ExitCode);
        Assert.Equal(0, RunCommand("add", path, "rectangle", "--name", "keep-me").ExitCode);

        var repeated = RunCommand("create", "stable", "-o", path, "--seed", "42", "-f", "json");

        Assert.Equal(1, repeated.ExitCode);
        AssertJsonError(repeated.Output, "DOCUMENT_EXISTS");
        var document = JsonDocument.Parse(File.ReadAllText(path));
        var children = document.RootElement.GetProperty("root").GetProperty("children");
        Assert.Contains(children.EnumerateArray(), node => node.GetProperty("name").GetString() == "keep-me");
    }

    [Fact]
    public void DeterministicCreate_ForceExplicitlyOverwrites()
    {
        var path = Path.Combine(TestDir, "forced.agd.json");
        Assert.Equal(0, RunCommand("create", "forced", "-o", path, "--seed", "7").ExitCode);
        Assert.Equal(0, RunCommand("add", path, "rectangle").ExitCode);

        var forced = RunCommand("create", "forced", "-o", path, "--seed", "7", "--force");

        Assert.Equal(0, forced.ExitCode);
        var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Empty(document.RootElement.GetProperty("root").GetProperty("children").EnumerateArray());
    }

    [Fact]
    public void ImportRepeat_DoesNotEraseEditsWithoutForce()
    {
        var svg = Path.Combine(TestDir, "source.svg");
        var output = Path.Combine(TestDir, "imported.agd.json");
        File.WriteAllText(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"5\" height=\"5\"/></svg>");
        Assert.Equal(0, RunCommand("import", svg, "-o", output).ExitCode);
        Assert.Equal(0, RunCommand("add", output, "rectangle", "--name", "keep-import-edit").ExitCode);

        var repeated = RunCommand("import", svg, "-o", output, "-f", "json");

        Assert.Equal(1, repeated.ExitCode);
        AssertJsonError(repeated.Output, "DOCUMENT_EXISTS");
        Assert.Contains("keep-import-edit", File.ReadAllText(output));
    }

    [Fact]
    public void CreateLocator_CanBeLoadedAndSavedBack()
    {
        var created = RunCommand("create", "roundtrip", "-f", "json");
        var locator = JsonDocument.Parse(created.Output).RootElement.GetProperty("data").GetProperty("locator").GetString()!;

        var added = RunCommand("add", locator, "rectangle", "--name", "roundtrip-node", "-f", "json");

        Assert.Equal(0, added.ExitCode);
        Assert.Contains("roundtrip-node", File.ReadAllText(Path.Combine(TestDir, locator)));
    }

    [Fact]
    public void AddPolygon_OpenFlagPersistsOpenPolygon()
    {
        var path = CreateTestDocument("polygon-open");

        var result = RunCommand("add", path, "polygon", "--points", "0,0 10,0 10,10", "--open");

        Assert.Equal(0, result.ExitCode);
        var document = JsonDocument.Parse(File.ReadAllText(path));
        var polygon = document.RootElement.GetProperty("root").GetProperty("children")[0];
        Assert.False(polygon.GetProperty("closed").GetBoolean());
    }

    [Fact]
    public void Reorder_BeforeAndAfter_UseConventionalMeaning()
    {
        var path = CreateTestDocument("reorder-conventional");
        var a = AddNamed(path, "a");
        var b = AddNamed(path, "b");
        var c = AddNamed(path, "c");

        var result = RunCommand("reorder", path, c, "--before", b, "--after", a, "-f", "json");

        Assert.Equal(0, result.ExitCode);
        var document = JsonDocument.Parse(File.ReadAllText(path));
        var nodes = document.RootElement.GetProperty("root").GetProperty("children").EnumerateArray().ToList();
        var aOrder = nodes.Single(node => node.GetProperty("id").GetString() == a).GetProperty("order").GetString();
        var bOrder = nodes.Single(node => node.GetProperty("id").GetString() == b).GetProperty("order").GetString();
        var cOrder = nodes.Single(node => node.GetProperty("id").GetString() == c).GetProperty("order").GetString();
        Assert.True(string.Compare(cOrder, aOrder, StringComparison.Ordinal) > 0);
        Assert.True(string.Compare(cOrder, bOrder, StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void ExpectedIoError_DoesNotDiscloseAbsolutePath()
    {
        var result = RunCommand("create", "io-error", "-o", TestDir, "--force", "-f", "json");

        Assert.Equal(1, result.ExitCode);
        var error = AssertJsonError(result.Output, "IO_ERROR");
        Assert.DoesNotContain(TestDir, error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("--fill")]
    [InlineData("--stroke")]
    [InlineData("--font-family")]
    public void Add_OversizedPersistedStyleValue_IsRejected(string option)
    {
        var path = CreateTestDocument("oversized-style");

        var result = RunCommand("add", path, "rectangle", option, new string('x', 1025), "-f", "json");

        Assert.Equal(4, result.ExitCode);
        AssertJsonError(result.Output, "INPUT_TOO_LARGE");
        var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Empty(document.RootElement.GetProperty("root").GetProperty("children").EnumerateArray());
    }

    [Fact]
    public void ErrorOutput_DoesNotDiscloseUnrecognizedUnixRoot()
    {
        const string absolutePath = "/Users/private-user/designs/secret.agd.json";

        var result = RunCommand("inspect", absolutePath, "-f", "json");

        var error = AssertJsonError(result.Output, "FILE_NOT_FOUND");
        Assert.DoesNotContain("/Users/", error.GetProperty("message").GetString());
        Assert.Contains("secret.agd.json", error.GetProperty("message").GetString());
    }

    private static JsonElement AssertJsonError(string output, string code)
    {
        var json = JsonDocument.Parse(output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        return error;
    }

    private string AddNamed(string path, string name)
    {
        var result = RunCommand("add", path, "rectangle", "--name", name, "-f", "json");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("data").GetProperty("nodeId").GetString()!;
    }
}
