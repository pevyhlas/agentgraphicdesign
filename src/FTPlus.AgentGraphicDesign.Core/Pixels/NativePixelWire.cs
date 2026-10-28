using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Paths;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

internal static class NativePixelWire
{
    private const string Invalid = "INVALID_ARGUMENTS";

    internal static ImportPixelLayerRequest ParseImportLayer(ReadOnlyMemory<byte> utf8, string assetDirectory,
        string inputBaseDirectory, string? confinementRoot, Action? checkBudget = null)
    {
        using var json = NativePixelFormat.ReadJson(utf8, NativePixelFormat.MaxJsonBytes, 16, 4096 * 6, Invalid, checkBudget);
        var root = json.RootElement;
        ValidateImportLayerObject(root);
        string descriptor = NormalizePath(String(root.GetProperty("descriptorPath"), "$.descriptorPath"),
            inputBaseDirectory, confinementRoot, "$.descriptorPath");
        return new ImportPixelLayerRequest(assetDirectory, descriptor,
            Snapshot(root.GetProperty("expectedSnapshot"), "$.expectedSnapshot"), String(root.GetProperty("layerName"), "$.layerName"));
    }

    internal static void ValidateImportLayerObject(JsonElement request)
    {
        Fields(request, ["descriptorPath", "expectedSnapshot", "layerName"], [], "$");
        _ = Snapshot(request.GetProperty("expectedSnapshot"), "$.expectedSnapshot");
        _ = String(request.GetProperty("descriptorPath"), "$.descriptorPath");
        NativePixelFormat.ValidateName(String(request.GetProperty("layerName"), "$.layerName"), "$.layerName", Invalid);
    }

    internal static EditPixelAssetRequest ParseEdit(
        ReadOnlyMemory<byte> utf8, string assetDirectory, string inputBaseDirectory,
        string? confinementRoot = null, Action? checkBudget = null)
    {
        using var json = NativePixelFormat.ReadJson(utf8, NativePixelFormat.MaxJsonBytes, 16, 4096 * 6, Invalid, checkBudget);
        var root = json.RootElement;
        Fields(root, ["expectedSnapshot", "operations"], [], "$");
        var snapshot = Snapshot(root.GetProperty("expectedSnapshot"), "$.expectedSnapshot");
        var operations = root.GetProperty("operations");
        if (operations.ValueKind != JsonValueKind.Array)
            throw Fail("Expected an operation array.", "$.operations");
        int count = operations.GetArrayLength();
        if (count < 1) throw Fail("An edit must contain at least one operation.", "$.operations");
        if (count > 64) throw new NativePixelException("RESOURCE_LIMIT", "Edit operation count exceeds its limit.", "$.operations");
        var result = ImmutableArray.CreateBuilder<PixelEdit>(count);
        var aliases = new Dictionary<string, bool>(StringComparer.Ordinal);
        long replacementBytes = 0, targetedSamples = 0;
        foreach (var operation in operations.EnumerateArray())
        {
            checkBudget?.Invoke();
            int index = result.Count;
            string path = "$.operations[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            try
            {
                // Check the discriminator without collapsing duplicate decoded member names.
                if (operation.ValueKind != JsonValueKind.Object)
                    throw Fail("Expected an edit operation object.", path);
                JsonElement opElement = default;
                bool found = false;
                foreach (var field in operation.EnumerateObject())
                {
                    if (!field.NameEquals("op")) continue;
                    if (found) throw Fail("Duplicate operation discriminator.", path + ".op");
                    found = true;
                    opElement = field.Value;
                }
                if (!found) throw Fail("Required operation discriminator is missing.", path + ".op");
                string op = String(opElement, path + ".op");
                string[] fields = op switch
                {
                    "addLayer" => ["op", "name"],
                    "removeLayer" or "attachMask" or "removeMask" => ["op", "layer"],
                    "reorderLayer" => ["op", "layer", "index"],
                    "setVisibility" => ["op", "layer", "visible"],
                    "setOpacity" => ["op", "layer", "opacity"],
                    "fill" => ["op", "layer", "target", "rect"],
                    "clear" => ["op", "layer", "target", "rect"],
                    "replace" => ["op", "layer", "target", "rect", "inputPath"],
                    _ => throw Fail("Unknown edit operation.", path + ".op")
                };
                Fields(operation, fields, op == "addLayer" ? ["alias"] : op == "fill" ? ["rgba", "value"] : [], path);
                if (op == "addLayer")
                {
                    string name = String(operation.GetProperty("name"), path + ".name");
                    NativePixelFormat.ValidateName(name, path + ".name", Invalid);
                    string? alias = null;
                    if (operation.TryGetProperty("alias", out var aliasValue))
                    {
                        alias = String(aliasValue, path + ".alias");
                        ValidateAlias(alias, path + ".alias");
                        if (!aliases.TryAdd(alias, true)) throw Fail("Alias is already declared in this batch.", path + ".alias");
                    }
                    result.Add(new PixelEdit.AddLayer(name, alias));
                    continue;
                }
                var layer = LayerRef(operation.GetProperty("layer"), path + ".layer");
                if (layer is PixelLayerRef.ByAlias reference && (!aliases.TryGetValue(reference.Alias, out bool live) || !live))
                    throw Fail("Alias must identify an earlier surviving layer declaration.", path + ".layer");
                PixelEdit edit;
                switch (op)
                {
                    case "removeLayer":
                        edit = new PixelEdit.RemoveLayer(layer);
                        if (layer is PixelLayerRef.ByAlias removed) aliases[removed.Alias] = false;
                        break;
                    case "reorderLayer":
                        int position = Integer(operation.GetProperty("index"), path + ".index");
                        if (position >= NativePixelFormat.MaxLayers) throw Fail("Layer index is outside the layer limit.", path + ".index");
                        edit = new PixelEdit.ReorderLayer(layer, position);
                        break;
                    case "setVisibility":
                        edit = new PixelEdit.SetVisibility(layer, Boolean(operation.GetProperty("visible"), path + ".visible"));
                        break;
                    case "setOpacity":
                        edit = new PixelEdit.SetOpacity(layer, Opacity(operation.GetProperty("opacity"), path + ".opacity"));
                        break;
                    case "attachMask":
                        edit = new PixelEdit.AttachMask(layer);
                        break;
                    case "removeMask":
                        edit = new PixelEdit.RemoveMask(layer);
                        break;
                    default:
                        PixelTarget target = Target(operation.GetProperty("target"), path + ".target");
                        PixelRect rect = Rect(operation.GetProperty("rect"), path + ".rect");
                        long area = (long)rect.Width * rect.Height;
                        if (area > 16_777_216 - targetedSamples)
                            throw new NativePixelException("RESOURCE_LIMIT", "Cumulative targeted samples exceed the edit limit.", path + ".rect");
                        targetedSamples += area;
                        if (op == "clear") edit = new PixelEdit.Clear(layer, target, rect);
                        else if (op == "replace")
                        {
                            long bytes = area * (target == PixelTarget.Pixels ? 4 : 1);
                            if (bytes > 16_777_216 - replacementBytes)
                                throw new NativePixelException("RESOURCE_LIMIT", "Replacement inputs exceed the aggregate byte limit.", path + ".rect");
                            replacementBytes += bytes;
                            string inputPath = NormalizePath(String(operation.GetProperty("inputPath"), path + ".inputPath"),
                                inputBaseDirectory, confinementRoot, path + ".inputPath");
                            edit = new PixelEdit.Replace(layer, target, rect, new RawSampleInput.File(inputPath));
                        }
                        else if (target == PixelTarget.Pixels)
                        {
                            Fields(operation, ["op", "layer", "target", "rect", "rgba"], [], path);
                            string rgba = String(operation.GetProperty("rgba"), path + ".rgba");
                            ValidateRgba(rgba, path + ".rgba");
                            edit = new PixelEdit.FillPixels(layer, rect, rgba);
                        }
                        else
                        {
                            Fields(operation, ["op", "layer", "target", "rect", "value"], [], path);
                            int value = Integer(operation.GetProperty("value"), path + ".value");
                            if (value > 255) throw Fail("Mask sample must be an integer byte.", path + ".value");
                            edit = new PixelEdit.FillMask(layer, rect, (byte)value);
                        }
                        break;
                }
                result.Add(edit);
            }
            catch (NativePixelException e)
            {
                throw new NativePixelException(e.Code, e.Message, e.Path ?? path, index);
            }
        }
        return new EditPixelAssetRequest(assetDirectory, snapshot, result.MoveToImmutable());
    }

    internal static PixelSnapshot ParseSnapshot(ReadOnlyMemory<byte> utf8, Action? checkBudget = null)
    {
        using var json = NativePixelFormat.ReadJson(utf8, 1024, 16, 1536, Invalid, checkBudget);
        return Snapshot(json.RootElement);
    }

    internal static PixelSnapshot Snapshot(JsonElement value, string path = "$")
    {
        if (value.ValueKind == JsonValueKind.Object && JsonMarshal.GetRawUtf8Value(value).Length > 1024)
            throw new NativePixelException("RESOURCE_LIMIT", "Snapshot JSON exceeds its byte limit.", path);
        Fields(value, ["assetId", "revn", "sourceDigest"], [], path);
        Guid id = Guid(value.GetProperty("assetId"), path + ".assetId");
        long revn = NativePixelFormat.Integer(value.GetProperty("revn"), path + ".revn", Invalid);
        if (revn < 1) throw Fail("Snapshot revision must be positive.", path + ".revn");
        string digest = String(value.GetProperty("sourceDigest"), path + ".sourceDigest");
        NativePixelFormat.ValidateHash(digest, path + ".sourceDigest", Invalid);
        return new PixelSnapshot(id, revn, digest);
    }

    internal static void ValidateSnapshot(PixelSnapshot? value, string path = "$.expectedSnapshot")
    {
        if (value is null) throw Fail("Expected a complete snapshot.", path);
        if (value.AssetId == System.Guid.Empty) throw Fail("Snapshot ID must be nonempty.", path + ".assetId");
        if (value.Revn < 1) throw Fail("Snapshot revision must be positive.", path + ".revn");
        NativePixelFormat.ValidateHash(value.SourceDigest, path + ".sourceDigest", Invalid);
    }

    internal static PixelRect Rect(JsonElement value, string path = "$.rect")
    {
        Fields(value, ["x", "y", "width", "height"], [], path);
        var rect = new PixelRect(Integer(value.GetProperty("x"), path + ".x"), Integer(value.GetProperty("y"), path + ".y"),
            Integer(value.GetProperty("width"), path + ".width"), Integer(value.GetProperty("height"), path + ".height"));
        ValidateRect(rect, path);
        return rect;
    }

    internal static void ValidateRect(PixelRect rect, string path = "$.rect", int? width = null, int? height = null)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width < 1 || rect.Height < 1
            || (long)rect.X + rect.Width > (width ?? int.MaxValue) || (long)rect.Y + rect.Height > (height ?? int.MaxValue))
            throw Fail("Rectangle must be positive and fully within its bounds.", path);
    }

    internal static PixelTarget Target(JsonElement value, string path = "$.target") => String(value, path) switch
    {
        "pixels" => PixelTarget.Pixels,
        "mask" => PixelTarget.Mask,
        _ => throw Fail("Expected a pixels or mask target.", path)
    };

    internal static PixelLayerRef LayerRef(JsonElement value, string path = "$.layer")
    {
        string text = String(value, path);
        if (text.Length > 36) throw Fail("Layer reference exceeds its character limit.", path);
        if (text.StartsWith('@'))
        {
            string alias = text[1..];
            ValidateAlias(alias, path);
            return new PixelLayerRef.ByAlias(alias);
        }
        return new PixelLayerRef.ById(Guid(value, path));
    }

    internal static void ValidateAlias(string? alias, string path = "$.alias")
    {
        if (alias is not { Length: >= 1 and <= 32 } || alias[0] is < 'a' or > 'z'
            || alias.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
            throw Fail("Invalid edit-local layer alias.", path);
    }

    internal static void ValidateRgba(string? rgba, string path = "$.rgba")
    {
        if (rgba is not { Length: 9 } || rgba[0] != '#' || rgba.AsSpan(1).ContainsAnyExcept("0123456789abcdef"))
            throw Fail("Expected a lowercase eight-channel-digit RGBA literal.", path);
    }

    internal static int Integer(JsonElement value, string path = "$")
    {
        long result = NativePixelFormat.Integer(value, path, Invalid);
        if (result > int.MaxValue) throw Fail("Integer exceeds the field range.", path);
        return (int)result;
    }

    internal static Guid Guid(JsonElement value, string path = "$") => NativePixelFormat.GuidValue(value, path, Invalid);
    internal static decimal Opacity(JsonElement value, string path = "$.opacity") => NativePixelFormat.Opacity(value, path, Invalid);
    internal static string String(JsonElement value, string path = "$") => NativePixelFormat.String(value, path, Invalid);
    internal static bool Boolean(JsonElement value, string path = "$") => NativePixelFormat.Boolean(value, path, Invalid);
    internal static void Fields(JsonElement value, string[] required, string[] optional, string path = "$")
        => NativePixelFormat.ObjectFields(value, required, optional, path, Invalid);

    internal static string NormalizePath(string path, string baseDirectory, string? confinementRoot = null, string location = "$")
    {
        int colon = path?.IndexOf(':') ?? -1;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(c => c < 32 || c == 127)
            || path.Contains('\\') || path.StartsWith("//", StringComparison.Ordinal)
            || (colon >= 0 && Uri.CheckSchemeName(path[..colon])))
            throw Fail("Expected a bounded local filesystem path.", location);
        if (string.IsNullOrWhiteSpace(baseDirectory) || baseDirectory.Length > 4096 || !Path.IsPathFullyQualified(baseDirectory))
            throw Fail("Input base directory must be an absolute vetted local path.", location);
        try
        {
            string absolute = PlatformPaths.FullPath(path, baseDirectory);
            if (absolute.Length > 4096) throw Fail("Normalized path exceeds its character limit.", location);
            if (confinementRoot is not null)
            {
                if (confinementRoot.Length > 4096 || !Path.IsPathFullyQualified(confinementRoot))
                    throw Fail("Confinement root must be an absolute vetted local path.", location);
                string root = Path.TrimEndingDirectorySeparator(PlatformPaths.FullPath(confinementRoot));
                string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
                if (!string.Equals(absolute, root, StringComparison.Ordinal) && !absolute.StartsWith(prefix, StringComparison.Ordinal))
                    throw new NativePixelException("PATH_ESCAPE", "Input path escapes the configured root.", location);
            }
            return absolute;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Fail("Invalid local filesystem path.", location);
        }
    }

    private static NativePixelException Fail(string message, string path) => new(Invalid, message, path);
}
