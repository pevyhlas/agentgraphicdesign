using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

internal sealed record AssetOptions
{
    internal bool? Strict { get; init; }
    internal string? Profile { get; init; }
    internal string? CurrentColor { get; init; }
    internal double? Scale { get; init; }
    internal int? Width { get; init; }
    internal int? Height { get; init; }
    internal string[]? FontDirs { get; init; }
    internal bool? SystemFonts { get; init; }
    internal bool? StrictFonts { get; init; }

    internal static AssetOptions Parse(JsonElement element, AssetOptions inherited)
    {
        AssetManifest.CheckObject(element, "strict", "profile", "currentColor", "scale", "width", "height",
            "fontDirs", "systemFonts", "strictFonts");
        var options = inherited;
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            var clear = value.ValueKind == JsonValueKind.Null;
            options = property.Name switch
            {
                "strict" => options with { Strict = clear ? null : value.GetBoolean() },
                "profile" => options with { Profile = clear ? null : value.GetString() },
                "currentColor" => options with { CurrentColor = clear ? null : value.GetString() },
                "scale" => options with { Scale = clear ? null : value.GetDouble() },
                "width" => options with { Width = clear ? null : value.GetInt32() },
                "height" => options with { Height = clear ? null : value.GetInt32() },
                "fontDirs" => options with { FontDirs = clear ? null : AssetManifest.Strings(value, 16) },
                "systemFonts" => options with { SystemFonts = clear ? null : value.GetBoolean() },
                "strictFonts" => options with { StrictFonts = clear ? null : value.GetBoolean() },
                _ => throw AssetManifest.Invalid("Unknown option")
            };
        }

        if (!ValidationProfiles.TryParse(options.Profile, out _) ||
            options.CurrentColor is not null && !DocumentLimits.IsValidColor(options.CurrentColor))
            throw AssetManifest.Invalid("Invalid profile or currentColor option");
        if (options.Scale is double scale && (!double.IsFinite(scale) || scale is < 0.1 or > 4) ||
            options.Width is < 1 or > 8192 || options.Height is < 1 or > 8192)
            throw AssetManifest.Invalid("Scale must be 0.1-4 and dimensions must be 1-8192");
        return options;
    }

    internal void ValidateFormat(string format)
    {
        if (format == "svg" && (Scale is not null || Width is not null || Height is not null ||
            FontDirs is not null || SystemFonts is not null || StrictFonts is not null))
            throw AssetManifest.Invalid("Explicit sizing and font options only apply to raster outputs; use null to clear inherited options");
        if (format != "svg" && CurrentColor is not null)
            throw AssetManifest.Invalid("currentColor only applies to SVG outputs");
    }
}

internal sealed record AssetEntry(string[] Sources, string Output, bool Sprite, AssetOptions Options);

internal sealed record AssetManifest(string OutputRoot, IReadOnlyList<AssetEntry> Entries)
{
    internal const int MaxDocuments = 256;

    internal static AssetManifest Parse(ReadOnlyMemory<byte> data)
    {
        try
        {
            using var json = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            CheckObject(root, "version", "outputRoot", "defaults", "assets", "sprites");
            if (root.GetProperty("version").GetInt32() != 1)
                throw new DocumentEngineException(EngineErrors.UnsupportedVersion, "Asset manifest version must be 1");
            var outputRoot = String(root.GetProperty("outputRoot"));
            var defaults = root.TryGetProperty("defaults", out var value)
                ? AssetOptions.Parse(value, new AssetOptions()) : new AssetOptions();
            var entries = new List<AssetEntry>();
            var documentCount = 0;
            foreach (var kind in new[] { "assets", "sprites" })
            {
                if (!root.TryGetProperty(kind, out var array))
                    continue;
                if (array.GetArrayLength() > MaxDocuments)
                    throw Limit();
                foreach (var entry in array.EnumerateArray())
                {
                    var sprite = kind == "sprites";
                    CheckObject(entry, sprite ? "sources" : "source", "output", "options");
                    var sources = sprite ? Strings(entry.GetProperty("sources"), MaxDocuments)
                        : [String(entry.GetProperty("source"))];
                    if (sources.Length == 0)
                        throw Invalid("A sprite requires at least one source");
                    documentCount += sources.Length;
                    if (documentCount > MaxDocuments)
                        throw Limit();
                    var options = entry.TryGetProperty("options", out value)
                        ? AssetOptions.Parse(value, defaults) : defaults;
                    entries.Add(new AssetEntry(sources, String(entry.GetProperty("output")), sprite, options));
                }
            }
            if (entries.Count == 0)
                throw Invalid("An asset manifest must contain at least one asset or sprite");
            return new AssetManifest(outputRoot, entries);
        }
        catch (JsonException ex)
        {
            throw new DocumentEngineException(EngineErrors.ParseError, $"Invalid asset manifest JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw Invalid("Asset manifest has a missing property or an incorrect JSON value type");
        }
    }

    internal static void CheckObject(JsonElement element, params string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw Invalid($"Unknown or duplicate manifest property '{property.Name}'");
        }
    }

    internal static string[] Strings(JsonElement element, int maximum)
    {
        if (element.GetArrayLength() > maximum)
            throw Limit();
        return element.EnumerateArray().Select(String).ToArray();
    }

    private static string String(JsonElement element)
    {
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid("Manifest paths must be nonempty strings");
        return value;
    }

    internal static DocumentEngineException Invalid(string message) => new(EngineErrors.InvalidArguments, message);
    internal static DocumentEngineException Limit() => new(EngineErrors.ResourceLimit, "Asset pipeline resource limit exceeded");
}
