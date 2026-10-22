using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp.Resources;

/// <summary>
/// Read-only MCP resources that ship the authoring reference with the server. The
/// files are embedded from the repository (<c>schemas/</c> and <c>docs/</c>) at build
/// time via <c>EmbeddedResource</c> links in the project file, so the installed tool
/// serves them without a checkout. The repository files remain the single source of
/// truth; the MCP tests compare each resource with its source file.
/// </summary>
[McpServerResourceType]
public static class DocumentResources
{
    private const string ResourcePrefix = "FTPlus.AgentGraphicDesign.Mcp.Resources.";
    private const string SchemaMimeType = "application/schema+json";
    private const string MarkdownMimeType = "text/markdown";

    private static readonly Lazy<string> SchemaText = new(() => ReadEmbedded("agd-document.schema.json"));
    private static readonly Lazy<string> FormatText = new(() => ReadEmbedded("format.md"));
    private static readonly Lazy<string> AgentGuideText = new(() => ReadEmbedded("agent-guide.md"));

    [McpServerResource(UriTemplate = "agd://schema", Name = "agd-schema", Title = "AGD Document JSON Schema", MimeType = "application/schema+json")]
    [Description("JSON Schema (draft 2020-12) for .agd.json documents; mirrors schemas/agd-document.schema.json")]
    public static ReadResourceResult Schema() => Text("agd://schema", SchemaMimeType, SchemaText.Value);

    [McpServerResource(UriTemplate = "agd://format", Name = "agd-format", Title = "AGD Document Format Reference", MimeType = "text/markdown")]
    [Description("Format-v1 reference for .agd.json documents: node types, transform, style, tokens, validation codes; mirrors docs/format.md")]
    public static ReadResourceResult Format() => Text("agd://format", MarkdownMimeType, FormatText.Value);

    [McpServerResource(UriTemplate = "agd://agent-guide", Name = "agd-agent-guide", Title = "AGD Agent Authoring Guide", MimeType = "text/markdown")]
    [Description("Authoring guide for agents that design with AGD: the loop, conventions, icon recipe, tokens, path tips, MCP session tips; mirrors docs/agent-guide.md")]
    public static ReadResourceResult AgentGuide() => Text("agd://agent-guide", MarkdownMimeType, AgentGuideText.Value);

    private static ReadResourceResult Text(string uri, string mimeType, string text) => new()
    {
        Contents = [new TextResourceContents { Uri = uri, MimeType = mimeType, Text = text }]
    };

    private static string ReadEmbedded(string fileName)
    {
        var assembly = typeof(DocumentResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"Embedded resource '{fileName}' is missing from {assembly.GetName().Name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
