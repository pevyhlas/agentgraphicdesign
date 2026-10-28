using System.Text;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using FTPlus.AgentGraphicDesign.TestFixtures;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests.TextShaping;

/// <summary>
/// Tests for HarfBuzz text shaping functionality. The default service resolves a
/// missing family to the bundled Inter faces, so most tests are host independent;
/// tests that need a script Inter lacks (Arabic) load pinned Noto fixtures from an explicit font
/// directory the same way a caller would with <c>--font-dir</c>.
/// </summary>
public class TextShapingServiceTests : IDisposable
{
    private readonly TextShapingService _service = new();

    private static TextShapingService CreatePortableService()
    {
        return new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = [PortableTestFonts.VerifiedDirectory()],
            SystemFonts = false,
            StrictFonts = true
        });
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---- Basic shaping (no font dependency) ----

    [Fact]
    public void ShapeText_EmptyString_ReturnsEmptyGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("", options);

        Assert.Empty(result.Glyphs);
        Assert.Equal(0, result.TotalWidth);
    }

    [Fact]
    public void ShapeText_SimpleAscii_ReturnsGlyphsPerCharacter()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("Hello", options);

        Assert.Equal(5, result.Glyphs.Count);
        Assert.Equal(TextShapingBackend.HarfBuzz, result.Backend);
        Assert.True(result.TotalWidth > 0);
        Assert.All(result.Glyphs, glyph => Assert.NotEqual((ushort)0, glyph.GlyphId));
    }

    [Fact]
    public void ShapeText_GlyphsHaveNonNegativePositions()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("ABC", options);

        Assert.Equal(3, result.Glyphs.Count);
        foreach (var glyph in result.Glyphs)
        {
            Assert.True(glyph.X >= 0, $"Glyph X ({glyph.X}) should be >= 0");
        }
    }

    [Fact]
    public void ShapeText_TotalWidthIsNonNegative()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("Test", options);

        Assert.True(result.TotalWidth > 0, "Non-empty text should have a positive advance");
    }

    // ---- Ligatures ----

    [Fact]
    public void ShapeText_ContextualAlternate_ReducesGlyphCount()
    {
        // Bundled Inter ships no fi ligature but its default calt feature draws "->" as one arrow glyph.
        var options = new TextShapingOptions { FontSize = 16 };

        var ligatureResult = _service.ShapeText("->", options);

        Assert.Equal(TextShapingBackend.HarfBuzz, ligatureResult.Backend);
        Assert.Single(ligatureResult.Glyphs);
        Assert.True(ligatureResult.TotalWidth > 0);
    }

    // ---- Diacritics ----

    [Fact]
    public void ShapeText_Diacritics_ProducesGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("café", options);

        Assert.True(result.Glyphs.Count >= 1, "Should produce at least one glyph");
    }

    [Fact]
    public void ShapeText_CombiningCharacters_Shapes()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("é", options);

        Assert.True(result.Glyphs.Count >= 1);
    }

    [Fact]
    public void ShapeText_EmojiSequence_ProducesGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("👋", options);

        Assert.True(result.Glyphs.Count >= 1);
    }

    // ---- Font weights ----

    [Fact]
    public void ShapeText_BoldWeight_ProducesGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16, FontWeight = 700 };

        var result = _service.ShapeText("Bold", options);

        Assert.Equal(4, result.Glyphs.Count);
    }

    [Fact]
    public void ShapeText_DifferentWeights_ProduceSameGlyphCount()
    {
        var normalOptions = new TextShapingOptions { FontSize = 16, FontWeight = 400 };
        var boldOptions = new TextShapingOptions { FontSize = 16, FontWeight = 700 };

        var normalResult = _service.ShapeText("Test", normalOptions);
        var boldResult = _service.ShapeText("Test", boldOptions);

        Assert.Equal(normalResult.Glyphs.Count, boldResult.Glyphs.Count);
    }

    // ---- Path conversion ----

    [Fact]
    public void GetTextAsPath_EmptyText_ReturnsNull()
    {
        var options = new TextShapingOptions { FontSize = 24 };
        var shapedText = _service.ShapeText("", options);

        var pathData = _service.GetTextAsPath(shapedText, options);

        Assert.Null(pathData);
    }

    [Fact]
    public void GetTextAsPath_SimpleText_ReturnsPathOrNull()
    {
        var options = new TextShapingOptions { FontSize = 24 };
        var shapedText = _service.ShapeText("Hi", options);

        var pathData = _service.GetTextAsPath(shapedText, options);

        if (pathData is not null)
        {
            Assert.Contains("M", pathData);
        }
    }

    // ---- Font discovery ----

    [Fact]
    public void GetAvailableFontFamilies_ReturnsCollection()
    {
        var families = TextShapingService.GetAvailableFontFamilies();

        Assert.NotNull(families);
    }

    [Fact]
    public void IsFontFamilyAvailable_BundledFamilyIsNotASystemFont()
    {
        // The bundled family is served from the assembly, never from the host font manager.
        Assert.False(TextShapingService.IsFontFamilyAvailable("Definitely Not Installed Family"));
    }

    // ---- Font resolution policy ----

    [Theory]
    [InlineData(null, 400, 400)]
    [InlineData(null, 599, 400)]
    [InlineData(null, 600, 700)]
    [InlineData(null, 900, 700)]
    [InlineData("Inter", 400, 400)]
    [InlineData("inter", 700, 700)]
    public void ResolveFont_NullOrInterFamily_UsesBundledFaces(string? family, int requested, int expectedWeight)
    {
        var resolution = _service.ResolveFont(family, requested);

        Assert.Equal(FontSource.Bundled, resolution.Source);
        Assert.Equal(BundledFonts.FamilyName, resolution.FamilyName);
        Assert.Equal(expectedWeight, resolution.Weight);
        Assert.False(resolution.Substituted);
        Assert.Empty(_service.Diagnostics);
    }

    [Theory]
    [InlineData(400, 331.09375f)]
    [InlineData(700, 340.92188f)]
    public void ShapeText_BundledInter_KnownStringHasExpectedWidth(int weight, float expectedWidth)
    {
        // Guards the bundled font bytes: a different Inter build or family changes these advances.
        using var service = new TextShapingService(new FontResolutionOptions { SystemFonts = false });

        var result = service.ShapeText("Agent Graphic Design", new TextShapingOptions { FontSize = 32, FontWeight = weight });

        Assert.Equal(TextShapingBackend.HarfBuzz, result.Backend);
        Assert.Equal(20, result.Glyphs.Count);
        Assert.InRange(result.TotalWidth, expectedWidth - 0.5f, expectedWidth + 0.5f);
    }

    [Fact]
    public void ResolveFont_MissingFamily_SubstitutesBundledInterWithDiagnostic()
    {
        using var service = new TextShapingService(new FontResolutionOptions { SystemFonts = false });

        var resolution = service.ResolveFont("Nonexistent Family", 700);
        service.ResolveFont("Nonexistent Family", 400);
        var shaped = service.ShapeText("Hello", new TextShapingOptions { FontFamily = "Nonexistent Family" });

        Assert.Equal(FontSource.Bundled, resolution.Source);
        Assert.Equal(700, resolution.Weight);
        Assert.Equal("Nonexistent Family", resolution.SubstitutedFamily);
        Assert.Equal(TextShapingBackend.HarfBuzz, shaped.Backend);
        var diagnostic = Assert.Single(service.Diagnostics);
        Assert.Equal(RenderDiagnosticCodes.FontSubstituted, diagnostic.Code);
        Assert.Contains("'Nonexistent Family'", diagnostic.Message);
        Assert.Contains("Inter Bold", diagnostic.Message);
    }

    [Fact]
    public void ResolveFont_MissingFamily_StrictFonts_Throws()
    {
        using var service = new TextShapingService(new FontResolutionOptions { SystemFonts = false, StrictFonts = true });

        var exception = Assert.Throws<DocumentEngineException>(() =>
            service.ShapeText("Hello", new TextShapingOptions { FontFamily = "Nonexistent Family" }));

        Assert.Equal(EngineErrors.FontNotFound, exception.Code);
        Assert.Contains("'Nonexistent Family'", exception.Message);
        Assert.Empty(service.Diagnostics);
    }

    [Fact]
    public void ResolveFont_FontDirectory_IsPreferredOverBundledInter()
    {
        using var fonts = new TempFontDirectory();
        fonts.AddBundledBold("custom-bold.ttf");
        using var service = new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = [fonts.Path],
            SystemFonts = false,
            StrictFonts = true
        });
        using var bundledOnly = new TextShapingService(new FontResolutionOptions { SystemFonts = false });

        // The directory holds only the Bold face, so a Regular request still resolves there.
        var resolution = service.ResolveFont("Inter", 400);
        var fromDirectory = service.ShapeText("Agent Graphic Design", new TextShapingOptions { FontFamily = "Inter", FontSize = 32 });
        var fromBundled = bundledOnly.ShapeText("Agent Graphic Design", new TextShapingOptions { FontFamily = "Inter", FontSize = 32 });

        Assert.Equal(FontSource.Directory, resolution.Source);
        Assert.Equal(700, resolution.Weight);
        Assert.False(resolution.Substituted);
        Assert.NotEqual(fromBundled.TotalWidth, fromDirectory.TotalWidth);
        Assert.Empty(service.Diagnostics);
    }

    [Fact]
    public void ResolveFont_FontDirectory_PicksNearestWeightAndUprightFace()
    {
        using var fonts = new TempFontDirectory();
        fonts.AddBundledRegular("a-regular.ttf");
        fonts.AddBundledBold("b-bold.ttf");
        using var service = new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = [fonts.Path],
            SystemFonts = false
        });

        Assert.Equal(400, service.ResolveFont("inter", 300).Weight);
        Assert.Equal(400, service.ResolveFont("Inter", 500).Weight);
        Assert.Equal(700, service.ResolveFont("Inter", 600).Weight);
        Assert.Equal(700, service.ResolveFont("INTER", 900).Weight);
        Assert.All(new[] { 300, 500, 600, 900 }, weight =>
            Assert.Equal(FontSource.Directory, service.ResolveFont("Inter", weight).Source));
    }

    [Fact]
    public void FontDirectory_IgnoresSymlinksNestedDirectoriesAndNonFontFiles()
    {
        using var fonts = new TempFontDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(fonts.Path, "nested")).FullName;
        File.Copy(fonts.AddBundledRegular("regular.ttf"), Path.Combine(nested, "nested-regular.ttf"));
        File.WriteAllText(Path.Combine(fonts.Path, "notes.txt"), "not a font");
        File.CreateSymbolicLink(Path.Combine(fonts.Path, "link.ttf"), Path.Combine(nested, "nested-regular.ttf"));
        using var service = new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = [fonts.Path],
            SystemFonts = false
        });

        var resolution = service.ResolveFont("Inter", 700);

        // Only regular.ttf is indexed: the symlink is skipped and the nested Bold copy is never read.
        Assert.Equal(FontSource.Directory, resolution.Source);
        Assert.Equal(400, resolution.Weight);
        var diagnostic = Assert.Single(service.Diagnostics);
        Assert.Equal(RenderDiagnosticCodes.FontFileSkipped, diagnostic.Code);
        Assert.Contains("link.ttf", diagnostic.Message);
        Assert.Contains("symbolic link", diagnostic.Message);
    }

    [Fact]
    public void FontDirectory_SkipsOversizedAndUnreadableFiles()
    {
        using var fonts = new TempFontDirectory();
        using (var oversized = File.Create(Path.Combine(fonts.Path, "huge.otf")))
        {
            oversized.SetLength(FontResolutionOptions.MaxFontFileBytes + 1);
        }
        File.WriteAllBytes(Path.Combine(fonts.Path, "garbage.ttf"), new byte[64]);
        using var service = new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = [fonts.Path],
            SystemFonts = false
        });

        var resolution = service.ResolveFont("Inter", 400);

        Assert.Equal(FontSource.Bundled, resolution.Source);
        Assert.Equal(2, service.Diagnostics.Count);
        Assert.All(service.Diagnostics, diagnostic => Assert.Equal(RenderDiagnosticCodes.FontFileSkipped, diagnostic.Code));
        Assert.Contains(service.Diagnostics, diagnostic => diagnostic.Message.Contains("huge.otf") && diagnostic.Message.Contains("exceeds"));
        Assert.Contains(service.Diagnostics, diagnostic => diagnostic.Message.Contains("garbage.ttf") && diagnostic.Message.Contains("not a readable"));
    }

    [Fact]
    public void FontDirectory_TooManyFiles_IsRejectedBeforeReading()
    {
        using var fonts = new TempFontDirectory();
        for (var index = 0; index <= FontResolutionOptions.MaxFilesPerDirectory; index++)
        {
            File.WriteAllBytes(Path.Combine(fonts.Path, $"font-{index:D3}.ttf"), []);
        }

        var exception = Assert.Throws<DocumentEngineException>(() => new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = [fonts.Path]
        }));

        Assert.Equal(EngineErrors.ResourceLimit, exception.Code);
    }

    [Fact]
    public void FontDirectory_MissingOrSymlinked_IsRejected()
    {
        using var fonts = new TempFontDirectory();
        var link = Path.Combine(Path.GetTempPath(), $"agd-font-link-{Guid.NewGuid():N}");
        Directory.CreateSymbolicLink(link, fonts.Path);
        try
        {
            Assert.Throws<ArgumentException>(() => new TextShapingService(new FontResolutionOptions
            {
                FontDirectories = [Path.Combine(fonts.Path, "does-not-exist")]
            }));
            Assert.Throws<ArgumentException>(() => new TextShapingService(new FontResolutionOptions
            {
                FontDirectories = [link]
            }));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void TooManyFontDirectories_IsRejected()
    {
        using var fonts = new TempFontDirectory();
        var directories = Enumerable.Repeat(fonts.Path, FontResolutionOptions.MaxFontDirectories + 1).ToArray();

        var exception = Assert.Throws<DocumentEngineException>(() => new TextShapingService(new FontResolutionOptions
        {
            FontDirectories = directories
        }));

        Assert.Equal(EngineErrors.ResourceLimit, exception.Code);
    }

    [Fact]
    public void ShapeText_SubstitutionDiagnostics_AreDedupedPerFamily()
    {
        using var service = new TextShapingService(new FontResolutionOptions { SystemFonts = false });

        service.ShapeText("a", new TextShapingOptions { FontFamily = "Missing One", FontWeight = 400 });
        service.ShapeText("b", new TextShapingOptions { FontFamily = "missing one", FontWeight = 700 });
        service.ShapeText("c", new TextShapingOptions { FontFamily = "Missing Two" });

        Assert.Equal(2, service.Diagnostics.Count);
        Assert.Contains(service.Diagnostics, diagnostic => diagnostic.Message.Contains("'Missing One'"));
        Assert.Contains(service.Diagnostics, diagnostic => diagnostic.Message.Contains("'Missing Two'"));
    }

    /// <summary>Temporary directory seeded with copies of the bundled faces.</summary>
    private sealed class TempFontDirectory : IDisposable
    {
        public TempFontDirectory()
        {
            Path = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agd-fonts-{Guid.NewGuid():N}")).FullName;
        }

        public string Path { get; }

        public string AddBundledRegular(string fileName) => Add(BundledFonts.RegularResourceName, fileName);

        public string AddBundledBold(string fileName) => Add(BundledFonts.BoldResourceName, fileName);

        private string Add(string resourceName, string fileName)
        {
            var target = System.IO.Path.Combine(Path, fileName);
            using var resource = typeof(BundledFonts).Assembly.GetManifestResourceStream(resourceName)!;
            using var file = File.Create(target);
            resource.CopyTo(file);
            return target;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup
            }
        }
    }

    // ---- Determinism ----

    [Fact]
    public void ShapeText_SameInput_ProducesSameOutput()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result1 = _service.ShapeText("Deterministic", options);
        var result2 = _service.ShapeText("Deterministic", options);

        Assert.Equal(result1.Glyphs.Count, result2.Glyphs.Count);
        Assert.Equal(result1.TotalWidth, result2.TotalWidth);

        for (int i = 0; i < result1.Glyphs.Count; i++)
        {
            Assert.Equal(result1.Glyphs[i].GlyphId, result2.Glyphs[i].GlyphId);
            Assert.Equal(result1.Glyphs[i].X, result2.Glyphs[i].X);
        }
    }

    // ---- Non-Latin scripts ----

    [Fact]
    public void ShapeText_CyrillicText_ProducesGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("Привет", options);

        Assert.True(result.Glyphs.Count >= 1);
    }

    [Fact]
    public void ShapeText_GreekText_ProducesGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("Γειά", options);

        Assert.True(result.Glyphs.Count >= 1);
    }

    [Fact]
    public void ShapeText_ChineseText_ProducesGlyphs()
    {
        var options = new TextShapingOptions { FontSize = 16 };

        var result = _service.ShapeText("你好", options);

        Assert.True(result.Glyphs.Count >= 1);
    }

    // ---- Disposal ----

    [Fact]
    public void ShapeText_LetterSpacing_ShiftsGlyphsAndWidensTotal()
    {
        var plain = _service.ShapeText("HHHH", new TextShapingOptions { FontSize = 20 });
        var spaced = _service.ShapeText("HHHH", new TextShapingOptions { FontSize = 20, LetterSpacing = 3 });

        Assert.Equal(plain.Glyphs.Count, spaced.Glyphs.Count);
        Assert.Equal(plain.TotalWidth + 3 * plain.Glyphs.Count, spaced.TotalWidth, 3);
        for (var index = 0; index < plain.Glyphs.Count; index++)
            Assert.Equal(plain.Glyphs[index].X + 3 * index, spaced.Glyphs[index].X, 3);
    }

    [Fact]
    public void ShapeText_ZeroLetterSpacing_IsIdenticalToDefault()
    {
        var plain = _service.ShapeText("Hello", new TextShapingOptions { FontSize = 20 });
        var zero = _service.ShapeText("Hello", new TextShapingOptions { FontSize = 20, LetterSpacing = 0 });

        Assert.Equal(plain.TotalWidth, zero.TotalWidth);
        Assert.Equal(plain.Glyphs, zero.Glyphs);
    }

    [Fact]
    public void ShapeText_NonFiniteLetterSpacing_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.ShapeText("x", new TextShapingOptions { FontSize = 20, LetterSpacing = double.NaN }));
    }

    [Fact]
    public void ShapeText_AfterDispose_Throws()
    {
        var service = new TextShapingService();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            service.ShapeText("Test", new TextShapingOptions { FontSize = 16 }));
    }

    [Fact]
    public void Dispose_MultipleTimes_NoError()
    {
        var service = new TextShapingService();

        service.Dispose();
        service.Dispose();
    }

    // ---- Options ----

    [Fact]
    public void TextShapingOptions_DefaultValues()
    {
        var options = new TextShapingOptions();

        Assert.Null(options.FontFamily);
        Assert.Equal(16, options.FontSize);
        Assert.Equal(400, options.FontWeight);
        Assert.Null(options.Language);
        Assert.Equal(TextDirection.LeftToRight, options.Direction);
    }

    [Fact]
    public void ShapedGlyph_PropertiesSet()
    {
        var glyph = new ShapedGlyph(
            GlyphId: 65,
            Cluster: 0,
            X: 10.5f,
            Y: 0,
            XAdvance: 12.0f,
            YAdvance: 0
        );

        Assert.Equal(65, glyph.GlyphId);
        Assert.Equal((uint)0, glyph.Cluster);
        Assert.Equal(10.5f, glyph.X);
        Assert.Equal(12.0f, glyph.XAdvance);
    }

    [Fact]
    public void ShapeText_PreservesUtf8ClusterOffsets_NotGlyphIndexes()
    {
        var result = _service.ShapeText("éa", new TextShapingOptions
        {
            FontSize = 20,
            Language = "fr"
        });

        Assert.NotEmpty(result.Glyphs);
        Assert.Contains(result.Glyphs, glyph => glyph.Cluster == 2);
        Assert.DoesNotContain(result.Glyphs, glyph => glyph.Cluster == 1);
    }

    [Fact]
    public void ShapeText_RightToLeft_HonorsVisualDirectionAndClusters()
    {
        var leftToRight = _service.ShapeText("abc", new TextShapingOptions
        {
            Direction = TextDirection.LeftToRight,
            Language = "en"
        });
        var rightToLeft = _service.ShapeText("abc", new TextShapingOptions
        {
            Direction = TextDirection.RightToLeft,
            Language = "ar"
        });

        Assert.Equal(new uint[] { 0, 1, 2 }, leftToRight.Glyphs.Select(glyph => glyph.Cluster));
        Assert.Equal(new uint[] { 2, 1, 0 }, rightToLeft.Glyphs.Select(glyph => glyph.Cluster));
        Assert.Equal(leftToRight.Glyphs.Select(glyph => glyph.GlyphId).Reverse(), rightToLeft.Glyphs.Select(glyph => glyph.GlyphId));
    }

    [Fact]
    public void ShapeText_ArabicRtl_ProducesDescendingUtf8Clusters()
    {
        using var service = CreatePortableService();
        var result = service.ShapeText("سلام", new TextShapingOptions
        {
            FontFamily = PortableTestFonts.ArabicFamily,
            Direction = TextDirection.RightToLeft,
            Language = "ar"
        });

        Assert.NotEmpty(result.Glyphs);
        Assert.Equal(TextShapingBackend.HarfBuzz, result.Backend);
        Assert.Equal(
            result.Glyphs.Select(glyph => glyph.Cluster).OrderByDescending(cluster => cluster),
            result.Glyphs.Select(glyph => glyph.Cluster));
        Assert.Contains(result.Glyphs, glyph => glyph.Cluster >= 2);
        Assert.True(result.Glyphs.Count < "سلام".EnumerateRunes().Count(), "Arabic shaping should form a lam-alef ligature");
        Assert.All(result.Glyphs, glyph => Assert.NotEqual((ushort)0, glyph.GlyphId));
        Assert.True(result.TotalWidth > 0);
    }

    [Fact]
    public async Task ShapeText_ConcurrentCalls_OnSharedService_AreSafeAndSemantic()
    {
        using var service = CreatePortableService();
        var tasks = Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            service.ShapeText(index % 2 == 0 ? "office" : "سلام", new TextShapingOptions
            {
                FontFamily = index % 2 == 0 ? PortableTestFonts.SansFamily : PortableTestFonts.ArabicFamily,
                Direction = index % 2 == 0 ? TextDirection.LeftToRight : TextDirection.RightToLeft,
                Language = index % 2 == 0 ? "en" : "ar"
            }))).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result =>
        {
            Assert.Equal(TextShapingBackend.HarfBuzz, result.Backend);
            Assert.True(result.TotalWidth > 0);
            Assert.All(result.Glyphs, glyph => Assert.NotEqual((ushort)0, glyph.GlyphId));
        });
    }

    [Fact]
    public void ShapeText_PreCancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _service.ShapeText(
            "text",
            new TextShapingOptions(),
            cancellation.Token));
    }

    [Fact]
    public void ShapeText_OverTextBudget_ThrowsBeforeShaping()
    {
        var text = new string('a', TextShapingService.MaxTextCharacters + 1);

        Assert.Throws<ArgumentException>(() => _service.ShapeText(text, new TextShapingOptions()));
    }

    [Fact]
    public void ShapeText_InvalidDirectionAndLanguage_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ShapeText("text", new TextShapingOptions
        {
            Direction = (TextDirection)999
        }));
        Assert.Throws<ArgumentException>(() => _service.ShapeText("text", new TextShapingOptions
        {
            Language = "en\0evil"
        }));
    }
}
