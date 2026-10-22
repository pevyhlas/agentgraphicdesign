using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests;

public class ParseErrorTests : IDisposable
{
    private readonly string _testDir;
    private readonly DocumentEngine _engine;

    public ParseErrorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-parse-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _engine = new DocumentEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void TryLoadDocumentFromFile_MalformedJson_ReturnsParseErrorWithDetails()
    {
        var filePath = Path.Combine(_testDir, "malformed.agd.json");
        File.WriteAllText(filePath, "{ invalid json }");

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error.Message);
    }

    [Fact]
    public void TryLoadDocumentFromFile_EmptyFile_ReturnsParseError()
    {
        var filePath = Path.Combine(_testDir, "empty.agd.json");
        File.WriteAllText(filePath, "");

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryLoadDocumentFromFile_NullJson_ReturnsParseError()
    {
        var filePath = Path.Combine(_testDir, "null.agd.json");
        File.WriteAllText(filePath, "null");

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryLoadDocumentFromFile_MissingFile_ReturnsError()
    {
        var filePath = Path.Combine(_testDir, "nonexistent.agd.json");

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLoadDocumentFromFile_InvalidPropertyType_ReturnsParseError()
    {
        var filePath = Path.Combine(_testDir, "invalid-type.agd.json");
        File.WriteAllText(filePath, """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "name": "test",
                "canvas": { "width": "not-a-number", "height": 100 }
            }
            """);

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryLoadDocumentFromFile_IncompleteJson_ReturnsErrorWithLineNumber()
    {
        var filePath = Path.Combine(_testDir, "incomplete.agd.json");
        File.WriteAllText(filePath, """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "name": "test"
            """);

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.Error.LineNumber);
    }

    [Fact]
    public void TryLoadDocumentFromFile_ValidDocument_ReturnsSuccess()
    {
        var filePath = Path.Combine(_testDir, "valid.agd.json");
        var doc = _engine.CreateDocument("test", 100, 100);
        _engine.SaveDocumentToFile(doc, filePath);

        var result = _engine.TryLoadDocumentFromFile(filePath);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Null(result.Error);
        Assert.Equal(doc.Id, result.Document.Id);
    }

    [Fact]
    public void LoadDocumentFromFile_MalformedJson_ReturnsNull()
    {
        var filePath = Path.Combine(_testDir, "malformed2.agd.json");
        File.WriteAllText(filePath, "{ bad }");

        var result = _engine.LoadDocumentFromFile(filePath);

        Assert.Null(result);
    }
}
