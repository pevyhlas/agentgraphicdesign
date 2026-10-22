using System.Text.Json;

namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// Parses the node-ID list accepted by the alignment surfaces: a comma, semicolon, or
/// whitespace separated string, or a JSON array of GUID strings.
/// </summary>
public static class NodeIdList
{
    public const int MaxCount = 1_000;
    public const int MaxInputLength = 64 * 1024;

    public static bool TryParse(string? input, out IReadOnlyList<Guid> ids, out string? error)
    {
        ids = [];
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "At least one node ID is required";
            return false;
        }

        if (input.Length > MaxInputLength)
        {
            error = $"Node ID list exceeds the {MaxInputLength} character limit";
            return false;
        }

        var trimmed = input.Trim();
        string[]? candidates;
        if (trimmed.StartsWith('['))
        {
            candidates = ParseJsonArray(trimmed, out error);
            if (candidates is null)
            {
                return false;
            }
        }
        else
        {
            candidates = trimmed.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (candidates.Length == 0)
        {
            error = "At least one node ID is required";
            return false;
        }

        if (candidates.Length > MaxCount)
        {
            error = $"Node ID list exceeds the {MaxCount} entry limit";
            return false;
        }

        var parsed = new List<Guid>(candidates.Length);
        var seen = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            if (!Guid.TryParse(candidate, out var id))
            {
                error = $"Invalid node ID: {candidate}";
                return false;
            }

            if (!seen.Add(id))
            {
                error = $"Duplicate node ID: {id}";
                return false;
            }

            parsed.Add(id);
        }

        ids = parsed;
        error = null;
        return true;
    }

    private static string[]? ParseJsonArray(string json, out string? error)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "Node ID list must be a JSON array of strings";
                return null;
            }

            var values = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    error = "Node ID list must be a JSON array of strings";
                    return null;
                }

                values.Add(element.GetString()!.Trim());
            }

            error = null;
            return [.. values];
        }
        catch (JsonException)
        {
            error = "Node ID list is not valid JSON";
            return null;
        }
    }
}
