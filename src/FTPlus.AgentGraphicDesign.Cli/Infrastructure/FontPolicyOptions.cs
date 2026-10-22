using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;

namespace FTPlus.AgentGraphicDesign.Cli.Infrastructure;

/// <summary>
/// Parses the shared <c>--font-dir</c> / <c>--no-system-fonts</c> / <c>--strict-fonts</c>
/// options into a <see cref="FontPolicy"/> for the measurement surfaces (bounds, lint, contact
/// sheet) so they resolve fonts exactly as a raster render would. Returns a null policy when no
/// font option is given, so the engine reuses its shared default measurer.
/// </summary>
public static class FontPolicyOptions
{
    public readonly record struct Result(bool Success, FontPolicy? Policy, string? ErrorCode, string? ErrorMessage);

    public static Result Resolve(string[]? fontDirs, bool noSystemFonts, bool strictFonts)
    {
        var directories = fontDirs ?? [];
        if (directories.Length > FontResolutionOptions.MaxFontDirectories)
        {
            return new Result(false, null, EngineErrors.InvalidArguments,
                $"At most {FontResolutionOptions.MaxFontDirectories} --font-dir values are allowed");
        }
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                return new Result(false, null, EngineErrors.InvalidArguments, $"Font directory not found: {directory}");
            }
        }

        // No font option means the default policy; return null so the engine keeps its shared measurer.
        if (directories.Length == 0 && !noSystemFonts && !strictFonts)
        {
            return new Result(true, null, null, null);
        }

        return new Result(true, new FontPolicy
        {
            FontDirectories = directories,
            SystemFonts = !noSystemFonts,
            StrictFonts = strictFonts
        }, null, null);
    }
}
