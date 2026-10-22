using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Pixels;

namespace FTPlus.AgentGraphicDesign.Core.Wire;

/// <summary>
/// Shared wire-fidelity parser for native pixel requests. It reads the ORIGINAL UTF-8
/// bytes (the CLI request/snapshot file, or the preserved MCP tool-argument capture)
/// BEFORE any typed/dictionary binding can collapse duplicate members or normalize
/// numeric tokens, and rejects the lexical violations enumerated by the native pixel
/// API contract. It does not construct the typed Core model (that lands with the Core
/// native pixel service in a later slice); it is the ingestion gate that preserves the
/// evidence the typed model would otherwise erase.
/// </summary>
/// <remarks>
/// <para>Rules enforced (schema-independent, at every nested object/array):</para>
/// <list type="bullet">
///   <item>Duplicate decoded member names, including escaped-key duplicates
///   (<c>"a"</c> versus <c>"a"</c>), detected by enumerating members and keying
///   a set on the DECODED name so distinct escapings of the same name collide.</item>
///   <item>Numeric token spelling: reject exponents (<c>e</c>/<c>E</c>), signs,
///   leading zeros (except the value <c>0</c>) and more than six fractional digits
///   (opacity's grammar), by inspecting the raw UTF-8 value slice. Integers above
///   2^53 (for example <c>revn</c>) are never parsed through a double and survive.</item>
/// </list>
/// <para>Rules enforced at the closed request/snapshot envelopes:</para>
/// <list type="bullet">
///   <item>The edit request object is closed to <c>expectedSnapshot</c> and
///   <c>operations</c>; the snapshot object is closed to <c>assetId</c>, <c>revn</c>
///   and <c>sourceDigest</c>. Deeper per-operation closed-object validation belongs to
///   the typed Core model and is not duplicated here.</item>
///   <item>Original-byte size bounds: the edit request slice (including whitespace) is
///   at most 1&#160;MiB and a snapshot slice at most 1&#160;KiB, measured on the raw
///   source bytes before any clone/<c>GetRawText</c>.</item>
/// </list>
/// </remarks>
public static class AgdWireParser
{
    /// <summary>Maximum original UTF-8 bytes of the edit <c>request</c> object, including whitespace (1 MiB).</summary>
    public const int MaxRequestBytes = 1 << 20;

    /// <summary>Maximum original UTF-8 bytes of a snapshot object/file (1 KiB).</summary>
    public const int MaxSnapshotBytes = 1024;

    /// <summary>Coarse per-message ceiling for the transport read loop (8 MiB); not a Core reservation.</summary>
    public const int MaxMessageBytes = 8 * 1024 * 1024;

    /// <summary>Maximum fractional digits permitted on a decimal (opacity) wire token.</summary>
    public const int MaxOpacityFractionDigits = 6;

    private const string InvalidArguments = "INVALID_ARGUMENTS";
    private const string InvalidFormat = "INVALID_FORMAT";
    private const string ResourceLimit = "RESOURCE_LIMIT";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        // Default STJ parsing keeps the last of duplicate members instead of throwing;
        // we detect duplicates ourselves by enumerating, so allow them through the parse.
        AllowDuplicateProperties = true,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    private static readonly string[] RequestMembers = ["expectedSnapshot", "operations"];
    private static readonly string[] SnapshotMembers = ["assetId", "revn", "sourceDigest"];

    /// <summary>
    /// Import-only prebinding ingress: closed operation-specific shapes, original token grammar,
    /// and a borrowed slice into the TRANSPORT capture, not a request copy outside the native scope.
    /// The existing edit gate stays closed to its existing members.
    /// </summary>
    internal static ReadOnlyMemory<byte> AdmitImportToolCall(byte[] message, string toolName)
    {
        try
        {
            using var json = NativePixelFormat.ReadJson(message, MaxMessageBytes, 16, 4096 * 6, InvalidArguments);
            var root = json.RootElement;
            ClosedIngress(root, ["jsonrpc", "method", "params"], ["id"]);
            var parameters = root.GetProperty("params");
            ClosedIngress(parameters, ["name", "arguments"], ["_meta"]);
            if (NativePixelWire.String(parameters.GetProperty("name")) != toolName)
                throw new NativePixelException(InvalidArguments, "Captured tool name differs from dispatch.");
            var arguments = parameters.GetProperty("arguments");
            if (toolName == "import_pixel_asset")
            {
                ClosedIngress(arguments, ["assetDirectory", "descriptorPath", "name", "layerName"], []);
                _ = NativePixelWire.String(arguments.GetProperty("assetDirectory"));
                _ = NativePixelWire.String(arguments.GetProperty("descriptorPath"));
                NativePixelFormat.ValidateName(NativePixelWire.String(arguments.GetProperty("name")), "$.name", InvalidArguments);
                NativePixelFormat.ValidateName(NativePixelWire.String(arguments.GetProperty("layerName")), "$.layerName", InvalidArguments);
                return ReadOnlyMemory<byte>.Empty;
            }
            ClosedIngress(arguments, ["assetDirectory", "request"], []);
            _ = NativePixelWire.String(arguments.GetProperty("assetDirectory"));
            var request = arguments.GetProperty("request");
            EnforceSize(request, MaxRequestBytes, "request");
            NativePixelWire.ValidateImportLayerObject(request);
            var reader = new Utf8JsonReader(message);
            if (!reader.Read() || !FindMember(ref reader, "params"u8)
                || !FindMember(ref reader, "arguments"u8) || !FindMember(ref reader, "request"u8))
                throw new NativePixelException(InvalidArguments, "Original import request is unavailable.");
            int start = checked((int)reader.TokenStartIndex);
            reader.Skip();
            return message.AsMemory(start, checked((int)reader.BytesConsumed - start));
        }
        catch (NativePixelException error)
        {
            throw new WireFidelityException(error.Code, error.Message, error.Path ?? "$");
        }
    }

    private static void ClosedIngress(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new NativePixelException(InvalidArguments, "Expected an import argument object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if ((!required.Contains(field.Name, StringComparer.Ordinal) && !optional.Contains(field.Name, StringComparer.Ordinal))
                || !seen.Add(field.Name) || field.Value.ValueKind == JsonValueKind.Null)
                throw new NativePixelException(InvalidArguments, "Unknown, duplicate or null import member.");
        if (required.Any(name => !seen.Contains(name)))
            throw new NativePixelException(InvalidArguments, "Required import member is missing.");
    }

    private static bool FindMember(ref Utf8JsonReader reader, ReadOnlySpan<byte> name)
    {
        if (reader.TokenType != JsonTokenType.StartObject) return false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) return false;
            bool match = reader.ValueTextEquals(name);
            if (!reader.Read()) return false;
            if (match) return true;
            reader.Skip();
        }
        return false;
    }

    /// <summary>
    /// Admits a captured MCP <c>tools/call</c> message. Navigates <c>params.arguments</c>,
    /// validates outer and nested member names, enforces the original-byte size bound on a
    /// present <c>request</c> object and on a present <c>expectedReadSnapshot</c>, and checks
    /// numeric token spelling throughout.
    /// </summary>
    public static void AdmitToolCall(ReadOnlySpan<byte> msgUtf8)
    {
        using var document = Parse(msgUtf8);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("arguments", out var arguments) ||
            arguments.ValueKind != JsonValueKind.Object)
        {
            // No arguments object to admit lexically; typed binding/Core enforces presence.
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in arguments.EnumerateObject())
        {
            if (!seen.Add(argument.Name))
            {
                throw new WireFidelityException(InvalidArguments, $"Duplicate argument member '{argument.Name}'.", "$." + argument.Name);
            }

            switch (argument.Name)
            {
                case "request":
                    EnforceSize(argument.Value, MaxRequestBytes, "request");
                    ValidateRequestObject(argument.Value);
                    break;
                case "expectedReadSnapshot":
                    EnforceSize(argument.Value, MaxSnapshotBytes, "snapshot");
                    ValidateSnapshotObject(argument.Value);
                    break;
                default:
                    ValidateNode(argument.Value, "$." + argument.Name);
                    break;
            }
        }
    }

    /// <summary>
    /// Admits the CLI edit request bytes (the whole <c>--request</c> file). The object is
    /// closed to <c>expectedSnapshot</c>/<c>operations</c>, at most 1&#160;MiB of file bytes,
    /// and its nested members obey the duplicate and numeric-token rules.
    /// </summary>
    public static void AdmitRequest(ReadOnlySpan<byte> requestUtf8)
    {
        if (requestUtf8.Length > MaxRequestBytes)
        {
            throw new WireFidelityException(ResourceLimit, "Edit request exceeds the 1 MiB limit.", "$");
        }

        using var document = Parse(requestUtf8);
        ValidateRequestObject(document.RootElement);
    }

    /// <summary>
    /// Admits a CLI snapshot file's bytes. The object is closed to
    /// <c>assetId</c>/<c>revn</c>/<c>sourceDigest</c> and at most 1&#160;KiB of file bytes.
    /// </summary>
    public static void AdmitSnapshot(ReadOnlySpan<byte> snapshotUtf8)
    {
        if (snapshotUtf8.Length > MaxSnapshotBytes)
        {
            throw new WireFidelityException(ResourceLimit, "Snapshot exceeds the 1 KiB limit.", "$");
        }

        using var document = Parse(snapshotUtf8);
        ValidateSnapshotObject(document.RootElement);
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            // JsonDocument.Parse copies the input; GetRawUtf8Value later returns spans over
            // that internal copy, preserving the original value spelling and whitespace.
            return JsonDocument.Parse(utf8.ToArray(), ParseOptions);
        }
        catch (JsonException)
        {
            throw new WireFidelityException(InvalidFormat, "Malformed native request JSON.", "$");
        }
    }

    private static void EnforceSize(JsonElement element, int limit, string what)
    {
        // Measure the ORIGINAL byte slice (including internal whitespace) before any
        // GetRawText/clone; GetRawUtf8Value returns the verbatim source span, O(1) length.
        if (JsonMarshal.GetRawUtf8Value(element).Length > limit)
        {
            var code = ResourceLimit;
            var unit = limit >= MaxRequestBytes ? "1 MiB" : "1 KiB";
            throw new WireFidelityException(code, $"Original {what} bytes exceed the {unit} limit.", "$");
        }
    }

    private static void ValidateRequestObject(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object)
        {
            throw new WireFidelityException(InvalidArguments, "The edit request must be a JSON object.", "$");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in request.EnumerateObject())
        {
            if (!seen.Add(member.Name))
            {
                throw new WireFidelityException(InvalidArguments, $"Duplicate member '{member.Name}'.", "$." + member.Name);
            }

            if (Array.IndexOf(RequestMembers, member.Name) < 0)
            {
                throw new WireFidelityException(InvalidArguments, $"Unknown request member '{member.Name}'.", "$." + member.Name);
            }

            ValidateNode(member.Value, "$." + member.Name);
        }
    }

    private static void ValidateSnapshotObject(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            throw new WireFidelityException(InvalidArguments, "The snapshot must be a JSON object.", "$");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in snapshot.EnumerateObject())
        {
            if (!seen.Add(member.Name))
            {
                throw new WireFidelityException(InvalidArguments, $"Duplicate member '{member.Name}'.", "$." + member.Name);
            }

            if (Array.IndexOf(SnapshotMembers, member.Name) < 0)
            {
                throw new WireFidelityException(InvalidArguments, $"Unknown snapshot member '{member.Name}'.", "$." + member.Name);
            }

            ValidateNode(member.Value, "$." + member.Name);
        }
    }

    private static void ValidateNode(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in element.EnumerateObject())
                {
                    // Key on the DECODED name so an escaped duplicate (e.g. "a" for "a")
                    // collides with its plain spelling. GetProperty/last-value would miss both.
                    if (!seen.Add(member.Name))
                    {
                        throw new WireFidelityException(InvalidArguments, $"Duplicate member '{member.Name}'.", path);
                    }

                    ValidateNode(member.Value, AppendMember(path, member.Name));
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ValidateNode(item, $"{path}[{index}]");
                    index++;
                }
                break;

            case JsonValueKind.Number:
                ValidateNumberToken(element, path);
                break;

            default:
                break;
        }
    }

    private static void ValidateNumberToken(JsonElement number, string path)
    {
        ReadOnlySpan<byte> raw = JsonMarshal.GetRawUtf8Value(number);
        if (raw.IsEmpty)
        {
            throw new WireFidelityException(InvalidFormat, "Empty numeric token.", path);
        }

        // No sign. JSON already forbids a leading '+', so any sign here is a leading '-'.
        if (raw[0] == (byte)'+' || raw[0] == (byte)'-')
        {
            throw new WireFidelityException(InvalidFormat, "Signed numeric tokens are not permitted.", path);
        }

        var dot = -1;
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            if (b == (byte)'e' || b == (byte)'E')
            {
                throw new WireFidelityException(InvalidFormat, "Exponent numeric tokens are not permitted.", path);
            }

            if (b == (byte)'.' && dot < 0)
            {
                dot = i;
            }
        }

        var integerLength = dot < 0 ? raw.Length : dot;
        // Leading zero is only valid for the integer value 0 (or "0.xxx"); reject "01".
        if (integerLength > 1 && raw[0] == (byte)'0')
        {
            throw new WireFidelityException(InvalidFormat, "Leading-zero numeric tokens are not permitted.", path);
        }

        if (dot >= 0)
        {
            var fractionDigits = raw.Length - dot - 1;
            if (fractionDigits > MaxOpacityFractionDigits)
            {
                throw new WireFidelityException(InvalidFormat, "Decimal token has more than six fractional digits.", path);
            }
        }
    }

    private static string AppendMember(string path, string member) =>
        new StringBuilder(path.Length + member.Length + 1).Append(path).Append('.').Append(member).ToString();
}
