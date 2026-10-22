using System.Collections.Immutable;
using System.Security.Cryptography;

namespace FTPlus.AgentGraphicDesign.Core.Pixels.Storage;

/// <summary>
/// Captures one replacement operation's raw samples into owned bytes: exactly
/// <paramref name="expectedLength"/> bytes, charged to the working reservation. The verbatim
/// per-operation path lets a failure carry the operation index and schema path.
/// </summary>
internal delegate byte[] RawInputCapturer(
    RawSampleInput input, int expectedLength, int operationIndex, NativeOperationScope scope);

/// <summary>
/// The verified in-memory outcome of resolving one edit batch against a freshly captured source.
/// Nothing here is published; the storage write protocol turns a changed plan into new payloads and
/// a replaced metadata file. <see cref="Document"/> keeps the old revision; the caller assigns the
/// published <c>oldRevn + 1</c>. <see cref="NewPayloads"/> holds only framed objects absent from the
/// old closure. All difference collections are computed from the before/final states.
/// </summary>
internal sealed record EditPlan(
    bool Changed,
    NativePixelDocument Document,
    IReadOnlyDictionary<string, byte[]> NewPayloads,
    ImmutableArray<PixelCreatedLayer> CreatedLayers,
    ImmutableArray<Guid> AffectedLayerIds,
    ImmutableArray<PixelLayerChange> LayerChanges,
    ImmutableArray<PixelChangedRegion> ChangedRegions);

/// <summary>
/// The ordered edit-batch engine (P6). It resolves aliases/IDs against the state immediately
/// before each operation, applies every operation to a private copy-on-write model, enforces every
/// intermediate invariant and budget, captures raw inputs once, canonicalizes the candidate and
/// performs the exact byte-for-byte no-op test, and computes the before/final difference. It reads
/// no filesystem state and publishes nothing; all I/O is the storage layer's.
/// </summary>
internal static class NativePixelEditEngine
{
    private const int MaxOperations = 64;
    private const long MaxReplacementInputBytes = 16 * 1024 * 1024;
    private const long MaxCumulativeWork = 16_777_216;
    private const int HeaderSize = NativePixelFormat.HeaderSize;

    private sealed class WorkLayer
    {
        public Guid Id;
        public string Name = "";
        public bool Visible;
        public decimal Opacity;
        public byte[] PixelBuffer = [];
        public int PixelOffset;
        public bool PixelOwned;
        public byte[]? MaskBuffer;
        public int MaskOffset;
        public bool MaskOwned;
        public int? CreatedOpIndex;
        public string? Alias;
        public int OriginalIndex = -1;
        public string? OriginalPixelHash;
        public string? OriginalMaskHash;
        public bool HasMask => MaskBuffer is not null;
    }

    internal static EditPlan Plan(
        CapturedPixelSource before, ImmutableArray<PixelEdit> operations,
        RawInputCapturer capture, NativeOperationScope scope)
    {
        scope.Check();
        if (operations.IsDefault || operations.Length is < 1 or > MaxOperations)
            throw new NativePixelException(EngineErrors.InvalidArguments, "An edit batch has one to sixty-four operations.", "$.operations");

        int width = before.Document.Width, height = before.Document.Height;
        long area = (long)width * height;
        int pixelLength = checked((int)(area * 4));
        int maskLength = checked((int)area);

        var layers = new List<WorkLayer>(before.Document.Layers.Length);
        var usedIds = new HashSet<Guid> { before.Document.AssetId };
        for (int i = 0; i < before.Document.Layers.Length; i++)
        {
            var info = before.Document.Layers[i];
            usedIds.Add(info.Id);
            layers.Add(new WorkLayer
            {
                Id = info.Id,
                Name = info.Name,
                Visible = info.Visible,
                Opacity = info.Opacity,
                PixelBuffer = before.Payloads[info.PixelsSha256],
                PixelOffset = HeaderSize,
                PixelOwned = false,
                OriginalPixelHash = info.PixelsSha256,
                MaskBuffer = info.MaskSha256 is null ? null : before.Payloads[info.MaskSha256],
                MaskOffset = info.MaskSha256 is null ? 0 : HeaderSize,
                MaskOwned = false,
                OriginalMaskHash = info.MaskSha256,
                OriginalIndex = i,
            });
        }

        var aliases = new Dictionary<string, Guid>(StringComparer.Ordinal);
        long work = 0, replacementTotal = 0;

        for (int opIndex = 0; opIndex < operations.Length; opIndex++)
        {
            scope.Check();
            string path = "$.operations[" + opIndex + "]";
            PixelEdit op = operations[opIndex];
            switch (op)
            {
                case PixelEdit.AddLayer add:
                    {
                        NativePixelFormat.ValidateName(add.Name, path + ".name", EngineErrors.InvalidArguments);
                        if (add.Alias is not null)
                        {
                            NativePixelWire.ValidateAlias(add.Alias, path + ".alias");
                            if (aliases.ContainsKey(add.Alias))
                                throw Fail("Alias is already declared in this batch.", path + ".alias", opIndex);
                        }
                        Guid id = MintLayerId(usedIds);
                        var layer = new WorkLayer
                        {
                            Id = id,
                            Name = add.Name,
                            Visible = true,
                            Opacity = 1m,
                            PixelBuffer = Reserved(pixelLength, scope),
                            PixelOffset = 0,
                            PixelOwned = true,
                            CreatedOpIndex = opIndex,
                            Alias = add.Alias,
                            OriginalIndex = -1,
                        };
                        layers.Add(layer);
                        if (add.Alias is not null) aliases[add.Alias] = id;
                        ChargeWork(ref work, area, path);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.RemoveLayer remove:
                    {
                        int index = Resolve(layers, aliases, remove.Layer, path, opIndex);
                        if (layers.Count == 1)
                            throw Fail("Cannot remove the only remaining layer.", path + ".layer", opIndex);
                        layers.RemoveAt(index);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.ReorderLayer reorder:
                    {
                        int index = Resolve(layers, aliases, reorder.Layer, path, opIndex);
                        if (reorder.Index < 0 || reorder.Index >= layers.Count)
                            throw Fail("Reorder index is outside the current layer range.", path + ".index", opIndex);
                        var moved = layers[index];
                        layers.RemoveAt(index);
                        layers.Insert(reorder.Index, moved);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.SetVisibility visibility:
                    {
                        int index = Resolve(layers, aliases, visibility.Layer, path, opIndex);
                        layers[index].Visible = visibility.Visible;
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.SetOpacity opacity:
                    {
                        int index = Resolve(layers, aliases, opacity.Layer, path, opIndex);
                        NativePixelFormat.OpacityMillionths(opacity.Opacity, path + ".opacity", EngineErrors.InvalidArguments);
                        layers[index].Opacity = opacity.Opacity;
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.AttachMask attach:
                    {
                        int index = Resolve(layers, aliases, attach.Layer, path, opIndex);
                        var layer = layers[index];
                        if (layer.HasMask)
                            throw Fail("The layer already has a mask.", path + ".layer", opIndex);
                        var mask = Reserved(maskLength, scope);
                        mask.AsSpan().Fill(255);
                        layer.MaskBuffer = mask;
                        layer.MaskOffset = 0;
                        layer.MaskOwned = true;
                        ChargeWork(ref work, area, path);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.RemoveMask removeMask:
                    {
                        int index = Resolve(layers, aliases, removeMask.Layer, path, opIndex);
                        var layer = layers[index];
                        layer.MaskBuffer = null;
                        layer.MaskOffset = 0;
                        layer.MaskOwned = false;
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.FillPixels fill:
                    {
                        int index = Resolve(layers, aliases, fill.Layer, path, opIndex);
                        NativePixelWire.ValidateRect(fill.Rect, path + ".rect", width, height);
                        (byte r, byte g, byte b, byte a) = ParseRgba(fill.Rgba, path + ".rgba", opIndex);
                        Span<byte> pixels = MutablePixels(layers[index], pixelLength, scope);
                        WritePixelRect(pixels, width, fill.Rect, r, g, b, a, scope);
                        ChargeWork(ref work, (long)fill.Rect.Width * fill.Rect.Height, path);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.FillMask fillMask:
                    {
                        int index = Resolve(layers, aliases, fillMask.Layer, path, opIndex);
                        var layer = layers[index];
                        NativePixelWire.ValidateRect(fillMask.Rect, path + ".rect", width, height);
                        if (!layer.HasMask)
                            throw Fail("The target layer has no attached mask.", path + ".layer", opIndex);
                        Span<byte> mask = MutableMask(layer, maskLength, scope);
                        WriteMaskRect(mask, width, fillMask.Rect, fillMask.Value, scope);
                        ChargeWork(ref work, (long)fillMask.Rect.Width * fillMask.Rect.Height, path);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.Clear clear:
                    {
                        int index = Resolve(layers, aliases, clear.Layer, path, opIndex);
                        var layer = layers[index];
                        NativePixelWire.ValidateRect(clear.Rect, path + ".rect", width, height);
                        if (clear.Target == PixelTarget.Pixels)
                            WritePixelRect(MutablePixels(layer, pixelLength, scope), width, clear.Rect, 0, 0, 0, 0, scope);
                        else
                        {
                            if (!layer.HasMask) throw Fail("The target layer has no attached mask.", path + ".layer", opIndex);
                            WriteMaskRect(MutableMask(layer, maskLength, scope), width, clear.Rect, 0, scope);
                        }
                        ChargeWork(ref work, (long)clear.Rect.Width * clear.Rect.Height, path);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                case PixelEdit.Replace replace:
                    {
                        int index = Resolve(layers, aliases, replace.Layer, path, opIndex);
                        var layer = layers[index];
                        NativePixelWire.ValidateRect(replace.Rect, path + ".rect", width, height);
                        long rectArea = (long)replace.Rect.Width * replace.Rect.Height;
                        int expected = checked((int)(rectArea * (replace.Target == PixelTarget.Pixels ? 4 : 1)));
                        if (expected > MaxReplacementInputBytes - replacementTotal)
                            throw new NativePixelException(EngineErrors.ResourceLimit, "Replacement inputs exceed the aggregate byte limit.", path + ".rect", opIndex);
                        replacementTotal += expected;
                        if (replace.Target == PixelTarget.Mask && !layer.HasMask)
                            throw Fail("The target layer has no attached mask.", path + ".layer", opIndex);
                        byte[] samples = capture(replace.Input, expected, opIndex, scope);
                        if (samples.Length != expected)
                            throw Fail("A replacement input length does not match its rectangle.", path, opIndex);
                        if (replace.Target == PixelTarget.Pixels)
                            ReplacePixelRect(MutablePixels(layer, pixelLength, scope), width, replace.Rect, samples, scope);
                        else
                            ReplaceMaskRect(MutableMask(layer, maskLength, scope), width, replace.Rect, samples, scope);
                        ChargeWork(ref work, rectArea, path);
                        ValidateState(layers, area, opIndex);
                        break;
                    }
                default:
                    throw Fail("Unknown edit operation.", path, opIndex);
            }
        }

        // Build the candidate document, hashing only touched surfaces and reusing existing hashes.
        var newPayloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var infos = ImmutableArray.CreateBuilder<PixelLayerInfo>(layers.Count);
        var finalPixelHash = new string[layers.Count];
        var finalMaskHash = new string?[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            scope.Check();
            var layer = layers[i];
            string pixelHash = layer.PixelOwned
                ? RegisterFramed(1, width, height, PixelSpan(layer, pixelLength), before, newPayloads, scope)
                : layer.OriginalPixelHash!;
            string? maskHash = !layer.HasMask ? null
                : layer.MaskOwned
                    ? RegisterFramed(2, width, height, MaskSpan(layer, maskLength), before, newPayloads, scope)
                    : layer.OriginalMaskHash!;
            finalPixelHash[i] = pixelHash;
            finalMaskHash[i] = maskHash;
            infos.Add(new PixelLayerInfo(layer.Id, layer.Name, layer.Visible, layer.Opacity, pixelHash, maskHash));
        }
        var candidate = new NativePixelDocument(
            before.Document.AssetId, before.Document.Revn, before.Document.Name, width, height, infos.ToImmutable());
        NativePixelFormat.ValidateDocument(candidate);

        // Exact byte-for-byte canonical no-op test (source form; revision excluded).
        byte[] candidateCanonical = NativePixelFormat.Serialize(candidate, source: true, scope.Budget);
        byte[] beforeCanonical = NativePixelFormat.Serialize(before.Document, source: true, scope.Budget);
        bool changed = !candidateCanonical.AsSpan().SequenceEqual(beforeCanonical);
        if (!changed)
            return new EditPlan(false, candidate, ReadOnlyEmpty(), [], [], [], []);

        var diff = Difference(before, layers, finalPixelHash, finalMaskHash, width, height, pixelLength, maskLength, scope);
        return new EditPlan(true, candidate, newPayloads, diff.Created, diff.Affected, diff.Changes, diff.Regions);
    }

    // ---- Difference computation --------------------------------------------

    private static (ImmutableArray<PixelCreatedLayer> Created, ImmutableArray<Guid> Affected,
        ImmutableArray<PixelLayerChange> Changes, ImmutableArray<PixelChangedRegion> Regions) Difference(
        CapturedPixelSource before, List<WorkLayer> layers, string[] finalPixelHash, string?[] finalMaskHash,
        int width, int height, int pixelLength, int maskLength, NativeOperationScope scope)
    {
        var beforeById = new Dictionary<Guid, (int Index, PixelLayerInfo Info)>();
        for (int i = 0; i < before.Document.Layers.Length; i++)
            beforeById[before.Document.Layers[i].Id] = (i, before.Document.Layers[i]);
        var finalIds = new HashSet<Guid>();
        foreach (var layer in layers) finalIds.Add(layer.Id);

        var created = ImmutableArray.CreateBuilder<PixelCreatedLayer>();
        var affected = ImmutableArray.CreateBuilder<Guid>();
        var changes = ImmutableArray.CreateBuilder<PixelLayerChange>();
        var regions = ImmutableArray.CreateBuilder<PixelChangedRegion>();

        for (int i = 0; i < layers.Count; i++)
        {
            scope.Check();
            var layer = layers[i];
            if (layer.CreatedOpIndex is int opIndex)
            {
                created.Add(new PixelCreatedLayer(opIndex, layer.Id, layer.Alias));
                affected.Add(layer.Id);
                changes.Add(new PixelLayerChange(layer.Id, [PixelLayerChangeKind.Created]));
                regions.Add(new PixelChangedRegion(layer.Id, PixelTarget.Pixels, WholeCanvas(width, height)));
                if (layer.HasMask)
                    regions.Add(new PixelChangedRegion(layer.Id, PixelTarget.Mask, WholeCanvas(width, height)));
                continue;
            }

            var (originalIndex, info) = beforeById[layer.Id];
            var kinds = new List<PixelLayerChangeKind>(6);
            if (i != originalIndex) kinds.Add(PixelLayerChangeKind.Order);
            if (layer.Visible != info.Visible) kinds.Add(PixelLayerChangeKind.Visibility);
            if (NativePixelFormat.OpacityMillionths(layer.Opacity) != NativePixelFormat.OpacityMillionths(info.Opacity))
                kinds.Add(PixelLayerChangeKind.Opacity);
            bool maskBefore = info.MaskSha256 is not null, maskAfter = layer.HasMask;
            if (maskBefore != maskAfter) kinds.Add(PixelLayerChangeKind.MaskPresence);
            bool pixelsChanged = !string.Equals(finalPixelHash[i], info.PixelsSha256, StringComparison.Ordinal);
            if (pixelsChanged) kinds.Add(PixelLayerChangeKind.Pixels);
            bool maskChanged = maskBefore && maskAfter
                && !string.Equals(finalMaskHash[i], info.MaskSha256, StringComparison.Ordinal);
            if (maskChanged) kinds.Add(PixelLayerChangeKind.Mask);

            if (kinds.Count == 0) continue;
            affected.Add(layer.Id);
            changes.Add(new PixelLayerChange(layer.Id, [.. kinds]));

            if (pixelsChanged)
                regions.Add(new PixelChangedRegion(layer.Id, PixelTarget.Pixels,
                    BoundingBox(before.Payloads[info.PixelsSha256].AsSpan(HeaderSize), PixelSpan(layer, pixelLength), width, height, 4, scope)));
            if (maskAfter && !maskBefore)
                regions.Add(new PixelChangedRegion(layer.Id, PixelTarget.Mask, WholeCanvas(width, height)));
            else if (maskChanged)
                regions.Add(new PixelChangedRegion(layer.Id, PixelTarget.Mask,
                    BoundingBox(before.Payloads[info.MaskSha256!].AsSpan(HeaderSize), MaskSpan(layer, maskLength), width, height, 1, scope)));
        }

        // Removed pre-existing layers follow, in original bottom-to-top order.
        for (int i = 0; i < before.Document.Layers.Length; i++)
        {
            var info = before.Document.Layers[i];
            if (finalIds.Contains(info.Id)) continue;
            affected.Add(info.Id);
            changes.Add(new PixelLayerChange(info.Id, [PixelLayerChangeKind.Removed]));
        }

        // Created layers are reported by add-operation index, which a later reorder can reshuffle
        // away from final layer order.
        created.Sort((left, right) => left.OperationIndex.CompareTo(right.OperationIndex));
        return (created.ToImmutable(), affected.ToImmutable(), changes.ToImmutable(), regions.ToImmutable());
    }

    // ---- Surface helpers ---------------------------------------------------

    private static Span<byte> MutablePixels(WorkLayer layer, int length, NativeOperationScope scope)
    {
        if (!layer.PixelOwned)
        {
            var owned = Reserved(length, scope);
            layer.PixelBuffer.AsSpan(layer.PixelOffset, length).CopyTo(owned);
            layer.PixelBuffer = owned;
            layer.PixelOffset = 0;
            layer.PixelOwned = true;
        }
        return layer.PixelBuffer.AsSpan(layer.PixelOffset, length);
    }

    private static Span<byte> MutableMask(WorkLayer layer, int length, NativeOperationScope scope)
    {
        if (!layer.MaskOwned)
        {
            var owned = Reserved(length, scope);
            layer.MaskBuffer!.AsSpan(layer.MaskOffset, length).CopyTo(owned);
            layer.MaskBuffer = owned;
            layer.MaskOffset = 0;
            layer.MaskOwned = true;
        }
        return layer.MaskBuffer!.AsSpan(layer.MaskOffset, length);
    }

    private static ReadOnlySpan<byte> PixelSpan(WorkLayer layer, int length) => layer.PixelBuffer.AsSpan(layer.PixelOffset, length);
    private static ReadOnlySpan<byte> MaskSpan(WorkLayer layer, int length) => layer.MaskBuffer!.AsSpan(layer.MaskOffset, length);

    private static void WritePixelRect(Span<byte> pixels, int width, PixelRect rect, byte r, byte g, byte b, byte a, NativeOperationScope scope)
    {
        int done = 0;
        for (int y = 0; y < rect.Height; y++)
        {
            int rowStart = ((rect.Y + y) * width + rect.X) * 4;
            for (int x = 0; x < rect.Width; x++)
            {
                int offset = rowStart + x * 4;
                pixels[offset] = r; pixels[offset + 1] = g; pixels[offset + 2] = b; pixels[offset + 3] = a;
                if ((++done & 4095) == 0) scope.Check();
            }
        }
    }

    private static void WriteMaskRect(Span<byte> mask, int width, PixelRect rect, byte value, NativeOperationScope scope)
    {
        int done = 0;
        for (int y = 0; y < rect.Height; y++)
        {
            int rowStart = (rect.Y + y) * width + rect.X;
            for (int x = 0; x < rect.Width; x++)
            {
                mask[rowStart + x] = value;
                if ((++done & 4095) == 0) scope.Check();
            }
        }
    }

    private static void ReplacePixelRect(Span<byte> pixels, int width, PixelRect rect, ReadOnlySpan<byte> samples, NativeOperationScope scope)
    {
        int done = 0, source = 0;
        for (int y = 0; y < rect.Height; y++)
        {
            int rowStart = ((rect.Y + y) * width + rect.X) * 4;
            for (int x = 0; x < rect.Width; x++, source += 4)
            {
                samples.Slice(source, 4).CopyTo(pixels.Slice(rowStart + x * 4, 4));
                if ((++done & 4095) == 0) scope.Check();
            }
        }
    }

    private static void ReplaceMaskRect(Span<byte> mask, int width, PixelRect rect, ReadOnlySpan<byte> samples, NativeOperationScope scope)
    {
        int done = 0, source = 0;
        for (int y = 0; y < rect.Height; y++)
        {
            int rowStart = (rect.Y + y) * width + rect.X;
            for (int x = 0; x < rect.Width; x++, source++)
            {
                mask[rowStart + x] = samples[source];
                if ((++done & 4095) == 0) scope.Check();
            }
        }
    }

    private static PixelRect BoundingBox(
        ReadOnlySpan<byte> before, ReadOnlySpan<byte> after, int width, int height, int channels, NativeOperationScope scope)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, done = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * channels;
                bool different = false;
                for (int c = 0; c < channels; c++)
                    if (before[offset + c] != after[offset + c]) { different = true; break; }
                if (different)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                if ((++done & 4095) == 0) scope.Check();
            }
        }
        // A changed surface has at least one differing sample, so the bounds are valid.
        return new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static PixelRect WholeCanvas(int width, int height) => new(0, 0, width, height);

    // ---- Building blocks ---------------------------------------------------

    private static string RegisterFramed(
        int kind, int width, int height, ReadOnlySpan<byte> samples,
        CapturedPixelSource before, Dictionary<string, byte[]> newPayloads, NativeOperationScope scope)
    {
        byte[] framed = NativePixelFormat.FramePayload(kind, width, height, samples, scope.Budget);
        scope.Reserve(framed.Length);
        string hash = Hash(framed, scope);
        if (!before.Payloads.ContainsKey(hash)) newPayloads.TryAdd(hash, framed);
        return hash;
    }

    private static string Hash(ReadOnlySpan<byte> framed, NativeOperationScope scope)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < framed.Length; offset += 65536)
        {
            scope.Check();
            digest.AppendData(framed.Slice(offset, Math.Min(65536, framed.Length - offset)));
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static (byte R, byte G, byte B, byte A) ParseRgba(string rgba, string path, int opIndex)
    {
        NativePixelWire.ValidateRgba(rgba, path);
        return (Byte(rgba, 1), Byte(rgba, 3), Byte(rgba, 5), Byte(rgba, 7));
        static byte Byte(string text, int at) => (byte)((Nibble(text[at]) << 4) | Nibble(text[at + 1]));
        static int Nibble(char c) => c <= '9' ? c - '0' : c - 'a' + 10;
    }

    private static int Resolve(
        List<WorkLayer> layers, Dictionary<string, Guid> aliases, PixelLayerRef reference, string path, int opIndex)
    {
        Guid id;
        switch (reference)
        {
            case PixelLayerRef.ById byId:
                id = byId.Id;
                break;
            case PixelLayerRef.ByAlias byAlias:
                if (!aliases.TryGetValue(byAlias.Alias, out id))
                    throw Fail("Alias does not identify an earlier layer declaration.", path + ".layer", opIndex);
                break;
            default:
                throw Fail("Unknown layer reference.", path + ".layer", opIndex);
        }
        for (int i = 0; i < layers.Count; i++)
            if (layers[i].Id == id) return i;
        throw Fail("The referenced layer is not currently present.", path + ".layer", opIndex);
    }

    private static Guid MintLayerId(HashSet<Guid> used)
    {
        Guid id;
        do { id = Guid.NewGuid(); } while (!used.Add(id));
        return id;
    }

    private static byte[] Reserved(int length, NativeOperationScope scope)
    {
        scope.Reserve(length);
        return new byte[length];
    }

    private static void ChargeWork(ref long work, long samples, string path)
    {
        work = checked(work + samples);
        if (work > MaxCumulativeWork)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Cumulative edit work exceeds its sample limit.", path);
    }

    private static void ValidateState(List<WorkLayer> layers, long area, int opIndex)
    {
        if (layers.Count is < 1 or > NativePixelFormat.MaxLayers)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Layer count is outside the runtime limit.",
                "$.operations[" + opIndex + "]");
        int masks = 0;
        foreach (var layer in layers) if (layer.HasMask) masks++;
        long logical = area * (4L * layers.Count + masks);
        if (logical > NativePixelFormat.MaxLogicalSourceBytes)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Logical source samples exceed the runtime limit.",
                "$.operations[" + opIndex + "]");
    }

    private static IReadOnlyDictionary<string, byte[]> ReadOnlyEmpty() =>
        System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty;

    private static NativePixelException Fail(string message, string path, int opIndex) =>
        new(EngineErrors.InvalidArguments, message, path, opIndex);
}
