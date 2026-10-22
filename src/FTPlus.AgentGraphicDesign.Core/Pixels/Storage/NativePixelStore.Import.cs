namespace FTPlus.AgentGraphicDesign.Core.Pixels.Storage;

internal static partial class NativePixelStore
{
    internal static ImportPixelAssetResult Import(ImportPixelAssetRequest request, NativeOperationScope scope,
        string? stagingNonceForTests = null)
    {
        scope.Check();
        NativePixelFormat.ValidateName(request.Name, "$.name", EngineErrors.InvalidArguments);
        NativePixelFormat.ValidateName(request.LayerName, "$.layerName", EngineErrors.InvalidArguments);
        string directory = NormalizeAssetDirectory(request.AssetDirectory);
        string parent = ImportParent(directory);
        Inspect(parent, isDirectory: true, required: true, scope);
        if (TargetOccupied(directory)) throw AlreadyExists();
        using var writerLock = NativePixelFileSystem.AcquireWriterLock(
            Path.Combine(parent, ".agd-pixels-lock-" + LockKey(Path.GetFileName(directory)) + ".lock"), scope);
        Inspect(parent, isDirectory: true, required: true, scope);
        if (TargetOccupied(directory)) throw AlreadyExists();

        var input = NativeStillInput.Capture(request.DescriptorPath, scope);
        int width = checked((int)input.Descriptor.Canvas.Width), height = checked((int)input.Descriptor.Canvas.Height);
        Guid assetId = Guid.NewGuid();
        Guid layerId = NewImportLayerId(assetId, []);
        var (framed, pixelsHash) = FrameImport(input, width, height, scope);
        var layer = new PixelLayerInfo(layerId, request.LayerName, true, 1m, pixelsHash, null);
        var document = new NativePixelDocument(assetId, 1, request.Name, width, height, [layer]);
        scope.Reserve(262_144); // canonical metadata/source identity and bounded native state.
        byte[] metadata = NativePixelFormat.Serialize(document, source: false, scope.Budget);
        byte[] record = NativeImportRecord.Serialize(input, assetId, layerId, pixelsHash, scope);
        var evidence = NativeImportRecord.Info(input, record, pixelsHash);
        string staging = Path.Combine(parent, ".agd-pixels-stage-" + ImportStageNonce(stagingNonceForTests));
        bool owned = false;
        bool published = false;
        try
        {
            NativePixelFileSystem.CreatePrivateDirectory(staging);
            owned = true;
            PreserveImport(staging, input, record, evidence.RecordSha256, scope);
            string payloads = Path.Combine(staging, PayloadDirectoryName);
            NativePixelFileSystem.CreatePrivateDirectory(payloads);
            scope.Reserve(131_072); // staged payload/metadata stream buffers.
            NativePixelFileSystem.WriteStagedFile(Path.Combine(payloads, pixelsHash + ".bin"), framed, scope);
            NativePixelFileSystem.WriteStagedFile(Path.Combine(staging, MetadataFileName), metadata, scope);
            var verified = Read(staging, scope);
            var result = new ImportPixelAssetResult(Info(verified), layerId, evidence);
            scope.Check();
            if (TargetOccupied(directory)) throw AlreadyExists();
            NativePixelFileSystem.PublishDirectoryNoReplace(staging, directory);
            published = true;
            return result; // Known visibility success: no reads, reservations or cancellation afterward.
        }
        finally
        {
            if (owned && !published) NativePixelFileSystem.TryRemoveStaging(staging);
        }
    }

    internal static ImportPixelLayerResult ImportLayer(ImportPixelLayerRequest request, NativeOperationScope scope,
        string? stagingNonceForTests = null)
    {
        scope.Check();
        NativePixelWire.ValidateSnapshot(request.ExpectedSnapshot);
        NativePixelFormat.ValidateName(request.LayerName, "$.layerName", EngineErrors.InvalidArguments);
        string directory = NormalizeAssetDirectory(request.AssetDirectory);
        string parent = ImportParent(directory);
        Inspect(parent, isDirectory: true, required: true, scope);
        using var writerLock = NativePixelFileSystem.AcquireWriterLock(
            Path.Combine(parent, ".agd-pixels-lock-" + LockKey(Path.GetFileName(directory)) + ".lock"), scope);
        var before = Read(directory, scope);
        GuardReadSnapshot(before, request.ExpectedSnapshot);
        long addedBytes = 4L * before.Document.Width * before.Document.Height;
        if (before.Document.Layers.Length >= NativePixelFormat.MaxLayers
            || before.LogicalSourceBytes + addedBytes > NativePixelFormat.MaxLogicalSourceBytes
            || before.Document.Revn == long.MaxValue)
            throw new NativePixelException(EngineErrors.ResourceLimit, "The asset cannot accept another imported layer.");
        Inspect(Path.Combine(directory, "imports"), isDirectory: true, required: false, scope);
        var input = NativeStillInput.Capture(request.DescriptorPath, scope);
        if (input.Descriptor.Canvas.Width != before.Document.Width || input.Descriptor.Canvas.Height != before.Document.Height)
            throw new NativePixelException(EngineErrors.InvalidArguments, "The imported canvas must match the native asset.", "$.canvas");
        Guid layerId = NewImportLayerId(before.Document.AssetId, before.Document.Layers.Select(layer => layer.Id));
        var (framed, pixelsHash) = FrameImport(input, before.Document.Width, before.Document.Height, scope);
        var layer = new PixelLayerInfo(layerId, request.LayerName, true, 1m, pixelsHash, null);
        var document = before.Document with { Revn = before.Document.Revn + 1, Layers = before.Document.Layers.Add(layer) };
        scope.Reserve(393_216); // metadata/source identity, bounded state/result and two write streams.
        byte[] metadata = NativePixelFormat.Serialize(document, source: false, scope.Budget);
        var state = new PixelAssetInfo(new PixelSnapshot(document.AssetId, document.Revn,
            NativePixelFormat.SourceDigest(document, scope.Budget)), document.Name,
            document.Width, document.Height, before.LogicalSourceBytes + addedBytes, document.Layers);
        byte[] record = NativeImportRecord.Serialize(input, document.AssetId, layerId, pixelsHash, scope);
        var result = new ImportPixelLayerResult(before.Snapshot, state, layerId, NativeImportRecord.Info(input, record, pixelsHash));
        PreserveImport(directory, input, record, result.Import.RecordSha256, scope, stagingNonceForTests);
        PublishEdit(directory, new Dictionary<string, byte[]> { [pixelsHash] = framed }, metadata, scope);
        return result; // All result construction preceded native metadata visibility.
    }

    private static string ImportParent(string directory) => Path.GetDirectoryName(directory) is { Length: > 0 } parent
        && Path.GetFileName(directory).Length > 0 ? parent
        : throw new NativePixelException(EngineErrors.InvalidArguments, "The asset directory must have an existing parent.");

    private static Guid NewImportLayerId(Guid assetId, IEnumerable<Guid> existing)
    {
        var ids = existing.ToHashSet();
        ids.Add(assetId);
        ids.Add(Guid.Empty);
        Guid id;
        do { id = Guid.NewGuid(); } while (ids.Contains(id));
        return id;
    }

    // Deterministic fault qualification is internal only; public requests cannot choose storage names.
    private static string ImportStageNonce(string? forTests)
    {
        if (forTests is null) return Nonce();
        if (forTests.Length != 32 || forTests.AsSpan().ContainsAnyExcept("0123456789abcdef"))
            throw new NativePixelException(EngineErrors.InvalidArguments, "Invalid internal staging nonce.");
        return forTests;
    }

    private static (byte[] Framed, string Hash) FrameImport(CapturedStill input, int width, int height, NativeOperationScope scope)
    {
        scope.Reserve(24L + input.Rgba.Length);
        byte[] framed = NativePixelFormat.FramePayload(1, width, height, input.Rgba, scope.Budget);
        return (framed, HashHex(framed, scope));
    }

    // Whole addressed objects are independently staged and published atomically. This is historical
    // input evidence, not a native metadata back-reference or durable retry/commit ledger.
    internal static void PreserveImport(string directory, CapturedStill input, byte[] record,
        string identity, NativeOperationScope scope, string? stagingNonceForTests = null)
    {
        scope.Check();
        string imports = Path.Combine(directory, "imports");
        if (!Inspect(imports, isDirectory: true, required: false, scope))
            NativePixelFileSystem.CreatePrivateDirectory(imports);
        string target = Path.Combine(imports, identity);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["record.json"] = record,
            ["bundle/descriptor.json"] = input.Metadata,
            ["bundle/" + input.Descriptor.Files[0].Path] = input.Png
        };
        var inventory = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string file in files.Keys)
        {
            if (Path.Combine(target, file).Length > 4096)
                throw new NativePixelException(EngineErrors.ResourceLimit, "Retained import path exceeds its limit.");
            inventory.Add(file, false);
            for (int i = 0; i < file.Length; i++)
                if (file[i] == '/') inventory.TryAdd(file[..i], true);
        }
        scope.Reserve(327_680); // three staged write streams, streaming verification and bounded inventories.
        if (TargetOccupied(target))
        {
            VerifyImportDirectory(target, files, inventory, scope);
            return;
        }
        string staging = Path.Combine(directory, ".agd-pixels-import-stage-" + ImportStageNonce(stagingNonceForTests));
        bool owned = false;
        bool published = false;
        try
        {
            NativePixelFileSystem.CreatePrivateDirectory(staging);
            owned = true;
            foreach (string relative in inventory.Where(entry => entry.Value).Select(entry => entry.Key).OrderBy(path => path.Length))
                NativePixelFileSystem.CreatePrivateDirectory(Path.Combine(staging, relative));
            foreach (var (relative, bytes) in files)
                NativePixelFileSystem.WriteStagedFile(Path.Combine(staging, relative), bytes, scope);
            VerifyImportDirectory(staging, files, inventory, scope);
            scope.Check();
            try
            {
                NativePixelFileSystem.PublishDirectoryNoReplace(staging, target);
                published = true;
            }
            catch (NativePixelException occupied) when (occupied.Code == NativePixelErrors.PixelAlreadyExists)
            {
                VerifyImportDirectory(target, files, inventory, scope);
            }
        }
        finally
        {
            if (owned && !published) NativePixelFileSystem.TryRemoveStaging(staging);
        }
    }

    private static void VerifyImportDirectory(string directory, IReadOnlyDictionary<string, byte[]> files,
        IReadOnlyDictionary<string, bool> inventory, NativeOperationScope scope)
    {
        Inspect(directory, isDirectory: true, required: true, scope);
        var remaining = new Dictionary<string, bool>(inventory, StringComparer.Ordinal);
        var pending = new Stack<(string Path, string Relative)>();
        pending.Push((directory, ""));
        while (pending.TryPop(out var current))
        {
            scope.Check();
            foreach (string entry in Directory.EnumerateFileSystemEntries(current.Path))
            {
                scope.Check();
                string relative = current.Relative.Length == 0 ? Path.GetFileName(entry) : current.Relative + "/" + Path.GetFileName(entry);
                if (!remaining.Remove(relative, out bool isDirectory))
                    throw new NativePixelException(NativePixelErrors.PixelInventoryMismatch, "Retained import has unexpected inventory.");
                Inspect(entry, isDirectory, required: true, scope);
                if (isDirectory) pending.Push((entry, relative));
            }
        }
        if (remaining.Count != 0)
            throw new NativePixelException(NativePixelErrors.PixelInventoryMismatch, "Retained import is missing required inventory.");
        var scratch = new byte[65_536];
        foreach (var (relative, intended) in files)
        {
            scope.Check();
            using var stream = new FileStream(Path.Combine(directory, relative), FileMode.Open,
                FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
            int offset = 0;
            while (offset < intended.Length)
            {
                scope.Check();
                int count = stream.Read(scratch, 0, Math.Min(scratch.Length, intended.Length - offset));
                if (count == 0 || !scratch.AsSpan(0, count).SequenceEqual(intended.AsSpan(offset, count)))
                    throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "Retained import bytes differ from captured input.");
                offset += count;
            }
            scope.Check();
            if (stream.ReadByte() != -1)
                throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "Retained import contains trailing bytes.");
        }
    }
}
