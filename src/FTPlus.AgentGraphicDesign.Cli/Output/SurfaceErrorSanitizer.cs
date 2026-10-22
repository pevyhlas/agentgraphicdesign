using System.Text.RegularExpressions;
using FTPlus.AgentGraphicDesign.Core;

namespace FTPlus.AgentGraphicDesign.Cli.Output;

internal static partial class SurfaceErrorSanitizer
{
    public static string Sanitize(string? message)
    {
        var sanitized = ErrorSanitizer.SanitizePaths(message);
        sanitized = WindowsAbsolutePath().Replace(sanitized, match => FileName(match.Value));
        return UnixAbsolutePath().Replace(sanitized, match => FileName(match.Value));
    }

    private static string FileName(string path)
    {
        var trimmed = path.TrimEnd('.', ',', ';', ':', ')', ']');
        var separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_:/.-])(?:[A-Za-z]:[/\\]|\\\\)[^\s\""'<>|\r\n]+")]
    private static partial Regex WindowsAbsolutePath();

    [GeneratedRegex(@"(?<![A-Za-z0-9_:/.-])/(?!/)[^\s\""'<>|\r\n]+")]
    private static partial Regex UnixAbsolutePath();
}
