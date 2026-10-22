using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

/// <summary>
/// Black-box coverage for the native pixel CLI branch (P6, archived plan slice 6). Exercises the public
/// <c>agd pixels</c> surface end to end: envelope shape, the native-code → exit-code mapping,
/// the completed non-success outcomes (invalid validation report → exit 2, output drift → exit 6),
/// and that preview is an explicit file write rather than terminal binary. Core semantics are
/// covered by the Core suite; these tests assert the CLI wiring and translation.
/// </summary>
public sealed partial class PixelsCommandTests : CommandTestBase
{
    private sealed record Created(string AssetId, long Revn, string SourceDigest, string LayerId);

    private Created CreateAsset(string dir, int width = 4, int height = 4, string name = "icon")
    {
        var result = RunCommand("pixels", "create", dir, "--name", name, "--width", width.ToString(), "--height", height.ToString(), "-f", "json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var snapshot = data.GetProperty("state").GetProperty("snapshot");
        return new Created(
            snapshot.GetProperty("assetId").GetString()!,
            snapshot.GetProperty("revn").GetInt64(),
            snapshot.GetProperty("sourceDigest").GetString()!,
            data.GetProperty("initialLayerId").GetString()!);
    }

    private string WriteFillRequest(Created asset, string name = "edit.json")
    {
        var path = Path.Combine(TestDir, name);
        File.WriteAllText(path, $$"""
        {
          "expectedSnapshot": { "assetId": "{{asset.AssetId}}", "revn": {{asset.Revn}}, "sourceDigest": "{{asset.SourceDigest}}" },
          "operations": [
            { "op": "fill", "layer": "{{asset.LayerId}}", "target": "pixels",
              "rect": { "x": 0, "y": 0, "width": 4, "height": 4 }, "rgba": "#ff0000ff" }
          ]
        }
        """);
        return path;
    }

    private string WriteWholeCanvasFillRequest(Created asset, int width, int height, string rgba, string name = "edit.json")
    {
        var path = Path.Combine(TestDir, name);
        File.WriteAllText(path, $$"""
        {
          "expectedSnapshot": { "assetId": "{{asset.AssetId}}", "revn": {{asset.Revn}}, "sourceDigest": "{{asset.SourceDigest}}" },
          "operations": [
            { "op": "fill", "layer": "{{asset.LayerId}}", "target": "pixels",
              "rect": { "x": 0, "y": 0, "width": {{width}}, "height": {{height}} }, "rgba": "{{rgba}}" }
          ]
        }
        """);
        return path;
    }

    private Created InspectAsset(string dir, string layerId)
    {
        var result = RunCommand("pixels", "inspect", dir, "-f", "json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var snapshot = json.RootElement.GetProperty("data").GetProperty("snapshot");
        return new Created(
            snapshot.GetProperty("assetId").GetString()!,
            snapshot.GetProperty("revn").GetInt64(),
            snapshot.GetProperty("sourceDigest").GetString()!,
            layerId);
    }

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = TestDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(' ', args)} did not exit");
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr.GetAwaiter().GetResult()}");
        }
    }

    [Theory]
    [InlineData(37, 23)]
    [InlineData(64, 64)]
    [InlineData(1, 64)]
    public void PixelLifecycle_AtBoundaryDimensions_ExportChecksThenDriftsAfterEdit(int width, int height)
    {
        var asset = CreateAsset("icon.pixels", width, height);

        Assert.Equal(0, RunCommand("pixels", "edit", "icon.pixels",
            "--request", WriteWholeCanvasFillRequest(asset, width, height, "#336699c0"), "-f", "json").ExitCode);
        Assert.Equal(0, RunCommand("pixels", "export", "icon.pixels", "--output", "out.png", "-f", "json").ExitCode);
        Assert.True(File.Exists(Path.Combine(TestDir, "out.png")));

        var check = RunCommand("pixels", "check", "icon.pixels", "--output", "out.png", "-f", "json");
        Assert.Equal(0, check.ExitCode);
        using (var json = JsonDocument.Parse(check.Output))
        {
            Assert.True(json.RootElement.GetProperty("data").GetProperty("matches").GetBoolean());
        }

        // A render-changing edit makes the already-exported PNG drift.
        var current = InspectAsset("icon.pixels", asset.LayerId);
        Assert.Equal(0, RunCommand("pixels", "edit", "icon.pixels",
            "--request", WriteWholeCanvasFillRequest(current, width, height, "#00ff00ff", "edit2.json"), "-f", "json").ExitCode);

        var drift = RunCommand("pixels", "check", "icon.pixels", "--output", "out.png", "-f", "json");
        Assert.Equal(6, drift.ExitCode);
        using (var json = JsonDocument.Parse(drift.Output))
        {
            Assert.Equal("PIXEL_OUTPUT_DRIFT", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public void GitRestore_ReturnsTheAssetToItsCommittedSnapshotAndComposite()
    {
        // The asset directory is the sole authoritative state (D-08 Git-first): after a user
        // reverts it with Git, agd must re-read the restored bytes and reproduce the exact committed
        // snapshot and export — with no hidden monotonic revision counter or out-of-tree state.
        var asset = CreateAsset("icon.pixels", 8, 8);
        Assert.Equal(0, RunCommand("pixels", "export", "icon.pixels", "--output", "baseline.png", "-f", "json").ExitCode);
        var baseline = File.ReadAllBytes(Path.Combine(TestDir, "baseline.png"));

        Git("init", "-q");
        Git("add", "icon.pixels");
        Git("-c", "user.email=agd@test.invalid", "-c", "user.name=agd", "commit", "-q", "-m", "asset");

        // Mutate the asset: the revision advances and the on-disk source digest changes.
        Assert.Equal(0, RunCommand("pixels", "edit", "icon.pixels",
            "--request", WriteWholeCanvasFillRequest(asset, 8, 8, "#00ff00ff"), "-f", "json").ExitCode);
        var edited = InspectAsset("icon.pixels", asset.LayerId);
        Assert.Equal(asset.Revn + 1, edited.Revn);
        Assert.NotEqual(asset.SourceDigest, edited.SourceDigest);

        // The user restores the committed bytes (tracked files from the index, then untracked payloads).
        Git("checkout", "--", "icon.pixels");
        Git("clean", "-fdq", "icon.pixels");

        var restored = InspectAsset("icon.pixels", asset.LayerId);
        Assert.Equal(asset.AssetId, restored.AssetId);
        Assert.Equal(asset.Revn, restored.Revn); // revn is source-derived, not a hidden counter that survives restore
        Assert.Equal(asset.SourceDigest, restored.SourceDigest);

        // A fresh export of the restored asset reproduces the committed composite byte-for-byte.
        Assert.Equal(0, RunCommand("pixels", "export", "icon.pixels", "--output", "restored.png", "-f", "json").ExitCode);
        Assert.Equal(baseline, File.ReadAllBytes(Path.Combine(TestDir, "restored.png")));
    }

    [Fact]
    public void Create_ThenInspect_ReturnSameSnapshotAndDimensions()
    {
        var asset = CreateAsset("icon.pixels");

        var inspect = RunCommand("pixels", "inspect", "icon.pixels", "-f", "json");

        Assert.Equal(0, inspect.ExitCode);
        Assert.Empty(inspect.Error);
        using var json = JsonDocument.Parse(inspect.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(asset.AssetId, data.GetProperty("snapshot").GetProperty("assetId").GetString());
        Assert.Equal(4, data.GetProperty("width").GetInt32());
        Assert.Equal(4, data.GetProperty("height").GetInt32());
        Assert.Single(data.GetProperty("layers").EnumerateArray());
    }

    [Fact]
    public void Validate_ValidAsset_ReportsValidWithExitZero()
    {
        CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "validate", "icon.pixels", "-f", "json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Validate_CorruptSource_ReportsInvalidReportWithExitTwo()
    {
        CreateAsset("icon.pixels");
        // An intrinsic source failure is a completed invalid report, not an execution error:
        // ok:true, data.valid:false, exit 2.
        File.WriteAllText(Path.Combine(TestDir, "icon.pixels", "asset.agd-pixels.json"), "{ not valid json }");

        var result = RunCommand("pixels", "validate", "icon.pixels", "-f", "json");

        Assert.Equal(2, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("valid").GetBoolean());
        Assert.NotEmpty(data.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public void Region_ReadsActualSamples()
    {
        var asset = CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "region", "icon.pixels", "--layer", asset.LayerId, "--target", "pixels", "--rect", "0,0,2,2", "-f", "json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(4, json.RootElement.GetProperty("data").GetProperty("samples").GetArrayLength());
    }

    [Fact]
    public void Edit_AppliesGuardedBatchAndAdvancesRevision()
    {
        var asset = CreateAsset("icon.pixels");
        var request = WriteFillRequest(asset);

        var result = RunCommand("pixels", "edit", "icon.pixels", "--request", request, "-f", "json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("changed").GetBoolean());
        Assert.Equal(asset.Revn + 1, data.GetProperty("state").GetProperty("snapshot").GetProperty("revn").GetInt64());
    }

    [Fact]
    public void Edit_StaleSnapshot_IsRevisionConflictExitOne()
    {
        var asset = CreateAsset("icon.pixels");
        var request = WriteFillRequest(asset);
        // First edit advances the revision, so the same request's snapshot is now stale.
        Assert.Equal(0, RunCommand("pixels", "edit", "icon.pixels", "--request", request, "-f", "json").ExitCode);

        var result = RunCommand("pixels", "edit", "icon.pixels", "--request", request, "-f", "json");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("REVISION_CONFLICT", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Export_ThenCheck_MatchesWithExitZero()
    {
        CreateAsset("icon.pixels");
        Assert.Equal(0, RunCommand("pixels", "export", "icon.pixels", "--output", "out.png", "-f", "json").ExitCode);
        Assert.True(File.Exists(Path.Combine(TestDir, "out.png")));

        var check = RunCommand("pixels", "check", "icon.pixels", "--output", "out.png", "-f", "json");

        Assert.Equal(0, check.ExitCode);
        using var json = JsonDocument.Parse(check.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("matches").GetBoolean());
    }

    [Fact]
    public void Check_StaleOutputAfterEdit_IsOutputDriftExitSix()
    {
        var asset = CreateAsset("icon.pixels");
        Assert.Equal(0, RunCommand("pixels", "export", "icon.pixels", "--output", "out.png", "-f", "json").ExitCode);
        // Change the source so the exported PNG no longer reflects a fresh evaluation.
        Assert.Equal(0, RunCommand("pixels", "edit", "icon.pixels", "--request", WriteFillRequest(asset), "-f", "json").ExitCode);

        var check = RunCommand("pixels", "check", "icon.pixels", "--output", "out.png", "-f", "json");

        Assert.Equal(6, check.ExitCode);
        using var json = JsonDocument.Parse(check.Output);
        // A completed mismatch: ok:false, comparison result still in data, error.code PIXEL_OUTPUT_DRIFT.
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("PIXEL_OUTPUT_DRIFT", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("matches").GetBoolean());
        Assert.Contains("samples", data.GetProperty("differences").EnumerateArray().Select(d => d.GetString()));
    }

    [Fact]
    public void Export_ExistingDestinationWithoutOverwrite_IsDestinationExistsExitOne()
    {
        CreateAsset("icon.pixels");
        Assert.Equal(0, RunCommand("pixels", "export", "icon.pixels", "--output", "out.png", "-f", "json").ExitCode);

        var second = RunCommand("pixels", "export", "icon.pixels", "--output", "out.png", "-f", "json");

        Assert.Equal(1, second.ExitCode);
        using var json = JsonDocument.Parse(second.Output);
        Assert.Equal("PIXEL_DESTINATION_EXISTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        var overwrite = RunCommand("pixels", "export", "icon.pixels", "--output", "out.png", "--overwrite", "-f", "json");
        Assert.Equal(0, overwrite.ExitCode);
    }

    [Fact]
    public void Preview_WritesFileWithoutInlineTerminalBinary()
    {
        CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "preview", "icon.pixels", "--output", "prev.png", "-f", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(TestDir, "prev.png")));
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("file", json.RootElement.GetProperty("data").GetProperty("delivery").GetString());
        // The CLI never emits inline base64/binary: no raw image bytes ride the envelope.
        Assert.DoesNotContain("pngBytes", result.Output);
    }

    [Fact]
    public void Inspect_MissingAsset_IsDocumentNotFoundExitOne()
    {
        var result = RunCommand("pixels", "inspect", "absent.pixels", "-f", "json");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void UnknownFormat_IsInvalidFormatExitFour()
    {
        CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "inspect", "icon.pixels", "-f", "yaml");

        Assert.Equal(4, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("INVALID_FORMAT", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("0,0,2")]      // too few
    [InlineData("0,0,2,2,2")]  // too many
    [InlineData("0,0,2,2.5")]  // fractional
    [InlineData("0,0,2,0x2")]  // non-decimal
    public void Region_MalformedRect_IsInvalidArgumentsExitFour(string rect)
    {
        var asset = CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "region", "icon.pixels", "--layer", asset.LayerId, "--target", "pixels", "--rect", rect, "-f", "json");

        Assert.Equal(4, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("INVALID_ARGUMENTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Region_MalformedLayerGuid_IsInvalidArgumentsExitFour()
    {
        CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "region", "icon.pixels", "--layer", "not-a-guid", "--target", "pixels", "--rect", "0,0,2,2", "-f", "json");

        Assert.Equal(4, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("INVALID_ARGUMENTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Edit_MissingRequestFile_IsInputUnavailableExitOne()
    {
        CreateAsset("icon.pixels");

        var result = RunCommand("pixels", "edit", "icon.pixels", "--request", "absent-request.json", "-f", "json");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("PIXEL_INPUT_UNAVAILABLE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Create_TextMode_RendersHumanSummary()
    {
        var result = RunCommand("pixels", "create", "icon.pixels", "--name", "icon", "--width", "4", "--height", "4");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Created", result.Output);
        // Text output identifies the captured source token without leaking the absolute test path.
        Assert.Contains("token", result.Output);
        Assert.DoesNotContain(TestDir, result.Output);
    }
}
