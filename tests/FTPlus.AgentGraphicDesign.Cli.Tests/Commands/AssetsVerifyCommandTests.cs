using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Cli.Commands;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using Spectre.Console.Cli;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public sealed class AssetsVerifyCommandTests : CommandTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_StillAndRepeatedFrames_ReturnExactSampleHashesWithoutWriting(bool clip)
    {
        var descriptor = CreateBundle(clip);
        var snapshot = Snapshot();

        var result = RunCommand("assets", "verify", descriptor, "-f", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        AssertSuccess(json.RootElement, descriptor);
        Assert.DoesNotContain(TestDir, result.Output);
        Assert.Equal(snapshot, Snapshot());
        Assert.False(Directory.Exists(Path.Combine(TestDir, ".agd")));
        Assert.False(File.Exists(Path.Combine(TestDir, ".agd-store.lock")));
    }

    [Fact]
    public void Verify_DifferentSamplesAndPivots_ReturnSortedUniqueHashesAndChangedMetadataDigest()
    {
        var descriptor = CreateBundle();
        // Stored, filter 0, hidden green under alpha zero; Adler-32 = 03020100.
        var second = PngWithIdat("7801010500FAFF0000FF000003020100");
        AddSecondFile(descriptor, second);
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        var files = (JsonArray)metadata["files"]!;
        var descending = files.Select(file => file!.DeepClone())
            .OrderByDescending(file => file["sha256"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        files.Clear();
        foreach (var file in descending)
            files.Add(file);
        File.WriteAllText(descriptor, metadata.ToJsonString());
        var secondHash = Convert.ToHexStringLower(SHA256.HashData(second));
        var samples = new Dictionary<string, string>
        {
            [metadata["clip"]!["frames"]![0]!["fileSha256"]!.GetValue<string>()] = "FF0000FF",
            [secondHash] = "00FF0000"
        };
        var snapshot = Snapshot();

        var first = RunCommand("assets", "verify", descriptor, "-f", "json");

        Assert.Equal(0, first.ExitCode);
        using var firstJson = JsonDocument.Parse(first.Output);
        AssertSuccess(firstJson.RootElement, descriptor, samples);
        Assert.Equal(snapshot, Snapshot());
        metadata["clip"]!["frames"]![1]!["pivot"]!["x"] = 0.5m;
        File.WriteAllText(descriptor, metadata.ToJsonString());
        snapshot = Snapshot();

        var movedPivot = RunCommand("assets", "verify", descriptor, "-f", "json");

        Assert.Equal(0, movedPivot.ExitCode);
        using var movedJson = JsonDocument.Parse(movedPivot.Output);
        AssertSuccess(movedJson.RootElement, descriptor, samples);
        var before = firstJson.RootElement.GetProperty("data");
        var after = movedJson.RootElement.GetProperty("data");
        Assert.NotEqual(before.GetProperty("metadataDigest").GetString(), after.GetProperty("metadataDigest").GetString());
        Assert.Equal(before.GetProperty("files").GetRawText(), after.GetProperty("files").GetRawText());
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void Verify_Text_StatesManagedSampleVerificationAndLimitsWithoutEchoingPrivateInput()
    {
        var descriptor = CreateBundle();
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        metadata["provenance"] = new JsonObject
        {
            ["producer"] = new JsonObject { ["id"] = "private-producer", ["version"] = "private-version" }
        };
        File.WriteAllText(descriptor, metadata.ToJsonString());
        var snapshot = Snapshot();

        var result = RunCommand("assets", "verify", descriptor);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("exact RGBA8 sample verification", result.Output);
        Assert.Contains("managed-in-process", result.Output);
        Assert.Contains("agd.png-rgba8-managed.v1", result.Output);
        Assert.Contains("Pixels decoded", result.Output);
        Assert.Contains("artifacts not admitted", result.Output);
        Assert.Contains("provenance not verified", result.Output);
        Assert.Contains("not an OS sandbox", result.Output);
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString("FF0000FF"))), result.Output);
        Assert.DoesNotContain(TestDir, result.Output);
        Assert.DoesNotContain("private-verification-input", result.Output);
        Assert.DoesNotContain("private-producer", result.Output);
        Assert.DoesNotContain("private-version", result.Output);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData("78010700000000", false)] // Reserved block type, also in PngStructureTests.
    [InlineData("789C03030000050001", false)] // Match before any history.
    [InlineData("78010700000000", true)]
    public void Verify_InvalidIdatWithMatchingOuterHash_PassesPreflightButReturnsNoPartialData(string compressedHex, bool secondFile)
    {
        var descriptor = CreateBundle();
        var png = PngWithIdat(compressedHex);
        if (secondFile)
            AddSecondFile(descriptor, png);
        else
            ReplaceImage(descriptor, png);
        var snapshot = Snapshot();

        var preflight = RunCommand("assets", "preflight", descriptor, "-f", "json");
        var verification = RunCommand("assets", "verify", descriptor, "-f", "json");

        Assert.Equal(0, preflight.ExitCode);
        using var preflightJson = JsonDocument.Parse(preflight.Output);
        Assert.True(preflightJson.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(preflightJson.RootElement.GetProperty("data").GetProperty("pixelsDecoded").GetBoolean());
        AssertError(verification, "ARTIFACT_PNG_INVALID", 1, secondFile ? "$.files[1]" : "$.files[0]");
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData("extra-file", "ARTIFACT_INVENTORY_MISMATCH", 1, null)]
    [InlineData("extra-directory", "ARTIFACT_INVENTORY_MISMATCH", 1, null)]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH", 1, "$.files[0]")]
    [InlineData("length", "ARTIFACT_CONTENT_MISMATCH", 1, "$.files[0]")]
    [InlineData("png", "ARTIFACT_PNG_INVALID", 1, "$.files[0]")]
    [InlineData("missing-payload", "ARTIFACT_INVENTORY_MISMATCH", 1, null)]
    [InlineData("missing-descriptor", "DOCUMENT_NOT_FOUND", 1, null)]
    [InlineData("json", "PARSE_ERROR", 1, "$")]
    [InlineData("metadata", "INVALID_ARGUMENTS", 4, "$.assetId")]
    [InlineData("metadataHash", "PARSE_ERROR", 1, "$")]
    [InlineData("version", "UNSUPPORTED_VERSION", 4, "$.version")]
    [InlineData("profile", "ARTIFACT_PROFILE_UNSUPPORTED", 1, "$.profile")]
    [InlineData("resource", "RESOURCE_LIMIT", 1, "$.canvas")]
    [InlineData("symlink", "PATH_ESCAPE", 1, null)]
    public void Verify_Failures_AreBoundedReadOnlyErrors(string failure, string code, int exitCode, string? location)
    {
        if (failure == "symlink" && !OperatingSystem.IsLinux())
            return;
        var descriptor = CreateBundle();
        var bundle = Path.GetDirectoryName(descriptor)!;
        var image = Path.Combine(bundle, "image.png");
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        switch (failure)
        {
            case "extra-file":
                File.WriteAllText(Path.Combine(bundle, ".private-verification-input"), "keep");
                break;
            case "extra-directory":
                Directory.CreateDirectory(Path.Combine(bundle, ".private-verification-input"));
                break;
            case "hash":
            case "png":
                var bytes = File.ReadAllBytes(image);
                bytes[0] ^= 1;
                File.WriteAllBytes(image, bytes);
                if (failure == "png")
                    ReplaceImage(descriptor, bytes);
                break;
            case "length":
                File.WriteAllBytes(image, File.ReadAllBytes(image)[..^1]);
                break;
            case "missing-payload":
                File.Delete(image);
                break;
            case "missing-descriptor":
                File.Delete(descriptor);
                break;
            case "json":
                File.WriteAllText(descriptor, "{private-verification-input");
                break;
            case "metadata":
                metadata["assetId"] = "/private-verification-input/" + new string('x', 900);
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "metadataHash":
                metadata["metadataHash"] = new string('0', 64);
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "version":
                metadata["version"] = 2;
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "profile":
                metadata["profile"] = "private-verification-input";
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "resource":
                metadata["canvas"]!["width"] = 8192;
                metadata["canvas"]!["height"] = 8192;
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "symlink":
                var target = Path.Combine(TestDir, "private-verification-input.png");
                File.Move(image, target);
                File.CreateSymbolicLink(image, target);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
        var snapshot = Snapshot();

        var result = RunCommand("assets", "verify", descriptor, "--format=json");

        AssertError(result, code, exitCode, location);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void Verify_LockedInput_ReturnsSanitizedIoFailureWithoutWriting()
    {
        var descriptor = CreateBundle();
        var snapshot = Snapshot();
        (int ExitCode, string Output, string Error) result;
        using (var lockedInput = new FileStream(descriptor, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = RunCommand("assets", "verify", descriptor, "-f", "json");

        AssertError(result, "ARTIFACT_INPUT_UNAVAILABLE", 1);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData("format", "INVALID_FORMAT")]
    [InlineData("long-format", "INVALID_FORMAT")]
    [InlineData("missing", "INVALID_ARGUMENTS")]
    [InlineData("output", "INVALID_ARGUMENTS")]
    [InlineData("check", "INVALID_ARGUMENTS")]
    public void Verify_InvalidArguments_ReturnExitFourWithoutWriting(string mode, string code)
    {
        var descriptor = CreateBundle();
        var snapshot = Snapshot();
        var format = mode == "long-format" ? new string('x', 2000) : "invalid";
        string[] args = mode switch
        {
            "format" or "long-format" => ["assets", "verify", descriptor, "-f", format],
            "missing" => ["assets", "verify", "-f", "json"],
            "output" => ["assets", "verify", descriptor, "-o", "report.json", "-f", "json"],
            _ => ["assets", "verify", descriptor, "--check", "-f", "json"]
        };

        var result = RunCommand(args);

        AssertError(result, code, 4);
        if (code == "INVALID_FORMAT")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal("Supported output formats are text and json.",
                json.RootElement.GetProperty("error").GetProperty("message").GetString());
            Assert.DoesNotContain(format, result.Output + result.Error);
        }
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public async Task Verify_PreCancelled_Returns130WithoutReadingOrWriting()
    {
        var snapshot = Snapshot();
        ICommand command = new AssetsVerifyCommand();
        using var output = new StringWriter();
        var original = Console.Out;
        int exitCode;
        try
        {
            Console.SetOut(output);
            exitCode = await command.ExecuteAsync(null!, new AssetsVerifyCommand.Settings
            {
                Descriptor = Path.Combine(TestDir, "private-verification-input.json"),
                Format = "json"
            }, new CancellationToken(canceled: true));
        }
        finally
        {
            Console.SetOut(original);
        }
        AssertError((exitCode, output.ToString(), ""), "CANCELLED", 130);
        Assert.Equal(snapshot, Snapshot());
    }

    private static void AssertSuccess(JsonElement envelope, string path, Dictionary<string, string>? samples = null)
    {
        var descriptor = ArtifactDescriptorContract.Parse(File.ReadAllBytes(path)).Descriptor!;
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.False(envelope.TryGetProperty("error", out _));
        var data = envelope.GetProperty("data");
        Assert.Equal(15, data.EnumerateObject().Count());
        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(descriptor), data.GetProperty("metadataDigest").GetString());
        Assert.False(data.TryGetProperty("metadataHash", out _));
        Assert.Equal(descriptor.Profile, data.GetProperty("profile").GetString());
        Assert.Equal(descriptor.AssetId, data.GetProperty("assetId").GetString());
        Assert.Equal(descriptor.Canvas.Width, data.GetProperty("width").GetInt64());
        Assert.Equal(descriptor.Canvas.Height, data.GetProperty("height").GetInt64());
        Assert.Equal(descriptor.Files.Length, data.GetProperty("fileCount").GetInt32());
        Assert.Equal(descriptor.Clip?.Frames.Length ?? 1, data.GetProperty("frameCount").GetInt32());
        Assert.Equal(descriptor.Files.Sum(file => file.ByteLength), data.GetProperty("totalBytes").GetInt64());
        Assert.Equal("decoded-rgba8", data.GetProperty("verification").GetString());
        Assert.Equal("agd.png-rgba8-managed.v1", data.GetProperty("decoderProfile").GetString());
        Assert.Equal("managed-in-process", data.GetProperty("execution").GetString());
        Assert.True(data.GetProperty("pixelsDecoded").GetBoolean());
        Assert.False(data.GetProperty("admitted").GetBoolean());
        Assert.False(data.GetProperty("provenanceVerified").GetBoolean());
        var files = data.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(descriptor.Files.Select(file => file.Sha256).Order(StringComparer.Ordinal),
            files.Select(file => file.GetProperty("fileSha256").GetString()));
        foreach (var file in files)
        {
            Assert.Equal(2, file.EnumerateObject().Count());
            var hash = file.GetProperty("fileSha256").GetString()!;
            var rgba = Convert.FromHexString(samples is null ? "FF0000FF" : samples[hash]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(rgba)), file.GetProperty("rgbaSha256").GetString());
        }
        Assert.DoesNotContain(Path.GetDirectoryName(path)!, envelope.GetRawText());
    }

    private void AssertError((int ExitCode, string Output, string Error) result, string code, int exitCode, string? location = null)
    {
        Assert.Equal(exitCode, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var envelope = json.RootElement;
        Assert.False(envelope.GetProperty("ok").GetBoolean());
        Assert.False(envelope.TryGetProperty("data", out _));
        Assert.False(envelope.TryGetProperty("files", out _));
        Assert.False(envelope.TryGetProperty("metadataDigest", out _));
        var error = envelope.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        var message = error.GetProperty("message").GetString()!;
        Assert.InRange(message.Length, 1, 1024);
        if (location is not null)
            Assert.Contains(location, message);
        Assert.DoesNotContain(TestDir, result.Output + result.Error);
        Assert.DoesNotContain("private-verification-input", result.Output + result.Error);
    }

    private string CreateBundle(bool clip = false)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FTPlus.AgentGraphicDesign.slnx")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Repository root not found");
        var fixtures = Path.Combine(root.FullName, "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures");
        var bundle = Path.Combine(TestDir, "private-verification-input");
        Directory.CreateDirectory(bundle);
        var png = Convert.FromBase64String(File.ReadAllText(Path.Combine(fixtures, "preflight-rgba.png.base64")));
        Assert.Equal(73, png.Length);
        File.WriteAllBytes(Path.Combine(bundle, "image.png"), png);
        var path = Path.Combine(bundle, "descriptor.json");
        File.Copy(Path.Combine(fixtures, "preflight-bundle.json"), path);
        if (clip)
        {
            var descriptor = ArtifactDescriptorContract.Parse(File.ReadAllBytes(path)).Descriptor!;
            var hash = Assert.Single(descriptor.Files).Sha256;
            descriptor = descriptor with
            {
                Still = null,
                Clip = new ArtifactClip("idle", "front", "default", 60, "forward-loop",
                [
                    new ArtifactFrame("first", hash, 1, new ArtifactPivot(0, 0)),
                    new ArtifactFrame("second", hash, 2, new ArtifactPivot(0.5m, -1))
                ])
            };
            File.WriteAllBytes(path, ArtifactDescriptorContract.SerializeCanonical(descriptor));
        }
        return path;
    }

    private static void ReplaceImage(string descriptor, byte[] png)
    {
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(descriptor)!, "image.png"), png);
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        var hash = Convert.ToHexStringLower(SHA256.HashData(png));
        metadata["files"]![0]!["sha256"] = hash;
        metadata["files"]![0]!["byteLength"] = png.Length;
        metadata["still"]!["fileSha256"] = hash;
        File.WriteAllText(descriptor, metadata.ToJsonString());
    }

    private static void AddSecondFile(string descriptor, byte[] png)
    {
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(descriptor)!, "second.png"), png);
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!.AsObject();
        var firstHash = metadata["files"]![0]!["sha256"]!.GetValue<string>();
        var hash = Convert.ToHexStringLower(SHA256.HashData(png));
        metadata["files"]!.AsArray().Add(new JsonObject { ["sha256"] = hash, ["path"] = "second.png", ["byteLength"] = png.Length });
        metadata.Remove("still");
        metadata["clip"] = new JsonObject
        {
            ["id"] = "idle",
            ["viewId"] = "front",
            ["variantId"] = "default",
            ["ticksPerSecond"] = 60,
            ["playback"] = "forward-loop",
            ["frames"] = new JsonArray(
                new JsonObject { ["name"] = "first", ["fileSha256"] = firstHash, ["durationTicks"] = 1, ["pivot"] = new JsonObject { ["x"] = 0, ["y"] = 0 } },
                new JsonObject { ["name"] = "second", ["fileSha256"] = hash, ["durationTicks"] = 2, ["pivot"] = new JsonObject { ["x"] = 0, ["y"] = 0 } })
        };
        File.WriteAllText(descriptor, metadata.ToJsonString());
    }

    private static byte[] PngWithIdat(string compressedHex)
    {
        var compressed = Convert.FromHexString(compressedHex);
        var chunk = new byte[compressed.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)compressed.Length);
        "IDAT"u8.CopyTo(chunk.AsSpan(4));
        compressed.CopyTo(chunk, 8);
        // Independent bitwise IEEE CRC, not the production PNG implementation.
        uint crc = 0xffffffff;
        foreach (var value in chunk.AsSpan(4, compressed.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(compressed.Length + 8), ~crc);
        return [.. Convert.FromHexString("89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C489"),
            .. chunk, .. Convert.FromHexString("0000000049454E44AE426082")];
    }

    private string[] Snapshot() => Directory.GetFileSystemEntries(TestDir, "*", SearchOption.AllDirectories)
        .Prepend(TestDir).Order(StringComparer.Ordinal)
        .Select(path =>
        {
            var attributes = File.GetAttributes(path);
            var contents = attributes.HasFlag(FileAttributes.ReparsePoint) ? new FileInfo(path).LinkTarget
                : attributes.HasFlag(FileAttributes.Directory) ? "/" : Convert.ToHexString(File.ReadAllBytes(path));
            return $"{path}|{attributes}|{File.GetLastWriteTimeUtc(path).Ticks}|{contents}";
        }).ToArray();
}
