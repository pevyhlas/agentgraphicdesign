using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Tests.Assets;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Artifacts;

public sealed class ArtifactPreflightTests : IDisposable
{
    private readonly string _root = FileProbeWorker.FixtureRoot ?? Path.Combine(Path.GetTempPath(), "agd-pf-private-input-marker-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _png = Convert.FromBase64String(File.ReadAllText(FixturePath("preflight-rgba.png.base64")));
    private readonly ProvidedArtifactDescriptor _model = ArtifactDescriptorContract.Parse(
        File.ReadAllBytes(FixturePath("preflight-bundle.json"))).Descriptor!;

    private string Bundle => Path.Combine(_root, "bundle");

    public ArtifactPreflightTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("bundle.json")]
    [InlineData("descriptor-without-extension")]
    [InlineData(".metadata")]
    public async Task Still_ReturnsOnlyBoundedObservations_AndLeavesInputsUnchanged(string name)
    {
        var path = WriteBundle();
        var renamed = Path.Combine(Bundle, name);
        if (path != renamed)
            File.Move(path, renamed);
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(renamed);

        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(_model), result.MetadataDigest);
        Assert.Equal(_model.Profile, result.Profile);
        Assert.Equal(_model.AssetId, result.AssetId);
        Assert.Equal(1L, result.Width);
        Assert.Equal(1L, result.Height);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.FrameCount);
        Assert.Equal(_png.LongLength, result.TotalBytes);
        Assert.Equal("encoded-integrity-and-png-structure", result.Verification);
        Assert.Equal("agd.png-rgba8-structure.v1", result.PngPolicy);
        Assert.False(result.PixelsDecoded);
        Assert.False(result.Admitted);
        Assert.False(result.ProvenanceVerified);
        Assert.Equal(result, await ArtifactPreflight.CheckAsync(renamed));
        Assert.Equal(before, Snapshot());

        var properties = typeof(ArtifactPreflightResult).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.True(typeof(ArtifactPreflightResult).IsSealed);
        Assert.Equal(new[]
        {
            "Admitted", "AssetId", "FileCount", "FrameCount", "Height", "MetadataDigest", "PixelsDecoded",
            "PngPolicy", "Profile", "ProvenanceVerified", "TotalBytes", "Verification", "Width"
        }, properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var property in properties.Where(property => property.SetMethod is not null))
            Assert.Contains(typeof(IsExternalInit), property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
        foreach (var property in properties.Where(property => property.Name is
            "Verification" or "PngPolicy" or "PixelsDecoded" or "Admitted" or "ProvenanceVerified"))
            Assert.Null(property.SetMethod);

        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            IgnoreReadOnlyProperties = true
        });
        Assert.Equal(result.Verification, json.GetProperty("verification").GetString());
        Assert.Equal(result.PngPolicy, json.GetProperty("pngPolicy").GetString());
        Assert.False(json.GetProperty("pixelsDecoded").GetBoolean());
        Assert.False(json.GetProperty("admitted").GetBoolean());
        Assert.False(json.GetProperty("provenanceVerified").GetBoolean());
        Assert.DoesNotContain("private-input-marker", json.GetRawText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task RepeatedClipReferences_ReportOneUniqueFileAndItsBytes(int frames)
    {
        var model = Clip(_model.Files, frames);
        var path = WriteBundle(model);
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(path);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(frames, result.FrameCount);
        Assert.Equal(_png.LongLength, result.TotalBytes);
        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(model), result.MetadataDigest);
        Assert.NotEqual(ArtifactDescriptorContract.ComputeMetadataDigest(_model), result.MetadataDigest);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task RepeatedFrames_OpenDescriptorAndUniquePayloadOnlyOnce()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return;
        var path = WriteBundle(Clip(_model.Files, 4096));
        var before = Snapshot();
        var fd = CreateInotify(0x80800); // Linux x64 O_CLOEXEC | O_NONBLOCK, scoped to this test's own files.
        Assert.True(fd >= 0);
        using var watcher = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        // Watching both open and close prevents inotify from coalescing consecutive opens.
        var descriptorWatch = WatchFile(watcher, path, 0x30); // IN_OPEN | IN_CLOSE_NOWRITE
        var payloadWatch = WatchFile(watcher, Path.Combine(Bundle, _model.Files[0].Path), 0x30);
        Assert.True(descriptorWatch >= 0);
        Assert.True(payloadWatch >= 0);

        var result = await ArtifactPreflight.CheckAsync(path);

        var events = new byte[256];
        Assert.Equal(64, (int)ReadEvents(watcher, events, (nuint)events.Length));
        var expectedWatches = new[] { descriptorWatch, descriptorWatch, payloadWatch, payloadWatch };
        for (var i = 0; i < expectedWatches.Length; i++)
        {
            var offset = i * 16; // inotify_event has no name bytes when watching a file directly.
            Assert.Equal(expectedWatches[i], BinaryPrimitives.ReadInt32LittleEndian(events.AsSpan(offset)));
            Assert.Equal(i % 2 == 0 ? 0x20u : 0x10u, BinaryPrimitives.ReadUInt32LittleEndian(events.AsSpan(offset + 4)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(events.AsSpan(offset + 12)));
        }
        Assert.Equal(1, result.FileCount);
        Assert.Equal(4096, result.FrameCount);
        Assert.Equal(_png.LongLength, result.TotalBytes);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Clip_ProcessesDistinctFiles_NotLogicalFrameOccurrences()
    {
        var second = OpaquePng(1);
        var model = Clip([_model.Files[0], Claim("pixels/second.png", second)], frames: 3);
        var path = WriteBundle(model, _png, second);
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(path);

        Assert.Equal(2, result.FileCount);
        Assert.Equal(3, result.FrameCount);
        Assert.Equal(_png.LongLength + second.LongLength, result.TotalBytes);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task InvalidCompressedStream_WithMatchingOuterHashAndCrc_IsNotDecodedOrAdmitted()
    {
        // Four zero bytes are not a zlib stream. This still has valid PNG framing and CRCs.
        var bytes = OpaquePng(0);
        var claim = Claim(_model.Files[0].Path, bytes);
        var model = _model with
        {
            Files = [claim],
            Still = _model.Still! with { FileSha256 = claim.Sha256 },
            Provenance = new() { Producer = new("unverified-tool", "1"), RecipeSnapshotSha256 = new string('a', 64) }
        };
        var path = WriteBundle(model, bytes);
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(path);

        Assert.False(result.PixelsDecoded);
        Assert.False(result.Admitted);
        Assert.False(result.ProvenanceVerified);
        Assert.Equal(bytes.LongLength, result.TotalBytes);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task PayloadAcrossMultipleReadAndHashChunks_UsesItsExactCapturedBytes()
    {
        var bytes = OpaquePng(0, idatLength: 2 * 65_536 + 17);
        var claim = Claim(_model.Files[0].Path, bytes);
        var model = _model with { Files = [claim], Still = _model.Still! with { FileSha256 = claim.Sha256 } };
        var path = WriteBundle(model, bytes);
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(path);

        Assert.Equal(bytes.LongLength, result.TotalBytes);
        Assert.Equal(before, Snapshot());
        bytes[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(Bundle, claim.Path), bytes);
        before = Snapshot();
        await AssertFailureAsync(path, "ARTIFACT_CONTENT_MISMATCH");
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("short", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("long", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("empty", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("png", "ARTIFACT_PNG_INVALID")]
    [InlineData("dimensions", "ARTIFACT_PNG_INVALID")]
    public async Task ActualContentFailures_AreStructuredAndDoNotChangeInputs(string mutation, string code)
    {
        var model = _model;
        var bytes = _png.ToArray();
        switch (mutation)
        {
            case "hash": bytes[0] ^= 1; break;
            case "short": bytes = bytes[..^1]; break;
            case "long": bytes = [.. bytes, 0]; break;
            case "empty": bytes = []; break;
            case "png":
                bytes[0] ^= 1;
                var claim = Claim(model.Files[0].Path, bytes);
                model = model with { Files = [claim], Still = model.Still! with { FileSha256 = claim.Sha256 } };
                break;
            case "dimensions": model = model with { Canvas = model.Canvas with { Width = 2 } }; break;
        }
        var path = WriteBundle(model, bytes);
        var before = Snapshot();

        var error = await AssertFailureAsync(path, code);

        Assert.StartsWith("$.files[0]: ", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("length")]
    public async Task SecondPayloadFailure_IdentifiesItsMetadataIndexWithoutEchoingItsPath(string mutation)
    {
        var second = OpaquePng(1);
        var model = Clip([
            _model.Files[0] with { Path = "pixels/private-input-marker-first.png" },
            Claim("pixels/private-input-marker-second.png", second)
        ], frames: 3);
        var path = WriteBundle(model, _png, second);
        // Canonical serialization sorts claims by hash; diagnostics must index the supplied array.
        var descriptor = ArtifactDescriptorContract.Parse(File.ReadAllBytes(path)).Descriptor!;
        var claim = descriptor.Files[1];
        var payload = Path.Combine(Bundle, claim.Path);
        var bytes = File.ReadAllBytes(payload);
        if (mutation == "hash")
            bytes[0] ^= 1;
        else
            bytes = bytes[..^1];
        File.WriteAllBytes(payload, bytes);
        var before = Snapshot();

        var error = await AssertFailureAsync(path, "ARTIFACT_CONTENT_MISMATCH");

        Assert.StartsWith("$.files[1]: ", error.Message);
        Assert.DoesNotContain(claim.Path, error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("missing-parent")]
    [InlineData("extra-file")]
    [InlineData("hidden-file")]
    [InlineData("empty-directory")]
    [InlineData("file-case")]
    [InlineData("directory-case")]
    [InlineData("case-alias")]
    [InlineData("descriptor-case-alias")]
    public async Task Inventory_IsExactIncludingCaseHiddenEntriesAndEmptyDirectories(string mutation)
    {
        if (OperatingSystem.IsWindows() && mutation.Contains("case", StringComparison.Ordinal))
            return; // A case-insensitive volume cannot create both spellings as separate entries.
        var model = _model with { Files = [_model.Files[0] with { Path = "pixels/still.png" }] };
        var path = WriteBundle(model);
        var payload = Path.Combine(Bundle, "pixels", "still.png");
        var parent = Path.GetDirectoryName(payload)!;
        switch (mutation)
        {
            case "missing-file": File.Delete(payload); break;
            case "missing-parent": Directory.Delete(parent, true); break;
            case "extra-file": File.WriteAllText(Path.Combine(Bundle, "private-input-marker"), "extra"); break;
            case "hidden-file": File.WriteAllText(Path.Combine(Bundle, ".private-input-marker"), "extra"); break;
            case "empty-directory": Directory.CreateDirectory(Path.Combine(parent, ".private-input-marker")); break;
            case "file-case": File.Move(payload, Path.Combine(parent, "STILL.png")); break;
            case "directory-case": Directory.Move(parent, Path.Combine(Bundle, "PIXELS")); break;
            case "case-alias": File.Copy(payload, Path.Combine(parent, "STILL.png")); break;
            case "descriptor-case-alias": File.Copy(path, Path.Combine(Bundle, "BUNDLE.json")); break;
        }
        var before = Snapshot();

        await AssertFailureAsync(path, "ARTIFACT_INVENTORY_MISMATCH");

        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("descriptor.png", "descriptor.png")]
    [InlineData("descriptor.png/child.png", "descriptor.png")]
    [InlineData("descriptor.png", "DESCRIPTOR.PNG")]
    [InlineData("descriptor.png/child.png", "DESCRIPTOR.PNG")]
    public async Task DescriptorCannotAlsoBeAPayloadOrNecessaryDirectory(string payloadPath, string descriptorName)
    {
        if (OperatingSystem.IsWindows() && descriptorName == "DESCRIPTOR.PNG")
            return;
        Directory.CreateDirectory(Bundle);
        var path = Path.Combine(Bundle, descriptorName);
        var model = _model with { Files = [_model.Files[0] with { Path = payloadPath }] };
        if (descriptorName == "DESCRIPTOR.PNG")
        {
            var payload = Path.Combine(Bundle, payloadPath);
            Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
            File.WriteAllBytes(payload, _png);
        }
        File.WriteAllBytes(path, ArtifactDescriptorContract.SerializeCanonical(model));
        var before = Snapshot();

        await AssertFailureAsync(path, "ARTIFACT_INVENTORY_MISMATCH");

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task NecessaryDirectoryInventory_CanExceedAssetPipelines4096EntryLimit()
    {
        const int count = 513; // Seven distinct parent directories per file, plus descriptor: 4105 entries.
        var payloads = Enumerable.Range(0, count).Select(i => OpaquePng((uint)i)).ToArray();
        var claims = payloads.Select((bytes, i) => Claim(
            "d" + i.ToString(CultureInfo.InvariantCulture) + "/a/b/c/d/e/f/frame.png", bytes)).ToImmutableArray();
        var path = WriteBundle(Clip(claims, count), payloads);
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(path);

        Assert.Equal(count, result.FileCount);
        Assert.Equal(count, result.FrameCount);
        Assert.Equal(payloads.Sum(bytes => bytes.LongLength), result.TotalBytes);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("descriptor")]
    [InlineData("payload")]
    [InlineData("parent")]
    public async Task WrongEntryTypes_AreRejectedBeforeFileOpen(string location)
    {
        var model = _model with { Files = [_model.Files[0] with { Path = "pixels/still.png" }] };
        var path = WriteBundle(model);
        var target = location == "descriptor" ? path : Path.Combine(Bundle, "pixels", "still.png");
        if (location == "parent")
        {
            target = Path.GetDirectoryName(target)!;
            Directory.Delete(target, true);
            File.WriteAllText(target, "not a directory");
        }
        else
        {
            File.Delete(target);
            Directory.CreateDirectory(target);
        }
        var before = Snapshot();

        await AssertFailureAsync(path, EngineErrors.PathEscape);

        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("descriptor", false)]
    [InlineData("descriptor", true)]
    [InlineData("payload", false)]
    [InlineData("payload", true)]
    [InlineData("parent", false)]
    [InlineData("parent", true)]
    [InlineData("bundle", false)]
    [InlineData("bundle", true)]
    public async Task LinksIncludingDanglingLinksAndAncestors_AreRejected(string location, bool dangling)
    {
        if (OperatingSystem.IsWindows())
            return; // Link creation requires privileges not promised to Windows test accounts.
        var model = _model with { Files = [_model.Files[0] with { Path = "pixels/still.png" }] };
        var path = WriteBundle(model);
        var target = location switch
        {
            "descriptor" => path,
            "payload" => Path.Combine(Bundle, "pixels", "still.png"),
            "parent" => Path.Combine(Bundle, "pixels"),
            _ => Bundle
        };
        var moved = Path.Combine(_root, "private-input-marker-source");
        if (location is "parent" or "bundle")
        {
            Directory.Move(target, moved);
            Directory.CreateSymbolicLink(target, dangling ? moved + "-missing" : moved);
        }
        else
        {
            File.Move(target, moved);
            File.CreateSymbolicLink(target, dangling ? moved + "-missing" : moved);
        }
        var before = Snapshot();

        await AssertFailureAsync(path, EngineErrors.PathEscape);

        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("descriptor")]
    [InlineData("payload")]
    public async Task UnixFifos_AreRejectedWithoutBlockingOpen(string location)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
            return;
        if (!FileProbeWorker.IsChild)
        {
            await FileProbeWorker.RunAsync("artifact-fifo", location);
            return;
        }
        var path = WriteBundle();
        var target = location == "descriptor" ? path : Path.Combine(Bundle, _model.Files[0].Path);
        File.Delete(target);
        Assert.Equal(0, MakeFifo(target, 0x180));

        await AssertFailureAsync(path, EngineErrors.PathEscape);
    }

    [Fact]
    public async Task UnixSocket_IsRejectedBeforeOpen()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
            return;
        if (!FileProbeWorker.IsChild)
        {
            await FileProbeWorker.RunAsync("artifact-socket");
            return;
        }
        var model = _model with { Files = [_model.Files[0] with { Path = "s.png" }] };
        var path = WriteBundle(model);
        var target = Path.Combine(Bundle, "s.png");
        File.Delete(target);
        var socketDirectory = Directory.CreateDirectory(Path.Combine(FileProbeWorker.OwnerRoot!, "socket"));
        try
        {
            // Bind below the socket path limit even when TMPDIR plus the bundle name is long.
            var socketPath = Path.Combine(socketDirectory.FullName, "s");
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.Move(socketPath, target);

            await AssertFailureAsync(path, EngineErrors.PathEscape);
        }
        finally
        {
            socketDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UnexpectedDirectory_IsRejectedWithoutInspectingItsChildren()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var path = WriteBundle();
        var extra = Path.Combine(Bundle, "private-input-marker-extra");
        Directory.CreateDirectory(extra);
        Assert.Equal(0, MakeFifo(Path.Combine(extra, "fifo"), 0x180));

        await Task.Run(() => AssertFailureAsync(path, "ARTIFACT_INVENTORY_MISMATCH")).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SourceHardLinks_AreAllowedButDoNotMakeResultADurableReceipt()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var path = WriteBundle();
        var payload = Path.Combine(Bundle, _model.Files[0].Path);
        var source = Path.Combine(_root, "source.png");
        File.Move(payload, source);
        Assert.Equal(0, MakeHardLink(source, payload));
        var before = Snapshot();

        var result = await ArtifactPreflight.CheckAsync(path);

        Assert.Equal(_png.LongLength, result.TotalBytes);
        Assert.Equal(before, Snapshot());
        File.WriteAllBytes(source, new byte[_png.Length]);
        await AssertFailureAsync(path, "ARTIFACT_CONTENT_MISMATCH");
        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(_model), result.MetadataDigest);
    }

    [Fact]
    public async Task DescriptorByteLimit_IsInclusiveAndCheckedBeforeParsing()
    {
        var path = WriteBundle();
        var metadata = ArtifactDescriptorContract.SerializeCanonical(_model);
        var padded = new byte[ArtifactDescriptorContract.MaxMetadataBytes];
        Array.Fill(padded, (byte)' ');
        metadata.CopyTo(padded, 0);
        File.WriteAllBytes(path, padded);
        var before = Snapshot();
        Assert.Equal(1, (await ArtifactPreflight.CheckAsync(path)).FileCount);
        Assert.Equal(before, Snapshot());

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            stream.SetLength(ArtifactDescriptorContract.MaxMetadataBytes + 1L);
        before = Snapshot();
        await AssertFailureAsync(path, EngineErrors.ResourceLimit);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task OversizedActualPayload_IsRejectedBeforeHashingOrAllocation()
    {
        var path = WriteBundle();
        var payload = Path.Combine(Bundle, _model.Files[0].Path);
        using (var stream = new FileStream(payload, FileMode.Open, FileAccess.Write))
            stream.SetLength(67_108_865); // Sparse file; do not allocate an oversized test buffer.
        var length = new FileInfo(payload).Length;
        var mtime = File.GetLastWriteTimeUtc(payload);
        var entries = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        await AssertFailureAsync(path, EngineErrors.ResourceLimit);

        Assert.Equal(length, new FileInfo(payload).Length);
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(payload));
        Assert.Equal(entries, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(67_108_864L, "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData(67_108_865L, EngineErrors.InvalidArguments)]
    public async Task ClaimedFileLimit_UsesExistingMetadataContract(long length, string code)
    {
        var path = WriteBundle();
        var json = JsonNode.Parse(File.ReadAllBytes(path))!;
        json["files"]![0]!["byteLength"] = length;
        File.WriteAllText(path, json.ToJsonString());
        var before = Snapshot();

        var error = await AssertFailureAsync(path, code);

        if (code == EngineErrors.InvalidArguments)
            Assert.Contains("$.files[0].byteLength", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task DeclaredAggregateLimit_IsCheckedBeforeInventoryOrPayloadReads()
    {
        Directory.CreateDirectory(Bundle);
        var claims = Enumerable.Range(0, 5).Select(i => new ArtifactFileClaim(
            i.ToString("x64", CultureInfo.InvariantCulture), "f" + i.ToString(CultureInfo.InvariantCulture) + ".png", 1)).ToImmutableArray();
        var json = JsonNode.Parse(ArtifactDescriptorContract.SerializeCanonical(Clip(claims, 5)))!;
        foreach (var file in json["files"]!.AsArray())
            file!["byteLength"] = 67_108_864;
        var path = Path.Combine(Bundle, "bundle.json");
        File.WriteAllText(path, json.ToJsonString());
        var before = Snapshot();

        var error = await AssertFailureAsync(path, EngineErrors.ResourceLimit);

        Assert.Contains("$.files", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("path", EngineErrors.InvalidArguments, "$.files[0].path")]
    [InlineData("profile", "ARTIFACT_PROFILE_UNSUPPORTED", "$.profile")]
    [InlineData("shape", EngineErrors.ParseError, "$")]
    public async Task MetadataErrors_KeepSafeLocationsButDoNotEchoUntrustedData(string mutation, string code, string location)
    {
        var path = WriteBundle();
        var json = JsonNode.Parse(File.ReadAllBytes(path))!;
        switch (mutation)
        {
            case "path": json["files"]![0]!["path"] = "../private-input-marker.png"; break;
            case "profile": json["profile"] = "private-input-marker"; break;
            case "shape": json["private-input-marker"] = new string('x', 1400); break;
        }
        File.WriteAllText(path, json.ToJsonString());
        var before = Snapshot();

        var error = await AssertFailureAsync(path, code);

        Assert.Contains(location, error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task MissingInvalidAndOverlongInputPaths_HaveBoundedNonEchoingErrors()
    {
        var before = Snapshot();
        await AssertFailureAsync(Path.Combine(_root, "private-input-marker-missing.json"), EngineErrors.DocumentNotFound);
        await AssertFailureAsync(Path.Combine(_root, "missing", "descriptor.json"), EngineErrors.DocumentNotFound);
        await AssertFailureAsync(null!, EngineErrors.InvalidArguments);
        await AssertFailureAsync("", EngineErrors.InvalidArguments);
        await AssertFailureAsync("private-input-marker\0", EngineErrors.InvalidArguments);
        await AssertFailureAsync("private-input-marker" + new string('x', 4096), EngineErrors.ResourceLimit);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task PreCancellation_PrecedesPathValidationAndAllInputIo()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = WriteBundle();
        var before = Snapshot();
        foreach (var input in new[] { path, Path.Combine(_root, "missing"), null! })
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ArtifactPreflight.CheckAsync(input, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyBundle_SuccessAndFailureDoNotWrite(bool corrupt)
    {
        var bytes = _png.ToArray();
        if (corrupt)
            bytes[0] ^= 1;
        var path = WriteBundle(_model, bytes);
        var files = Directory.GetFiles(Bundle, "*", SearchOption.AllDirectories);
        var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).Prepend(_root).ToArray();
        foreach (var file in files)
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
            foreach (var directory in directories)
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var before = Snapshot();
            if (corrupt)
                await AssertFailureAsync(path, "ARTIFACT_CONTENT_MISMATCH");
            else
                Assert.Equal(1, (await ArtifactPreflight.CheckAsync(path)).FileCount);
            Assert.Equal(before, Snapshot());
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
                foreach (var directory in directories)
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var file in files)
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpectedAccessFailures_AreNormalizedWithoutExceptionDetails(bool descriptor)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var path = WriteBundle();
        var target = descriptor ? path : Path.Combine(Bundle, _model.Files[0].Path);
        var mode = File.GetUnixFileMode(target);
        var before = Snapshot();
        File.SetUnixFileMode(target, UnixFileMode.None);
        try
        {
            try
            {
                using var probe = File.OpenRead(target);
                return; // Privileged accounts can bypass mode bits; this is not access-denial evidence.
            }
            catch (UnauthorizedAccessException)
            {
                await AssertFailureAsync(path, "ARTIFACT_INPUT_UNAVAILABLE");
            }
        }
        finally
        {
            File.SetUnixFileMode(target, mode);
            Assert.Equal(before, Snapshot());
        }
    }

    private string WriteBundle(ProvidedArtifactDescriptor? model = null, params byte[][] payloads)
    {
        model ??= _model;
        Assert.NotNull(model);
        Directory.CreateDirectory(Bundle);
        for (var i = 0; i < model.Files.Length; i++)
        {
            var path = Path.Combine(Bundle, model.Files[i].Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, payloads.Length == 0 ? _png : payloads[i]);
        }
        var descriptor = Path.Combine(Bundle, "bundle.json");
        File.WriteAllBytes(descriptor, ArtifactDescriptorContract.SerializeCanonical(model));
        return descriptor;
    }

    private ProvidedArtifactDescriptor Clip(ImmutableArray<ArtifactFileClaim> files, int frames) => _model with
    {
        Files = files,
        Still = null,
        Clip = new("idle", "front", "default", 30, "forward-loop", Enumerable.Range(0, frames).Select(i =>
            new ArtifactFrame("f" + i.ToString(CultureInfo.InvariantCulture), files[i % files.Length].Sha256, 1, new(0, 0))).ToImmutableArray())
    };

    private static ArtifactFileClaim Claim(string path, byte[] data) =>
        new(Convert.ToHexStringLower(SHA256.HashData(data)), path, data.LongLength);

    private byte[] OpaquePng(uint value, int idatLength = 4)
    {
        var bytes = new byte[57 + idatLength];
        _png.AsSpan(0, 33).CopyTo(bytes); // Literal fixture signature and IHDR, not a codec-generated header.
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33), (uint)idatLength);
        "IDAT"u8.CopyTo(bytes.AsSpan(37));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(41), value);
        uint crc = uint.MaxValue;
        foreach (var b in bytes.AsSpan(37, idatLength + 4))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(41 + idatLength), ~crc);
        Convert.FromHexString("0000000049454e44ae426082").CopyTo(bytes, 45 + idatLength);
        return bytes;
    }

    private async Task<DocumentEngineException> AssertFailureAsync(string path, string code)
    {
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => ArtifactPreflight.CheckAsync(path));
        Assert.Equal(code, error.Code);
        Assert.InRange(error.Message.Length, 1, 1024);
        Assert.DoesNotContain("private-input-marker", error.ToString());
        Assert.DoesNotContain(_root, error.Message);
        Assert.Null(error.InnerException);
        return error;
    }

    private string[] Snapshot()
    {
        var entries = new List<string> { $"directory:.:{Directory.GetLastWriteTimeUtc(_root).Ticks}" };
        var pending = new Stack<string>();
        pending.Push(_root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(_root, path);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    entries.Add($"link:{relative}:{new FileInfo(path).LinkTarget}");
                else if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add($"directory:{relative}:{Directory.GetLastWriteTimeUtc(path).Ticks}:{attributes}");
                    pending.Push(path);
                }
                else
                    entries.Add($"file:{relative}:{File.GetLastWriteTimeUtc(path).Ticks}:{attributes}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
            }
        }
        return entries.Order(StringComparer.Ordinal).ToArray();
    }

    private static string FixturePath(string name) => Path.Combine(
        JsonSchemaContractTests.FindRepositoryRoot(), "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures", name);

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int MakeHardLink([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string target);

    [DllImport("libc", EntryPoint = "inotify_init1", SetLastError = true)]
    private static extern int CreateInotify(int flags);

    [DllImport("libc", EntryPoint = "inotify_add_watch", SetLastError = true)]
    private static extern int WatchFile(SafeFileHandle fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mask);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint ReadEvents(SafeFileHandle fd, [Out] byte[] buffer, nuint count);
}
