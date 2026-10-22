using System.Text.Json;
using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Serialization;

public sealed class FixedPrecisionDoubleConverter : JsonConverter<double>
{
    private const int MaxDecimalPlaces = 4;
    private const double MaxSafeInteger = 9007199254740991; // 2^53 - 1, max safe integer in JSON

    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (!double.IsFinite(value))
        {
            throw new JsonException($"Cannot serialize non-finite value: {value}. NaN and Infinity are not valid JSON numbers.");
        }

        var rounded = Math.Round(value, MaxDecimalPlaces);

        if (rounded == Math.Floor(rounded) && Math.Abs(rounded) <= MaxSafeInteger)
        {
            writer.WriteNumberValue((long)rounded);
        }
        else
        {
            writer.WriteNumberValue(rounded);
        }
    }
}
