using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Serialization;

/// <summary>Structured result from deserialization with parse error details.</summary>
public sealed class DeserializeResult
{
    private DeserializeResult(Document? document, ParseError? error)
    {
        Document = document;
        Error = error;
    }

    public Document? Document { get; }
    public ParseError? Error { get; }
    public bool Success => Document is not null && Error is null;

    public static DeserializeResult Ok(Document document) => new(document, null);
    public static DeserializeResult Fail(ParseError error) => new(null, error);
}

/// <summary>Parse error details for agent-friendly error reporting.</summary>
public sealed class ParseError
{
    public required string Message { get; init; }
    public string? Path { get; init; }
    public long? LineNumber { get; init; }
    public long? BytePosition { get; init; }
}

public static class DocumentSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly HashSet<string> KnownNodeTypes =
    [
        "frame", "rectangle", "ellipse", "path", "text", "group", "line", "polygon", "image", "unknown"
    ];
    private static readonly Dictionary<Type, string> NodeDiscriminators = typeof(Node)
        .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
        .Cast<JsonDerivedTypeAttribute>()
        .ToDictionary(attribute => attribute.DerivedType, attribute => (string)attribute.TypeDiscriminator!);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            // Full files and patch subtrees accept the same JSON property ordering.
            // Raw-input duplicate and depth checks still run before model construction.
            AllowOutOfOrderMetadataProperties = true
        };

        options.Converters.Add(new SortedDictionaryConverterFactory());
        options.Converters.Add(new FixedPrecisionDoubleConverter());

        return options;
    }

    public static string Serialize(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentLimits.ThrowIfPersistedStringsExceedLimits(document);
        var json = JsonSerializer.Serialize(document, Options);
        var root = ParseObject(JsonNode.Parse(json));

        var rootNode = root["root"]?.AsObject()
            ?? throw new JsonException("Document root cannot be null", "$.root", null, null);
        DenormalizeNode(rootNode, document.Root, "frame");
        return root.ToJsonString(Options);
    }

    /// <summary>Canonical JSON for one node and its descendants, preserving native discriminators.</summary>
    public static string SerializeSubtree(Node node) => SerializeNode(node).ToJsonString(Options);

    public static Document? Deserialize(string json)
    {
        return DeserializeCore(json);
    }

    public static Document? Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        utf8Json = RemoveUtf8Bom(utf8Json);
        ValidateNoDuplicateProperties(utf8Json);
        var root = ParseObject(JsonNode.Parse(utf8Json));
        return DeserializeCore(root);
    }

    /// <summary>
    /// Deserialize with structured error handling. Returns <see cref="DeserializeResult"/>
    /// with either a document or detailed parse error info for agent callers.
    /// </summary>
    public static DeserializeResult TryDeserialize(string json)
    {
        if (json is null)
        {
            return DeserializeResult.Fail(new ParseError { Message = "Document JSON cannot be null", Path = "$" });
        }

        try
        {
            return DeserializeResult.Ok(DeserializeCore(json));
        }
        catch (JsonException ex)
        {
            return DeserializeResult.Fail(new ParseError
            {
                Message = ex.Message,
                Path = ex.Path,
                LineNumber = ex.LineNumber,
                BytePosition = ex.BytePositionInLine
            });
        }
        catch (ArgumentException ex)
        {
            return DeserializeResult.Fail(new ParseError { Message = ex.Message });
        }
    }

    public static DeserializeResult TryDeserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return DeserializeResult.Ok(Deserialize(utf8Json)!);
        }
        catch (JsonException ex)
        {
            return DeserializeResult.Fail(new ParseError
            {
                Message = ex.Message,
                Path = ex.Path,
                LineNumber = ex.LineNumber,
                BytePosition = ex.BytePositionInLine
            });
        }
        catch (ArgumentException ex)
        {
            return DeserializeResult.Fail(new ParseError { Message = ex.Message });
        }
    }

    /// <summary>
    /// Serialize one node and its subtree to a JSON object carrying the persisted
    /// discriminators (including a preserved unknown-node discriminator).
    /// </summary>
    internal static JsonObject SerializeNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        // Serialize by concrete type so no discriminator is emitted, then set it explicitly:
        // a root loaded through the FrameNode-typed Document.Root keeps its "type" in
        // extension data, and a polymorphic write would emit that key twice.
        var json = JsonSerializer.SerializeToNode(node, node.GetType(), Options) as JsonObject
            ?? throw new JsonException("Node serialized to a non-object value");
        json.Remove("type");
        var discriminator = NodeDiscriminators.GetValueOrDefault(node.GetType())
            ?? throw new JsonException($"Node type {node.GetType().Name} has no registered discriminator");
        json.Insert(0, "type", discriminator);
        DenormalizeNode(json, node);
        return json;
    }

    /// <summary>
    /// Validate and deserialize one node JSON object and its subtree through the polymorphic
    /// node serializer. The <c>type</c> discriminator may appear in any position.
    /// </summary>
    internal static Node DeserializeNode(JsonObject node, string path, bool requireFrame)
    {
        ArgumentNullException.ThrowIfNull(node);
        ValidateNode(node, path, requireFrame);
        var unknownDiscriminators = new Dictionary<string, string>(StringComparer.Ordinal);
        NormalizeUnknownNodes(node, path, unknownDiscriminators);

        var model = node.Deserialize<Node>(Options)
            ?? throw new JsonException("Deserialized node was null", path, null, null);
        ApplyUnknownDiscriminators(model, path, unknownDiscriminators);
        return model;
    }

    private static Document DeserializeCore(string json)
    {
        var utf8Json = Encoding.UTF8.GetBytes(json);
        ValidateNoDuplicateProperties(utf8Json);
        return DeserializeCore(ParseObject(JsonNode.Parse(utf8Json)));
    }

    private static Document DeserializeCore(JsonObject root)
    {
        ValidateStructure(root);
        var unknownDiscriminators = new Dictionary<string, string>(StringComparer.Ordinal);
        NormalizeUnknownNodes(root["root"]!.AsObject(), "$.root", unknownDiscriminators);

        var document = root.Deserialize<Document>(Options)
            ?? throw new JsonException("Deserialized document was null", "$", null, null);
        ApplyUnknownDiscriminators(document.Root, "$.root", unknownDiscriminators);
        return document;
    }

    private static void ValidateStructure(JsonObject document)
    {
        RequireValue(document, "version", "$", allowNull: false);
        RequireGuid(document, "id", "$");
        RequireString(document, "name", "$", allowBlank: true);
        RequireValue(document, "revn", "$", allowNull: false);

        var canvas = RequireObject(document, "canvas", "$");
        RequireValue(canvas, "width", "$.canvas", allowNull: false);
        RequireValue(canvas, "height", "$.canvas", allowNull: false);

        var root = RequireObject(document, "root", "$");
        ValidateNode(root, "$.root", requireFrame: true);
    }

    private static void ValidateNode(JsonObject node, string path, bool requireFrame = false)
    {
        var type = RequireString(node, "type", path, allowBlank: false);
        if (requireFrame && type != "frame")
        {
            throw new JsonException("Document root type must be 'frame'", $"{path}.type", null, null);
        }

        RequireGuid(node, "id", path);

        if (type is "frame" or "group")
        {
            var children = RequireArray(node, "children", path);
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not JsonObject child)
                {
                    throw new JsonException("Node children must be objects", $"{path}.children[{i}]", null, null);
                }

                ValidateNode(child, $"{path}.children[{i}]");
            }
        }

        if (type == "polygon" && node.TryGetPropertyValue("points", out var points) && points is null)
        {
            throw new JsonException("Polygon points cannot be null", $"{path}.points", null, null);
        }
    }

    private static void NormalizeUnknownNodes(
        JsonObject node,
        string path,
        Dictionary<string, string> unknownDiscriminators)
    {
        var type = node["type"]!.GetValue<string>();
        if (!KnownNodeTypes.Contains(type))
        {
            unknownDiscriminators.Add(path, type);
            node["type"] = "unknown";
            return;
        }

        if (type is not ("frame" or "group"))
        {
            return;
        }

        var children = node["children"]!.AsArray();
        for (var i = 0; i < children.Count; i++)
        {
            NormalizeUnknownNodes(children[i]!.AsObject(), $"{path}.children[{i}]", unknownDiscriminators);
        }
    }

    private static void ApplyUnknownDiscriminators(
        Node node,
        string path,
        IReadOnlyDictionary<string, string> unknownDiscriminators)
    {
        if (node is UnknownNode unknown && unknownDiscriminators.TryGetValue(path, out var discriminator))
        {
            unknown.SourceDiscriminator = discriminator;
            return;
        }

        if (node is IContainerNode container)
        {
            for (var i = 0; i < container.Children.Count; i++)
                ApplyUnknownDiscriminators(container.Children[i], $"{path}.children[{i}]", unknownDiscriminators);
        }
    }

    private static void DenormalizeNode(JsonObject node, Node model, string? forcedType = null)
    {
        var type = model is UnknownNode { SourceDiscriminator: { } discriminator }
            ? discriminator
            : forcedType ?? node["type"]?.GetValue<string>();
        if (type is null)
        {
            throw new JsonException("Serialized node has no type discriminator");
        }

        node["type"] = type;
        if (model is not IContainerNode container || node["children"] is not JsonArray children)
        {
            return;
        }

        if (children.Count != container.Children.Count)
            throw new JsonException("Serialized container child count does not match the document model");

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not JsonObject childObject)
                throw new JsonException("Serialized node child must be an object");
            DenormalizeNode(childObject, container.Children[i]);
        }
    }

    internal static void ValidateNoDuplicateProperties(string json) =>
        ValidateNoDuplicateProperties(Encoding.UTF8.GetBytes(json));

    internal static void ValidateNoDuplicateProperties(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64
        });
        var objectProperties = new Stack<HashSet<string>>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                objectProperties.Pop();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var property = reader.GetString()!;
                if (!objectProperties.Peek().Add(property))
                {
                    throw new JsonException(
                        $"Duplicate JSON property '{property}' is not allowed",
                        path: null,
                        lineNumber: null,
                        bytePositionInLine: reader.TokenStartIndex);
                }
            }
        }
    }

    private static ReadOnlySpan<byte> RemoveUtf8Bom(ReadOnlySpan<byte> utf8Json) =>
        utf8Json.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? utf8Json[3..] : utf8Json;

    private static JsonNode RequireValue(JsonObject parent, string property, string path, bool allowNull)
    {
        if (!parent.TryGetPropertyValue(property, out var value))
        {
            throw new JsonException($"Missing required property '{property}'", $"{path}.{property}", null, null);
        }

        if (!allowNull && value is null)
        {
            throw new JsonException($"Property '{property}' cannot be null", $"{path}.{property}", null, null);
        }

        return value!;
    }

    private static JsonObject RequireObject(JsonObject parent, string property, string path)
    {
        var value = RequireValue(parent, property, path, allowNull: false);
        return value as JsonObject
            ?? throw new JsonException($"Property '{property}' must be an object", $"{path}.{property}", null, null);
    }

    private static JsonArray RequireArray(JsonObject parent, string property, string path)
    {
        var value = RequireValue(parent, property, path, allowNull: false);
        return value as JsonArray
            ?? throw new JsonException($"Property '{property}' must be an array", $"{path}.{property}", null, null);
    }

    private static string RequireString(JsonObject parent, string property, string path, bool allowBlank)
    {
        var value = RequireValue(parent, property, path, allowNull: false);
        if (value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text) ||
            (!allowBlank && string.IsNullOrWhiteSpace(text)))
        {
            var expectation = allowBlank ? "a string" : "a nonblank string";
            throw new JsonException($"Property '{property}' must be {expectation}", $"{path}.{property}", null, null);
        }

        return text;
    }

    private static void RequireGuid(JsonObject parent, string property, string path)
    {
        var value = RequireValue(parent, property, path, allowNull: false);
        if (value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text) ||
            !Guid.TryParse(text, out _))
        {
            throw new JsonException($"Property '{property}' must be a UUID", $"{path}.{property}", null, null);
        }
    }

    private static JsonObject ParseObject(JsonNode? node) =>
        node as JsonObject
        ?? throw new JsonException("Document cannot be null and must be a JSON object", "$", null, null);
}
