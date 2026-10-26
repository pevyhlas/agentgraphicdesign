using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

internal interface IReferenceReader
{
    Task<ReferenceInputs> ReadAsync(IReadOnlyList<AssetReference> references, IReadOnlyList<string> fontDirectories,
        string baseDirectory, CancellationToken ct = default);
}

internal sealed record CapturedDocument(Document Document, string FullPath, string? OutputPath, string Sha256, long Bytes);
internal sealed record ResolvedReference(AssetReference Reference, CapturedDocument Source);
internal sealed record ReferenceInputs(ImmutableArray<ResolvedReference> References, InspectedFontSet Fonts, long CountedBytes);

/// <summary>
/// One capture per distinct full path, shared by hash and parse. No renderer or store is opened.
/// MCP must confine paths first; CLI may use paths outside the base (reported as null).
/// </summary>
internal sealed class ReferenceReader : IReferenceReader
{
    internal const long MaxInputBytes = 64 * 1024 * 1024;
    private const string NativeName = "asset.agd-pixels.json";
    private readonly bool _inspect;
    private readonly long _maxInputBytes;
    private readonly Func<string, Stream> _open;

    internal ReferenceReader() : this(PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline), MaxInputBytes,
        path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
    { }

    // Internal test seams only: no public gate override, budget override or input-open hook.
    internal ReferenceReader(bool inspect, long maxInputBytes, Func<string, Stream> open)
    {
        _inspect = inspect;
        _maxInputBytes = maxInputBytes;
        _open = open;
    }

    public async Task<ReferenceInputs> ReadAsync(IReadOnlyList<AssetReference> references,
        IReadOnlyList<string> fontDirectories, string baseDirectory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (!Path.IsPathFullyQualified(baseDirectory))
            throw new ArgumentException("An absolute base directory is required", nameof(baseDirectory));
        if (references.Count is < 1 or > 256)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "Review inputs require 1 to 256 references");
        ct.ThrowIfCancellationRequested();
        try
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var lengths = new Dictionary<string, long>(comparer);
            var paths = new List<string>();
            long total = 0;
            foreach (var reference in references)
            {
                ct.ThrowIfCancellationRequested();
                ArgumentException.ThrowIfNullOrWhiteSpace(reference.DocumentPath);
                // Lexical refusal must win even for a missing path or an invalid base component.
                if (Path.GetFileName(Path.TrimEndingDirectorySeparator(reference.DocumentPath)).Equals(NativeName, StringComparison.OrdinalIgnoreCase))
                    throw Unsupported();
                var raw = Path.IsPathFullyQualified(reference.DocumentPath) ? reference.DocumentPath : Path.Combine(baseDirectory, reference.DocumentPath);
                InspectReference(raw, ct);
                var path = Path.GetFullPath(raw);
                paths.Add(path);
                if (lengths.ContainsKey(path))
                    continue;
                var length = new FileInfo(path).Length;
                if (length > FileDocumentStore.DefaultMaxDocumentBytes)
                    throw TooLarge();
                lengths.Add(path, length);
                total += length;
                CheckTotal(total);
            }

            var fonts = InspectedFontSet.Inspect(fontDirectories, baseDirectory, _inspect, ct);
            total += fonts.CountedBytes;
            CheckTotal(total); // Includes all distinct lengths and fonts BEFORE the first document open.
            var captured = new Dictionary<string, CapturedDocument>(comparer);
            foreach (var (path, length) in lengths)
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = _open(path);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, ct)) != 0)
                {
                    if (buffer.Length + read > FileDocumentStore.DefaultMaxDocumentBytes ||
                        total - length + buffer.Length + read > _maxInputBytes)
                        throw TooLarge();
                    buffer.Write(chunk, 0, read);
                }
                total += buffer.Length - length;
                CheckTotal(total);
                var bytes = buffer.ToArray();
                ct.ThrowIfCancellationRequested();
                RejectNativeContent(bytes);
                var parsed = DocumentSerializer.TryDeserialize(bytes);
                if (!parsed.Success)
                    throw new DocumentEngineException(EngineErrors.ParseError, "Invalid review document JSON");
                captured.Add(path, new CapturedDocument(parsed.Document!, path, RelativePath(baseDirectory, path),
                    Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength));
            }
            return new ReferenceInputs(references.Select((reference, index) => new ResolvedReference(reference, captured[paths[index]])).ToImmutableArray(), fonts, total);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DocumentEngineException(EngineErrors.DocumentNotFound, "Review input not found");
        }
        catch (DocumentEngineException ex) when (ex.Code is EngineErrors.PathEscape or EngineErrors.DocumentNotFound)
        {
            // The shared probe's diagnostic may contain host paths. New review outputs never do.
            throw new DocumentEngineException(ex.Code, ex.Code == EngineErrors.PathEscape
                ? "Review input must be link-free and of the required file type" : "Review input not found");
        }
    }

    private void InspectReference(string path, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            if (_inspect)
                AssetPaths.Inspect(path, directory: true, required: true, PlatformCapability.AssetPipeline, ct);
            var child = Path.Combine(path, NativeName);
            var exists = _inspect
                ? AssetPaths.Inspect(child, directory: false, required: false, PlatformCapability.AssetPipeline, ct)
                : File.Exists(child);
            if (exists)
                throw Unsupported();
            throw AssetPaths.Unsafe(path);
        }
        if (_inspect)
            AssetPaths.Inspect(path, directory: false, required: true, PlatformCapability.AssetPipeline, ct);
    }

    private static void RejectNativeContent(byte[] bytes)
    {
        try
        {
            ReadOnlyMemory<byte> json = bytes;
            if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
                json = json[3..];
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Any(property =>
                property.NameEquals("schema") && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == "agd.native-pixels"))
                throw Unsupported();
        }
        catch (JsonException)
        {
            throw new DocumentEngineException(EngineErrors.ParseError, "Invalid review document JSON");
        }
    }

    internal static string? RelativePath(string baseDirectory, string path)
    {
        var relative = Path.GetRelativePath(baseDirectory, path);
        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void CheckTotal(long total)
    {
        if (total > _maxInputBytes)
            throw TooLarge();
    }

    private static DocumentEngineException Unsupported() => new("REFERENCE_KIND_UNSUPPORTED", "Native pixel assets are not supported review references");
    private static DocumentEngineException TooLarge() => new("INPUT_TOO_LARGE", "Review input byte limit exceeded");
}
