using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests;

public class ErrorSanitizerTests
{
    [Fact]
    public void SanitizePaths_NullMessage_ReturnsEmptyString()
    {
        var result = ErrorSanitizer.SanitizePaths(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizePaths_EmptyMessage_ReturnsEmptyString()
    {
        var result = ErrorSanitizer.SanitizePaths("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizePaths_MessageWithoutPaths_ReturnsOriginal()
    {
        var message = "Failed to parse SVG: Invalid root element";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal(message, result);
    }

    [Fact]
    public void SanitizePaths_WindowsPathWithBackslash_ReturnsFilename()
    {
        var message = @"Could not find file 'C:\Users\user\Documents\test.svg'.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Could not find file 'test.svg'.", result);
    }

    [Fact]
    public void SanitizePaths_WindowsPathWithForwardSlash_ReturnsFilename()
    {
        var message = "Could not find file 'C:/Users/user/Documents/test.svg'.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Could not find file 'test.svg'.", result);
    }

    [Fact]
    public void SanitizePaths_UnixHomePath_ReturnsFilename()
    {
        var message = "Could not find file '/home/user/documents/test.svg'.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Could not find file 'test.svg'.", result);
    }

    [Fact]
    public void SanitizePaths_UnixMntPath_ReturnsFilename()
    {
        var message = "Could not find file '/mnt/c/work/test.agd.json'.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Could not find file 'test.agd.json'.", result);
    }

    [Fact]
    public void SanitizePaths_UnixTmpPath_ReturnsFilename()
    {
        var message = "Could not find file '/tmp/temp-12345/data.json'.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Could not find file 'data.json'.", result);
    }

    [Fact]
    public void SanitizePaths_MultiplePaths_SanitizesAll()
    {
        var message = @"Failed to copy 'C:\source\file.txt' to '/home/user/dest/output.txt'.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Failed to copy 'file.txt' to 'output.txt'.", result);
    }

    [Fact]
    public void SanitizePaths_PreservesOtherContent()
    {
        var message = "Error at line 42: Could not read '/home/user/config.json' - permission denied.";
        var result = ErrorSanitizer.SanitizePaths(message);
        Assert.Equal("Error at line 42: Could not read 'config.json' - permission denied.", result);
    }
}
