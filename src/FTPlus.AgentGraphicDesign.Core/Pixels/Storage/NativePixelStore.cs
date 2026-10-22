using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Assets;

namespace FTPlus.AgentGraphicDesign.Core.Pixels.Storage;

/// <summary>One verified, fully captured in-memory native source and its snapshot.</summary>
internal sealed record CapturedPixelSource(
    NativePixelDocument Document,
    PixelSnapshot Snapshot,
    IReadOnlyDictionary<string, byte[]> Payloads,
    long LogicalSourceBytes);

/// <summary>
/// The native pixel storage backend: the reader protocol (closure capture and verification
/// without enumeration) and the Create writer protocol (sibling-lock arbitration, private
/// staging and atomic no-replace publication). All read/create work is bounded and
/// cancellation/deadline-aware through the supplied <see cref="NativeOperationScope"/>.
/// </summary>
internal static partial class NativePixelStore
{
    internal const string MetadataFileName = NativePixelFormat.MetadataFileName; // asset.agd-pixels.json
    internal const string PayloadDirectoryName = NativePixelFormat.PayloadDirectoryName; // payloads
    private const int MaxReferences = 32;

    // ---- Reader protocol ---------------------------------------------------

    internal static CapturedPixelSource Read(string assetDirectory, NativeOperationScope scope)
    {
        scope.Check();
        string directory = NormalizeAssetDirectory(assetDirectory);

        // (1) Validate the locator, fixed metadata path and needed ancestor types/identities
        //     without enumeration. A missing asset target or metadata is DOCUMENT_NOT_FOUND.
        Inspect(directory, isDirectory: true, required: true, scope);
        string metadataPath = Path.Combine(directory, MetadataFileName);
        Inspect(metadataPath, isDirectory: false, required: true, scope);

        // (2) Open metadata once, capture <= 1 MiB requiring EOF, parse and validate.
        byte[] metadata = NativePixelFileSystem.ReadMetadata(metadataPath, NativePixelFormat.MaxJsonBytes, scope);
        NativePixelDocument document = NativePixelFormat.Parse(metadata, scope.Budget);

        // (3) Derive the current referenced closure and verify every payload.
        var payloads = CaptureClosure(directory, document, scope);

        // (4) Compute the source digest and return one verified snapshot/owned result.
        string digest = NativePixelFormat.SourceDigest(document, scope.Budget);
        var snapshot = new PixelSnapshot(document.AssetId, document.Revn, digest);
        long logical = (long)document.Width * document.Height * (4L * document.Layers.Length + MaskCount(document));
        return new CapturedPixelSource(document, snapshot, payloads, logical);
    }

    private static IReadOnlyDictionary<string, byte[]> CaptureClosure(
        string directory, NativePixelDocument document, NativeOperationScope scope)
    {
        string payloadDirectory = Path.Combine(directory, PayloadDirectoryName);
        if (!Inspect(payloadDirectory, isDirectory: true, required: false, scope))
            throw new NativePixelException(NativePixelErrors.PixelInventoryMismatch, "The payload directory is missing.");

        // A hash addressed as both a pixel surface (kind 1) and a mask (kind 2) cannot be one
        // framed object; reject rather than reading a length that only one reference could match.
        var pixelHashes = new HashSet<string>(StringComparer.Ordinal);
        var maskHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in document.Layers)
        {
            pixelHashes.Add(layer.PixelsSha256);
            if (layer.MaskSha256 is not null) maskHashes.Add(layer.MaskSha256);
        }
        if (pixelHashes.Overlaps(maskHashes))
            throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "A payload is referenced as both a pixel surface and a mask.");
        if (pixelHashes.Count + maskHashes.Count > MaxReferences)
            throw new NativePixelException(EngineErrors.ResourceLimit, "The referenced payload closure exceeds its limit.");

        long area = (long)document.Width * document.Height;
        var captures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        CaptureEach(payloadDirectory, pixelHashes, kind: 1, checked((int)(24 + area * 4)), captures, scope);
        CaptureEach(payloadDirectory, maskHashes, kind: 2, checked((int)(24 + area)), captures, scope);

        // Verify framing/length/kind/dimensions/hash/filename for every reference.
        foreach (var layer in document.Layers)
        {
            scope.Check();
            NativePixelFormat.ValidatePayload(captures[layer.PixelsSha256], layer.PixelsSha256, 1, document.Width, document.Height, scope.Budget);
            if (layer.MaskSha256 is not null)
                NativePixelFormat.ValidatePayload(captures[layer.MaskSha256], layer.MaskSha256, 2, document.Width, document.Height, scope.Budget);
        }
        return captures;
    }

    private static void CaptureEach(
        string payloadDirectory, HashSet<string> hashes, int kind, int expectedLength,
        Dictionary<string, byte[]> captures, NativeOperationScope scope)
    {
        foreach (string hash in hashes)
        {
            scope.Check();
            string path = Path.Combine(payloadDirectory, hash + ".bin");
            if (!Inspect(path, isDirectory: false, required: false, scope))
                throw new NativePixelException(NativePixelErrors.PixelInventoryMismatch, "A referenced payload is missing.");
            captures[hash] = NativePixelFileSystem.ReadPayload(path, expectedLength, scope);
        }
    }

    // ---- Create writer protocol -------------------------------------------

    internal static (CapturedPixelSource Source, Guid AssetId, Guid LayerId) Create(
        string assetDirectory, string name, int width, int height, NativeOperationScope scope)
    {
        scope.Check();
        NativePixelFormat.ValidateName(name, "$.name");
        NativePixelFormat.ValidateDimensions(width, height);

        string directory = NormalizeAssetDirectory(assetDirectory);
        string? parent = Path.GetDirectoryName(directory);
        string leaf = Path.GetFileName(directory);
        if (string.IsNullOrEmpty(parent) || leaf.Length == 0)
            throw new NativePixelException(EngineErrors.InvalidArguments, "The asset directory must have an existing parent.");

        // (1) Require an existing permitted parent and an entirely absent target.
        Inspect(parent, isDirectory: true, required: true, scope);
        if (TargetOccupied(directory))
            throw AlreadyExists();

        // Build the initial single-layer transparent document in private memory.
        Guid assetId = Guid.NewGuid();
        Guid layerId = Guid.NewGuid();
        while (layerId == assetId) layerId = Guid.NewGuid();

        long pixelBytes = (long)width * height * 4;
        scope.Reserve(24 + pixelBytes);
        byte[] framedPixels = NativePixelFormat.FramePayload(1, width, height, new byte[pixelBytes], scope.Budget);
        string pixelsHash = HashHex(framedPixels, scope);
        var layer = new PixelLayerInfo(layerId, "Layer 1", Visible: true, Opacity: 1m, PixelsSha256: pixelsHash, MaskSha256: null);
        var document = new NativePixelDocument(assetId, Revn: 1, name, width, height, [layer]);
        byte[] metadata = NativePixelFormat.Serialize(document, source: false, scope.Budget);

        // (2) Acquire the stable sibling lock and revalidate parent identity and target absence.
        string lockPath = Path.Combine(parent, ".agd-pixels-lock-" + LockKey(leaf) + ".lock");
        using var writerLock = NativePixelFileSystem.AcquireWriterLock(lockPath, scope);
        Inspect(parent, isDirectory: true, required: true, scope);
        if (TargetOccupied(directory))
            throw AlreadyExists();

        // (4) Build private staging with payloads + canonical metadata, flush and verify.
        string staging = Path.Combine(parent, ".agd-pixels-stage-" + Nonce());
        bool owned = false;
        bool published = false;
        try
        {
            NativePixelFileSystem.CreatePrivateDirectory(staging);
            owned = true;
            string stagedPayloads = Path.Combine(staging, PayloadDirectoryName);
            NativePixelFileSystem.CreatePrivateDirectory(stagedPayloads);
            NativePixelFileSystem.WriteStagedFile(Path.Combine(stagedPayloads, pixelsHash + ".bin"), framedPixels, scope);
            NativePixelFileSystem.WriteStagedFile(Path.Combine(staging, MetadataFileName), metadata, scope);

            CapturedPixelSource verified = Read(staging, scope);
            if (verified.Snapshot.AssetId != assetId || verified.Snapshot.Revn != 1)
                throw new NativePixelException(EngineErrors.StoreUnavailable, "Staged native closure failed verification.");

            // (5)-(6) Revalidate absence and publish the entire directory atomically, no replacement.
            scope.Check();
            if (TargetOccupied(directory))
                throw AlreadyExists();
            NativePixelFileSystem.PublishDirectoryNoReplace(staging, directory);
            published = true;
            return (verified, assetId, layerId);
        }
        finally
        {
            if (owned && !published)
                NativePixelFileSystem.TryRemoveStaging(staging);
        }
    }

    // ---- Existing asset edit ----------------------------------------------

    internal static EditPixelAssetResult Edit(EditPixelAssetRequest request, NativeOperationScope scope)
    {
        scope.Check();
        NativePixelWire.ValidateSnapshot(request.ExpectedSnapshot);
        string directory = NormalizeAssetDirectory(request.AssetDirectory);
        string? parent = Path.GetDirectoryName(directory);
        string leaf = Path.GetFileName(directory);
        if (string.IsNullOrEmpty(parent) || leaf.Length == 0)
            throw new NativePixelException(EngineErrors.InvalidArguments, "The asset directory must have an existing parent.");

        // (1)-(2) Acquire the sibling lock, then freshly reload/verify the source under it and
        //         compare the required snapshot exactly before any replacement-content reads.
        Inspect(parent, isDirectory: true, required: true, scope);
        string lockPath = Path.Combine(parent, ".agd-pixels-lock-" + LockKey(leaf) + ".lock");
        using var writerLock = NativePixelFileSystem.AcquireWriterLock(lockPath, scope);

        CapturedPixelSource before = Read(directory, scope);
        PixelSnapshot expected = request.ExpectedSnapshot;
        if (before.Snapshot.AssetId != expected.AssetId || before.Snapshot.Revn != expected.Revn
            || !string.Equals(before.Snapshot.SourceDigest, expected.SourceDigest, StringComparison.Ordinal))
            throw new NativePixelException(EngineErrors.RevisionConflict, "The asset changed since the expected snapshot.");

        // (3)-(4) Resolve/validate the whole batch privately and perform the exact no-op test.
        EditPlan plan = NativePixelEditEngine.Plan(before, request.Operations, CaptureRawInput, scope);
        if (!plan.Changed)
            return new EditPixelAssetResult(before.Snapshot, Info(before), false, [], [], [], []);

        // (5) Advance the revision exactly once and construct the authoritative after-state fully
        //     BEFORE the visibility point, so nothing computes, reserves or checks cancellation
        //     after publication: once the metadata replace commits, success wins.
        if (before.Document.Revn >= long.MaxValue)
            throw new NativePixelException(EngineErrors.ResourceLimit, "The asset revision cannot advance further.");
        long newRevn = before.Document.Revn + 1;
        var published = new NativePixelDocument(
            before.Document.AssetId, newRevn, before.Document.Name,
            before.Document.Width, before.Document.Height, plan.Document.Layers);
        byte[] metadata = NativePixelFormat.Serialize(published, source: false, scope.Budget);
        long logical = (long)published.Width * published.Height * (4L * published.Layers.Length + MaskCount(published));
        var afterState = new PixelAssetInfo(
            new PixelSnapshot(published.AssetId, newRevn, NativePixelFormat.SourceDigest(published, scope.Budget)),
            published.Name, published.Width, published.Height, logical, published.Layers);

        PublishEdit(directory, plan.NewPayloads, metadata, scope);

        // Publication succeeded (the metadata replace was the single visibility point); do not
        // re-read, re-reserve or re-check cancellation here, or a committed edit could surface as
        // a spurious cancellation/RESOURCE_LIMIT failure.
        return new EditPixelAssetResult(
            before.Snapshot, afterState, true,
            plan.CreatedLayers, plan.AffectedLayerIds, plan.LayerChanges, plan.ChangedRegions);
    }

    private static void PublishEdit(
        string directory, IReadOnlyDictionary<string, byte[]> newPayloads, byte[] metadata, NativeOperationScope scope)
    {
        string payloadDirectory = Path.Combine(directory, PayloadDirectoryName);
        Inspect(payloadDirectory, isDirectory: true, required: true, scope);
        var temporaries = new List<string>(newPayloads.Count + 1);
        try
        {
            // (6) Publish each required new hash object without overwriting; reuse only an exact match.
            //     Owned temporaries live in the asset root under the reserved '.agd-pixels-tmp' name.
            foreach (var (hash, framed) in newPayloads)
            {
                scope.Check();
                string target = Path.Combine(payloadDirectory, hash + ".bin");
                string temp = Path.Combine(directory, ".agd-pixels-tmp-" + Nonce() + ".tmp");
                temporaries.Add(temp);
                NativePixelFileSystem.WriteStagedFile(temp, framed, scope);
                if (!NativePixelFileSystem.PublishFileNoReplace(temp, target))
                {
                    VerifyExistingPayload(target, hash, framed, scope);
                    NativePixelFileSystem.TryRemoveFile(temp);
                }
            }

            // (7)-(8) Complete canonical metadata in an owned temporary, then atomically replace it
            //         last, with a final cancellation check before the single visibility point.
            scope.Check();
            string metadataTemp = Path.Combine(directory, ".agd-pixels-tmp-" + Nonce() + ".tmp");
            temporaries.Add(metadataTemp);
            NativePixelFileSystem.WriteStagedFile(metadataTemp, metadata, scope);
            scope.Check();
            NativePixelFileSystem.ReplaceFileAtomic(metadataTemp, Path.Combine(directory, MetadataFileName));
        }
        finally
        {
            foreach (string temp in temporaries) NativePixelFileSystem.TryRemoveFile(temp);
        }
    }

    private static void VerifyExistingPayload(string target, string hash, byte[] intended, NativeOperationScope scope)
    {
        // Type-check the existing entry (regular file, no symlink/reparse/device) before opening it,
        // exactly as the reader closure does; never reuse or follow a wrong-type addressed object.
        Inspect(target, isDirectory: false, required: true, scope);
        int kind = (int)BinaryPrimitives.ReadUInt32BigEndian(intended.AsSpan(12));
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(intended.AsSpan(16));
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(intended.AsSpan(20));
        byte[] existing = NativePixelFileSystem.ReadPayload(target, intended.Length, scope);
        NativePixelFormat.ValidatePayload(existing, hash, kind, width, height, scope.Budget);
        if (!existing.AsSpan().SequenceEqual(intended))
            throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "An existing payload object differs from the intended bytes.");
    }

    private static byte[] CaptureRawInput(RawSampleInput input, int expected, int opIndex, NativeOperationScope scope)
    {
        string location = "$.operations[" + opIndex.ToString(CultureInfo.InvariantCulture) + "]";
        switch (input)
        {
            case RawSampleInput.Buffer buffer:
                if (buffer.Length != expected)
                    throw new NativePixelException(EngineErrors.InvalidArguments, "A replacement buffer length does not match its rectangle.", location, opIndex);
                scope.Reserve(expected);
                return buffer.Bytes.ToArray();
            case RawSampleInput.File file:
                string normalized = NormalizeInputPath(file.Path, location, opIndex);
                try
                {
                    return NativePixelFileSystem.ReadRawInput(normalized, expected, scope);
                }
                catch (NativePixelException error)
                {
                    throw new NativePixelException(error.Code, error.Message, error.Path ?? location, opIndex);
                }
            default:
                throw new NativePixelException(EngineErrors.InvalidArguments, "Unknown replacement input.", location, opIndex);
        }
    }

    private static string NormalizeInputPath(string path, string location, int opIndex)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path))
            throw new NativePixelException(EngineErrors.InvalidArguments, "A replacement input path must be an absolute local path.", location, opIndex);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    // ---- Query helpers -----------------------------------------------------

    internal static PixelAssetInfo Info(CapturedPixelSource source) => new(
        source.Snapshot, source.Document.Name, source.Document.Width, source.Document.Height,
        source.LogicalSourceBytes, source.Document.Layers);

    internal static PixelRegionResult Region(InspectPixelRegionRequest request, NativeOperationScope scope)
    {
        CapturedPixelSource source = Read(request.AssetDirectory, scope);
        PixelLayerInfo layer = FindLayer(source.Document, request.LayerId);
        PixelRect rect = request.Rect;
        NativePixelWire.ValidateRect(rect, "$.rect", source.Document.Width, source.Document.Height);
        long entries = (long)rect.Width * rect.Height;
        if (entries > 4096)
            throw new NativePixelException(EngineErrors.ResourceLimit, "The requested region exceeds the sample-count limit.");

        int width = source.Document.Width;
        int emitted = 0;
        if (request.Target == PixelTarget.Pixels)
        {
            byte[] payload = source.Payloads[layer.PixelsSha256];
            var samples = ImmutableArray.CreateBuilder<string>((int)entries);
            for (int y = 0; y < rect.Height; y++)
            {
                for (int x = 0; x < rect.Width; x++)
                {
                    int offset = 24 + 4 * ((rect.Y + y) * width + (rect.X + x));
                    samples.Add(string.Create(CultureInfo.InvariantCulture,
                        $"#{payload[offset]:x2}{payload[offset + 1]:x2}{payload[offset + 2]:x2}{payload[offset + 3]:x2}"));
                    if (++emitted % 1024 == 0) scope.Check();
                }
            }
            return new RgbaRegionResult(source.Snapshot, layer.Id, rect, samples.MoveToImmutable());
        }

        if (layer.MaskSha256 is null)
            throw new NativePixelException(EngineErrors.InvalidArguments, "The target layer has no attached mask.");
        byte[] mask = source.Payloads[layer.MaskSha256];
        var maskSamples = ImmutableArray.CreateBuilder<byte>((int)entries);
        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                maskSamples.Add(mask[24 + ((rect.Y + y) * width + (rect.X + x))]);
                if (++emitted % 1024 == 0) scope.Check();
            }
        }
        return new MaskRegionResult(source.Snapshot, layer.Id, rect, maskSamples.MoveToImmutable());
    }

    // ---- Preview / Export / Check -----------------------------------------

    private const int PreviewMaxPixels = 1_048_576;         // 1,048,576 output pixels.
    private const int PreviewMaxEncodedBytes = 8_388_608;   // 8 MiB preview PNG.
    private const int ExportMaxEncodedBytes = 33_554_432;   // 32 MiB export PNG.
    private const int CandidateMaxBytes = 33_554_432;       // 32 MiB check input.

    internal static PixelPreviewResult Preview(PreviewPixelAssetRequest request, NativeOperationScope scope)
    {
        string directory = NormalizeAssetDirectory(request.AssetDirectory);
        CapturedPixelSource source = Read(directory, scope);
        GuardReadSnapshot(source, request.ExpectedReadSnapshot);

        PixelRect rect;
        if (request.Rect is { } explicitRect)
        {
            NativePixelWire.ValidateRect(explicitRect, "$.rect", source.Document.Width, source.Document.Height);
            rect = explicitRect;
        }
        else
        {
            rect = new PixelRect(0, 0, source.Document.Width, source.Document.Height);
        }
        if ((long)rect.Width * rect.Height > PreviewMaxPixels)
            throw new NativePixelException(EngineErrors.ResourceLimit, "The preview region exceeds the preview pixel limit.");

        scope.Reserve(2L * PreviewMaxEncodedBytes + 65536);
        var (png, rgbaHash) = NativePixelRenderer.Encode(
            source.Document, source.Payloads, rect, PreviewMaxEncodedBytes, scope.Budget);
        string fileSha = HashHex(png, scope);

        switch (request.Delivery)
        {
            case PixelPreviewDelivery.Inline:
                return new PixelPreviewResult(
                    source.Snapshot, rect, rgbaHash, PixelPreviewDeliveryKind.Inline, png.Length, fileSha, [.. png]);
            case PixelPreviewDelivery.File file:
                PublishOutput(directory, file.OutputPath, png, file.Overwrite, scope);
                return new PixelPreviewResult(
                    source.Snapshot, rect, rgbaHash, PixelPreviewDeliveryKind.File, png.Length, fileSha, []);
            default:
                throw new NativePixelException(EngineErrors.InvalidArguments, "Unknown preview delivery variant.");
        }
    }

    internal static PixelExportResult Export(ExportPixelAssetRequest request, NativeOperationScope scope)
    {
        string directory = NormalizeAssetDirectory(request.AssetDirectory);
        CapturedPixelSource source = Read(directory, scope);
        GuardReadSnapshot(source, request.ExpectedReadSnapshot);

        var rect = new PixelRect(0, 0, source.Document.Width, source.Document.Height);
        scope.Reserve(2L * ExportMaxEncodedBytes + 65536);
        var (png, rgbaHash) = NativePixelRenderer.Encode(
            source.Document, source.Payloads, rect, ExportMaxEncodedBytes, scope.Budget);
        string fileSha = HashHex(png, scope);
        PublishOutput(directory, request.OutputPath, png, request.Overwrite, scope);
        return new PixelExportResult(source.Snapshot, rect, rgbaHash, png.Length, fileSha);
    }

    internal static PixelExportCheckResult Check(CheckPixelExportRequest request, NativeOperationScope scope)
    {
        string directory = NormalizeAssetDirectory(request.AssetDirectory);
        CapturedPixelSource source = Read(directory, scope);
        GuardReadSnapshot(source, request.ExpectedReadSnapshot);

        var rect = new PixelRect(0, 0, source.Document.Width, source.Document.Height);
        scope.Reserve((long)rect.Width * 4 + 1 + 24L * source.Document.Layers.Length); // Composite filter row + source table.
        string reference = NativePixelRenderer.CompositeHash(source.Document, source.Payloads, rect, scope.Budget);

        // Read-only output boundary: reject a source-colliding, wrong-type or multiply-linked path,
        // and require an existing candidate. No output temporary is created and nothing is written.
        string output = ResolveOutputPath(directory, request.OutputPath, scope);
        if (!NativePixelFileSystem.InspectOutputTarget(output).Exists)
            throw new NativePixelException(NativePixelErrors.PixelInputUnavailable, "The candidate PNG is absent.");

        byte[] candidate;
        try
        {
            candidate = NativePixelFileSystem.ReadCandidatePng(output, CandidateMaxBytes, scope);
        }
        catch (Exception io) when (io is IOException or UnauthorizedAccessException)
        {
            throw new NativePixelException(NativePixelErrors.PixelInputUnavailable, "The candidate PNG is unreadable.");
        }
        scope.Reserve(131072); // Managed PNG decoder working set (two 4*width rows + 32 KiB history + tables).
        NativePngObservation observed = NativePixelRenderer.InspectPng(candidate, scope.Budget, scope.Caller);

        bool dimensionsMatch = observed.Width == rect.Width && observed.Height == rect.Height;
        bool samplesMatch = string.Equals(observed.RgbaSha256, reference, StringComparison.Ordinal);
        var differences = ImmutableArray.CreateBuilder<PixelExportDifference>(2);
        if (!dimensionsMatch) differences.Add(PixelExportDifference.Dimensions);
        if (!samplesMatch) differences.Add(PixelExportDifference.Samples);

        var facts = new PixelObservedPng(
            observed.Width, observed.Height, observed.EncodedBytes, observed.FileSha256, observed.RgbaSha256);
        return new PixelExportCheckResult(
            source.Snapshot, rect, reference, dimensionsMatch && samplesMatch, facts, differences.ToImmutable());
    }

    private static void GuardReadSnapshot(CapturedPixelSource source, PixelSnapshot? expected)
    {
        if (expected is null) return;
        NativePixelWire.ValidateSnapshot(expected, "$.expectedReadSnapshot");
        PixelSnapshot current = source.Snapshot;
        if (current.AssetId != expected.AssetId || current.Revn != expected.Revn
            || !string.Equals(current.SourceDigest, expected.SourceDigest, StringComparison.Ordinal))
            throw new NativePixelException(EngineErrors.RevisionConflict, "The asset changed since the expected read snapshot.");
    }

    /// <summary>
    /// Normalizes and confines an output path: an absolute local file path with an existing permitted
    /// parent, rejecting source collisions (the asset directory, anything inside it, an ancestor of
    /// it, or the reserved sibling lock/staging namespace) and rejecting a directory, symlink or
    /// special-file leaf. It does not inspect link count (the caller's identity probe does that) and
    /// creates nothing.
    /// </summary>
    private static string ResolveOutputPath(string directory, string outputPath, NativeOperationScope scope)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || outputPath.Length > 4096 || !Path.IsPathFullyQualified(outputPath)
            || outputPath.Any(c => c < 32 || c == 127) || outputPath.Contains('\\')
            || outputPath.StartsWith("//", StringComparison.Ordinal))
            throw new NativePixelException(EngineErrors.InvalidArguments, "The output path must be an absolute local file path.");
        int colon = outputPath.IndexOf(':');
        if (colon >= 0 && Uri.CheckSchemeName(outputPath[..colon]))
            throw new NativePixelException(EngineErrors.InvalidArguments, "The output path must be an absolute local file path.");
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));

        // Source-collision rejection. Overlaps (case-insensitive) covers the asset directory, every
        // referenced payload/owned temporary under it, and any ancestor of it. The reserved sibling
        // lock/creation-staging namespace lives in the shared parent under the '.agd-pixels-' prefix.
        if (AssetPaths.Overlaps(output, directory))
            throw new NativePixelException(EngineErrors.PathEscape, "The output path collides with the source asset.");
        string? parent = Path.GetDirectoryName(directory);
        string? outputParent = Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(outputParent))
            throw new NativePixelException(EngineErrors.InvalidArguments, "The output path must have a parent directory.");
        if (parent is not null && string.Equals(outputParent, parent, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(output).StartsWith(".agd-pixels-", StringComparison.Ordinal))
            throw new NativePixelException(EngineErrors.PathEscape, "The output path collides with a reserved storage name.");

        // Existing permitted parent (a missing output directory is a caller-supplied bad location,
        // distinct from a missing source asset) and a non-directory, non-reparse, non-special leaf.
        if (!Inspect(outputParent, isDirectory: true, required: false, scope))
            throw new NativePixelException(EngineErrors.InvalidArguments, "The output parent directory does not exist.");
        Inspect(output, isDirectory: false, required: false, scope);
        return output;
    }

    /// <summary>
    /// Generates the output atomically: an exclusive owned sibling temporary is written and flushed,
    /// destination safety is rechecked at publication, then it is published by the supported single-file
    /// atomic operation — no-replace by default (an existing destination fails), or atomic replace of a
    /// permitted regular file when overwrite is set. Only this call's temporary is cleaned up.
    /// </summary>
    private static void PublishOutput(
        string directory, string outputPath, byte[] png, bool overwrite, NativeOperationScope scope)
    {
        string output = ResolveOutputPath(directory, outputPath, scope);
        NativePixelFileSystem.InspectOutputTarget(output); // Reject a multiply-linked/wrong-type destination; fail closed if unsupported.

        string temp = Path.Combine(Path.GetDirectoryName(output)!, ".agd-pixels-out-" + Nonce() + ".tmp");
        bool published = false;
        try
        {
            try
            {
                NativePixelFileSystem.WriteStagedFile(temp, png, scope);
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
                throw new NativePixelException(NativePixelErrors.PixelWriteFailed, "Native output PNG staging failed.");
            }
            scope.Check();
            NativePixelFileSystem.InspectOutputTarget(output); // Recheck destination safety at publication under cooperative assumptions.
            try
            {
                if (overwrite)
                    NativePixelFileSystem.ReplaceFileAtomic(temp, output);
                else if (!NativePixelFileSystem.PublishFileNoReplace(temp, output))
                    throw new NativePixelException(NativePixelErrors.PixelDestinationExists, "The output path already exists; pass overwrite to replace it.");
            }
            catch (NativePixelException publish) when (publish.Code == EngineErrors.StoreUnavailable)
            {
                throw new NativePixelException(NativePixelErrors.PixelWriteFailed, "Native output PNG publication failed.");
            }
            published = true;
        }
        finally
        {
            if (!published) NativePixelFileSystem.TryRemoveFile(temp);
        }
    }

    // ---- Internals ---------------------------------------------------------

    private static PixelLayerInfo FindLayer(NativePixelDocument document, Guid layerId)
    {
        foreach (var layer in document.Layers)
            if (layer.Id == layerId) return layer;
        throw new NativePixelException(EngineErrors.InvalidArguments, "The requested layer does not exist.");
    }

    private static int MaskCount(NativePixelDocument document)
    {
        int masks = 0;
        foreach (var layer in document.Layers)
            if (layer.MaskSha256 is not null) masks++;
        return masks;
    }

    private static string NormalizeAssetDirectory(string assetDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory) || assetDirectory.Length > 4096)
            throw new NativePixelException(EngineErrors.InvalidArguments, "The asset directory is missing or too long.");
        if (!Path.IsPathFullyQualified(assetDirectory))
            throw new NativePixelException(EngineErrors.InvalidArguments, "The asset directory must be an absolute local path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetDirectory));
    }

    /// <summary>Any entry, including a dangling link, means the Create target is occupied.</summary>
    private static bool TargetOccupied(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        try
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Type/confinement probe over exact named components, mapped to native error codes.</summary>
    private static bool Inspect(string path, bool isDirectory, bool required, NativeOperationScope scope)
    {
        scope.Check();
        try
        {
            return AssetPaths.Inspect(path, isDirectory, required, scope.Caller);
        }
        catch (DocumentEngineException error)
        {
            throw MapStorage(error);
        }
        catch (IOException)
        {
            throw new NativePixelException(EngineErrors.StoreUnavailable, "A native asset path could not be inspected.");
        }
    }

    private static NativePixelException MapStorage(DocumentEngineException error) => error.Code switch
    {
        EngineErrors.PathEscape => new NativePixelException(EngineErrors.PathEscape, "An asset path is unconfined or of an unsafe type."),
        EngineErrors.DocumentNotFound => new NativePixelException(EngineErrors.DocumentNotFound, "A required native asset path was not found."),
        NativePixelErrors.AssetPlatformUnsupported => new NativePixelException(NativePixelErrors.AssetPlatformUnsupported, "The native asset platform primitives are unavailable."),
        _ => new NativePixelException(EngineErrors.StoreUnavailable, "A native asset path could not be accessed."),
    };

    private static string HashHex(byte[] data, NativeOperationScope scope)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < data.Length; offset += 65536)
        {
            scope.Check();
            hash.AppendData(data.AsSpan(offset, Math.Min(65536, data.Length - offset)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string LockKey(string leaf) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(leaf)));

    private static string Nonce() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static NativePixelException AlreadyExists() =>
        new(NativePixelErrors.PixelAlreadyExists, "A directory entry already occupies the create target.");
}
