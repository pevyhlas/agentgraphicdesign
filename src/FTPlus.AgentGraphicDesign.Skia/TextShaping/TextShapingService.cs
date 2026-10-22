using System.Text;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FTPlus.AgentGraphicDesign.Skia.TextShaping;

/// <summary>
/// Text shaping service using HarfBuzz for proper glyph layout.
/// Handles ligatures, diacritics, and complex scripts.
/// </summary>
/// <remarks>
/// <para>
/// Fonts resolve in a fixed order so output is reproducible: caller-supplied
/// <see cref="FontResolutionOptions.FontDirectories"/>, then the bundled
/// <see cref="BundledFonts.FamilyName"/> family, then host-installed fonts when
/// <see cref="FontResolutionOptions.SystemFonts"/> allows them. A text run without a
/// family always uses the bundled family. A family that cannot be resolved substitutes
/// the bundled family and records a <see cref="RenderDiagnosticCodes.FontSubstituted"/>
/// diagnostic, or fails with <see cref="EngineErrors.FontNotFound"/> under
/// <see cref="FontResolutionOptions.StrictFonts"/>.
/// </para>
/// </remarks>
public sealed class TextShapingService : IDisposable
{
    public const int MaxTextCharacters = 64_000;
    public const int MaxCachedTypefaces = 64;
    public const int MaxLanguageCharacters = 128;
    public const int MaxSubstitutionDiagnostics = 64;

    private readonly object _sync = new();
    private readonly Dictionary<string, CachedTypeface> _typefaceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly FontResolutionOptions _fontOptions;
    private readonly FontDirectoryCatalog _directories;
    private readonly List<RenderDiagnostic> _diagnostics = [];
    private readonly HashSet<string> _diagnosedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public TextShapingService() : this(FontResolutionOptions.Default)
    {
    }

    /// <summary>
    /// Creates a service bound to one resolution policy. Font directories are indexed
    /// eagerly so an invalid directory fails here rather than at the first text run.
    /// </summary>
    public TextShapingService(FontResolutionOptions fontOptions)
    {
        ArgumentNullException.ThrowIfNull(fontOptions);
        _fontOptions = fontOptions;
        _directories = FontDirectoryCatalog.Load(fontOptions.FontDirectories);
    }

    public FontResolutionOptions FontOptions => _fontOptions;

    /// <summary>
    /// Font substitutions and skipped font files observed so far, in the order they were
    /// found. Bounded per service; see <see cref="MaxSubstitutionDiagnostics"/>.
    /// </summary>
    public IReadOnlyList<RenderDiagnostic> Diagnostics
    {
        get
        {
            lock (_sync)
            {
                return [.. _directories.Diagnostics, .. _diagnostics];
            }
        }
    }

    /// <summary>
    /// Resolves a family/weight request under this service's policy and reports which
    /// source satisfied it. The typeface is cached for later shaping calls.
    /// </summary>
    public FontResolution ResolveFont(string? fontFamily, int fontWeight)
    {
        if (fontWeight is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(fontWeight), "Font weight must be in the range [1, 1000]");
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetCachedTypeface(fontFamily, fontWeight).Resolution;
        }
    }

    /// <summary>
    /// Shape text using HarfBuzz and return positioned glyphs.
    /// Falls back to Skia's built-in glyph enumeration when HarfBuzz shaping
    /// is unavailable (e.g., system fonts without embedded data).
    /// </summary>
    public ShapedText ShapeText(string text, TextShapingOptions options) =>
        ShapeText(text, options, CancellationToken.None);

    public ShapedText ShapeText(
        string text,
        TextShapingOptions options,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ShapeTextCore(text, options, cancellationToken);
        }
    }

    /// <summary>
    /// Metrics of the typeface this service resolves for <paramref name="options"/>, at
    /// its font size. Ascent is negative (Skia convention). Bounds callers use this so
    /// measured text matches the typeface that renders.
    /// </summary>
    public FontMetricsInfo GetFontMetrics(TextShapingOptions options)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(options);
            if (!double.IsFinite(options.FontSize) || options.FontSize <= 0 || options.FontSize > 4096)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Font size must be finite and in the range (0, 4096]");
            }
            if (options.FontWeight is < 1 or > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Font weight must be in the range [1, 1000]");
            }

            var typeface = GetCachedTypeface(options.FontFamily, options.FontWeight).Typeface;
            using var font = new SKFont(typeface, (float)options.FontSize);
            var metrics = font.Metrics;
            return new FontMetricsInfo(metrics.Ascent, metrics.Descent, metrics.Leading);
        }
    }

    private ShapedText ShapeTextCore(
        string text,
        TextShapingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > MaxTextCharacters)
            throw new ArgumentException($"Text exceeds maximum length ({MaxTextCharacters} characters)", nameof(text));
        if (!double.IsFinite(options.FontSize) || options.FontSize <= 0 || options.FontSize > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Font size must be finite and in the range (0, 4096]");
        }
        if (options.FontWeight is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Font weight must be in the range [1, 1000]");
        }
        if (!double.IsFinite(options.LetterSpacing))
            throw new ArgumentOutOfRangeException(nameof(options), "Letter spacing must be finite");
        if (!Enum.IsDefined(options.Direction))
            throw new ArgumentOutOfRangeException(nameof(options), "Text direction is not supported");
        if (options.Language?.Length > MaxLanguageCharacters
            || options.Language?.Any(char.IsControl) == true)
        {
            throw new ArgumentException($"Language must not contain control characters or exceed {MaxLanguageCharacters} characters", nameof(options));
        }

        if (string.IsNullOrEmpty(text))
        {
            return new ShapedText([], 0, 0) { Backend = TextShapingBackend.None };
        }

        var typeface = GetCachedTypeface(options.FontFamily, options.FontWeight).Typeface;
        using var skFont = new SKFont(typeface, (float)options.FontSize);

        using var stream = typeface.OpenStream(out var ttcIndex);
        if (stream is not null)
        {
            var harfBuzzResult = TryShapeWithHarfBuzz(
                text, stream, ttcIndex, options, skFont, cancellationToken, out var harfBuzzFailure);
            if (harfBuzzResult is not null)
            {
                return ApplyLetterSpacing(harfBuzzResult, options.LetterSpacing);
            }

            if (harfBuzzFailure is not null)
            {
                var fallback = ShapeWithSkia(text, skFont, options, cancellationToken);
                return ApplyLetterSpacing(fallback with { FallbackDiagnostic = harfBuzzFailure }, options.LetterSpacing);
            }
        }

        return ApplyLetterSpacing(ShapeWithSkia(text, skFont, options, cancellationToken), options.LetterSpacing);
    }

    /// <summary>
    /// Adds <paramref name="letterSpacing"/> to the advance of every glyph that advances
    /// (combining marks stay attached to their base) and to the total width, in visual
    /// order. A spacing of zero returns the shaped text untouched.
    /// </summary>
    private static ShapedText ApplyLetterSpacing(ShapedText shaped, double letterSpacing)
    {
        var spacing = (float)letterSpacing;
        if (spacing == 0 || shaped.Glyphs.Count == 0)
            return shaped;

        var glyphs = new List<ShapedGlyph>(shaped.Glyphs.Count);
        float extra = 0;
        foreach (var glyph in shaped.Glyphs)
        {
            var advances = glyph.XAdvance > 0;
            glyphs.Add(glyph with
            {
                X = glyph.X + extra,
                XAdvance = advances ? glyph.XAdvance + spacing : glyph.XAdvance
            });
            if (advances)
                extra += spacing;
        }

        return shaped with { Glyphs = glyphs, TotalWidth = shaped.TotalWidth + extra };
    }

    private static ShapedText? TryShapeWithHarfBuzz(
        string text,
        SKStreamAsset stream,
        int ttcIndex,
        TextShapingOptions options,
        SKFont skFont,
        CancellationToken cancellationToken,
        out string? failureReason)
    {
        failureReason = null;
        try
        {
            using var blob = stream.ToHarfBuzzBlob();
            using var face = new Face(blob, ttcIndex);
            using var font = new Font(face);

            font.SetFunctionsOpenType();
            font.SetScale((int)(options.FontSize * 64), (int)(options.FontSize * 64));

            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf8(text);
            buffer.GuessSegmentProperties();
            buffer.Direction = options.Direction == TextDirection.RightToLeft
                ? HarfBuzzSharp.Direction.RightToLeft
                : HarfBuzzSharp.Direction.LeftToRight;
            if (!string.IsNullOrWhiteSpace(options.Language))
            {
                buffer.Language = new Language(options.Language);
            }

            cancellationToken.ThrowIfCancellationRequested();
            font.Shape(buffer);
            cancellationToken.ThrowIfCancellationRequested();

            var glyphInfos = buffer.GlyphInfos;
            var glyphPositions = buffer.GlyphPositions;
            var glyphs = new List<ShapedGlyph>(glyphInfos.Length);

            float x = 0;
            float y = 0;

            for (int i = 0; i < glyphInfos.Length; i++)
            {
                if ((i & 1023) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                var info = glyphInfos[i];
                var pos = glyphPositions[i];

                var xAdvance = pos.XAdvance / 64f;
                var yAdvance = pos.YAdvance / 64f;
                var xOffset = pos.XOffset / 64f;
                var yOffset = pos.YOffset / 64f;

                glyphs.Add(new ShapedGlyph(
                    GlyphId: (ushort)info.Codepoint,
                    Cluster: info.Cluster,
                    X: x + xOffset,
                    Y: y - yOffset,
                    XAdvance: xAdvance,
                    YAdvance: yAdvance
                ));

                x += xAdvance;
                y += yAdvance;
            }

            var maxHeight = skFont.Metrics.Descent - skFont.Metrics.Ascent;
            return new ShapedText(glyphs, x, maxHeight) { Backend = TextShapingBackend.HarfBuzz };
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            and not OutOfMemoryException
            and not AccessViolationException)
        {
            failureReason = $"HarfBuzz shaping failed ({ex.GetType().Name}): {ex.Message}";
            return null;
        }
    }

    private static ShapedText ShapeWithSkia(
        string text,
        SKFont font,
        TextShapingOptions options,
        CancellationToken cancellationToken)
    {
        var glyphIds = font.GetGlyphs(text);
        var widths = font.GetGlyphWidths(glyphIds);
        var glyphs = new List<ShapedGlyph>(glyphIds.Length);
        var clusters = GetUtf8ClusterOffsets(text, glyphIds.Length);

        if (options.Direction == TextDirection.RightToLeft)
        {
            Array.Reverse(glyphIds);
            Array.Reverse(widths);
            Array.Reverse(clusters);
        }

        float x = 0;
        for (int i = 0; i < glyphIds.Length; i++)
        {
            if ((i & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var width = widths[i];
            glyphs.Add(new ShapedGlyph(
                GlyphId: glyphIds[i],
                Cluster: clusters[i],
                X: x,
                Y: 0,
                XAdvance: width,
                YAdvance: 0
            ));
            x += width;
        }

        var maxHeight = font.Metrics.Descent - font.Metrics.Ascent;
        return new ShapedText(glyphs, x, maxHeight) { Backend = TextShapingBackend.SkiaFallback };
    }

    private static uint[] GetUtf8ClusterOffsets(string text, int glyphCount)
    {
        var offsets = new List<uint>();
        uint byteOffset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            offsets.Add(byteOffset);
            byteOffset += (uint)rune.Utf8SequenceLength;
        }

        if (offsets.Count == glyphCount)
        {
            return offsets.ToArray();
        }

        // Skia's fallback does not expose shaping clusters. Preserve UTF-8
        // offsets when counts align; otherwise provide deterministic offsets
        // without pretending they are HarfBuzz glyph indices.
        var fallback = new uint[glyphCount];
        for (var index = 0; index < fallback.Length; index++)
        {
            fallback[index] = offsets.Count == 0
                ? 0
                : offsets[Math.Min(index, offsets.Count - 1)];
        }
        return fallback;
    }

    /// <summary>
    /// Draw shaped text to a canvas.
    /// </summary>
    public void DrawShapedText(SKCanvas canvas, ShapedText shapedText, TextShapingOptions options, SKPaint paint) =>
        DrawShapedText(canvas, shapedText, options, paint, CancellationToken.None);

    public void DrawShapedText(
        SKCanvas canvas,
        ShapedText shapedText,
        TextShapingOptions options,
        SKPaint paint,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(shapedText);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(paint);
            cancellationToken.ThrowIfCancellationRequested();

            if (shapedText.Glyphs.Count == 0)
                return;
            if (shapedText.Glyphs.Count > MaxTextCharacters)
                throw new ArgumentException("Shaped glyph count exceeds the text budget", nameof(shapedText));

            var typeface = GetCachedTypeface(options.FontFamily, options.FontWeight).Typeface;
            using var font = new SKFont(typeface, (float)options.FontSize);

            var glyphIds = shapedText.Glyphs.Select(g => g.GlyphId).ToArray();
            var positions = shapedText.Glyphs.Select(g => new SKPoint(g.X, g.Y + (float)options.FontSize)).ToArray();

            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(font, glyphIds.Length);
            run.SetGlyphs(glyphIds);
            run.SetPositions(positions);

            using var textBlob = builder.Build();
            cancellationToken.ThrowIfCancellationRequested();
            if (textBlob is not null)
                canvas.DrawText(textBlob, 0, 0, paint);
        }
    }

    /// <summary>
    /// Convert shaped text to an SVG path for vector export.
    /// Returns null if glyph outlines are unavailable for the font.
    /// </summary>
    public string? GetTextAsPath(ShapedText shapedText, TextShapingOptions options)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (shapedText.Glyphs.Count == 0)
                return null;

            var typeface = GetCachedTypeface(options.FontFamily, options.FontWeight).Typeface;
            using var font = new SKFont(typeface, (float)options.FontSize);

            using var pathBuilder = new SKPathBuilder();

            foreach (var glyph in shapedText.Glyphs)
            {
                using var glyphPath = font.GetGlyphPath(glyph.GlyphId);
                if (glyphPath is null)
                    continue;

                using var translatedPath = new SKPath();
                glyphPath.Transform(SKMatrix.CreateTranslation(glyph.X, glyph.Y + (float)options.FontSize), translatedPath);
                pathBuilder.AddPath(translatedPath);
            }

            using var combinedPath = pathBuilder.Detach();
            if (combinedPath.IsEmpty)
                return null;

            return combinedPath.ToSvgPathData();
        }
    }

    /// <summary>
    /// Get the font families the platform font manager exposes. These are host
    /// dependent and are only consulted when <see cref="FontResolutionOptions.SystemFonts"/>
    /// is enabled.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableFontFamilies() =>
        SKFontManager.Default.FontFamilies.ToList();

    /// <summary>
    /// Check whether a host-installed font family exists under exactly that name
    /// (case-insensitive). Generic aliases such as <c>sans-serif</c> are not treated as
    /// available because the face they map to differs between hosts.
    /// </summary>
    public static bool IsFontFamilyAvailable(string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        using var typeface = MatchSystemTypeface(fontFamily, BundledFonts.RegularWeight);
        return typeface is not null;
    }

    private CachedTypeface GetCachedTypeface(string? fontFamily, int fontWeight)
    {
        var key = $"{fontFamily ?? string.Empty}:{fontWeight}";
        if (_typefaceCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = Resolve(fontFamily, fontWeight);
        if (_typefaceCache.Count >= MaxCachedTypefaces)
        {
            var evictionKey = _typefaceCache.Keys.First();
            _typefaceCache.Remove(evictionKey, out var evicted);
            if (evicted is { OwnsTypeface: true })
                evicted.Typeface.Dispose();
        }
        _typefaceCache[key] = resolved;
        return resolved;
    }

    private CachedTypeface Resolve(string? fontFamily, int fontWeight)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return Bundled(fontWeight, substitutedFamily: null);
        }

        var face = _directories.Match(fontFamily, fontWeight);
        if (face is not null)
        {
            return new CachedTypeface(
                face.Typeface,
                new FontResolution(FontSource.Directory, face.FamilyName, face.Weight, null),
                OwnsTypeface: false);
        }

        if (string.Equals(fontFamily, BundledFonts.FamilyName, StringComparison.OrdinalIgnoreCase))
        {
            return Bundled(fontWeight, substitutedFamily: null);
        }

        if (_fontOptions.SystemFonts)
        {
            var system = MatchSystemTypeface(fontFamily, fontWeight);
            if (system is not null)
            {
                return new CachedTypeface(
                    system,
                    new FontResolution(FontSource.System, system.FamilyName, system.FontWeight, null),
                    OwnsTypeface: true);
            }
        }

        if (_fontOptions.StrictFonts)
        {
            var searched = _fontOptions.SystemFonts
                ? "the font directories, the bundled Inter family, or the system fonts"
                : "the font directories or the bundled Inter family";
            throw new DocumentEngineException(EngineErrors.FontNotFound,
                $"Font family '{fontFamily}' was not found in {searched}");
        }

        RecordSubstitution(fontFamily, fontWeight);
        return Bundled(fontWeight, fontFamily);
    }

    private static CachedTypeface Bundled(int fontWeight, string? substitutedFamily) =>
        new(
            BundledFonts.CreateTypeface(fontWeight),
            new FontResolution(FontSource.Bundled, BundledFonts.FamilyName, BundledFonts.SelectWeight(fontWeight), substitutedFamily),
            OwnsTypeface: true);

    private void RecordSubstitution(string fontFamily, int fontWeight)
    {
        if (!_diagnosedFamilies.Add(fontFamily))
            return;
        if (_diagnostics.Count == MaxSubstitutionDiagnostics)
        {
            _diagnostics.Add(new RenderDiagnostic(RenderDiagnosticCodes.DiagnosticsTruncated,
                $"Further font substitutions are not reported after {MaxSubstitutionDiagnostics} families"));
            return;
        }
        if (_diagnostics.Count > MaxSubstitutionDiagnostics)
            return;
        var face = BundledFonts.SelectWeight(fontWeight) == BundledFonts.BoldWeight ? "Bold" : "Regular";
        _diagnostics.Add(new RenderDiagnostic(RenderDiagnosticCodes.FontSubstituted,
            $"Font family '{fontFamily}' was not found; substituted bundled {BundledFonts.FamilyName} {face}"));
    }

    /// <summary>
    /// Asks the platform font manager for <paramref name="fontFamily"/> and accepts the
    /// answer only when the returned family name matches the request, so a manager
    /// default (for example DejaVu Sans on Linux) never masquerades as the requested font.
    /// </summary>
    private static SKTypeface? MatchSystemTypeface(string fontFamily, int fontWeight)
    {
        var weight = (SKFontStyleWeight)Math.Clamp(fontWeight, 100, 900);
        using var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var typeface = SKFontManager.Default.MatchFamily(fontFamily, style);
        if (typeface is null)
            return null;
        if (!string.Equals(typeface.FamilyName, fontFamily, StringComparison.OrdinalIgnoreCase)
            || !IsUsableTypeface(typeface)
            || !HasOpenStream(typeface))
        {
            typeface.Dispose();
            return null;
        }
        return typeface;
    }

    private static bool IsUsableTypeface(SKTypeface typeface)
    {
        using var font = new SKFont(typeface, 16);
        var glyphs = font.GetGlyphs("A");
        return glyphs.Length == 1
            && glyphs[0] != 0
            && font.Metrics.Descent - font.Metrics.Ascent > 0;
    }

    private static bool HasOpenStream(SKTypeface typeface)
    {
        using var stream = typeface.OpenStream(out _);
        return stream is not null;
    }

    private sealed record CachedTypeface(SKTypeface Typeface, FontResolution Resolution, bool OwnsTypeface);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var cached in _typefaceCache.Values)
            {
                if (cached.OwnsTypeface)
                    cached.Typeface.Dispose();
            }
            _typefaceCache.Clear();
            _directories.Dispose();
        }
    }
}

public sealed record FontMetricsInfo(float Ascent, float Descent, float Leading);

/// <summary>
/// Options for text shaping.
/// </summary>
public sealed record TextShapingOptions
{
    public string? FontFamily { get; init; }
    public double FontSize { get; init; } = 16;
    public int FontWeight { get; init; } = 400;
    public string? Language { get; init; }
    public TextDirection Direction { get; init; } = TextDirection.LeftToRight;

    /// <summary>Extra advance after each glyph in pixels (SVG <c>letter-spacing</c>); default 0.</summary>
    public double LetterSpacing { get; init; }
}

/// <summary>
/// Text direction for shaping.
/// </summary>
public enum TextDirection
{
    LeftToRight,
    RightToLeft
}

/// <summary>
/// Result of text shaping containing positioned glyphs.
/// </summary>
public sealed record ShapedText(
    IReadOnlyList<ShapedGlyph> Glyphs,
    float TotalWidth,
    float Height)
{
    public TextShapingBackend Backend { get; init; }

    /// <summary>
    /// Set when HarfBuzz shaping was attempted but threw and the result fell back to Skia.
    /// Null when HarfBuzz succeeded or was never applicable, so callers can distinguish a
    /// genuine shaping failure from the ordinary fallback path.
    /// </summary>
    public string? FallbackDiagnostic { get; init; }
}

public enum TextShapingBackend
{
    None,
    HarfBuzz,
    SkiaFallback
}

/// <summary>
/// A single positioned glyph from text shaping.
/// </summary>
public sealed record ShapedGlyph(
    ushort GlyphId,
    uint Cluster,
    float X,
    float Y,
    float XAdvance,
    float YAdvance
);
