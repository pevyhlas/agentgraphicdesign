using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Platform;

namespace FTPlus.AgentGraphicDesign.Core.Artifacts;

/// <summary>
/// Read-only encoded integrity and PNG structure checks, not decoding or admission.
/// Requires a trusted, quiescent workspace; inspection and opening are not an atomic snapshot.
/// File-type inspection reuses the qualified .NET 10 Linux x64 ABI and existing Windows behavior.
/// </summary>
public static class ArtifactPreflight
{
    private const int MaxPathLength = 4096;
    private const int MaxInventoryEntries = 32_769;
    private const int MaxFileBytes = 67_108_864;
    private const int ReadChunkBytes = 65_536;

    public static Task<ArtifactPreflightResult> CheckAsync(
        string descriptorPath, CancellationToken cancellationToken = default) =>
        RunAsync(descriptorPath, cancellationToken);

    internal static async Task<ArtifactPreflightResult> RunAsync(
        string descriptorPath, CancellationToken cancellationToken,
        Action<byte[], ArtifactFileClaim, ArtifactCanvas>? verifyPayload = null,
        ArtifactCapturePolicy? capturePolicy = null)
    {
        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            capturePolicy?.CheckBudget();
        }
        Check();
        if (descriptorPath is { Length: > MaxPathLength })
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "Artifact input path exceeds 4096 characters.");
        if (string.IsNullOrEmpty(descriptorPath) || descriptorPath.Contains('\0'))
            throw new DocumentEngineException(EngineErrors.InvalidArguments, "A valid descriptor path is required.");

        try
        {
            var fullPath = PlatformPaths.FullPath(descriptorPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new DocumentEngineException(EngineErrors.InvalidArguments, "The descriptor must be a file in a bundle directory.");

            var metadata = await ReadAsync(fullPath, expectedLength: null, cancellationToken, capturePolicy).ConfigureAwait(false);
            Check();
            // Conservative cumulative ownership for parser copies, duplicate-key sets, decoded
            // strings/models and canonical identity buffers, before any metadata interpretation.
            capturePolicy?.Reserve(32L * metadata.Length + 262_144);
            var parsed = ArtifactDescriptorContract.Parse(metadata);
            if (parsed.Error is { } error)
            {
                // Both message and location are contract-generated, never supplier JSON/path values.
                var message = $"{error.Path ?? "$"}: {error.Message}";
                throw new DocumentEngineException(error.Code, message.Length <= 1024 ? message : message[..1024]);
            }
            var descriptor = parsed.Descriptor!;
            Check();
            // Import can reject clip/profile/native dimensions/declared bytes BEFORE PNG allocation.
            // The observer receives the very same owned original bytes, never a supplier reread.
            capturePolicy?.CaptureMetadata(metadata, descriptor);

            // Only claims and their necessary parents can enter the worklist. Do not reuse the
            // asset pipeline's smaller inventory limit or recursively enumerate unexpected trees.
            var descriptorName = Path.GetFileName(fullPath);
            var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [descriptorName] = false
            };
            foreach (var file in descriptor.Files)
            {
                Check();
                if (Path.Combine(directory, file.Path).Length > MaxPathLength)
                    throw new DocumentEngineException(EngineErrors.ResourceLimit, "Artifact input path exceeds 4096 characters.");
                if (file.Path.Equals(descriptorName, StringComparison.OrdinalIgnoreCase) || !expected.TryAdd(file.Path, false))
                    throw new DocumentEngineException("ARTIFACT_INVENTORY_MISMATCH", "Descriptor and payload inventory entries conflict.");
                for (var i = 0; i < file.Path.Length; i++)
                {
                    if (file.Path[i] != '/')
                        continue;
                    var parent = file.Path[..i];
                    if (parent.Equals(descriptorName, StringComparison.OrdinalIgnoreCase)
                        || (expected.TryGetValue(parent, out var wantsDirectory) && !wantsDirectory))
                        throw new DocumentEngineException("ARTIFACT_INVENTORY_MISMATCH", "Descriptor and payload inventory entries conflict.");
                    expected.TryAdd(parent, true);
                }
                if (expected.Count > MaxInventoryEntries)
                    throw new DocumentEngineException(EngineErrors.ResourceLimit, "Artifact inventory exceeds 32769 entries.");
            }

            var pending = new Stack<(string Path, string Relative)>();
            pending.Push((directory, ""));
            while (pending.TryPop(out var current))
            {
                Check();
                foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
                {
                    Check();
                    var name = Path.GetFileName(entry);
                    var relative = current.Relative.Length == 0 ? name : current.Relative + "/" + name;
                    if (!expected.Remove(relative, out var wantsDirectory))
                        throw new DocumentEngineException("ARTIFACT_INVENTORY_MISMATCH", "Bundle contains an unexpected or case-aliased entry.");
                    Inspect(entry, wantsDirectory, cancellationToken);
                    if (wantsDirectory)
                        pending.Push((entry, relative));
                }
            }
            if (expected.Count != 0)
                throw new DocumentEngineException("ARTIFACT_INVENTORY_MISMATCH", "Bundle is missing a declared inventory entry.");

            long totalBytes = 0;
            for (var fileIndex = 0; fileIndex < descriptor.Files.Length; fileIndex++)
            {
                var file = descriptor.Files[fileIndex];
                try
                {
                    var data = await ReadAsync(Path.Combine(directory, file.Path), file.ByteLength, cancellationToken, capturePolicy).ConfigureAwait(false);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    for (var offset = 0; offset < data.Length; offset += ReadChunkBytes)
                    {
                        Check();
                        hash.AppendData(data.AsSpan(offset, Math.Min(ReadChunkBytes, data.Length - offset)));
                    }
                    Check();
                    if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.Ordinal))
                        throw new DocumentEngineException("ARTIFACT_CONTENT_MISMATCH", "Payload SHA-256 does not match its metadata claim.");
                    PngStructure.ValidateBounded(data, descriptor.Canvas.Width, descriptor.Canvas.Height,
                        cancellationToken, capturePolicy?.CheckBudget, capturePolicy?.MaxFileBytes ?? MaxFileBytes);
                    verifyPayload?.Invoke(data, file, descriptor.Canvas);
                    totalBytes += data.LongLength;
                }
                catch (DocumentEngineException ex)
                {
                    var message = $"$.files[{fileIndex.ToString(CultureInfo.InvariantCulture)}]: {ex.Message}";
                    throw new DocumentEngineException(ex.Code, message.Length <= 1024 ? message : message[..1024]);
                }
            }

            Check();
            var digest = ArtifactDescriptorContract.ComputeMetadataDigest(descriptor);
            Check();
            return new(digest, descriptor.Profile, descriptor.AssetId, descriptor.Canvas.Width, descriptor.Canvas.Height,
                descriptor.Files.Length, descriptor.Clip?.Frames.Length ?? 1, totalBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new DocumentEngineException("ARTIFACT_INPUT_UNAVAILABLE", "Artifact input could not be inspected or read.");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new DocumentEngineException(EngineErrors.InvalidArguments, "Artifact input path is invalid.");
        }
    }

    private static void Inspect(string path, bool directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (path.Length > MaxPathLength)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "Artifact input path exceeds 4096 characters.");
        try
        {
            AssetPaths.Inspect(path, directory, required: true, PlatformCapability.ArtifactVerify, ct);
        }
        catch (DocumentEngineException ex)
        {
            // AssetPaths diagnostics contain absolute paths. Preserve codes, not those messages.
            throw new DocumentEngineException(ex.Code, ex.Code switch
            {
                EngineErrors.DocumentNotFound => "Required artifact input was not found.",
                EngineErrors.PathEscape => "Artifact input must be free of links and special files and have the required entry type.",
                EngineErrors.AssetPlatformUnsupported => ex.Message,
                _ => "Artifact input path inspection failed."
            });
        }
    }

    private static async Task<byte[]> ReadAsync(string path, long? expectedLength, CancellationToken ct,
        ArtifactCapturePolicy? capturePolicy)
    {
        void Check()
        {
            ct.ThrowIfCancellationRequested();
            capturePolicy?.CheckBudget();
        }
        Check();
        Inspect(path, directory: false, ct);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        Check();
        var length = stream.Length;
        var limit = expectedLength.HasValue ? capturePolicy?.MaxFileBytes ?? MaxFileBytes : ArtifactDescriptorContract.MaxMetadataBytes;
        if (length > limit)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "Artifact input exceeds its encoded byte limit.");
        if (expectedLength.HasValue && length != expectedLength.Value)
            throw new DocumentEngineException("ARTIFACT_CONTENT_MISMATCH", "Payload length does not match its metadata claim.");

        capturePolicy?.Reserve(length + 65_536);
        var data = new byte[checked((int)length)];
        var offset = 0;
        while (offset < data.Length)
        {
            Check();
            var read = await stream.ReadAsync(data.AsMemory(offset, Math.Min(ReadChunkBytes, data.Length - offset)), ct).ConfigureAwait(false);
            if (read == 0)
                throw new DocumentEngineException("ARTIFACT_CONTENT_MISMATCH", "Artifact input length changed while reading.");
            offset += read;
        }
        Check();
        if (await stream.ReadAsync(new byte[1], ct).ConfigureAwait(false) != 0 || stream.Length != length)
            throw new DocumentEngineException("ARTIFACT_CONTENT_MISMATCH", "Artifact input length changed while reading.");
        Check();
        return data;
    }
}

/// <summary>Internal capture ownership/budget hooks; public read-only checks keep their existing profile.</summary>
internal sealed record ArtifactCapturePolicy(
    int MaxFileBytes, Action CheckBudget, Action<long> Reserve,
    Action<byte[], ProvidedArtifactDescriptor> CaptureMetadata);

/// <summary>Bounded observations of this call's captured bytes, not a durable receipt or payload ownership.</summary>
public sealed record ArtifactPreflightResult(
    string MetadataDigest, string Profile, string AssetId, long Width, long Height,
    int FileCount, int FrameCount, long TotalBytes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Verification => "encoded-integrity-and-png-structure";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string PngPolicy => "agd.png-rgba8-structure.v1";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool PixelsDecoded => false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Admitted => false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool ProvenanceVerified => false;
}
