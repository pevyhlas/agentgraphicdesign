using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Paths;

namespace FTPlus.AgentGraphicDesign.Cli.Infrastructure;

internal static class SurfaceIO
{
    public const int MaxNameLength = 256;
    public const int MaxPersistedValueLength = 1024;
    public const int MaxTextLength = 1_000_000;
    public const long MaxInputFileBytes = 16 * 1024 * 1024;
    public const int MaxRasterDimension = 2048;
    public const long MaxOutputBytes = 64 * 1024 * 1024;

    public static bool IsFinite(double value) => double.IsFinite(value);

    public static bool IsTooLong(string? value, int maximum) => value?.Length > maximum;

    public static string DisplayPath(string path)
    {
        var relative = PlatformPaths.RelativePath(Environment.CurrentDirectory, path);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static bool TryCheckFileSize(string path, long maximumBytes, out string? error)
    {
        path = PlatformPaths.FullPath(path);
        var length = new FileInfo(path).Length;
        if (length > maximumBytes)
        {
            error = $"Input file exceeds the {maximumBytes} byte limit";
            return false;
        }

        error = null;
        return true;
    }

    public static DocumentHandle? OpenDocument(
        IDocumentEngine engine,
        string locator,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(locator, out _))
        {
            if (!TryResolvePath(locator, out var path))
                return null;
            locator = path;
            if (File.Exists(locator) && new FileInfo(locator).Length > MaxInputFileBytes)
                throw new DocumentEngineException("INPUT_TOO_LARGE",
                    $"Document exceeds the {MaxInputFileBytes} byte limit");
        }

        var handle = engine.Open(locator, cancellationToken);
        if (handle?.SourcePath is not null && File.Exists(handle.SourcePath) &&
            new FileInfo(handle.SourcePath).Length > MaxInputFileBytes)
        {
            throw new DocumentEngineException("INPUT_TOO_LARGE",
                $"Document exceeds the {MaxInputFileBytes} byte limit");
        }

        return handle;
    }

    // Preserve callers' existing not-found envelopes for invalid lexical input, while
    // ensuring every successful existence/size/read check uses a canonical string.
    public static bool TryResolvePath(string input, out string path)
    {
        try
        {
            path = PlatformPaths.FullPath(input);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            path = string.Empty;
            return false;
        }
    }

    public static Task CreateDocumentFileAsync(
        IDocumentEngine engine,
        Document document,
        string path,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (force)
        {
            engine.SaveDocumentToFile(document, path, expectedRevn: null, cancellationToken);
            return Task.CompletedTask;
        }

        engine.CreateDocumentFile(document, path, cancellationToken);
        return Task.CompletedTask;
    }

    public static void Save(
        IDocumentEngine engine,
        DocumentHandle handle,
        long? expectedRevn,
        CancellationToken cancellationToken)
    {
        engine.Save(handle, expectedRevn, cancellationToken);
    }
}
