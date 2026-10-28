using FTPlus.AgentGraphicDesign.Core.Paths;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public sealed class CanonicalConfinementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agd-canonical-mcp-" + Guid.NewGuid().ToString("N"));

    public CanonicalConfinementTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CanonicalAndOriginalRootSpellingsConfineAndDisplayTheSamePath()
    {
        var original = new PathConfinement(_root);
        var canonical = new PathConfinement(PlatformPaths.FullPath(_root));
        var file = Path.Combine(_root, "document.agd.json");
        File.WriteAllText(file, "sentinel");
        var first = original.Confine(file);
        var second = canonical.Confine(file);
        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(original.WorkingDirectory, canonical.WorkingDirectory);
        Assert.Equal("document.agd.json", original.RelativePath(second.Path!));
        Assert.Equal("document.agd.json", canonical.RelativePath(file));
        Assert.Equal("sentinel", File.ReadAllText(first.Path!));
    }

    [Fact]
    public void UserLinkAboveTheConfiguredRootRemainsRefused()
    {
        var physical = Path.Combine(_root, "physical");
        Directory.CreateDirectory(Path.Combine(physical, "project"));
        var link = Path.Combine(_root, "user-link");
        Directory.CreateSymbolicLink(link, physical);
        var confinement = new PathConfinement(Path.Combine(link, "project"));
        var result = confinement.Confine("document.agd.json");
        Assert.False(result.Success);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.Null(result.Path);
        Assert.False(File.Exists(Path.Combine(physical, "project", "document.agd.json")));
    }
}
