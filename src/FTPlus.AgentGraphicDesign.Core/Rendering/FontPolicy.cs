namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>
/// The font-resolution policy a surface asks for: explicit <see cref="FontDirectories"/>
/// are searched first, then the bundled default family, then host-installed fonts when
/// <see cref="SystemFonts"/> allows. <see cref="StrictFonts"/> turns an unresolved family
/// into a hard failure instead of a bundled substitution.
/// </summary>
/// <remarks>
/// This is the engine-level policy that lets measurement, layout, lint, and contact sheets
/// resolve fonts exactly as a raster render would, so measured/wrapped text matches rendered
/// text. The Skia project maps it onto its own font-resolution options; the core never loads
/// fonts itself. It mirrors the font fields on <see cref="RenderOptions"/> so a single policy
/// describes both a measurement and the render it is checked against.
/// </remarks>
public sealed record FontPolicy
{
    /// <summary>The host-independent default: bundled family plus host fonts, non-strict.</summary>
    public static FontPolicy Default { get; } = new();

    /// <summary>
    /// Directories whose <c>.ttf</c>/<c>.otf</c> files are searched before the bundled default
    /// family and before system fonts. Each directory is read once, without recursion.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>
    /// Whether host-installed fonts may satisfy a family that neither <see cref="FontDirectories"/>
    /// nor the bundled default family provides. <c>false</c> makes text independent of the host.
    /// </summary>
    public bool SystemFonts { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, a requested family that cannot be resolved fails with
    /// <see cref="EngineErrors.FontNotFound"/> instead of substituting the bundled default family.
    /// </summary>
    public bool StrictFonts { get; init; }
}
