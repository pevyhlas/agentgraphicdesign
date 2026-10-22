using System.Globalization;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>An 8-bit RGBA color decoded from a strict color literal.</summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A);

/// <summary>
/// Decodes the strict color grammar accepted by <see cref="DocumentLimits.IsValidColor"/>
/// (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, <c>rgb()</c>, <c>rgba()</c>)
/// into normalized RGBA bytes so differently spelled literals can be compared exactly.
/// </summary>
public static class ColorLiteral
{
    public static bool TryParse(string? color, out RgbaColor rgba)
    {
        rgba = default;
        if (!DocumentLimits.IsValidColor(color))
            return false;

        if (color![0] == '#')
            return TryParseHex(color[1..], out rgba);

        var arguments = color[(color.IndexOf('(') + 1)..^1].Split(',');
        if (!TryParseChannel(arguments[0], out var red)
            || !TryParseChannel(arguments[1], out var green)
            || !TryParseChannel(arguments[2], out var blue))
        {
            return false;
        }

        byte alpha = 255;
        if (arguments.Length == 4)
        {
            if (!double.TryParse(arguments[3].Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var opacity))
                return false;
            alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        }

        rgba = new RgbaColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseHex(string hex, out RgbaColor rgba)
    {
        rgba = default;
        if (hex.Length is 3 or 4)
            hex = string.Concat(hex.Select(character => new string(character, 2)));

        byte alpha = 255;
        if (!TryParseHexByte(hex[..2], out var red)
            || !TryParseHexByte(hex[2..4], out var green)
            || !TryParseHexByte(hex[4..6], out var blue)
            || (hex.Length == 8 && !TryParseHexByte(hex[6..8], out alpha)))
        {
            return false;
        }

        rgba = new RgbaColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseHexByte(string value, out byte result) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    private static bool TryParseChannel(string value, out byte result) =>
        byte.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out result);
}
