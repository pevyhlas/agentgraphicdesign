namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>One straight (unpremultiplied) sRGB pixel, 8 bits per channel.</summary>
public readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

/// <summary>
/// An internal raw raster: unpremultiplied sRGB RGBA, 8 bits per channel, row-major, top-down,
/// no row padding. Produced only by a <see cref="RenderFormat.Rgba8"/> render (whose
/// unpremultiply is the renderer's own read-back, the authoritative conversion) and consumed by
/// sheet composition and analysis. It is never a file format: no export, preview or manifest path
/// writes it.
/// </summary>
public sealed class RgbaImage
{
    /// <summary>Internal MIME marker of a <see cref="RenderFormat.Rgba8"/> result; never written to a file.</summary>
    public const string MimeType = "application/vnd.agd.rgba8";

    /// <summary>Largest side in pixels; matches the raster renderer's limit.</summary>
    public const int MaxDimension = 8192;

    /// <summary>Largest area in pixels; matches the raster renderer's limit.</summary>
    public const long MaxPixelCount = 64_000_000;

    public const int BytesPerPixel = 4;

    public RgbaImage(int width, int height, ReadOnlyMemory<byte> pixels)
    {
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(width is < 1 or > MaxDimension ? nameof(width) : nameof(height),
                $"RGBA image sides must be between 1 and {MaxDimension}; got {width}x{height}");
        }
        if ((long)width * height > MaxPixelCount)
            throw new ArgumentOutOfRangeException(nameof(width), $"RGBA image pixel count exceeds {MaxPixelCount}; got {width}x{height}");
        var expected = (long)width * height * BytesPerPixel;
        if (pixels.Length != expected)
            throw new ArgumentException($"RGBA image of {width}x{height} needs {expected} bytes; got {pixels.Length}", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The raw bytes, <c>Width·Height·4</c> long.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Wraps a <see cref="RenderFormat.Rgba8"/> render; any other result is refused.</summary>
    public static RgbaImage FromRender(RenderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.MimeType, MimeType, StringComparison.Ordinal))
            throw new ArgumentException($"Expected an {MimeType} render result; got '{result.MimeType}'", nameof(result));
        return new RgbaImage(result.Width, result.Height, result.Data);
    }

    public RgbaPixel Pixel(int x, int y)
    {
        var span = Pixels.Span.Slice(Offset(x, y), BytesPerPixel);
        return new RgbaPixel(span[0], span[1], span[2], span[3]);
    }

    public byte Alpha(int x, int y) => Pixels.Span[Offset(x, y) + 3];

    private int Offset(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        return (y * Width + x) * BytesPerPixel;
    }
}
