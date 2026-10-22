using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Patching;

/// <summary>
/// Parses an operations JSON array into <see cref="PatchOperation"/> records with the
/// document serializer's conventions (camelCase, strict numbers, no duplicate properties)
/// and the <see cref="PatchLimits"/> bounds. Every rejection names the operation index.
/// </summary>
public static class PatchOperationParser
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = PatchLimits.MaxJsonDepth
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            AllowOutOfOrderMetadataProperties = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = PatchLimits.MaxJsonDepth
        };

        options.Converters.Add(new SortedDictionaryConverterFactory());
        options.Converters.Add(new FixedPrecisionDoubleConverter());
        return options;
    }

    /// <summary>Parse an operations array from JSON text.</summary>
    public static PatchParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(EngineErrors.InvalidArguments, "Operations must be a non-empty JSON array");
        }

        if (Encoding.UTF8.GetByteCount(json) > PatchLimits.MaxJsonBytes)
        {
            return Fail(EngineErrors.ResourceLimit, $"Operations exceed the {PatchLimits.MaxJsonBytes} byte limit");
        }

        JsonNode? root;
        try
        {
            DocumentSerializer.ValidateNoDuplicateProperties(json);
            root = JsonNode.Parse(json, nodeOptions: null, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return Fail(EngineErrors.InvalidArguments, $"Operations are not valid JSON: {ex.Message}");
        }

        if (root is not JsonArray array)
        {
            return Fail(EngineErrors.InvalidArguments, "Operations must be a JSON array of operation objects");
        }

        if (array.Count == 0)
        {
            return Fail(EngineErrors.InvalidArguments, "Patch contains no operations");
        }

        if (array.Count > PatchLimits.MaxOperations)
        {
            return Fail(EngineErrors.ResourceLimit,
                $"Patch contains {array.Count} operations; the maximum is {PatchLimits.MaxOperations}");
        }

        var operations = new List<PatchOperation>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject item)
            {
                return Fail(EngineErrors.InvalidArguments, "Operation must be a JSON object", index);
            }

            if (!item.TryGetPropertyValue("op", out var opNode) || opNode is null)
            {
                return Fail(EngineErrors.InvalidArguments,
                    $"Operation has no 'op' discriminator. Supported: {string.Join(", ", PatchOperation.OpNames)}", index);
            }

            if (opNode is not JsonValue opValue || !opValue.TryGetValue<string>(out var op))
            {
                return Fail(EngineErrors.InvalidArguments, "Operation 'op' must be a string", index);
            }

            if (Array.IndexOf(PatchOperation.OpNames, op) < 0)
            {
                return Fail(EngineErrors.InvalidArguments,
                    $"Unknown op '{op}'. Supported: {string.Join(", ", PatchOperation.OpNames)}", index);
            }

            try
            {
                var operation = item.Deserialize<PatchOperation>(Options)
                    ?? throw new JsonException("Operation deserialized to null");
                operations.Add(operation);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                return Fail(EngineErrors.InvalidArguments, $"Operation '{op}' is malformed: {ex.Message}", index);
            }
        }

        return PatchParseResult.Ok(operations);
    }

    private static PatchParseResult Fail(string code, string message, int? index = null) =>
        PatchParseResult.Fail(new PatchError(code, message, index));
}
