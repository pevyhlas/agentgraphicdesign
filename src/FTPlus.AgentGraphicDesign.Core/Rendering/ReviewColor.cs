using FTPlus.AgentGraphicDesign.Core.Lint;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>CSS order, opacity after byte rounding; never changes authored document colours.</summary>
internal static class ReviewColor
{
    internal static string ParseOpaque(string? value)
    {
        if (!SrgbColor.TryParse(value, out var color) || ToByte(color.A) != 255)
            throw new DocumentEngineException(EngineErrors.InvalidProperty, "Review colour must be an opaque CSS colour");
        return $"#{ToByte(color.R):x2}{ToByte(color.G):x2}{ToByte(color.B):x2}";
    }

    internal static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.ToEven);
}
