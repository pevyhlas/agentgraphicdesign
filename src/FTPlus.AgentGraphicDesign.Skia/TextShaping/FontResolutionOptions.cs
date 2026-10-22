using FTPlus.AgentGraphicDesign.Core.Rendering;

namespace FTPlus.AgentGraphicDesign.Skia.TextShaping;

/// <summary>
/// Font resolution policy for one render: explicit <see cref="FontDirectories"/> are
/// searched first, then the bundled <see cref="BundledFonts.FamilyName"/> family, then
/// host-installed fonts when <see cref="SystemFonts"/> allows them.
/// </summary>
public sealed record FontResolutionOptions
{
    /// <summary>Upper bound on the number of directories a single render may search.</summary>
    public const int MaxFontDirectories = 16;

    /// <summary>A directory holding more <c>.ttf</c>/<c>.otf</c> files than this is refused.</summary>
    public const int MaxFilesPerDirectory = 256;

    /// <summary>Font files larger than this are skipped with a diagnostic.</summary>
    public const long MaxFontFileBytes = 8 * 1024 * 1024;

    public static FontResolutionOptions Default { get; } = new();

    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    public bool SystemFonts { get; init; } = true;

    public bool StrictFonts { get; init; }

    public static FontResolutionOptions From(RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new FontResolutionOptions
        {
            FontDirectories = options.FontDirectories ?? [],
            SystemFonts = options.SystemFonts,
            StrictFonts = options.StrictFonts
        };
    }

    /// <summary>Maps an engine-level <see cref="FontPolicy"/> onto the shaping font options.</summary>
    public static FontResolutionOptions From(FontPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new FontResolutionOptions
        {
            FontDirectories = policy.FontDirectories ?? [],
            SystemFonts = policy.SystemFonts,
            StrictFonts = policy.StrictFonts
        };
    }
}

/// <summary>Where a resolved typeface came from.</summary>
public enum FontSource
{
    /// <summary>A file from one of the caller-supplied font directories.</summary>
    Directory,

    /// <summary>The Inter faces embedded in this assembly.</summary>
    Bundled,

    /// <summary>A host-installed font matched through the platform font manager.</summary>
    System
}

/// <summary>
/// Outcome of resolving a (family, weight) request. <see cref="SubstitutedFamily"/> is the
/// requested family when it could not be found and the bundled family was used instead.
/// </summary>
public sealed record FontResolution(FontSource Source, string FamilyName, int Weight, string? SubstitutedFamily)
{
    public bool Substituted => SubstitutedFamily is not null;
}
