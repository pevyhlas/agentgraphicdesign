using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public sealed class ArtifactVerificationToolsTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"agd-artifact-verification-tools-{Guid.NewGuid():N}");
    private readonly string _workingDirectory;
    private readonly ArtifactTools _tools;

    public ArtifactVerificationToolsTests()
    {
        _workingDirectory = Path.Combine(_testDir, "work");
        Directory.CreateDirectory(_workingDirectory);
        _tools = new ArtifactTools(new PathConfinement(_workingDirectory));
    }

    public void Dispose() => Directory.Delete(_testDir, recursive: true);

    [Fact]
    public void Verify_RequiresOnlyConfinement_AndHasExplicitReadOnlyRegistration()
    {
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        _ = new ArtifactTools(new PathConfinement(Path.Combine(_testDir, "not-created")));

        var constructor = Assert.Single(typeof(ArtifactTools).GetConstructors());
        Assert.Equal(typeof(PathConfinement), Assert.Single(constructor.GetParameters()).ParameterType);
        var method = typeof(ArtifactTools).GetMethod(nameof(ArtifactTools.VerifyArtifact))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal("verify_artifact", attribute.Name);
        Assert.Equal("Verify Artifact", attribute.Title);
        Assert.True(attribute.ReadOnly);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_StillAndRepeatedFrames_ReturnExactSampleHashesWithoutWriting(bool clip)
    {
        var descriptor = ArtifactTestBundle.Create(_workingDirectory, clip);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var result = await _tools.VerifyArtifact(Path.GetRelativePath(_workingDirectory, descriptor));

        Assert.False(result.IsError);
        ArtifactVerificationTestBundle.AssertSuccess(Envelope(result), descriptor);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
        Assert.False(File.Exists(Path.Combine(_workingDirectory, ".agd-store.lock")));
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, ".agd")));
    }

    [Fact]
    public async Task Verify_DifferentSamplesAndPivots_ReturnSortedUniqueHashesAndChangedMetadataDigest()
    {
        var descriptor = ArtifactTestBundle.Create(_workingDirectory);
        // Stored, filter 0, hidden green under alpha zero; Adler-32 = 03020100.
        var second = ArtifactVerificationTestBundle.PngWithIdat("7801010500FAFF0000FF000003020100");
        ArtifactVerificationTestBundle.AddSecondFile(descriptor, second);
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        var files = metadata["files"]!.AsArray();
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
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var first = await _tools.VerifyArtifact(descriptor);

        Assert.False(first.IsError);
        var firstEnvelope = Envelope(first);
        ArtifactVerificationTestBundle.AssertSuccess(firstEnvelope, descriptor, samples);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
        metadata["clip"]!["frames"]![1]!["pivot"]!["x"] = 0.5m;
        File.WriteAllText(descriptor, metadata.ToJsonString());
        snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var movedPivot = await _tools.VerifyArtifact(descriptor);

        Assert.False(movedPivot.IsError);
        var movedEnvelope = Envelope(movedPivot);
        ArtifactVerificationTestBundle.AssertSuccess(movedEnvelope, descriptor, samples);
        var before = firstEnvelope.GetProperty("data");
        var after = movedEnvelope.GetProperty("data");
        Assert.NotEqual(before.GetProperty("metadataDigest").GetString(), after.GetProperty("metadataDigest").GetString());
        Assert.Equal(before.GetProperty("files").GetRawText(), after.GetProperty("files").GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Theory]
    [InlineData("78010700000000", false)] // Reserved block type, also in PngStructureTests.
    [InlineData("789C03030000050001", false)] // Match before any history.
    [InlineData("78010700000000", true)]
    public async Task Verify_InvalidIdatWithMatchingOuterHash_PassesPreflightButReturnsNoPartialData(string compressedHex, bool secondFile)
    {
        var descriptor = ArtifactTestBundle.Create(_workingDirectory);
        var png = ArtifactVerificationTestBundle.PngWithIdat(compressedHex);
        if (secondFile)
            ArtifactVerificationTestBundle.AddSecondFile(descriptor, png);
        else
            ArtifactVerificationTestBundle.ReplaceImage(descriptor, png);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var preflight = await _tools.PreflightArtifact(descriptor);
        var verification = await _tools.VerifyArtifact(descriptor);

        Assert.False(preflight.IsError);
        var preflightEnvelope = Envelope(preflight);
        Assert.True(preflightEnvelope.GetProperty("ok").GetBoolean());
        Assert.False(preflightEnvelope.GetProperty("data").GetProperty("pixelsDecoded").GetBoolean());
        Assert.True(verification.IsError);
        var envelope = Envelope(verification);
        ArtifactVerificationTestBundle.AssertError(envelope, "ARTIFACT_PNG_INVALID");
        Assert.Contains(secondFile ? "$.files[1]" : "$.files[0]", envelope.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Theory]
    [InlineData("extra-file", "ARTIFACT_INVENTORY_MISMATCH", null)]
    [InlineData("extra-directory", "ARTIFACT_INVENTORY_MISMATCH", null)]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH", "$.files[0]")]
    [InlineData("length", "ARTIFACT_CONTENT_MISMATCH", "$.files[0]")]
    [InlineData("png", "ARTIFACT_PNG_INVALID", "$.files[0]")]
    [InlineData("missing-payload", "ARTIFACT_INVENTORY_MISMATCH", null)]
    [InlineData("missing-descriptor", "DOCUMENT_NOT_FOUND", null)]
    [InlineData("json", "PARSE_ERROR", "$")]
    [InlineData("metadata", "INVALID_ARGUMENTS", "$.assetId")]
    [InlineData("metadataHash", "PARSE_ERROR", "$")]
    [InlineData("version", "UNSUPPORTED_VERSION", "$.version")]
    [InlineData("profile", "ARTIFACT_PROFILE_UNSUPPORTED", "$.profile")]
    [InlineData("resource", "RESOURCE_LIMIT", "$.canvas")]
    [InlineData("symlink", "PATH_ESCAPE", null)]
    public async Task Verify_Failures_AreBoundedReadOnlyErrors(string failure, string code, string? location)
    {
        if (failure == "symlink" && !OperatingSystem.IsLinux())
            return;
        var descriptor = ArtifactTestBundle.Create(_workingDirectory);
        var bundle = Path.GetDirectoryName(descriptor)!;
        var image = Path.Combine(bundle, "image.png");
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        switch (failure)
        {
            case "extra-file":
                File.WriteAllText(Path.Combine(bundle, ".private-preflight-input"), "keep");
                break;
            case "extra-directory":
                Directory.CreateDirectory(Path.Combine(bundle, ".private-preflight-input"));
                break;
            case "hash":
            case "png":
                var bytes = File.ReadAllBytes(image);
                bytes[0] ^= 1;
                File.WriteAllBytes(image, bytes);
                if (failure == "png")
                    ArtifactVerificationTestBundle.ReplaceImage(descriptor, bytes);
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
                File.WriteAllText(descriptor, "{private-preflight-input");
                break;
            case "metadata":
                metadata["assetId"] = "/private-preflight-input/" + new string('x', 900);
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
                metadata["profile"] = "private-preflight-input";
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "resource":
                metadata["canvas"]!["width"] = 8192;
                metadata["canvas"]!["height"] = 8192;
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "symlink":
                var target = Path.Combine(_testDir, "private-preflight-input.png");
                File.Move(image, target);
                File.CreateSymbolicLink(image, target);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var result = await _tools.VerifyArtifact(descriptor);

        Assert.True(result.IsError);
        var envelope = Envelope(result);
        ArtifactVerificationTestBundle.AssertError(envelope, code);
        if (location is not null)
            Assert.Contains(location, envelope.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(_testDir, envelope.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Fact]
    public async Task Verify_LockedInput_ReturnsSanitizedIoFailureWithoutWriting()
    {
        var descriptor = ArtifactTestBundle.Create(_workingDirectory);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        CallToolResult result;
        using (var lockedInput = new FileStream(descriptor, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await _tools.VerifyArtifact(descriptor);

        Assert.True(result.IsError);
        var envelope = Envelope(result);
        ArtifactVerificationTestBundle.AssertError(envelope, "ARTIFACT_INPUT_UNAVAILABLE");
        Assert.DoesNotContain(_testDir, envelope.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Fact]
    public async Task Verify_UnsearchableInRootDirectory_ReturnsInputUnavailable()
    {
        if (OperatingSystem.IsWindows())
            return;
        var descriptor = ArtifactTestBundle.Create(_workingDirectory);
        var directory = Path.GetDirectoryName(descriptor)!;
        var confinement = new PathConfinement(_workingDirectory);
        var tools = new ArtifactTools(confinement);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        var mode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.None);
            try
            {
                _ = File.GetAttributes(descriptor);
                return; // Privileged accounts can bypass mode bits; this is not access-denial evidence.
            }
            catch (UnauthorizedAccessException)
            {
                var confined = confinement.Confine(descriptor);
                Assert.False(confined.Success);
                Assert.Equal("ACCESS_DENIED", confined.ErrorCode);

                var result = await tools.VerifyArtifact(descriptor);

                Assert.True(result.IsError);
                var envelope = Envelope(result);
                ArtifactVerificationTestBundle.AssertError(envelope, "ARTIFACT_INPUT_UNAVAILABLE");
                Assert.Equal("Artifact input is unavailable.", envelope.GetProperty("error").GetProperty("message").GetString());
                Assert.DoesNotContain(_testDir, envelope.GetRawText());
            }
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
            Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
        }
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("absolute")]
    [InlineData("missing")]
    [InlineData("symlink")]
    public async Task Verify_ConfinesDescriptorBeforeCoreReads(string mode)
    {
        if (mode == "symlink" && !OperatingSystem.IsLinux())
            return;
        var outside = Path.Combine(_testDir, "private-preflight-input.json");
        File.WriteAllText(outside, "{private-preflight-input");
        var path = mode switch
        {
            "relative" => "../private-preflight-input.json",
            "absolute" => outside,
            "missing" => "../missing/private-preflight-input.json",
            _ => Path.Combine(_workingDirectory, "descriptor-link.json")
        };
        if (mode == "symlink")
            File.CreateSymbolicLink(path, outside);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var result = await _tools.VerifyArtifact(path);

        Assert.True(result.IsError);
        var envelope = Envelope(result);
        ArtifactVerificationTestBundle.AssertError(envelope, "PATH_ESCAPE");
        Assert.DoesNotContain(_testDir, envelope.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Fact]
    public async Task Verify_PreCancelled_ReturnsCancellationBeforeConfinementOrReads()
    {
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var result = await _tools.VerifyArtifact("../private-preflight-input.json", new CancellationToken(canceled: true));

        Assert.True(result.IsError);
        ArtifactVerificationTestBundle.AssertError(Envelope(result), "CANCELLED");
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    private static JsonElement Envelope(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(structured.GetRawText(), text);
        return structured;
    }
}

internal static class ArtifactVerificationTestBundle
{
    public static void AssertSuccess(JsonElement envelope, string path, Dictionary<string, string>? samples = null)
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
        Assert.DoesNotContain("private-preflight-input", envelope.GetRawText());
    }

    public static void AssertError(JsonElement envelope, string code)
    {
        ArtifactTestBundle.AssertError(envelope, code);
        Assert.False(envelope.TryGetProperty("files", out _));
        Assert.False(envelope.TryGetProperty("metadataDigest", out _));
    }

    public static void ReplaceImage(string descriptor, byte[] png)
    {
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(descriptor)!, "image.png"), png);
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        var hash = Convert.ToHexStringLower(SHA256.HashData(png));
        metadata["files"]![0]!["sha256"] = hash;
        metadata["files"]![0]!["byteLength"] = png.Length;
        metadata["still"]!["fileSha256"] = hash;
        File.WriteAllText(descriptor, metadata.ToJsonString());
    }

    public static void AddSecondFile(string descriptor, byte[] png)
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

    public static byte[] PngWithIdat(string compressedHex)
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
}
