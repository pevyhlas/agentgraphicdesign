using FTPlus.AgentGraphicDesign.Core.Documents;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>
/// A sequential render scope owning the call's loaded fonts. Each render has its own font
/// policy and diagnostics; FontDirectories must be empty. Dispose even when a render fails.
/// Sessions are not shared between calls or used concurrently with rendering/disposal.
/// </summary>
public interface IRenderSession : IDisposable
{
    RenderResult Render(Document document, RenderOptions options, CancellationToken cancellationToken = default);
}

internal sealed class ForwardingRenderSession(IRenderer renderer) : IRenderSession
{
    private bool _disposed;

    public RenderResult Render(Document document, RenderOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.FontDirectories);
        if (options.FontDirectories.Count != 0)
            throw new ArgumentException("Session renders cannot supply FontDirectories", nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        return renderer.Render(document, options, cancellationToken);
    }

    public void Dispose() => _disposed = true;
}
