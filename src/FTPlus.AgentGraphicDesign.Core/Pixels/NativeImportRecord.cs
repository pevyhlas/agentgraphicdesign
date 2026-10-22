using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Artifacts;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

/// <summary>Closed historical v1 format outside native metadata. Only verified captured input reaches this writer.</summary>
internal static class NativeImportRecord
{
    internal const string DecoderProfile = "agd.png-rgba8-managed.v1";
    internal const string InterpretationProfile = "agd.pixel-still-import.srgb-straight-source.v1";
    internal const string LimitsProfile = "agd.pixel-still-import.managed-local.v1";
    internal const int MaxBytes = 16_384;

    internal static byte[] Serialize(CapturedStill input, Guid assetId, Guid layerId,
        string pixelsSha256, NativeOperationScope scope)
    {
        scope.Check();
        scope.Reserve(4L * MaxBytes + 65_536); // writer, exact bytes, identity scratch and bounded result.
        var buffer = new ArrayBufferWriter<byte>(MaxBytes);
        using var writer = new Utf8JsonWriter(buffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("schema", "agd.pixel-still-import");
        writer.WriteNumber("version", 1);
        writer.WriteString("inputProfile", ArtifactDescriptorContract.Profile);
        writer.WriteString("decoderProfile", DecoderProfile);
        writer.WriteString("interpretationProfile", InterpretationProfile);
        writer.WriteString("execution", "managed-in-process");
        writer.WriteString("limitsProfile", LimitsProfile);
        writer.WriteStartObject("source");
        writer.WriteString("assetId", input.Descriptor.AssetId);
        writer.WriteString("stillName", input.Descriptor.Still!.Name);
        writer.WriteNumber("width", input.Descriptor.Canvas.Width);
        writer.WriteNumber("height", input.Descriptor.Canvas.Height);
        writer.WriteString("pngPath", input.Descriptor.Files[0].Path);
        writer.WriteStartObject("pivot");
        writer.WritePropertyName("x");
        writer.WriteRawValue(input.Descriptor.Still.Pivot.X.ToString("0.######", CultureInfo.InvariantCulture));
        writer.WritePropertyName("y");
        writer.WriteRawValue(input.Descriptor.Still.Pivot.Y.ToString("0.######", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
        writer.WriteString("descriptorSha256", input.DescriptorSha256);
        writer.WriteString("metadataDigest", input.MetadataDigest);
        writer.WriteString("encodedSha256", input.Descriptor.Files[0].Sha256);
        writer.WriteNumber("encodedByteLength", input.Png.LongLength);
        writer.WriteString("rgbaSha256", input.RgbaSha256);
        writer.WriteEndObject();
        writer.WriteStartObject("mapping");
        writer.WriteString("assetId", assetId.ToString("D"));
        writer.WriteString("layerId", layerId.ToString("D"));
        writer.WriteString("pixelsSha256", pixelsSha256);
        writer.WriteEndObject();
        writer.WriteBoolean("admitted", false);
        writer.WriteBoolean("provenanceVerified", false);
        writer.WriteEndObject();
        writer.Flush();
        if (buffer.WrittenCount > MaxBytes)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Import record exceeds its byte limit.");
        scope.Check();
        return buffer.WrittenSpan.ToArray();
    }

    internal static string Identity(byte[] record)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("agd.pixel-still-import.record.v1\n"u8);
        hash.AppendData(record);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static PixelStillImportInfo Info(CapturedStill input, byte[] record, string pixelsSha256)
    {
        string identity = Identity(record);
        return new PixelStillImportInfo(identity, "imports/" + identity + "/record.json",
            input.MetadataDigest, input.DescriptorSha256, input.Descriptor.Files[0].Sha256,
            input.RgbaSha256, pixelsSha256);
    }
}
