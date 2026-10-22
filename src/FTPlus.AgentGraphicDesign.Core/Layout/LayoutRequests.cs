namespace FTPlus.AgentGraphicDesign.Core.Layout;

public enum AlignMode
{
    Left,
    CenterX,
    Right,
    Top,
    CenterY,
    Bottom
}

public enum AlignReference
{
    Parent,
    Selection,
    Canvas
}

public enum DistributeAxis
{
    Horizontal,
    Vertical
}

public enum DistributeMode
{
    Gap,
    Centers
}

/// <summary>Input for <see cref="IDocumentEngine.AlignNodes"/>.</summary>
public sealed record AlignRequest
{
    public required IReadOnlyList<Guid> NodeIds { get; init; }

    public required AlignMode Mode { get; init; }

    /// <summary>Default: <c>Selection</c> for several nodes, <c>Parent</c> for a single node.</summary>
    public AlignReference? RelativeTo { get; init; }
}

/// <summary>Input for <see cref="IDocumentEngine.DistributeNodes"/>.</summary>
public sealed record DistributeRequest
{
    public required IReadOnlyList<Guid> NodeIds { get; init; }

    public required DistributeAxis Axis { get; init; }

    public DistributeMode Mode { get; init; } = DistributeMode.Gap;

    /// <summary>
    /// Explicit spacing. When omitted the first and last nodes stay fixed and the
    /// spacing is derived; when given, nodes are packed from the first node.
    /// </summary>
    public double? Gap { get; init; }
}

/// <summary>Input for <see cref="IDocumentEngine.FitFrame"/>.</summary>
public sealed record FitRequest
{
    public required Guid FrameId { get; init; }

    public double Padding { get; init; }
}

/// <summary>A node whose <c>transform.x</c>/<c>transform.y</c> were changed by a layout mutation.</summary>
public sealed record NodePlacement(
    Guid Id,
    string? Name,
    double PreviousX,
    double PreviousY,
    double X,
    double Y);

public sealed record AlignResult(
    AlignMode Mode,
    AlignReference RelativeTo,
    BoundingBox Reference,
    IReadOnlyList<NodePlacement> Nodes);

public sealed record DistributeResult(
    DistributeAxis Axis,
    DistributeMode Mode,
    double Gap,
    IReadOnlyList<NodePlacement> Nodes);

/// <summary>
/// <see cref="Previous"/> and <see cref="Frame"/> carry the frame's transform x/y and
/// its width/height before and after the fit.
/// </summary>
public sealed record FitResult(
    Guid FrameId,
    string? FrameName,
    BoundingBox Previous,
    BoundingBox Frame,
    IReadOnlyList<NodePlacement> Children);

/// <summary>String forms of the layout enums shared by the CLI and MCP surfaces.</summary>
public static class LayoutOptions
{
    public static readonly string[] AlignModes = ["left", "centerX", "right", "top", "centerY", "bottom"];
    public static readonly string[] AlignReferences = ["parent", "selection", "canvas"];
    public static readonly string[] DistributeAxes = ["horizontal", "vertical"];
    public static readonly string[] DistributeModes = ["gap", "centers"];

    public static bool TryParseAlignMode(string? value, out AlignMode mode)
    {
        mode = Normalize(value) switch
        {
            "left" => AlignMode.Left,
            "centerx" or "centrex" or "hcenter" or "middle" => AlignMode.CenterX,
            "right" => AlignMode.Right,
            "top" => AlignMode.Top,
            "centery" or "centrey" or "vcenter" => AlignMode.CenterY,
            "bottom" => AlignMode.Bottom,
            _ => (AlignMode)(-1)
        };
        return (int)mode >= 0;
    }

    public static bool TryParseAlignReference(string? value, out AlignReference reference)
    {
        reference = Normalize(value) switch
        {
            "parent" => AlignReference.Parent,
            "selection" => AlignReference.Selection,
            "canvas" => AlignReference.Canvas,
            _ => (AlignReference)(-1)
        };
        return (int)reference >= 0;
    }

    public static bool TryParseDistributeAxis(string? value, out DistributeAxis axis)
    {
        axis = Normalize(value) switch
        {
            "horizontal" or "h" or "x" => DistributeAxis.Horizontal,
            "vertical" or "v" or "y" => DistributeAxis.Vertical,
            _ => (DistributeAxis)(-1)
        };
        return (int)axis >= 0;
    }

    public static bool TryParseDistributeMode(string? value, out DistributeMode mode)
    {
        mode = Normalize(value) switch
        {
            "gap" or "gaps" => DistributeMode.Gap,
            "centers" or "center" or "centres" or "centre" => DistributeMode.Centers,
            _ => (DistributeMode)(-1)
        };
        return (int)mode >= 0;
    }

    public static string Name(AlignMode mode) => mode switch
    {
        AlignMode.Left => "left",
        AlignMode.CenterX => "centerX",
        AlignMode.Right => "right",
        AlignMode.Top => "top",
        AlignMode.CenterY => "centerY",
        AlignMode.Bottom => "bottom",
        _ => mode.ToString()
    };

    public static string Name(AlignReference reference) => reference.ToString().ToLowerInvariant();

    public static string Name(DistributeAxis axis) => axis.ToString().ToLowerInvariant();

    public static string Name(DistributeMode mode) => mode.ToString().ToLowerInvariant();

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
}
