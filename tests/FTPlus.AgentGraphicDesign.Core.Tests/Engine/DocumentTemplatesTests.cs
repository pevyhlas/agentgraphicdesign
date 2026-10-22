using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Engine;

public class DocumentTemplatesTests
{
    [Fact]
    public void Catalog_ContainsTheThreeShippedTemplates()
    {
        var ids = DocumentTemplates.All.Select(template => template.Id).ToList();

        Assert.Equal(["web-icon-32", "web-icon-24", "social-1200x630"], ids);
        Assert.Equal("web-icon-32, web-icon-24, social-1200x630", DocumentTemplates.IdList);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("web-icon-32", 32, 32, null)]
    [InlineData("web-icon-24", 24, 24, null)]
    [InlineData("social-1200x630", 1200, 630, "#ffffff")]
    public void TryGet_ReturnsCanvasPreset(string id, double width, double height, string? background)
    {
        Assert.True(DocumentTemplates.TryGet(id, out var template));

        Assert.Equal(width, template.Width);
        Assert.Equal(height, template.Height);
        Assert.Equal(background, template.Background);
        Assert.False(string.IsNullOrWhiteSpace(template.Description));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WEB-ICON-32")]
    [InlineData("web-icon-48")]
    public void TryGet_RejectsUnknownOrBlankIds(string? id)
    {
        Assert.False(DocumentTemplates.TryGet(id, out var template));
        Assert.Null(template);
    }

    [Fact]
    public void TryGet_IgnoresSurroundingWhitespace()
    {
        Assert.True(DocumentTemplates.TryGet("  web-icon-24 ", out var template));
        Assert.Equal("web-icon-24", template.Id);
    }

    [Fact]
    public void Get_UnknownId_ThrowsInvalidArgumentsListingValidIds()
    {
        var exception = Assert.Throws<DocumentEngineException>(() => DocumentTemplates.Get("poster"));

        Assert.Equal(EngineErrors.InvalidArguments, exception.Code);
        Assert.Contains("'poster'", exception.Message);
        foreach (var template in DocumentTemplates.All)
        {
            Assert.Contains(template.Id, exception.Message);
        }
    }

    [Fact]
    public void ResolveCanvas_TemplateSuppliesDefaultsAndExplicitValuesOverride()
    {
        var social = DocumentTemplates.Get("social-1200x630");

        var fromTemplate = DocumentTemplates.ResolveCanvas(social, null, null, null);
        var overridden = DocumentTemplates.ResolveCanvas(social, 600, null, "#000000");
        var engineDefaults = DocumentTemplates.ResolveCanvas(null, null, null, null);

        Assert.Equal(new CanvasSpec(1200, 630, "#ffffff"), fromTemplate);
        Assert.Equal(new CanvasSpec(600, 630, "#000000"), overridden);
        Assert.Equal(new CanvasSpec(1920, 1080, null), engineDefaults);
    }

    [Fact]
    public void Templates_CreateValidDocumentsThroughTheEngine()
    {
        var engine = new DocumentEngine();

        foreach (var template in DocumentTemplates.All)
        {
            var canvas = DocumentTemplates.ResolveCanvas(template, null, null, null);
            var document = engine.CreateDocument(template.Id, canvas.Width, canvas.Height, canvas.Background);

            Assert.Equal(template.Width, document.Canvas.Width);
            Assert.Equal(template.Height, document.Canvas.Height);
            Assert.Equal(template.Background, document.Canvas.Background);
            Assert.Equal(template.Width, document.Root.Width);
            Assert.Equal(template.Height, document.Root.Height);
            Assert.True(DocumentValidator.Validate(document).IsValid, $"template {template.Id} should create a valid document");
        }
    }
}
