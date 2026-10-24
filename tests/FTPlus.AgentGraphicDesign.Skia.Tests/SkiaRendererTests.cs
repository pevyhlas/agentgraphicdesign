using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>
/// Tests for SkiaRenderer PNG/JPEG/WebP raster export.
/// </summary>
public class SkiaRendererTests
{
    private readonly SkiaRenderer _renderer = new();

    [Theory]
    [InlineData("#f008", "rgba(255,0,0,0.5333333333)", 136)]
    [InlineData("#ff000080", "rgba(255,0,0,0.5)", 128)]
    [InlineData("#f00", "rgb(255,0,0)", 255)]
    [InlineData("#ff000000", "rgba(255,0,0,0)", 0)]
    public void CssAlphaHexMatchesRgbaAcrossPaintAndBackground(string hex, string equivalent, byte alpha)
    {
        foreach (var mode in new[] { "fill", "stroke", "gradient", "canvas", "override" })
        {
            RenderResult Render(string color)
            {
                var doc = CreateEmptyDocument(width: 32, height: 32);
                if (mode == "canvas") doc.Canvas.Background = color;
                else if (mode != "override")
                {
                    var style = new Style();
                    if (mode == "stroke") style.Stroke = new Stroke { Color = color, Width = 32 };
                    else if (mode == "gradient") style.Fill = new Fill
                    {
                        Gradient = new LinearGradient
                        {
                            Stops = [new GradientStop { Offset = 0, Color = color }, new GradientStop { Offset = 1, Color = color }]
                        }
                    };
                    else style.Fill = new Fill { Color = color };
                    doc.Root.Children.Add(new RectangleNode { Width = 32, Height = 32, Style = style });
                }
                return _renderer.Render(doc, new RenderOptions { Background = mode == "override" ? color : null });
            }
            var rendered = Render(hex);
            Assert.Equal(Render(equivalent).Data, rendered.Data);
            using var bitmap = SKBitmap.Decode(rendered.Data)!;
            var pixel = bitmap.GetPixel(8, 8);
            Assert.Equal(alpha, pixel.Alpha);
            if (alpha != 0)
            {
                Assert.Equal((byte)255, pixel.Red);
                Assert.Equal((byte)0, pixel.Green);
                Assert.Equal((byte)0, pixel.Blue);
            }
        }
    }

    // ---- PNG format ----

    [Fact]
    public void Render_EmptyDocument_ProducesValidPng()
    {
        var document = CreateEmptyDocument();

        var result = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Png });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);
        Assert.Equal("image/png", result.MimeType);
        AssertPngSignature(result.Data);
    }

    [Fact]
    public void Render_BasicShapes_ProducesValidPng()
    {
        var document = CreateDocumentWithShapes();

        var result = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Png });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100, "PNG should have meaningful content");
        Assert.Equal("image/png", result.MimeType);
        AssertPngSignature(result.Data);
    }

    [Fact]
    public void Render_Png_DefaultDimensions_MatchesCanvas()
    {
        var document = CreateEmptyDocument(width: 400, height: 300);

        var result = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Png });

        Assert.Equal(400, result.Width);
        Assert.Equal(300, result.Height);
    }

    [Fact]
    public void Render_Png_OverrideDimensions()
    {
        var document = CreateEmptyDocument(width: 400, height: 300);

        var result = _renderer.Render(document, new RenderOptions
        {
            Format = RenderFormat.Png,
            Width = 200,
            Height = 100
        });

        Assert.Equal(200, result.Width);
        Assert.Equal(100, result.Height);
    }

    [Fact]
    public void Render_Png_ScaleFactor()
    {
        var document = CreateEmptyDocument(width: 100, height: 100);

        var result = _renderer.Render(document, new RenderOptions
        {
            Format = RenderFormat.Png,
            Scale = 2.0
        });

        Assert.Equal(200, result.Width);
        Assert.Equal(200, result.Height);
    }

    [Fact]
    public void Render_Png_WithBackground()
    {
        var document = CreateEmptyDocument();

        var result = _renderer.Render(document, new RenderOptions
        {
            Format = RenderFormat.Png,
            Background = "#ff0000"
        });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);
    }

    // ---- JPEG format ----

    [Fact]
    public void Render_Jpeg_ProducesValidJpeg()
    {
        var document = CreateDocumentWithShapes();

        var result = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Jpeg });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);
        Assert.Equal("image/jpeg", result.MimeType);
        AssertJpegSignature(result.Data);
    }

    // ---- WebP format ----

    [Fact]
    public void Render_Webp_ProducesValidWebp()
    {
        var document = CreateDocumentWithShapes();

        var result = _renderer.Render(document, new RenderOptions { Format = RenderFormat.Webp });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);
        Assert.Equal("image/webp", result.MimeType);
        AssertWebpSignature(result.Data);
    }

    // ---- Dimension validation ----

    [Fact]
    public void Render_ZeroDimensions_Throws()
    {
        var document = CreateEmptyDocument(width: 0, height: 100);

        var ex = Assert.Throws<ArgumentException>(() =>
            _renderer.Render(document, new RenderOptions()));

        Assert.Contains("must be positive", ex.Message);
    }

    [Fact]
    public void Render_NegativeDimensions_Throws()
    {
        var document = CreateEmptyDocument();

        var ex = Assert.Throws<ArgumentException>(() =>
            _renderer.Render(document, new RenderOptions { Width = -1 }));

        Assert.Contains("must be positive", ex.Message);
    }

    [Fact]
    public void Render_ExceedsMaxDimension_Throws()
    {
        var document = CreateEmptyDocument();

        var ex = Assert.Throws<ArgumentException>(() =>
            _renderer.Render(document, new RenderOptions { Width = 10000 }));

        Assert.Contains("exceed maximum", ex.Message);
    }

    [Fact]
    public void Render_ExceedsMaxPixelCount_Throws()
    {
        var document = CreateEmptyDocument();

        var ex = Assert.Throws<ArgumentException>(() =>
            _renderer.Render(document, new RenderOptions { Width = 8000, Height = 8100 }));

        Assert.Contains("pixel count exceeds maximum", ex.Message);
    }

    // ---- Node rendering ----

    [Fact]
    public void Render_Rectangle_ProducesOutput()
    {
        var document = CreateDocumentWith(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 100,
            Height = 50,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });

        var result = _renderer.Render(document, new RenderOptions());

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");
        Assert.True(HasNonTransparentPixel(bitmap), "Shaped text must alter output pixels");
    }

    [Fact]
    public void Render_Ellipse_ProducesOutput()
    {
        var document = CreateDocumentWith(new EllipseNode
        {
            Id = Guid.NewGuid(),
            Name = "ellipse",
            Width = 100,
            Height = 80,
            Style = new Style { Fill = new Fill { Color = "#00ff00" } }
        });

        var result = _renderer.Render(document, new RenderOptions());

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public void Render_Path_ProducesOutput()
    {
        var document = CreateDocumentWith(new PathNode
        {
            Id = Guid.NewGuid(),
            Name = "path",
            D = "M 10 10 L 90 10 L 50 90 Z",
            Style = new Style { Fill = new Fill { Color = "#0000ff" } }
        });

        var result = _renderer.Render(document, new RenderOptions());

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public void Render_Text_ProducesOutput()
    {
        var document = CreateDocumentWith(new TextNode
        {
            Id = Guid.NewGuid(),
            Name = "text",
            Text = "Hello",
            FontSize = 24,
            Style = new Style { Fill = new Fill { Color = "#333333" } }
        });

        var result = _renderer.Render(document, new RenderOptions());

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public void Render_Line_ProducesOutput()
    {
        var document = CreateDocumentWith(new LineNode
        {
            Id = Guid.NewGuid(),
            Name = "line",
            X1 = 10,
            Y1 = 10,
            X2 = 90,
            Y2 = 90,
            Style = new Style { Stroke = new Stroke { Color = "#000000", Width = 2 } }
        });

        var result = _renderer.Render(document, new RenderOptions());

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public void Render_Polygon_ProducesOutput()
    {
        var document = CreateDocumentWith(new PolygonNode
        {
            Id = Guid.NewGuid(),
            Name = "polygon",
            Points = [new Point { X = 50, Y = 10 }, new Point { X = 90, Y = 90 }, new Point { X = 10, Y = 90 }],
            Closed = true,
            Style = new Style { Fill = new Fill { Color = "#ffff00" } }
        });

        var result = _renderer.Render(document, new RenderOptions());

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public void Render_InvisibleNode_Excluded()
    {
        var visibleDoc = CreateDocumentWith(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 100,
            Height = 100,
            Visible = true,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });

        var invisibleDoc = CreateDocumentWith(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Width = 100,
            Height = 100,
            Visible = false,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });

        var visibleResult = _renderer.Render(visibleDoc, new RenderOptions());
        var invisibleResult = _renderer.Render(invisibleDoc, new RenderOptions());

        Assert.True(visibleResult.Data.Length > invisibleResult.Data.Length,
            "Visible shape should produce larger output than invisible");
    }

    // ---- Determinism ----

    [Fact]
    public void Render_SameInput_ProducesSameOutput()
    {
        var document = CreateDocumentWithShapes();
        var options = new RenderOptions { Format = RenderFormat.Png };

        var result1 = _renderer.Render(document, options);
        var result2 = _renderer.Render(document, options);

        Assert.Equal(result1.Data, result2.Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1001)]
    public void Render_InvalidScale_Throws(double scale)
    {
        Assert.Throws<ArgumentException>(() =>
            _renderer.Render(CreateEmptyDocument(), new RenderOptions { Scale = scale }));
    }

    [Fact]
    public void Render_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _renderer.Render(CreateEmptyDocument(), new RenderOptions { Format = (RenderFormat)999 }));
    }

    [Fact]
    public void Render_FractionalCanvasDimension_RoundsOutputConsistently()
    {
        var result = _renderer.Render(
            CreateEmptyDocument(width: 10.5, height: 20.5),
            new RenderOptions());

        Assert.Equal(11, result.Width);
        Assert.Equal(21, result.Height);
    }

    [Fact]
    public void Render_UnstyledRectangle_UsesExplicitBlackDefault()
    {
        var document = CreateDocumentWith(new RectangleNode { Width = 100, Height = 100 });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");
        var pixel = bitmap.GetPixel(50, 50);

        Assert.Equal(SKColors.Black, pixel);
    }

    [Fact]
    public void Render_ColorAlias_ResolvesWithoutMutatingSource()
    {
        var rectangle = new RectangleNode
        {
            Width = 100,
            Height = 100,
            Style = new Style { Fill = new Fill { TokenRef = "{semantic}" } }
        };
        var document = CreateDocumentWith(rectangle);
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["base"] = "#123456",
                ["semantic"] = "{base}"
            }
        };

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.Equal(new SKColor(0x12, 0x34, 0x56), bitmap.GetPixel(50, 50));
        Assert.Null(rectangle.Style!.Fill!.Color);
    }

    [Fact]
    public void Render_GroupOpacity_CompositesChildrenOnce()
    {
        var group = new GroupNode
        {
            Style = new Style { Opacity = 0.5 },
            Children =
            [
                new RectangleNode
                {
                    Width = 100,
                    Height = 100,
                    Style = new Style { Fill = new Fill { Color = "#ff0000" } }
                }
            ]
        };
        var document = CreateDocumentWith(group);

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");
        var pixel = bitmap.GetPixel(50, 50);

        Assert.InRange(pixel.Alpha, (byte)126, (byte)129);
        Assert.Equal((byte)255, pixel.Red);
    }

    [Fact]
    public void Render_BoundedEmbeddedPng_RendersSourcePixels()
    {
        using var sourceSurface = SKSurface.Create(new SKImageInfo(2, 2))
            ?? throw new InvalidOperationException("Failed to create source surface");
        sourceSurface.Canvas.Clear(SKColors.Red);
        using var sourceImage = sourceSurface.Snapshot();
        using var encoded = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        var source = $"data:image/png;base64,{Convert.ToBase64String(encoded.ToArray())}";
        var document = CreateDocumentWith(new ImageNode
        {
            Src = source,
            Width = 100,
            Height = 100
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.Equal(ImageRenderingCapability.BoundedEmbeddedRasterWithPlaceholderFallback, _renderer.ImageCapability);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 50));
    }

    [Fact]
    public void Render_UnsupportedImageSource_RendersVisiblePlaceholder()
    {
        var document = CreateDocumentWith(new ImageNode
        {
            Src = "https://example.test/image.png",
            Width = 100,
            Height = 100
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.NotEqual(SKColors.Transparent, bitmap.GetPixel(0, 0));
        Assert.NotEqual(SKColors.Red, bitmap.GetPixel(50, 50));
    }

    [Fact]
    public void Render_PrimitiveOpacity_CompositesFillAndStrokeOnce()
    {
        var document = CreateDocumentWith(new RectangleNode
        {
            Width = 100,
            Height = 100,
            Style = new Style
            {
                Fill = new Fill { Color = "#ff0000" },
                Stroke = new Stroke { Color = "#0000ff", Width = 20 },
                Opacity = 0.5
            }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.InRange(bitmap.GetPixel(5, 50).Alpha, (byte)126, (byte)129);
        Assert.InRange(bitmap.GetPixel(50, 50).Alpha, (byte)126, (byte)129);
    }

    [Fact]
    public void Render_TextStrokeWithoutFill_ProducesVisiblePixels()
    {
        var document = CreateDocumentWith(new TextNode
        {
            Text = "Stroke",
            FontSize = 32,
            Style = new Style
            {
                Stroke = new Stroke { Color = "#00ff00", Width = 2 }
            }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.True(HasNonTransparentPixel(bitmap));
    }

    [Fact]
    public void Render_OddDashArray_IsRepeatedRatherThanDropped()
    {
        var document = CreateDocumentWith(new LineNode
        {
            X1 = 0,
            Y1 = 20,
            X2 = 180,
            Y2 = 20,
            Style = new Style
            {
                Stroke = new Stroke { Color = "#000000", Width = 4, DashArray = [10, 5, 2] }
            }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var opaquePixels = Enumerable.Range(0, 180).Count(x => bitmap.GetPixel(x, 20).Alpha > 0);
        Assert.InRange(opaquePixels, 1, 179);
    }

    [Fact]
    public void Render_RadialFocalPoint_ChangesRasterResult()
    {
        static Document Create(double focalX) => CreateDocumentWith(new RectangleNode
        {
            Width = 100,
            Height = 50,
            Style = new Style
            {
                Fill = new Fill
                {
                    Gradient = new RadialGradient
                    {
                        Cx = 0.5,
                        Cy = 0.5,
                        R = 0.5,
                        Fx = focalX,
                        Fy = 0.5,
                        Stops =
                        [
                            new GradientStop { Offset = 0, Color = "#ffffff" },
                            new GradientStop { Offset = 1, Color = "#000000" }
                        ]
                    }
                }
            }
        });

        var centered = _renderer.Render(Create(0.5), new RenderOptions()).Data;
        var offset = _renderer.Render(Create(0.2), new RenderOptions()).Data;

        Assert.False(centered.SequenceEqual(offset));
    }

    [Fact]
    public void Render_DataImageMimeMismatch_UsesPlaceholder()
    {
        const string gifDeclaredAsPng = "data:image/png;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==";
        var document = CreateDocumentWith(new ImageNode
        {
            Src = gifDeclaredAsPng,
            Width = 100,
            Height = 100
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.NotEqual(SKColors.Transparent, bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void Render_PreCancelled_ThrowsWithoutAllocatingSurface()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _renderer.Render(CreateEmptyDocument(), new RenderOptions(), cancellation.Token));
    }

    [Fact]
    public void Render_OverPathBudget_IsRejected()
    {
        var document = CreateDocumentWith(new PathNode
        {
            D = new string('0', SkiaRenderer.MaxPathDataCharacters + 1)
        });

        Assert.Throws<InvalidOperationException>(() =>
            _renderer.Render(document, new RenderOptions()));
    }

    [Fact]
    public void Render_CyclicSceneGraph_IsRejected()
    {
        var document = CreateEmptyDocument();
        var group = new GroupNode();
        group.Children.Add(group);
        document.Root.Children.Add(group);

        Assert.Throws<InvalidOperationException>(() =>
            _renderer.Render(document, new RenderOptions()));
    }

    [Fact]
    public void Render_DoesNotMutateCompleteSourceDocument()
    {
        var rectangle = new RectangleNode
        {
            Width = 100,
            Height = 100,
            Style = new Style { Fill = new Fill { TokenRef = "{semantic}" } }
        };
        var document = CreateDocumentWith(rectangle);
        document.Tokens = new DesignTokens
        {
            Colors = new Dictionary<string, string>
            {
                ["base"] = "#123456",
                ["semantic"] = "{base}"
            }
        };
        var sourceBefore = DocumentSerializer.Serialize(document);

        _renderer.Render(document, new RenderOptions());

        Assert.Equal(sourceBefore, DocumentSerializer.Serialize(document));
    }

    [Fact]
    public void Render_ImportedNonzeroViewBox_ClipsOutsideContentAtCanvasBoundary()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="10 0 10 10">
              <rect x="0" y="0" width="10" height="10" fill="#ff0000"/>
              <rect x="10" y="0" width="10" height="10" fill="#00ff00"/>
            </svg>
            """;
        var imported = new SvgImportService().Import(svg);
        Assert.True(imported.Success);

        var result = _renderer.Render(imported.Document!, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(5, 5));
    }

    // ---- Fonts ----

    [Fact]
    public void Render_TextWithoutSystemFonts_IsByteIdentical()
    {
        var document = CreateDocumentWith(CreateText("Deterministic", fontFamily: null, fontWeight: 400));
        var options = new RenderOptions { Format = RenderFormat.Png, SystemFonts = false };

        var first = _renderer.Render(document, options);
        var second = _renderer.Render(document, options);

        Assert.Equal(first.Data, second.Data);
        Assert.Empty(first.Diagnostics);
        Assert.True(first.Data.Length > 100);
    }

    [Fact]
    public void Render_NullFontFamily_UsesBundledInterWithoutDiagnostics()
    {
        var document = CreateDocumentWith(CreateText("Hello", fontFamily: null, fontWeight: 700));

        var result = _renderer.Render(document, new RenderOptions());

        Assert.Empty(result.Diagnostics);
        AssertPngSignature(result.Data);
    }

    [Fact]
    public void Render_MissingFontFamily_SubstitutesAndReportsDiagnostic()
    {
        var document = CreateDocumentWith(CreateText("Hello", fontFamily: "Nonexistent Family", fontWeight: 400));

        var result = _renderer.Render(document, new RenderOptions { SystemFonts = false });

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnosticCodes.FontSubstituted, diagnostic.Code);
        Assert.Contains("'Nonexistent Family'", diagnostic.Message);
        Assert.Contains("Inter", diagnostic.Message);
    }

    [Fact]
    public void Render_MissingFontFamily_StrictFonts_Fails()
    {
        var document = CreateDocumentWith(CreateText("Hello", fontFamily: "Nonexistent Family", fontWeight: 400));

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _renderer.Render(document, new RenderOptions { SystemFonts = false, StrictFonts = true }));

        Assert.Equal(EngineErrors.FontNotFound, exception.Code);
    }

    [Fact]
    public void Render_FontDirectory_IsPreferredOverBundledInter()
    {
        var fontDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"agd-render-fonts-{Guid.NewGuid():N}")).FullName;
        try
        {
            using (var resource = typeof(BundledFonts).Assembly.GetManifestResourceStream(BundledFonts.BoldResourceName)!)
            using (var file = File.Create(Path.Combine(fontDirectory, "only-bold.ttf")))
            {
                resource.CopyTo(file);
            }
            var document = CreateDocumentWith(CreateText("Agent Graphic Design", fontFamily: "Inter", fontWeight: 400));

            var bundled = _renderer.Render(document, new RenderOptions { SystemFonts = false, StrictFonts = true });
            var directory = _renderer.Render(document, new RenderOptions
            {
                FontDirectories = [fontDirectory],
                SystemFonts = false,
                StrictFonts = true
            });

            // The directory only offers Bold, which wins over the bundled Regular face.
            Assert.NotEqual(bundled.Data, directory.Data);
            Assert.Empty(directory.Diagnostics);
        }
        finally
        {
            Directory.Delete(fontDirectory, recursive: true);
        }
    }

    [Fact]
    public void Render_MissingFontDirectory_ThrowsBeforeRendering()
    {
        var document = CreateEmptyDocument();

        Assert.Throws<ArgumentException>(() => _renderer.Render(document, new RenderOptions
        {
            FontDirectories = [Path.Combine(Path.GetTempPath(), $"agd-missing-{Guid.NewGuid():N}")]
        }));
    }

    private static TextNode CreateText(string text, string? fontFamily, int fontWeight) => new()
    {
        Id = Guid.NewGuid(),
        Name = "text",
        Text = text,
        FontFamily = fontFamily,
        FontSize = 32,
        FontWeight = fontWeight,
        Transform = new Transform { X = 10, Y = 40 },
        Style = new Style { Fill = new Fill { Color = "#222222" } }
    };
    // ---- Text layout (roadmap 0.2) ----

    [Fact]
    public void Render_WrappedText_StaysWithinMaxWidth()
    {
        const int anchorX = 10;
        const int maxWidth = 120;
        var document = CreateEmptyDocument(300, 200);
        document.Root.Children.Add(new TextNode
        {
            Order = "a0",
            Text = "The quick brown fox jumps over the lazy dog again and again",
            FontSize = 16,
            MaxWidth = maxWidth,
            Transform = new Transform { X = anchorX, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var inkColumns = InkColumns(bitmap);
        Assert.NotEmpty(inkColumns);
        Assert.True(inkColumns.Max() <= anchorX + maxWidth + 1, $"ink reached column {inkColumns.Max()} beyond maxWidth");
        // Every column beyond the wrap width is background.
        for (var x = anchorX + maxWidth + 2; x < bitmap.Width; x++)
            for (var y = 0; y < bitmap.Height; y++)
                Assert.True(bitmap.GetPixel(x, y).Alpha == 0, $"unexpected ink at ({x},{y})");
        // The paragraph needed several lines: ink well below a single 16 px line box.
        Assert.Contains(InkRows(bitmap), y => y > 10 + 16 * 1.2 * 2);
    }

    [Fact]
    public void Render_CenteredText_IsSymmetricAroundAnchor()
    {
        const int anchorX = 150;
        var document = CreateEmptyDocument(300, 100);
        document.Root.Children.Add(new TextNode
        {
            Order = "a0",
            Text = "HHHH",
            FontSize = 32,
            TextAlign = "center",
            Transform = new Transform { X = anchorX, Y = 20 },
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var inkColumns = InkColumns(bitmap);
        var leftExtent = anchorX - inkColumns.Min();
        var rightExtent = inkColumns.Max() + 1 - anchorX;
        Assert.True(Math.Abs(leftExtent - rightExtent) <= 1, $"left {leftExtent} px vs right {rightExtent} px");
    }

    [Fact]
    public void Render_EndAlignedText_EndsAtAnchor()
    {
        const int anchorX = 250;
        var document = CreateEmptyDocument(300, 100);
        document.Root.Children.Add(new TextNode
        {
            Order = "a0",
            Text = "HHHH",
            FontSize = 32,
            TextAlign = "end",
            Transform = new Transform { X = anchorX, Y = 20 },
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var inkColumns = InkColumns(bitmap);
        Assert.InRange(inkColumns.Max(), anchorX - 6, anchorX);
        Assert.True(inkColumns.Min() < anchorX - 40);
    }

    [Fact]
    public void Render_MultiLineText_StacksLinesAtLineHeight()
    {
        var document = CreateEmptyDocument(200, 200);
        document.Root.Children.Add(new TextNode
        {
            Order = "a0",
            Text = "A\nA",
            FontSize = 20,
            LineHeight = 2,
            Transform = new Transform { X = 10, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        var inkRows = InkRows(bitmap);
        // First baseline at y = 30, second at y = 70 (pitch 40): caps above each baseline, nothing between.
        Assert.Contains(inkRows, y => y is >= 12 and <= 30);
        Assert.Contains(inkRows, y => y is >= 52 and <= 70);
        Assert.DoesNotContain(inkRows, y => y is >= 34 and <= 50);
    }

    [Fact]
    public void Render_LetterSpacing_WidensInk()
    {
        static Document Create(double? spacing) => CreateDocumentWith(new TextNode
        {
            Text = "HHHH",
            FontSize = 24,
            LetterSpacing = spacing,
            Transform = new Transform { X = 10, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        using var plain = SKBitmap.Decode(_renderer.Render(Create(null), new RenderOptions()).Data)!;
        using var spaced = SKBitmap.Decode(_renderer.Render(Create(5), new RenderOptions()).Data)!;

        var plainWidth = InkColumns(plain).Max() - InkColumns(plain).Min();
        var spacedWidth = InkColumns(spaced).Max() - InkColumns(spaced).Min();
        // Three gaps between four glyphs widen by 5 px each; the trailing advance carries no ink.
        Assert.InRange(spacedWidth - plainWidth, 14, 16);
    }

    [Fact]
    public void Render_VerticalAlignBaseline_LiftsTextByFontSize()
    {
        static Document Create(string? verticalAlign) => CreateDocumentWith(new TextNode
        {
            Text = "H",
            FontSize = 40,
            VerticalAlign = verticalAlign,
            Transform = new Transform { X = 10, Y = 60 },
            Style = new Style { Fill = new Fill { Color = "#000000" } }
        });

        using var top = SKBitmap.Decode(_renderer.Render(Create(null), new RenderOptions()).Data)!;
        using var baseline = SKBitmap.Decode(_renderer.Render(Create("baseline"), new RenderOptions()).Data)!;

        Assert.InRange(InkRows(top).Max() - InkRows(baseline).Max(), 39, 41);
    }

    private static List<int> InkColumns(SKBitmap bitmap)
    {
        var columns = new List<int>();
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    columns.Add(x);
                    break;
                }
            }
        }
        return columns;
    }

    private static List<int> InkRows(SKBitmap bitmap)
    {
        var rows = new List<int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    rows.Add(y);
                    break;
                }
            }
        }
        return rows;
    }
    // ---- Helpers ----


    // ---- Fill rule ----

    private const string SameWindingRing = "M0,0 L100,0 L100,100 L0,100 Z M25,25 L75,25 L75,75 L25,75 Z";

    [Fact]
    public void Render_EvenOddPath_LeavesInnerContourTransparent()
    {
        var document = CreateDocumentWith(new PathNode
        {
            D = SameWindingRing,
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = "evenodd" }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.Equal((byte)0, bitmap.GetPixel(50, 50).Alpha);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(10, 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nonzero")]
    public void Render_NonZeroPath_FillsInnerContour(string? fillRule)
    {
        var document = CreateDocumentWith(new PathNode
        {
            D = SameWindingRing,
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = fillRule }
        });

        var result = _renderer.Render(document, new RenderOptions());
        using var bitmap = SKBitmap.Decode(result.Data) ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 50));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(10, 10));
    }

    [Fact]
    public void Render_EvenOddPolygon_LeavesPentagramCentreTransparent()
    {
        var star = new List<Point>
        {
            new() { X = 60, Y = 10 }, new() { X = 89.39, Y = 100.45 }, new() { X = 12.45, Y = 44.55 },
            new() { X = 107.55, Y = 44.55 }, new() { X = 30.61, Y = 100.45 }
        };
        var evenOdd = CreateDocumentWith(new PolygonNode
        {
            Points = star,
            Style = new Style { Fill = new Fill { Color = "#ff0000" }, FillRule = "evenodd" }
        });
        var nonZero = CreateDocumentWith(new PolygonNode
        {
            Points = star,
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });

        using var evenOddBitmap = SKBitmap.Decode(_renderer.Render(evenOdd, new RenderOptions()).Data)
            ?? throw new InvalidOperationException("Failed to decode rendered PNG");
        using var nonZeroBitmap = SKBitmap.Decode(_renderer.Render(nonZero, new RenderOptions()).Data)
            ?? throw new InvalidOperationException("Failed to decode rendered PNG");

        Assert.Equal((byte)0, evenOddBitmap.GetPixel(60, 60).Alpha);
        Assert.Equal(SKColors.Red, nonZeroBitmap.GetPixel(60, 60));
        Assert.Equal(SKColors.Red, evenOddBitmap.GetPixel(60, 20));
        Assert.Equal(SKColors.Red, nonZeroBitmap.GetPixel(60, 20));
    }

    private static Document CreateEmptyDocument(double width = 200, double height = 200) =>
        new()
        {
            Schema = Document.SchemaUrl,
            Id = Guid.NewGuid(),
            Name = "test",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = width, Height = height },
            Root = new FrameNode
            {
                Id = Guid.NewGuid(),
                Name = "root",
                Width = width,
                Height = height,
                Children = []
            }
        };

    private static Document CreateDocumentWithShapes()
    {
        var doc = CreateEmptyDocument();
        doc.Root.Children.Add(new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = "rect",
            Order = "a0",
            Width = 50,
            Height = 50,
            Transform = new Transform { X = 10, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#ff0000" } }
        });
        doc.Root.Children.Add(new EllipseNode
        {
            Id = Guid.NewGuid(),
            Name = "ellipse",
            Order = "a1",
            Width = 40,
            Height = 40,
            Transform = new Transform { X = 80, Y = 10 },
            Style = new Style { Fill = new Fill { Color = "#00ff00" } }
        });
        return doc;
    }

    private static Document CreateDocumentWith(Node node)
    {
        var doc = CreateEmptyDocument();
        node.Order = "a0";
        doc.Root.Children.Add(node);
        return doc;
    }

    private static void AssertPngSignature(byte[] data)
    {
        Assert.True(data.Length >= 8, "PNG must be at least 8 bytes");
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        for (int i = 0; i < pngSignature.Length; i++)
        {
            Assert.Equal(pngSignature[i], data[i]);
        }
    }

    private static void AssertJpegSignature(byte[] data)
    {
        Assert.True(data.Length >= 2, "JPEG must be at least 2 bytes");
        Assert.Equal(0xFF, data[0]);
        Assert.Equal(0xD8, data[1]);
    }

    private static void AssertWebpSignature(byte[] data)
    {
        Assert.True(data.Length >= 12, "WebP must be at least 12 bytes");
        Assert.Equal((byte)'R', data[0]);
        Assert.Equal((byte)'I', data[1]);
        Assert.Equal((byte)'F', data[2]);
        Assert.Equal((byte)'F', data[3]);
        Assert.Equal((byte)'W', data[8]);
        Assert.Equal((byte)'E', data[9]);
        Assert.Equal((byte)'B', data[10]);
        Assert.Equal((byte)'P', data[11]);
    }

    private static bool HasNonTransparentPixel(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha != 0)
                    return true;
            }
        }
        return false;
    }
}
