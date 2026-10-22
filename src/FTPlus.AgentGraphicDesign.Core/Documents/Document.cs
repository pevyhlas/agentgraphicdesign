using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Documents;

public sealed class Document
{
    public const int SupportedVersion = 1;
    public const string SchemaUrl = "https://raw.githubusercontent.com/pevyhlas/agentgraphicdesign/v0.1.0/schemas/agd-document.schema.json";

    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("version")]
    [JsonRequired]
    public int Version { get; set; } = 1;

    [JsonPropertyName("id")]
    [JsonRequired]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("revn")]
    [JsonRequired]
    public long Revn { get; set; } = 1;

    [JsonPropertyName("canvas")]
    [JsonRequired]
    public Canvas Canvas { get; set; } = new();

    [JsonPropertyName("root")]
    [JsonRequired]
    public FrameNode Root { get; set; } = new();

    [JsonPropertyName("tokens")]
    public DesignTokens? Tokens { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class Canvas
{
    [JsonPropertyName("width")]
    [JsonRequired]
    public double Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    [JsonRequired]
    public double Height { get; set; } = 1080;

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class DesignTokens
{
    [JsonPropertyName("colors")]
    public Dictionary<string, string>? Colors { get; set; }

    [JsonPropertyName("typography")]
    public Dictionary<string, TypographyToken>? Typography { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TypographyToken
{
    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; set; }

    [JsonPropertyName("fontSize")]
    public double? FontSize { get; set; }

    [JsonPropertyName("fontWeight")]
    public int? FontWeight { get; set; }

    [JsonPropertyName("lineHeight")]
    public double? LineHeight { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
