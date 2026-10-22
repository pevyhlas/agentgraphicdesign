using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Serialization;

public class DiscriminatorOrderTests
{
    // Literal inputs are deliberately independent of the production serializer.
    private const string First = """{"type":"rectangle","id":"00000000-0000-0000-0000-000000000004","width":16,"height":8}""";
    private const string Middle = """{"id":"00000000-0000-0000-0000-000000000004","width":16,"type":"rectangle","height":8}""";
    private const string Last = """{"id":"00000000-0000-0000-0000-000000000004","width":16,"height":8,"type":"rectangle"}""";

    [Theory]
    [InlineData(First)]
    [InlineData(Middle)]
    [InlineData(Last)]
    public void FullDocument_NestedDiscriminatorsAcceptAnyPosition(string child)
    {
        var json = Document(child);
        foreach (var result in new[]
        {
            DocumentSerializer.TryDeserialize(json),
            DocumentSerializer.TryDeserialize(Encoding.UTF8.GetBytes(json))
        })
        {
            Assert.True(result.Success, result.Error?.Message);
            var group = Assert.IsType<GroupNode>(Assert.Single(result.Document!.Root.Children));
            var rectangle = Assert.IsType<RectangleNode>(Assert.Single(group.Children));
            Assert.Equal(16, rectangle.Width);
            Assert.Equal(8, rectangle.Height);
            var canonical = DocumentSerializer.Serialize(result.Document);
            Assert.Equal(DocumentSerializer.Serialize(DocumentSerializer.Deserialize(Document(First))!), canonical);
            Assert.Equal(canonical, DocumentSerializer.Serialize(DocumentSerializer.Deserialize(canonical)!));
        }
    }

    [Fact]
    public void FullDocument_UnknownLateDiscriminatorPreservesFields()
    {
        var json = Document("""{"id":"00000000-0000-0000-0000-000000000004","futureValue":42,"type":"future-shape"}""");
        var result = DocumentSerializer.TryDeserialize(Encoding.UTF8.GetBytes(json));
        Assert.True(result.Success, result.Error?.Message);
        var group = Assert.IsType<GroupNode>(Assert.Single(result.Document!.Root.Children));
        var unknown = Assert.IsType<UnknownNode>(Assert.Single(group.Children));
        Assert.Equal(42, unknown.ExtensionData!["futureValue"].GetInt32());
        var serialized = DocumentSerializer.Serialize(result.Document);
        Assert.Contains("\"type\": \"future-shape\"", serialized);
        Assert.Equal(serialized, DocumentSerializer.Serialize(DocumentSerializer.Deserialize(serialized)!));
    }

    [Theory]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000004","type":"rectangle","type":"ellipse"}""")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000004","type":"rectangle","\u0074ype":"ellipse"}""")]
    public void FullDocument_DuplicateDiscriminatorStillRejected(string child)
    {
        var json = Document(child);
        foreach (var result in new[]
        {
            DocumentSerializer.TryDeserialize(json),
            DocumentSerializer.TryDeserialize(Encoding.UTF8.GetBytes(json))
        })
        {
            Assert.False(result.Success);
            Assert.Contains("Duplicate JSON property 'type'", result.Error!.Message);
        }
    }

    [Fact]
    public void FullDocument_LateDiscriminatorDoesNotBypassDepthLimit()
    {
        var nested = new string('[', 65) + "0" + new string(']', 65);
        var json = Document("""{"id":"00000000-0000-0000-0000-000000000004","extra":DEPTH,"type":"rectangle"}""".Replace("DEPTH", nested));
        Assert.False(DocumentSerializer.TryDeserialize(json).Success);
        Assert.False(DocumentSerializer.TryDeserialize(Encoding.UTF8.GetBytes(json)).Success);
    }

    private static string Document(string child) => $$"""
        {
          "version":1,"id":"00000000-0000-0000-0000-000000000001","name":"property-order","revn":1,
          "canvas":{"width":128,"height":64},
          "root":{"id":"00000000-0000-0000-0000-000000000002","width":128,"height":64,
            "children":[{"id":"00000000-0000-0000-0000-000000000003","children":[{{child}}],"type":"group"}],
            "type":"frame"}
        }
        """;
}
