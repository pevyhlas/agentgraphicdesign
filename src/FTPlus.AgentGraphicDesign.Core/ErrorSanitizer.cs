using System.Text.RegularExpressions;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// Utilities for sanitizing error messages to avoid leaking sensitive information
/// such as absolute file paths.
/// </summary>
public static partial class ErrorSanitizer
{
    /// <summary>
    /// Sanitizes an error message by replacing absolute file paths with just filenames.
    /// </summary>
    /// <param name="message">The error message to sanitize.</param>
    /// <returns>The sanitized message with paths replaced by filenames.</returns>
    public static string SanitizePaths(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message ?? string.Empty;
        }

        // Replace Windows absolute paths (C:\path\to\file.txt or C:/path/to/file.txt)
        var result = WindowsPathRegex().Replace(message, match => ExtractFileName(match.Value));

        // Replace Unix absolute paths (/home/user/file.txt, /mnt/..., /tmp/..., etc.)
        result = UnixPathRegex().Replace(result, match => ExtractFileName(match.Value));

        return result;
    }

    /// <summary>
    /// Extracts the filename from a path, handling both Windows and Unix separators.
    /// This is platform-independent, unlike Path.GetFileName.
    /// </summary>
    private static string ExtractFileName(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        var lastBackslash = path.LastIndexOf('\\');
        var lastSeparator = Math.Max(lastSlash, lastBackslash);

        if (lastSeparator < 0 || lastSeparator == path.Length - 1)
        {
            return path;
        }

        return path[(lastSeparator + 1)..];
    }

    // Windows paths: drive letter followed by :\ or :/ then path segments
    [GeneratedRegex(@"[A-Za-z]:[/\\](?:[^""'<>|\r\n\s:*?]+[/\\])*[^""'<>|\r\n\s:*?]+")]
    private static partial Regex WindowsPathRegex();

    // Unix paths: starts with / followed by common root dirs and path segments
    [GeneratedRegex(@"/(?:home|mnt|tmp|var|usr|opt|etc|root)[/][^""'<>|\r\n\s:*?]+")]
    private static partial Regex UnixPathRegex();
}
