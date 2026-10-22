using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Tests.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public sealed partial class PixelsCommandTests
{
    private JsonElement ImportData(params string[] args)
    {
        var result = RunCommand(args);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        return json.RootElement.GetProperty("data").Clone();
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(1, 17)]
    public void StillImport_CliPreservesInspectsEditsExportsAndGitRestoresChosenProject(int width, int height)
    {
        byte[] raw = [.. Enumerable.Range(0, width * height).SelectMany(i => i % 2 == 0 ? new byte[] { 17, 34, 51, 0 } : new byte[] { 255, 0, 0, 255 })];
        string descriptor = StillImportFixture.Write(Path.Combine(TestDir, "supplier"), width, height, raw);
        byte[] metadata = File.ReadAllBytes(descriptor), png = File.ReadAllBytes(Path.Combine(TestDir, "supplier/image.png"));
        var imported = ImportData("pixels", "import", "chosen", "--descriptor", "supplier/artifact.json", "--name", "Chosen", "--layer-name", "Original", "-f", "json");
        var evidence = imported.GetProperty("import");
        Assert.False(evidence.GetProperty("admitted").GetBoolean());
        Assert.False(evidence.GetProperty("provenanceVerified").GetBoolean());
        string layer = imported.GetProperty("initialLayerId").GetString()!;
        string record = Path.Combine(TestDir, "chosen", evidence.GetProperty("recordPath").GetString()!);
        Assert.Equal(metadata, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(record)!, "bundle/descriptor.json")));
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(record)!, "bundle/image.png")));
        Directory.Delete(Path.Combine(TestDir, "supplier"), recursive: true);
        var region = ImportData("pixels", "region", "chosen", "--layer", layer, "--target", "pixels", "--rect", $"0,0,{width},{height}", "-f", "json");
        Assert.Equal("#11223300", region.GetProperty("samples")[0].GetString());
        var baseline = ImportData("pixels", "export", "chosen", "--output", "baseline.png", "-f", "json");
        byte[] flattened = [.. Enumerable.Range(0, width * height).SelectMany(i => i % 2 == 0 ? new byte[4] : new byte[] { 255, 0, 0, 255 })];
        Assert.Equal(StillImportFixture.Hash(flattened), baseline.GetProperty("rgbaSha256").GetString());
        Git("init", "-q"); Git("add", "chosen");
        Git("-c", "user.email=agd@test.invalid", "-c", "user.name=agd", "commit", "-q", "-m", "preserved import");
        var snapshot = imported.GetProperty("state").GetProperty("snapshot");
        string edit = Path.Combine(TestDir, "edit-import.json");
        File.WriteAllText(edit, JsonSerializer.Serialize(new
        {
            expectedSnapshot = snapshot,
            operations = new object[] { new { op = "attachMask", layer }, new { op = "fill", layer, target = "mask", rect = new { x = 0, y = 0, width, height }, value = 0 } }
        }));
        ImportData("pixels", "edit", "chosen", "--request", edit, "-f", "json");
        Assert.Equal(6, RunCommand("pixels", "check", "chosen", "--output", "baseline.png", "-f", "json").ExitCode);
        ImportData("pixels", "preview", "chosen", "--output", "masked.png", "-f", "json");
        Git("checkout", "--", "chosen"); Git("clean", "-fdq", "chosen");
        var restored = ImportData("pixels", "inspect", "chosen", "-f", "json");
        Assert.True(JsonElement.DeepEquals(snapshot, restored.GetProperty("snapshot")));
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(record)!, "bundle/image.png")));
        Assert.True(ImportData("pixels", "check", "chosen", "--output", "baseline.png", "-f", "json").GetProperty("matches").GetBoolean());
    }

    [Fact]
    public void StillImport_CliAppendUsesInvocationRootAndRejectsOriginalDuplicatesAndStaleTokens()
    {
        StillImportFixture.Write(Path.Combine(TestDir, "red"), 1, 1, [255, 0, 0, 255]);
        StillImportFixture.Write(Path.Combine(TestDir, "green"), 1, 1, [0, 255, 0, 128]);
        var imported = ImportData("pixels", "import", "chosen", "--descriptor", "red/artifact.json", "--name", "Color", "--layer-name", "Red", "-f", "json");
        Directory.CreateDirectory(Path.Combine(TestDir, "requests"));
        string file = Path.Combine(TestDir, "requests/import.json");
        string request = JsonSerializer.Serialize(new { descriptorPath = "green/artifact.json", expectedSnapshot = imported.GetProperty("state").GetProperty("snapshot"), layerName = "Green" });
        File.WriteAllText(file, request.Replace("\"layerName\":\"Green\"", "\"layerName\":\"Green\",\"\\u006cayerName\":\"Other\""));
        var duplicate = RunCommand("pixels", "import-layer", "chosen", "--request", file, "-f", "json");
        Assert.Equal(4, duplicate.ExitCode);
        File.WriteAllText(file, request);
        var appended = ImportData("pixels", "import-layer", "chosen", "--request", file, "-f", "json");
        Assert.Equal(2, appended.GetProperty("state").GetProperty("snapshot").GetProperty("revn").GetInt32());
        string redLayer = imported.GetProperty("initialLayerId").GetString()!;
        string greenLayer = appended.GetProperty("layerId").GetString()!;
        Assert.Equal("#ff0000ff", ImportData("pixels", "region", "chosen", "--layer", redLayer, "--target", "pixels", "--rect", "0,0,1,1", "-f", "json").GetProperty("samples")[0].GetString());
        Assert.Equal("#00ff0080", ImportData("pixels", "region", "chosen", "--layer", greenLayer, "--target", "pixels", "--rect", "0,0,1,1", "-f", "json").GetProperty("samples")[0].GetString());
        var exported = ImportData("pixels", "export", "chosen", "--output", "over.png", "-f", "json");
        Assert.Equal(StillImportFixture.Hash([187, 188, 0, 255]), exported.GetProperty("rgbaSha256").GetString());
        Assert.True(ImportData("pixels", "check", "chosen", "--output", "over.png", "-f", "json").GetProperty("matches").GetBoolean());
        Assert.Equal(exported.GetProperty("rgbaSha256").GetString(), ImportData("pixels", "preview", "chosen", "--output", "over-preview.png", "-f", "json").GetProperty("rgbaSha256").GetString());
        string maskRequest = Path.Combine(TestDir, "requests/mask.json");
        File.WriteAllText(maskRequest, JsonSerializer.Serialize(new
        {
            expectedSnapshot = appended.GetProperty("state").GetProperty("snapshot"),
            operations = new object[] { new { op = "attachMask", layer = greenLayer }, new { op = "fill", layer = greenLayer, target = "mask", rect = new { x = 0, y = 0, width = 1, height = 1 }, value = 0 } }
        }));
        var masked = ImportData("pixels", "edit", "chosen", "--request", maskRequest, "-f", "json");
        Assert.Equal(0, ImportData("pixels", "region", "chosen", "--layer", greenLayer, "--target", "mask", "--rect", "0,0,1,1", "-f", "json").GetProperty("samples")[0].GetInt32());
        Assert.Equal("#ff0000ff", ImportData("pixels", "region", "chosen", "--layer", redLayer, "--target", "pixels", "--rect", "0,0,1,1", "-f", "json").GetProperty("samples")[0].GetString());
        Assert.Equal("#00ff0080", ImportData("pixels", "region", "chosen", "--layer", greenLayer, "--target", "pixels", "--rect", "0,0,1,1", "-f", "json").GetProperty("samples")[0].GetString());
        string redHash = StillImportFixture.Hash([255, 0, 0, 255]);
        Assert.Equal(redHash, ImportData("pixels", "preview", "chosen", "--output", "masked-preview.png", "-f", "json").GetProperty("rgbaSha256").GetString());
        Assert.Equal(redHash, ImportData("pixels", "export", "chosen", "--output", "masked-over.png", "-f", "json").GetProperty("rgbaSha256").GetString());
        Assert.True(ImportData("pixels", "check", "chosen", "--output", "masked-over.png", "-f", "json").GetProperty("matches").GetBoolean());
        Assert.Equal(6, RunCommand("pixels", "check", "chosen", "--output", "over.png", "-f", "json").ExitCode);
        var stale = RunCommand("pixels", "import-layer", "chosen", "--request", file, "-f", "json");
        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("REVISION_CONFLICT", stale.Output);
        Assert.True(JsonElement.DeepEquals(masked.GetProperty("state").GetProperty("snapshot"), ImportData("pixels", "inspect", "chosen", "-f", "json").GetProperty("snapshot")));
    }

    [Fact]
    public void StillImport_CliRawNativeStorageAccessFailureHasSharedCodeAndUnchangedState()
    {
        if (OperatingSystem.IsWindows()) return;
        StillImportFixture.Write(Path.Combine(TestDir, "supplier"), 1, 1, [17, 34, 51, 0]);
        var initial = ImportData("pixels", "create", "chosen", "--name", "Asset", "--width", "1", "--height", "1", "-f", "json");
        string request = Path.Combine(TestDir, "import.json");
        File.WriteAllText(request, JsonSerializer.Serialize(new { descriptorPath = "supplier/artifact.json", expectedSnapshot = initial.GetProperty("state").GetProperty("snapshot"), layerName = "New" }));
        string metadata = Path.Combine(TestDir, "chosen/asset.agd-pixels.json");
        byte[] before = File.ReadAllBytes(metadata);
        var mode = File.GetUnixFileMode(metadata);
        File.SetUnixFileMode(metadata, UnixFileMode.None);
        try
        {
            var result = RunCommand("pixels", "import-layer", "chosen", "--request", request, "-f", "json");
            Assert.Equal(1, result.ExitCode);
            using var error = JsonDocument.Parse(result.Output);
            Assert.Equal("STORE_UNAVAILABLE", error.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally { File.SetUnixFileMode(metadata, mode); }
        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Equal(2, ImportData("pixels", "import-layer", "chosen", "--request", request, "-f", "json").GetProperty("state").GetProperty("snapshot").GetProperty("revn").GetInt32());
    }
}
