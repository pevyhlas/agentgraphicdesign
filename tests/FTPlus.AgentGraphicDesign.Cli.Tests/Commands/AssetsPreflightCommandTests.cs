using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Cli.Commands;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using Spectre.Console.Cli;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public sealed class AssetsPreflightCommandTests : CommandTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preflight_StillAndRepeatedFrames_ReturnBoundedEnvelopeWithoutWriting(bool clip)
    {
        var descriptorPath = CreateBundle(clip);
        var descriptor = ArtifactDescriptorContract.Parse(File.ReadAllBytes(descriptorPath)).Descriptor!;
        var snapshot = Snapshot();

        var result = RunCommand("assets", "preflight", descriptorPath, "-f", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var envelope = json.RootElement;
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
        Assert.Equal(clip ? 2 : 1, data.GetProperty("frameCount").GetInt32());
        Assert.Equal(new FileInfo(Path.Combine(Path.GetDirectoryName(descriptorPath)!, "image.png")).Length,
            data.GetProperty("totalBytes").GetInt64());
        Assert.Equal("encoded-integrity-and-png-structure", data.GetProperty("verification").GetString());
        Assert.Equal("agd.png-rgba8-structure.v1", data.GetProperty("pngPolicy").GetString());
        Assert.False(data.GetProperty("pixelsDecoded").GetBoolean());
        Assert.False(data.GetProperty("admitted").GetBoolean());
        Assert.False(data.GetProperty("provenanceVerified").GetBoolean());
        Assert.DoesNotContain(TestDir, result.Output);
        Assert.Equal(snapshot, Snapshot());
        Assert.False(Directory.Exists(Path.Combine(TestDir, ".agd")));
        Assert.False(File.Exists(Path.Combine(TestDir, ".agd-store.lock")));
    }

    [Fact]
    public void Preflight_Text_StatesLimitsWithoutEchoingPathsOrProvenance()
    {
        var descriptor = CreateBundle();
        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        metadata["provenance"] = new JsonObject
        {
            ["producer"] = new JsonObject { ["id"] = "private-producer", ["version"] = "private-version" }
        };
        File.WriteAllText(descriptor, metadata.ToJsonString());
        var snapshot = Snapshot();

        var result = RunCommand("assets", "preflight", descriptor, "-f", "text");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("encoded integrity and PNG structure only", result.Output);
        Assert.Contains("Pixels not decoded", result.Output);
        Assert.Contains("artifacts not admitted", result.Output);
        Assert.Contains("provenance not verified", result.Output);
        Assert.Contains("files: 1; frames: 1;", result.Output);
        Assert.DoesNotContain(TestDir, result.Output);
        Assert.DoesNotContain("private-preflight-input", result.Output);
        Assert.DoesNotContain("private-producer", result.Output);
        Assert.DoesNotContain("private-version", result.Output);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData("extra-file", "ARTIFACT_INVENTORY_MISMATCH", 1)]
    [InlineData("extra-directory", "ARTIFACT_INVENTORY_MISMATCH", 1)]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH", 1)]
    [InlineData("length", "ARTIFACT_CONTENT_MISMATCH", 1)]
    [InlineData("png", "ARTIFACT_PNG_INVALID", 1)]
    [InlineData("missing-payload", "ARTIFACT_INVENTORY_MISMATCH", 1)]
    [InlineData("missing-descriptor", "DOCUMENT_NOT_FOUND", 1)]
    [InlineData("json", "PARSE_ERROR", 1)]
    [InlineData("metadata", "INVALID_ARGUMENTS", 4)]
    [InlineData("version", "UNSUPPORTED_VERSION", 4)]
    [InlineData("profile", "ARTIFACT_PROFILE_UNSUPPORTED", 1)]
    [InlineData("symlink", "PATH_ESCAPE", 1)]
    public void Preflight_Failures_AreBoundedReadOnlyErrors(string failure, string errorCode, int exitCode)
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
                var target = Path.Combine(TestDir, "private-preflight-input.png");
                File.Move(image, target);
                File.CreateSymbolicLink(image, target);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
        var snapshot = Snapshot();

        var result = RunCommand("assets", "preflight", descriptor, "--format=json");

        Assert.Equal(exitCode, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(errorCode, error.GetProperty("code").GetString());
        Assert.InRange(error.GetProperty("message").GetString()!.Length, 1, 1024);
        if (failure is "json" or "metadata" or "version" or "profile")
            Assert.Contains("$", error.GetProperty("message").GetString());
        Assert.DoesNotContain(TestDir, result.Output + result.Error);
        Assert.DoesNotContain("private-preflight-input", result.Output + result.Error);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void Preflight_LockedInput_ReturnsSanitizedIoFailureWithoutWriting()
    {
        var descriptor = CreateBundle();
        var snapshot = Snapshot();
        (int ExitCode, string Output, string Error) result;
        using (var lockedInput = new FileStream(descriptor, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = RunCommand("assets", "preflight", descriptor, "-f", "json");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("ARTIFACT_INPUT_UNAVAILABLE", error.GetProperty("code").GetString());
        Assert.InRange(error.GetProperty("message").GetString()!.Length, 1, 1024);
        Assert.DoesNotContain(TestDir, result.Output + result.Error);
        Assert.DoesNotContain("private-preflight-input", result.Output + result.Error);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData("format", "INVALID_FORMAT")]
    [InlineData("long-format", "INVALID_FORMAT")]
    [InlineData("missing", "INVALID_ARGUMENTS")]
    [InlineData("output", "INVALID_ARGUMENTS")]
    [InlineData("check", "INVALID_ARGUMENTS")]
    public void Preflight_InvalidArguments_ReturnExitFourWithoutWriting(string mode, string code)
    {
        var descriptor = CreateBundle();
        var snapshot = Snapshot();
        var format = mode == "long-format" ? new string('x', 2000) : "invalid";
        string[] args = mode switch
        {
            "format" or "long-format" => ["assets", "preflight", descriptor, "-f", format],
            "missing" => ["assets", "preflight", "-f", "json"],
            "output" => ["assets", "preflight", descriptor, "-o", "report.json", "-f", "json"],
            _ => ["assets", "preflight", descriptor, "--check", "-f", "json"]
        };

        var result = RunCommand(args);

        Assert.Equal(4, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
        if (code == "INVALID_FORMAT")
        {
            var message = json.RootElement.GetProperty("error").GetProperty("message").GetString()!;
            Assert.Equal("Supported output formats are text and json.", message);
            Assert.InRange(message.Length, 1, 1024);
            Assert.DoesNotContain(format, result.Output + result.Error);
        }
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public async Task Preflight_PreCancelled_Returns130WithoutReadingOrWriting()
    {
        var snapshot = Snapshot();
        ICommand command = new AssetsPreflightCommand();
        using var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = await command.ExecuteAsync(null!, new AssetsPreflightCommand.Settings
            {
                Descriptor = Path.Combine(TestDir, "private-preflight-input.json"),
                Format = "json"
            }, new CancellationToken(canceled: true));
            Assert.Equal(130, exitCode);
        }
        finally
        {
            Console.SetOut(original);
        }
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("CANCELLED", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(snapshot, Snapshot());
    }

    private string CreateBundle(bool clip = false)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FTPlus.AgentGraphicDesign.slnx")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Repository root not found");
        var fixtures = Path.Combine(root.FullName, "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures");
        var bundle = Path.Combine(TestDir, "private-preflight-input");
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
