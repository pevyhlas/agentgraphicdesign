using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests;

public class ValidationTests
{
    [Fact]
    public void Validate_ValidDocument_ReturnsNoErrors()
    {
        var document = CreateValidDocument();

        var result = DocumentValidator.Validate(document);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EmptyDocumentId_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Id = Guid.Empty;

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DOC001");
    }

    [Fact]
    public void Validate_EmptyDocumentName_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Name = "";

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DOC002");
    }

    [Fact]
    public void Validate_ZeroCanvasWidth_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Canvas.Width = 0;

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CANVAS001");
    }

    [Fact]
    public void Validate_NegativeCanvasHeight_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Canvas.Height = -100;

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CANVAS002");
    }

    [Fact]
    public void Validate_LargeCanvas_ReturnsWarning()
    {
        var document = CreateValidDocument();
        document.Canvas.Width = 20000;

        var result = DocumentValidator.Validate(document);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "CANVAS003");
    }

    [Fact]
    public void Validate_DuplicateNodeIds_ReturnsError()
    {
        var duplicateId = Guid.NewGuid();
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new RectangleNode { Id = duplicateId, Width = 100, Height = 100 },
            new RectangleNode { Id = duplicateId, Width = 50, Height = 50 }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NODE002");
    }

    [Fact]
    public void Validate_InvalidFillColor_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new RectangleNode
            {
                Width = 100,
                Height = 100,
                Style = new Style { Fill = new Fill { Color = "not-a-color" } }
            }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "STYLE002");
    }

    [Fact]
    public void Validate_OpacityOutOfRange_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new RectangleNode
            {
                Width = 100,
                Height = 100,
                Style = new Style { Opacity = 1.5 }
            }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "STYLE001");
    }

    [Fact]
    public void Validate_NegativeRectangleWidth_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new RectangleNode { Width = -50, Height = 100 }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "RECT001");
    }

    [Fact]
    public void Validate_NegativeCornerRadius_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new RectangleNode { Width = 100, Height = 100, CornerRadius = -5 }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "RECT003");
    }

    [Fact]
    public void Validate_ZeroFontSize_ReturnsError()
    {
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new TextNode { Text = "Hello", FontSize = 0 }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TEXT001");
    }

    [Fact]
    public void Validate_ValidHexColors_ReturnsNoErrors()
    {
        var document = CreateValidDocument();
        document.Root.Children =
        [
            new RectangleNode
            {
                Width = 100,
                Height = 100,
                Style = new Style
                {
                    Fill = new Fill { Color = "#fff" },
                    Stroke = new Stroke { Color = "#ff00ff" }
                }
            }
        ];

        var result = DocumentValidator.Validate(document);

        Assert.True(result.IsValid);
    }

    private static Document CreateValidDocument()
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = "test-document",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 800, Height = 600 },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = 800,
                Height = 600,
                Children = []
            }
        };
    }
}
