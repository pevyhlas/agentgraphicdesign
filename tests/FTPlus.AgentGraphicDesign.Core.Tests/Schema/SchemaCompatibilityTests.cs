using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Schema;

public class SchemaCompatibilityTests
{
    [Fact]
    public void SerializationRoundTrip_PreservesAllFields()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Revn, restored.Revn);
        Assert.Equal(original.Canvas.Width, restored.Canvas.Width);
        Assert.Equal(original.Canvas.Height, restored.Canvas.Height);
        Assert.Equal(original.Canvas.Background, restored.Canvas.Background);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesNodeHierarchy()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Root.Id, restored.Root.Id);
        Assert.Equal(original.Root.Children.Count, restored.Root.Children.Count);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesNodeTypes()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var children = restored.Root.Children;

        Assert.IsType<RectangleNode>(children[0]);
        Assert.IsType<EllipseNode>(children[1]);
        Assert.IsType<TextNode>(children[2]);
        Assert.IsType<PathNode>(children[3]);
        Assert.IsType<GroupNode>(children[4]);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesRectangleProperties()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var rect = (RectangleNode)restored.Root.Children[0];

        Assert.Equal("rect1", rect.Name);
        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
        Assert.Equal(8, rect.CornerRadius);
        Assert.Equal("#ff0000", rect.Style?.Fill?.Color);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesTransform()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var rect = (RectangleNode)restored.Root.Children[0];

        Assert.NotNull(rect.Transform);
        Assert.Equal(10, rect.Transform.X);
        Assert.Equal(20, rect.Transform.Y);
        Assert.Equal(45, rect.Transform.Rotation);
        Assert.Equal(1.5, rect.Transform.ScaleX);
        Assert.Equal(1.5, rect.Transform.ScaleY);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesStyle()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var ellipse = (EllipseNode)restored.Root.Children[1];

        Assert.NotNull(ellipse.Style);
        Assert.Equal("#0000ff", ellipse.Style.Fill?.Color);
        Assert.Equal("#000000", ellipse.Style.Stroke?.Color);
        Assert.Equal(2, ellipse.Style.Stroke?.Width);
        Assert.Equal(0.8, ellipse.Style.Opacity);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesTextProperties()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var text = (TextNode)restored.Root.Children[2];

        Assert.Equal("Hello World", text.Text);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal(24, text.FontSize);
        Assert.Equal(700, text.FontWeight);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesPathData()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var path = (PathNode)restored.Root.Children[3];

        Assert.Equal("M 0 0 L 100 100 Z", path.D);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesGroupChildren()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var group = (GroupNode)restored.Root.Children[4];

        Assert.Equal(2, group.Children.Count);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesFractionalIndices()
    {
        var original = CreateFullFeaturedDocument();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        for (var i = 0; i < original.Root.Children.Count; i++)
        {
            Assert.Equal(original.Root.Children[i].Order, restored.Root.Children[i].Order);
        }
    }

    [Fact]
    public void Deserialize_ToleratesUnknownFields()
    {
        var json = """
        {
          "version": 1,
          "id": "00000000-0000-0000-0000-000000000001",
          "name": "test",
          "revn": 1,
          "futureField": "should be ignored",
          "canvas": {
            "width": 800,
            "height": 600,
            "unknownCanvasField": 42
          },
          "root": {
            "type": "frame",
            "id": "00000000-0000-0000-0000-000000000002",
            "name": "root",
            "width": 800,
            "height": 600,
            "unknownNodeField": true,
            "children": []
          }
        }
        """;

        var doc = DocumentSerializer.Deserialize(json);

        Assert.NotNull(doc);
        Assert.Equal("test", doc.Name);
        Assert.Equal(800, doc.Canvas.Width);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownDocumentFields()
    {
        var json = """
        {
          "version": 1,
          "id": "00000000-0000-0000-0000-000000000001",
          "name": "test",
          "revn": 1,
          "futureDocumentField": "preserved",
          "anotherFuture": { "nested": 42 },
          "canvas": {
            "width": 800,
            "height": 600
          },
          "root": {
            "type": "frame",
            "id": "00000000-0000-0000-0000-000000000002",
            "name": "root",
            "width": 800,
            "height": 600,
            "children": []
          }
        }
        """;

        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var serialized = DocumentSerializer.Serialize(doc);
        Assert.Contains("futureDocumentField", serialized);
        Assert.Contains("preserved", serialized);
        Assert.Contains("anotherFuture", serialized);
        Assert.Contains("nested", serialized);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownCanvasFields()
    {
        var json = """
        {
          "version": 1,
          "id": "00000000-0000-0000-0000-000000000001",
          "name": "test",
          "revn": 1,
          "canvas": {
            "width": 800,
            "height": 600,
            "futureCanvasField": "preserved",
            "gridSettings": { "spacing": 10 }
          },
          "root": {
            "type": "frame",
            "id": "00000000-0000-0000-0000-000000000002",
            "name": "root",
            "width": 800,
            "height": 600,
            "children": []
          }
        }
        """;

        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var serialized = DocumentSerializer.Serialize(doc);
        Assert.Contains("futureCanvasField", serialized);
        Assert.Contains("gridSettings", serialized);
        Assert.Contains("spacing", serialized);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownNodeFields()
    {
        var json = """
        {
          "version": 1,
          "id": "00000000-0000-0000-0000-000000000001",
          "name": "test",
          "revn": 1,
          "canvas": { "width": 800, "height": 600 },
          "root": {
            "type": "frame",
            "id": "00000000-0000-0000-0000-000000000002",
            "name": "root",
            "width": 800,
            "height": 600,
            "futureFrameField": "preserved",
            "children": [
              {
                "type": "rectangle",
                "id": "00000000-0000-0000-0000-000000000003",
                "name": "rect",
                "width": 100,
                "height": 100,
                "futureRectField": { "complex": true }
              }
            ]
          }
        }
        """;

        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var serialized = DocumentSerializer.Serialize(doc);
        Assert.Contains("futureFrameField", serialized);
        Assert.Contains("futureRectField", serialized);
        Assert.Contains("complex", serialized);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownStyleFields()
    {
        var json = """
        {
          "version": 1,
          "id": "00000000-0000-0000-0000-000000000001",
          "name": "test",
          "revn": 1,
          "canvas": { "width": 800, "height": 600 },
          "root": {
            "type": "frame",
            "id": "00000000-0000-0000-0000-000000000002",
            "name": "root",
            "width": 800,
            "height": 600,
            "children": [
              {
                "type": "rectangle",
                "id": "00000000-0000-0000-0000-000000000003",
                "name": "rect",
                "width": 100,
                "height": 100,
                "style": {
                  "fill": { "color": "#ff0000", "futureGradient": "linear" },
                  "futureEffect": { "blur": 5 }
                },
                "transform": {
                  "x": 10,
                  "y": 20,
                  "futureSkew": 15
                }
              }
            ]
          }
        }
        """;

        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var serialized = DocumentSerializer.Serialize(doc);
        Assert.Contains("futureGradient", serialized);
        Assert.Contains("futureEffect", serialized);
        Assert.Contains("futureSkew", serialized);
    }

    [Fact]
    public void Validate_CurrentVersion_NoVersionWarning()
    {
        var doc = CreateMinimalValidDocument();
        doc.Version = Document.SupportedVersion;

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "DOC005");
    }

    [Fact]
    public void Validate_NewerVersion_WarnsAboutUnsupportedVersion()
    {
        var doc = CreateMinimalValidDocument();
        doc.Version = Document.SupportedVersion + 1;

        var result = DocumentValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "DOC005");
        Assert.Contains(result.Warnings, w => w.Message.Contains("newer than supported"));
    }

    [Fact]
    public void Validate_FutureVersion_WarnsWithVersionNumbers()
    {
        var doc = CreateMinimalValidDocument();
        doc.Version = 99;

        var result = DocumentValidator.Validate(doc);

        var warning = Assert.Single(result.Warnings, w => w.Code == "DOC005");
        Assert.Contains("99", warning.Message);
        Assert.Contains(Document.SupportedVersion.ToString(), warning.Message);
    }

    private static Document CreateMinimalValidDocument()
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = "test",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 800, Height = 600 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Order = "a0",
                Width = 800,
                Height = 600,
                Children = []
            }
        };
    }

    [Fact]
    public void SupportedVersion_IsOne()
    {
        Assert.Equal(1, Document.SupportedVersion);
    }

    [Fact]
    public void Serialize_ProducesDeterministicOutput()
    {
        var doc = CreateFullFeaturedDocument();

        var json1 = DocumentSerializer.Serialize(doc);
        var json2 = DocumentSerializer.Serialize(doc);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Serialize_ProducesStableKeyOrder()
    {
        var doc = CreateFullFeaturedDocument();
        var json = DocumentSerializer.Serialize(doc);

        var firstKeyIndex = json.IndexOf("\"canvas\"", StringComparison.Ordinal);
        var idIndex = json.IndexOf("\"id\"", StringComparison.Ordinal);
        var nameIndex = json.IndexOf("\"name\"", StringComparison.Ordinal);

        Assert.True(idIndex < nameIndex, "Keys should be in stable order (id before name)");
        Assert.True(nameIndex < firstKeyIndex || idIndex < firstKeyIndex, "Top-level keys should be in stable order");
    }

    [Fact]
    public void ExistingFixtures_LoadSuccessfully()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");

        var fixtures = Directory.GetFiles(examplesDir, "*.agd.json");
        Assert.NotEmpty(fixtures);

        foreach (var fixture in fixtures)
        {
            if (Path.GetFileName(fixture).StartsWith("invalid-", StringComparison.Ordinal))
            {
                continue;
            }

            var json = File.ReadAllText(fixture);
            var doc = DocumentSerializer.Deserialize(json);
            Assert.NotNull(doc);
        }
    }

    [Fact]
    public void ExistingFixtures_ValidateSuccessfully()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");

        var fixtures = Directory.GetFiles(examplesDir, "*.agd.json");

        foreach (var fixture in fixtures)
        {
            if (Path.GetFileName(fixture).StartsWith("invalid-", StringComparison.Ordinal))
            {
                continue;
            }

            var json = File.ReadAllText(fixture);
            var doc = DocumentSerializer.Deserialize(json);
            Assert.NotNull(doc);

            var result = DocumentValidator.Validate(doc);
            Assert.True(result.IsValid, $"Fixture {Path.GetFileName(fixture)} should be valid but has errors: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }

    [Fact]
    public void ExistingFixtures_RoundTripSerializationEquality()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");

        var fixtures = Directory.GetFiles(examplesDir, "*.agd.json");

        foreach (var fixture in fixtures)
        {
            if (Path.GetFileName(fixture).StartsWith("invalid-", StringComparison.Ordinal))
            {
                continue;
            }

            var json = File.ReadAllText(fixture);
            var doc = DocumentSerializer.Deserialize(json);
            Assert.NotNull(doc);

            var json2 = DocumentSerializer.Serialize(doc);
            var doc2 = DocumentSerializer.Deserialize(json2);
            Assert.NotNull(doc2);
            var json3 = DocumentSerializer.Serialize(doc2);

            Assert.Equal(json2, json3);
        }
    }

    [Theory]
    [InlineData("basic-shapes.agd.json")]
    [InlineData("nested-groups.agd.json")]
    [InlineData("transforms.agd.json")]
    [InlineData("unicode-text.agd.json")]
    public void NamedFixture_LoadsAndValidates(string fixtureName)
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var path = Path.Combine(examplesDir, fixtureName);

        Assert.True(File.Exists(path), $"Fixture {fixtureName} should exist");

        var json = File.ReadAllText(path);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var result = DocumentValidator.Validate(doc);
        Assert.True(result.IsValid, $"Fixture {fixtureName} should be valid");
    }

    [Fact]
    public void UnicodeFixture_PreservesUnicodeText()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var path = Path.Combine(examplesDir, "unicode-text.agd.json");

        var json = File.ReadAllText(path);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        Assert.Contains("日本語", doc.Name);
        Assert.Contains("根", doc.Root.Name);

        var textNodes = doc.Root.Children.OfType<TextNode>().ToList();
        Assert.Contains(textNodes, t => t.Text.Contains("こんにちは"));
        Assert.Contains(textNodes, t => t.Text.Contains("你好"));
        Assert.Contains(textNodes, t => t.Text.Contains("🌍"));
        Assert.Contains(textNodes, t => t.Text.Contains("مرحبا"));
    }

    [Fact]
    public void TransformsFixture_PreservesTransformValues()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var path = Path.Combine(examplesDir, "transforms.agd.json");

        var json = File.ReadAllText(path);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var children = doc.Root.Children.ToList();

        var translated = children.FirstOrDefault(c => c.Name == "translated");
        Assert.NotNull(translated?.Transform);
        Assert.Equal(150.5, translated.Transform.X);
        Assert.Equal(200.75, translated.Transform.Y);

        var rotated = children.FirstOrDefault(c => c.Name == "rotated");
        Assert.NotNull(rotated?.Transform);
        Assert.Equal(45, rotated.Transform.Rotation);

        var scaled = children.FirstOrDefault(c => c.Name == "scaled");
        Assert.NotNull(scaled?.Transform);
        Assert.Equal(1.5, scaled.Transform.ScaleX);
        Assert.Equal(0.75, scaled.Transform.ScaleY);
    }

    [Fact]
    public void NestedGroupsFixture_PreservesHierarchy()
    {
        var examplesDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "examples");
        var path = Path.Combine(examplesDir, "nested-groups.agd.json");

        var json = File.ReadAllText(path);
        var doc = DocumentSerializer.Deserialize(json);
        Assert.NotNull(doc);

        var outerGroup = doc.Root.Children.FirstOrDefault() as GroupNode;
        Assert.NotNull(outerGroup);
        Assert.Equal("outer-group", outerGroup.Name);
        Assert.Equal(2, outerGroup.Children.Count);

        var innerGroup1 = outerGroup.Children[0] as GroupNode;
        Assert.NotNull(innerGroup1);
        Assert.Equal("inner-group-1", innerGroup1.Name);
        Assert.Single(innerGroup1.Children);
        Assert.IsType<RectangleNode>(innerGroup1.Children[0]);
    }

    private static Document CreateFullFeaturedDocument()
    {
        var rootId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        return new Document
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000000"),
            Name = "full-featured",
            Version = 1,
            Revn = 5,
            Canvas = new Canvas
            {
                Width = 1920,
                Height = 1080,
                Background = "#ffffff"
            },
            Root = new FrameNode
            {
                Id = rootId,
                Name = "root",
                Order = "a0",
                Width = 1920,
                Height = 1080,
                ClipContent = true,
                Children =
                [
                    new RectangleNode
                    {
                        Id = Guid.NewGuid(),
                        Name = "rect1",
                        Order = "a0",
                        Width = 100,
                        Height = 50,
                        CornerRadius = 8,
                        Transform = new Transform
                        {
                            X = 10,
                            Y = 20,
                            Rotation = 45,
                            ScaleX = 1.5,
                            ScaleY = 1.5
                        },
                        Style = new Style
                        {
                            Fill = new Fill { Color = "#ff0000" }
                        }
                    },
                    new EllipseNode
                    {
                        Id = Guid.NewGuid(),
                        Name = "ellipse1",
                        Order = "a1",
                        Width = 80,
                        Height = 80,
                        Style = new Style
                        {
                            Fill = new Fill { Color = "#0000ff" },
                            Stroke = new Stroke { Color = "#000000", Width = 2 },
                            Opacity = 0.8
                        }
                    },
                    new TextNode
                    {
                        Id = Guid.NewGuid(),
                        Name = "text1",
                        Order = "a2",
                        Text = "Hello World",
                        FontFamily = "Arial",
                        FontSize = 24,
                        FontWeight = 700
                    },
                    new PathNode
                    {
                        Id = Guid.NewGuid(),
                        Name = "path1",
                        Order = "a3",
                        D = "M 0 0 L 100 100 Z"
                    },
                    new GroupNode
                    {
                        Id = Guid.NewGuid(),
                        Name = "group1",
                        Order = "a4",
                        Children =
                        [
                            new RectangleNode
                            {
                                Id = Guid.NewGuid(),
                                Name = "nested-rect",
                                Order = "a0",
                                Width = 50,
                                Height = 50
                            },
                            new EllipseNode
                            {
                                Id = Guid.NewGuid(),
                                Name = "nested-ellipse",
                                Order = "a1",
                                Width = 30,
                                Height = 30
                            }
                        ]
                    }
                ]
            }
        };
    }
}
