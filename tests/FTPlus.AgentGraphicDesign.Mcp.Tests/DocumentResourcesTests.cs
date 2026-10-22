using System.Reflection;
using FTPlus.AgentGraphicDesign.Mcp.Resources;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// The MCP resources are embedded copies of repository files. These tests pin the
/// registration metadata and prove that each served resource is byte-for-byte the
/// repository file it mirrors, so the embedded copy cannot drift from the source.
/// </summary>
public class DocumentResourcesTests
{
    public static TheoryData<string, string, string, Func<ReadResourceResult>> Resources => new()
    {
        { "agd://schema", "application/schema+json", Path.Combine("schemas", "agd-document.schema.json"), DocumentResources.Schema },
        { "agd://format", "text/markdown", Path.Combine("docs", "format.md"), DocumentResources.Format },
        { "agd://agent-guide", "text/markdown", Path.Combine("docs", "agent-guide.md"), DocumentResources.AgentGuide },
    };

    [Theory]
    [MemberData(nameof(Resources))]
    public void Read_ReturnsRepositoryFileContent(string uri, string mimeType, string relativeSource, Func<ReadResourceResult> read)
    {
        var expected = File.ReadAllText(Path.Combine(RepositoryPaths.Root, relativeSource));

        var result = read();

        var contents = Assert.Single(result.Contents);
        var text = Assert.IsType<TextResourceContents>(contents);
        Assert.Equal(uri, text.Uri);
        Assert.Equal(mimeType, text.MimeType);
        Assert.False(string.IsNullOrWhiteSpace(text.Text));
        Assert.Equal(expected, text.Text);
    }

    [Fact]
    public void Registration_DeclaresThreeDirectResourcesWithMatchingMetadata()
    {
        Assert.NotNull(typeof(DocumentResources).GetCustomAttribute<McpServerResourceTypeAttribute>());

        var registered = typeof(DocumentResources)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerResourceAttribute>()))
            .Where(item => item.Attribute is not null)
            .ToDictionary(item => item.Attribute!.UriTemplate!, item => item, StringComparer.Ordinal);

        Assert.Equal(["agd://agent-guide", "agd://format", "agd://schema"], registered.Keys.Order(StringComparer.Ordinal));
        foreach (var (uri, (method, attribute)) in registered)
        {
            Assert.DoesNotContain('{', uri);
            Assert.False(string.IsNullOrWhiteSpace(attribute!.Name));
            Assert.False(string.IsNullOrWhiteSpace(attribute.Title));
            Assert.False(string.IsNullOrWhiteSpace(attribute.MimeType));

            var result = Assert.IsType<ReadResourceResult>(method.Invoke(null, null));
            var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
            Assert.Equal(uri, text.Uri);
            Assert.Equal(attribute.MimeType, text.MimeType);
        }
    }
}

internal static class RepositoryPaths
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FTPlus.AgentGraphicDesign.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
