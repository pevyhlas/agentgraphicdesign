using System.Text.Json;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class ListDocumentsCommandTests : CommandTestBase
{
    [Fact]
    public void ListDocuments_EmptyDirectory_ReturnsZero()
    {
        var (exitCode, output, _) = RunCommand("list-documents", "-d", TestDir, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, result.RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }

    [Fact]
    public void ListDocuments_WithDocuments_FindsAll()
    {
        // Create test documents
        CreateTestDocument("doc1");
        CreateTestDocument("doc2");
        CreateTestDocument("doc3");

        var (exitCode, output, _) = RunCommand("list-documents", "-d", TestDir, "-f", "json");

        Assert.Equal(0, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.Equal(3, result.RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }

    [Fact]
    public void ListDocuments_TextFormat_ReturnsReadable()
    {
        CreateTestDocument("my-design");

        var (exitCode, _, stderr) = RunCommand("list-documents", "-d", TestDir);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ListDocuments_InvalidDirectory_ReturnsError()
    {
        var (exitCode, output, _) = RunCommand("list-documents", "-d", "/nonexistent/path", "-f", "json");

        Assert.Equal(1, exitCode);
        var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DIRECTORY_NOT_FOUND", result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ListDocuments_DefaultsToCurrentDirectory()
    {
        var (exitCode, _, _) = RunCommand("list-documents", "-f", "json");

        // Should succeed even if no documents in current dir
        Assert.Equal(0, exitCode);
    }

    private void CreateTestDocument(string name)
    {
        var (exitCode, _, _) = RunCommand("create", name, "-o", Path.Combine(TestDir, $"{name}.agd.json"));
        Assert.Equal(0, exitCode);
    }
}
