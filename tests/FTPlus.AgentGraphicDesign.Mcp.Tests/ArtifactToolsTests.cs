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

public sealed class ArtifactToolsTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"agd-artifact-tools-{Guid.NewGuid():N}");
    private readonly string _workingDirectory;
    private readonly ArtifactTools _tools;

    public ArtifactToolsTests()
    {
        _workingDirectory = Path.Combine(_testDir, "work");
        Directory.CreateDirectory(_workingDirectory);
        _tools = new ArtifactTools(new PathConfinement(_workingDirectory));
    }

    public void Dispose() => Directory.Delete(_testDir, recursive: true);

    [Fact]
    public void Construction_RequiresOnlyConfinement_AndDoesNotInitializeAStore()
    {
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        var missingRoot = Path.Combine(_testDir, "not-created");

        _ = new ArtifactTools(new PathConfinement(missingRoot));

        var constructor = Assert.Single(typeof(ArtifactTools).GetConstructors());
        Assert.Equal(typeof(PathConfinement), Assert.Single(constructor.GetParameters()).ParameterType);
        var method = typeof(ArtifactTools).GetMethod(nameof(ArtifactTools.PreflightArtifact))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal("preflight_artifact", attribute.Name);
        Assert.Equal("Preflight Artifact", attribute.Title);
        Assert.True(attribute.ReadOnly);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preflight_StillAndRepeatedFrames_ReturnSharedReadOnlyEnvelope(bool clip)
    {
        var descriptor = ArtifactTestBundle.Create(_workingDirectory, clip);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var result = await _tools.PreflightArtifact(Path.GetRelativePath(_workingDirectory, descriptor));

        Assert.False(result.IsError);
        ArtifactTestBundle.AssertSuccess(Envelope(result), descriptor, clip ? 2 : 1);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
        Assert.False(File.Exists(Path.Combine(_workingDirectory, ".agd-store.lock")));
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, ".agd")));
    }

    [Theory]
    [InlineData("extra-file", "ARTIFACT_INVENTORY_MISMATCH")]
    [InlineData("extra-directory", "ARTIFACT_INVENTORY_MISMATCH")]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("length", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("png", "ARTIFACT_PNG_INVALID")]
    [InlineData("missing-payload", "ARTIFACT_INVENTORY_MISMATCH")]
    [InlineData("missing-descriptor", "DOCUMENT_NOT_FOUND")]
    [InlineData("json", "PARSE_ERROR")]
    [InlineData("metadata", "INVALID_ARGUMENTS")]
    [InlineData("version", "UNSUPPORTED_VERSION")]
    [InlineData("profile", "ARTIFACT_PROFILE_UNSUPPORTED")]
    [InlineData("symlink", "PATH_ESCAPE")]
    public async Task Preflight_Failures_AreBoundedReadOnlyErrors(string failure, string code)
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
                {
                    var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    metadata["files"]![0]!["sha256"] = hash;
                    metadata["still"]!["fileSha256"] = hash;
                    File.WriteAllText(descriptor, metadata.ToJsonString());
                }
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
            case "version":
                metadata["version"] = 2;
                File.WriteAllText(descriptor, metadata.ToJsonString());
                break;
            case "profile":
                metadata["profile"] = "private-preflight-input";
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

        var result = await _tools.PreflightArtifact(descriptor);

        Assert.True(result.IsError);
        var envelope = Envelope(result);
        ArtifactTestBundle.AssertError(envelope, code);
        if (failure is "json" or "metadata" or "version" or "profile")
            Assert.Contains("$", envelope.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(_testDir, envelope.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Fact]
    public async Task Preflight_LockedInput_ReturnsSanitizedIoFailureWithoutWriting()
    {
        var descriptor = ArtifactTestBundle.Create(_workingDirectory);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        CallToolResult result;
        using (var lockedInput = new FileStream(descriptor, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await _tools.PreflightArtifact(descriptor);

        Assert.True(result.IsError);
        var envelope = Envelope(result);
        ArtifactTestBundle.AssertError(envelope, "ARTIFACT_INPUT_UNAVAILABLE");
        Assert.DoesNotContain(_testDir, envelope.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Fact]
    public async Task Preflight_UnsearchableInRootDirectory_ReturnsInputUnavailable()
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

                var result = await tools.PreflightArtifact(descriptor);

                Assert.True(result.IsError);
                var envelope = Envelope(result);
                ArtifactTestBundle.AssertError(envelope, "ARTIFACT_INPUT_UNAVAILABLE");
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
    public async Task Preflight_ConfinesDescriptorBeforeCoreReads(string mode)
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

        var result = await _tools.PreflightArtifact(path);

        Assert.True(result.IsError);
        ArtifactTestBundle.AssertError(Envelope(result), "PATH_ESCAPE");
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
    }

    [Fact]
    public async Task Preflight_PreCancelled_ReturnsCancellationBeforeConfinementOrReads()
    {
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);

        var result = await _tools.PreflightArtifact("../private-preflight-input.json", new CancellationToken(canceled: true));

        Assert.True(result.IsError);
        ArtifactTestBundle.AssertError(Envelope(result), "CANCELLED");
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

internal static class ArtifactTestBundle
{
    public static string Create(string root, bool clip = false)
    {
        var fixtures = Path.Combine(RepositoryPaths.Root, "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures");
        var bundle = Path.Combine(root, "private-preflight-input");
        Directory.CreateDirectory(bundle);
        File.WriteAllBytes(Path.Combine(bundle, "image.png"),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(fixtures, "preflight-rgba.png.base64"))));
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
                    new ArtifactFrame("second", hash, 2, new ArtifactPivot(0, 0))
                ])
            };
            File.WriteAllBytes(path, ArtifactDescriptorContract.SerializeCanonical(descriptor));
        }
        return path;
    }

    public static void AssertSuccess(JsonElement envelope, string descriptorPath, int frameCount)
    {
        var descriptor = ArtifactDescriptorContract.Parse(File.ReadAllBytes(descriptorPath)).Descriptor!;
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.False(envelope.TryGetProperty("error", out _));
        var data = envelope.GetProperty("data");
        Assert.Equal(13, data.EnumerateObject().Count());
        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(descriptor), data.GetProperty("metadataDigest").GetString());
        Assert.Equal(descriptor.Profile, data.GetProperty("profile").GetString());
        Assert.Equal(descriptor.AssetId, data.GetProperty("assetId").GetString());
        Assert.Equal(descriptor.Canvas.Width, data.GetProperty("width").GetInt64());
        Assert.Equal(descriptor.Canvas.Height, data.GetProperty("height").GetInt64());
        Assert.Equal(1, data.GetProperty("fileCount").GetInt32());
        Assert.Equal(frameCount, data.GetProperty("frameCount").GetInt32());
        Assert.Equal(new FileInfo(Path.Combine(Path.GetDirectoryName(descriptorPath)!, "image.png")).Length,
            data.GetProperty("totalBytes").GetInt64());
        Assert.Equal("encoded-integrity-and-png-structure", data.GetProperty("verification").GetString());
        Assert.Equal("agd.png-rgba8-structure.v1", data.GetProperty("pngPolicy").GetString());
        Assert.False(data.GetProperty("pixelsDecoded").GetBoolean());
        Assert.False(data.GetProperty("admitted").GetBoolean());
        Assert.False(data.GetProperty("provenanceVerified").GetBoolean());
        Assert.DoesNotContain(Path.GetDirectoryName(descriptorPath)!, envelope.GetRawText());
    }

    public static void AssertError(JsonElement envelope, string code)
    {
        Assert.False(envelope.GetProperty("ok").GetBoolean());
        Assert.False(envelope.TryGetProperty("data", out _));
        var error = envelope.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.InRange(error.GetProperty("message").GetString()!.Length, 1, 1024);
        Assert.DoesNotContain("private-preflight-input", envelope.GetRawText());
    }

    public static string[] Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Prepend(root).Order(StringComparer.Ordinal)
        .Select(path =>
        {
            var attributes = File.GetAttributes(path);
            var contents = attributes.HasFlag(FileAttributes.ReparsePoint) ? new FileInfo(path).LinkTarget
                : attributes.HasFlag(FileAttributes.Directory) ? "/" : Convert.ToHexString(File.ReadAllBytes(path));
            return $"{path}|{attributes}|{File.GetLastWriteTimeUtc(path).Ticks}|{contents}";
        }).ToArray();
}
