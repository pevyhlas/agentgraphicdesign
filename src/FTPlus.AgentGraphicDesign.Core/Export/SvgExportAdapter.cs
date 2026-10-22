using System.Globalization;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Tokens;

namespace FTPlus.AgentGraphicDesign.Core.Export;

public sealed class SvgExportAdapter : IExportAdapter
{
    private readonly ITextMeasurer _textMeasurer;

    /// <param name="textMeasurer">
    /// Measures lines when a text node sets <c>maxWidth</c>; the glyph-count estimator is
    /// used when omitted. Documents without wrapped text produce the same SVG either way.
    /// </param>
    public SvgExportAdapter(ITextMeasurer? textMeasurer = null)
    {
        _textMeasurer = textMeasurer ?? EstimatedTextMeasurer.Instance;
    }

    public const double MaxScale = 1_000;
    public const double MaxOutputDimension = 1_000_000;
    public const int MaxOutputCharacters = 16_000_000;
    public const int MaxNodeCount = 10_000;
    public const int MaxTraversalDepth = 128;
    public const int MaxPathDataCharacters = 256_000;
    public const int MaxTextCharacters = 64_000;
    public const int MaxNameCharacters = 4096;
    public const int MaxSymbolIdCharacters = 128;
    public const int MaxPolygonPoints = 250_000;
    public const int MaxDashEntries = 10_000;
    public const int MaxGradientStops = 10_000;
    public const int MaxTotalPathDataCharacters = 1_000_000;
    public const int MaxTotalTextCharacters = 1_000_000;
    public const int MaxTotalPolygonPoints = 250_000;

    private static readonly HashSet<string> SupportedBlendModes =
    [
        "normal", "multiply", "screen", "overlay", "darken", "lighten",
        "color-dodge", "color-burn", "hard-light", "soft-light", "difference",
        "exclusion", "hue", "saturation", "color", "luminosity"
    ];

    public string Id => "svg";
    public ExportCapabilities Capabilities => ExportCapabilities.Vector;

    public Task<ExportResult> ExportAsync(Document document, ExportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<ExportResult>(ct);

        var svg = GenerateSvg(document, options, ct);
        ct.ThrowIfCancellationRequested();
        var data = Encoding.UTF8.GetBytes(svg);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(new ExportResult
        {
            Data = data,
            MimeType = "image/svg+xml",
            FileName = options.SymbolId is null ? $"{document.Name}.svg" : $"{document.Name}.symbol.svg"
        });
    }

    private string GenerateSvg(Document document, ExportOptions options, CancellationToken ct)
    {
        if (options.SymbolId is not null)
            return GenerateSymbolFragment(document, options, ct);

        var width = options.Width ?? document.Canvas.Width;
        var height = options.Height ?? document.Canvas.Height;
        ValidateOutputOptions(width, height, options.Scale);

        var context = new SvgRenderContext(document, options, _textMeasurer, ct);
        CollectGradients(document.Root, context, 0);

        var outputWidth = width * options.Scale;
        var outputHeight = height * options.Scale;
        var builder = new SvgOutputBuilder(MaxOutputCharacters);
        AppendLine(builder, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        AppendLine(builder, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Format(outputWidth)}\" height=\"{Format(outputHeight)}\" viewBox=\"0 0 {Format(width)} {Format(height)}\">");

        if (context.Gradients.Count > 0)
        {
            AppendLine(builder, "  <defs>");
            foreach (var (id, gradient) in context.Gradients.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                context.CheckCancellation();
                RenderGradientDef(builder, id, gradient, "    ", context);
            }
            AppendLine(builder, "  </defs>");
        }

        var background = options.Background ?? document.Canvas.Background;
        if (background is not null)
        {
            background = ResolveColor(context, null, background, "#000000");
            AppendLine(builder, $"  <rect width=\"100%\" height=\"100%\" fill=\"{EscapeXml(background)}\"/>");
        }

        RenderNode(builder, document.Root, "  ", context, 0);
        AppendLine(builder, "</svg>");
        ct.ThrowIfCancellationRequested();
        return builder.ToString();
    }

    /// <summary>
    /// Emits a reusable sprite fragment: a bare <c>symbol</c> element with the document's
    /// canvas as its viewBox. No XML declaration and no svg root — the caller nests the
    /// fragment inside a sprite sheet's <c>defs</c>. Sizing options are rejected rather
    /// than ignored because a symbol has no intrinsic size (viewBox only).
    /// </summary>
    private string GenerateSymbolFragment(Document document, ExportOptions options, CancellationToken ct)
    {
        var symbolId = options.SymbolId!;
        if (!IsValidSymbolId(symbolId))
        {
            throw new ArgumentException(
                $"Symbol id must start with a letter, use only letters, digits, '-' or '_', and be at most {MaxSymbolIdCharacters} characters; got \"{symbolId}\"");
        }
        if (options.Scale != 1.0 || options.Width is not null || options.Height is not null)
            throw new ArgumentException("Symbol export does not accept scale, width, or height overrides; a symbol has no intrinsic size");

        var width = document.Canvas.Width;
        var height = document.Canvas.Height;
        ValidateOutputOptions(width, height, scale: 1.0);

        var context = new SvgRenderContext(document, options, _textMeasurer, ct);
        CollectGradients(document.Root, context, 0);

        var builder = new SvgOutputBuilder(MaxOutputCharacters);
        AppendLine(builder, $"<symbol id=\"{symbolId}\" viewBox=\"0 0 {Format(width)} {Format(height)}\">");

        if (context.Gradients.Count > 0)
        {
            AppendLine(builder, "  <defs>");
            foreach (var (id, gradient) in context.Gradients.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                context.CheckCancellation();
                RenderGradientDef(builder, id, gradient, "    ", context);
            }
            AppendLine(builder, "  </defs>");
        }

        var background = options.Background ?? document.Canvas.Background;
        if (background is not null)
        {
            background = ResolveColor(context, null, background, "#000000");
            AppendLine(builder, $"  <rect width=\"100%\" height=\"100%\" fill=\"{EscapeXml(background)}\"/>");
        }

        RenderNode(builder, document.Root, "  ", context, 0);
        AppendLine(builder, "</symbol>");
        ct.ThrowIfCancellationRequested();
        return builder.ToString();
    }

    public static bool IsValidSymbolId(string? symbolId)
    {
        if (string.IsNullOrEmpty(symbolId) || symbolId.Length > MaxSymbolIdCharacters)
            return false;
        if (!char.IsAsciiLetter(symbolId[0]))
            return false;
        foreach (var ch in symbolId)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_'))
                return false;
        }
        return true;
    }

    private static void ValidateOutputOptions(double width, double height, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 || scale > MaxScale)
            throw new ArgumentOutOfRangeException(nameof(scale), $"SVG scale must be finite and in the range (0, {MaxScale.ToString(CultureInfo.InvariantCulture)}]");
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentException($"SVG dimensions must be finite and positive; got {FormatForError(width)}x{FormatForError(height)}");
        if (width > MaxOutputDimension || height > MaxOutputDimension
            || width * scale > MaxOutputDimension || height * scale > MaxOutputDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                $"SVG dimensions after scaling must not exceed {MaxOutputDimension.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static void CollectGradients(Node node, SvgRenderContext context, int depth)
    {
        context.EnterCollection(node, depth);
        try
        {
            if (node.Style?.Fill?.Gradient is { } gradient)
            {
                context.ChargeGradientStops(gradient.Stops.Count);
                context.Gradients[$"grad-{node.Id:N}"] = gradient;
            }
            if (node is IContainerNode container)
            {
                context.ValidateChildCount(container.Children.Count);
                foreach (var child in container.Children)
                    CollectGradients(child, context, depth + 1);
            }
        }
        finally
        {
            context.ExitCollection(node);
        }
    }

    private static void RenderGradientDef(SvgOutputBuilder builder, string id, Gradient gradient, string indent, SvgRenderContext context)
    {
        switch (gradient)
        {
            case LinearGradient linear:
                AppendLine(builder, $"{indent}<linearGradient id=\"{id}\" x1=\"{Format(linear.X1 * 100)}%\" y1=\"{Format(linear.Y1 * 100)}%\" x2=\"{Format(linear.X2 * 100)}%\" y2=\"{Format(linear.Y2 * 100)}%\">");
                RenderGradientStops(builder, linear.Stops, indent + "  ", context);
                AppendLine(builder, $"{indent}</linearGradient>");
                break;
            case RadialGradient radial:
                builder.Append($"{indent}<radialGradient id=\"{id}\" cx=\"{Format(radial.Cx * 100)}%\" cy=\"{Format(radial.Cy * 100)}%\" r=\"{Format(radial.R * 100)}%\"");
                if (radial.Fx.HasValue)
                    builder.Append($" fx=\"{Format(radial.Fx.Value * 100)}%\"");
                if (radial.Fy.HasValue)
                    builder.Append($" fy=\"{Format(radial.Fy.Value * 100)}%\"");
                AppendLine(builder, ">");
                RenderGradientStops(builder, radial.Stops, indent + "  ", context);
                AppendLine(builder, $"{indent}</radialGradient>");
                break;
        }
    }

    private static void RenderGradientStops(SvgOutputBuilder builder, List<GradientStop> stops, string indent, SvgRenderContext context)
    {
        foreach (var stop in stops)
        {
            context.CheckCancellation();
            var color = ResolveColor(context, null, stop.Color, "#000000");
            builder.Append($"{indent}<stop offset=\"{Format(stop.Offset * 100)}%\" stop-color=\"{EscapeXml(color)}\"");
            if (stop.Opacity != 1)
                builder.Append($" stop-opacity=\"{Format(stop.Opacity)}\"");
            AppendLine(builder, "/>");
        }
    }

    private static void RenderNode(SvgOutputBuilder builder, Node node, string indent, SvgRenderContext context, int depth)
    {
        context.EnterRender(node, depth);
        try
        {
            if (!node.Visible)
                return;
            context.ValidateName(node.Name);
            switch (node)
            {
                case FrameNode frame:
                    RenderFrame(builder, frame, indent, context, depth);
                    break;
                case GroupNode group:
                    RenderGroup(builder, group, indent, context, depth);
                    break;
                case RectangleNode rectangle:
                    RenderRectangle(builder, rectangle, indent, context);
                    break;
                case EllipseNode ellipse:
                    RenderEllipse(builder, ellipse, indent, context);
                    break;
                case PathNode path:
                    RenderPath(builder, path, indent, context);
                    break;
                case TextNode text:
                    RenderText(builder, text, indent, context);
                    break;
                case LineNode line:
                    RenderLine(builder, line, indent, context);
                    break;
                case PolygonNode polygon:
                    RenderPolygon(builder, polygon, indent, context);
                    break;
                case ImageNode image:
                    RenderImage(builder, image, indent, context);
                    break;
            }
        }
        finally
        {
            context.ExitRender(node);
        }
    }

    private static void RenderFrame(SvgOutputBuilder builder, FrameNode frame, string indent, SvgRenderContext context, int depth)
    {
        var transform = GetTransformAttribute(frame.Transform);
        var style = GetStyleAttributes(frame.Style, frame.Id, context, PrimitiveDefault.None);
        var clipId = frame.ClipContent ? $"clip-{frame.Id:N}" : null;
        if (clipId is not null)
        {
            AppendLine(builder, $"{indent}<defs>");
            AppendLine(builder, $"{indent}  <clipPath id=\"{clipId}\">");
            AppendLine(builder, $"{indent}    <rect width=\"{Format(frame.Width)}\" height=\"{Format(frame.Height)}\"/>");
            AppendLine(builder, $"{indent}  </clipPath>");
            AppendLine(builder, $"{indent}</defs>");
        }

        builder.Append($"{indent}<g");
        AppendCommonGroupAttributes(builder, frame.Name, transform, style);
        if (clipId is not null)
            builder.Append($" clip-path=\"url(#{clipId})\"");
        AppendLine(builder, ">");
        context.ValidateChildCount(frame.Children.Count);
        foreach (var child in frame.Children.OrderBy(child => child.Order, StringComparer.Ordinal))
            RenderNode(builder, child, indent + "  ", context, depth + 1);
        AppendLine(builder, $"{indent}</g>");
    }

    private static void RenderGroup(SvgOutputBuilder builder, GroupNode group, string indent, SvgRenderContext context, int depth)
    {
        var transform = GetTransformAttribute(group.Transform);
        var style = GetStyleAttributes(group.Style, group.Id, context, PrimitiveDefault.None);
        builder.Append($"{indent}<g");
        AppendCommonGroupAttributes(builder, group.Name, transform, style);
        AppendLine(builder, ">");
        context.ValidateChildCount(group.Children.Count);
        foreach (var child in group.Children.OrderBy(child => child.Order, StringComparer.Ordinal))
            RenderNode(builder, child, indent + "  ", context, depth + 1);
        AppendLine(builder, $"{indent}</g>");
    }

    private static void AppendCommonGroupAttributes(SvgOutputBuilder builder, string? name, string? transform, string? style)
    {
        if (!string.IsNullOrEmpty(name))
            builder.Append($" data-name=\"{EscapeXml(name)}\"");
        if (!string.IsNullOrEmpty(transform))
            builder.Append($" transform=\"{transform}\"");
        if (!string.IsNullOrEmpty(style))
            builder.Append($" {style}");
    }

    private static void RenderRectangle(SvgOutputBuilder builder, RectangleNode rectangle, string indent, SvgRenderContext context)
    {
        builder.Append($"{indent}<rect");
        AppendName(builder, rectangle.Name);
        builder.Append($" width=\"{Format(rectangle.Width)}\" height=\"{Format(rectangle.Height)}\"");
        if (rectangle.CornerRadius > 0)
            builder.Append($" rx=\"{Format(rectangle.CornerRadius)}\"");
        AppendTransformAndStyle(builder, rectangle.Transform, GetStyleAttributes(rectangle.Style, rectangle.Id, context, PrimitiveDefault.Filled));
        AppendLine(builder, "/>");
    }

    private static void RenderEllipse(SvgOutputBuilder builder, EllipseNode ellipse, string indent, SvgRenderContext context)
    {
        builder.Append($"{indent}<ellipse");
        AppendName(builder, ellipse.Name);
        builder.Append($" cx=\"{Format(ellipse.Width / 2)}\" cy=\"{Format(ellipse.Height / 2)}\" rx=\"{Format(ellipse.Width / 2)}\" ry=\"{Format(ellipse.Height / 2)}\"");
        AppendTransformAndStyle(builder, ellipse.Transform, GetStyleAttributes(ellipse.Style, ellipse.Id, context, PrimitiveDefault.Filled));
        AppendLine(builder, "/>");
    }

    private static void RenderPath(SvgOutputBuilder builder, PathNode path, string indent, SvgRenderContext context)
    {
        context.ChargePath(path.D);
        builder.Append($"{indent}<path");
        AppendName(builder, path.Name);
        builder.Append($" d=\"{EscapeXml(path.D)}\"");
        AppendTransformAndStyle(builder, path.Transform, GetStyleAttributes(path.Style, path.Id, context, PrimitiveDefault.Filled, emitFillRule: true));
        AppendLine(builder, "/>");
    }

    private static void RenderText(SvgOutputBuilder builder, TextNode text, string indent, SvgRenderContext context)
    {
        context.ChargeText(text.Text);
        TypographyToken? token = null;
        if (text.TypographyTokenRef is { } reference)
        {
            var resolved = context.TokenService.ResolveTypographyRefWithDiagnostics(context.Document, reference);
            if (resolved is { Success: true, Value: { } typography })
                token = typography;
        }

        var style = TextLayout.Resolve(text, token);
        var fontSize = style.FontSize;
        var lines = TextLayout.BreakLines(text.Text, style.MaxWidth, line => context.MeasureWidth(style, line));
        var verticalOffset = TextLayout.VerticalOffset(style.VerticalAlign, lines.Count, fontSize, style.LineHeight);

        builder.Append($"{indent}<text");
        AppendName(builder, text.Name);
        builder.Append($" y=\"{Format(fontSize + verticalOffset)}\" font-size=\"{Format(fontSize)}\"");
        if (!string.IsNullOrEmpty(style.FontFamily))
        {
            context.ValidateFontFamily(style.FontFamily);
            builder.Append($" font-family=\"{EscapeXml(style.FontFamily)}\"");
        }
        builder.Append($" font-weight=\"{style.FontWeight}\"");
        if (TextLayout.ToSvgTextAnchor(style.TextAlign) is { } anchor)
            builder.Append($" text-anchor=\"{anchor}\"");
        if (style.LetterSpacing != 0)
            builder.Append($" letter-spacing=\"{Format(style.LetterSpacing)}\"");
        AppendTransformAndStyle(builder, text.Transform, GetStyleAttributes(text.Style, text.Id, context, PrimitiveDefault.Filled));
        builder.Append('>');
        if (lines.Count == 1)
        {
            builder.Append(EscapeXml(lines[0]));
        }
        else
        {
            // One tspan per line, all anchored at x = 0 so text-anchor applies per line. An
            // empty line has no glyph to carry dy, so its pitch rolls into the next line.
            var pitch = TextLayout.LinePitch(fontSize, style.LineHeight);
            var pendingShift = 0d;
            for (var index = 0; index < lines.Count; index++)
            {
                context.CheckCancellation();
                var shift = (index == 0 ? 0 : pitch) + pendingShift;
                if (lines[index].Length == 0)
                {
                    pendingShift = shift;
                    continue;
                }

                builder.Append($"<tspan x=\"0\" dy=\"{Format(shift)}\">{EscapeXml(lines[index])}</tspan>");
                pendingShift = 0;
            }
        }
        AppendLine(builder, "</text>");
    }

    private static void RenderLine(SvgOutputBuilder builder, LineNode line, string indent, SvgRenderContext context)
    {
        builder.Append($"{indent}<line");
        AppendName(builder, line.Name);
        builder.Append($" x1=\"{Format(line.X1)}\" y1=\"{Format(line.Y1)}\" x2=\"{Format(line.X2)}\" y2=\"{Format(line.Y2)}\"");
        AppendTransformAndStyle(builder, line.Transform, GetStyleAttributes(line.Style, line.Id, context, PrimitiveDefault.Stroked));
        AppendLine(builder, "/>");
    }

    private static void RenderPolygon(SvgOutputBuilder builder, PolygonNode polygon, string indent, SvgRenderContext context)
    {
        context.ChargePoints(polygon.Points.Count);
        var elementName = polygon.Closed ? "polygon" : "polyline";
        builder.Append($"{indent}<{elementName}");
        AppendName(builder, polygon.Name);
        builder.Append(" points=\"");
        for (var index = 0; index < polygon.Points.Count; index++)
        {
            context.CheckCancellation();
            if (index > 0)
                builder.Append(' ');
            builder.Append($"{Format(polygon.Points[index].X)},{Format(polygon.Points[index].Y)}");
        }
        builder.Append('"');
        var defaults = polygon.Closed ? PrimitiveDefault.Filled : PrimitiveDefault.Stroked;
        AppendTransformAndStyle(builder, polygon.Transform, GetStyleAttributes(polygon.Style, polygon.Id, context, defaults, emitFillRule: true));
        AppendLine(builder, "/>");
    }

    private static void RenderImage(SvgOutputBuilder builder, ImageNode image, string indent, SvgRenderContext context)
    {
        var source = context.GetSafeImageHref(image.Src);
        builder.Append($"{indent}<image");
        AppendName(builder, image.Name);
        builder.Append($" href=\"{EscapeXml(source)}\" width=\"{Format(image.Width)}\" height=\"{Format(image.Height)}\" preserveAspectRatio=\"none\"");
        AppendTransformAndStyle(builder, image.Transform, GetStyleAttributes(image.Style, image.Id, context, PrimitiveDefault.None));
        AppendLine(builder, "/>");
    }

    private static void AppendName(SvgOutputBuilder builder, string? name)
    {
        if (!string.IsNullOrEmpty(name))
            builder.Append($" data-name=\"{EscapeXml(name)}\"");
    }

    private static void AppendTransformAndStyle(SvgOutputBuilder builder, Transform? transform, string? style)
    {
        var transformAttribute = GetTransformAttribute(transform);
        if (!string.IsNullOrEmpty(transformAttribute))
            builder.Append($" transform=\"{transformAttribute}\"");
        if (!string.IsNullOrEmpty(style))
            builder.Append($" {style}");
    }

    private static string? GetTransformAttribute(Transform? transform)
    {
        if (transform is null)
            return null;
        var parts = new List<string>();
        if (transform.X != 0 || transform.Y != 0)
            parts.Add($"translate({Format(transform.X)}, {Format(transform.Y)})");
        if (transform.Rotation != 0)
            parts.Add($"rotate({Format(transform.Rotation)})");
        if (transform.ScaleX != 1 || transform.ScaleY != 1)
            parts.Add($"scale({Format(transform.ScaleX)}, {Format(transform.ScaleY)})");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string? GetStyleAttributes(
        Style? style,
        Guid nodeId,
        SvgRenderContext context,
        PrimitiveDefault primitiveDefault,
        bool emitFillRule = false)
    {
        if (style is null && primitiveDefault == PrimitiveDefault.None)
            return null;

        var parts = new List<string>();
        if (style?.Fill?.Gradient is not null)
        {
            parts.Add($"fill=\"url(#grad-{nodeId:N})\"");
        }
        else if (style?.Fill is { } fill)
        {
            var fillColor = context.ApplyCurrentColor(ResolveColor(context, fill.TokenRef, fill.Color, "#000000"));
            parts.Add($"fill=\"{EscapeXml(fillColor)}\"");
        }
        else
        {
            parts.Add(style is null && primitiveDefault == PrimitiveDefault.Filled
                ? $"fill=\"{context.ApplyCurrentColor("#000000")}\""
                : "fill=\"none\"");
        }

        // fill-rule is only meaningful on self-intersecting geometry, so it is emitted for
        // path and polygon elements only; other node types ignore the style field.
        if (emitFillRule && FillRules.IsEvenOdd(style?.FillRule))
            parts.Add("fill-rule=\"evenodd\"");

        if (style?.Stroke is { } stroke)
        {
            var strokeColor = context.ApplyCurrentColor(ResolveColor(context, stroke.TokenRef, stroke.Color, "#000000"));
            parts.Add($"stroke=\"{EscapeXml(strokeColor)}\"");
            if (stroke.Width != 1)
                parts.Add($"stroke-width=\"{Format(stroke.Width)}\"");
            if (stroke.Cap != StrokeCap.Butt)
                parts.Add($"stroke-linecap=\"{stroke.Cap.ToString().ToLowerInvariant()}\"");
            if (stroke.Join != StrokeJoin.Miter)
                parts.Add($"stroke-linejoin=\"{stroke.Join.ToString().ToLowerInvariant()}\"");
            if (stroke.Join == StrokeJoin.Miter && stroke.MiterLimit != 4)
                parts.Add($"stroke-miterlimit=\"{Format(stroke.MiterLimit)}\"");
            if (stroke.DashArray is { Length: > 0 })
            {
                context.ValidateDashArray(stroke.DashArray);
                parts.Add($"stroke-dasharray=\"{string.Join(" ", stroke.DashArray.Select(Format))}\"");
                if (stroke.DashOffset != 0)
                    parts.Add($"stroke-dashoffset=\"{Format(stroke.DashOffset)}\"");
            }
        }
        else if (style is null && primitiveDefault == PrimitiveDefault.Stroked)
        {
            parts.Add($"stroke=\"{context.ApplyCurrentColor("#000000")}\"");
        }

        if (style is not null && style.Opacity != 1)
            parts.Add($"opacity=\"{Format(style.Opacity)}\"");
        if (style?.BlendMode is not null && SupportedBlendModes.Contains(style.BlendMode.ToLowerInvariant()))
            parts.Add($"style=\"mix-blend-mode: {style.BlendMode.ToLowerInvariant()}\"");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string ResolveColor(SvgRenderContext context, string? tokenReference, string? color, string fallback)
    {
        var candidate = tokenReference ?? color;
        if (candidate is null)
            return fallback;
        var resolved = context.TokenService.ResolveColorRefWithDiagnostics(context.Document, candidate);
        if (resolved is { Success: true, Value: { } value })
            return EnsureSafePaint(value);
        if (tokenReference is not null && color is not null && !color.StartsWith('{'))
            return EnsureSafePaint(color);
        return fallback;
    }

    private static string EnsureSafePaint(string value)
    {
        if (!SvgResourcePolicy.IsSafePaintValue(value))
            throw new InvalidOperationException("SVG paint value is not a safe literal color");
        return value;
    }

    private static string Format(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("SVG output contains a non-finite numeric value");
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            rounded = 0;
        return rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatForError(double value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeXml(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    private static void AppendLine(SvgOutputBuilder builder, string value) => builder.AppendLine(value);

    private enum PrimitiveDefault
    {
        None,
        Filled,
        Stroked
    }

    private sealed class SvgRenderContext(
        Document document,
        ExportOptions options,
        ITextMeasurer textMeasurer,
        CancellationToken cancellationToken)
    {
        private int _operationCount;
        private int _collectedNodeCount;
        private int _renderedNodeCount;
        private int _gradientStopCount;
        private int _totalPathCharacters;
        private int _totalTextCharacters;
        private int _totalPointCount;
        private readonly HashSet<Node> _collectionStack = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Node> _renderStack = new(ReferenceEqualityComparer.Instance);

        public Document Document { get; } = document;
        public ExportOptions Options { get; } = options;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TokenService TokenService { get; } = new();
        public Dictionary<string, Gradient> Gradients { get; } = new(StringComparer.Ordinal);
        public RgbaColor? CurrentColor { get; } = ParseCurrentColor(options.CurrentColor);

        /// <summary>
        /// Rewrites a resolved fill/stroke paint to <c>currentColor</c> when it normalizes to
        /// the export's current color. Non-color paints (none, url(...)) pass through unchanged.
        /// </summary>
        public string ApplyCurrentColor(string paint)
        {
            if (CurrentColor is { } target && ColorLiteral.TryParse(paint, out var rgba) && rgba == target)
                return "currentColor";
            return paint;
        }

        private static RgbaColor? ParseCurrentColor(string? value)
        {
            if (value is null)
                return null;
            if (!ColorLiteral.TryParse(value, out var rgba))
                throw new ArgumentException($"Current color must be a hex, rgb(), or rgba() color; got \"{value}\"");
            return rgba;
        }

        public void CheckCancellation()
        {
            if ((_operationCount++ & 63) == 0)
                CancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>Width of one candidate line while wrapping; measurer failures fall back to the estimate.</summary>
        public double MeasureWidth(ResolvedTextStyle style, string line)
        {
            CheckCancellation();
            return textMeasurer.MeasureSafely(style.ToMeasurementRequest(line), CancellationToken).Width;
        }

        public void EnterCollection(Node node, int depth)
        {
            CheckTraversal(depth, ref _collectedNodeCount);
            if (!_collectionStack.Add(node))
                throw new InvalidOperationException("SVG export scene graph contains a cycle");
        }

        public void ExitCollection(Node node) => _collectionStack.Remove(node);

        public void EnterRender(Node node, int depth)
        {
            CheckTraversal(depth, ref _renderedNodeCount);
            if (!_renderStack.Add(node))
                throw new InvalidOperationException("SVG export scene graph contains a cycle");
        }

        public void ExitRender(Node node) => _renderStack.Remove(node);

        public void ValidateChildCount(int count)
        {
            if (count > MaxNodeCount)
                throw new InvalidOperationException($"SVG child count exceeds maximum ({MaxNodeCount})");
        }

        public void ChargeGradientStops(int count)
        {
            if (count < 0 || count > MaxGradientStops
                || checked(_gradientStopCount += count) > MaxGradientStops)
            {
                throw new InvalidOperationException($"SVG gradient stop count exceeds maximum ({MaxGradientStops})");
            }
        }

        public void ChargePath(string pathData)
        {
            ArgumentNullException.ThrowIfNull(pathData);
            if (pathData.Length > MaxPathDataCharacters
                || checked(_totalPathCharacters += pathData.Length) > MaxTotalPathDataCharacters)
            {
                throw new InvalidOperationException($"SVG path data exceeds export budget ({MaxTotalPathDataCharacters} characters)");
            }
            CheckCancellation();
        }

        public void ChargeText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length > MaxTextCharacters
                || checked(_totalTextCharacters += text.Length) > MaxTotalTextCharacters)
            {
                throw new InvalidOperationException($"SVG text exceeds export budget ({MaxTotalTextCharacters} characters)");
            }
            CheckCancellation();
        }

        public void ChargePoints(int count)
        {
            if (count < 0 || count > MaxPolygonPoints
                || checked(_totalPointCount += count) > MaxTotalPolygonPoints)
            {
                throw new InvalidOperationException($"SVG point count exceeds maximum ({MaxTotalPolygonPoints})");
            }
        }

        public void ValidateName(string? name)
        {
            if (name?.Length > MaxNameCharacters)
                throw new InvalidOperationException($"SVG node name exceeds maximum length ({MaxNameCharacters})");
        }

        public void ValidateFontFamily(string fontFamily)
        {
            if (fontFamily.Length > MaxNameCharacters)
                throw new InvalidOperationException($"SVG font family exceeds maximum length ({MaxNameCharacters})");
        }

        public void ValidateDashArray(double[] values)
        {
            if (values.Length > MaxDashEntries)
                throw new InvalidOperationException($"SVG dash array exceeds maximum count ({MaxDashEntries})");
            foreach (var value in values)
            {
                if (!double.IsFinite(value) || value < 0)
                    throw new ArgumentException("SVG dash array contains a non-finite or negative value");
            }
        }

        public string GetSafeImageHref(string source)
        {
            CheckCancellation();
            if (SvgResourcePolicy.TryGetSafeImageHref(source, out var safeHref, out var reason, CancellationToken))
                return safeHref;
            if (Options.AllowExternalResources
                && source.Length <= SvgResourcePolicy.MaxRelativeHrefCharacters
                && source.IndexOf('\0') < 0)
            {
                return source;
            }
            throw new InvalidOperationException($"Unsafe SVG image resource rejected: {reason}");
        }

        private void CheckTraversal(int depth, ref int count)
        {
            CheckCancellation();
            if (depth > MaxTraversalDepth)
                throw new InvalidOperationException($"SVG traversal depth exceeds maximum ({MaxTraversalDepth})");
            if (++count > MaxNodeCount)
                throw new InvalidOperationException($"SVG node count exceeds maximum ({MaxNodeCount})");
        }
    }

    private sealed class SvgOutputBuilder(int maximumCharacters)
    {
        private readonly StringBuilder _builder = new();

        public SvgOutputBuilder Append(string value)
        {
            EnsureCapacity(value.Length);
            _builder.Append(value);
            return this;
        }

        public SvgOutputBuilder Append(char value)
        {
            EnsureCapacity(1);
            _builder.Append(value);
            return this;
        }

        public void AppendLine(string value)
        {
            EnsureCapacity(checked(value.Length + 1));
            _builder.Append(value).Append('\n');
        }

        public override string ToString() => _builder.ToString();

        private void EnsureCapacity(int additionalCharacters)
        {
            if (additionalCharacters < 0 || _builder.Length > maximumCharacters - additionalCharacters)
            {
                throw new InvalidOperationException(
                    $"SVG output exceeds maximum size ({maximumCharacters} characters)");
            }
        }
    }
}
