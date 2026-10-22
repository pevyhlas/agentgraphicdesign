using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Tokens;

/// <summary>
/// W3C Design Tokens Community Group (DTCG) token format.
/// See: https://tr.designtokens.org/format/
/// </summary>
public sealed class DtcgTokenFile
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Groups { get; set; } = [];

    public static DtcgTokenFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > TokenService.MaxDtcgInputCharacters)
            throw new JsonException($"DTCG JSON exceeds maximum length ({TokenService.MaxDtcgInputCharacters} characters)");
        DocumentSerializer.ValidateNoDuplicateProperties(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = TokenService.MaxDtcgDepth
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("DTCG root must be a JSON object");
        return document.RootElement.Deserialize<DtcgTokenFile>()
            ?? throw new JsonException("DTCG root must not be null");
    }
}

/// <summary>
/// A single DTCG token with $value, $type, and optional metadata.
/// </summary>
public sealed class DtcgToken
{
    [JsonPropertyName("$value")]
    public JsonElement Value { get; set; }

    [JsonPropertyName("$type")]
    public string? Type { get; set; }

    [JsonPropertyName("$description")]
    public string? Description { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// DTCG token types as defined in the spec.
/// </summary>
public static class DtcgTokenTypes
{
    public const string Color = "color";
    public const string Dimension = "dimension";
    public const string FontFamily = "fontFamily";
    public const string FontWeight = "fontWeight";
    public const string FontStyle = "fontStyle";
    public const string Duration = "duration";
    public const string CubicBezier = "cubicBezier";
    public const string Number = "number";
    public const string StrokeStyle = "strokeStyle";
    public const string Border = "border";
    public const string Transition = "transition";
    public const string Shadow = "shadow";
    public const string Gradient = "gradient";
    public const string Typography = "typography";
}

/// <summary>
/// DTCG typography composite token value.
/// </summary>
public sealed class DtcgTypographyValue
{
    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; set; }

    [JsonPropertyName("fontSize")]
    public string? FontSize { get; set; }

    [JsonPropertyName("fontWeight")]
    public JsonElement? FontWeight { get; set; }

    [JsonPropertyName("letterSpacing")]
    public string? LetterSpacing { get; set; }

    [JsonPropertyName("lineHeight")]
    public JsonElement? LineHeight { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Result of importing DTCG tokens into a document.
/// </summary>
public sealed record TokenImportResult(
    bool Success,
    int ColorCount,
    int TypographyCount,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Result of exporting tokens to DTCG format.
/// </summary>
public sealed record TokenExportResult(
    bool Success,
    string? Content,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Detailed result for token lookups used by output surfaces. Renderers may use
/// a safe local fallback while callers can inspect missing/cycle diagnostics.
/// </summary>
public sealed record TokenResolutionResult<T>(
    bool Success,
    T? Value,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static TokenResolutionResult<T> Ok(T? value) => new(true, value);

    public static TokenResolutionResult<T> Fail(string code, string message) =>
        new(false, default, code, message);
}

/// <summary>
/// Result of applying a bundled palette.
/// </summary>
public sealed record PaletteApplyResult(
    bool Success,
    string PaletteName,
    int ColorCount,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Info about an available bundled palette.
/// </summary>
public sealed record BundledPaletteInfo(
    string Name,
    string Description,
    string License,
    int ColorCount
);
