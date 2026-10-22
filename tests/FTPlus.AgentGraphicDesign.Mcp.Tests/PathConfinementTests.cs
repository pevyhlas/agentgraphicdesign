using System.Runtime.InteropServices;
using FTPlus.AgentGraphicDesign.Mcp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public class PathConfinementTests : IDisposable
{
    private readonly string _testDir;
    private readonly PathConfinement _confinement;

    public PathConfinementTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-confine-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _confinement = new PathConfinement(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void Confine_RelativePath_ReturnsConfinedAbsolutePath()
    {
        var result = _confinement.Confine("document.agd.json");

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Path);
        Assert.StartsWith(_testDir, result.Path);
        Assert.EndsWith("document.agd.json", result.Path);
    }

    [Fact]
    public void Confine_AbsolutePathWithinWorkingDir_Succeeds()
    {
        var absolutePath = Path.Combine(_testDir, "subfolder", "doc.agd.json");

        var result = _confinement.Confine(absolutePath);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal(absolutePath, result.Path);
    }

    [Fact]
    public void Confine_ParentTraversal_ReturnsError()
    {
        var result = _confinement.Confine("../../../etc/passwd");

        Assert.False(result.Success);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.NotNull(result.Error);
        Assert.Contains("outside", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_testDir, result.Error);
    }

    [Fact]
    public void Confine_AbsolutePathOutsideWorkingDir_ReturnsError()
    {
        var outsidePath = "/etc/passwd";

        var result = _confinement.Confine(outsidePath);

        Assert.False(result.Success);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.NotNull(result.Error);
        Assert.Contains("outside", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Confine_NestedParentTraversal_ReturnsError()
    {
        var result = _confinement.Confine("subdir/../../sibling/file.json");

        Assert.False(result.Success);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Confine_EmptyPath_ReturnsError()
    {
        var result = _confinement.Confine("");

        Assert.False(result.Success);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.NotNull(result.Error);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Confine_WhitespacePath_ReturnsError()
    {
        var result = _confinement.Confine("   ");

        Assert.False(result.Success);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Confine_SubdirectoryPath_Succeeds()
    {
        var result = _confinement.Confine("projects/design1/doc.agd.json");

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Path);
        Assert.StartsWith(_testDir, result.Path);
    }

    [Fact]
    public void IsGuid_ValidGuid_ReturnsTrue()
    {
        Assert.True(PathConfinement.IsGuid("550e8400-e29b-41d4-a716-446655440000"));
    }

    [Fact]
    public void IsGuid_FilePath_ReturnsFalse()
    {
        Assert.False(PathConfinement.IsGuid("/path/to/file.json"));
        Assert.False(PathConfinement.IsGuid("document.agd.json"));
    }

    [Fact]
    public void WorkingDirectory_ReturnsNormalizedPath()
    {
        Assert.Equal(_testDir, _confinement.WorkingDirectory);
    }

    [Fact]
    public void Confine_CaseSensitivity_MatchesPlatformBehavior()
    {
        var upperCaseDir = _testDir.ToUpperInvariant();
        if (upperCaseDir == _testDir)
        {
            return;
        }

        var testPath = Path.Combine(upperCaseDir, "test.agd.json");
        var result = _confinement.Confine(testPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(result.Success, "Windows paths should be case-insensitive");
            Assert.Null(result.ErrorCode);
        }
        else
        {
            Assert.False(result.Success, "Linux/macOS paths should be case-sensitive");
            Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        }
    }

    [Fact]
    public void Confine_SymlinkPointingOutside_ReturnsError()
    {
        var outsideDir = Path.Combine(Path.GetTempPath(), $"agd-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        var outsideFile = Path.Combine(outsideDir, "secret.txt");
        File.WriteAllText(outsideFile, "sensitive data");

        var symlink = Path.Combine(_testDir, "escape-link");
        try
        {
            Directory.CreateSymbolicLink(symlink, outsideDir);
        }
        catch (IOException)
        {
            Directory.Delete(outsideDir, true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(outsideDir, true);
            return;
        }

        try
        {
            var result = _confinement.Confine("escape-link/secret.txt");

            Assert.False(result.Success);
            Assert.Equal("PATH_ESCAPE", result.ErrorCode);
            Assert.NotNull(result.Error);
            Assert.Contains("symbolic link", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(outsideDir, result.Error);
        }
        finally
        {
            Directory.Delete(symlink);
            Directory.Delete(outsideDir, true);
        }
    }

    [Fact]
    public void Confine_SymlinkPointingInside_IsRejected()
    {
        var target = Path.Combine(_testDir, "target.agd.json");
        File.WriteAllText(target, "{}");
        var link = Path.Combine(_testDir, "linked.agd.json");

        try
        {
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            var result = _confinement.Confine("linked.agd.json");

            Assert.False(result.Success);
            Assert.Equal("PATH_ESCAPE", result.ErrorCode);
            Assert.Contains("reparse", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
        }
    }

    [Fact]
    public void Confine_InvalidPath_ReturnsPathEscape()
    {
        var result = _confinement.Confine("invalid\0path");

        Assert.False(result.Success);
        Assert.Null(result.Path);
        Assert.Equal("PATH_ESCAPE", result.ErrorCode);
        Assert.Equal("Path is invalid", result.Error);
    }

    [Fact]
    public void Confine_UnsearchableInRootDirectory_ReturnsAccessDenied()
    {
        if (OperatingSystem.IsWindows())
            return;
        var directory = Path.Combine(_testDir, "private-input");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "descriptor.json");
        File.WriteAllText(path, "{}");
        var mode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.None);
            try
            {
                _ = File.GetAttributes(path);
                return; // Privileged accounts can bypass mode bits; this is not access-denial evidence.
            }
            catch (UnauthorizedAccessException)
            {
                var result = _confinement.Confine(path);

                Assert.False(result.Success);
                Assert.Null(result.Path);
                Assert.Equal("ACCESS_DENIED", result.ErrorCode);
                Assert.Equal("Path is invalid", result.Error);
                Assert.DoesNotContain(_testDir, result.Error);
            }
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
        }
    }

    [Theory]
    [InlineData("IO_ERROR")]
    [InlineData("ACCESS_DENIED")]
    public void Failure_PreservesExplicitErrorCode(string code)
    {
        var result = PathConfinementResult.Fail("Path is invalid", code);

        Assert.False(result.Success);
        Assert.Null(result.Path);
        Assert.Equal("Path is invalid", result.Error);
        Assert.Equal(code, result.ErrorCode);
    }
}
