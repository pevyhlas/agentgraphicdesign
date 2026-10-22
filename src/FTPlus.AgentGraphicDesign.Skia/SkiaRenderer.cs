using System.Globalization;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;

namespace FTPlus.AgentGraphicDesign.Skia;

/// <summary>
/// SkiaSharp raster renderer. Embedded bounded raster data images are decoded;
/// every other image source is represented by a visible placeholder.
/// </summary>
public sealed class SkiaRenderer : IRenderer
{
    public const int MaxDimension = 8192;
    public const long MaxPixelCount = 64_000_000;
    public const double MaxScale = 1_000;
    public const int MaxEmbeddedImageBytes = SvgResourcePolicy.MaxDecodedImageBytes;
    public const int MaxNodeCount = 10_000;
    public const int MaxTraversalDepth = 128;
    public const int MaxPathDataCharacters = 256_000;
    public const int MaxTotalPathDataCharacters = 1_000_000;
    public const int MaxTextCharacters = TextShapingService.MaxTextCharacters;
    public const int MaxTotalTextCharacters = 1_000_000;
    public const int MaxPolygonPoints = 250_000;
    public const int MaxTotalPolygonPoints = 250_000;
    public const int MaxDashEntries = 10_000;
    public const int MaxGradientStops = 10_000;
    public const double MaxGeometryMagnitude = 1_000_000_000;

    /// <summary>
    /// Signals that only bounded embedded raster data is decoded. Unsupported,
    /// invalid, relative, and remote sources render a placeholder, never success
    /// that can be mistaken for the source image.
    /// </summary>
    public ImageRenderingCapability ImageCapability =>
        ImageRenderingCapability.BoundedEmbeddedRasterWithPlaceholderFallback;

    public RenderResult Render(Document document, RenderOptions options) =>
        Render(document, options, CancellationToken.None);

    public RenderResult Render(
        Document document,
        RenderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Format))
            throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported render format: {options.Format}");
        cancellationToken.ThrowIfCancellationRequested();

        var logicalWidth = options.Width ?? document.Canvas.Width;
        var logicalHeight = options.Height ?? document.Canvas.Height;
        var (width, height) = ValidateAndScaleDimensions(logicalWidth, logicalHeight, options.Scale);

        // Font directories are indexed here so an invalid font policy fails before any
        // surface is allocated and independently of whether the document has text.
        using var context = new RenderContext(document, FontResolutionOptions.From(options), cancellationToken);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException($"Unable to allocate Skia surface for {width}x{height} render");
        var canvas = surface.Canvas;
        canvas.Clear(options.Background is { } background
            ? ParseColor(ResolveColor(context, null, background, "#000000"))
            : document.Canvas.Background is { } canvasBackground
                ? ParseColor(ResolveColor(context, null, canvasBackground, "#000000"))
                : SKColors.Transparent);

        if (options.Scale != 1)
            canvas.Scale((float)options.Scale);

        RenderNode(canvas, document.Root, context, null, 0);

        cancellationToken.ThrowIfCancellationRequested();
        using var image = surface.Snapshot();
        var data = EncodeImage(image, options.Format);
        cancellationToken.ThrowIfCancellationRequested();
        return new RenderResult
        {
            Data = data,
            MimeType = GetMimeType(options.Format),
            Width = width,
            Height = height,
            Diagnostics = context.TextShaping.Diagnostics
        };
    }

    private static (int Width, int Height) ValidateAndScaleDimensions(double width, double height, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 || scale > MaxScale)
            throw new ArgumentException($"Render scale must be finite and in the range (0, {MaxScale.ToString(CultureInfo.InvariantCulture)}]");
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentException($"Render dimensions must be positive and finite; got {FormatForError(width)}x{FormatForError(height)}");

        var scaledWidth = width * scale;
        var scaledHeight = height * scale;
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight))
            throw new ArgumentException("Scaled render dimensions must be finite");
        if (scaledWidth > MaxDimension || scaledHeight > MaxDimension)
            throw new ArgumentException($"Render dimensions exceed maximum ({MaxDimension}); got {FormatForError(scaledWidth)}x{FormatForError(scaledHeight)}");

        var pixelWidth = checked((int)Math.Round(scaledWidth, MidpointRounding.AwayFromZero));
        var pixelHeight = checked((int)Math.Round(scaledHeight, MidpointRounding.AwayFromZero));
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentException($"Scaled render dimensions must produce at least one pixel; got {pixelWidth}x{pixelHeight}");
        var pixelCount = (long)pixelWidth * pixelHeight;
        if (pixelCount > MaxPixelCount)
            throw new ArgumentException($"Render pixel count exceeds maximum ({MaxPixelCount:N0}); got {pixelCount:N0} ({pixelWidth}x{pixelHeight})");
        return (pixelWidth, pixelHeight);
    }

    private static byte[] EncodeImage(SKImage image, RenderFormat format)
    {
        var encodedFormat = format switch
        {
            RenderFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            RenderFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        using var data = image.Encode(encodedFormat, format == RenderFormat.Png ? 100 : 90)
            ?? throw new InvalidOperationException($"Skia failed to encode {format} output");
        return data.ToArray();
    }

    private static string GetMimeType(RenderFormat format) => format switch
    {
        RenderFormat.Jpeg => "image/jpeg",
        RenderFormat.Webp => "image/webp",
        _ => "image/png"
    };

    private static void RenderNode(
        SKCanvas canvas,
        Node node,
        RenderContext context,
        Style? inheritedStyle,
        int depth)
    {
        context.EnterNode(node, depth);

        canvas.Save();
        try
        {
            if (!node.Visible)
                return;
            ApplyTransform(canvas, node.Transform);
            switch (node)
            {
                case FrameNode frame:
                    RenderFrame(canvas, frame, context, inheritedStyle, depth);
                    break;
                case GroupNode group:
                    RenderGroup(canvas, group, context, inheritedStyle, depth);
                    break;
                case RectangleNode rectangle:
                    RenderRectangle(canvas, rectangle, context, inheritedStyle);
                    break;
                case EllipseNode ellipse:
                    RenderEllipse(canvas, ellipse, context, inheritedStyle);
                    break;
                case PathNode path:
                    RenderPath(canvas, path, context, inheritedStyle);
                    break;
                case TextNode text:
                    RenderText(canvas, text, context, inheritedStyle);
                    break;
                case LineNode line:
                    RenderLine(canvas, line, context, inheritedStyle);
                    break;
                case PolygonNode polygon:
                    RenderPolygon(canvas, polygon, context, inheritedStyle);
                    break;
                case ImageNode image:
                    RenderImage(canvas, image, context, inheritedStyle);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
            context.ExitNode(node);
        }
    }

    private static void ApplyTransform(SKCanvas canvas, Transform? transform)
    {
        if (transform is null)
            return;
        if (transform.X != 0 || transform.Y != 0)
            canvas.Translate((float)transform.X, (float)transform.Y);
        if (transform.Rotation != 0)
            canvas.RotateDegrees((float)transform.Rotation);
        if (transform.ScaleX != 1 || transform.ScaleY != 1)
            canvas.Scale((float)transform.ScaleX, (float)transform.ScaleY);
    }

    private static void RenderFrame(SKCanvas canvas, FrameNode frame, RenderContext context, Style? inheritedStyle, int depth)
    {
        if (frame.ClipContent)
            canvas.ClipRect(new SKRect(0, 0, (float)frame.Width, (float)frame.Height));
        RenderContainerChildren(canvas, frame.Children, frame.Style, inheritedStyle, context, depth);
    }

    private static void RenderGroup(SKCanvas canvas, GroupNode group, RenderContext context, Style? inheritedStyle, int depth) =>
        RenderContainerChildren(canvas, group.Children, group.Style, inheritedStyle, context, depth);

    private static void RenderContainerChildren(
        SKCanvas canvas,
        IEnumerable<Node> children,
        Style? localStyle,
        Style? inheritedStyle,
        RenderContext context,
        int depth)
    {
        context.ValidateChildCount(children);
        var layerSaved = localStyle is { Opacity: not 1 } || localStyle?.BlendMode is not null;
        using var layerPaint = layerSaved ? CreateLayerPaint(localStyle!) : null;
        if (layerPaint is not null)
            canvas.SaveLayer(layerPaint.Paint);
        try
        {
            var childInheritedStyle = localStyle ?? inheritedStyle;
            foreach (var child in children.OrderBy(child => child.Order, StringComparer.Ordinal))
                RenderNode(canvas, child, context, childInheritedStyle, depth + 1);
        }
        finally
        {
            if (layerPaint is not null)
                canvas.Restore();
        }
    }

    private static void RenderRectangle(SKCanvas canvas, RectangleNode rectangle, RenderContext context, Style? inheritedStyle)
    {
        var bounds = new SKRect(0, 0, (float)rectangle.Width, (float)rectangle.Height);
        var style = rectangle.Style ?? inheritedStyle;
        DrawElement(canvas, rectangle.Style, () =>
        {
            using var fill = CreateFillPaint(style, bounds, context, style is null, 1, null);
            if (fill is not null)
            {
                if (rectangle.CornerRadius > 0)
                    canvas.DrawRoundRect(bounds, (float)rectangle.CornerRadius, (float)rectangle.CornerRadius, fill.Paint);
                else
                    canvas.DrawRect(bounds, fill.Paint);
            }
            using var stroke = CreateStrokePaint(style, context, false, 1, null);
            if (stroke is not null)
            {
                if (rectangle.CornerRadius > 0)
                    canvas.DrawRoundRect(bounds, (float)rectangle.CornerRadius, (float)rectangle.CornerRadius, stroke.Paint);
                else
                    canvas.DrawRect(bounds, stroke.Paint);
            }
        });
    }

    private static void RenderEllipse(SKCanvas canvas, EllipseNode ellipse, RenderContext context, Style? inheritedStyle)
    {
        var bounds = new SKRect(0, 0, (float)ellipse.Width, (float)ellipse.Height);
        var style = ellipse.Style ?? inheritedStyle;
        DrawElement(canvas, ellipse.Style, () =>
        {
            using var fill = CreateFillPaint(style, bounds, context, style is null, 1, null);
            if (fill is not null)
                canvas.DrawOval(bounds.MidX, bounds.MidY, bounds.Width / 2, bounds.Height / 2, fill.Paint);
            using var stroke = CreateStrokePaint(style, context, false, 1, null);
            if (stroke is not null)
                canvas.DrawOval(bounds.MidX, bounds.MidY, bounds.Width / 2, bounds.Height / 2, stroke.Paint);
        });
    }

    private static void RenderPath(SKCanvas canvas, PathNode pathNode, RenderContext context, Style? inheritedStyle)
    {
        if (string.IsNullOrEmpty(pathNode.D))
            return;
        context.ChargePath(pathNode.D);
        context.CheckCancellation();
        using var path = SKPath.ParseSvgPathData(pathNode.D);
        context.CheckCancellation();
        if (path is null)
            return;
        ApplyFillRule(path, pathNode.Style);

        var style = pathNode.Style ?? inheritedStyle;
        DrawElement(canvas, pathNode.Style, () =>
        {
            using var fill = CreateFillPaint(style, path.Bounds, context, style is null, 1, null);
            if (fill is not null)
                canvas.DrawPath(path, fill.Paint);
            using var stroke = CreateStrokePaint(style, context, false, 1, null);
            if (stroke is not null)
                canvas.DrawPath(path, stroke.Paint);
        });
    }

    private static void RenderText(SKCanvas canvas, TextNode text, RenderContext context, Style? inheritedStyle)
    {
        context.ChargeText(text.Text);
        TypographyToken? token = null;
        if (text.TypographyTokenRef is { } reference)
        {
            var resolved = context.TokenService.ResolveTypographyRefWithDiagnostics(context.Document, reference);
            if (resolved is { Success: true, Value: { } typography })
                token = typography;
        }

        var layout = TextLayout.Resolve(text, token);
        var shapingService = context.TextShaping;
        var options = new TextShapingOptions
        {
            FontFamily = layout.FontFamily,
            FontSize = layout.FontSize,
            FontWeight = layout.FontWeight,
            LetterSpacing = layout.LetterSpacing
        };

        // Lines are broken with the same shaper that draws them, so a wrapped line never
        // measures wider than maxWidth once rendered.
        var lines = TextLayout.BreakLines(text.Text, layout.MaxWidth,
            line => shapingService.ShapeText(line, options, context.CancellationToken).TotalWidth);
        var shapedLines = new List<ShapedText>(lines.Count);
        float maxLineWidth = 0;
        foreach (var line in lines)
        {
            var shaped = shapingService.ShapeText(line, options, context.CancellationToken);
            shapedLines.Add(shaped);
            maxLineWidth = Math.Max(maxLineWidth, shaped.TotalWidth);
        }

        var pitch = (float)TextLayout.LinePitch(layout.FontSize, layout.LineHeight);
        var verticalOffset = (float)TextLayout.VerticalOffset(layout.VerticalAlign, lines.Count, layout.FontSize, layout.LineHeight);
        var blockLeft = (float)TextLayout.AlignOffset(layout.TextAlign, maxLineWidth);
        var blockHeight = (lines.Count - 1) * pitch + shapedLines[0].Height;
        var bounds = new SKRect(blockLeft, verticalOffset, blockLeft + maxLineWidth, verticalOffset + blockHeight);
        var style = text.Style ?? inheritedStyle;
        DrawElement(canvas, text.Style, () =>
        {
            using var fill = CreateFillPaint(style, bounds, context, style is null, 1, null);
            if (fill is not null)
                DrawLines(canvas, shapingService, shapedLines, options, layout, verticalOffset, pitch, fill.Paint, context);
            using var stroke = CreateStrokePaint(style, context, false, 1, null);
            if (stroke is not null)
                DrawLines(canvas, shapingService, shapedLines, options, layout, verticalOffset, pitch, stroke.Paint, context);
        });
    }

    private static void DrawLines(
        SKCanvas canvas,
        TextShapingService shapingService,
        List<ShapedText> shapedLines,
        TextShapingOptions options,
        ResolvedTextStyle layout,
        float verticalOffset,
        float pitch,
        SKPaint paint,
        RenderContext context)
    {
        for (var index = 0; index < shapedLines.Count; index++)
        {
            var shaped = shapedLines[index];
            var dx = (float)TextLayout.AlignOffset(layout.TextAlign, shaped.TotalWidth);
            var dy = verticalOffset + index * pitch;
            if (dx == 0 && dy == 0)
            {
                shapingService.DrawShapedText(canvas, shaped, options, paint, context.CancellationToken);
                continue;
            }

            canvas.Save();
            try
            {
                canvas.Translate(dx, dy);
                shapingService.DrawShapedText(canvas, shaped, options, paint, context.CancellationToken);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }

    private static void RenderLine(SKCanvas canvas, LineNode line, RenderContext context, Style? inheritedStyle)
    {
        var style = line.Style ?? inheritedStyle;
        DrawElement(canvas, line.Style, () =>
        {
            using var stroke = CreateStrokePaint(style, context, defaultStroke: style is null, 1, null);
            if (stroke is not null)
                canvas.DrawLine((float)line.X1, (float)line.Y1, (float)line.X2, (float)line.Y2, stroke.Paint);
        });
    }

    private static void RenderPolygon(SKCanvas canvas, PolygonNode polygon, RenderContext context, Style? inheritedStyle)
    {
        if (polygon.Points.Count < 2)
            return;
        context.ChargePoints(polygon.Points.Count);
        using var builder = new SKPathBuilder();
        builder.MoveTo((float)polygon.Points[0].X, (float)polygon.Points[0].Y);
        for (var index = 1; index < polygon.Points.Count; index++)
        {
            if ((index & 1023) == 0)
                context.CheckCancellation();
            context.ValidatePoint(polygon.Points[index]);
            builder.LineTo((float)polygon.Points[index].X, (float)polygon.Points[index].Y);
        }
        if (polygon.Closed)
            builder.Close();
        using var path = builder.Detach();
        ApplyFillRule(path, polygon.Style);

        var style = polygon.Style ?? inheritedStyle;
        DrawElement(canvas, polygon.Style, () =>
        {
            using var fill = CreateFillPaint(style, path.Bounds, context, polygon.Closed && style is null, 1, null);
            if (fill is not null)
                canvas.DrawPath(path, fill.Paint);
            using var stroke = CreateStrokePaint(style, context, !polygon.Closed && style is null, 1, null);
            if (stroke is not null)
                canvas.DrawPath(path, stroke.Paint);
        });
    }

    private static void RenderImage(
        SKCanvas canvas,
        ImageNode imageNode,
        RenderContext context,
        Style? inheritedStyle)
    {
        var destination = new SKRect(0, 0, (float)imageNode.Width, (float)imageNode.Height);
        using var image = TryDecodeEmbeddedImage(imageNode.Src, context);
        DrawElement(canvas, imageNode.Style, () =>
        {
            if (image is not null)
            {
                using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
                canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Linear), paint);
                return;
            }
            RenderImagePlaceholder(canvas, destination);
        });
    }

    private static SKImage? TryDecodeEmbeddedImage(string source, RenderContext context)
    {
        if (string.IsNullOrEmpty(source) || !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!SvgResourcePolicy.TryDecodeDataImage(
                source,
                out var bytes,
                out var declaredInfo,
                out _,
                context.CancellationToken))
        {
            return null;
        }

        context.CheckCancellation();
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null
            || !EncodedFormatMatchesMime(codec.EncodedFormat, declaredInfo.MimeType)
            || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || codec.Info.Width > MaxDimension || codec.Info.Height > MaxDimension
            || (long)codec.Info.Width * codec.Info.Height > MaxPixelCount
            || codec.Info.Width != declaredInfo.Width
            || codec.Info.Height != declaredInfo.Height)
        {
            return null;
        }
        context.CheckCancellation();
        return SKImage.FromEncodedData(data);
    }

    private static bool EncodedFormatMatchesMime(SKEncodedImageFormat format, string mimeType) =>
        (format, mimeType) switch
        {
            (SKEncodedImageFormat.Png, "image/png") => true,
            (SKEncodedImageFormat.Jpeg, "image/jpeg") => true,
            (SKEncodedImageFormat.Gif, "image/gif") => true,
            (SKEncodedImageFormat.Webp, "image/webp") => true,
            _ => false
        };

    private static void RenderImagePlaceholder(SKCanvas canvas, SKRect bounds)
    {
        using var pathEffect = SKPathEffect.CreateDash([5, 5], 0);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Gray,
            StrokeWidth = 1,
            PathEffect = pathEffect
        };
        canvas.DrawRect(bounds, paint);

        using var font = new SKFont(SKTypeface.Default, 12);
        using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Gray };
        var textWidth = font.MeasureText("[image]");
        canvas.DrawText("[image]", bounds.MidX - textWidth / 2, bounds.MidY + 4, SKTextAlign.Left, font, textPaint);
    }

    /// <summary>
    /// The fill rule is read from the node's own style only, mirroring SVG export, which
    /// emits <c>fill-rule</c> on path and polygon elements and never on containers.
    /// </summary>
    private static void ApplyFillRule(SKPath path, Style? style)
    {
        if (FillRules.IsEvenOdd(style?.FillRule))
            path.FillType = SKPathFillType.EvenOdd;
    }

    private static PaintResources CreateLayerPaint(Style style)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            Color = ApplyOpacity(SKColors.White, style.Opacity)
        };
        ApplyBlendMode(paint, style.BlendMode);
        return new PaintResources(paint);
    }

    private static void DrawElement(SKCanvas canvas, Style? localStyle, Action draw)
    {
        var useLayer = localStyle is { Opacity: not 1 } || localStyle?.BlendMode is not null;
        using var layerPaint = useLayer ? CreateLayerPaint(localStyle!) : null;
        if (layerPaint is not null)
            canvas.SaveLayer(layerPaint.Paint);
        try
        {
            draw();
        }
        finally
        {
            if (layerPaint is not null)
                canvas.Restore();
        }
    }

    private static PaintResources? CreateFillPaint(
        Style? style,
        SKRect bounds,
        RenderContext context,
        bool defaultFill,
        double opacity,
        string? blendMode)
    {
        if (style?.Fill is null && !defaultFill)
            return null;
        if (style?.Fill is { Color: null, Gradient: null, TokenRef: null })
            return null;

        var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        SKShader? shader = null;
        if (style?.Fill?.Gradient is { } gradient)
        {
            shader = CreateGradientShader(gradient, bounds, context);
            if (shader is null)
            {
                paint.Dispose();
                return null;
            }
            paint.Shader = shader;
            paint.Color = ApplyOpacity(SKColors.White, opacity);
        }
        else
        {
            var color = style?.Fill is { } fill
                ? ResolveColor(context, fill.TokenRef, fill.Color, "#000000")
                : "#000000";
            paint.Color = ApplyOpacity(ParseColor(color), opacity);
        }
        ApplyBlendMode(paint, blendMode);
        return new PaintResources(paint, shader);
    }

    private static SKShader? CreateGradientShader(Gradient gradient, SKRect bounds, RenderContext context)
    {
        if (gradient.Stops.Count == 0)
            return null;
        var colors = gradient.Stops.Select(stop =>
        {
            var color = ResolveColor(context, null, stop.Color, "#000000");
            return ApplyOpacity(ParseColor(color), stop.Opacity);
        }).ToArray();
        var positions = gradient.Stops.Select(stop => (float)stop.Offset).ToArray();
        return gradient switch
        {
            LinearGradient linear => SKShader.CreateLinearGradient(
                new SKPoint(bounds.Left + (float)linear.X1 * bounds.Width, bounds.Top + (float)linear.Y1 * bounds.Height),
                new SKPoint(bounds.Left + (float)linear.X2 * bounds.Width, bounds.Top + (float)linear.Y2 * bounds.Height),
                colors,
                positions,
                SKShaderTileMode.Clamp),
            RadialGradient radial => CreateRadialGradientShader(radial, bounds, colors, positions),
            _ => null
        };
    }

    private static SKShader? CreateRadialGradientShader(
        RadialGradient radial,
        SKRect bounds,
        SKColor[] colors,
        float[] positions)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || radial.R <= 0)
            return null;
        var focalX = radial.Fx ?? radial.Cx;
        var focalY = radial.Fy ?? radial.Cy;
        using var normalized = SKShader.CreateTwoPointConicalGradient(
            new SKPoint((float)focalX, (float)focalY),
            0,
            new SKPoint((float)radial.Cx, (float)radial.Cy),
            (float)radial.R,
            colors,
            positions,
            SKShaderTileMode.Clamp);
        if (normalized is null)
            return null;
        var matrix = SKMatrix.CreateScale(bounds.Width, bounds.Height);
        matrix.TransX = bounds.Left;
        matrix.TransY = bounds.Top;
        return normalized.WithLocalMatrix(matrix);
    }

    private static PaintResources? CreateStrokePaint(
        Style? style,
        RenderContext context,
        bool defaultStroke,
        double opacity,
        string? blendMode)
    {
        if (style?.Stroke is null && !defaultStroke)
            return null;
        var stroke = style?.Stroke;
        if (stroke is { Color: null, TokenRef: null })
            return null;
        var color = stroke is null
            ? "#000000"
            : ResolveColor(context, stroke.TokenRef, stroke.Color, "#000000");
        var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = ApplyOpacity(ParseColor(color), opacity),
            StrokeWidth = (float)(stroke?.Width ?? 1),
            StrokeCap = stroke?.Cap switch
            {
                StrokeCap.Round => SKStrokeCap.Round,
                StrokeCap.Square => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt
            },
            StrokeJoin = stroke?.Join switch
            {
                StrokeJoin.Round => SKStrokeJoin.Round,
                StrokeJoin.Bevel => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Miter
            },
            StrokeMiter = (float)(stroke?.MiterLimit ?? 4)
        };

        SKPathEffect? pathEffect = null;
        if (stroke?.DashArray is { Length: > 0 })
        {
            context.ValidateDashArray(stroke.DashArray);
            if (stroke.DashArray.Any(value => value != 0))
            {
                var source = stroke.DashArray;
                var intervals = source.Length % 2 == 0
                    ? source.Select(value => (float)value).ToArray()
                    : source.Concat(source).Select(value => (float)value).ToArray();
                pathEffect = SKPathEffect.CreateDash(intervals, (float)stroke.DashOffset);
                paint.PathEffect = pathEffect;
            }
        }
        ApplyBlendMode(paint, blendMode);
        return new PaintResources(paint, pathEffect: pathEffect);
    }

    private static string ResolveColor(RenderContext context, string? tokenReference, string? color, string fallback)
    {
        var candidate = tokenReference ?? color;
        if (candidate is null)
            return fallback;
        var resolved = context.TokenService.ResolveColorRefWithDiagnostics(context.Document, candidate);
        if (resolved is { Success: true, Value: { } value } && SvgResourcePolicy.IsSafePaintValue(value))
            return value;
        if (tokenReference is not null && color is not null && !color.StartsWith('{'))
            return SvgResourcePolicy.IsSafePaintValue(color) ? color : fallback;
        return fallback;
    }

    private static void ApplyBlendMode(SKPaint paint, string? blendMode)
    {
        if (blendMode is null)
            return;
        paint.BlendMode = blendMode.ToLowerInvariant() switch
        {
            "multiply" => SKBlendMode.Multiply,
            "screen" => SKBlendMode.Screen,
            "overlay" => SKBlendMode.Overlay,
            "darken" => SKBlendMode.Darken,
            "lighten" => SKBlendMode.Lighten,
            "color-dodge" => SKBlendMode.ColorDodge,
            "color-burn" => SKBlendMode.ColorBurn,
            "hard-light" => SKBlendMode.HardLight,
            "soft-light" => SKBlendMode.SoftLight,
            "difference" => SKBlendMode.Difference,
            "exclusion" => SKBlendMode.Exclusion,
            "hue" => SKBlendMode.Hue,
            "saturation" => SKBlendMode.Saturation,
            "color" => SKBlendMode.Color,
            "luminosity" => SKBlendMode.Luminosity,
            _ => SKBlendMode.SrcOver
        };
    }

    private static SKColor ParseColor(string color)
    {
        if (SKColor.TryParse(color, out var parsedColor))
            return parsedColor;
        if ((color.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase)
                || color.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            && color.EndsWith(')'))
        {
            var rgba = color.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
            var parts = color[(rgba ? 5 : 4)..^1].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == (rgba ? 4 : 3)
                && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var red)
                && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var green)
                && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue))
            {
                if (!rgba)
                    return new SKColor(red, green, blue);
                if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha) && double.IsFinite(alpha))
                    return new SKColor(red, green, blue, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));
            }
        }
        return SKColors.Black;
    }

    private static SKColor ApplyOpacity(SKColor color, double opacity)
    {
        var clamped = Math.Clamp(double.IsFinite(opacity) ? opacity : 1, 0, 1);
        return color.WithAlpha((byte)Math.Round(color.Alpha * clamped));
    }

    private static string FormatForError(double value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private sealed class RenderContext(
        Document document,
        FontResolutionOptions fontOptions,
        CancellationToken cancellationToken) : IDisposable
    {
        private int _nodeCount;
        private int _pathCharacters;
        private int _textCharacters;
        private int _pointCount;
        private int _operationCount;
        private int _gradientStopCount;
        private readonly HashSet<Node> _activeNodes = new(ReferenceEqualityComparer.Instance);

        public Document Document { get; } = document;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TokenService TokenService { get; } = new();

        /// <summary>One shaping service per render so typefaces are resolved and cached once.</summary>
        public TextShapingService TextShaping { get; } = new(fontOptions);

        public void Dispose() => TextShaping.Dispose();

        public void EnterNode(Node node, int depth)
        {
            CheckCancellation();
            if (depth > MaxTraversalDepth)
                throw new InvalidOperationException($"Render traversal depth exceeds maximum ({MaxTraversalDepth})");
            if (++_nodeCount > MaxNodeCount)
                throw new InvalidOperationException($"Render node count exceeds maximum ({MaxNodeCount})");
            if (!_activeNodes.Add(node))
                throw new InvalidOperationException("Render scene graph contains a cycle");
            ValidateNode(node);
        }

        public void ExitNode(Node node) => _activeNodes.Remove(node);

        public void ValidateChildCount(IEnumerable<Node> children)
        {
            if (children is ICollection<Node> collection && collection.Count > MaxNodeCount)
                throw new InvalidOperationException($"Render child count exceeds maximum ({MaxNodeCount})");
        }

        public void ChargePath(string pathData)
        {
            if (pathData.Length > MaxPathDataCharacters
                || _pathCharacters > MaxTotalPathDataCharacters - pathData.Length)
            {
                throw new InvalidOperationException($"Render path data exceeds maximum ({MaxTotalPathDataCharacters} characters)");
            }
            _pathCharacters += pathData.Length;
        }

        public void ChargeText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length > MaxTextCharacters
                || _textCharacters > MaxTotalTextCharacters - text.Length)
            {
                throw new InvalidOperationException($"Render text exceeds maximum ({MaxTotalTextCharacters} characters)");
            }
            _textCharacters += text.Length;
        }

        public void ChargePoints(int count)
        {
            if (count > MaxPolygonPoints || _pointCount > MaxTotalPolygonPoints - count)
                throw new InvalidOperationException($"Render point count exceeds maximum ({MaxTotalPolygonPoints})");
            _pointCount += count;
        }

        public void ValidatePoint(Point point)
        {
            ValidateFinite(point.X, "polygon X coordinate");
            ValidateFinite(point.Y, "polygon Y coordinate");
        }

        public void ValidateDashArray(double[] values)
        {
            if (values.Length > MaxDashEntries)
                throw new InvalidOperationException($"Render dash array exceeds maximum count ({MaxDashEntries})");
            foreach (var value in values)
            {
                if (!double.IsFinite(value) || value < 0)
                    throw new ArgumentException("Render dash array contains a non-finite or negative value");
            }
        }

        public void CheckCancellation()
        {
            if ((_operationCount++ & 63) == 0)
                CancellationToken.ThrowIfCancellationRequested();
        }

        private void ValidateNode(Node node)
        {
            if (node.Transform is { } transform)
            {
                ValidateFinite(transform.X, "transform X");
                ValidateFinite(transform.Y, "transform Y");
                ValidateFinite(transform.Rotation, "transform rotation");
                ValidateFinite(transform.ScaleX, "transform scale X");
                ValidateFinite(transform.ScaleY, "transform scale Y");
            }
            if (node.Style is { } style)
            {
                if (!double.IsFinite(style.Opacity) || style.Opacity is < 0 or > 1)
                    throw new ArgumentException("Render opacity must be finite and in the range [0, 1]");
                if (style.Stroke is { } stroke)
                {
                    ValidateNonNegative(stroke.Width, "stroke width");
                    ValidateNonNegative(stroke.MiterLimit, "stroke miter limit");
                    ValidateFinite(stroke.DashOffset, "stroke dash offset");
                    if (stroke.DashArray is { } dashes)
                        ValidateDashArray(dashes);
                }
                if (style.Fill?.Gradient is { } gradient)
                {
                    if (gradient.Stops.Count > MaxGradientStops
                        || _gradientStopCount > MaxGradientStops - gradient.Stops.Count)
                    {
                        throw new InvalidOperationException($"Render gradient stop count exceeds maximum ({MaxGradientStops})");
                    }
                    _gradientStopCount += gradient.Stops.Count;
                    foreach (var stop in gradient.Stops)
                    {
                        if (!double.IsFinite(stop.Offset) || !double.IsFinite(stop.Opacity)
                            || stop.Offset is < 0 or > 1 || stop.Opacity is < 0 or > 1)
                        {
                            throw new ArgumentException("Render gradient stop values must be finite and in the range [0, 1]");
                        }
                    }
                    if (gradient is LinearGradient linear)
                    {
                        ValidateFinite(linear.X1, "linear gradient X1");
                        ValidateFinite(linear.Y1, "linear gradient Y1");
                        ValidateFinite(linear.X2, "linear gradient X2");
                        ValidateFinite(linear.Y2, "linear gradient Y2");
                    }
                    else if (gradient is RadialGradient radial)
                    {
                        ValidateFinite(radial.Cx, "radial gradient center X");
                        ValidateFinite(radial.Cy, "radial gradient center Y");
                        ValidateNonNegative(radial.R, "radial gradient radius");
                        if (radial.Fx is { } fx)
                            ValidateFinite(fx, "radial gradient focal X");
                        if (radial.Fy is { } fy)
                            ValidateFinite(fy, "radial gradient focal Y");
                    }
                }
            }

            switch (node)
            {
                case FrameNode frame:
                    ValidateNonNegative(frame.Width, "frame width");
                    ValidateNonNegative(frame.Height, "frame height");
                    break;
                case RectangleNode rectangle:
                    ValidateNonNegative(rectangle.Width, "rectangle width");
                    ValidateNonNegative(rectangle.Height, "rectangle height");
                    ValidateNonNegative(rectangle.CornerRadius, "rectangle corner radius");
                    break;
                case EllipseNode ellipse:
                    ValidateNonNegative(ellipse.Width, "ellipse width");
                    ValidateNonNegative(ellipse.Height, "ellipse height");
                    break;
                case LineNode line:
                    ValidateFinite(line.X1, "line X1");
                    ValidateFinite(line.Y1, "line Y1");
                    ValidateFinite(line.X2, "line X2");
                    ValidateFinite(line.Y2, "line Y2");
                    break;
                case ImageNode image:
                    ValidateNonNegative(image.Width, "image width");
                    ValidateNonNegative(image.Height, "image height");
                    break;
                case PolygonNode polygon when polygon.Points.Count > 0:
                    ValidatePoint(polygon.Points[0]);
                    break;
            }
        }

        private static void ValidateNonNegative(double value, string name)
        {
            ValidateFinite(value, name);
            if (value < 0)
                throw new ArgumentException($"Render {name} must not be negative");
        }

        private static void ValidateFinite(double value, string name)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > MaxGeometryMagnitude)
                throw new ArgumentException($"Render {name} is non-finite or exceeds the geometry limit");
        }
    }

    private sealed class PaintResources(
        SKPaint paint,
        SKShader? shader = null,
        SKPathEffect? pathEffect = null) : IDisposable
    {
        public SKPaint Paint { get; } = paint;

        public void Dispose()
        {
            Paint.Dispose();
            shader?.Dispose();
            pathEffect?.Dispose();
        }
    }
}

public enum ImageRenderingCapability
{
    BoundedEmbeddedRasterWithPlaceholderFallback
}
