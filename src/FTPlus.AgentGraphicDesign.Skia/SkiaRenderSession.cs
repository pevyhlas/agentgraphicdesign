using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;

namespace FTPlus.AgentGraphicDesign.Skia;

internal sealed class SkiaRenderSession : IRenderSession
{
    private readonly FontDirectoryCatalog _catalog;
    private bool _disposed;

    internal SkiaRenderSession(InspectedFontSet fonts, CancellationToken cancellationToken,
        Func<string, SKTypeface?>? loadTypeface = null) =>
        _catalog = FontDirectoryCatalog.FromInspected(fonts, cancellationToken, loadTypeface);

    public RenderResult Render(Document document, RenderOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.FontDirectories);
        if (options.FontDirectories.Count != 0)
            throw new ArgumentException("Session renders cannot supply FontDirectories", nameof(options));
        return SkiaRenderer.RenderCore(document, options, cancellationToken, _catalog);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _catalog.Dispose();
    }
}
