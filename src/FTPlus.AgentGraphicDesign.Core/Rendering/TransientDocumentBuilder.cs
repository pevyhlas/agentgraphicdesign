using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

internal enum TransientCrop { Canvas, Ink }
internal enum TransientBackground { Source, None }

internal sealed record TransientMeasurement(BoundingBox? Bounds, IReadOnlyList<RenderDiagnostic> Diagnostics);

/// <summary>
/// One synchronized copy per build. Measurement is supplied by the engine; this builder does
/// no rendering, font IO or storage. Ink means the legacy D-10 geometry crop, not expanded stroke ink.
/// </summary>
internal sealed class TransientDocumentBuilder(
    Func<Document, Guid?, FontPolicy, CancellationToken, TransientMeasurement> measure,
    Func<Document, Document>? copyDocument = null)
{
    public TransientDocument Build(Document source, Guid? nodeId = null,
        TransientCrop crop = TransientCrop.Canvas, FontPolicy? fontPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        Document copy;
        lock (DocumentSynchronization.For(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            copy = copyDocument is null
                ? DocumentSerializer.Deserialize(DocumentSerializer.Serialize(source))!
                : copyDocument(source);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sourceBackground = copy.Canvas.Background;
        var index = DocumentIndex.Build(copy);
        var node = nodeId is { } id
            ? index.Find(id) ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {id}")
            : copy.Root;

        // Selected nodes are isolated from ancestors, exactly as in D-10. Tokens belong to
        // the private snapshot; the probe and resulting viewport reuse that snapshot's nodes.
        var probe = nodeId is null ? copy : new Document
        {
            Id = Guid.NewGuid(),
            Name = copy.Name,
            Tokens = copy.Tokens,
            Canvas = new Canvas { Width = copy.Canvas.Width, Height = copy.Canvas.Height },
            Root = new FrameNode
            {
                ClipContent = false,
                Width = copy.Canvas.Width,
                Height = copy.Canvas.Height,
                Children = [node]
            }
        };
        // Hidden geometry can affect the legacy crop. Only the renderer decides whether a
        // missing rendered font is fatal; measuring that hidden geometry must stay non-strict.
        var policy = (fontPolicy ?? FontPolicy.Default) with { StrictFonts = false };
        var measurement = measure(probe, crop == TransientCrop.Ink ? node.Id : null, policy, cancellationToken);
        var box = new BoundingBox(0, 0, copy.Canvas.Width, copy.Canvas.Height);
        if (crop == TransientCrop.Ink)
        {
            if (measurement.Bounds is not { Width: > 0, Height: > 0 } measured)
                throw new DocumentEngineException(EngineErrors.NodeNoBounds, $"Node {node.Id} has no visible bounds to export");
            box = measured;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (nodeId is not null || crop == TransientCrop.Ink)
        {
            copy = new Document
            {
                Id = Guid.NewGuid(),
                Name = copy.Name,
                Tokens = copy.Tokens,
                Canvas = new Canvas { Width = box.Width, Height = box.Height },
                Root = new FrameNode
                {
                    ClipContent = false,
                    Width = box.Width,
                    Height = box.Height,
                    Children = [new GroupNode
                    {
                        Transform = new Transform { X = -box.X, Y = -box.Y }, Children = [node]
                    }]
                }
            };
        }
        return new TransientDocument(copy, sourceBackground, box, measurement.Diagnostics, cancellationToken);
    }
}

/// <summary>Owned mutable snapshot. Masks and background changes never copy or mutate the source.</summary>
internal sealed class TransientDocument
{
    private readonly Dictionary<Guid, (Node Node, bool Visible)> _authored = [];
    private readonly string? _sourceBackground;

    internal TransientDocument(Document document, string? sourceBackground, BoundingBox cropBounds,
        IReadOnlyList<RenderDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        Document = document;
        _sourceBackground = sourceBackground;
        CropBounds = cropBounds;
        Diagnostics = diagnostics;
        Capture(document.Root, 0);

        void Capture(Node node, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > BoundsCalculator.MaxDepth || _authored.Count >= BoundsCalculator.MaxNodeCount)
                throw new DocumentEngineException(EngineErrors.ResourceLimit, "Transient document exceeds node or depth limits");
            _authored.Add(node.Id, (node, node.Visible));
            if (node is IContainerNode container)
                foreach (var child in container.Children)
                    Capture(child, depth + 1);
        }
    }

    public Document Document { get; }
    public BoundingBox CropBounds { get; }
    public IReadOnlyList<RenderDiagnostic> Diagnostics { get; }

    public void SetBackground(TransientBackground background)
    {
        if (!Enum.IsDefined(background))
            throw new ArgumentOutOfRangeException(nameof(background));
        Document.Canvas.Background = background == TransientBackground.Source ? _sourceBackground : null;
    }

    public void ApplyMask(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ids?.Count > BoundsCalculator.MaxNodeCount)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "Transient mask exceeds the node limit");
        HashSet<Guid>? selected = ids?.ToHashSet();
        if (selected is not null)
            foreach (var id in selected)
                if (!_authored.ContainsKey(id))
                    throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {id}");

        // Stage before applying: cancellation or a bad id leaves the previous mask intact.
        var visibility = new Dictionary<Guid, bool>();
        Visit(Document.Root, selected is null);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var (id, visible) in visibility)
            _authored[id].Node.Visible = visible;

        bool Visit(Node node, bool retained)
        {
            cancellationToken.ThrowIfCancellationRequested();
            retained |= selected?.Contains(node.Id) == true;
            var keep = retained;
            if (node is IContainerNode container)
                foreach (var child in container.Children)
                    keep |= Visit(child, retained);
            visibility.Add(node.Id, keep && _authored[node.Id].Visible);
            return keep;
        }
    }
}
