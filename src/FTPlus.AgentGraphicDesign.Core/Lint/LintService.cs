using System.Globalization;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Lint;

/// <summary>
/// Design lints over one document. Lint is separate from validation: it never blocks
/// export and reports only <see cref="LintSeverity.Warning"/> and
/// <see cref="LintSeverity.Info"/>. The walk mirrors the renderer: nodes under a hidden
/// or fully transparent container are skipped, style falls back to the nearest ancestor
/// container's style, and geometry is measured through <see cref="BoundsCalculator"/>.
/// </summary>
internal sealed class LintService
{
    public const int MaxDiagnostics = 10_000;
    public const int MaxUnusedTokenDiagnostics = 100;
    public const int MaxOffGridFieldsListed = 8;

    /// <summary>WCAG 2.x AA thresholds and the large-text cut-offs (24 px, or 18.66 px at weight 700).</summary>
    public const double NormalTextMinimumContrast = 4.5;
    public const double LargeTextMinimumContrast = 3.0;
    public const double LargeTextFontSize = 24;
    public const double LargeTextBoldFontSize = 18.66;
    public const int LargeTextBoldWeight = 700;

    public const double HairlineThresholdPixels = 1.0;

    /// <summary>Canvases whose larger dimension is at most this are treated as icons for the grid rule.</summary>
    public const double IconCanvasSize = 64;

    public const double MaxTargetSize = 1_000_000;

    private const double GridTolerance = 1e-6;
    private const double EdgeTolerance = 1e-6;

    private readonly Document _document;
    private readonly BoundsCalculator _bounds;
    private readonly TokenService _tokens;
    private readonly LintOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly List<LintDiagnostic> _diagnostics = [];
    private readonly List<BackgroundLayer> _backgrounds = [];
    private readonly List<(Node Node, SrgbColor Color)> _iconFills = [];
    private readonly BoundingBox _canvasBox;
    private readonly double _targetSize;
    private readonly double _targetScale;
    private readonly bool _gridChecked;
    private bool _truncated;

    public LintService(
        Document document,
        BoundsCalculator bounds,
        TokenService tokens,
        LintOptions options,
        CancellationToken cancellationToken)
    {
        _document = document;
        _bounds = bounds;
        _tokens = tokens;
        _options = options;
        _cancellationToken = cancellationToken;

        if (!Enum.IsDefined(options.Profile))
            throw new DocumentEngineException(EngineErrors.InvalidArguments, "Unknown lint profile");
        if (options.TargetSize is { } requested && (!double.IsFinite(requested) || requested <= 0 || requested > MaxTargetSize))
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Target size must be finite and between 0 and {MaxTargetSize.ToString(CultureInfo.InvariantCulture)}");
        }

        var canvas = document.Canvas;
        _canvasBox = new BoundingBox(0, 0, canvas.Width, canvas.Height);
        var larger = Math.Max(canvas.Width, canvas.Height);
        var hasCanvas = double.IsFinite(larger) && larger > 0;
        _targetSize = options.TargetSize ?? (hasCanvas ? larger : 0);
        _targetScale = hasCanvas && options.TargetSize is { } target ? target / larger : 1;
        _gridChecked = options.Profile == ValidationProfile.WebIcon || (hasCanvas && larger <= IconCanvasSize);
    }

    public LintResult Run()
    {
        Walk(_document.Root, new Scope(null, 1, null, false, 0));
        ReportUnusedTokens();
        ReportSymbolReadiness();

        return new LintResult
        {
            Diagnostics = _diagnostics,
            Profile = _options.Profile,
            TargetSize = _targetSize,
            StrictGrid = _options.StrictGrid,
            GridChecked = _gridChecked,
            Truncated = _truncated
        };
    }

    // ---- Traversal ----

    private void Walk(Node node, Scope scope)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var isRoot = ReferenceEquals(node, _document.Root);
        var container = node as IContainerNode;

        if (!node.Visible)
        {
            if (container is { Children.Count: > 0 })
            {
                Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                    $"{Describe(node)} is hidden but still holds {container.Children.Count} child node(s)",
                    Details(("reason", "hidden-container"), ("childCount", container.Children.Count)));
            }
            return;
        }

        var ownOpacity = node.Style?.Opacity ?? 1;
        if (ownOpacity <= 0)
        {
            Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                $"{Describe(node)} has opacity 0 and cannot be seen",
                Details(("reason", "opacity-zero")));
            return;
        }

        var opacity = scope.Opacity * Math.Clamp(ownOpacity, 0, 1);
        var effectiveStyle = node.Style ?? scope.InheritedStyle;

        if (!isRoot)
        {
            CheckInvisibleGeometry(node);
            CheckHairlineStroke(node, effectiveStyle);
            if (_gridChecked)
                CheckGrid(node);
            var skipSubtree = CheckOutsideCanvas(node, scope);
            if (skipSubtree)
                return;
        }

        switch (node)
        {
            case TextNode text:
                CheckTextContrast(text, effectiveStyle, opacity);
                break;
            case RectangleNode rectangle:
                RecordBackground(rectangle, _bounds.WorldBounds(rectangle), effectiveStyle, opacity, scope);
                RecordIconFill(rectangle, effectiveStyle);
                break;
            case EllipseNode or PathNode or PolygonNode:
                RecordIconFill(node, effectiveStyle);
                break;
        }

        if (container is null)
            return;

        var childScope = scope with
        {
            InheritedStyle = node.Style ?? scope.InheritedStyle,
            Opacity = opacity,
            Depth = scope.Depth + 1
        };

        if (node is FrameNode frame)
        {
            var frameBox = _bounds.ReferenceBounds(frame);
            if (!isRoot && frame.ClipContent && frameBox is { } clipBox)
            {
                var clip = scope.ClipEmpty
                    ? null
                    : scope.Clip is { } existing ? BoundingBox.Intersect(existing, clipBox) : clipBox;
                childScope = childScope with { Clip = clip, ClipEmpty = clip is null };
            }
        }

        // Frames pass styles to children but do not paint a background themselves.
        foreach (var child in container.Children.OrderBy(child => child.Order, StringComparer.Ordinal))
            Walk(child, childScope);
    }

    // ---- LINT001: text contrast ----

    private void CheckTextContrast(TextNode text, Style? effectiveStyle, double opacity)
    {
        if (string.IsNullOrWhiteSpace(text.Text))
            return;
        if (!TryResolveFill(effectiveStyle, out var fill) || fill.Kind != FillKind.Solid)
            return;

        var ink = fill.Color.WithAlpha(fill.Color.A * opacity);
        if (ink.IsTransparent)
            return;

        var box = _bounds.WorldBounds(text);
        if (box is not { } textBox || !textBox.IsFinite)
            return;

        if (!TryResolveBackground(textBox, out var background, out var source, out var sourceNode))
            return;

        // Container opacity applies to the composited children, not each paint independently.
        // Skip shared translucent layers until background resolution models those layers.
        if (sourceNode is not null && opacity < 1)
        {
            var translucentAncestors = new HashSet<Guid>();
            for (var ancestor = _bounds.Index.ParentOf(text.Id) as Node;
                 ancestor is not null;
                 ancestor = _bounds.Index.ParentOf(ancestor.Id) as Node)
            {
                if (ancestor.Style is { Opacity: > 0 and < 1 })
                    translucentAncestors.Add(ancestor.Id);
            }

            for (var ancestor = _bounds.Index.ParentOf(sourceNode.Id) as Node;
                 ancestor is not null;
                 ancestor = _bounds.Index.ParentOf(ancestor.Id) as Node)
            {
                if (translucentAncestors.Contains(ancestor.Id))
                    return;
            }
        }

        var (fontSize, fontWeight) = ResolveTypography(text);
        var large = fontSize >= LargeTextFontSize ||
            (fontSize >= LargeTextBoldFontSize && fontWeight >= LargeTextBoldWeight);
        var threshold = large ? LargeTextMinimumContrast : NormalTextMinimumContrast;
        var ratio = WcagContrast.Ratio(ink, background);
        if (ratio >= threshold)
            return;

        var composited = ink.IsOpaque ? ink : ink.Over(background);
        Add(LintCodes.TextContrast, LintSeverity.Warning, text,
            $"{Describe(text)} contrast {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1 " +
            $"({composited.ToHex()} on {background.ToHex()}) is below {Format(threshold)}:1" +
            (large ? " for large text" : ""),
            Details(
                ("ratio", Math.Round(ratio, 1)),
                ("threshold", threshold),
                ("foreground", composited.ToHex()),
                ("background", background.ToHex()),
                ("backgroundSource", source),
                ("backgroundNodeId", sourceNode?.Id),
                ("fontSize", fontSize),
                ("fontWeight", fontWeight),
                ("largeText", large)));
    }

    /// <summary>
    /// The color underneath <paramref name="box"/>: the canvas background (white when
    /// unset) with every earlier-painted rectangle that fully contains
    /// the box composited on top, in paint order. False when a containing layer is a
    /// gradient, because the contrast is then not a single number.
    /// </summary>
    private bool TryResolveBackground(BoundingBox box, out SrgbColor background, out string source, out Node? sourceNode)
    {
        source = "default";
        sourceNode = null;
        background = SrgbColor.White;
        if (_document.Canvas.Background is { } canvasColor && SrgbColor.TryParse(canvasColor, out var parsedCanvas))
        {
            background = parsedCanvas.IsOpaque ? parsedCanvas : parsedCanvas.Over(SrgbColor.White);
            source = "canvas";
        }

        foreach (var layer in _backgrounds)
        {
            if (!Contains(layer.Box, box))
                continue;
            if (layer.Fill.Kind == FillKind.Gradient)
                return false;

            background = layer.Fill.Color.WithAlpha(layer.Fill.Color.A * layer.Opacity).Over(background);
            source = layer.Source;
            sourceNode = layer.Node;
        }

        return true;
    }

    private void RecordBackground(Node node, BoundingBox? worldBox, Style? effectiveStyle, double opacity, Scope scope)
    {
        if (worldBox is not { } box || !box.IsFinite)
            return;
        if (!TryResolveFill(effectiveStyle, out var fill) || fill.Kind == FillKind.None)
            return;

        // A rectangle inside a clipping frame only covers what the frame lets through.
        if (scope.ClipEmpty)
            return;
        if (scope.Clip is { } clip)
        {
            var visible = BoundingBox.Intersect(clip, box);
            if (visible is null)
                return;
            box = visible.Value;
        }

        _backgrounds.Add(new BackgroundLayer(node, box, fill, opacity, "rectangle"));
    }

    // ---- LINT002: hairline strokes ----

    private void CheckHairlineStroke(Node node, Style? effectiveStyle)
    {
        if (!TryGetRenderedStroke(node, effectiveStyle, out var width))
            return;
        if (!double.IsFinite(width) || width <= 0)
            return;

        var matrix = _bounds.WorldMatrix(node);
        var nodeScale = Math.Sqrt(Math.Abs(matrix.A * matrix.D - matrix.B * matrix.C));
        if (!double.IsFinite(nodeScale) || nodeScale <= 0)
            return;

        var rendered = width * nodeScale * _targetScale;
        if (!double.IsFinite(rendered) || rendered >= HairlineThresholdPixels)
            return;

        Add(LintCodes.HairlineStroke, LintSeverity.Warning, node,
            $"{Describe(node)} stroke width {Format(width)} renders as {rendered.ToString("0.00", CultureInfo.InvariantCulture)} px " +
            $"at target size {Format(_targetSize)}; strokes below 1 px look broken or vanish",
            Details(
                ("strokeWidth", width),
                ("renderedWidth", Math.Round(rendered, 3)),
                ("targetSize", _targetSize),
                ("scale", Math.Round(nodeScale * _targetScale, 6))));
    }

    /// <summary>Mirrors the renderer's decision of whether a stroke is drawn, and at which width.</summary>
    private static bool TryGetRenderedStroke(Node node, Style? effectiveStyle, out double width)
    {
        width = 0;
        switch (node)
        {
            case FrameNode or GroupNode or ImageNode or UnknownNode:
                return false;
            case LineNode when effectiveStyle is null:
            case PolygonNode { Closed: false } when effectiveStyle is null:
                width = 1;
                return true;
        }

        var stroke = effectiveStyle?.Stroke;
        if (stroke is null || (stroke.Color is null && stroke.TokenRef is null))
            return false;
        width = stroke.Width;
        return true;
    }

    // ---- LINT003: off-grid icon geometry ----

    private void CheckGrid(Node node)
    {
        var step = _options.StrictGrid ? 1.0 : 0.5;
        var fields = new List<(string Name, double Value)>();

        if (node.Transform is { } transform)
        {
            Check("transform.x", transform.X);
            Check("transform.y", transform.Y);
        }

        switch (node)
        {
            case RectangleNode rectangle:
                Check("width", rectangle.Width);
                Check("height", rectangle.Height);
                break;
            case EllipseNode ellipse:
                Check("width", ellipse.Width);
                Check("height", ellipse.Height);
                break;
            case FrameNode frame:
                Check("width", frame.Width);
                Check("height", frame.Height);
                break;
            case ImageNode image:
                Check("width", image.Width);
                Check("height", image.Height);
                break;
            case LineNode line:
                Check("x1", line.X1);
                Check("y1", line.Y1);
                Check("x2", line.X2);
                Check("y2", line.Y2);
                break;
            case PolygonNode polygon:
                for (var i = 0; i < polygon.Points.Count; i++)
                {
                    Check($"points[{i}].x", polygon.Points[i].X);
                    Check($"points[{i}].y", polygon.Points[i].Y);
                }
                break;
        }

        if (fields.Count == 0)
            return;

        var listed = fields.Take(MaxOffGridFieldsListed)
            .Select(field => $"{field.Name}={Format(field.Value)}");
        var suffix = fields.Count > MaxOffGridFieldsListed ? $", +{fields.Count - MaxOffGridFieldsListed} more" : "";
        Add(LintCodes.OffGrid, _options.StrictGrid ? LintSeverity.Warning : LintSeverity.Info, node,
            $"{Describe(node)} is off the {Format(step)} px grid: {string.Join(", ", listed)}{suffix}",
            Details(
                ("grid", step),
                ("fields", fields.Take(MaxOffGridFieldsListed).Select(field => new { name = field.Name, value = field.Value }).ToArray()),
                ("count", fields.Count)));

        void Check(string name, double value)
        {
            if (!double.IsFinite(value))
                return;
            var steps = value / step;
            if (Math.Abs(steps - Math.Round(steps)) > GridTolerance)
                fields.Add((name, value));
        }
    }

    // ---- LINT004: outside the canvas ----

    /// <summary>Returns true when the node's subtree can be skipped (a clipping frame that is fully outside).</summary>
    private bool CheckOutsideCanvas(Node node, Scope scope)
    {
        if (node is GroupNode)
            return false;

        var world = node is FrameNode frame ? _bounds.ReferenceBounds(frame) : _bounds.WorldBounds(node);
        if (world is not { } box || !box.IsFinite || !_canvasBox.IsFinite)
            return false;

        var clippedBy = (Guid?)null;
        if (scope.ClipEmpty)
            return false;
        if (scope.Clip is { } clip)
        {
            var visible = BoundingBox.Intersect(clip, box);
            if (visible is null)
                return false;
            if (visible != box)
                clippedBy = FindClippingAncestor(node);
            box = visible.Value;
        }

        var left = Math.Max(0, -box.X);
        var top = Math.Max(0, -box.Y);
        var right = Math.Max(0, box.Right - _canvasBox.Width);
        var bottom = Math.Max(0, box.Bottom - _canvasBox.Height);
        if (left <= EdgeTolerance && top <= EdgeTolerance && right <= EdgeTolerance && bottom <= EdgeTolerance)
            return false;

        var fullyOutside = box.Right <= EdgeTolerance || box.Bottom <= EdgeTolerance ||
            box.X >= _canvasBox.Width - EdgeTolerance || box.Y >= _canvasBox.Height - EdgeTolerance;
        var details = Details(
            ("world", new { x = box.X, y = box.Y, width = box.Width, height = box.Height }),
            ("canvas", new { width = _canvasBox.Width, height = _canvasBox.Height }),
            ("overflow", new { left, top, right, bottom }),
            ("fullyOutside", fullyOutside),
            ("clippedBy", clippedBy));

        if (fullyOutside)
        {
            Add(LintCodes.OutsideCanvas, LintSeverity.Warning, node,
                $"{Describe(node)} lies entirely outside the {Format(_canvasBox.Width)}x{Format(_canvasBox.Height)} canvas and will not appear in exports",
                details);
            return node is FrameNode { ClipContent: true };
        }

        Add(LintCodes.OutsideCanvas, LintSeverity.Info, node,
            $"{Describe(node)} extends past the canvas edge (left {Format(left)}, top {Format(top)}, right {Format(right)}, bottom {Format(bottom)}) and will be cropped",
            details);
        return false;
    }

    private Guid? FindClippingAncestor(Node node)
    {
        var parent = _bounds.Index.ParentOf(node.Id);
        while (parent is Node ancestor)
        {
            if (ancestor is FrameNode { ClipContent: true } && !ReferenceEquals(ancestor, _document.Root))
                return ancestor.Id;
            parent = _bounds.Index.ParentOf(ancestor.Id);
        }
        return null;
    }

    // ---- LINT005: unused tokens ----

    private void ReportUnusedTokens()
    {
        var colors = _document.Tokens?.Colors;
        var typography = _document.Tokens?.Typography;
        if (colors is not { Count: > 0 } && typography is not { Count: > 0 })
            return;

        var usedColors = new HashSet<string>(StringComparer.Ordinal);
        var usedTypography = new HashSet<string>(StringComparer.Ordinal);
        CollectTokenUsage(_document.Root, usedColors, usedTypography);

        // Aliases: a token used through another token counts as used.
        if (colors is not null)
        {
            var pending = new Queue<string>(usedColors);
            while (pending.Count > 0)
            {
                var name = pending.Dequeue();
                if (!colors.TryGetValue(name, out var value) || value is null)
                    continue;
                var target = TokenTarget(value, colors);
                if (target is not null && usedColors.Add(target))
                    pending.Enqueue(target);
            }
        }

        var reported = 0;
        var skipped = 0;
        if (colors is not null)
        {
            foreach (var name in colors.Keys.Order(StringComparer.Ordinal))
            {
                if (usedColors.Contains(name))
                    continue;
                ReportUnused("color", name, ref reported, ref skipped);
            }
        }

        if (typography is not null)
        {
            foreach (var name in typography.Keys.Order(StringComparer.Ordinal))
            {
                if (usedTypography.Contains(name))
                    continue;
                ReportUnused("typography", name, ref reported, ref skipped);
            }
        }

        if (skipped > 0)
        {
            Add(LintCodes.UnusedToken, LintSeverity.Info, null,
                $"{skipped} more token(s) are referenced by no node",
                Details(("skipped", skipped)));
        }
    }

    private void ReportUnused(string category, string name, ref int reported, ref int skipped)
    {
        if (reported >= MaxUnusedTokenDiagnostics)
        {
            skipped++;
            return;
        }

        reported++;
        Add(LintCodes.UnusedToken, LintSeverity.Info, null,
            $"{Capitalize(category)} token '{name}' is referenced by no node",
            Details(("category", category), ("name", name)));
    }

    private void CollectTokenUsage(Node node, HashSet<string> colors, HashSet<string> typography)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (node.Style is { } style)
        {
            NoteColor(style.Fill?.TokenRef, colors);
            NoteColor(style.Fill?.Color, colors);
            NoteColor(style.Stroke?.TokenRef, colors);
            NoteColor(style.Stroke?.Color, colors);
            if (style.Fill?.Gradient is { } gradient)
            {
                foreach (var stop in gradient.Stops)
                    NoteColor(stop.Color, colors);
            }
        }

        if (node is TextNode { TypographyTokenRef: { } reference })
        {
            var name = TokenName(reference);
            if (name is not null)
                typography.Add(name);
        }

        if (node is IContainerNode container)
        {
            foreach (var child in container.Children)
                CollectTokenUsage(child, colors, typography);
        }
    }

    private void NoteColor(string? value, HashSet<string> colors)
    {
        if (value is null || DocumentLimits.IsValidColor(value))
            return;
        var name = TokenTarget(value, _document.Tokens?.Colors);
        if (name is not null)
            colors.Add(name);
    }

    private static string? TokenName(string reference) =>
        DocumentLimits.TryGetTokenReference(reference, out var braced)
            ? braced
            : DocumentLimits.IsValidTokenName(reference) ? reference : null;

    /// <summary>The token a value points at: a braced reference always, a bare name only when it exists.</summary>
    private static string? TokenTarget(string value, IReadOnlyDictionary<string, string>? colors)
    {
        if (DocumentLimits.TryGetTokenReference(value, out var braced))
            return braced;
        return DocumentLimits.IsValidTokenName(value) && colors?.ContainsKey(value) == true ? value : null;
    }

    // ---- LINT006: invisible content ----

    private void CheckInvisibleGeometry(Node node)
    {
        switch (node)
        {
            case RectangleNode { Width: <= 0 } or RectangleNode { Height: <= 0 }:
            case EllipseNode { Width: <= 0 } or EllipseNode { Height: <= 0 }:
            case ImageNode { Width: <= 0 } or ImageNode { Height: <= 0 }:
                Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                    $"{Describe(node)} has a zero-size box and draws nothing",
                    Details(("reason", "zero-size")));
                break;
            case FrameNode { ClipContent: true, Children.Count: > 0 } frame when frame.Width <= 0 || frame.Height <= 0:
                Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                    $"{Describe(node)} is a zero-size clipping frame and hides its {frame.Children.Count} child node(s)",
                    Details(("reason", "zero-size"), ("childCount", frame.Children.Count)));
                break;
            case LineNode line when line.X1 == line.X2 && line.Y1 == line.Y2:
                Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                    $"{Describe(node)} has zero length and draws nothing",
                    Details(("reason", "zero-length")));
                break;
            case PolygonNode polygon when polygon.Points.Count < 2 || IsZeroArea(polygon):
                Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                    $"{Describe(node)} has no extent ({polygon.Points.Count} point(s)) and draws nothing",
                    Details(("reason", "zero-size"), ("pointCount", polygon.Points.Count)));
                break;
            case TextNode text when string.IsNullOrWhiteSpace(text.Text):
                Add(LintCodes.InvisibleContent, LintSeverity.Info, node,
                    $"{Describe(node)} has no text",
                    Details(("reason", "empty-text")));
                break;
        }
    }

    private static bool IsZeroArea(PolygonNode polygon)
    {
        var first = polygon.Points[0];
        return polygon.Points.All(point => point.X == first.X && point.Y == first.Y);
    }

    // ---- LINT007: symbol readiness (web-icon only) ----

    private void RecordIconFill(Node node, Style? effectiveStyle)
    {
        if (_options.Profile != ValidationProfile.WebIcon)
            return;
        if (node is PolygonNode { Closed: false } && effectiveStyle?.Fill is null)
            return;
        if (!TryResolveFill(effectiveStyle, out var fill))
            return;

        switch (fill.Kind)
        {
            case FillKind.Gradient:
                Add(LintCodes.SymbolReadiness, LintSeverity.Info, node,
                    $"{Describe(node)} uses a gradient fill, which a currentColor export cannot recolor",
                    Details(("fill", "gradient")));
                break;
            case FillKind.Solid when !fill.Color.IsTransparent:
                _iconFills.Add((node, fill.Color));
                break;
        }
    }

    private void ReportSymbolReadiness()
    {
        if (_options.Profile != ValidationProfile.WebIcon || _iconFills.Count == 0)
            return;

        var distinct = _iconFills.Select(item => item.Color.WithAlpha(1).ToHex()).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count <= 1)
            return;

        foreach (var (node, color) in _iconFills)
        {
            var hex = color.WithAlpha(1).ToHex();
            if (hex == "#000000")
                continue;
            Add(LintCodes.SymbolReadiness, LintSeverity.Info, node,
                $"{Describe(node)} fill {hex} is not black while the icon mixes {distinct.Count} fill colors; a currentColor export recolors every fill the same",
                Details(("fill", hex), ("distinctFills", distinct.Count)));
        }
    }

    // ---- Style resolution (mirrors SkiaRenderer) ----

    private bool TryResolveFill(Style? style, out ResolvedFill fill)
    {
        if (style is null)
        {
            fill = new ResolvedFill(FillKind.Solid, SrgbColor.Black);
            return true;
        }

        if (style.Fill is null)
        {
            fill = ResolvedFill.None;
            return true;
        }

        if (style.Fill.Gradient is not null)
        {
            fill = new ResolvedFill(FillKind.Gradient, default);
            return true;
        }

        if (style.Fill is { Color: null, TokenRef: null })
        {
            fill = ResolvedFill.None;
            return true;
        }

        var value = ResolvePaint(style.Fill.TokenRef, style.Fill.Color);
        if (value is null)
        {
            fill = ResolvedFill.None;
            return true;
        }

        fill = new ResolvedFill(FillKind.Solid, value.Value);
        return true;
    }

    /// <summary>Token-resolved paint as a color; null for <c>none</c>/<c>transparent</c>; black for anything unparsable.</summary>
    private SrgbColor? ResolvePaint(string? tokenReference, string? color)
    {
        var candidate = tokenReference ?? color;
        if (candidate is null)
            return SrgbColor.Black;

        var resolved = _tokens.ResolveColorRefWithDiagnostics(_document, candidate);
        string? paint = null;
        if (resolved is { Success: true, Value: { } value })
            paint = value;
        else if (tokenReference is not null && color is not null && !color.StartsWith('{'))
            paint = color;

        if (paint is null)
            return SrgbColor.Black;
        var trimmed = paint.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return SrgbColor.TryParse(trimmed, out var parsed) ? parsed : SrgbColor.Black;
    }

    private (double FontSize, int FontWeight) ResolveTypography(TextNode text)
    {
        var fontSize = text.FontSize;
        var fontWeight = text.FontWeight;
        if (text.TypographyTokenRef is { } reference)
        {
            var resolved = _tokens.ResolveTypographyRefWithDiagnostics(_document, reference);
            if (resolved is { Success: true, Value: { } typography })
            {
                fontSize = typography.FontSize ?? fontSize;
                fontWeight = typography.FontWeight ?? fontWeight;
            }
        }
        return (fontSize, fontWeight);
    }

    // ---- Helpers ----

    private void Add(string code, LintSeverity severity, Node? node, string message, IReadOnlyDictionary<string, object?> details)
    {
        if (_diagnostics.Count >= MaxDiagnostics)
        {
            _truncated = true;
            return;
        }

        _diagnostics.Add(new LintDiagnostic
        {
            Code = code,
            Severity = severity,
            Message = message,
            NodeId = node?.Id,
            Details = details
        });
    }

    private static IReadOnlyDictionary<string, object?> Details(params (string Key, object? Value)[] entries)
    {
        var details = new Dictionary<string, object?>(entries.Length, StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null)
                details[key] = value;
        }
        return details;
    }

    private static string Describe(Node node)
    {
        var type = Capitalize(DocumentEngine.GetNodeTypeName(node));
        return string.IsNullOrEmpty(node.Name) ? $"{type} {node.Id}" : $"{type} '{node.Name}'";
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool Contains(BoundingBox outer, BoundingBox inner) =>
        inner.X >= outer.X - EdgeTolerance &&
        inner.Y >= outer.Y - EdgeTolerance &&
        inner.Right <= outer.Right + EdgeTolerance &&
        inner.Bottom <= outer.Bottom + EdgeTolerance;

    private enum FillKind
    {
        None,
        Solid,
        Gradient
    }

    private readonly record struct ResolvedFill(FillKind Kind, SrgbColor Color)
    {
        public static readonly ResolvedFill None = new(FillKind.None, default);
    }

    /// <summary>A painted area that can sit behind text, in paint order.</summary>
    private sealed record BackgroundLayer(Node Node, BoundingBox Box, ResolvedFill Fill, double Opacity, string Source);

    /// <summary>
    /// Per-subtree state: the style children inherit, the accumulated container opacity,
    /// and the intersection of every non-root clipping frame above (null = unclipped;
    /// <see cref="ClipEmpty"/> = clipped to nothing).
    /// </summary>
    private readonly record struct Scope(Style? InheritedStyle, double Opacity, BoundingBox? Clip, bool ClipEmpty, int Depth);
}
