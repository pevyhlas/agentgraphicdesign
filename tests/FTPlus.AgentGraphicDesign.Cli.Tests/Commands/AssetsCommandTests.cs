using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public sealed class AssetsCommandTests : CommandTestBase
{
    [Fact]
    public void BuildThenCheck_MixedOutputsAndSprite_AreReadOnlyAndStoreless()
    {
        var manifest = CreateManifest();
        var (buildCode, buildOutput, _) = RunCommand("assets", "build", manifest, "-f", "json");
        Assert.Equal(0, buildCode);
        var build = JsonDocument.Parse(buildOutput).RootElement;
        Assert.True(build.GetProperty("ok").GetBoolean());
        Assert.All(build.GetProperty("data").GetProperty("files").EnumerateArray(),
            file => Assert.Equal("written", file.GetProperty("status").GetString()));
        Assert.Contains("currentColor", File.ReadAllText(Path.Combine(TestDir, "designs/generated/icon.svg")));
        Assert.Contains("<symbol id=\"icon\"", File.ReadAllText(Path.Combine(TestDir, "designs/generated/sprite.svg")));
        var snapshot = Snapshot();

        var (checkCode, checkOutput, _) = RunCommand("assets", "check", manifest, "--format=json");
        Assert.Equal(0, checkCode);
        Assert.True(JsonDocument.Parse(checkOutput).RootElement.GetProperty("data").GetProperty("check").GetBoolean());
        Assert.Equal(snapshot, Snapshot());
        Assert.False(Directory.Exists(Path.Combine(TestDir, ".agd")));
        Assert.False(Directory.Exists(Path.Combine(TestDir, "designs/.agd")));

        Assert.Equal(0, RunCommand("assets", "build", manifest).ExitCode);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void CheckMissingOutputs_FailsWithInventoryAndDoesNotCreateDirectories()
    {
        var manifest = CreateManifest();
        var snapshot = Snapshot();
        var (code, output, _) = RunCommand("assets", "check", manifest, "-f", "json");
        Assert.Equal(6, code);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal("ASSET_DRIFT", json.GetProperty("error").GetProperty("code").GetString());
        Assert.All(json.GetProperty("data").GetProperty("files").EnumerateArray(),
            file => Assert.Equal("missing", file.GetProperty("status").GetString()));
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void CheckStaleAndOrphan_BuildRepairsOnlyDeclaredOutputs()
    {
        var manifest = CreateManifest();
        Assert.Equal(0, RunCommand("assets", "build", manifest).ExitCode);
        var generated = Path.Combine(TestDir, "designs/generated");
        File.WriteAllText(Path.Combine(generated, "icon.svg"), "old");
        File.WriteAllText(Path.Combine(generated, "orphan.txt"), "keep");
        var snapshot = Snapshot();

        var (code, output, _) = RunCommand("assets", "check", manifest, "-f", "json");
        Assert.Equal(6, code);
        Assert.Contains("stale", output);
        Assert.Contains("unexpected", output);
        Assert.Equal(snapshot, Snapshot());

        Assert.Equal(6, RunCommand("assets", "build", manifest).ExitCode);
        Assert.Contains("<svg", File.ReadAllText(Path.Combine(generated, "icon.svg")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(generated, "orphan.txt")));
    }

    [Theory]
    [InlineData("{", "PARSE_ERROR", 1)]
    [InlineData("{\"version\":2,\"outputRoot\":\"generated\"}", "UNSUPPORTED_VERSION", 4)]
    [InlineData("{\"version\":1,\"outputRoot\":\"generated\",\"hook\":\"bad\"}", "INVALID_ARGUMENTS", 4)]
    public void InvalidManifest_ReturnsStructuredFailure(string contents, string errorCode, int exitCode)
    {
        var manifest = Path.Combine(TestDir, "assets.json");
        File.WriteAllText(manifest, contents);
        var (code, output, _) = RunCommand("assets", "build", manifest, "-f", "json");
        Assert.Equal(exitCode, code);
        Assert.Equal(errorCode, JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(Directory.Exists(Path.Combine(TestDir, ".agd")));
    }

    [Fact]
    public void OrphanPaths_PreserveUnixNamesAndEscapeTerminalControls()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var manifest = CreateManifest();
        Assert.Equal(0, RunCommand("assets", "build", manifest).ExitCode);
        var generated = Path.Combine(TestDir, "designs/generated");
        var hostileName = "orphan\nunchanged: forged.svg\u001b[2J";
        File.WriteAllText(Path.Combine(generated, hostileName), "keep");
        File.WriteAllText(Path.Combine(generated, "literal\\name.svg"), "backslash");
        Directory.CreateDirectory(Path.Combine(generated, "literal"));
        File.WriteAllText(Path.Combine(generated, "literal/name.svg"), "nested");

        var text = RunCommand("assets", "check", manifest);
        Assert.Equal(6, text.ExitCode);
        // CI enables Spectre's ANSI colours; reject the injected screen-clear escape,
        // while checking that the complete filename is emitted as one quoted value.
        Assert.DoesNotContain("\u001b[2J", text.Output, StringComparison.Ordinal);
        Assert.Contains($"unexpected: {JsonSerializer.Serialize("generated/" + hostileName)}", text.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\nunchanged: forged.svg", text.Output, StringComparison.Ordinal);
        Assert.Contains("\\nunchanged: forged.svg", text.Output, StringComparison.Ordinal);

        var json = RunCommand("assets", "check", manifest, "-f", "json");
        Assert.Equal(6, json.ExitCode);
        var paths = JsonDocument.Parse(json.Output).RootElement.GetProperty("data").GetProperty("files")
            .EnumerateArray().Select(file => file.GetProperty("path").GetString()).ToArray();
        Assert.Contains("generated/" + hostileName, paths);
        Assert.Contains("generated/literal\\name.svg", paths);
        Assert.Contains("generated/literal/name.svg", paths);
    }

    [Fact]
    public void EscapingOutput_IsRefusedWithoutWriting()
    {
        var manifest = CreateManifest();
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("generated/icon.svg", "../escape.svg"));
        var snapshot = Snapshot();
        var (code, output, _) = RunCommand("assets", "build", manifest, "-f", "json");
        Assert.Equal(1, code);
        Assert.Equal("PATH_ESCAPE", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void InvalidOutputFormat_AndMissingArguments_AreMachineReadable()
    {
        var (code, output, _) = RunCommand("assets", "check", CreateManifest(), "-f", "invalid");
        Assert.Equal(4, code);
        Assert.Equal("INVALID_FORMAT", JsonDocument.Parse(output).RootElement.GetProperty("error").GetProperty("code").GetString());
        var missing = RunCommand("assets", "check", "-f", "json");
        Assert.Equal(4, missing.ExitCode);
        Assert.False(JsonDocument.Parse(missing.Output).RootElement.GetProperty("ok").GetBoolean());
    }

    private string CreateManifest()
    {
        var directory = Path.Combine(TestDir, "designs");
        Directory.CreateDirectory(directory);
        var engine = new DocumentEngine();
        var document = engine.CreateDocument("icon", 24, 24);
        engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle", Width = 12, Height = 12, Fill = "#000000" });
        File.WriteAllText(Path.Combine(directory, "icon.agd.json"), DocumentSerializer.Serialize(document));
        var manifest = Path.Combine(directory, "assets.json");
        File.WriteAllText(manifest, """
            {"version":1,"outputRoot":"generated","defaults":{"profile":"web-icon"},
             "assets":[
               {"source":"icon.agd.json","output":"generated/icon.svg","options":{"currentColor":"#000"}},
               {"source":"icon.agd.json","output":"generated/icon.png"}],
             "sprites":[{"sources":["icon.agd.json"],"output":"generated/sprite.svg"}]}
            """);
        return manifest;
    }

    private string[] Snapshot() => Directory.GetFileSystemEntries(TestDir, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Directory.Exists(path) ? path + "/" : $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{Convert.ToHexString(File.ReadAllBytes(path))}")
        .ToArray();
}
