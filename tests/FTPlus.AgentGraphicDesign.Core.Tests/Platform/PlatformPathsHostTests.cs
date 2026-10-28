using FTPlus.AgentGraphicDesign.Core.Paths;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Platform;

public sealed class PlatformPathsHostTests
{
    [Theory]
    [InlineData("var")]
    [InlineData("tmp")]
    [InlineData("etc")]
    public void CurrentHost_AliasIdentityMatchesItsVerifiedMetadata(string name)
    {
        var input = "/" + name + "/agd-path-probe";
        var expected = OperatingSystem.IsMacOS() ? "/private" + input : Path.GetFullPath(input);
        Assert.Equal(expected, PlatformPaths.FullPath(input));
        Assert.Equal(expected, PlatformPaths.FullPath(PlatformPaths.FullPath(input)));
        Assert.Equal("agd-path-probe", PlatformPaths.RelativePath("/" + name, expected));
        Assert.Equal(Path.GetFullPath("/agd-path-probe"), PlatformPaths.FullPath("/" + name + "/../agd-path-probe"));
    }
}
