using FTPlus.AgentGraphicDesign.Core.Documents;

namespace FTPlus.AgentGraphicDesign.Core.Export;

public interface IExportAdapter
{
    string Id { get; }

    ExportCapabilities Capabilities { get; }

    Task<ExportResult> ExportAsync(Document document, ExportOptions options, CancellationToken ct = default);
}

[Flags]
public enum ExportCapabilities
{
    None = 0,
    Vector = 1 << 0,
    Raster = 1 << 1,
    Streaming = 1 << 2
}

public sealed record ExportOptions
{
    public string Format { get; init; } = "svg";

    public double Scale { get; init; } = 1.0;

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? Background { get; init; }

    public bool AllowExternalResources { get; init; }

    /// <summary>
    /// When set, SVG export emits a reusable <c>symbol</c> fragment with this id
    /// (viewBox only, no XML declaration or svg root) for sprite assembly instead of
    /// a standalone document. Requires <see cref="Scale"/> 1.0 and no
    /// <see cref="Width"/>/<see cref="Height"/> overrides; ignored by raster formats.
    /// </summary>
    public string? SymbolId { get; init; }

    /// <summary>
    /// When set, SVG export writes <c>currentColor</c> in place of any node fill or stroke
    /// color that resolves (after token resolution) to this color. Gradient stops and the
    /// canvas background are not rewritten. SVG only; raster surfaces reject the option.
    /// </summary>
    public string? CurrentColor { get; init; }

    /// <summary>
    /// Directories whose <c>.ttf</c>/<c>.otf</c> files are searched before the bundled
    /// default family and before system fonts when an adapter rasterizes text. SVG
    /// export references families by name and does not resolve fonts.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>
    /// Whether host-installed fonts may satisfy a family that neither
    /// <see cref="FontDirectories"/> nor the bundled default family provides when an
    /// adapter rasterizes text.
    /// </summary>
    public bool SystemFonts { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, a raster adapter fails with <see cref="EngineErrors.FontNotFound"/>
    /// for an unresolved family instead of substituting the bundled default family.
    /// </summary>
    public bool StrictFonts { get; init; }
}

public sealed record ExportResult
{
    public required byte[] Data { get; init; }

    public required string MimeType { get; init; }

    public string? FileName { get; init; }
}
