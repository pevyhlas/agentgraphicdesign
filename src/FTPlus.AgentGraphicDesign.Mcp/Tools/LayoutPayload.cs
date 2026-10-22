using FTPlus.AgentGraphicDesign.Core.Layout;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>Result shapes for the bounds and alignment tools, mirroring the CLI JSON output.</summary>
internal static class LayoutPayload
{
    public static object Box(BoundingBox box) =>
        new { x = box.X, y = box.Y, width = box.Width, height = box.Height };

    public static object? Box(BoundingBox? box) => box is { } value ? Box(value) : null;

    public static object Node(NodeBounds bounds) => new
    {
        id = bounds.Id,
        type = bounds.Type,
        name = bounds.Name,
        visible = bounds.Visible,
        local = Box(bounds.Local),
        world = Box(bounds.World),
        measured = bounds.Measured,
        clipped = bounds.Clipped,
        diagnostics = bounds.Diagnostics
    };

    public static object Placement(NodePlacement placement) => new
    {
        id = placement.Id,
        name = placement.Name,
        previousX = placement.PreviousX,
        previousY = placement.PreviousY,
        x = placement.X,
        y = placement.Y
    };
}
