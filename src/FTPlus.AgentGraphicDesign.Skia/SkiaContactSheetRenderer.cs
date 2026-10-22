using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;

namespace FTPlus.AgentGraphicDesign.Skia;

/// <summary>
/// Composes documents into one PNG grid. Every tile goes through the shared
/// <see cref="IRenderer"/> at the scale that aspect-fits its canvas into the cell, so
/// resource limits, token resolution, and fonts behave exactly as in a normal export.
/// Labels are drawn through <see cref="TextShapingService"/>, the renderer's own text path.
/// </summary>
public sealed class SkiaContactSheetRenderer : IContactSheetRenderer
{
    public const double LabelFontSize = 12;
    public const int LabelPadding = 2;
    private const int MaxLabelCharacters = 256;
    private const string LabelEllipsis = "…";
    private const string DarkLabelColor = "#1f2937";
    private const string LightLabelColor = "#f3f4f6";

    private readonly IRenderer _renderer;

    public SkiaContactSheetRenderer() : this(new SkiaRenderer())
    {
    }

    public SkiaContactSheetRenderer(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public ContactSheetResult Render(
        IReadOnlyList<ContactSheetEntry> entries,
        ContactSheetOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var layout = ContactSheetLayout.Compute(entries.Count, options);
        if (layout.Width > SkiaRenderer.MaxDimension || layout.Height > SkiaRenderer.MaxDimension ||
            (long)layout.Width * layout.Height > SkiaRenderer.MaxPixelCount)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Contact sheet dimensions exceed the renderer limit; got {layout.Width}x{layout.Height}");
        }

        var cells = layout.Cells(entries);
        var background = ParseColor(options.Background ?? ContactSheetOptions.DefaultBackground);
        var labelColor = ParseColor(background.Luminance > 0.35 ? DarkLabelColor : LightLabelColor);

        using var surface = SKSurface.Create(new SKImageInfo(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException($"Unable to allocate Skia surface for {layout.Width}x{layout.Height} contact sheet");
        var canvas = surface.Canvas;
        canvas.Clear(ToSkColor(background));

        // Resolve labels through the same font policy as the tiles so the sheet matches a
        // normal export of each document.
        using var shaping = options.Labels ? new TextShapingService(FontResolutionOptions.From(options.FontPolicy)) : null;
        for (var i = 0; i < cells.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawTile(canvas, entries[i], cells[i], options, cancellationToken);
            if (shaping is not null)
                DrawLabel(canvas, shaping, entries[i].Label, layout.LabelBox(i), ToSkColor(labelColor), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Unable to encode the contact sheet as PNG");

        return new ContactSheetResult
        {
            Data = encoded.ToArray(),
            MimeType = "image/png",
            Layout = layout,
            Cells = cells
        };
    }

    private void DrawTile(SKCanvas canvas, ContactSheetEntry entry, ContactSheetCell cell, ContactSheetOptions options, CancellationToken cancellationToken)
    {
        var document = entry.Document;
        var target = cell.Image;
        var scale = Math.Min(target.Width / document.Canvas.Width, target.Height / document.Canvas.Height);
        scale = Math.Min(scale, SkiaRenderer.MaxScale);

        RenderResult rendered;
        try
        {
            rendered = _renderer.Render(document, new RenderOptions
            {
                Scale = scale,
                Format = RenderFormat.Png,
                FontDirectories = options.FontDirectories,
                SystemFonts = options.SystemFonts,
                StrictFonts = options.StrictFonts
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new DocumentEngineException("RENDER_FAILED",
                $"Cannot render '{entry.Label}' into a {target.Width}x{target.Height} cell: {ex.Message}");
        }

        using var tile = SKImage.FromEncodedData(rendered.Data)
            ?? throw new DocumentEngineException("RENDER_FAILED", $"Cannot decode the rendered tile for '{entry.Label}'");
        var destination = new SKRect(target.X, target.Y, target.Right, target.Bottom);
        canvas.DrawImage(tile, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }

    private static void DrawLabel(
        SKCanvas canvas,
        TextShapingService shaping,
        string label,
        PixelBox box,
        SKColor color,
        CancellationToken cancellationToken)
    {
        if (box.Width <= 0 || box.Height <= 0)
            return;

        var options = new TextShapingOptions { FontSize = LabelFontSize, FontWeight = 400 };
        var shaped = FitLabel(shaping, Sanitize(label), options, box.Width - 2 * LabelPadding, cancellationToken);
        if (shaped is null || shaped.Glyphs.Count == 0)
            return;

        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(box.X, box.Y, box.Right, box.Bottom));
            canvas.Translate(box.X + (box.Width - shaped.TotalWidth) / 2f, box.Y + (box.Height - shaped.Height) / 2f);
            shaping.DrawShapedText(canvas, shaped, options, paint, cancellationToken);
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Shapes the label, trimming it with an ellipsis until it fits the cell width.</summary>
    private static ShapedText? FitLabel(
        TextShapingService shaping,
        string label,
        TextShapingOptions options,
        int maxWidth,
        CancellationToken cancellationToken)
    {
        if (label.Length == 0 || maxWidth <= 0)
            return null;

        var shaped = shaping.ShapeText(label, options, cancellationToken);
        var length = label.Length;
        while (shaped.TotalWidth > maxWidth && length > 1)
        {
            length--;
            shaped = shaping.ShapeText(label[..length].TrimEnd() + LabelEllipsis, options, cancellationToken);
        }

        return shaped.TotalWidth > maxWidth ? null : shaped;
    }

    private static string Sanitize(string label)
    {
        var chars = label.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        var cleaned = new string(chars).Trim();
        return cleaned.Length > MaxLabelCharacters ? cleaned[..MaxLabelCharacters] : cleaned;
    }

    private static SrgbColor ParseColor(string value) =>
        SrgbColor.TryParse(value, out var color)
            ? color
            : throw new DocumentEngineException(EngineErrors.InvalidProperty, $"Invalid color: '{value}'");

    private static SKColor ToSkColor(SrgbColor color) => new(
        (byte)Math.Round(color.R * 255),
        (byte)Math.Round(color.G * 255),
        (byte)Math.Round(color.B * 255),
        (byte)Math.Round(color.A * 255));
}
