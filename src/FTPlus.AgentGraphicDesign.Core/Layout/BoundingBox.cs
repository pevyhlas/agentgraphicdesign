using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core.Layout;

/// <summary>
/// An axis-aligned rectangle. Used both for node-local extents and for world-space
/// bounding boxes after transforms.
/// </summary>
public readonly record struct BoundingBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public bool IsFinite =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height);

    public static BoundingBox FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    public static BoundingBox Union(BoundingBox a, BoundingBox b) => FromEdges(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Max(a.Right, b.Right),
        Math.Max(a.Bottom, b.Bottom));

    public static BoundingBox? Union(BoundingBox? a, BoundingBox? b) =>
        a is null ? b : b is null ? a : Union(a.Value, b.Value);

    /// <summary>Overlap of two boxes, or null when they do not overlap.</summary>
    public static BoundingBox? Intersect(BoundingBox a, BoundingBox b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right < left || bottom < top ? null : FromEdges(left, top, right, bottom);
    }

    public BoundingBox Round(int decimals) => new(
        Math.Round(X, decimals),
        Math.Round(Y, decimals),
        Math.Round(Width, decimals),
        Math.Round(Height, decimals));
}

/// <summary>
/// A 2D affine matrix in the same convention as the renderer: a local point
/// <c>p</c> maps to <c>(A·x + C·y + Tx, B·x + D·y + Ty)</c>.
/// </summary>
internal readonly record struct Affine(double A, double B, double C, double D, double Tx, double Ty)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Affine Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Clockwise rotation in a y-down coordinate system, matching <c>SKCanvas.RotateDegrees</c>.</summary>
    public static Affine Rotation(double degrees)
    {
        var (sin, cos) = ExactSinCos(degrees);
        return new Affine(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>
    /// The matrix a node's own transform contributes. The renderer applies translate,
    /// then rotate, then scale to the canvas, so a local point is scaled first, then
    /// rotated, then translated.
    /// </summary>
    public static Affine FromTransform(Transform? transform)
    {
        if (transform is null)
            return Identity;

        var matrix = Identity;
        if (transform.X != 0 || transform.Y != 0)
            matrix = matrix.Multiply(Translation(transform.X, transform.Y));
        if (transform.Rotation != 0)
            matrix = matrix.Multiply(Rotation(transform.Rotation));
        if (transform.ScaleX != 1 || transform.ScaleY != 1)
            matrix = matrix.Multiply(Scale(transform.ScaleX, transform.ScaleY));
        return matrix;
    }

    /// <summary>Returns <c>this ∘ other</c>: <paramref name="other"/> is applied first.</summary>
    public Affine Multiply(Affine other) => new(
        A * other.A + C * other.B,
        B * other.A + D * other.B,
        A * other.C + C * other.D,
        B * other.C + D * other.D,
        A * other.Tx + C * other.Ty + Tx,
        B * other.Tx + D * other.Ty + Ty);

    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + Tx, B * x + D * y + Ty);

    /// <summary>Applies only the linear part (no translation) to a direction vector.</summary>
    public (double X, double Y) ApplyVector(double x, double y) => (A * x + C * y, B * x + D * y);

    /// <summary>Inverts the linear part so a world-space offset can be expressed in local units.</summary>
    public bool TryInvertVector(double x, double y, out double localX, out double localY)
    {
        var determinant = A * D - B * C;
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            localX = localY = 0;
            return false;
        }

        localX = (D * x - C * y) / determinant;
        localY = (A * y - B * x) / determinant;
        return double.IsFinite(localX) && double.IsFinite(localY);
    }

    /// <summary>Axis-aligned bounding box of the four transformed corners.</summary>
    public BoundingBox TransformBox(BoundingBox box)
    {
        var (x0, y0) = Apply(box.X, box.Y);
        var (x1, y1) = Apply(box.Right, box.Y);
        var (x2, y2) = Apply(box.Right, box.Bottom);
        var (x3, y3) = Apply(box.X, box.Bottom);
        return BoundingBox.FromEdges(
            Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)),
            Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)),
            Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)),
            Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
    }

    private static (double Sin, double Cos) ExactSinCos(double degrees)
    {
        var normalized = degrees % 360;
        if (normalized < 0)
            normalized += 360;
        return normalized switch
        {
            0 => (0, 1),
            90 => (1, 0),
            180 => (0, -1),
            270 => (-1, 0),
            _ => (Math.Sin(degrees * Math.PI / 180), Math.Cos(degrees * Math.PI / 180))
        };
    }
}
