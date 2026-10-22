using System.Text.Json;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>
/// Parses the multi-path argument of <c>render_contact_sheet</c>: either a JSON array of
/// strings (needed when a name contains a comma) or a comma-separated list. Whitespace is
/// never a separator. Confinement happens later, per entry.
/// </summary>
internal static class PathList
{
    public const int MaxInputCharacters = 64 * 1024;

    public static bool TryParse(string? input, int maxEntries, out IReadOnlyList<string> paths, out string? error)
    {
        paths = [];
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "At least one document path is required";
            return false;
        }
        if (input.Length > MaxInputCharacters)
        {
            error = $"Path list exceeds the {MaxInputCharacters} character limit";
            return false;
        }

        var trimmed = input.Trim();
        List<string> items;
        if (trimmed.StartsWith('['))
        {
            string?[]? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<string?[]>(trimmed);
            }
            catch (JsonException)
            {
                error = "Paths must be a JSON array of strings or a comma-separated list";
                return false;
            }

            if (parsed is null || parsed.Any(item => item is null))
            {
                error = "Paths must be a JSON array of strings or a comma-separated list";
                return false;
            }

            items = parsed.Select(item => item!.Trim()).ToList();
        }
        else
        {
            items = trimmed.Split(',').Select(item => item.Trim()).ToList();
        }

        if (items.Count == 0)
        {
            error = "At least one document path is required";
            return false;
        }
        if (items.Count > maxEntries)
        {
            error = $"At most {maxEntries} documents can be placed on one contact sheet";
            return false;
        }
        if (items.Any(string.IsNullOrEmpty))
        {
            error = "Paths cannot be empty";
            return false;
        }

        paths = items;
        error = null;
        return true;
    }
}
