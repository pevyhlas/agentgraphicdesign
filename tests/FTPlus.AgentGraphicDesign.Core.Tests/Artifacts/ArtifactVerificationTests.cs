using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Artifacts;

[CollectionDefinition("Artifact verification", DisableParallelization = true)]
public sealed class ArtifactVerificationCollection
{
}

[Collection("Artifact verification")]
public sealed class ArtifactVerificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agd-verify-private-input-marker-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _png = Convert.FromBase64String(File.ReadAllText(FixturePath("preflight-rgba.png.base64")));
    private readonly ProvidedArtifactDescriptor _model = ArtifactDescriptorContract.Parse(
        File.ReadAllBytes(FixturePath("preflight-bundle.json"))).Descriptor!;

    private string Bundle => Path.Combine(_root, "bundle");
    private static FieldInfo Guard => typeof(ArtifactVerification).GetField("_activeVerification", BindingFlags.NonPublic | BindingFlags.Static)!;

    public ArtifactVerificationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Still_ReturnsRawSampleHashAndOnlyImmutableBoundedFacts_WithoutChangingInputs()
    {
        var model = _model with
        {
            Provenance = new() { Producer = new("private-input-marker", "1"), RecipeSnapshotSha256 = new string('a', 64) }
        };
        var path = WriteBundle(model);
        var before = Snapshot();

        var result = await ArtifactVerification.CheckAsync(path);

        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(model), result.MetadataDigest);
        Assert.Equal(model.Profile, result.Profile);
        Assert.Equal(model.AssetId, result.AssetId);
        Assert.Equal(1L, result.Width);
        Assert.Equal(1L, result.Height);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.FrameCount);
        Assert.Equal(_png.LongLength, result.TotalBytes);
        Assert.Equal(new ArtifactPixelDigest(model.Files[0].Sha256, Sha256([255, 0, 0, 255])), Assert.Single(result.Files));
        Assert.Equal("decoded-rgba8", result.Verification);
        Assert.Equal("agd.png-rgba8-managed.v1", result.DecoderProfile);
        Assert.Equal("managed-in-process", result.Execution);
        Assert.True(result.PixelsDecoded);
        Assert.False(result.Admitted);
        Assert.False(result.ProvenanceVerified);
        Assert.Equal(0, Guard.GetValue(null));
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await ArtifactVerification.CheckAsync(path)));
        Assert.Equal(0, Guard.GetValue(null));
        Assert.Equal(before, Snapshot());

        var properties = typeof(ArtifactVerificationResult).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.True(typeof(ArtifactVerificationResult).IsSealed);
        Assert.Equal(new[]
        {
            "Admitted", "AssetId", "DecoderProfile", "Execution", "FileCount", "Files", "FrameCount", "Height",
            "MetadataDigest", "PixelsDecoded", "Profile", "ProvenanceVerified", "TotalBytes", "Verification", "Width"
        }, properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(ImmutableArray<ArtifactPixelDigest>), properties.Single(property => property.Name == "Files").PropertyType);
        Assert.True(typeof(ArtifactPixelDigest).IsSealed);
        var digestProperties = typeof(ArtifactPixelDigest).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(new[] { "FileSha256", "RgbaSha256" }, digestProperties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(digestProperties, property => Assert.Equal(typeof(string), property.PropertyType));
        foreach (var property in properties.Concat(digestProperties).Where(property => property.SetMethod is not null))
            Assert.Contains(typeof(IsExternalInit), property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
        foreach (var property in properties.Where(property => property.Name is
            "Verification" or "DecoderProfile" or "Execution" or "PixelsDecoded" or "Admitted" or "ProvenanceVerified"))
            Assert.Null(property.SetMethod);

        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            IgnoreReadOnlyProperties = true
        });
        Assert.Equal(result.Verification, json.GetProperty("verification").GetString());
        Assert.Equal(result.DecoderProfile, json.GetProperty("decoderProfile").GetString());
        Assert.Equal(result.Execution, json.GetProperty("execution").GetString());
        Assert.True(json.GetProperty("pixelsDecoded").GetBoolean());
        Assert.False(json.GetProperty("admitted").GetBoolean());
        Assert.False(json.GetProperty("provenanceVerified").GetBoolean());
        Assert.Equal(result.Files[0].FileSha256, json.GetProperty("files")[0].GetProperty("fileSha256").GetString());
        Assert.Equal(result.Files[0].RgbaSha256, json.GetProperty("files")[0].GetProperty("rgbaSha256").GetString());
        Assert.DoesNotContain("private-input-marker", json.GetRawText());
        Assert.DoesNotContain(_root, json.GetRawText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task RepeatedClipReferences_ReturnOneUniqueDigestAndUnchangedMetadataIdentity(int frames)
    {
        var model = Clip(_model.Files, frames);
        var path = WriteBundle(model);
        var before = Snapshot();

        var result = await ArtifactVerification.CheckAsync(path);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(frames, result.FrameCount);
        Assert.Equal(_png.LongLength, result.TotalBytes);
        Assert.Equal(Sha256([255, 0, 0, 255]), Assert.Single(result.Files).RgbaSha256);
        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(model), result.MetadataDigest);
        Assert.NotEqual(ArtifactDescriptorContract.ComputeMetadataDigest(_model), result.MetadataDigest);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task DistinctStoredInputs_ReturnOrdinalEncodedHashOrderAndPreserveHiddenRgbSamples()
    {
        byte[] hidden = [17, 34, 51, 0];
        byte[] blue = [0, 0, 255, 255];
        var inputs = new[]
        {
            (Bytes: _png, Rgba: new byte[] { 255, 0, 0, 255 }),
            (Bytes: StoredPixel(hidden), Rgba: hidden),
            (Bytes: StoredPixel(blue), Rgba: blue)
        }.OrderByDescending(input => Sha256(input.Bytes), StringComparer.Ordinal).ToArray();
        var claims = inputs.Select((input, i) => Claim("pixels/f" + i.ToString(CultureInfo.InvariantCulture) + ".png", input.Bytes)).ToImmutableArray();
        var model = Clip(claims, 7);
        var path = WriteBundle(model, inputs.Select(input => input.Bytes).ToArray());
        var before = Snapshot();
        Assert.Equal(claims.ToArray(), ArtifactDescriptorContract.Parse(File.ReadAllBytes(path)).Descriptor!.Files.ToArray());

        var result = await ArtifactVerification.CheckAsync(path);

        Assert.Equal(3, result.FileCount);
        Assert.Equal(7, result.FrameCount);
        Assert.Equal(inputs.Sum(input => input.Bytes.LongLength), result.TotalBytes);
        Assert.Equal(ArtifactDescriptorContract.ComputeMetadataDigest(model), result.MetadataDigest);
        var expected = inputs.Select(input => new ArtifactPixelDigest(Sha256(input.Bytes), Sha256(input.Rgba)))
            .OrderBy(digest => digest.FileSha256, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, result.Files.ToArray());
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task MatchingHashAndCrcInvalidIdat_PassesPreflightButFailsVerification_AndReleasesGuard()
    {
        var bytes = PngWithIdat([0, 0, 0, 0]); // Valid framing/CRC, not an RFC 1950 member.
        var claim = Claim(_model.Files[0].Path, bytes);
        var model = _model with { Files = [claim], Still = _model.Still! with { FileSha256 = claim.Sha256 } };
        var path = WriteBundle(model, bytes);
        var before = Snapshot();

        var structural = await ArtifactPreflight.CheckAsync(path);
        Assert.False(structural.PixelsDecoded);
        Assert.False(structural.Admitted);
        Assert.False(structural.ProvenanceVerified);
        var error = await AssertFailureAsync(path, "ARTIFACT_PNG_INVALID");

        Assert.StartsWith("$.files[0]: ", error.Message);
        Assert.Equal(0, Guard.GetValue(null));
        Assert.Equal(before, Snapshot());
        Assert.Equal(1, (await ArtifactVerification.CheckAsync(WriteBundle())).FileCount);
        Assert.Equal(0, Guard.GetValue(null));
    }

    [Theory]
    [InlineData("decode", "ARTIFACT_PNG_INVALID")]
    [InlineData("hash", "ARTIFACT_CONTENT_MISMATCH")]
    [InlineData("crc", "ARTIFACT_PNG_INVALID")]
    public async Task SecondPayloadFailure_KeepsSuppliedMetadataIndex_NotSortedReportOrder(string failure, string code)
    {
        var second = PngWithIdat([0, 0, 0, 0]);
        if (failure == "crc")
            second[^1] ^= 1;
        var secondClaim = Claim("pixels/private-input-marker-second.png", second);
        // Deliberately put a greater encoded hash first: only completed result records may be sorted.
        var first = Enumerable.Range(0, 256).Select(value => StoredPixel([(byte)value, 0, 0, 255]))
            .First(bytes => StringComparer.Ordinal.Compare(Sha256(bytes), secondClaim.Sha256) > 0);
        var model = Clip([Claim("pixels/private-input-marker-first.png", first), secondClaim], 3);
        if (failure == "hash")
            second[41] ^= 1; // Both IDAT and CRC are invalid, but encoded hash failure must win.
        var path = WriteBundle(model, first, second);
        var before = Snapshot();
        Assert.Equal(secondClaim, ArtifactDescriptorContract.Parse(File.ReadAllBytes(path)).Descriptor!.Files[1]);

        var error = await AssertFailureAsync(path, code);

        Assert.StartsWith("$.files[1]: ", error.Message);
        Assert.DoesNotContain(secondClaim.Path, error.Message);
        Assert.Equal(0, Guard.GetValue(null));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task RepeatedFrames_OpenDescriptorAndUniqueSupplierPayloadExactlyOnce()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return;
        var path = WriteBundle(Clip(_model.Files, 4096));
        var before = Snapshot();
        // Same small Linux x64 open-count probe as preflight; no codec or runtime host-tool dependency.
        var fd = CreateInotify(0x80800);
        Assert.True(fd >= 0);
        using var watcher = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        var descriptorWatch = WatchFile(watcher, path, 0x30); // IN_OPEN | IN_CLOSE_NOWRITE prevents coalescing opens.
        var payloadWatch = WatchFile(watcher, Path.Combine(Bundle, _model.Files[0].Path), 0x30);
        Assert.True(descriptorWatch >= 0);
        Assert.True(payloadWatch >= 0);

        var result = await ArtifactVerification.CheckAsync(path);

        var events = new byte[256];
        Assert.Equal(64, (int)ReadEvents(watcher, events, (nuint)events.Length));
        var expectedWatches = new[] { descriptorWatch, descriptorWatch, payloadWatch, payloadWatch };
        for (var i = 0; i < expectedWatches.Length; i++)
        {
            var offset = i * 16;
            Assert.Equal(expectedWatches[i], BinaryPrimitives.ReadInt32LittleEndian(events.AsSpan(offset)));
            Assert.Equal(i % 2 == 0 ? 0x20u : 0x10u, BinaryPrimitives.ReadUInt32LittleEndian(events.AsSpan(offset + 4)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(events.AsSpan(offset + 12)));
        }
        Assert.Single(result.Files);
        Assert.Equal(4096, result.FrameCount);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task BusyRejection_IsImmediateBeforePathValidationOrIo_AndDoesNotReleaseActiveOwner()
    {
        var path = WriteBundle();
        var before = Snapshot();
        var guard = Guard;
        Assert.Equal(0, guard.GetValue(null));
        guard.SetValue(null, 1);
        try
        {
            foreach (var input in new[] { path, Path.Combine(_root, "private-input-marker-missing"), "private-input-marker\0", null! })
            {
                var work = ArtifactVerification.CheckAsync(input);
                Assert.True(work.IsCompleted);
                var error = await Assert.ThrowsAsync<DocumentEngineException>(() => work);
                AssertSafeError(error, "ARTIFACT_VERIFY_BUSY");
                Assert.Equal(1, guard.GetValue(null));
            }
            Assert.False((await ArtifactPreflight.CheckAsync(path)).PixelsDecoded);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArtifactVerification.CheckAsync(null!, cancellation.Token));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(1, guard.GetValue(null));
        }
        finally
        {
            guard.SetValue(null, 0);
        }
        Assert.Equal(1, (await ArtifactVerification.CheckAsync(path)).FileCount);
        Assert.Equal(0, guard.GetValue(null));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task PreCancellation_PreservesCallerTokenBeforeValidationAndIo_AndLeavesGuardAvailable()
    {
        var path = WriteBundle();
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var input in new[] { path, Path.Combine(_root, "missing"), null! })
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArtifactVerification.CheckAsync(input, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(0, Guard.GetValue(null));
        }
        Assert.Equal(1, (await ArtifactVerification.CheckAsync(path)).FileCount);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task CancellationAfterGuardAcquisition_PreservesCallerTokenAndReleasesOnlyAfterUnwinding()
    {
        // Keep capture/hash/CRC observable without depending on decoder speed or a delayed cancellation timer.
        var bytes = PngWithIdat(new byte[16 * 1024 * 1024]);
        var claim = Claim(_model.Files[0].Path, bytes);
        var path = WriteBundle(_model with { Files = [claim], Still = _model.Still! with { FileSha256 = claim.Sha256 } }, bytes);
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        var guard = Guard;
        var work = Task.Run(() => ArtifactVerification.CheckAsync(path, cancellation.Token));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Equals(guard.GetValue(null), 1) || work.IsCompleted, TimeSpan.FromSeconds(10)));
            Assert.Equal(1, guard.GetValue(null));
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();
            try { await work; }
            catch (OperationCanceledException) { }
        }
        Assert.Equal(0, guard.GetValue(null));
        Assert.Equal(before, Snapshot());
        Assert.Equal(1, (await ArtifactVerification.CheckAsync(WriteBundle())).FileCount);
        Assert.Equal(0, guard.GetValue(null));
    }

    [Fact]
    public async Task InputErrors_RemainBoundedAndSafe_AndReleaseGuardOnEarlyExits()
    {
        var before = Snapshot();
        await AssertFailureAsync(Path.Combine(_root, "private-input-marker-missing.json"), EngineErrors.DocumentNotFound);
        await AssertFailureAsync(null!, EngineErrors.InvalidArguments);
        await AssertFailureAsync("", EngineErrors.InvalidArguments);
        await AssertFailureAsync("private-input-marker\0", EngineErrors.InvalidArguments);
        await AssertFailureAsync("private-input-marker" + new string('x', 4096), EngineErrors.ResourceLimit);
        Assert.Equal(0, Guard.GetValue(null));
        Assert.Equal(before, Snapshot());
        Assert.Equal(1, (await ArtifactVerification.CheckAsync(WriteBundle())).FileCount);
    }

    [Fact]
    public void CooperativeDeadline_IsFixedAtSixtySeconds_WithoutPublicExecutionPolicyKnobs()
    {
        // Configuration evidence only, not a claim that an in-process operation is hard-killed after 60 seconds.
        var timeout = typeof(ArtifactVerification).GetField("VerificationTimeout", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(timeout.IsInitOnly);
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.IsType<TimeSpan>(timeout.GetValue(null)));
        var method = Assert.Single(typeof(ArtifactVerification).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("CheckAsync", method.Name);
        Assert.Equal(typeof(Task<ArtifactVerificationResult>), method.ReturnType);
        Assert.Equal(new[] { typeof(string), typeof(CancellationToken) }, method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private string WriteBundle(ProvidedArtifactDescriptor? model = null, params byte[][] payloads)
    {
        model ??= _model;
        Directory.CreateDirectory(Bundle);
        for (var i = 0; i < model.Files.Length; i++)
        {
            var path = Path.Combine(Bundle, model.Files[i].Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, payloads.Length == 0 ? _png : payloads[i]);
        }
        var descriptor = Path.Combine(Bundle, "bundle.json");
        // Preserve supplied claim order; canonical serialization intentionally sorts by encoded hash.
        File.WriteAllBytes(descriptor, JsonSerializer.SerializeToUtf8Bytes(model, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }));
        return descriptor;
    }

    private ProvidedArtifactDescriptor Clip(ImmutableArray<ArtifactFileClaim> files, int frames) => _model with
    {
        Files = files,
        Still = null,
        Clip = new("idle", "front", "default", 30, "forward-loop", Enumerable.Range(0, frames).Select(i =>
            new ArtifactFrame("f" + i.ToString(CultureInfo.InvariantCulture), files[i % files.Length].Sha256, 1, new(0, 0))).ToImmutableArray())
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static ArtifactFileClaim Claim(string path, byte[] bytes) => new(Sha256(bytes), path, bytes.LongLength);

    private byte[] StoredPixel(byte[] rgba)
    {
        Assert.Equal(4, rgba.Length);
        // Literal RFC 1950 header, one final stored block, LEN/NLEN, filter 0 and raw RGBA, then Adler-32.
        var member = new byte[16];
        Convert.FromHexString("7801010500faff00").CopyTo(member, 0);
        rgba.CopyTo(member, 8);
        uint a = 1, b = 0;
        foreach (var value in member.AsSpan(7, 5))
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        BinaryPrimitives.WriteUInt32BigEndian(member.AsSpan(12), (b << 16) | a);
        return PngWithIdat(member);
    }

    private byte[] PngWithIdat(byte[] member)
    {
        var bytes = new byte[57 + member.Length];
        _png.AsSpan(0, 33).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33), (uint)member.Length);
        "IDAT"u8.CopyTo(bytes.AsSpan(37));
        member.CopyTo(bytes, 41);
        uint crc = uint.MaxValue;
        foreach (var value in bytes.AsSpan(37, member.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(41 + member.Length), ~crc);
        Convert.FromHexString("0000000049454e44ae426082").CopyTo(bytes, 45 + member.Length);
        return bytes;
    }

    private async Task<DocumentEngineException> AssertFailureAsync(string path, string code)
    {
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => ArtifactVerification.CheckAsync(path));
        AssertSafeError(error, code);
        return error;
    }

    private void AssertSafeError(DocumentEngineException error, string code)
    {
        Assert.Equal(code, error.Code);
        Assert.InRange(error.Message.Length, 1, 1024);
        Assert.DoesNotContain("private-input-marker", error.ToString());
        Assert.DoesNotContain(_root, error.Message);
        Assert.Null(error.InnerException);
    }

    private string[] Snapshot() => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Prepend(_root).Order(StringComparer.Ordinal).Select(path =>
        {
            var attributes = File.GetAttributes(path);
            var directory = (attributes & FileAttributes.Directory) != 0;
            var hash = directory ? "" : Sha256(File.ReadAllBytes(path));
            var modified = directory ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
            return $"{Path.GetRelativePath(_root, path)}:{attributes}:{modified.Ticks}:{hash}";
        }).ToArray();

    private static string FixturePath(string name) => Path.Combine(
        JsonSchemaContractTests.FindRepositoryRoot(), "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures", name);

    [DllImport("libc", EntryPoint = "inotify_init1", SetLastError = true)]
    private static extern int CreateInotify(int flags);

    [DllImport("libc", EntryPoint = "inotify_add_watch", SetLastError = true)]
    private static extern int WatchFile(SafeFileHandle fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mask);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint ReadEvents(SafeFileHandle fd, [Out] byte[] buffer, nuint count);
}
