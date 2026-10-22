using System.Text.Json;
using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Serialization;

/// <summary>
/// Factory that creates converters for all Dictionary&lt;string, T&gt; types,
/// ensuring keys are written in sorted (ordinal) order for deterministic output.
/// </summary>
public sealed class SortedDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        var genericDef = typeToConvert.GetGenericTypeDefinition();
        if (genericDef != typeof(Dictionary<,>))
        {
            return false;
        }

        var args = typeToConvert.GetGenericArguments();
        var keyType = args[0];
        var valueType = args[1];

        if (keyType != typeof(string))
        {
            return false;
        }

        // Exclude Dictionary<string, JsonElement> as it's used for [JsonExtensionData]
        // which has special handling in System.Text.Json that we must not override.
        if (valueType == typeof(JsonElement))
        {
            return false;
        }

        return true;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[1];
        var converterType = typeof(SortedDictionaryConverter<>).MakeGenericType(valueType);
        return (JsonConverter?)Activator.CreateInstance(converterType, options);
    }
}

/// <summary>
/// Generic converter that serializes Dictionary&lt;string, TValue&gt; with keys in ordinal sort order.
/// </summary>
internal sealed class SortedDictionaryConverter<TValue> : JsonConverter<Dictionary<string, TValue>>
{
    private readonly JsonSerializerOptions _valueOptions;

    public SortedDictionaryConverter(JsonSerializerOptions options)
    {
        _valueOptions = new JsonSerializerOptions(options);
        for (int i = _valueOptions.Converters.Count - 1; i >= 0; i--)
        {
            if (_valueOptions.Converters[i] is SortedDictionaryConverterFactory)
            {
                _valueOptions.Converters.RemoveAt(i);
            }
        }
    }

    public override Dictionary<string, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        var dictionary = new Dictionary<string, TValue>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dictionary;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            var key = reader.GetString() ?? throw new JsonException("Property name cannot be null");
            reader.Read();

            var value = JsonSerializer.Deserialize<TValue>(ref reader, _valueOptions);
            dictionary[key] = value!;
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(kvp.Key);
            JsonSerializer.Serialize(writer, kvp.Value, _valueOptions);
        }

        writer.WriteEndObject();
    }
}
