using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Artifacts;

/// <summary>
/// Pure provided-artifact v1 metadata parsing, validation and identity. No file access or decoding.
/// PARSE_ERROR denotes JSON/shape/lexical errors; INVALID_ARGUMENTS denotes invalid values or graph
/// relationships; UNSUPPORTED_VERSION and ARTIFACT_PROFILE_UNSUPPORTED denote unsupported contracts;
/// RESOURCE_LIMIT denotes byte/token/depth/count/aggregate limits. Diagnostics never echo input values.
/// </summary>
public static class ArtifactDescriptorContract
{
    public const string Schema = "agd.provided-artifact";
    public const string Profile = "png8-rgba-srgb-straight-fixed-canvas-v1";
    public const string Coordinates = "top-left-pixel-edges-x-right-y-down";
    public const string ProfileUnsupported = "ARTIFACT_PROFILE_UNSUPPORTED";
    public const string PngInvalid = "ARTIFACT_PNG_INVALID";
    public const int MaxMetadataBytes = 1_048_576;
    private const long MaxDurationTicks = 9_007_199_254_740_991;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8,
        Converters = { new IntegerConverter(), new PivotConverter() }
    };

    /// <summary>
    /// Copies the input before multi-pass reads. Success means all metadata invariants hold,
    /// not that the claimed PNG payloads exist or have been verified.
    /// </summary>
    public static ArtifactDescriptorParseResult Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length > MaxMetadataBytes)
            return new(null, Error(EngineErrors.ResourceLimit, "Metadata input exceeds 1 MiB.", "$"));

        var owned = utf8Json.ToArray();
        try
        {
            ValidateTokens(owned);
            DocumentSerializer.ValidateNoDuplicateProperties(owned);
            var descriptor = JsonSerializer.Deserialize<ProvidedArtifactDescriptor>(owned, ReadOptions);
            ValidateAndSerialize(descriptor);
            return new(descriptor, null);
        }
        catch (ContractViolation ex)
        {
            return new(null, ex.Error);
        }
        catch (JsonException)
        {
            return new(null, Error(EngineErrors.ParseError, "Invalid JSON syntax, closed object shape or duplicate property.", "$"));
        }
        catch (DecoderFallbackException)
        {
            return new(null, Error(EngineErrors.ParseError, "Metadata must contain valid UTF-8 Unicode.", "$"));
        }
    }

    /// <summary>Returns the first metadata error, or null, including the canonical 1 MiB check.</summary>
    public static ValidationError? Validate(ProvidedArtifactDescriptor descriptor)
    {
        try
        {
            ValidateAndSerialize(descriptor);
            return null;
        }
        catch (ContractViolation ex)
        {
            return ex.Error;
        }
    }

    /// <summary>
    /// Emits compact schema-specific canonical metadata, not RFC 8785/JCS or verified payload identity.
    /// Throws <see cref="DocumentEngineException"/> for invalid models, including oversized output.
    /// </summary>
    public static byte[] SerializeCanonical(ProvidedArtifactDescriptor descriptor)
    {
        try
        {
            return ValidateAndSerialize(descriptor);
        }
        catch (ContractViolation ex)
        {
            throw new DocumentEngineException(ex.Error.Code, ex.Error.Message);
        }
    }

    /// <summary>
    /// Lowercase SHA-256 of ASCII("agd.provided-artifact.canonical-metadata.v1\n") plus canonical bytes.
    /// Identifies unverified metadata claims only. Invalid models throw DocumentEngineException.
    /// </summary>
    public static string ComputeMetadataDigest(ProvidedArtifactDescriptor descriptor)
    {
        var bytes = SerializeCanonical(descriptor);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("agd.provided-artifact.canonical-metadata.v1\n"u8);
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void ValidateTokens(ReadOnlySpan<byte> bytes)
    {
        Require(!bytes.StartsWith("\uFEFF"u8), "A UTF-8 BOM is not permitted.", "$", EngineErrors.ParseError);
        StrictUtf8.GetCharCount(bytes);
        // Inspect bounds before duplicate detection or typed deserialization allocates decoded strings.
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 9 });
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                Require(reader.CurrentDepth < 8, "JSON exceeds eight containers of depth.", "$", EngineErrors.ResourceLimit);
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
            {
                Require(reader.ValueSpan.Length <= 1536, "Raw string token exceeds 1536 bytes.", "$", EngineErrors.ResourceLimit);
                // GetString rejects malformed escaped surrogate pairs; raw UTF-8 was checked above.
                try
                {
                    _ = reader.GetString();
                }
                catch (InvalidOperationException)
                {
                    throw new ContractViolation(Error(EngineErrors.ParseError, "Invalid Unicode string escape.", "$"));
                }
            }
            if (reader.TokenType == JsonTokenType.Number)
                Require(reader.ValueSpan.Length <= 32, "Raw numeric token exceeds 32 bytes.", "$", EngineErrors.ResourceLimit);
            Require(reader.TokenType != JsonTokenType.Null, "Explicit null is not permitted.", "$", EngineErrors.ParseError);
        }
    }

    private static byte[] ValidateAndSerialize(ProvidedArtifactDescriptor? model)
    {
        if (model is null)
            throw new ContractViolation(Error(EngineErrors.InvalidArguments, "Descriptor is required.", "$"));
        Require(model.Schema == Schema, "Invalid descriptor schema.", "$.schema");
        Require(model.Version == 1, "Only descriptor version 1 is supported.", "$.version", EngineErrors.UnsupportedVersion);
        Require(model.Profile == Profile, "Unsupported artifact metadata profile.", "$.profile", ProfileUnsupported);
        Require(IsId(model.AssetId), "Invalid logical identifier.", "$.assetId");
        Require(model.Canvas is not null, "Canvas is required.", "$.canvas");
        var canvas = model.Canvas!;
        Require(canvas.Width is >= 1 and <= 8192, "Width must be in [1, 8192].", "$.canvas.width");
        Require(canvas.Height is >= 1 and <= 8192, "Height must be in [1, 8192].", "$.canvas.height");
        var area = checked(canvas.Width * canvas.Height);
        Require(area <= 16_777_216, "Canvas area exceeds 16777216 pixels.", "$.canvas", EngineErrors.ResourceLimit);
        Require(canvas.Coordinates == Coordinates, "Unsupported canvas coordinates.", "$.canvas.coordinates");
        Require(model.CropOrigin is { X: 0, Y: 0 }, "Crop origin must contain integer zero coordinates.", "$.cropOrigin");
        Require(!model.Files.IsDefaultOrEmpty && model.Files.Length <= 4096,
            "File count must be in [1, 4096].", "$.files", EngineErrors.ResourceLimit);
        Require(checked(model.Files.Length * area) <= 67_108_864,
            "Unique-file pixel work exceeds 67108864.", "$.files", EngineErrors.ResourceLimit);
        Require((model.Still is null) != (model.Clip is null), "Exactly one of still or clip is required.", "$");

        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        for (var i = 0; i < model.Files.Length; i++)
        {
            var file = model.Files[i];
            var path = "$.files[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            Require(file is not null, "File claim is required.", path);
            Require(IsHash(file!.Sha256), "Invalid lowercase SHA-256 claim.", path + ".sha256");
            Require(hashes.Add(file.Sha256), "Duplicate file hash claim.", path + ".sha256");
            Require(IsPortablePath(file.Path), "Invalid portable PNG path.", path + ".path");
            Require(paths.Add(file.Path), "Duplicate file path claim.", path + ".path");
            Require(file.ByteLength is >= 1 and <= 67_108_864, "Byte length must be in [1, 67108864].", path + ".byteLength");
            totalBytes = checked(totalBytes + file.ByteLength);
            Require(totalBytes <= 268_435_456, "Declared byte sum exceeds 268435456.", "$.files", EngineErrors.ResourceLimit);
        }
        // Check every directory prefix against the complete inventory, independent of input order.
        foreach (var path in paths)
            for (var i = 0; i < path.Length; i++)
                if (path[i] == '/')
                    Require(!paths.Contains(path[..i]), "File/directory path prefix conflict.", "$.files");

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        if (model.Still is { } still)
        {
            Require(model.Files.Length == 1, "A still requires exactly one file.", "$.files");
            Require(IsId(still.Name), "Invalid still name.", "$.still.name");
            Require(IsHash(still.FileSha256) && hashes.Contains(still.FileSha256), "Missing or invalid file reference.", "$.still.fileSha256");
            referenced.Add(still.FileSha256);
            ValidatePivot(still.Pivot, "$.still.pivot");
        }
        else if (model.Clip is { } clip)
        {
            Require(IsId(clip.Id), "Invalid clip identifier.", "$.clip.id");
            Require(IsId(clip.ViewId), "Invalid view identifier.", "$.clip.viewId");
            Require(IsId(clip.VariantId), "Invalid variant identifier.", "$.clip.variantId");
            Require(clip.TicksPerSecond is >= 1 and <= 1_000_000_000, "Tick rate must be in [1, 1000000000].", "$.clip.ticksPerSecond");
            Require(clip.Playback is "forward-loop" or "forward-once-hold", "Unsupported playback mode.", "$.clip.playback");
            Require(!clip.Frames.IsDefaultOrEmpty && clip.Frames.Length <= 4096,
                "Frame count must be in [1, 4096].", "$.clip.frames", EngineErrors.ResourceLimit);
            Require(checked(clip.Frames.Length * area) <= 268_435_456,
                "Logical-frame pixel work exceeds 268435456.", "$.clip.frames", EngineErrors.ResourceLimit);
            var names = new HashSet<string>(StringComparer.Ordinal);
            long totalTicks = 0;
            for (var i = 0; i < clip.Frames.Length; i++)
            {
                var frame = clip.Frames[i];
                var path = "$.clip.frames[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                Require(frame is not null, "Frame is required.", path);
                Require(IsId(frame!.Name), "Invalid frame name.", path + ".name");
                Require(names.Add(frame.Name), "Duplicate logical frame name.", path + ".name");
                Require(IsHash(frame.FileSha256) && hashes.Contains(frame.FileSha256), "Missing or invalid file reference.", path + ".fileSha256");
                referenced.Add(frame.FileSha256);
                Require(frame.DurationTicks is >= 1 and <= MaxDurationTicks,
                    "Duration must be in [1, 9007199254740991].", path + ".durationTicks");
                totalTicks = checked(totalTicks + frame.DurationTicks);
                Require(totalTicks <= MaxDurationTicks, "Duration prefix sum exceeds 9007199254740991.", "$.clip.frames", EngineErrors.ResourceLimit);
                ValidatePivot(frame.Pivot, path + ".pivot");
            }
        }
        Require(referenced.Count == hashes.Count, "Every file must be referenced.", "$.files");
        if (model.Provenance is { } provenance)
        {
            Require(provenance.Producer is not null || provenance.RecipeSnapshotSha256 is not null,
                "Provenance must contain at least one claim.", "$.provenance");
            if (provenance.Producer is { } producer)
            {
                Require(IsId(producer.Id), "Invalid producer identifier.", "$.provenance.producer.id");
                Require(IsProducerVersion(producer.Version), "Invalid producer version.", "$.provenance.producer.version");
            }
            if (provenance.RecipeSnapshotSha256 is { } recipe)
                Require(IsHash(recipe), "Invalid lowercase recipe snapshot SHA-256 claim.", "$.provenance.recipeSnapshotSha256");
        }

        return WriteCanonical(model);
    }

    private static void ValidatePivot(ArtifactPivot? pivot, string path)
    {
        Require(pivot is not null, "Pivot is required.", path);
        Require(pivot!.X is >= -1_000_000m and <= 1_000_000m && pivot.X % 0.000001m == 0,
            "Pivot must be in [-1000000, 1000000] with at most six fractional digits.", path + ".x");
        Require(pivot.Y is >= -1_000_000m and <= 1_000_000m && pivot.Y % 0.000001m == 0,
            "Pivot must be in [-1000000, 1000000] with at most six fractional digits.", path + ".y");
    }

    private static bool IsId(string? value) => value is { Length: >= 1 and <= 64 }
        && value[0] is >= 'a' and <= 'z'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');

    private static bool IsHash(string? value) => value is { Length: 64 }
        && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');

    private static bool IsProducerVersion(string? value) => value is { Length: >= 1 and <= 64 }
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '_' or '-');

    private static bool IsPortablePath(string? value)
    {
        if (value is not { Length: >= 1 and <= 240 } || !value.EndsWith(".png", StringComparison.Ordinal))
            return false;
        var segments = value.Split('/');
        if (segments.Length > 8)
            return false;
        foreach (var segment in segments)
        {
            if (segment.Length is < 1 or > 64 || segment[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9')
                || segment[^1] == '.' || !segment.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'))
                return false;
            var dot = segment.IndexOf('.');
            var stem = dot < 0 ? segment : segment[..dot];
            if (stem is "con" or "prn" or "aux" or "nul"
                || (stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal))
                    && stem[3] is >= '1' and <= '9'))
                return false;
        }
        return true;
    }

    private static byte[] WriteCanonical(ProvidedArtifactDescriptor model)
    {
        var buffer = new ArrayBufferWriter<byte>();
        // Validation restricts every string to safe ASCII; '+' and '/' must remain literal.
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("schema", model.Schema);
        writer.WriteNumber("version", model.Version);
        writer.WriteString("profile", model.Profile);
        writer.WriteString("assetId", model.AssetId);
        writer.WriteStartObject("canvas");
        writer.WriteNumber("width", model.Canvas.Width);
        writer.WriteNumber("height", model.Canvas.Height);
        writer.WriteString("coordinates", model.Canvas.Coordinates);
        writer.WriteEndObject();
        writer.WriteStartObject("cropOrigin");
        writer.WriteNumber("x", model.CropOrigin.X);
        writer.WriteNumber("y", model.CropOrigin.Y);
        writer.WriteEndObject();
        writer.WriteStartArray("files");
        foreach (var file in model.Files.OrderBy(file => file.Sha256, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("sha256", file.Sha256);
            writer.WriteString("path", file.Path);
            writer.WriteNumber("byteLength", file.ByteLength);
            writer.WriteEndObject();
            CheckCanonicalSize(writer);
        }
        writer.WriteEndArray();
        if (model.Still is { } still)
        {
            writer.WriteStartObject("still");
            writer.WriteString("name", still.Name);
            writer.WriteString("fileSha256", still.FileSha256);
            WritePivot(writer, still.Pivot);
            writer.WriteEndObject();
        }
        else if (model.Clip is { } clip)
        {
            writer.WriteStartObject("clip");
            writer.WriteString("id", clip.Id);
            writer.WriteString("viewId", clip.ViewId);
            writer.WriteString("variantId", clip.VariantId);
            writer.WriteNumber("ticksPerSecond", clip.TicksPerSecond);
            writer.WriteString("playback", clip.Playback);
            writer.WriteStartArray("frames");
            foreach (var frame in clip.Frames)
            {
                writer.WriteStartObject();
                writer.WriteString("name", frame.Name);
                writer.WriteString("fileSha256", frame.FileSha256);
                writer.WriteNumber("durationTicks", frame.DurationTicks);
                WritePivot(writer, frame.Pivot);
                writer.WriteEndObject();
                CheckCanonicalSize(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        if (model.Provenance is { } provenance)
        {
            writer.WriteStartObject("provenance");
            if (provenance.Producer is { } producer)
            {
                writer.WriteStartObject("producer");
                writer.WriteString("id", producer.Id);
                writer.WriteString("version", producer.Version);
                writer.WriteEndObject();
            }
            if (provenance.RecipeSnapshotSha256 is { } recipe)
                writer.WriteString("recipeSnapshotSha256", recipe);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        CheckCanonicalSize(writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WritePivot(Utf8JsonWriter writer, ArtifactPivot pivot)
    {
        writer.WriteStartObject("pivot");
        writer.WritePropertyName("x");
        writer.WriteRawValue(pivot.X.ToString("0.######", CultureInfo.InvariantCulture), skipInputValidation: true);
        writer.WritePropertyName("y");
        writer.WriteRawValue(pivot.Y.ToString("0.######", CultureInfo.InvariantCulture), skipInputValidation: true);
        writer.WriteEndObject();
    }

    private static void CheckCanonicalSize(Utf8JsonWriter writer) => Require(
        writer.BytesCommitted + writer.BytesPending <= MaxMetadataBytes,
        "Canonical metadata output exceeds 1 MiB.", "$", EngineErrors.ResourceLimit);

    private static void Require(bool condition, string message, string path, string code = EngineErrors.InvalidArguments)
    {
        if (!condition)
            throw new ContractViolation(Error(code, message, path));
    }

    private static ValidationError Error(string code, string message, string path) =>
        new(code, Bound(message, 1024), Bound(path, 512));

    private static string Bound(string value, int limit) => value.Length <= limit ? value : value[..(limit - 11)] + "[truncated]";

    private sealed class ContractViolation(ValidationError error) : Exception(error.Message)
    {
        public ValidationError Error { get; } = error;
    }

    private sealed class IntegerConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Require(reader.TokenType == JsonTokenType.Number, "Integer token required.", "$", EngineErrors.ParseError);
            foreach (var digit in reader.ValueSpan)
                Require(digit is >= (byte)'0' and <= (byte)'9', "Integer tokens must contain only digits.", "$", EngineErrors.ParseError);
            Require(reader.TryGetInt64(out var value), "Integer value is outside the supported range.", "$");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => throw new NotSupportedException();
    }

    private sealed class PivotConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Require(reader.TokenType == JsonTokenType.Number, "Decimal pivot token required.", "$", EngineErrors.ParseError);
            var raw = reader.ValueSpan;
            var fractionalDigits = -1;
            for (var i = 0; i < raw.Length; i++)
            {
                if (i == 0 && raw[i] == '-')
                    continue;
                if (raw[i] == '.')
                    fractionalDigits = 0;
                else
                {
                    Require(raw[i] is >= (byte)'0' and <= (byte)'9', "Pivot exponents are not permitted.", "$", EngineErrors.ParseError);
                    if (fractionalDigits >= 0)
                        fractionalDigits++;
                }
            }
            Require(fractionalDigits <= 6, "Pivot tokens allow at most six fractional digits.", "$", EngineErrors.ParseError);
            // Precision is checked before conversion; no rounding can turn an invalid claim into a valid one.
            Require(reader.TryGetDecimal(out var value), "Pivot value is outside the supported range.", "$");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
}
