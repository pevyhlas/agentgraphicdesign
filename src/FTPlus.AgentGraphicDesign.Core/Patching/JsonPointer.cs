using System.Globalization;
using System.Text.Json.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Patching;

/// <summary>
/// RFC 6901 JSON Pointer evaluation against a <see cref="JsonObject"/>, restricted to what a
/// node patch needs: set (creating missing intermediate objects, arrays by existing index or
/// <c>-</c> append) and remove (existing targets only). Failures are engine exceptions with
/// <see cref="EngineErrors.InvalidArguments"/> or <see cref="EngineErrors.InvalidProperty"/>.
/// </summary>
internal static class JsonPointer
{
    public const string AppendSegment = "-";

    public static IReadOnlyList<string> Parse(string pointer)
    {
        if (string.IsNullOrEmpty(pointer))
        {
            throw Invalid("Path must be a JSON Pointer such as '/style/fill/color'; use setNode to replace a whole node");
        }

        if (pointer[0] != '/')
        {
            throw Invalid($"Path '{pointer}' must start with '/'");
        }

        var segments = pointer[1..].Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length == 0)
            {
                throw Invalid($"Path '{pointer}' contains an empty segment");
            }

            segments[i] = segments[i].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        }

        return segments;
    }

    public static void Set(JsonObject root, IReadOnlyList<string> segments, JsonNode? value, string pointer)
    {
        JsonNode current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            switch (current)
            {
                case JsonObject obj:
                    if (!obj.TryGetPropertyValue(segment, out var child) || child is null)
                    {
                        child = new JsonObject();
                        obj[segment] = child;
                    }
                    current = child;
                    break;

                case JsonArray array:
                    current = array[ParseExistingIndex(segment, array.Count, pointer)]
                        ?? throw Invalid($"Path '{pointer}' descends into a null element");
                    break;

                default:
                    throw Invalid($"Path '{pointer}' descends into a value at '{Prefix(segments, i)}'");
            }
        }

        var last = segments[^1];
        switch (current)
        {
            case JsonObject obj:
                obj[last] = value;
                break;

            case JsonArray array:
                if (last == AppendSegment)
                {
                    array.Add(value);
                }
                else
                {
                    array[ParseExistingIndex(last, array.Count, pointer)] = value;
                }
                break;

            default:
                throw Invalid($"Path '{pointer}' descends into a value at '{Prefix(segments, segments.Count - 1)}'");
        }
    }

    public static void Remove(JsonObject root, IReadOnlyList<string> segments, string pointer)
    {
        JsonNode current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            current = current switch
            {
                JsonObject obj => obj.TryGetPropertyValue(segment, out var child) && child is not null
                    ? child
                    : throw Missing(pointer),
                JsonArray array => array[ParseExistingIndex(segment, array.Count, pointer)] ?? throw Missing(pointer),
                _ => throw Invalid($"Path '{pointer}' descends into a value at '{Prefix(segments, i)}'")
            };
        }

        var last = segments[^1];
        switch (current)
        {
            case JsonObject obj:
                if (!obj.Remove(last))
                {
                    throw Missing(pointer);
                }
                break;

            case JsonArray array:
                if (last == AppendSegment)
                {
                    throw Invalid($"Path '{pointer}' cannot remove with the '-' append index");
                }
                array.RemoveAt(ParseExistingIndex(last, array.Count, pointer));
                break;

            default:
                throw Invalid($"Path '{pointer}' descends into a value at '{Prefix(segments, segments.Count - 1)}'");
        }
    }

    private static int ParseExistingIndex(string segment, int count, string pointer)
    {
        if (segment == AppendSegment)
        {
            throw Invalid($"Path '{pointer}' may only use '-' as the final segment of a set");
        }

        if (segment.Length > 1 && segment[0] == '0' ||
            !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            throw Invalid($"Path '{pointer}' uses '{segment}' where an array index is required");
        }

        if (index >= count)
        {
            throw Invalid($"Path '{pointer}' index {index} is outside the array of {count} element(s); use '-' to append");
        }

        return index;
    }

    private static string Prefix(IReadOnlyList<string> segments, int count) =>
        "/" + string.Join("/", segments.Take(count).Select(Escape));

    private static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static DocumentEngineException Invalid(string message) =>
        new(EngineErrors.InvalidArguments, message);

    private static DocumentEngineException Missing(string pointer) =>
        new(EngineErrors.InvalidProperty, $"Path '{pointer}' does not exist on the node");
}
