using System.Runtime.InteropServices;
using FTPlus.AgentGraphicDesign.Core.Paths;

namespace FTPlus.AgentGraphicDesign.Mcp;

/// <summary>Confines user-visible paths to one repository root and rejects link traversal.</summary>
public sealed class PathConfinement
{
    public const int MaxPathLength = 4096;

    private readonly string _workingDirectory;
    private readonly string _rootPrefix;
    private readonly StringComparison _comparison;

    public PathConfinement(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory cannot be empty", nameof(workingDirectory));
        }

        _workingDirectory = Path.TrimEndingDirectorySeparator(PlatformPaths.FullPath(workingDirectory));
        _rootPrefix = _workingDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _workingDirectory
            : _workingDirectory + Path.DirectorySeparatorChar;
        _comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (TryGetAttributes(_workingDirectory, out var rootAttributes) &&
            rootAttributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ArgumentException("Working directory cannot be a symbolic link or reparse point", nameof(workingDirectory));
        }
    }

    public string WorkingDirectory => _workingDirectory;

    public PathConfinementResult Confine(string userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
        {
            return PathConfinementResult.Fail("Path cannot be empty");
        }

        if (userPath.Length > MaxPathLength)
        {
            return PathConfinementResult.Fail($"Path exceeds the {MaxPathLength} character limit");
        }

        try
        {
            var absolutePath = PlatformPaths.FullPath(Path.IsPathRooted(userPath)
                ? userPath
                : Path.Combine(_workingDirectory, userPath));

            if (!IsWithinRoot(absolutePath))
            {
                return PathConfinementResult.Fail("Path is outside the working directory");
            }

            var reparseError = FindReparsePoint(absolutePath);
            if (reparseError is not null)
            {
                return PathConfinementResult.Fail(reparseError);
            }

            return PathConfinementResult.Ok(absolutePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return PathConfinementResult.Fail("Path is invalid");
        }
        catch (UnauthorizedAccessException)
        {
            return PathConfinementResult.Fail("Path is invalid", "ACCESS_DENIED");
        }
        catch (IOException)
        {
            return PathConfinementResult.Fail("Path is invalid", "IO_ERROR");
        }
    }

    public string RelativePath(string confinedPath)
    {
        var relative = PlatformPaths.RelativePath(_workingDirectory, confinedPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static bool IsGuid(string value) => Guid.TryParse(value, out _);

    private bool IsWithinRoot(string path) =>
        path.Equals(_workingDirectory, _comparison) || path.StartsWith(_rootPrefix, _comparison);

    private string? FindReparsePoint(string absolutePath)
    {
        // Include ancestors of the configured root: canonicalization grants only verified
        // system aliases, never an arbitrary link above an otherwise ordinary root.
        var current = Path.GetPathRoot(absolutePath)!;
        var relative = PlatformPaths.RelativePath(current, absolutePath);
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!TryGetAttributes(current, out var attributes))
            {
                break;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return "Path contains a symbolic link or reparse point";
            }
        }

        return null;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }
}

public sealed class PathConfinementResult
{
    private PathConfinementResult(string? path, string? error, string? errorCode)
    {
        Path = path;
        Error = error;
        ErrorCode = errorCode;
    }

    public string? Path { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }
    public bool Success => Path is not null && Error is null;

    public static PathConfinementResult Ok(string path) => new(path, null, null);
    public static PathConfinementResult Fail(string error, string errorCode = "PATH_ESCAPE") => new(null, error, errorCode);
}
