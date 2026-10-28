using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Schema;

public class SchemaTestSupportTests
{
    [Theory]
    [InlineData("true", "null", true)]
    [InlineData("false", "null", false)]
    [InlineData("{\"minProperties\":1}", "{}", false)]
    [InlineData("{\"minProperties\":1,\"maxProperties\":1}", "{\"x\":0}", true)]
    [InlineData("{\"maxProperties\":1}", "{\"x\":0,\"y\":0}", false)]
    [InlineData("{\"minItems\":1}", "[]", false)]
    [InlineData("{\"minItems\":1,\"maxItems\":1}", "[0]", true)]
    [InlineData("{\"maxItems\":1}", "[0,1]", false)]
    [InlineData("{\"items\":{\"type\":\"integer\"}}", "[1,1.5]", false)]
    [InlineData("{\"items\":false}", "[]", true)]
    [InlineData("{\"items\":false}", "[0]", false)]
    [InlineData("{\"required\":[\"x\"]}", "{}", false)]
    [InlineData("{\"properties\":{\"x\":false}}", "{\"x\":null}", false)]
    [InlineData("{\"properties\":{\"x\":{\"type\":\"integer\"}},\"additionalProperties\":false}", "{\"x\":1}", true)]
    [InlineData("{\"properties\":{\"x\":{}},\"additionalProperties\":false}", "{\"y\":1}", false)]
    [InlineData("{\"additionalProperties\":true}", "{\"y\":null}", true)]
    [InlineData("{\"additionalProperties\":{\"type\":\"integer\"}}", "{\"y\":false}", false)]
    [InlineData("{\"propertyNames\":{\"pattern\":\"^[a-z]+$\"}}", "{\"X\":0}", false)]
    [InlineData("{\"minLength\":1}", "\"\"", false)]
    [InlineData("{\"maxLength\":1}", "\"aa\"", false)]
    [InlineData("{\"minLength\":1,\"maxLength\":1}", "@emoji-json", true)]
    [InlineData("{\"minLength\":2}", "@emoji-json", false)]
    [InlineData("{\"pattern\":\"[a-z]\"}", "\"0a0\"", true)]
    [InlineData("{\"pattern\":\"^[a-z]+$\"}", "\"0a0\"", false)]
    [InlineData("{\"format\":\"uuid\"}", "\"10000000-0000-0000-0000-000000000001\"", true)]
    [InlineData("{\"format\":\"uuid\"}", "\"not-a-uuid\"", false)]
    [InlineData("{\"type\":\"number\"}", "null", false)]
    [InlineData("{\"type\":\"boolean\"}", "true", true)]
    [InlineData("{\"allOf\":[{\"minimum\":1},{\"maximum\":2}]}", "3", false)]
    [InlineData("{\"allOf\":[{\"minimum\":1},{\"maximum\":2}]}", "2", true)]
    [InlineData("{\"oneOf\":[{\"type\":\"integer\"},{\"type\":\"string\"}]}", "1", true)]
    [InlineData("{\"oneOf\":[{\"type\":\"integer\"},{\"type\":\"number\"}]}", "1", false)]
    [InlineData("{\"oneOf\":[{\"type\":\"integer\"},{\"type\":\"string\"}]}", "false", false)]
    [InlineData("{\"minItems\":1,\"minProperties\":1,\"minLength\":1,\"minimum\":1}", "null", true)]
    public void Assertions_ApplyToTheirInstanceTypes(string schema, string instance, bool valid)
    {
        // VSTest escapes non-BMP display names differently in discovery and TRX. Keep the
        // identical JSON input behind an ASCII key, not a global identity-normalization rule.
        if (instance == "@emoji-json") instance = "\"\uD83D\uDE00\"";
        Assert.Equal(valid, Validate(schema, instance).Count == 0);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1.0", true)]
    [InlineData("1e0", true)]
    [InlineData("-0", true)]
    [InlineData("-0.000e-20", true)]
    [InlineData("-12.00", true)]
    [InlineData("100e-2", true)]
    [InlineData("0.01e2", true)]
    [InlineData("1e100", true)]
    [InlineData("9223372036854775808", true)]
    [InlineData("1.1", false)]
    [InlineData("1e-1", false)]
    [InlineData("1.0000000000000000000000000000000000000001", false)]
    [InlineData("1e-100", false)]
    public void Integers_AreMathematicalNotLexicalOrInt64(string instance, bool valid)
    {
        Assert.Equal(valid, Validate("{\"type\":\"integer\"}", instance).Count == 0);
    }

    [Theory]
    [InlineData("{\"const\":1}", "1.0", true)]
    [InlineData("{\"const\":1}", "1e0", true)]
    [InlineData("{\"const\":0}", "-0", true)]
    [InlineData("{\"enum\":[0,1]}", "1e0", true)]
    [InlineData("{\"const\":1}", "1.0000000000000000000000000000000000000001", false)]
    [InlineData("{\"enum\":[1]}", "1.0000000000000000000000000000000000000001", false)]
    [InlineData("{\"minimum\":-1000000}", "-1000000", true)]
    [InlineData("{\"minimum\":-1000000}", "-1000000.00000000000000000000000000000001", false)]
    [InlineData("{\"maximum\":1000000}", "1000000", true)]
    [InlineData("{\"maximum\":1000000}", "1000000.00000000000000000000000000000001", false)]
    [InlineData("{\"maximum\":9007199254740992}", "9007199254740993", false)]
    [InlineData("{\"minimum\":9007199254740993}", "9007199254740992", false)]
    [InlineData("{\"maximum\":1e100}", "1.000000000000000000000000000000000000001e100", false)]
    [InlineData("{\"minimum\":1e-100}", "0", false)]
    [InlineData("{\"exclusiveMinimum\":1}", "1", false)]
    [InlineData("{\"exclusiveMinimum\":1}", "1.0000000000000000000000000000000000000001", true)]
    [InlineData("{\"multipleOf\":0.000001}", "0.000001", true)]
    [InlineData("{\"multipleOf\":0.000001}", "-999999.999999", true)]
    [InlineData("{\"multipleOf\":0.000001}", "-0", true)]
    [InlineData("{\"multipleOf\":0.000001}", "1.0000000", true)]
    [InlineData("{\"multipleOf\":0.000001}", "1e-6", true)]
    [InlineData("{\"multipleOf\":0.000001}", "0.0000001", false)]
    [InlineData("{\"multipleOf\":0.000001}", "1.0000000000000000000000000000000000000001", false)]
    [InlineData("{\"multipleOf\":0.000001}", "0.9999999999999999999999999999999999999999", false)]
    [InlineData("{\"multipleOf\":0.000001}", "-0.000001000000000000000000000000000000001", false)]
    [InlineData("{\"multipleOf\":1e-100}", "3e-100", true)]
    [InlineData("{\"multipleOf\":1e-100}", "1e-101", false)]
    public void NumericAssertions_UseExactValuesWithoutPrecisionEpsilon(string schema, string instance, bool valid)
    {
        Assert.Equal(valid, Validate(schema, instance).Count == 0);
    }

    [Theory]
    [InlineData("{\"uniqueItems\":true}")]
    [InlineData("{\"maximim\":0}")]
    [InlineData("{\"exclusiveMaximum\":0}")]
    [InlineData("{\"properties\":{\"absent\":{\"not\":{}}}}")]
    [InlineData("{\"$defs\":{\"unused\":{\"if\":{}}}}")]
    [InlineData("{\"oneOf\":[{}, {\"type\":\"string\",\"unknown\":true}]}")]
    [InlineData("{\"items\":{\"contains\":{}}}")]
    [InlineData("{\"additionalProperties\":{\"patternProperties\":{}}}")]
    [InlineData("{\"$defs\":{\"ok\":{}},\"$ref\":\"#/$defs/ok\",\"not\":{}}")]
    [InlineData("{\"format\":\"email\"}")]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{\"type\":[\"string\",\"integer\"]}")]
    [InlineData("{\"items\":[{}]}")]
    [InlineData("{\"$ref\":\"https://example.invalid/schema\"}")]
    [InlineData("{\"multipleOf\":0}")]
    [InlineData("{\"multipleOf\":-1}")]
    public void UnsupportedKeywordsAndForms_ThrowEvenWhenNotVisited(string schema)
    {
        Assert.Throws<NotSupportedException>(() => Validate(schema, "{}"));
    }

    [Theory]
    [InlineData("""{"required":true}""")]
    [InlineData("""{"required":["x",null]}""")]
    [InlineData("""{"required":["x","x"]}""")]
    [InlineData("""{"pattern":true}""")]
    [InlineData("""{"pattern":"["}""")]
    [InlineData("""{"enum":true}""")]
    [InlineData("""{"enum":[]}""")]
    [InlineData("""{"type":true}""")]
    [InlineData("""{"format":true}""")]
    [InlineData("""{"minimum":"0"}""")]
    [InlineData("""{"maximum":false}""")]
    [InlineData("""{"exclusiveMinimum":null}""")]
    [InlineData("""{"multipleOf":true}""")]
    [InlineData("""{"$defs":[]}""")]
    [InlineData("""{"properties":true}""")]
    [InlineData("""{"items":"integer"}""")]
    [InlineData("""{"propertyNames":null}""")]
    [InlineData("""{"additionalProperties":0}""")]
    [InlineData("""{"allOf":true}""")]
    [InlineData("""{"oneOf":{}}""")]
    [InlineData("""{"allOf":[]}""")]
    [InlineData("""{"oneOf":[]}""")]
    [InlineData("""{"allOf":[null]}""")]
    [InlineData("""{"oneOf":["integer"]}""")]
    [InlineData("""{"$ref":true}""")]
    [InlineData("""{"$ref":"#/$defs/missing"}""")]
    [InlineData("""{"$schema":true}""")]
    [InlineData("""{"$id":true}""")]
    [InlineData("""{"$comment":true}""")]
    [InlineData("""{"title":true}""")]
    [InlineData("""{"description":true}""")]
    [InlineData("""{"discriminator":true}""")]
    [InlineData("""{"discriminator":{"propertyName":true}}""")]
    public void MalformedKeywordForms_ThrowAtRootAndInUnusedSchemas(string malformed)
    {
        Assert.Throws<NotSupportedException>(() => Validate(malformed, "null"));
        Assert.Throws<NotSupportedException>(() => Validate($$"""{"properties":{"absent":{{malformed}} } }""", "{}"));
        Assert.Throws<NotSupportedException>(() => Validate($$"""{"$defs":{"unused":{{malformed}} } }""", "null"));
    }

    [Theory]
    [InlineData("minProperties", "{\"x\":0}", "{}")]
    [InlineData("maxProperties", "{\"x\":0}", "{\"x\":0,\"y\":0}")]
    [InlineData("minItems", "[0]", "[]")]
    [InlineData("maxItems", "[0]", "[0,0]")]
    [InlineData("minLength", "\"x\"", "\"\"")]
    [InlineData("maxLength", "\"x\"", "\"xx\"")]
    public void CountsAndLengths_PreflightExactNonnegativeIntegersWithinHelperBounds(string keyword, string valid, string invalid)
    {
        foreach (var raw in new[] { "true", "null", "\"1\"", "-1", "0.5", "1.0000000000000000000000000000000000001", "2147483648" })
        {
            var schema = $$"""{"$defs":{"unused":{"{{keyword}}":{{raw}} } } }""";
            Assert.Throws<NotSupportedException>(() => Validate(schema, "null"));
        }
        foreach (var raw in new[] { "1", "1.0", "1e0" })
        {
            var schema = $$"""{"{{keyword}}":{{raw}}}""";
            Assert.Empty(Validate(schema, valid));
            Assert.NotEmpty(Validate(schema, invalid));
        }
        Assert.Empty(Validate($$"""{"{{keyword}}":-0}""", "null"));
        Assert.Empty(Validate($$"""{"{{keyword}}":2147483647}""", "null"));
    }

    [Theory]
    [InlineData("""{"default":{"not":{}},"$ref":"#/default"}""")]
    [InlineData("""{"default":{"not":{}},"properties":{"absent":{"$ref":"#/default"}}}""")]
    [InlineData("""{"default":{"not":{}},"$defs":{"unused":{"$ref":"#/default"}}}""")]
    public void ReferenceTargets_InAnnotationDataArePreflighted(string schema)
    {
        var error = Assert.Throws<NotSupportedException>(() => Validate(schema, "null"));
        Assert.Contains("Unsupported schema keyword: not", error.Message);
    }

    [Theory]
    [InlineData("""{"default":{"required":true},"$ref":"#/default"}""")]
    [InlineData("""{"default":42,"$ref":"#/default"}""")]
    [InlineData("""{"default":null,"$ref":"#/default/child"}""")]
    public void ReferenceTargets_RejectMalformedSchemasAndUnsupportedLocations(string schema)
    {
        Assert.Throws<NotSupportedException>(() => Validate(schema, "null"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void NestedIds_AreRejectedRatherThanUsingTheWrongReferenceScope(string instance)
    {
        const string schema = """
            {"$id":"urn:outer","$defs":{"v":{"const":1},"nested":{
              "$id":"urn:independent","$defs":{"v":{"const":2}},"$ref":"#/$defs/v"}},
             "$ref":"#/$defs/nested"}
            """;
        var error = Assert.Throws<NotSupportedException>(() => Validate(schema, instance));
        Assert.Contains("Only root $id", error.Message);
    }

    [Theory]
    [InlineData("""{"properties":{"absent":{"$id":"urn:nested"}}}""")]
    [InlineData("""{"default":{"$id":"urn:nested"},"$ref":"#/default"}""")]
    public void NestedIds_AreRejectedInUnusedSchemasAndReferenceTargets(string schema)
    {
        Assert.Throws<NotSupportedException>(() => Validate(schema, "null"));
    }

    [Theory]
    [InlineData("""{"default":{"$ref":"#/default"},"$ref":"#/default"}""")]
    [InlineData("""{"$defs":{"a":{"$ref":"#/$defs/b"},"b":{"allOf":[{"$ref":"#/$defs/a"}] }},"$ref":"#/$defs/a"}""")]
    [InlineData("""{"$defs":{"a":{"oneOf":[true,{"$ref":"#/$defs/a"}]}},"$ref":"#/$defs/a"}""")]
    public void NonProgressingReferenceCycles_ThrowInsteadOfOverflowing(string schema)
    {
        var error = Assert.Throws<NotSupportedException>(() => Validate(schema, "null"));
        Assert.Contains("Non-progressing schema reference cycle", error.Message);
    }

    [Theory]
    [InlineData("{\"value\":1,\"children\":[{\"value\":2},{\"value\":2}]}", true)]
    [InlineData("{\"value\":1,\"children\":[{\"value\":2,\"children\":[{\"value\":3}]}]}", true)]
    [InlineData("{\"value\":1,\"children\":[{\"value\":1.5}]}", false)]
    [InlineData("{\"value\":1,\"children\":[{}]}", false)]
    public void RecursiveSchemas_AllowProgressToChildInstancesAndRepeatedCompletedReferences(string instance, bool valid)
    {
        const string schema = """
            {"$defs":{"node":{"type":"object","required":["value"],"properties":{
              "value":{"type":"integer"},"children":{"type":"array","items":{"$ref":"#/$defs/node"}}}}},
             "allOf":[{"$ref":"#/$defs/node"},{"$ref":"#/$defs/node"}]}
            """;
        Assert.Equal(valid, Validate(schema, instance).Count == 0);
    }

    [Fact]
    public void ExplicitAnnotations_DoNotAssertOrRecurseIntoAnnotationData()
    {
        const string schema = """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"urn:test",
             "$comment":"test", "title":"test", "description":"test",
             "default":{"not-a-keyword":true}, "discriminator":{"propertyName":"type"}}
            """;
        Assert.Empty(Validate(schema, "null"));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1.0", true)]
    [InlineData("2", false)]
    [InlineData("1.5", false)]
    public void References_RespectSiblingAssertions(string instance, bool valid)
    {
        const string schema = """
            {"$defs":{"value":{"type":"integer","minimum":1}},
             "$ref":"#/$defs/value","maximum":1}
            """;
        Assert.Equal(valid, Validate(schema, instance).Count == 0);
    }

    [Fact]
    public void References_DecodeJsonPointerSegments()
    {
        const string schema = """{"$defs":{"a/b~c":{"const":1}},"$ref":"#/$defs/a~1b~0c"}""";
        Assert.Empty(Validate(schema, "1"));
        Assert.NotEmpty(Validate(schema, "2"));
    }

    [Theory]
    [InlineData("1e4097")]
    [InlineData("1e-4097")]
    [InlineData("1e99999999999999999999")]
    [InlineData("0.01e-4096")]
    public void NumericTestHelperLimits_ThrowRatherThanRoundOrAccept(string instance)
    {
        Assert.Throws<NotSupportedException>(() => Validate("{\"type\":\"number\"}", instance));
    }

    [Fact]
    public void NumericTokenTestHelperLimit_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Validate("{\"type\":\"integer\"}", new string('1', 4097)));
    }

    private static List<string> Validate(string schemaJson, string instanceJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        using var instance = JsonDocument.Parse(instanceJson);
        return SchemaTestSupport.Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$");
    }
}
