using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Parity;

public class CliMcpParityTests
{
    [Fact]
    public void SurfaceManifest_MatchesExecutableRegistrations()
    {
        var root = FindRepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "surface-manifest.json")));

        var cliSource = File.ReadAllText(Path.Combine(root, "src", "FTPlus.AgentGraphicDesign.Cli", "Program.cs"));
        var actualCli = Regex.Matches(
                cliSource,
                @"\b\w+\.AddCommand<(?<handler>\w+)>\(""(?<name>[^""\r\n]+)""\)")
            .Select(match => $"{match.Groups["handler"].Value}|{match.Groups["name"].Value}")
            .ToHashSet(StringComparer.Ordinal);
        var expectedCli = manifest.RootElement.GetProperty("cliCommands").EnumerateArray()
            .Select(item => $"{item.GetProperty("handler").GetString()}|{item.GetProperty("registeredName").GetString()}")
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedCli.Order(), actualCli.Order());

        var actualBranches = Regex.Matches(cliSource, @"config\.AddBranch\(""(?<name>[^""\r\n]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var expectedBranches = manifest.RootElement.GetProperty("cliBranches").EnumerateArray()
            .Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedBranches.Order(), actualBranches.Order());

        var mcpSources = Directory.EnumerateFiles(Path.Combine(root, "src", "FTPlus.AgentGraphicDesign.Mcp", "Tools"), "*.cs")
            .Select(File.ReadAllText);
        var actualMcp = mcpSources.SelectMany(source => Regex.Matches(
                source,
                @"\[McpServerTool\((?<arguments>[^\]]*)\)\]\s*\[Description\([^\]]*\)\]\s*public\s+(?:async\s+)?[\w<>,\[\]\s]+\s+(?<method>\w+)\s*\(",
                RegexOptions.Singleline).Cast<Match>())
            .Select(match =>
            {
                var title = Regex.Match(match.Groups["arguments"].Value, @"Title\s*=\s*""(?<title>[^""\r\n]+)""");
                Assert.True(title.Success, $"MCP tool {match.Groups["method"].Value} has no title");
                var method = match.Groups["method"].Value;
                var explicitName = Regex.Match(match.Groups["arguments"].Value, @"Name\s*=\s*""(?<name>[^""\r\n]+)""");
                var name = explicitName.Success ? explicitName.Groups["name"].Value : ToSnakeCase(method);
                return $"{method}|{name}|{title.Groups["title"].Value}";
            })
            .ToHashSet(StringComparer.Ordinal);
        var expectedMcp = manifest.RootElement.GetProperty("mcpTools").EnumerateArray()
            .Select(item => $"{item.GetProperty("method").GetString()}|{item.GetProperty("name").GetString()}|{item.GetProperty("title").GetString()}")
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedMcp.Order(), actualMcp.Order());

        var resourceSource = File.ReadAllText(Path.Combine(root, "src", "FTPlus.AgentGraphicDesign.Mcp", "Resources", "DocumentResources.cs"));
        var actualResources = Regex.Matches(
                resourceSource,
                @"\[McpServerResource\((?<arguments>[^\]]*)\)\]\s*\[Description\([^\]]*\)\]\s*public\s+static\s+[\w<>,\[\]\s]+\s+(?<method>\w+)\s*\(",
                RegexOptions.Singleline)
            .Select(match =>
            {
                var method = match.Groups["method"].Value;
                var arguments = match.Groups["arguments"].Value;
                var uri = Regex.Match(arguments, @"UriTemplate\s*=\s*""(?<value>[^""\r\n]+)""");
                var name = Regex.Match(arguments, @"Name\s*=\s*""(?<value>[^""\r\n]+)""");
                var mimeType = Regex.Match(arguments, @"MimeType\s*=\s*""(?<value>[^""\r\n]+)""");
                Assert.True(uri.Success, $"MCP resource {method} has no UriTemplate");
                Assert.True(name.Success, $"MCP resource {method} has no Name");
                Assert.True(mimeType.Success, $"MCP resource {method} has no MimeType");
                return $"{method}|{uri.Groups["value"].Value}|{name.Groups["value"].Value}|{mimeType.Groups["value"].Value}";
            })
            .ToHashSet(StringComparer.Ordinal);
        var expectedResources = manifest.RootElement.GetProperty("mcpResources").EnumerateArray()
            .Select(item =>
            {
                var source = item.GetProperty("source").GetString()!;
                Assert.True(File.Exists(Path.Combine(root, source)), $"MCP resource source {source} is missing");
                return $"{item.GetProperty("method").GetString()}|{item.GetProperty("uri").GetString()}|{item.GetProperty("name").GetString()}|{item.GetProperty("mimeType").GetString()}";
            })
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedResources.Order(), actualResources.Order());

        var mcpProject = File.ReadAllText(Path.Combine(root, "src", "FTPlus.AgentGraphicDesign.Mcp", "FTPlus.AgentGraphicDesign.Mcp.csproj"));
        foreach (var item in manifest.RootElement.GetProperty("mcpResources").EnumerateArray())
        {
            var source = item.GetProperty("source").GetString()!;
            Assert.Contains($"<EmbeddedResource Include=\"../../{source}\"", mcpProject);
        }
    }

    [Fact]
    public void SurfaceManifest_UsesSharedPackageVersion()
    {
        var root = FindRepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "surface-manifest.json")));
        var properties = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var prefix = properties.Descendants("VersionPrefix").Single().Value;
        var suffix = properties.Descendants("VersionSuffix").SingleOrDefault()?.Value;
        var expectedVersion = string.IsNullOrEmpty(suffix) ? prefix : $"{prefix}-{suffix}";

        Assert.Equal(expectedVersion, manifest.RootElement.GetProperty("version").GetString());
        Assert.DoesNotContain("<Version>", File.ReadAllText(Path.Combine(root, "src", "FTPlus.AgentGraphicDesign.Cli", "FTPlus.AgentGraphicDesign.Cli.csproj")));
        Assert.DoesNotContain("<Version>", File.ReadAllText(Path.Combine(root, "src", "FTPlus.AgentGraphicDesign.Mcp", "FTPlus.AgentGraphicDesign.Mcp.csproj")));
    }

    private static string FindRepositoryRoot()
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

    private static string ToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current) && index > 0
                && (char.IsLower(value[index - 1]) || (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }
}
