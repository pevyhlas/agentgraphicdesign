using System.Collections.Immutable;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

/// <summary>
/// Offline manifest builds and read-only drift checks. All exports finish before publication;
/// replacement is atomic per file, not across the batch. Never removes unexpected files.
/// Assumes a trusted local workspace, not hostile concurrent filesystem replacement.
/// </summary>
public sealed class AssetPipeline
{
    public const int MaxManifestBytes = 1024 * 1024;
    public const long MaxInputBytes = 64 * 1024 * 1024;
    public const long MaxOutputBytes = 64 * 1024 * 1024;
    public const int MaxAssetBytes = 32 * 1024 * 1024;
    public const long MaxRasterPixels = 64_000_000;
    public const int MaxNodes = 100_000;
    public const int MaxDiagnostics = 256;
    public const int MaxDiagnosticMessageCharacters = 4096;

    private readonly IDocumentEngine _engine;
    private readonly IAssetProducer<VectorAssetRequest> _producer;
    private readonly Func<string, FileStreamOptions, Stream> _open;

    public AssetPipeline(IDocumentEngine engine, IAssetProducer<VectorAssetRequest> producer)
        : this(engine, producer, LeafFileIO.Open) { }

    internal AssetPipeline(IDocumentEngine engine, IAssetProducer<VectorAssetRequest> producer,
        Func<string, FileStreamOptions, Stream> open)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _open = open;
    }

    public async Task<AssetPipelineResult> RunAsync(string manifestPath, bool check, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        ct.ThrowIfCancellationRequested();
        var paths = new AssetPathPolicy();
        try
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                throw AssetManifest.Invalid("A manifest path is required");
            // Reject traversal before normalization so a link/.. pair cannot hide a link.
            if (manifestPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
                throw AssetPaths.Unsafe(manifestPath);
            manifestPath = PlatformPaths.FullPath(manifestPath);
            AssetPaths.Inspect(manifestPath, directory: false, required: true, PlatformCapability.AssetPipeline, ct);
            paths.Check(manifestPath, ct);
            var directory = Path.GetDirectoryName(manifestPath)!;
            var manifest = AssetManifest.Parse(await ReadBoundedAsync(manifestPath, MaxManifestBytes, ct));
            var outputRoot = AssetPaths.Resolve(directory, manifest.OutputRoot);
            AssetPaths.Inspect(outputRoot, directory: true, required: false, PlatformCapability.AssetPipeline, ct);
            AssetPaths.RejectCaseAliases(directory, outputRoot, PlatformCapability.AssetPipeline, ct);
            paths.Check(outputRoot, ct);
            var plans = new List<PlannedAsset>();
            var outputs = new HashSet<string>(AssetPathPolicy.Comparer);
            var outputComponents = new Dictionary<string, string>(AssetPathPolicy.Comparer);
            var fontPathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var fontDirectories = new HashSet<string>(fontPathComparer);
            long inputBytes = 0;

            foreach (var entry in manifest.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var output = AssetPaths.Resolve(directory, entry.Output);
                if (!AssetPaths.Within(output, outputRoot) ||
                    !output.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw AssetPaths.Unsafe(entry.Output);
                if (!outputs.Add(output) || outputs.Any(other => other != output && AssetPaths.Overlaps(other, output)))
                    throw AssetManifest.Invalid($"Colliding asset output: {entry.Output}");
                for (var component = output; component != outputRoot; component = Path.GetDirectoryName(component)!)
                {
                    if (outputComponents.TryGetValue(component, out var prior) && prior != component)
                        throw AssetManifest.Invalid($"Case-insensitive output directory collision: {entry.Output}");
                    outputComponents[component] = component;
                }
                AssetPaths.Inspect(output, directory: false, required: false, PlatformCapability.AssetPipeline, ct);
                AssetPaths.RejectCaseAliases(directory, output, PlatformCapability.AssetPipeline, ct);
                paths.Check(output, ct);
                var format = Path.GetExtension(output).ToLowerInvariant() switch
                {
                    ".svg" => "svg",
                    ".png" => "png",
                    ".jpg" or ".jpeg" => "jpeg",
                    ".webp" => "webp",
                    _ => throw new DocumentEngineException(EngineErrors.ExportUnsupported, $"Unsupported output extension: {entry.Output}")
                };
                if (entry.Sprite && format != "svg")
                    throw AssetManifest.Invalid("Sprites require an .svg output");
                entry.Options.ValidateFormat(format);
                var sources = entry.Sources.Select(source => AssetPaths.Resolve(directory, source)).ToArray();
                foreach (var source in sources)
                {
                    if (AssetPaths.Overlaps(source, outputRoot) || AssetPathPolicy.Comparer.Equals(source, manifestPath))
                        throw AssetPaths.Unsafe(source);
                    AssetPaths.Inspect(source, directory: false, required: true, PlatformCapability.AssetPipeline, ct);
                    paths.Check(source, ct);
                    var size = new FileInfo(source).Length;
                    inputBytes += size;
                    if (size > FileDocumentStore.DefaultMaxDocumentBytes || inputBytes > MaxInputBytes)
                        throw AssetManifest.Limit();
                }
                var seenFonts = new HashSet<string>(fontPathComparer);
                var fonts = (entry.Options.FontDirs ?? []).Select(font => AssetPaths.Resolve(directory, font))
                    .Where(seenFonts.Add).ToImmutableArray();
                foreach (var font in fonts)
                {
                    if (AssetPaths.Overlaps(font, outputRoot))
                        throw AssetPaths.Unsafe(font);
                    paths.Check(font, ct);
                    if (!fontDirectories.Add(font))
                        continue;
                    if (fontDirectories.Count > 16)
                        throw AssetManifest.Limit();
                    foreach (var file in FontDirectoryInspection.Inspect(font, ct))
                    {
                        var isDirectory = (File.GetAttributes(file) & FileAttributes.Directory) != 0;
                        if (isDirectory || !FontDirectoryRules.IsFont(file))
                            continue;
                        var size = new FileInfo(file).Length;
                        inputBytes += size;
                        if (size > 8 * 1024 * 1024 || inputBytes > MaxInputBytes)
                            throw AssetManifest.Limit();
                    }
                }
                plans.Add(new PlannedAsset(sources, output, format, entry.Sprite, entry.Options, fonts));
            }

            var inventory = AssetPaths.Inventory(outputRoot, PlatformCapability.AssetPipeline, ct).ToHashSet(StringComparer.Ordinal);
            var generated = new List<byte[]>();
            var diagnostics = new List<AssetPipelineDiagnostic>();
            long outputBytes = 0;
            var budget = new AssetProductionBudget();
            foreach (var plan in plans)
            {
                var fragments = new List<byte[]>();
                foreach (var source in plan.Sources)
                {
                    ct.ThrowIfCancellationRequested();
                    AssetPaths.Inspect(source, directory: false, required: true, PlatformCapability.AssetPipeline, ct);
                    var loaded = _engine.TryLoadDocumentFromFile(source, ct);
                    var document = loaded.Document;
                    if (!loaded.Success || document is null)
                        throw new DocumentEngineException(EngineErrors.ParseError, $"{source}: {loaded.Error?.Message ?? "Failed to parse document"}");
                    ValidationProfiles.TryParse(plan.Options.Profile, out var profile);
                    var produced = await _producer.ProduceAsync(new VectorAssetRequest
                    {
                        Document = document,
                        SourceName = source,
                        Format = plan.Format switch
                        {
                            "svg" => VectorAssetFormat.Svg,
                            "png" => VectorAssetFormat.Png,
                            "jpeg" => VectorAssetFormat.Jpeg,
                            "webp" => VectorAssetFormat.Webp,
                            _ => throw AssetManifest.Invalid("Unknown vector asset format")
                        },
                        AsSymbol = plan.Sprite,
                        Strict = plan.Options.Strict ?? true,
                        Profile = profile,
                        CurrentColor = plan.Options.CurrentColor,
                        Scale = plan.Options.Scale ?? 1,
                        Width = plan.Options.Width,
                        Height = plan.Options.Height,
                        FontDirectories = plan.Fonts,
                        SystemFonts = plan.Options.SystemFonts ?? false,
                        StrictFonts = plan.Options.StrictFonts ?? true
                    }, budget, ct);
                    try
                    {
                        foreach (var diagnostic in produced.Diagnostics)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (diagnostics.Count == MaxDiagnostics)
                            {
                                // The last slot identifies the first omitted diagnostic's source/output.
                                diagnostics[^1] = diagnostics[^1] with
                                {
                                    Code = RenderDiagnosticCodes.DiagnosticsTruncated,
                                    Message = $"Further renderer diagnostics omitted after the first {MaxDiagnostics - 1} entries in this batch"
                                };
                                break;
                            }
                            if (diagnostic.Code.Length > 128)
                                throw AssetManifest.Limit();
                            const string truncated = "... [truncated]";
                            var message = diagnostic.Message.Length <= MaxDiagnosticMessageCharacters ? diagnostic.Message
                                : diagnostic.Message[..(MaxDiagnosticMessageCharacters - truncated.Length)] + truncated;
                            diagnostics.Add(new AssetPipelineDiagnostic(
                                PlatformPaths.RelativePath(directory, source).Replace(Path.DirectorySeparatorChar, '/'),
                                PlatformPaths.RelativePath(directory, plan.Output).Replace(Path.DirectorySeparatorChar, '/'), diagnostic.Code, message));
                        }
                    }
                    catch (Exception ex) when (ex is not (DocumentEngineException or OperationCanceledException or OutOfMemoryException))
                    {
                        throw new DocumentEngineException("ASSET_EXPORT_FAILED", $"{source}: {ex.Message}");
                    }
                    ct.ThrowIfCancellationRequested();
                    var data = produced.Data;
                    outputBytes += data.Length;
                    if (data.Length > MaxAssetBytes || outputBytes > MaxOutputBytes)
                        throw AssetManifest.Limit();
                    // Freeze exactly this slice before another call can reuse producer storage.
                    fragments.Add(data.ToArray());
                }
                var bytes = plan.Sprite ? SvgSpriteComposer.Compose(fragments, ct) : fragments[0];
                if (plan.Sprite)
                    outputBytes += bytes.Length - fragments.Sum(fragment => (long)fragment.Length);
                if (bytes.Length > MaxAssetBytes || outputBytes > MaxOutputBytes)
                    throw AssetManifest.Limit();
                generated.Add(bytes);
            }

            // Compare the complete batch before any mkdir/temp file/rename, even in build mode.
            paths.Invalidate();
            var files = new List<AssetFileResult>();
            for (var index = 0; index < plans.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var output = plans[index].Output;
                AssetPaths.RejectCaseAliases(directory, output, PlatformCapability.AssetPipeline, ct);
                paths.Check(output, ct);
                var status = "missing";
                if (inventory.Contains(output))
                {
                    AssetPaths.Inspect(output, directory: false, required: true, PlatformCapability.AssetPipeline, ct);
                    var expected = generated[index];
                    status = new FileInfo(output).Length == expected.Length &&
                        (await ReadBoundedAsync(output, expected.Length, ct)).AsSpan().SequenceEqual(expected)
                        ? "unchanged" : "stale";
                }
                files.Add(new AssetFileResult(PlatformPaths.RelativePath(directory, output).Replace(Path.DirectorySeparatorChar, '/'), status));
            }
            foreach (var orphan in inventory.Where(path => !plans.Any(plan => plan.Output == path)).Order(StringComparer.Ordinal))
                files.Add(new AssetFileResult(PlatformPaths.RelativePath(directory, orphan).Replace(Path.DirectorySeparatorChar, '/'), "unexpected"));

            if (!check)
            {
                for (var index = 0; index < plans.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (files[index].Status == "unchanged")
                        continue;
                    await PublishAsync(plans[index].Output, generated[index], paths, ct);
                    files[index] = files[index] with { Status = "written" };
                }
            }
            ct.ThrowIfCancellationRequested();
            return new AssetPipelineResult(check, files.AsReadOnly(), diagnostics.AsReadOnly());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DocumentEngineException("ASSET_IO_ERROR", $"Asset filesystem operation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw AssetManifest.Invalid($"Invalid asset path or option: {ex.Message}");
        }
    }

    private async Task<byte[]> ReadBoundedAsync(string path, int maximum, CancellationToken ct)
    {
        await using var stream = _open(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 64 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        if (stream.Length > maximum)
            throw AssetManifest.Limit();
        var data = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(data, ct);
        if (stream.ReadByte() != -1)
            throw AssetManifest.Limit();
        return data;
    }

    private async Task PublishAsync(string output, byte[] data, AssetPathPolicy paths, CancellationToken ct)
    {
        AssetPaths.Inspect(output, directory: false, required: false, PlatformCapability.AssetPipeline, ct);
        paths.Check(output, ct);
        var directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        paths.Invalidate();
        paths.Check(output, ct);
        AssetPaths.Inspect(directory, directory: true, required: true, PlatformCapability.AssetPipeline, ct);
        var temporary = Path.Combine(directory, $".agd-assets-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            await using (var stream = _open(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous
            }))
            {
                created = true;
                await stream.WriteAsync(data, ct);
                await stream.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            AssetPaths.Inspect(output, directory: false, required: false, PlatformCapability.AssetPipeline, ct);
            paths.Invalidate();
            paths.Check(output, ct);
            File.Move(temporary, output, overwrite: true);
            paths.Invalidate();
        }
        finally
        {
            if (created)
                File.Delete(temporary);
        }
    }

    private sealed class AssetProductionBudget : IAssetProductionBudget
    {
        private int _remainingNodes = MaxNodes;
        private long _remainingPixels = MaxRasterPixels;

        public void ChargeNodes(int count)
        {
            if (count < 0 || count > _remainingNodes)
                throw AssetManifest.Limit();
            _remainingNodes -= count;
        }

        public void ChargeRasterPixels(long count)
        {
            if (count < 0 || count > _remainingPixels)
                throw AssetManifest.Limit();
            _remainingPixels -= count;
        }
    }

    private sealed record PlannedAsset(string[] Sources, string Output, string Format, bool Sprite, AssetOptions Options, ImmutableArray<string> Fonts);
}
