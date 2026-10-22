using System.Buffers.Binary;
using System.Globalization;

namespace FTPlus.AgentGraphicDesign.Core.Import;

/// <summary>
/// Shared semantic policy for resource-bearing SVG values. XML escaping is
/// necessary for serialization, but it does not make URLs or CSS paint values
/// safe to dereference.
/// </summary>
public static class SvgResourcePolicy
{
    public const int MaxDecodedImageBytes = 1_000_000;
    public const int MaxImageDimension = 8192;
    public const long MaxImagePixels = 64_000_000;
    public const int MaxRelativeHrefCharacters = 4096;
    public const int MaxPaintCharacters = 256;
    public const int MaxDataUriCharacters = ((MaxDecodedImageBytes + 2) / 3) * 4 + 4096;

    private static readonly HashSet<string> AllowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp"
    };

    public static bool TryGetSafeImageHref(
        string? href,
        out string safeHref,
        out string reason,
        CancellationToken cancellationToken = default)
    {
        safeHref = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(href))
            return true;

        cancellationToken.ThrowIfCancellationRequested();
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryDecodeDataImage(href, out _, out _, out reason, cancellationToken))
                return false;
            safeHref = href;
            return true;
        }

        if (href.Length > MaxRelativeHrefCharacters)
        {
            reason = $"Relative image href exceeds maximum length ({MaxRelativeHrefCharacters} characters)";
            return false;
        }
        if (href.IndexOf('\0') >= 0
            || href.StartsWith('/')
            || href.StartsWith('\\')
            || Uri.TryCreate(href, UriKind.Absolute, out _)
            || HasSchemePrefix(href))
        {
            reason = "Only bounded raster data images and safe relative image references are allowed";
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(href).Replace('\\', '/');
        }
        catch (UriFormatException)
        {
            reason = "Malformed relative image href";
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pathPart = decoded.Split(['?', '#'], 2)[0];
        if (decoded.StartsWith('/')
            || HasSchemePrefix(decoded)
            || pathPart.StartsWith("//", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pathPart)
            || pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            reason = "Unsafe relative image href";
            return false;
        }

        safeHref = href;
        return true;
    }

    public static bool TryDecodeDataImage(
        string source,
        out byte[] bytes,
        out SvgDataImageInfo info,
        out string reason,
        CancellationToken cancellationToken = default)
    {
        bytes = [];
        info = default;
        reason = string.Empty;
        if (string.IsNullOrEmpty(source) || !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Image source is not a data URI";
            return false;
        }
        if (source.Length > MaxDataUriCharacters)
        {
            reason = $"Image data URI exceeds maximum source length ({MaxDataUriCharacters} characters)";
            return false;
        }

        var comma = source.IndexOf(',');
        if (comma <= 5 || comma > 128)
        {
            reason = "Malformed image data URI";
            return false;
        }

        var metadata = source.AsSpan(5, comma - 5);
        var separator = metadata.IndexOf(';');
        if (separator <= 0
            || metadata[(separator + 1)..].IndexOf(';') >= 0
            || !metadata[(separator + 1)..].Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Image data URI must use an allowed MIME type and base64 encoding";
            return false;
        }

        var mimeType = metadata[..separator].ToString();
        if (!AllowedImageMimeTypes.Contains(mimeType))
        {
            reason = "Image data URI MIME type is not allowed";
            return false;
        }

        var payload = source.AsSpan(comma + 1);
        var nonWhitespaceCharacters = 0;
        for (var index = 0; index < payload.Length; index++)
        {
            if ((index & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (!char.IsWhiteSpace(payload[index]))
                nonWhitespaceCharacters++;
        }

        var maxEncodedCharacters = ((MaxDecodedImageBytes + 2) / 3) * 4;
        if (nonWhitespaceCharacters == 0 || nonWhitespaceCharacters > maxEncodedCharacters)
        {
            reason = $"Image data URI exceeds maximum decoded size ({MaxDecodedImageBytes} bytes) or is empty";
            return false;
        }

        var maximumDecodedLength = checked(((nonWhitespaceCharacters + 3) / 4) * 3);
        var decodeBuffer = new byte[maximumDecodedLength];
        if (!Convert.TryFromBase64Chars(payload, decodeBuffer, out var bytesWritten)
            || bytesWritten == 0
            || bytesWritten > MaxDecodedImageBytes)
        {
            reason = "Malformed or oversized base64 image data URI";
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var decoded = decodeBuffer.AsSpan(0, bytesWritten);
        if (!TryReadImageInfo(decoded, mimeType, out var width, out var height))
        {
            reason = "Image bytes do not match the declared MIME type or contain unsupported headers";
            return false;
        }
        if (width <= 0 || height <= 0
            || width > MaxImageDimension || height > MaxImageDimension
            || (long)width * height > MaxImagePixels)
        {
            reason = $"Embedded image dimensions exceed {MaxImageDimension.ToString(CultureInfo.InvariantCulture)} or {MaxImagePixels.ToString(CultureInfo.InvariantCulture)} pixels";
            return false;
        }

        bytes = decodeBuffer.AsSpan(0, bytesWritten).ToArray();
        info = new SvgDataImageInfo(mimeType.ToLowerInvariant(), width, height, bytesWritten);
        return true;
    }

    public static bool IsSafePaintValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxPaintCharacters)
            return false;
        var span = value.AsSpan().Trim();
        if (span.Equals("none", StringComparison.OrdinalIgnoreCase)
            || span.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (span[0] == '#')
        {
            var hex = span[1..];
            return hex.Length is 3 or 4 or 6 or 8 && IsAsciiHex(hex);
        }

        if (IsSafeColorFunction(span, "rgb", 3) || IsSafeColorFunction(span, "rgba", 4))
            return true;

        foreach (var character in span)
        {
            if (!char.IsAsciiLetter(character) && character != '-')
                return false;
        }
        return true;
    }

    private static bool IsSafeColorFunction(ReadOnlySpan<char> value, string name, int arity)
    {
        if (!value.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            || value.Length <= name.Length + 2
            || value[name.Length] != '('
            || value[^1] != ')')
        {
            return false;
        }

        var parts = value[(name.Length + 1)..^1].ToString().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != arity)
            return false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number))
            {
                return false;
            }
            var maximum = index == 3 ? 1 : 255;
            if (number < 0 || number > maximum)
                return false;
        }
        return true;
    }

    private static bool TryReadImageInfo(ReadOnlySpan<byte> bytes, string mimeType, out int width, out int height)
    {
        width = height = 0;
        if (mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
                return false;
            var pngWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
            var pngHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
            if (pngWidth > int.MaxValue || pngHeight > int.MaxValue)
                return false;
            width = (int)pngWidth;
            height = (int)pngHeight;
            return true;
        }

        if (mimeType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length < 10
                || (!bytes[..6].SequenceEqual("GIF87a"u8) && !bytes[..6].SequenceEqual("GIF89a"u8)))
            {
                return false;
            }
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
            return true;
        }

        if (mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            return TryReadJpegDimensions(bytes, out width, out height);

        if (!mimeType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
            || bytes.Length < 30
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var chunkType = bytes.Slice(12, 4);
        if (chunkType.SequenceEqual("VP8X"u8))
        {
            width = 1 + ReadUInt24LittleEndian(bytes.Slice(24, 3));
            height = 1 + ReadUInt24LittleEndian(bytes.Slice(27, 3));
            return true;
        }
        if (chunkType.SequenceEqual("VP8L"u8) && bytes.Length >= 25 && bytes[20] == 0x2F)
        {
            width = 1 + bytes[21] + ((bytes[22] & 0x3F) << 8);
            height = 1 + (bytes[22] >> 6) + (bytes[23] << 2) + ((bytes[24] & 0x0F) << 10);
            return true;
        }
        if (chunkType.SequenceEqual("VP8 "u8)
            && bytes.Length >= 30
            && bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3FFF;
            return true;
        }
        return false;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return false;

        var index = 2;
        while (index + 3 < bytes.Length)
        {
            while (index < bytes.Length && bytes[index] != 0xFF)
                index++;
            while (index < bytes.Length && bytes[index] == 0xFF)
                index++;
            if (index >= bytes.Length)
                return false;
            var marker = bytes[index++];
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
                continue;
            if (index + 2 > bytes.Length)
                return false;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index, 2));
            if (segmentLength < 2 || index + segmentLength > bytes.Length)
                return false;
            if (IsJpegStartOfFrame(marker))
            {
                if (segmentLength < 7)
                    return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 5, 2));
                return true;
            }
            if (marker == 0xDA)
                return false;
            index += segmentLength;
        }
        return false;
    }

    private static bool IsJpegStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static bool HasSchemePrefix(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(value[0]))
            return false;
        for (var index = 1; index < colon; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
                return false;
        }
        return true;
    }

    private static bool IsAsciiHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
                return false;
        }
        return true;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> value) =>
        value[0] | (value[1] << 8) | (value[2] << 16);
}

public readonly record struct SvgDataImageInfo(string MimeType, int Width, int Height, int DecodedBytes);
