using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Serialization;

public class DocumentSerializerTests
{
    [Fact]
    public void Serialize_SameDocument_ProducesDeterministicOutput()
    {
        var doc = CreateTestDocument();

        var json1 = DocumentSerializer.Serialize(doc);
        var json2 = DocumentSerializer.Serialize(doc);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesDocument()
    {
        var original = CreateTestDocument();

        var json = DocumentSerializer.Serialize(original);
        var deserialized = DocumentSerializer.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Canvas.Width, deserialized.Canvas.Width);
        Assert.Equal(original.Canvas.Height, deserialized.Canvas.Height);
    }

    [Fact]
    public void Serialize_DoubleValues_RoundedToFourDecimalPlaces()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "precision-test",
            Canvas = new Canvas { Width = 100.123456789, Height = 200.999999999 }
        };

        var json = DocumentSerializer.Serialize(doc);

        Assert.Contains("100.1235", json);
        Assert.Contains("201", json);
    }

    [Fact]
    public void Serialize_WholeNumbers_SerializedWithoutDecimal()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "whole-number-test",
            Canvas = new Canvas { Width = 1920, Height = 1080 }
        };

        var json = DocumentSerializer.Serialize(doc);

        Assert.Contains("\"width\": 1920", json);
        Assert.Contains("\"height\": 1080", json);
        Assert.DoesNotContain("1920.0", json);
    }

    [Fact]
    public void Serialize_NullProperties_OmittedFromOutput()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "null-test"
        };

        var json = DocumentSerializer.Serialize(doc);

        Assert.DoesNotContain("\"tokens\"", json);
        Assert.DoesNotContain("\"background\"", json);
    }

    [Fact]
    public void Deserialize_PolymorphicNodes_ResolvesCorrectTypes()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "polymorphic-test",
            Root = new FrameNode
            {
                Width = 800,
                Height = 600,
                Children =
                [
                    new RectangleNode { Width = 100, Height = 50 },
                    new EllipseNode { Width = 80, Height = 80 },
                    new TextNode { Text = "Hello", FontSize = 24 }
                ]
            }
        };

        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Root.Children.Count);
        Assert.IsType<RectangleNode>(deserialized.Root.Children[0]);
        Assert.IsType<EllipseNode>(deserialized.Root.Children[1]);
        Assert.IsType<TextNode>(deserialized.Root.Children[2]);
    }

    [Fact]
    public void TryDeserialize_ValidJson_ReturnsSuccess()
    {
        var doc = CreateTestDocument();
        var json = DocumentSerializer.Serialize(doc);

        var result = DocumentSerializer.TryDeserialize(json);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Null(result.Error);
        Assert.Equal(doc.Id, result.Document.Id);
    }

    [Fact]
    public void TryDeserialize_MalformedJson_ReturnsErrorWithDetails()
    {
        var malformedJson = "{ invalid json }";

        var result = DocumentSerializer.TryDeserialize(malformedJson);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.NotNull(result.Error);
        Assert.Contains("invalid", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDeserialize_InvalidPropertyType_ReturnsErrorWithPath()
    {
        var jsonWithInvalidType = """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "name": "test",
                "canvas": { "width": "not-a-number", "height": 100 }
            }
            """;

        var result = DocumentSerializer.TryDeserialize(jsonWithInvalidType);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryDeserialize_EmptyString_ReturnsError()
    {
        var result = DocumentSerializer.TryDeserialize("");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryDeserialize_NullDocument_ReturnsError()
    {
        var result = DocumentSerializer.TryDeserialize("null");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("null", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDeserialize_IncompleteJson_ReturnsErrorWithLineNumber()
    {
        var incompleteJson = """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "name": "test"
            """;

        var result = DocumentSerializer.TryDeserialize(incompleteJson);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.Error.LineNumber);
    }

    [Fact]
    public void RoundTrip_ReSerializationEquality_ProducesSameJson()
    {
        var doc = CreateTestDocument();

        var json1 = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json1);
        Assert.NotNull(deserialized);
        var json2 = DocumentSerializer.Serialize(deserialized);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void RoundTrip_TripleSerializationEquality_ProducesSameJson()
    {
        var doc = CreateTestDocument();

        var json1 = DocumentSerializer.Serialize(doc);
        var deserialized1 = DocumentSerializer.Deserialize(json1);
        Assert.NotNull(deserialized1);
        var json2 = DocumentSerializer.Serialize(deserialized1);
        var deserialized2 = DocumentSerializer.Deserialize(json2);
        Assert.NotNull(deserialized2);
        var json3 = DocumentSerializer.Serialize(deserialized2);

        Assert.Equal(json1, json2);
        Assert.Equal(json2, json3);
    }

    [Fact]
    public void Serialize_Culture_Invariant_DeDE()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var doc = new Document
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Name = "culture-test",
                Version = 1,
                Revn = 1,
                Canvas = new Canvas { Width = 1234.5678, Height = 9876.5432 },
                Root = new FrameNode
                {
                    Id = Guid.NewGuid(),
                    Name = "root",
                    Order = "a0",
                    Width = 800.25,
                    Height = 600.75,
                    Children =
                    [
                        new RectangleNode
                        {
                            Id = Guid.NewGuid(),
                            Name = "rect",
                            Order = "a0",
                            Width = 100.5,
                            Height = 50.25,
                            Transform = new Transform
                            {
                                X = 10.125,
                                Y = 20.875,
                                Rotation = 45.5,
                                ScaleX = 1.5,
                                ScaleY = 2.25
                            }
                        }
                    ]
                }
            };

            var json = DocumentSerializer.Serialize(doc);

            Assert.DoesNotContain(",5", json);
            Assert.DoesNotContain(",25", json);
            Assert.DoesNotContain(",75", json);
            Assert.DoesNotContain(",125", json);
            Assert.DoesNotContain(",875", json);
            Assert.Contains("1234.5678", json);
            Assert.Contains("100.5", json);
            Assert.Contains("10.125", json);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Deserialize_Culture_Invariant_DeDE()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var json = """
                {
                  "version": 1,
                  "id": "00000000-0000-0000-0000-000000000001",
                  "name": "culture-test",
                  "revn": 1,
                  "canvas": {
                    "width": 1234.5678,
                    "height": 9876.5432
                  },
                  "root": {
                    "type": "frame",
                    "id": "00000000-0000-0000-0000-000000000002",
                    "name": "root",
                    "order": "a0",
                    "width": 800.25,
                    "height": 600.75,
                    "children": []
                  }
                }
                """;

            var doc = DocumentSerializer.Deserialize(json);

            Assert.NotNull(doc);
            Assert.Equal(1234.5678, doc.Canvas.Width, precision: 4);
            Assert.Equal(9876.5432, doc.Canvas.Height, precision: 4);
            Assert.Equal(800.25, doc.Root.Width);
            Assert.Equal(600.75, doc.Root.Height);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void RoundTrip_Culture_Invariant_DeDE()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var doc = CreateTestDocument();

            var json1 = DocumentSerializer.Serialize(doc);
            var deserialized = DocumentSerializer.Deserialize(json1);
            Assert.NotNull(deserialized);
            var json2 = DocumentSerializer.Serialize(deserialized);

            Assert.Equal(json1, json2);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Serialize_NonFiniteValue_ThrowsJsonException(double nonFiniteValue)
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "non-finite-test",
            Canvas = new Canvas { Width = nonFiniteValue, Height = 100 }
        };

        var ex = Assert.Throws<System.Text.Json.JsonException>(() => DocumentSerializer.Serialize(doc));
        Assert.Contains("non-finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_NaNInNodeCoordinate_ThrowsJsonException()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "nan-coordinate-test",
            Root = new FrameNode
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Name = "root",
                Width = 800,
                Height = 600,
                Children =
                [
                    new RectangleNode
                    {
                        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                        Name = "bad-rect",
                        Width = 100,
                        Height = 50,
                        Transform = new Transform { X = double.NaN, Y = 0 }
                    }
                ]
            }
        };

        var ex = Assert.Throws<System.Text.Json.JsonException>(() => DocumentSerializer.Serialize(doc));
        Assert.Contains("non-finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_LargeIntegerWithinSafeRange_UsesIntegerNotation()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "safe-integer-test",
            Canvas = new Canvas { Width = 1000000000000, Height = 100 }
        };

        var json = DocumentSerializer.Serialize(doc);
        Assert.Contains("\"width\": 1000000000000", json);
    }

    [Fact]
    public void Serialize_TypographyTokens_SortedByKey()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "typography-sort-test",
            Tokens = new DesignTokens
            {
                Typography = new Dictionary<string, TypographyToken>
                {
                    ["zebra"] = new TypographyToken { FontSize = 16 },
                    ["alpha"] = new TypographyToken { FontSize = 24 },
                    ["middle"] = new TypographyToken { FontSize = 12 }
                }
            }
        };

        var json = DocumentSerializer.Serialize(doc);

        var alphaIdx = json.IndexOf("\"alpha\"", StringComparison.Ordinal);
        var middleIdx = json.IndexOf("\"middle\"", StringComparison.Ordinal);
        var zebraIdx = json.IndexOf("\"zebra\"", StringComparison.Ordinal);

        Assert.True(alphaIdx < middleIdx, "alpha should appear before middle");
        Assert.True(middleIdx < zebraIdx, "middle should appear before zebra");
    }

    [Fact]
    public void Serialize_ColorTokens_SortedByKey()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "color-sort-test",
            Tokens = new DesignTokens
            {
                Colors = new Dictionary<string, string>
                {
                    ["warning"] = "#ffcc00",
                    ["error"] = "#ff0000",
                    ["success"] = "#00ff00"
                }
            }
        };

        var json = DocumentSerializer.Serialize(doc);

        var errorIdx = json.IndexOf("\"error\"", StringComparison.Ordinal);
        var successIdx = json.IndexOf("\"success\"", StringComparison.Ordinal);
        var warningIdx = json.IndexOf("\"warning\"", StringComparison.Ordinal);

        Assert.True(errorIdx < successIdx, "error should appear before success");
        Assert.True(successIdx < warningIdx, "success should appear before warning");
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesSortedOrder()
    {
        var doc = new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "round-trip-sort-test",
            Tokens = new DesignTokens
            {
                Typography = new Dictionary<string, TypographyToken>
                {
                    ["zebra"] = new TypographyToken { FontSize = 16 },
                    ["alpha"] = new TypographyToken { FontSize = 24 }
                },
                Colors = new Dictionary<string, string>
                {
                    ["warning"] = "#ffcc00",
                    ["error"] = "#ff0000"
                }
            }
        };

        var json1 = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json1);
        Assert.NotNull(deserialized);
        var json2 = DocumentSerializer.Serialize(deserialized);

        Assert.Equal(json1, json2);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("revn")]
    [InlineData("canvas")]
    [InlineData("root")]
    public void TryDeserialize_MissingRequiredDocumentProperty_ReturnsStructuredFailure(string property)
    {
        var json = """
            {
              "version": 1,
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "strict",
              "revn": 1,
              "canvas": { "width": 100, "height": 100 },
              "root": {
                "type": "frame",
                "id": "00000000-0000-0000-0000-000000000002",
                "children": []
              }
            }
            """;
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove(property);

        var result = DocumentSerializer.TryDeserialize(node.ToJsonString());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains(property, result.Error.Path ?? result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDeserialize_NodeMissingId_DoesNotFabricateId()
    {
        var json = """
            {
              "version": 1,
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "strict",
              "revn": 1,
              "canvas": { "width": 100, "height": 100 },
              "root": {
                "type": "frame",
                "id": "00000000-0000-0000-0000-000000000002",
                "children": [ { "type": "rectangle", "width": 10, "height": 10 } ]
              }
            }
            """;

        var result = DocumentSerializer.TryDeserialize(json);

        Assert.False(result.Success);
        Assert.Contains("id", result.Error!.Path ?? result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("canvas")]
    [InlineData("root")]
    public void TryDeserialize_NullStructuralDocumentProperty_ReturnsStructuredFailure(string property)
    {
        var json = """
            {
              "version": 1,
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "strict",
              "revn": 1,
              "canvas": { "width": 100, "height": 100 },
              "root": {
                "type": "frame",
                "id": "00000000-0000-0000-0000-000000000002",
                "children": []
              }
            }
            """;
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node[property] = null;

        var result = DocumentSerializer.TryDeserialize(node.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains(property, result.Error!.Path ?? result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDeserialize_NullChildren_ReturnsStructuredFailure()
    {
        var json = """
            {
              "version": 1,
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "strict",
              "revn": 1,
              "canvas": { "width": 100, "height": 100 },
              "root": {
                "type": "frame",
                "id": "00000000-0000-0000-0000-000000000002",
                "children": null
              }
            }
            """;

        var result = DocumentSerializer.TryDeserialize(json);

        Assert.False(result.Success);
        Assert.Contains("children", result.Error!.Path ?? result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownNodeDiscriminator_RoundTripPreservesOriginalTypeAndFields()
    {
        var json = """
            {
              "version": 1,
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "future",
              "revn": 1,
              "canvas": { "width": 100, "height": 100 },
              "root": {
                "type": "frame",
                "id": "00000000-0000-0000-0000-000000000002",
                "children": [
                  {
                    "type": "future-shape",
                    "id": "00000000-0000-0000-0000-000000000003",
                    "order": "a0",
                    "originalType": "future-payload",
                    "futureScalar": 42,
                    "futureObject": { "enabled": true }
                  }
                ]
              }
            }
            """;

        var result = DocumentSerializer.TryDeserialize(json);

        Assert.True(result.Success, result.Error?.Message);
        var unknown = Assert.IsType<UnknownNode>(result.Document!.Root.Children[0]);
        Assert.Equal("future-payload", unknown.OriginalType);
        Assert.Equal(42, unknown.ExtensionData!["futureScalar"].GetInt32());

        var roundTrip = DocumentSerializer.Serialize(result.Document);
        Assert.Contains("\"type\": \"future-shape\"", roundTrip);
        Assert.Contains("\"originalType\": \"future-payload\"", roundTrip);
        Assert.Contains("futureObject", roundTrip);

        var second = DocumentSerializer.TryDeserialize(roundTrip);
        Assert.True(second.Success, second.Error?.Message);
        Assert.Equal("future-payload", Assert.IsType<UnknownNode>(second.Document!.Root.Children[0]).OriginalType);
        Assert.Equal(roundTrip, DocumentSerializer.Serialize(second.Document));
    }

    [Fact]
    public void TryDeserialize_DuplicateProperty_ReturnsStructuredFailure()
    {
        var json = """
            {
              "version": 1,
              "version": 2,
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "duplicate",
              "revn": 1,
              "canvas": { "width": 100, "height": 100 },
              "root": {
                "type": "frame",
                "id": "00000000-0000-0000-0000-000000000002",
                "children": []
              }
            }
            """;

        var result = DocumentSerializer.TryDeserialize(json);

        Assert.False(result.Success);
        Assert.Contains("Duplicate JSON property 'version'", result.Error!.Message);
    }

    [Fact]
    public void Serialize_OversizedPersistedString_Throws()
    {
        var document = CreateTestDocument();
        document.Root.Children.Add(new TextNode
        {
            Id = Guid.NewGuid(),
            Order = "a1",
            Text = new string('x', DocumentLimits.MaxTextLength + 1)
        });

        var exception = Assert.Throws<System.Text.Json.JsonException>(() => DocumentSerializer.Serialize(document));

        Assert.Contains("maximum length", exception.Message);
    }

    [Fact]
    public void Serialize_TextLayoutFields_OmittedWhenUnsetAndRoundTripWhenSet()
    {
        var doc = CreateTestDocument();
        var plain = new TextNode { Id = Guid.NewGuid(), Order = "a5", Text = "plain" };
        var laidOut = new TextNode
        {
            Id = Guid.NewGuid(),
            Order = "a6",
            Text = "laid\nout",
            TextAlign = "center",
            VerticalAlign = "bottom",
            MaxWidth = 120.5,
            LineHeight = 1.4,
            LetterSpacing = 0.25
        };
        doc.Root.Children.Add(plain);
        doc.Root.Children.Add(laidOut);

        var json = DocumentSerializer.Serialize(doc);
        var restored = DocumentSerializer.Deserialize(json)!;

        var plainJson = System.Text.Json.Nodes.JsonNode.Parse(json)!["root"]!["children"]!.AsArray()
            .First(n => n!["id"]!.GetValue<string>() == plain.Id.ToString())!.AsObject();
        foreach (var property in new[] { "textAlign", "verticalAlign", "maxWidth", "lineHeight", "letterSpacing" })
            Assert.False(plainJson.ContainsKey(property), $"{property} should be omitted when unset");

        var restoredText = restored.Root.Children.OfType<TextNode>().Single(t => t.Id == laidOut.Id);
        Assert.Equal("laid\nout", restoredText.Text);
        Assert.Equal("center", restoredText.TextAlign);
        Assert.Equal("bottom", restoredText.VerticalAlign);
        Assert.Equal(120.5, restoredText.MaxWidth);
        Assert.Equal(1.4, restoredText.LineHeight);
        Assert.Equal(0.25, restoredText.LetterSpacing);
    }

    private static Document CreateTestDocument()
    {
        return new Document
        {
            Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
            Name = "test-document",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas
            {
                Width = 1920,
                Height = 1080,
                Background = "#ffffff"
            },
            Root = new FrameNode
            {
                Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440001"),
                Name = "root",
                Width = 1920,
                Height = 1080,
                Children =
                [
                    new RectangleNode
                    {
                        Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440002"),
                        Name = "rectangle-1",
                        Width = 200,
                        Height = 100,
                        Transform = new Transform { X = 50, Y = 50 },
                        Style = new Style
                        {
                            Fill = new Fill { Color = "#ff0000" }
                        }
                    }
                ]
            }
        };
    }
}
