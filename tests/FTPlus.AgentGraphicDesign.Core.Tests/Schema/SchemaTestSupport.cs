using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Schema;

// Test-only evaluator for assertions used by the two checked-in schemas, not a general
// JSON Schema runtime. Unsupported keywords/forms throw, even in unvisited branches.
internal static class SchemaTestSupport
{
    internal static List<string> Validate(JsonElement instance, JsonElement schema, JsonElement schemaRoot, string path)
    {
        var visited = new HashSet<JsonElement>();
        CheckSupportedSchema(schemaRoot, schemaRoot, visited);
        CheckSupportedSchema(schema, schemaRoot, visited);
        return ValidateCore(instance, schema, schemaRoot, path, []);
    }

    private static void CheckSupportedSchema(JsonElement schema, JsonElement schemaRoot, HashSet<JsonElement> visited)
    {
        if (!visited.Add(schema)) return;
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False) return;
        if (schema.ValueKind != JsonValueKind.Object) throw new NotSupportedException("Expected an object or boolean schema");

        foreach (var keyword in schema.EnumerateObject())
        {
            switch (keyword.Name)
            {
                // Explicit annotation allowlist; discriminator is a vector-schema hint.
                case "$id":
                    if (!schema.Equals(schemaRoot)) throw new NotSupportedException("Only root $id is supported; nested reference scopes are not");
                    goto case "title";
                case "$schema":
                case "$comment":
                case "title":
                case "description":
                    if (keyword.Value.ValueKind != JsonValueKind.String)
                        throw new NotSupportedException($"{keyword.Name} must be a string");
                    break;
                case "default":
                case "const":
                    break;
                case "discriminator":
                    if (keyword.Value.ValueKind != JsonValueKind.Object || keyword.Value.EnumerateObject().Count() != 1 ||
                        !keyword.Value.TryGetProperty("propertyName", out var propertyName) || propertyName.ValueKind != JsonValueKind.String)
                        throw new NotSupportedException("Only a discriminator propertyName string is supported");
                    break;
                case "enum":
                    if (keyword.Value.ValueKind != JsonValueKind.Array || keyword.Value.GetArrayLength() == 0)
                        throw new NotSupportedException("enum must be a nonempty array");
                    break;
                case "required":
                    if (keyword.Value.ValueKind != JsonValueKind.Array ||
                        keyword.Value.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String) ||
                        keyword.Value.EnumerateArray().Select(value => value.GetString()).Distinct(StringComparer.Ordinal).Count() != keyword.Value.GetArrayLength())
                        throw new NotSupportedException("required must be an array of unique strings");
                    break;
                case "minProperties":
                case "maxProperties":
                case "minItems":
                case "maxItems":
                case "minLength":
                case "maxLength":
                    ReadCount(keyword.Value);
                    break;
                case "pattern":
                    if (keyword.Value.ValueKind != JsonValueKind.String) throw new NotSupportedException("pattern must be a string");
                    try
                    {
                        _ = new Regex(keyword.Value.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new NotSupportedException("Unsupported pattern syntax", ex);
                    }
                    break;
                case "$ref":
                    if (keyword.Value.ValueKind != JsonValueKind.String) throw new NotSupportedException("$ref must be a string");
                    CheckSupportedSchema(ResolveReference(schemaRoot, keyword.Value.GetString()!), schemaRoot, visited);
                    break;
                case "type":
                    if (keyword.Value.ValueKind != JsonValueKind.String || keyword.Value.GetString() is not
                        ("object" or "array" or "string" or "boolean" or "number" or "integer"))
                        throw new NotSupportedException("Unsupported schema type");
                    break;
                case "format":
                    if (keyword.Value.ValueKind != JsonValueKind.String || keyword.Value.GetString() != "uuid")
                        throw new NotSupportedException("Only the vector schema's UUID format check is supported");
                    break;
                case "minimum":
                case "maximum":
                case "exclusiveMinimum":
                case "multipleOf":
                    var number = ReadNumber(keyword.Value);
                    if (keyword.Name == "multipleOf" && number.Numerator <= 0)
                        throw new NotSupportedException("multipleOf must be positive");
                    break;
                case "$defs":
                case "properties":
                    if (keyword.Value.ValueKind != JsonValueKind.Object) throw new NotSupportedException($"{keyword.Name} must be an object of schemas");
                    foreach (var child in keyword.Value.EnumerateObject()) CheckSupportedSchema(child.Value, schemaRoot, visited);
                    break;
                case "allOf":
                case "oneOf":
                    if (keyword.Value.ValueKind != JsonValueKind.Array || keyword.Value.GetArrayLength() == 0)
                        throw new NotSupportedException($"{keyword.Name} must be a nonempty array of schemas");
                    foreach (var child in keyword.Value.EnumerateArray()) CheckSupportedSchema(child, schemaRoot, visited);
                    break;
                case "items":
                case "propertyNames":
                case "additionalProperties":
                    CheckSupportedSchema(keyword.Value, schemaRoot, visited);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported schema keyword: {keyword.Name}");
            }
        }
    }

    private static List<string> ValidateCore(JsonElement instance, JsonElement schema, JsonElement schemaRoot, string path,
        HashSet<(JsonElement Instance, JsonElement Schema)> active)
    {
        // Track identity, not value equality: recursive schemas may revisit the same
        // schema at a child instance, but must not loop at the current instance.
        if (!active.Add((instance, schema))) throw new NotSupportedException("Non-progressing schema reference cycle");
        try
        {
            var errors = new List<string>();
            if (schema.ValueKind == JsonValueKind.True) return errors;
            if (schema.ValueKind == JsonValueKind.False) return [$"{path}: rejected by false schema"];
            if (schema.TryGetProperty("$ref", out var reference))
            {
                errors.AddRange(ValidateCore(instance, ResolveReference(schemaRoot, reference.GetString()!), schemaRoot, path, active));
            }
            if (schema.TryGetProperty("allOf", out var allOf))
            {
                foreach (var part in allOf.EnumerateArray())
                {
                    errors.AddRange(ValidateCore(instance, part, schemaRoot, path, active));
                }
            }
            if (schema.TryGetProperty("oneOf", out var oneOf))
            {
                var candidateErrors = oneOf.EnumerateArray()
                    .Select(candidate => ValidateCore(instance, candidate, schemaRoot, path, active))
                    .ToArray();
                var matches = candidateErrors.Count(candidate => candidate.Count == 0);
                if (matches != 1)
                {
                    var detail = string.Join(" | ", candidateErrors.Select((candidate, index) =>
                        $"candidate {index}: {string.Join(", ", candidate.Take(2))}"));
                    errors.Add($"{path}: expected exactly one schema match, got {matches}; {detail}");
                }
            }
            if (schema.TryGetProperty("const", out var constant) && !ValuesEqual(instance, constant))
            {
                errors.Add($"{path}: value does not match const {constant.GetRawText()}");
            }
            if (schema.TryGetProperty("enum", out var enumValues)
                && !enumValues.EnumerateArray().Any(value => ValuesEqual(instance, value)))
            {
                errors.Add($"{path}: value is not in enum");
            }
            if (schema.TryGetProperty("type", out var type))
            {
                ValidateType(instance, type.GetString()!, path, errors);
            }

            if (instance.ValueKind == JsonValueKind.Object)
            {
                if (schema.TryGetProperty("minProperties", out var minProperties) &&
                    instance.EnumerateObject().Count() < ReadCount(minProperties))
                {
                    errors.Add($"{path}: object has fewer than {ReadCount(minProperties)} properties");
                }
                if (schema.TryGetProperty("maxProperties", out var maxProperties) &&
                    instance.EnumerateObject().Count() > ReadCount(maxProperties))
                {
                    errors.Add($"{path}: object has more than {ReadCount(maxProperties)} properties");
                }
                if (schema.TryGetProperty("propertyNames", out var propertyNames))
                {
                    foreach (var property in instance.EnumerateObject())
                    {
                        using var propertyName = JsonDocument.Parse(JsonSerializer.Serialize(property.Name));
                        errors.AddRange(ValidateCore(propertyName.RootElement, propertyNames, schemaRoot, $"{path}.{property.Name}", active));
                    }
                }
                if (schema.TryGetProperty("required", out var required))
                {
                    foreach (var property in required.EnumerateArray().Select(item => item.GetString()!))
                    {
                        if (!instance.TryGetProperty(property, out _)) errors.Add($"{path}: missing required property {property}");
                    }
                }
                if (schema.TryGetProperty("properties", out var properties))
                {
                    foreach (var propertySchema in properties.EnumerateObject())
                    {
                        if (instance.TryGetProperty(propertySchema.Name, out var propertyValue))
                        {
                            errors.AddRange(ValidateCore(propertyValue, propertySchema.Value, schemaRoot, $"{path}.{propertySchema.Name}", active));
                        }
                    }
                }
                if (schema.TryGetProperty("additionalProperties", out var additionalProperties))
                {
                    var declared = schema.TryGetProperty("properties", out var declaredProperties)
                        ? declaredProperties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                        : [];
                    foreach (var property in instance.EnumerateObject().Where(property => !declared.Contains(property.Name)))
                    {
                        errors.AddRange(ValidateCore(property.Value, additionalProperties, schemaRoot, $"{path}.{property.Name}", active));
                    }
                }
            }
            else if (instance.ValueKind == JsonValueKind.Array)
            {
                if (schema.TryGetProperty("minItems", out var minItems) && instance.GetArrayLength() < ReadCount(minItems))
                    errors.Add($"{path}: array has fewer than {ReadCount(minItems)} items");
                if (schema.TryGetProperty("maxItems", out var maxItems) && instance.GetArrayLength() > ReadCount(maxItems))
                    errors.Add($"{path}: array has more than {ReadCount(maxItems)} items");
                if (schema.TryGetProperty("items", out var items))
                {
                    var index = 0;
                    foreach (var item in instance.EnumerateArray())
                    {
                        errors.AddRange(ValidateCore(item, items, schemaRoot, $"{path}[{index++}]", active));
                    }
                }
            }
            else if (instance.ValueKind == JsonValueKind.String)
            {
                var value = instance.GetString()!;
                var length = value.EnumerateRunes().Count();
                if (schema.TryGetProperty("minLength", out var minLength) && length < ReadCount(minLength))
                {
                    errors.Add($"{path}: string is shorter than {ReadCount(minLength)}");
                }
                if (schema.TryGetProperty("maxLength", out var maxLength) && length > ReadCount(maxLength))
                {
                    errors.Add($"{path}: string is longer than {ReadCount(maxLength)}");
                }
                if (schema.TryGetProperty("pattern", out var pattern) &&
                    !Regex.IsMatch(value, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    errors.Add($"{path}: string does not match pattern");
                }
                if (schema.TryGetProperty("format", out _) && !Guid.TryParse(value, out _))
                {
                    errors.Add($"{path}: string is not a UUID");
                }
            }
            else if (instance.ValueKind == JsonValueKind.Number)
            {
                var value = ReadNumber(instance);
                if (schema.TryGetProperty("minimum", out var minimum) && Compare(value, ReadNumber(minimum)) < 0) errors.Add($"{path}: below minimum");
                if (schema.TryGetProperty("maximum", out var maximum) && Compare(value, ReadNumber(maximum)) > 0) errors.Add($"{path}: above maximum");
                if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimum) && Compare(value, ReadNumber(exclusiveMinimum)) <= 0) errors.Add($"{path}: below exclusive minimum");
                if (schema.TryGetProperty("multipleOf", out var multipleOf))
                {
                    var divisor = ReadNumber(multipleOf);
                    if (value.Numerator * divisor.Denominator % (value.Denominator * divisor.Numerator) != 0)
                        errors.Add($"{path}: not a multipleOf value");
                }
            }

            return errors;
        }
        finally
        {
            active.Remove((instance, schema));
        }
    }

    private static void ValidateType(JsonElement instance, string expectedType, string path, ICollection<string> errors)
    {
        var valid = expectedType switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number && IsInteger(instance),
            _ => throw new NotSupportedException($"Unsupported schema type: {expectedType}")
        };
        if (!valid) errors.Add($"{path}: expected {expectedType}, got {instance.ValueKind}");
    }

    private static bool IsInteger(JsonElement value)
    {
        var number = ReadNumber(value);
        return number.Numerator % number.Denominator == 0;
    }

    private static int ReadCount(JsonElement value)
    {
        var number = ReadNumber(value);
        if (number.Numerator < 0 || number.Numerator % number.Denominator != 0 ||
            number.Numerator / number.Denominator > int.MaxValue)
            throw new NotSupportedException("Test schema evaluator counts/lengths must be nonnegative integers within Int32");
        return (int)(number.Numerator / number.Denominator);
    }

    private static bool ValuesEqual(JsonElement left, JsonElement right) =>
        left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number
            ? Compare(ReadNumber(left), ReadNumber(right)) == 0
            : JsonElement.DeepEquals(left, right);

    private static int Compare((BigInteger Numerator, BigInteger Denominator) left,
        (BigInteger Numerator, BigInteger Denominator) right) =>
        (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);

    private static (BigInteger Numerator, BigInteger Denominator) ReadNumber(JsonElement value)
    {
        // Exact decimal rationals, deliberately bounded for tests. Never round via
        // double/decimal: even an epsilon beyond a bound or a multiple must fail.
        const int limit = 4096;
        var raw = value.GetRawText();
        if (value.ValueKind != JsonValueKind.Number || raw.Length > limit)
            throw new NotSupportedException("Test schema evaluator numeric token limit exceeded or non-number supplied");
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0 &&
            (!int.TryParse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)
             || exponent < -limit || exponent > limit))
            throw new NotSupportedException("Test schema evaluator exponent limit exceeded");
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var dot = mantissa.IndexOf('.');
        var scale = (dot < 0 ? 0 : mantissa.Length - dot - 1) - exponent;
        if (scale < -limit || scale > limit) throw new NotSupportedException("Test schema evaluator scale limit exceeded");
        var numerator = BigInteger.Parse(mantissa.Replace(".", "", StringComparison.Ordinal),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return scale >= 0
            ? (numerator, BigInteger.Pow(10, scale))
            : (numerator * BigInteger.Pow(10, -scale), BigInteger.One);
    }

    internal static JsonElement ResolveReference(JsonElement schemaRoot, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Only local schema references are supported: {reference}");
        }

        var current = schemaRoot;
        foreach (var segment in reference[2..].Split('/'))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal), out current))
                throw new NotSupportedException($"Unresolved or unsupported local schema reference: {reference}");
        }
        return current;
    }
}
