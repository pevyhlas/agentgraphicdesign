using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

internal sealed record NativePixelDocument(
    Guid AssetId, long Revn, string Name, int Width, int Height, ImmutableArray<PixelLayerInfo> Layers);

public sealed record PixelLayerInfo(
    Guid Id, string Name, bool Visible, decimal Opacity, string PixelsSha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? MaskSha256);

public sealed record PixelSnapshot(Guid AssetId, long Revn, string SourceDigest);

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

[JsonConverter(typeof(JsonStringEnumConverter<PixelTarget>))]
public enum PixelTarget
{
    [JsonStringEnumMemberName("pixels")] Pixels,
    [JsonStringEnumMemberName("mask")] Mask
}

public abstract record PixelLayerRef
{
    private PixelLayerRef() { }
    protected PixelLayerRef(PixelLayerRef Original)
    {
        // A record's protected copy constructor must not open an external union variant.
        if (Original is null || GetType() != Original.GetType())
            throw new NativePixelException("INVALID_ARGUMENTS", "Unknown layer reference variant.");
    }
    public sealed record ById(Guid Id) : PixelLayerRef;
    public sealed record ByAlias(string Alias) : PixelLayerRef;
}

public abstract record PixelEdit
{
    private PixelEdit() { }
    protected PixelEdit(PixelEdit Original)
    {
        if (Original is null || GetType() != Original.GetType())
            throw new NativePixelException("INVALID_ARGUMENTS", "Unknown pixel edit variant.");
    }
    public sealed record AddLayer(string Name, string? Alias = null) : PixelEdit;
    public sealed record RemoveLayer(PixelLayerRef Layer) : PixelEdit;
    public sealed record ReorderLayer(PixelLayerRef Layer, int Index) : PixelEdit;
    public sealed record SetVisibility(PixelLayerRef Layer, bool Visible) : PixelEdit;
    public sealed record SetOpacity(PixelLayerRef Layer, decimal Opacity) : PixelEdit;
    public sealed record AttachMask(PixelLayerRef Layer) : PixelEdit;
    public sealed record RemoveMask(PixelLayerRef Layer) : PixelEdit;
    public sealed record FillPixels(PixelLayerRef Layer, PixelRect Rect, string Rgba) : PixelEdit;
    public sealed record FillMask(PixelLayerRef Layer, PixelRect Rect, byte Value) : PixelEdit;
    public sealed record Clear(PixelLayerRef Layer, PixelTarget Target, PixelRect Rect) : PixelEdit;
    public sealed record Replace(PixelLayerRef Layer, PixelTarget Target, PixelRect Rect, RawSampleInput Input) : PixelEdit;
}

public abstract class RawSampleInput
{
    private RawSampleInput() { }

    public sealed class File : RawSampleInput
    {
        public File(string Path) => this.Path = Path;
        public string Path { get; }
    }

    public sealed class Buffer : RawSampleInput
    {
        internal Buffer(byte[] Samples) => Bytes = Samples;
        internal ReadOnlyMemory<byte> Bytes { get; }
        public int Length => Bytes.Length;
    }

    public static Buffer FromBytes(ReadOnlySpan<byte> Samples, CancellationToken CancellationToken = default)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (Samples.Length > 16 * 1024 * 1024)
            throw new NativePixelException("RESOURCE_LIMIT", "Replacement input exceeds its byte limit.");
        var owned = new byte[Samples.Length];
        for (int offset = 0; offset < Samples.Length; offset += 65536)
        {
            CancellationToken.ThrowIfCancellationRequested();
            Samples.Slice(offset, Math.Min(65536, Samples.Length - offset)).CopyTo(owned.AsSpan(offset));
        }
        return new Buffer(owned);
    }
}

public abstract record PixelPreviewDelivery
{
    private PixelPreviewDelivery() { }
    protected PixelPreviewDelivery(PixelPreviewDelivery Original)
    {
        if (Original is null || GetType() != Original.GetType())
            throw new NativePixelException("INVALID_ARGUMENTS", "Unknown preview delivery variant.");
    }
    public sealed record Inline : PixelPreviewDelivery;
    public sealed record File(string OutputPath, bool Overwrite = false) : PixelPreviewDelivery;
}

public sealed record CreatePixelAssetRequest(string AssetDirectory, string Name, int Width, int Height);
public sealed record ImportPixelAssetRequest(string AssetDirectory, string DescriptorPath, string Name, string LayerName);
public sealed record ImportPixelLayerRequest(
    string AssetDirectory, string DescriptorPath, PixelSnapshot ExpectedSnapshot, string LayerName);
public sealed record InspectPixelAssetRequest(string AssetDirectory);
public sealed record InspectPixelRegionRequest(string AssetDirectory, Guid LayerId, PixelTarget Target, PixelRect Rect);
public sealed record EditPixelAssetRequest(
    string AssetDirectory, PixelSnapshot ExpectedSnapshot, ImmutableArray<PixelEdit> Operations);
public sealed record ValidatePixelAssetRequest(string AssetDirectory);
public sealed record PreviewPixelAssetRequest(
    string AssetDirectory, PixelPreviewDelivery Delivery, PixelRect? Rect = null, PixelSnapshot? ExpectedReadSnapshot = null);
public sealed record ExportPixelAssetRequest(
    string AssetDirectory, string OutputPath, bool Overwrite = false, PixelSnapshot? ExpectedReadSnapshot = null);
public sealed record CheckPixelExportRequest(
    string AssetDirectory, string OutputPath, PixelSnapshot? ExpectedReadSnapshot = null);

public sealed record PixelAssetInfo(
    PixelSnapshot Snapshot, string Name, int Width, int Height, long LogicalSourceBytes, ImmutableArray<PixelLayerInfo> Layers)
{
    public string Profile => NativePixelFormat.Profile;
    public string LimitsProfile => NativePixelFormat.LimitsProfile;
}

public sealed record CreatePixelAssetResult(PixelAssetInfo State, Guid InitialLayerId);

public sealed record ImportPixelAssetResult(PixelAssetInfo State, Guid InitialLayerId, PixelStillImportInfo Import);
public sealed record ImportPixelLayerResult(
    PixelSnapshot PreviousSnapshot, PixelAssetInfo State, Guid LayerId, PixelStillImportInfo Import);

/// <summary>Historical captured-byte/interpretation evidence, not full admission or verified provenance.</summary>
public sealed record PixelStillImportInfo(
    string RecordSha256, string RecordPath, string MetadataDigest, string DescriptorSha256,
    string EncodedSha256, string RgbaSha256, string PixelsSha256)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string DecoderProfile => NativeImportRecord.DecoderProfile;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string InterpretationProfile => NativeImportRecord.InterpretationProfile;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Execution => "managed-in-process";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Admitted => false;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool ProvenanceVerified => false;
}

public abstract record PixelRegionResult(PixelSnapshot Snapshot, Guid LayerId, PixelRect Rect)
{
    public string Profile => NativePixelFormat.Profile;
    public string LimitsProfile => NativePixelFormat.LimitsProfile;
    public abstract PixelTarget Target { get; }
}

public sealed record RgbaRegionResult(
    PixelSnapshot Snapshot, Guid LayerId, PixelRect Rect, ImmutableArray<string> Samples)
    : PixelRegionResult(Snapshot, LayerId, Rect)
{
    public override PixelTarget Target => PixelTarget.Pixels;
}

public sealed record MaskRegionResult(
    PixelSnapshot Snapshot, Guid LayerId, PixelRect Rect, ImmutableArray<byte> Samples)
    : PixelRegionResult(Snapshot, LayerId, Rect)
{
    public override PixelTarget Target => PixelTarget.Mask;
}

public sealed record PixelDiagnostic(string Code, string Message, string? Path = null);

public abstract record ValidatePixelAssetResult
{
    private protected ValidatePixelAssetResult() { }
    public abstract bool Valid { get; }
}

public sealed record ValidPixelAssetResult(PixelAssetInfo State) : ValidatePixelAssetResult
{
    public override bool Valid => true;
}

public sealed record InvalidPixelAssetResult(ImmutableArray<PixelDiagnostic> Errors) : ValidatePixelAssetResult
{
    public override bool Valid => false;
}

public sealed record PixelCreatedLayer(
    int OperationIndex, Guid LayerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Alias = null);

[JsonConverter(typeof(JsonStringEnumConverter<PixelLayerChangeKind>))]
public enum PixelLayerChangeKind
{
    [JsonStringEnumMemberName("created")] Created,
    [JsonStringEnumMemberName("removed")] Removed,
    [JsonStringEnumMemberName("order")] Order,
    [JsonStringEnumMemberName("visibility")] Visibility,
    [JsonStringEnumMemberName("opacity")] Opacity,
    [JsonStringEnumMemberName("maskPresence")] MaskPresence,
    [JsonStringEnumMemberName("pixels")] Pixels,
    [JsonStringEnumMemberName("mask")] Mask
}

public sealed record PixelLayerChange(Guid LayerId, ImmutableArray<PixelLayerChangeKind> Changes);
public sealed record PixelChangedRegion(Guid LayerId, PixelTarget Target, PixelRect Rect);
public sealed record EditPixelAssetResult(
    PixelSnapshot BeforeSnapshot, PixelAssetInfo State, bool Changed,
    ImmutableArray<PixelCreatedLayer> CreatedLayers, ImmutableArray<Guid> AffectedLayerIds,
    ImmutableArray<PixelLayerChange> LayerChanges, ImmutableArray<PixelChangedRegion> ChangedRegions);

public sealed record PixelEvaluationSettings
{
    public int FractionalBits => 24;
    public string TableSha256 => "353442b2a3b85e1030cc800349cc40d4d0b7e2bdf063a7a9766298f4dabaa7b8";
    public string Rounding => "nearest-ties-up";
}

public abstract record PixelEvaluationEvidence(PixelSnapshot SourceToken, PixelRect Rect, string RgbaSha256)
{
    private static readonly PixelEvaluationSettings FixedSettings = new();
    public string Profile => NativePixelFormat.Profile;
    public string LimitsProfile => NativePixelFormat.LimitsProfile;
    public string EvaluatorProfile => NativePixelFormat.Profile;
    public PixelEvaluationSettings Settings => FixedSettings;
    public int Width => Rect.Width;
    public int Height => Rect.Height;
}

[JsonConverter(typeof(JsonStringEnumConverter<PixelPreviewDeliveryKind>))]
public enum PixelPreviewDeliveryKind
{
    [JsonStringEnumMemberName("inline")] Inline,
    [JsonStringEnumMemberName("file")] File
}

public sealed record PixelPreviewResult(
    PixelSnapshot SourceToken, PixelRect Rect, string RgbaSha256, PixelPreviewDeliveryKind Delivery,
    long EncodedBytes, string FileSha256, [property: JsonIgnore] ImmutableArray<byte> PngBytes)
    : PixelEvaluationEvidence(SourceToken, Rect, RgbaSha256)
{
    public string EncoderProfile => "agd.pixels.png.v1";
    public string MediaType => "image/png";
}

public sealed record PixelExportResult(
    PixelSnapshot SourceToken, PixelRect Rect, string RgbaSha256, long EncodedBytes, string FileSha256)
    : PixelEvaluationEvidence(SourceToken, Rect, RgbaSha256)
{
    public string EncoderProfile => "agd.pixels.png.v1";
    public string MediaType => "image/png";
}

public sealed record PixelObservedPng(int Width, int Height, long EncodedBytes, string FileSha256, string RgbaSha256);

[JsonConverter(typeof(JsonStringEnumConverter<PixelExportDifference>))]
public enum PixelExportDifference
{
    [JsonStringEnumMemberName("dimensions")] Dimensions,
    [JsonStringEnumMemberName("samples")] Samples
}

public sealed record PixelExportCheckResult(
    PixelSnapshot SourceToken, PixelRect Rect, string RgbaSha256, bool Matches,
    PixelObservedPng Observed, ImmutableArray<PixelExportDifference> Differences)
    : PixelEvaluationEvidence(SourceToken, Rect, RgbaSha256)
{
    public string Comparison => "dimensions-and-decoded-rgba8";
    public string ComparatorProfile => "agd.pixels.png-samples.v1";
    public string PngPolicy => "agd.png-rgba8-structure.v1";
    public string DecoderProfile => "agd.png-rgba8-managed.v1";
    public string Execution => "managed-in-process";
    public bool PixelsDecoded => true;
}

public sealed class NativePixelException : Exception
{
    public NativePixelException(string Code, string Message, string? Path = null, int? OperationIndex = null)
        : base(SafeMessage(Message))
    {
        this.Code = Code is { Length: > 0 and <= 64 } && Code.All(c => c is >= 'A' and <= 'Z' or '_')
            ? Code : "INVALID_ARGUMENTS";
        this.Path = SafePath(Path);
        this.OperationIndex = OperationIndex is >= 0 and < 64 ? OperationIndex : null;
    }

    public string Code { get; }
    public string? Path { get; }
    public int? OperationIndex { get; }

    private static string? SafePath(string? Path) => Path is { Length: > 0 and <= 512 } && Path[0] == '$'
        && Path.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '$' or '.' or '[' or ']' or '_')
        ? Path : null;

    private static string SafeMessage(string? Message)
    {
        if (string.IsNullOrEmpty(Message)) return "Native pixel operation failed.";
        // Diagnostics are host-authored, never a channel for raw paths or exception details.
        if (Message.Contains('/') || Message.Contains('\\')) return "Native pixel operation failed.";
        var result = new StringBuilder(Math.Min(Message.Length, 1024));
        int count = 0;
        foreach (var rune in Message.EnumerateRunes())
        {
            if (++count > 1024) break;
            int value = rune.Value;
            if (value is <= 31 or >= 127 and <= 159 or >= 0x2028 and <= 0x202e or >= 0x2066 and <= 0x2069 or 0xfeff)
                result.Append(' ');
            else result.Append(rune.ToString());
        }
        return result.ToString();
    }
}

/// <summary>Native pixel error codes shared across the format, storage and renderer layers.</summary>
internal static class NativePixelErrors
{
    public const string PixelContentMismatch = "PIXEL_CONTENT_MISMATCH";
    public const string PixelInventoryMismatch = "PIXEL_INVENTORY_MISMATCH";
    public const string PixelAlreadyExists = "PIXEL_ALREADY_EXISTS";
    public const string PixelDestinationExists = "PIXEL_DESTINATION_EXISTS";
    public const string PixelInputUnavailable = "PIXEL_INPUT_UNAVAILABLE";
    public const string PixelBusy = "PIXEL_BUSY";
    public const string PixelProfileUnsupported = "PIXEL_PROFILE_UNSUPPORTED";
    public const string PixelWriteFailed = "PIXEL_WRITE_FAILED";
    public const string PixelWriteOutcomeUnknown = "PIXEL_WRITE_OUTCOME_UNKNOWN";
    public const string AssetPlatformUnsupported = "ASSET_PLATFORM_UNSUPPORTED";

    // Surface-only code (CLI/MCP): Core never throws it. A fully decoded, supported output PNG whose
    // dimensions and/or decoded samples differ from a fresh evaluation is a completed check result
    // (Matches=false), which the surfaces map to this code, not the manifest-specific ASSET_DRIFT.
    public const string PixelOutputDrift = "PIXEL_OUTPUT_DRIFT";

    // Fixed reason messages that distinguish the two PIXEL_BUSY cases (API and storage contracts).
    public const string SlotBusyMessage = "Native operation slot is busy.";
    public const string LockWaitExpiredMessage = "Asset writer lock wait expired.";
}
