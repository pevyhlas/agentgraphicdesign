using FTPlus.AgentGraphicDesign.Core.Documents;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

public interface IRenderer
{
    RenderResult Render(Document document, RenderOptions options);

    /// <summary>
    /// Opens one sequential, call-scoped session after input validation. The caller disposes it
    /// on success, failure and cancellation. Renderers must override to consume nonempty sets.
    /// </summary>
    IRenderSession OpenSession(InspectedFontSet fonts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        cancellationToken.ThrowIfCancellationRequested();
        if (!fonts.Entries.IsEmpty)
            throw new NotSupportedException("This renderer does not support inspected font entries");
        return new ForwardingRenderSession(this);
    }

    RenderResult Render(Document document, RenderOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Render(document, options);
    }
}

public sealed record RenderOptions
{
    public double Scale { get; init; } = 1.0;

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? Background { get; init; }

    public RenderFormat Format { get; init; } = RenderFormat.Png;

    /// <summary>
    /// Directories whose <c>.ttf</c>/<c>.otf</c> files are searched before the bundled
    /// default family and before system fonts. Each directory is read once, without
    /// recursion and without following symbolic links.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>
    /// Whether host-installed fonts may satisfy a family that neither
    /// <see cref="FontDirectories"/> nor the bundled default family provides.
    /// <c>false</c> makes text output independent of the host.
    /// </summary>
    public bool SystemFonts { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, a requested family that cannot be resolved fails the render with
    /// <see cref="EngineErrors.FontNotFound"/> instead of substituting the bundled default
    /// family and reporting a <see cref="RenderDiagnosticCodes.FontSubstituted"/> diagnostic.
    /// </summary>
    public bool StrictFonts { get; init; }
}

/// <summary>
/// Output of <see cref="IRenderer.Render(FTPlus.AgentGraphicDesign.Core.Documents.Document, RenderOptions)"/>.
/// <see cref="Png"/>, <see cref="Jpeg"/> and <see cref="Webp"/> are encoded image files.
/// <see cref="Rgba8"/> is an internal raw buffer (see <see cref="RgbaImage"/>): it is never a file
/// format, and no export, preview or manifest path accepts or encodes it.
/// </summary>
public enum RenderFormat
{
    Png,
    Jpeg,
    Webp,

    /// <summary>
    /// Unpremultiplied sRGB RGBA, 8 bits per channel, row-major, top-down, no row padding:
    /// <c>Width·Height·4</c> bytes with MIME marker <see cref="RgbaImage.MimeType"/>.
    /// </summary>
    Rgba8
}

public sealed record RenderResult
{
    public required byte[] Data { get; init; }

    public required string MimeType { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Non-fatal notes about the render, such as font substitutions.</summary>
    public IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>A non-fatal, machine-readable note attached to a render result.</summary>
public sealed record RenderDiagnostic(string Code, string Message);

/// <summary>Stable diagnostic codes shared by all surfaces.</summary>
public static class RenderDiagnosticCodes
{
    /// <summary>The sub-node crop or visible geometry has a non-fatal bounds diagnostic.</summary>
    public const string BoundsDiagnostic = "BOUNDS_DIAGNOSTIC";

    /// <summary>A requested font family was unavailable and the bundled default family was drawn instead.</summary>
    public const string FontSubstituted = "FONT_SUBSTITUTED";

    /// <summary>A file in a font directory was ignored (symbolic link, oversized, or unreadable).</summary>
    public const string FontFileSkipped = "FONT_FILE_SKIPPED";

    /// <summary>Further diagnostics of the same kind were dropped after the per-render limit.</summary>
    public const string DiagnosticsTruncated = "DIAGNOSTICS_TRUNCATED";
}
