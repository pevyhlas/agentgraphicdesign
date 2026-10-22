using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Validation;

public class WebIconValidationTests
{
    [Fact]
    public void WebIcon_AllowsGeometryStylesGradientsTransformsAndTokenPaints()
    {
        var document = CreateDocument();
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string> { ["accent"] = "#336699" }
        };
        document.Root.Children.Add(new GroupNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Transform = new Transform { X = 2, Y = 3, Rotation = 5, ScaleX = 0.5, ScaleY = 0.5 },
            Children =
            [
                new RectangleNode
                {
                    Id = Guid.NewGuid(),
                    Order = "a0",
                    Width = 20,
                    Height = 20,
                    CornerRadius = 2,
                    Style = new Style
                    {
                        Fill = new Fill { TokenRef = "{accent}" },
                        Stroke = new Stroke { Color = "rgba(0, 0, 0, 0.5)", Width = 2 }
                    }
                },
                new EllipseNode
                {
                    Id = Guid.NewGuid(),
                    Order = "a1",
                    Width = 20,
                    Height = 20,
                    Style = new Style
                    {
                        Fill = new Fill
                        {
                            Gradient = new LinearGradient
                            {
                                Stops =
                                [
                                    new GradientStop { Offset = 0, Color = "#ffffff" },
                                    new GradientStop { Offset = 1, Color = "#000000" }
                                ]
                            }
                        }
                    }
                },
                new PathNode
                {
                    Id = Guid.NewGuid(),
                    Order = "a2",
                    D = "M0 0L10 10Z",
                    Style = new Style { Fill = new Fill { Color = "#123456" } }
                },
                new LineNode
                {
                    Id = Guid.NewGuid(),
                    Order = "a3",
                    X1 = 0,
                    Y1 = 0,
                    X2 = 10,
                    Y2 = 10
                },
                new PolygonNode
                {
                    Id = Guid.NewGuid(),
                    Order = "a4",
                    Points =
                    [
                        new Point { X = 0, Y = 0 },
                        new Point { X = 10, Y = 0 },
                        new Point { X = 5, Y = 10 }
                    ]
                }
            ]
        });

        var result = DocumentValidator.Validate(document, ValidationOptions.WebIcon);

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(error => error.Code)));
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("text", WebIconValidationCodes.TextNode)]
    [InlineData("image", WebIconValidationCodes.ImageNode)]
    [InlineData("unknown", WebIconValidationCodes.UnknownNode)]
    public void WebIcon_RejectsProhibitedNodeCategoriesWithoutChangingDefault(string category, string code)
    {
        var document = CreateDocument();
        document.Root.Children.Add(CreateNode(category, "a0"));

        var defaultResult = DocumentValidator.Validate(document);
        var profileResult = DocumentValidator.Validate(document, ValidationOptions.WebIcon);

        Assert.True(defaultResult.IsValid);
        Assert.DoesNotContain(defaultResult.Errors, error => error.Code.StartsWith("WEBICON", StringComparison.Ordinal));
        var error = Assert.Single(profileResult.Errors, error => error.Code == code);
        Assert.Equal("root.children[0].type", error.Path);
    }

    [Fact]
    public void WebIcon_RejectsProhibitedNodesAtNestedPath()
    {
        var document = CreateDocument();
        document.Root.Children.Add(new GroupNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Children = [CreateNode("text", "a0")]
        });

        var result = DocumentValidator.Validate(document, ValidationOptions.WebIcon);

        var error = Assert.Single(result.Errors, error => error.Code == WebIconValidationCodes.TextNode);
        Assert.Equal("root.children[0].children[0].type", error.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#0000")]
    [InlineData("#ffffff00")]
    [InlineData("rgba(255, 255, 255, 0)")]
    public void WebIcon_AllowsNullOrFullyTransparentBackground(string? background)
    {
        var document = CreateDocument();
        document.Canvas.Background = background;

        var result = DocumentValidator.Validate(document, ValidationOptions.WebIcon);

        Assert.DoesNotContain(result.Errors, error => error.Code == WebIconValidationCodes.Background);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("#ffffff")]
    [InlineData("#ffffff01")]
    [InlineData("rgb(0, 0, 0)")]
    [InlineData("rgba(0, 0, 0, 0.1)")]
    [InlineData("transparent")]
    public void WebIcon_RejectsAnyNonTransparentOrNonStrictBackground(string background)
    {
        var document = CreateDocument();
        document.Canvas.Background = background;

        var result = DocumentValidator.Validate(document, ValidationOptions.WebIcon);

        var error = Assert.Single(result.Errors, error => error.Code == WebIconValidationCodes.Background);
        Assert.Equal("canvas.background", error.Path);
    }

    [Fact]
    public void WebIcon_RejectsOpaqueExportBackgroundOverride()
    {
        var document = CreateDocument();

        var result = DocumentValidator.Validate(document, new ValidationOptions
        {
            Profile = ValidationProfile.WebIcon,
            BackgroundOverride = "#ffffff"
        });

        var error = Assert.Single(result.Errors, error => error.Code == WebIconValidationCodes.Background);
        Assert.Equal("export.background", error.Path);
    }

    [Fact]
    public void WebIcon_PreservesOrdinaryValidationErrorsAlongsideProfileErrors()
    {
        var document = CreateDocument();
        document.Root.Children.Add(new TextNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Text = "label",
            FontSize = 0
        });

        var result = DocumentValidator.Validate(document, ValidationOptions.WebIcon);

        Assert.Contains(result.Errors, error => error.Code == "TEXT001" && error.Path == "root.children[0].fontSize");
        Assert.Contains(result.Errors, error => error.Code == WebIconValidationCodes.TextNode && error.Path == "root.children[0].type");
    }

    private static Node CreateNode(string category, string order) => category switch
    {
        "text" => new TextNode
        {
            Id = Guid.NewGuid(),
            Order = order,
            Text = "label",
            FontSize = 16
        },
        "image" => new ImageNode
        {
            Id = Guid.NewGuid(),
            Order = order,
            Src = "asset.png",
            Width = 16,
            Height = 16
        },
        "unknown" => new UnknownNode
        {
            Id = Guid.NewGuid(),
            Order = order,
            OriginalType = "future-resource"
        },
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static Document CreateDocument() => new()
    {
        Id = Guid.NewGuid(),
        Name = "web-icon",
        Version = 1,
        Revn = 1,
        Canvas = new Canvas { Width = 24, Height = 24 },
        Root = new FrameNode
        {
            Id = Guid.NewGuid(),
            Order = "a0",
            Width = 24,
            Height = 24
        }
    };
}
