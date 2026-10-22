using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;
using static FTPlus.AgentGraphicDesign.Core.Tests.Schema.SchemaTestSupport;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Schema;

public class JsonSchemaContractTests
{
    private static readonly IReadOnlyDictionary<Type, string> ModelDefinitions = new Dictionary<Type, string>
    {
        [typeof(Canvas)] = "canvas",
        [typeof(DesignTokens)] = "designTokens",
        [typeof(TypographyToken)] = "typographyToken",
        [typeof(Node)] = "nodeBase",
        [typeof(Transform)] = "transform",
        [typeof(Style)] = "style",
        [typeof(Fill)] = "fill",
        [typeof(Gradient)] = "gradientBase",
        [typeof(LinearGradient)] = "linearGradient",
        [typeof(RadialGradient)] = "radialGradient",
        [typeof(GradientStop)] = "gradientStop",
        [typeof(Stroke)] = "stroke",
        [typeof(FrameNode)] = "frameNode",
        [typeof(GroupNode)] = "groupNode",
        [typeof(RectangleNode)] = "rectangleNode",
        [typeof(EllipseNode)] = "ellipseNode",
        [typeof(PathNode)] = "pathNode",
        [typeof(TextNode)] = "textNode",
        [typeof(LineNode)] = "lineNode",
        [typeof(PolygonNode)] = "polygonNode",
        [typeof(Point)] = "point",
        [typeof(ImageNode)] = "imageNode",
        [typeof(UnknownNode)] = "unknownNode"
    };

    [Fact]
    public void Schema_DeclaresEveryCurrentModelField()
    {
        using var schema = LoadSchema();
        var root = schema.RootElement;
        AssertModelProperties(typeof(Document), root, root, "document");

        foreach (var (type, definitionName) in ModelDefinitions)
        {
            var definition = root.GetProperty("$defs").GetProperty(definitionName);
            AssertModelProperties(type, definition, root, definitionName);
        }
    }

    [Fact]
    public void Schema_NodeAndGradientDiscriminatorsMatchSerializerRegistrations()
    {
        using var schema = LoadSchema();
        AssertDiscriminatorContract(typeof(Node), schema.RootElement.GetProperty("$defs").GetProperty("node"), schema.RootElement);
        AssertDiscriminatorContract(typeof(Gradient), schema.RootElement.GetProperty("$defs").GetProperty("gradient"), schema.RootElement);
    }

    [Fact]
    public void Schema_ValidatesSerializedAllFieldsDocument()
    {
        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(DocumentSerializer.Serialize(CreateAllFieldsDocument()));
        var errors = Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$");
        Assert.Empty(errors);
    }

    [Fact]
    public void Schema_ValidatesAllSupportedExamples()
    {
        var root = FindRepositoryRoot();
        using var schema = LoadSchema();
        var examples = Directory.GetFiles(Path.Combine(root, "examples"), "*.agd.json")
            .Where(path => !Path.GetFileName(path).StartsWith("invalid-", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(examples);

        foreach (var example in examples)
        {
            using var instance = JsonDocument.Parse(File.ReadAllText(example));
            var errors = Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$");
            Assert.True(errors.Count == 0, $"{Path.GetFileName(example)} failed schema contract: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void Schema_RejectsNegativeFixtures()
    {
        var root = FindRepositoryRoot();
        using var schema = LoadSchema();
        var fixtures = Directory.GetFiles(
            Path.Combine(root, "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Schema", "Fixtures"),
            "invalid-*.agd.json");
        Assert.NotEmpty(fixtures);

        foreach (var fixture in fixtures)
        {
            using var instance = JsonDocument.Parse(File.ReadAllText(fixture));
            var errors = Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$");
            Assert.NotEmpty(errors);
        }
    }

    [Fact]
    public void Schema_AndParser_RejectContainerWithoutChildren()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Schema", "Fixtures",
            "invalid-missing-children.agd.json");
        var json = File.ReadAllText(fixture);
        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(json);

        Assert.NotEmpty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
        Assert.False(DocumentSerializer.TryDeserialize(json).Success);
    }

    [Fact]
    public void Schema_AcceptsStrictTokenAliasGrammar()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Schema", "Fixtures",
            "valid-token-alias.agd.json");
        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(File.ReadAllText(fixture));

        Assert.Empty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("#11223344")]
    [InlineData("rgb(0, 127, 255)")]
    [InlineData("RGBA(255, 0, 12, .5)")]
    public void Schema_AcceptsRuntimeColorLiterals(string color)
    {
        Assert.True(DocumentLimits.IsValidColor(color));
        var document = CreateAllFieldsDocument();
        document.Canvas.Background = color;
        document.Tokens!.Colors!["primary"] = color;
        var rectangle = Assert.IsType<RectangleNode>(document.Root.Children[0]);
        rectangle.Style!.Fill!.Color = color;
        rectangle.Style.Stroke!.Color = color;
        rectangle.Style.Fill.Gradient!.Stops[0].Color = color;

        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(DocumentSerializer.Serialize(document));

        Assert.Empty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("rgb(256, 0, 0)")]
    [InlineData("rgba(0, 0, 0, 1.1)")]
    [InlineData("rgb(0, 0, 0) trailing")]
    public void Schema_RejectsColorsRejectedByRuntime(string color)
    {
        Assert.False(DocumentLimits.IsValidColor(color));
        var document = CreateAllFieldsDocument();
        document.Canvas.Background = color;

        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(DocumentSerializer.Serialize(document));

        Assert.NotEmpty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    [Fact]
    public void Schema_AcceptsBareAndBracedColorTokenAliases()
    {
        var document = CreateAllFieldsDocument();
        document.Tokens!.Colors = new Dictionary<string, string>
        {
            ["base"] = "rgb(1, 2, 3)",
            ["bare"] = "base",
            ["braced"] = "{bare}"
        };

        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(DocumentSerializer.Serialize(document));

        Assert.Empty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    [Fact]
    public void Schema_RejectsTokenNamesAndTypographyFamiliesRejectedByRuntime()
    {
        var document = CreateAllFieldsDocument();
        document.Tokens!.Colors = new Dictionary<string, string> { ["bad token"] = "#000000" };
        document.Tokens.Typography = new Dictionary<string, TypographyToken>
        {
            ["body"] = new() { FontFamily = " " }
        };

        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(DocumentSerializer.Serialize(document));

        Assert.NotEmpty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    [Theory]
    [InlineData("textAlign", "\"justify\"")]
    [InlineData("verticalAlign", "\"center\"")]
    [InlineData("maxWidth", "0")]
    [InlineData("lineHeight", "0")]
    [InlineData("letterSpacing", "\"1px\"")]
    public void Schema_RejectsInvalidTextLayoutValues(string property, string rawValue)
    {
        using var schema = LoadSchema();
        var json = DocumentSerializer.Serialize(CreateAllFieldsDocument());
        var mutated = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        var text = mutated["root"]!["children"]!.AsArray().First(node => node!["type"]!.GetValue<string>() == "text")!.AsObject();
        text[property] = System.Text.Json.Nodes.JsonNode.Parse(rawValue);
        using var instance = JsonDocument.Parse(mutated.ToJsonString());

        Assert.NotEmpty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    [Fact]
    public void Schema_AllowsZeroLineHeightLikeRuntimeValidator()
    {
        var document = CreateAllFieldsDocument();
        document.Tokens!.Typography!["body"].LineHeight = 0;

        Assert.True(FTPlus.AgentGraphicDesign.Core.Validation.DocumentValidator.Validate(document).IsValid);
        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(DocumentSerializer.Serialize(document));
        Assert.Empty(Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$"));
    }

    private static void AssertModelProperties(Type type, JsonElement schema, JsonElement schemaRoot, string context)
    {
        var schemaProperties = CollectPropertyNames(schema, schemaRoot);
        var modelProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is null)
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        foreach (var property in modelProperties)
        {
            Assert.Contains(property, schemaProperties);
        }
        Assert.True(modelProperties.Length > 0 || type == typeof(GroupNode), $"No model fields were inspected for {context}");
    }

    private static HashSet<string> CollectPropertyNames(JsonElement schema, JsonElement schemaRoot)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("$ref", out var reference))
        {
            result.UnionWith(CollectPropertyNames(ResolveReference(schemaRoot, reference.GetString()!), schemaRoot));
        }
        if (schema.TryGetProperty("properties", out var properties))
        {
            result.UnionWith(properties.EnumerateObject().Select(property => property.Name));
        }
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var part in allOf.EnumerateArray())
            {
                result.UnionWith(CollectPropertyNames(part, schemaRoot));
            }
        }
        return result;
    }

    private static void AssertDiscriminatorContract(Type baseType, JsonElement unionSchema, JsonElement schemaRoot)
    {
        var registrations = baseType.GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToDictionary(attribute => attribute.DerivedType, attribute => attribute.TypeDiscriminator?.ToString()!, EqualityComparer<Type>.Default);
        var referencedDefinitions = unionSchema.GetProperty("oneOf").EnumerateArray()
            .Select(item => item.GetProperty("$ref").GetString()!.Split('/').Last())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (derivedType, discriminator) in registrations)
        {
            Assert.True(ModelDefinitions.TryGetValue(derivedType, out var definitionName), $"No schema definition mapping for {derivedType.Name}");
            Assert.Contains(definitionName!, referencedDefinitions);
            var definition = schemaRoot.GetProperty("$defs").GetProperty(definitionName!);
            var typeSchema = FindPropertySchema(definition, schemaRoot, "type");
            Assert.Equal(discriminator, typeSchema.GetProperty("const").GetString());
        }

        Assert.Equal(registrations.Count, referencedDefinitions.Count);
    }

    private static JsonElement FindPropertySchema(JsonElement schema, JsonElement schemaRoot, string propertyName)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return FindPropertySchema(ResolveReference(schemaRoot, reference.GetString()!), schemaRoot, propertyName);
        }
        if (schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(propertyName, out var property))
        {
            return property;
        }
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var part in allOf.EnumerateArray().Reverse())
            {
                try
                {
                    return FindPropertySchema(part, schemaRoot, propertyName);
                }
                catch (KeyNotFoundException)
                {
                }
            }
        }
        throw new KeyNotFoundException(propertyName);
    }

    private static JsonDocument LoadSchema()
    {
        var root = FindRepositoryRoot();
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "agd-document.schema.json")));
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FTPlus.AgentGraphicDesign.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }

    private static Document CreateAllFieldsDocument()
    {
        return new Document
        {
            Schema = Document.SchemaUrl,
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "schema-contract",
            Version = Document.SupportedVersion,
            Revn = 7,
            Canvas = new Canvas { Width = 640, Height = 480, Background = "#ffffff" },
            Tokens = new DesignTokens
            {
                Colors = new Dictionary<string, string> { ["primary"] = "#336699" },
                Typography = new Dictionary<string, TypographyToken>
                {
                    ["body"] = new() { FontFamily = "Inter", FontSize = 16, FontWeight = 400, LineHeight = 1.5 }
                }
            },
            Root = new FrameNode
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Order = "a0",
                Name = "root",
                Visible = true,
                Locked = false,
                Width = 640,
                Height = 480,
                ClipContent = true,
                Children =
                [
                    new RectangleNode
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Order = "a0", Name = "rectangle",
                        Width = 100, Height = 80, CornerRadius = 8,
                        Transform = new Transform { X = 10, Y = 20, Rotation = 15, ScaleX = 1.2, ScaleY = 0.8 },
                        Style = new Style
                        {
                            Fill = new Fill
                            {
                                TokenRef = "primary",
                                Gradient = new LinearGradient
                                {
                                    X1 = 0, Y1 = 0, X2 = 1, Y2 = 1,
                                    Stops =
                                    [
                                        new GradientStop { Offset = 0, Color = "#000000", Opacity = 1 },
                                        new GradientStop { Offset = 1, Color = "#ffffff", Opacity = 0.5 }
                                    ]
                                }
                            },
                            Stroke = new Stroke
                            {
                                Color = "#112233", TokenRef = "primary", Width = 2, Cap = StrokeCap.Round,
                                Join = StrokeJoin.Bevel, MiterLimit = 5, DashArray = [2, 3], DashOffset = 1
                            },
                            Opacity = 0.75,
                            BlendMode = "multiply",
                            FillRule = "evenodd"
                        }
                    },
                    new EllipseNode { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Order = "a1", Width = 50, Height = 60 },
                    new PathNode { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Order = "a2", D = "M0 0L10 10Z" },
                    new TextNode
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000006"), Order = "a3", Text = "Hello\nworld",
                        FontFamily = "Inter", FontSize = 18, FontWeight = 600, TypographyTokenRef = "body",
                        TextAlign = "center", VerticalAlign = "middle", MaxWidth = 240, LineHeight = 1.4, LetterSpacing = 0.5
                    },
                    new LineNode { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Order = "a4", X1 = 1, Y1 = 2, X2 = 3, Y2 = 4 },
                    new PolygonNode
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000008"), Order = "a5", Closed = false,
                        Points = [new Point { X = 0, Y = 0 }, new Point { X = 10, Y = 10 }]
                    },
                    new ImageNode
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000009"), Order = "a6",
                        Src = "assets/example.png", Width = 320, Height = 200
                    },
                    new GroupNode
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000010"), Order = "a7",
                        Children =
                        [
                            new UnknownNode
                            {
                                Id = Guid.Parse("10000000-0000-0000-0000-000000000011"), Order = "a0", OriginalType = "future-node"
                            }
                        ]
                    }
                ]
            }
        };
    }
}
