using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Tests.Pixels;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public partial class ProtocolTests
{
    private static JsonElement ImportedData(JsonElement result)
    {
        Assert.False(result.GetProperty("isError").GetBoolean(), result.ToString());
        var envelope = NativeEnvelope(result);
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        return envelope.GetProperty("data");
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(1, 17)]
    public async Task StillImport_RealWirePreservesHiddenSamplesEditsAndRestoresAfterServerRestart(int width, int height)
    {
        byte[] raw = [.. Enumerable.Range(0, width * height).SelectMany(i => i % 2 == 0 ? new byte[] { 17, 34, 51, 0 } : new byte[] { 255, 0, 0, 255 })];
        string descriptor = StillImportFixture.Write(Path.Combine(_testDir, "supplier"), width, height, raw);
        byte[] metadata = File.ReadAllBytes(descriptor), png = File.ReadAllBytes(Path.Combine(_testDir, "supplier/image.png"));
        await StartServerAsync(); await InitializeAsync();
        var created = ImportedData(await CallToolAsync("import_pixel_asset", new { assetDirectory = "chosen", descriptorPath = "supplier/artifact.json", name = "Chosen", layerName = "Original" }));
        var snapshot = created.GetProperty("state").GetProperty("snapshot").Clone();
        string layer = created.GetProperty("initialLayerId").GetString()!;
        var evidence = created.GetProperty("import");
        Assert.False(evidence.GetProperty("admitted").GetBoolean());
        Assert.False(evidence.GetProperty("provenanceVerified").GetBoolean());
        string asset = Path.Combine(_testDir, "chosen");
        string record = Path.Combine(asset, evidence.GetProperty("recordPath").GetString()!);
        Assert.Equal(metadata, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(record)!, "bundle/descriptor.json")));
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(record)!, "bundle/image.png")));
        var sourceFiles = Directory.GetFiles(asset, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(asset, path), File.ReadAllBytes);
        Directory.Delete(Path.Combine(_testDir, "supplier"), recursive: true);
        var samples = ImportedData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "chosen", layerId = layer, target = "pixels", rect = new { x = 0, y = 0, width, height } }));
        Assert.Equal("#11223300", samples.GetProperty("samples")[0].GetString());
        var exported = ImportedData(await CallToolAsync("export_pixel_asset", new { assetDirectory = "chosen", outputPath = "baseline.png" }));
        byte[] flat = [.. Enumerable.Range(0, width * height).SelectMany(i => i % 2 == 0 ? new byte[4] : new byte[] { 255, 0, 0, 255 })];
        Assert.Equal(StillImportFixture.Hash(flat), exported.GetProperty("rgbaSha256").GetString());
        var preview = await CallToolAsync("preview_pixel_asset", new { assetDirectory = "chosen" });
        Assert.Equal("image", preview.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal(exported.GetProperty("rgbaSha256").GetString(), ImportedData(preview).GetProperty("rgbaSha256").GetString());
        ImportedData(await CallToolAsync("edit_pixel_asset", new
        {
            assetDirectory = "chosen",
            request = new
            {
                expectedSnapshot = snapshot,
                operations = new object[] { new { op = "attachMask", layer }, new { op = "fill", layer, target = "mask", rect = new { x = 0, y = 0, width, height }, value = 0 } }
            }
        }));
        AssertWireError(await CallToolAsync("check_pixel_export", new { assetDirectory = "chosen", outputPath = "baseline.png" }), "PIXEL_OUTPUT_DRIFT");
        StopServer();
        foreach (var (path, bytes) in sourceFiles) File.WriteAllBytes(Path.Combine(asset, path), bytes);
        await StartServerAsync(); await InitializeAsync();
        Assert.Equal(snapshot.GetRawText(), ImportedData(await CallToolAsync("inspect_pixel_asset", new { assetDirectory = "chosen" })).GetProperty("snapshot").GetRawText());
        Assert.True(ImportedData(await CallToolAsync("check_pixel_export", new { assetDirectory = "chosen", outputPath = "baseline.png" })).GetProperty("matches").GetBoolean());
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(record)!, "bundle/image.png")));
    }

    [Fact]
    public async Task StillImport_RealWireAppendIsIndependentAndHasNativeCompositionAndStaleConflict()
    {
        StillImportFixture.Write(Path.Combine(_testDir, "red"), 1, 1, [255, 0, 0, 255]);
        StillImportFixture.Write(Path.Combine(_testDir, "green"), 1, 1, [0, 255, 0, 128]);
        await StartServerAsync(); await InitializeAsync();
        var first = ImportedData(await CallToolAsync("import_pixel_asset", new { assetDirectory = "chosen", descriptorPath = "red/artifact.json", name = "Color", layerName = "Red" }));
        var request = new { descriptorPath = "green/artifact.json", expectedSnapshot = first.GetProperty("state").GetProperty("snapshot"), layerName = "Green" };
        var second = ImportedData(await CallToolAsync("import_pixel_layer", new { assetDirectory = "chosen", request }));
        Assert.NotEqual(first.GetProperty("initialLayerId").GetString(), second.GetProperty("layerId").GetString());
        string redLayer = first.GetProperty("initialLayerId").GetString()!, greenLayer = second.GetProperty("layerId").GetString()!;
        var rect = new { x = 0, y = 0, width = 1, height = 1 };
        Assert.Equal("#ff0000ff", ImportedData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "chosen", layerId = redLayer, target = "pixels", rect })).GetProperty("samples")[0].GetString());
        Assert.Equal("#00ff0080", ImportedData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "chosen", layerId = greenLayer, target = "pixels", rect })).GetProperty("samples")[0].GetString());
        var exported = ImportedData(await CallToolAsync("export_pixel_asset", new { assetDirectory = "chosen", outputPath = "over.png" }));
        Assert.Equal(StillImportFixture.Hash([187, 188, 0, 255]), exported.GetProperty("rgbaSha256").GetString());
        Assert.True(ImportedData(await CallToolAsync("check_pixel_export", new { assetDirectory = "chosen", outputPath = "over.png" })).GetProperty("matches").GetBoolean());
        Assert.Equal(exported.GetProperty("rgbaSha256").GetString(), ImportedData(await CallToolAsync("preview_pixel_asset", new { assetDirectory = "chosen" })).GetProperty("rgbaSha256").GetString());
        var masked = ImportedData(await CallToolAsync("edit_pixel_asset", new
        {
            assetDirectory = "chosen",
            request = new
            {
                expectedSnapshot = second.GetProperty("state").GetProperty("snapshot"),
                operations = new object[] { new { op = "attachMask", layer = greenLayer }, new { op = "fill", layer = greenLayer, target = "mask", rect, value = 0 } }
            }
        }));
        Assert.Equal(0, ImportedData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "chosen", layerId = greenLayer, target = "mask", rect })).GetProperty("samples")[0].GetInt32());
        Assert.Equal("#ff0000ff", ImportedData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "chosen", layerId = redLayer, target = "pixels", rect })).GetProperty("samples")[0].GetString());
        Assert.Equal("#00ff0080", ImportedData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "chosen", layerId = greenLayer, target = "pixels", rect })).GetProperty("samples")[0].GetString());
        string redHash = StillImportFixture.Hash([255, 0, 0, 255]);
        Assert.Equal(redHash, ImportedData(await CallToolAsync("preview_pixel_asset", new { assetDirectory = "chosen" })).GetProperty("rgbaSha256").GetString());
        Assert.Equal(redHash, ImportedData(await CallToolAsync("export_pixel_asset", new { assetDirectory = "chosen", outputPath = "masked-over.png" })).GetProperty("rgbaSha256").GetString());
        Assert.True(ImportedData(await CallToolAsync("check_pixel_export", new { assetDirectory = "chosen", outputPath = "masked-over.png" })).GetProperty("matches").GetBoolean());
        AssertWireError(await CallToolAsync("check_pixel_export", new { assetDirectory = "chosen", outputPath = "over.png" }), "PIXEL_OUTPUT_DRIFT");
        AssertWireError(await CallToolAsync("import_pixel_layer", new { assetDirectory = "chosen", request }), "REVISION_CONFLICT");
        Assert.Equal(masked.GetProperty("state").GetProperty("snapshot").GetRawText(), ImportedData(await CallToolAsync("inspect_pixel_asset", new { assetDirectory = "chosen" })).GetProperty("snapshot").GetRawText());
    }

    [Theory]
    [InlineData("duplicate", "INVALID_ARGUMENTS")]
    [InlineData("unknown", "INVALID_ARGUMENTS")]
    [InlineData("null", "INVALID_ARGUMENTS")]
    [InlineData("exponent", "INVALID_ARGUMENTS")]
    [InlineData("escape", "PATH_ESCAPE")]
    [InlineData("oversize", "RESOURCE_LIMIT")]
    [InlineData("snapshot", "RESOURCE_LIMIT")]
    public async Task StillImport_RealWireOriginalRequestFailuresDoNotEditState(string fault, string code)
    {
        await StartServerAsync(); await InitializeAsync();
        var created = await CreatePixelAssetAsync("chosen", 1, 1);
        string snapshot = JsonSerializer.Serialize(new { assetId = created.AssetId, revn = created.Revn, sourceDigest = created.SourceDigest });
        string request = "{\"descriptorPath\":\"missing/artifact.json\",\"expectedSnapshot\":" + snapshot + ",\"layerName\":\"New\"}";
        request = fault switch
        {
            "duplicate" => request.Replace("\"layerName\":\"New\"", "\"layerName\":\"New\",\"\\u006cayerName\":\"Other\""),
            "unknown" => request.Replace("\"layerName\":\"New\"", "\"layerName\":\"New\",\"operations\":[]"),
            "null" => request.Replace("\"layerName\":\"New\"", "\"layerName\":null"),
            "exponent" => request.Replace("\"revn\":1", "\"revn\":1e0"),
            "escape" => request.Replace("missing/artifact.json", "../outside/artifact.json"),
            "oversize" => "{ " + new string(' ', 1_048_576) + request[1..],
            "snapshot" => request.Replace(snapshot, "{ " + new string(' ', 1024) + snapshot[1..]),
            _ => request
        };
        byte[] before = File.ReadAllBytes(Path.Combine(_testDir, "chosen/asset.agd-pixels.json"));
        var result = await SendRawToolCallAsync("import_pixel_layer", "{\"assetDirectory\":\"chosen\",\"request\":" + request + "}");
        AssertWireError(result, code);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_testDir, "chosen/asset.agd-pixels.json")));
        Assert.False(Directory.Exists(Path.Combine(_testDir, "chosen/imports")));
    }

    [Fact]
    public async Task StillImport_ExactRequestLimitAcceptsLargerOuterEnvelopeAndRejectsUnknownCreateArguments()
    {
        StillImportFixture.Write(Path.Combine(_testDir, "supplier"), 1, 1, [17, 34, 51, 0]);
        await StartServerAsync(); await InitializeAsync();
        var created = await CreatePixelAssetAsync("chosen", 1, 1);
        string snapshot = JsonSerializer.Serialize(new { assetId = created.AssetId, revn = created.Revn, sourceDigest = created.SourceDigest });
        string request = "{\"descriptorPath\":\"supplier/artifact.json\",\"expectedSnapshot\":" + snapshot + ",\"layerName\":\"New\"}";
        string bounded = "{" + new string(' ', 1_048_576 - request.Length) + request[1..];
        Assert.Equal(1_048_576, System.Text.Encoding.UTF8.GetByteCount(bounded));
        var imported = ImportedData(await SendRawToolCallAsync("import_pixel_layer", "{\"assetDirectory\":\"chosen\",\"request\":" + bounded + "}"));
        Assert.Equal(2, imported.GetProperty("state").GetProperty("snapshot").GetProperty("revn").GetInt32());
        AssertWireError(await SendRawToolCallAsync("import_pixel_asset", "{\"assetDirectory\":\"absent\",\"descriptorPath\":\"supplier/artifact.json\",\"name\":\"N\",\"layerName\":\"L\",\"unexpected\":true}"), "INVALID_ARGUMENTS");
        Assert.False(Directory.Exists(Path.Combine(_testDir, "absent")));
    }

    [Fact]
    public async Task StillImport_RealWireRawStorageFailurePreservesCodeStateAndSubsequentUsability()
    {
        if (OperatingSystem.IsWindows()) return;
        StillImportFixture.Write(Path.Combine(_testDir, "supplier"), 1, 1, [17, 34, 51, 0]);
        await StartServerAsync(); await InitializeAsync();
        var created = await CreatePixelAssetAsync("chosen", 1, 1);
        var request = new { descriptorPath = "supplier/artifact.json", expectedSnapshot = new { assetId = created.AssetId, revn = created.Revn, sourceDigest = created.SourceDigest }, layerName = "New" };
        string metadata = Path.Combine(_testDir, "chosen/asset.agd-pixels.json");
        byte[] before = File.ReadAllBytes(metadata);
        var mode = File.GetUnixFileMode(metadata);
        File.SetUnixFileMode(metadata, UnixFileMode.None);
        try { AssertWireError(await CallToolAsync("import_pixel_layer", new { assetDirectory = "chosen", request }), "STORE_UNAVAILABLE"); }
        finally { File.SetUnixFileMode(metadata, mode); }
        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Equal(2, ImportedData(await CallToolAsync("import_pixel_layer", new { assetDirectory = "chosen", request })).GetProperty("state").GetProperty("snapshot").GetProperty("revn").GetInt32());
    }
}
