using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

internal static class NativePixelFormat
{
    internal const string Schema = "agd.native-pixels";
    internal const string Profile = "agd.pixels.rgba8-linear-over.v1";
    internal const string LimitsProfile = "agd.pixels.runtime.v1";
    internal const string MetadataFileName = "asset.agd-pixels.json";
    internal const string PayloadDirectoryName = "payloads";
    internal const int HeaderSize = 24;
    internal const int MaxJsonBytes = 1_048_576;
    internal const int MaxAxis = 8192;
    internal const long MaxArea = 4_194_304;
    internal const int MaxLayers = 16;
    internal const long MaxLogicalSourceBytes = 67_108_864;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static NativePixelDocument Parse(ReadOnlyMemory<byte> utf8, Action? checkBudget = null)
    {
        using var json = ReadJson(utf8, MaxJsonBytes, 4, 1536, "PARSE_ERROR", checkBudget);
        var root = json.RootElement;
        ObjectFields(root, ["schema", "version", "profile", "assetId", "revn", "name", "canvas", "layers"], [], "$", "PARSE_ERROR");
        if (String(root.GetProperty("schema"), "$.schema", "PARSE_ERROR") != Schema)
            throw Error("UNSUPPORTED_VERSION", "Unsupported native pixel schema.", "$.schema");
        if (Integer(root.GetProperty("version"), "$.version", "INVALID_PROPERTY") != 1)
            throw Error("UNSUPPORTED_VERSION", "Unsupported native pixel version.", "$.version");
        if (String(root.GetProperty("profile"), "$.profile", "PARSE_ERROR") != Profile)
            throw Error("PIXEL_PROFILE_UNSUPPORTED", "Unsupported native pixel profile.", "$.profile");
        Guid assetId = GuidValue(root.GetProperty("assetId"), "$.assetId", "INVALID_ID");
        long revn = Integer(root.GetProperty("revn"), "$.revn", "INVALID_PROPERTY");
        string name = String(root.GetProperty("name"), "$.name", "PARSE_ERROR");
        var canvas = root.GetProperty("canvas");
        ObjectFields(canvas, ["width", "height"], [], "$.canvas", "PARSE_ERROR");
        long width = Integer(canvas.GetProperty("width"), "$.canvas.width", "INVALID_DIMENSIONS");
        long height = Integer(canvas.GetProperty("height"), "$.canvas.height", "INVALID_DIMENSIONS");
        ValidateDimensions(width, height);
        var layers = root.GetProperty("layers");
        if (layers.ValueKind != JsonValueKind.Array)
            throw Error("PARSE_ERROR", "Expected a layer array.", "$.layers");
        if (layers.GetArrayLength() is < 1 or > MaxLayers)
            throw Error("RESOURCE_LIMIT", "Layer count is outside the runtime limit.", "$.layers");
        var builder = ImmutableArray.CreateBuilder<PixelLayerInfo>(layers.GetArrayLength());
        foreach (var layer in layers.EnumerateArray())
        {
            checkBudget?.Invoke();
            string path = "$.layers[" + builder.Count.ToString(CultureInfo.InvariantCulture) + "]";
            ObjectFields(layer, ["id", "name", "visible", "opacity", "pixelsSha256", "maskSha256"], [], path, "PARSE_ERROR");
            var mask = layer.GetProperty("maskSha256");
            builder.Add(new PixelLayerInfo(
                GuidValue(layer.GetProperty("id"), path + ".id", "INVALID_ID"),
                String(layer.GetProperty("name"), path + ".name", "PARSE_ERROR"),
                Boolean(layer.GetProperty("visible"), path + ".visible", "PARSE_ERROR"),
                Opacity(layer.GetProperty("opacity"), path + ".opacity", "INVALID_PROPERTY"),
                String(layer.GetProperty("pixelsSha256"), path + ".pixelsSha256", "PARSE_ERROR"),
                mask.ValueKind == JsonValueKind.Null ? null : String(mask, path + ".maskSha256", "PARSE_ERROR")));
        }
        var document = new NativePixelDocument(assetId, revn, name, (int)width, (int)height, builder.MoveToImmutable());
        ValidateDocument(document);
        return document;
    }

    internal static void ValidateDocument(NativePixelDocument document)
    {
        if (document is null) throw Error("PARSE_ERROR", "Expected native pixel metadata.", "$");
        if (document.AssetId == Guid.Empty) throw Error("INVALID_ID", "Asset ID must be nonempty.", "$.assetId");
        if (document.Revn < 1) throw Error("INVALID_PROPERTY", "Revision must be positive.", "$.revn");
        ValidateName(document.Name, "$.name");
        ValidateDimensions(document.Width, document.Height);
        if (document.Layers.IsDefault || document.Layers.Length is < 1 or > MaxLayers)
            throw Error("RESOURCE_LIMIT", "Layer count is outside the runtime limit.", "$.layers");
        var ids = new HashSet<Guid> { document.AssetId };
        int masks = 0;
        for (int i = 0; i < document.Layers.Length; i++)
        {
            var layer = document.Layers[i];
            string path = "$.layers[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (layer is null) throw Error("PARSE_ERROR", "Expected a layer object.", path);
            if (layer.Id == Guid.Empty || !ids.Add(layer.Id))
                throw Error("INVALID_ID", "Asset and layer IDs must be nonempty and distinct.", path + ".id");
            ValidateName(layer.Name, path + ".name");
            OpacityMillionths(layer.Opacity, path + ".opacity");
            ValidateHash(layer.PixelsSha256, path + ".pixelsSha256");
            if (layer.MaskSha256 is not null)
            {
                ValidateHash(layer.MaskSha256, path + ".maskSha256");
                masks++;
            }
        }
        long logical = (long)document.Width * document.Height * (4L * document.Layers.Length + masks);
        if (logical > MaxLogicalSourceBytes)
            throw Error("RESOURCE_LIMIT", "Logical source samples exceed the runtime limit.", "$.layers");
        // All validated names/fields together fit well below 64 KiB, hence below the 1 MiB canonical cap.
    }

    internal static byte[] Serialize(NativePixelDocument document, bool source = false, Action? checkBudget = null)
    {
        checkBudget?.Invoke();
        ValidateDocument(document);
        var text = new StringBuilder(16384);
        text.Append("{\"schema\":\"").Append(Schema).Append("\",\"version\":1,\"profile\":\"").Append(Profile)
            .Append("\",\"assetId\":\"").Append(document.AssetId.ToString("D")).Append('"');
        if (!source) text.Append(",\"revn\":").Append(document.Revn.ToString(CultureInfo.InvariantCulture));
        text.Append(",\"name\":");
        Quoted(text, document.Name);
        text.Append(",\"canvas\":{\"width\":").Append(document.Width.ToString(CultureInfo.InvariantCulture))
            .Append(",\"height\":").Append(document.Height.ToString(CultureInfo.InvariantCulture)).Append("},\"layers\":[");
        for (int i = 0; i < document.Layers.Length; i++)
        {
            checkBudget?.Invoke();
            var layer = document.Layers[i];
            if (i != 0) text.Append(',');
            text.Append("{\"id\":\"").Append(layer.Id.ToString("D")).Append("\",\"name\":");
            Quoted(text, layer.Name);
            text.Append(",\"visible\":").Append(layer.Visible ? "true" : "false")
                .Append(",\"opacity\":").Append((OpacityMillionths(layer.Opacity) / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture))
                .Append(",\"pixelsSha256\":\"").Append(layer.PixelsSha256).Append("\",\"maskSha256\":");
            if (layer.MaskSha256 is null) text.Append("null");
            else Quoted(text, layer.MaskSha256);
            text.Append('}');
        }
        text.Append("]}");
        string result = text.ToString();
        if (StrictUtf8.GetByteCount(result) > MaxJsonBytes)
            throw Error("RESOURCE_LIMIT", "Canonical metadata exceeds its byte limit.", "$");
        checkBudget?.Invoke();
        return StrictUtf8.GetBytes(result);
    }

    private static void Quoted(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (char c in value)
        {
            if (c is '"' or '\\') text.Append('\\');
            text.Append(c);
        }
        text.Append('"');
    }

    internal static string SourceDigest(NativePixelDocument document, Action? checkBudget = null)
    {
        byte[] bytes = Serialize(document, source: true, checkBudget);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("agd.native-pixels.source.v1\n"u8);
        for (int offset = 0; offset < bytes.Length; offset += 65536)
        {
            checkBudget?.Invoke();
            hash.AppendData(bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static byte[] FramePayload(int kind, int width, int height, ReadOnlySpan<byte> samples, Action? checkBudget = null)
    {
        checkBudget?.Invoke();
        ValidateDimensions(width, height);
        if (kind is not (1 or 2)) throw Error("PIXEL_PROFILE_UNSUPPORTED", "Unsupported native payload kind.");
        long size = (long)width * height * (kind == 1 ? 4 : 1);
        if (samples.Length != size) throw Error(NativePixelErrors.PixelContentMismatch, "Payload sample length does not match its dimensions.");
        var framed = new byte[checked(HeaderSize + (int)size)];
        "AGDPIXL\n"u8.CopyTo(framed);
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(12), (uint)kind);
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(20), (uint)height);
        for (int offset = 0; offset < samples.Length; offset += 65536)
        {
            checkBudget?.Invoke();
            samples.Slice(offset, Math.Min(65536, samples.Length - offset)).CopyTo(framed.AsSpan(HeaderSize + offset));
        }
        return framed;
    }

    internal static void ValidatePayload(
        ReadOnlySpan<byte> framed, string hash, int kind, int width, int height, Action? checkBudget = null)
    {
        checkBudget?.Invoke();
        if (framed.Length < HeaderSize || !framed[..8].SequenceEqual("AGDPIXL\n"u8))
            throw Error(NativePixelErrors.PixelContentMismatch, "Invalid native payload header.");
        if (BinaryPrimitives.ReadUInt32BigEndian(framed[8..]) != 1)
            throw Error("UNSUPPORTED_VERSION", "Unsupported native payload version.");
        uint actualKind = BinaryPrimitives.ReadUInt32BigEndian(framed[12..]);
        if (actualKind is not (1 or 2))
            throw Error("PIXEL_PROFILE_UNSUPPORTED", "Unsupported native payload kind.");
        if (kind is not (1 or 2) || actualKind != kind || width is < 1 or > MaxAxis || height is < 1 or > MaxAxis
            || (long)width * height > MaxArea
            || BinaryPrimitives.ReadUInt32BigEndian(framed[16..]) != width
            || BinaryPrimitives.ReadUInt32BigEndian(framed[20..]) != height
            || framed.Length != HeaderSize + (long)width * height * (kind == 1 ? 4 : 1))
            throw Error(NativePixelErrors.PixelContentMismatch, "Payload kind, dimensions or length do not match the reference.");
        if (!IsHash(hash)) throw Error(NativePixelErrors.PixelContentMismatch, "Invalid payload hash reference.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < framed.Length; offset += 65536)
        {
            checkBudget?.Invoke();
            digest.AppendData(framed.Slice(offset, Math.Min(65536, framed.Length - offset)));
        }
        if (!string.Equals(Convert.ToHexStringLower(digest.GetHashAndReset()), hash, StringComparison.Ordinal))
            throw Error(NativePixelErrors.PixelContentMismatch, "Payload bytes do not match the reference hash.");
    }

    internal static void ValidateDimensions(long width, long height, string code = "INVALID_DIMENSIONS", string path = "$.canvas")
    {
        if (width < 1 || height < 1) throw Error(code, "Canvas dimensions must be positive integers.", path);
        if (width > MaxAxis || height > MaxAxis || width * height > MaxArea)
            throw Error("RESOURCE_LIMIT", "Canvas dimensions exceed the runtime limit.", path);
    }

    internal static void ValidateName(string? name, string path = "$.name", string code = "INVALID_PROPERTY")
    {
        if (name is null || name.Length is < 1 or > 256) throw Error(code, "Invalid native display name.", path);
        int scalars = 0, bytes = 0;
        bool nonblank = false;
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out int consumed) != OperationStatus.Done)
                throw Error(code, "Display name contains invalid Unicode.", path);
            remaining = remaining[consumed..];
            int value = rune.Value;
            if (++scalars > 128 || (bytes += rune.Utf8SequenceLength) > 512
                || value is <= 0x1f or >= 0x7f and <= 0x9f or >= 0x2028 and <= 0x202e or >= 0x2066 and <= 0x2069 or 0xfeff)
                throw Error(code, "Display name exceeds its bounds or contains a prohibited scalar.", path);
            if (!(value is >= 9 and <= 13 or 0x20 or 0x85 or 0xa0 or 0x1680 or >= 0x2000 and <= 0x200a or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000))
                nonblank = true;
        }
        if (!nonblank) throw Error(code, "Display name must not be blank.", path);
    }

    internal static int OpacityMillionths(decimal opacity, string path = "$.opacity", string code = "INVALID_PROPERTY")
    {
        if (opacity is < 0 or > 1) throw Error(code, "Opacity must be exact integer millionths between zero and one.", path);
        decimal scaled = opacity * 1_000_000m;
        if (scaled != decimal.Truncate(scaled))
            throw Error(code, "Opacity must be exact integer millionths between zero and one.", path);
        return (int)scaled;
    }

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void ValidateHash(string? value, string path, string code = "INVALID_PROPERTY")
    {
        if (!IsHash(value)) throw Error(code, "Expected a lowercase SHA-256 digest.", path);
    }

    internal static JsonDocument ReadJson(
        ReadOnlyMemory<byte> utf8, int maxBytes, int maxDepth, int maxStringBytes, string code, Action? checkBudget = null)
    {
        checkBudget?.Invoke();
        if (utf8.Length > maxBytes) throw Error("RESOURCE_LIMIT", "JSON input exceeds its byte limit.", "$");
        var bytes = utf8.Span;
        if (bytes.StartsWith("\uFEFF"u8)) throw Error(code, "JSON must not contain a UTF-8 BOM.", "$");
        // Validate before JsonDocument owns an input-sized copy. Rune decoding rejects overlong UTF-8 and surrogates.
        int checkpoint = 0, invalidUtf8Offset = -1;
        for (int offset = 0; offset < bytes.Length;)
        {
            if (offset >= checkpoint)
            {
                checkBudget?.Invoke();
                checkpoint = offset + 65532;
            }
            if (Rune.DecodeFromUtf8(bytes[offset..], out _, out int consumed) != OperationStatus.Done)
            {
                invalidUtf8Offset = offset;
                break;
            }
            offset += consumed;
        }
        int? operationIndex = null;
        checkpoint = 0;
        bool pendingOperations = false, operationsArray = false;
        int nextOperation = 0;
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maxDepth });
        try
        {
            while (reader.Read())
            {
                if (reader.BytesConsumed >= checkpoint)
                {
                    checkBudget?.Invoke();
                    checkpoint = checked((int)reader.BytesConsumed + 65532);
                }
                if (code == "INVALID_ARGUMENTS" && operationsArray && reader.CurrentDepth == 2
                    && reader.TokenType is not (JsonTokenType.EndObject or JsonTokenType.EndArray))
                    operationIndex = nextOperation++;
                if (invalidUtf8Offset >= reader.TokenStartIndex && invalidUtf8Offset < reader.BytesConsumed)
                    throw Error(code, "JSON contains invalid UTF-8.");
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
                {
                    if (reader.ValueSpan.Length > maxStringBytes)
                        throw Error("RESOURCE_LIMIT", "JSON string token exceeds its byte limit.");
                    ValidateEscapes(reader.ValueSpan, code);
                }
                if (code == "INVALID_ARGUMENTS")
                {
                    if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == 1)
                    {
                        operationsArray = false;
                        operationIndex = null;
                    }
                    if (pendingOperations)
                    {
                        operationsArray = reader.TokenType == JsonTokenType.StartArray;
                        pendingOperations = false;
                        nextOperation = 0;
                    }
                    if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals("operations"u8))
                        pendingOperations = true;
                }
                if (reader.TokenType == JsonTokenType.Number && reader.ValueSpan.Length > 19)
                    throw Error("RESOURCE_LIMIT", "JSON number token exceeds its byte limit.");
            }
            checkBudget?.Invoke();
            return JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = maxDepth });
        }
        catch (JsonException)
        {
            throw Error(code, "Invalid JSON syntax or nesting depth.", OperationPath(operationIndex), operationIndex);
        }
        catch (NativePixelException e) when (e.Path is null)
        {
            throw Error(e.Code, e.Message, OperationPath(operationIndex), operationIndex);
        }
    }

    private static void ValidateEscapes(ReadOnlySpan<byte> token, string code)
    {
        for (int i = 0; i < token.Length; i++)
        {
            if (token[i] != '\\') continue;
            i++;
            if (i >= token.Length) throw Error(code, "Invalid JSON string escape.");
            if (token[i] != 'u') continue;
            if (i + 4 >= token.Length) throw Error(code, "Invalid JSON Unicode escape.");
            int scalar = Hex4(token.Slice(i + 1, 4));
            i += 4;
            if (scalar is >= 0xdc00 and <= 0xdfff) throw Error(code, "Isolated JSON surrogate escape.");
            if (scalar is < 0xd800 or > 0xdbff) continue;
            if (i + 6 >= token.Length || token[i + 1] != '\\' || token[i + 2] != 'u'
                || Hex4(token.Slice(i + 3, 4)) is < 0xdc00 or > 0xdfff)
                throw Error(code, "Isolated JSON surrogate escape.");
            i += 6;
        }
    }

    private static int Hex4(ReadOnlySpan<byte> bytes)
    {
        int value = 0;
        foreach (byte b in bytes)
            value = (value << 4) | (b <= '9' ? b - '0' : (b | 32) - 'a' + 10);
        return value;
    }

    internal static void ObjectFields(JsonElement value, string[] required, string[] optional, string path, string code)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error(code, "Expected a JSON object.", path);
        if (JsonMarshal.GetRawUtf8Value(value).Length > MaxJsonBytes)
            throw Error("RESOURCE_LIMIT", "JSON object exceeds its byte limit.", path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            var rawName = JsonMarshal.GetRawUtf8PropertyName(field);
            ValidateStringToken(rawName, path, code);
            string name = field.Name;
            if (!required.Contains(name, StringComparer.Ordinal) && !optional.Contains(name, StringComparer.Ordinal))
                throw Error(code, "Unknown JSON member.", path);
            if (!seen.Add(name)) throw Error(code, "Duplicate JSON member.", path + "." + name);
            if (field.Value.ValueKind == JsonValueKind.Null && name != "maskSha256")
                throw Error(code, "JSON member must not be null.", path + "." + name);
        }
        foreach (string name in required)
            if (!seen.Contains(name)) throw Error(code, "Required JSON member is missing.", path + "." + name);
    }

    internal static string String(JsonElement value, string path, string code)
    {
        if (value.ValueKind != JsonValueKind.String) throw Error(code, "Expected a JSON string.", path);
        ValidateStringToken(JsonMarshal.GetRawUtf8Value(value)[1..^1], path, code);
        return value.GetString()!;
    }

    private static void ValidateStringToken(ReadOnlySpan<byte> token, string path, string code)
    {
        if (token.Length > 4096 * 6) throw Error("RESOURCE_LIMIT", "JSON string token exceeds its byte limit.", path);
        try
        {
            StrictUtf8.GetCharCount(token);
            ValidateEscapes(token, code);
        }
        catch (DecoderFallbackException) { throw Error(code, "JSON contains invalid UTF-8.", path); }
        catch (NativePixelException e) when (e.Path is null) { throw Error(e.Code, e.Message, path); }
    }

    internal static bool Boolean(JsonElement value, string path, string code)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Error(code, "Expected a JSON boolean.", path);
        return value.GetBoolean();
    }

    internal static long Integer(JsonElement value, string path, string code)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Error(code == "INVALID_ARGUMENTS" ? code : "PARSE_ERROR", "Expected a digit-only JSON integer.", path);
        var token = JsonMarshal.GetRawUtf8Value(value);
        if (token.Length is < 1 or > 19 || (token.Length > 1 && token[0] == '0'))
            throw Error(code, "Expected an exact bounded digit-only JSON integer.", path);
        long result = 0;
        foreach (byte digit in token)
        {
            if (digit is < (byte)'0' or > (byte)'9' || result > (long.MaxValue - (digit - '0')) / 10)
                throw Error(code, "Expected an exact bounded digit-only JSON integer.", path);
            result = checked(result * 10 + (digit - '0'));
        }
        return result;
    }

    internal static decimal Opacity(JsonElement value, string path, string code)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Error(code == "INVALID_ARGUMENTS" ? code : "PARSE_ERROR", "Expected an exact JSON opacity decimal.", path);
        var token = JsonMarshal.GetRawUtf8Value(value);
        if (token.Length is < 1 or > 8 || token[0] is not ((byte)'0' or (byte)'1')
            || (token.Length > 1 && (token.Length < 3 || token[1] != '.' || token[2..].ContainsAnyExceptInRange((byte)'0', (byte)'9'))))
            throw Error(code, "Invalid opacity spelling or fractional precision.", path);
        int millionths = (token[0] - '0') * 1_000_000, place = 100_000;
        for (int i = 2; i < token.Length; i++, place /= 10) millionths += (token[i] - '0') * place;
        if (millionths > 1_000_000) throw Error(code, "Opacity must be between zero and one.", path);
        return millionths / 1_000_000m;
    }

    internal static Guid GuidValue(JsonElement value, string path, string code)
    {
        string text = String(value, path, code == "INVALID_ID" ? "PARSE_ERROR" : code);
        if (text.Length != 36 || !Guid.TryParseExact(text, "D", out Guid id) || id == Guid.Empty
            || !string.Equals(text, id.ToString("D"), StringComparison.Ordinal))
            throw Error(code, "Expected a nonempty lowercase D-form GUID.", path);
        return id;
    }

    private static string OperationPath(int? index) => index is null ? "$" : "$.operations[" + index.Value.ToString(CultureInfo.InvariantCulture) + "]";

    internal static NativePixelException Error(string code, string message, string? path = null, int? operationIndex = null)
        => new(code, message, path, operationIndex);
}
