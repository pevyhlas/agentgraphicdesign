using System.Globalization;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Tokens;

public class TokenServiceTests
{
    private readonly TokenService _service = new();

    private Document CreateDocument() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Revn = 1
    };

    [Fact]
    public void SetColorToken_AddsToken()
    {
        var doc = CreateDocument();

        _service.SetColorToken(doc, "primary", "#0066cc");

        Assert.NotNull(doc.Tokens?.Colors);
        Assert.Equal("#0066cc", doc.Tokens.Colors["primary"]);
        Assert.Equal(2, doc.Revn);
    }

    [Fact]
    public void SetColorToken_InvalidLiteral_DoesNotMutate()
    {
        var doc = CreateDocument();

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _service.SetColorToken(doc, "primary", "rgb(999, 0, 0)"));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Null(doc.Tokens);
        Assert.Equal(1, doc.Revn);
    }

    [Fact]
    public void SetTypographyToken_OversizedFamily_DoesNotMutate()
    {
        var doc = CreateDocument();

        Assert.Throws<DocumentEngineException>(() => _service.SetTypographyToken(
            doc,
            "body",
            new TypographyToken { FontFamily = new string('f', DocumentLimits.MaxFontFamilyLength + 1) }));

        Assert.Null(doc.Tokens);
        Assert.Equal(1, doc.Revn);
    }

    [Fact]
    public void TokenMutation_FutureDocumentVersion_IsRejected()
    {
        var doc = CreateDocument();
        doc.Version = Document.SupportedVersion + 1;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _service.SetColorToken(doc, "primary", "#0066cc"));

        Assert.Equal(EngineErrors.UnsupportedVersion, exception.Code);
        Assert.Null(doc.Tokens);
    }

    [Fact]
    public void SetColorToken_UpdatesExisting()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "primary", "#0066cc");

        _service.SetColorToken(doc, "primary", "#ff0000");

        Assert.Equal("#ff0000", doc.Tokens!.Colors!["primary"]);
    }

    [Fact]
    public void RemoveColorToken_RemovesExisting()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "primary", "#0066cc");

        var removed = _service.RemoveColorToken(doc, "primary");

        Assert.True(removed);
        Assert.DoesNotContain("primary", doc.Tokens!.Colors!.Keys);
    }

    [Fact]
    public void RemoveColorToken_ReturnsFalse_WhenNotFound()
    {
        var doc = CreateDocument();

        var removed = _service.RemoveColorToken(doc, "nonexistent");

        Assert.False(removed);
    }

    [Fact]
    public void GetColorTokens_ReturnsEmpty_WhenNoTokens()
    {
        var doc = CreateDocument();

        var colors = _service.GetColorTokens(doc);

        Assert.Empty(colors);
    }

    [Fact]
    public void GetColorTokens_ReturnsAllTokens()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "primary", "#0066cc");
        _service.SetColorToken(doc, "secondary", "#ff0000");

        var colors = _service.GetColorTokens(doc);

        Assert.Equal(2, colors.Count);
        Assert.Equal("#0066cc", colors["primary"]);
        Assert.Equal("#ff0000", colors["secondary"]);
    }

    [Fact]
    public void SetTypographyToken_AddsToken()
    {
        var doc = CreateDocument();
        var token = new TypographyToken
        {
            FontFamily = "Inter",
            FontSize = 16,
            FontWeight = 400,
            LineHeight = 1.5
        };

        _service.SetTypographyToken(doc, "body", token);

        Assert.NotNull(doc.Tokens?.Typography);
        var stored = doc.Tokens.Typography["body"];
        Assert.Equal("Inter", stored.FontFamily);
        Assert.Equal(16, stored.FontSize);
    }

    [Fact]
    public void ApplyPalette_AddsTailwindColors()
    {
        var doc = CreateDocument();

        var result = _service.ApplyPalette(doc, "tailwind");

        Assert.True(result.Success);
        Assert.Equal("tailwind", result.PaletteName);
        Assert.True(result.ColorCount > 0);
        Assert.Contains("blue-500", doc.Tokens!.Colors!.Keys);
        Assert.Equal("#3b82f6", doc.Tokens.Colors["blue-500"]);
    }

    [Fact]
    public void ApplyPalette_AddsMaterialColors()
    {
        var doc = CreateDocument();

        var result = _service.ApplyPalette(doc, "material");

        Assert.True(result.Success);
        Assert.Equal("material", result.PaletteName);
        Assert.Contains("blue-500", doc.Tokens!.Colors!.Keys);
        Assert.Equal("#2196f3", doc.Tokens.Colors["blue-500"]);
    }

    [Fact]
    public void ApplyPalette_AddsColorBrewerColors()
    {
        var doc = CreateDocument();

        var result = _service.ApplyPalette(doc, "colorbrewer-qualitative");

        Assert.True(result.Success);
        Assert.Contains("set3-1", doc.Tokens!.Colors!.Keys);
    }

    [Fact]
    public void ApplyPalette_ReturnsError_WhenPaletteNotFound()
    {
        var doc = CreateDocument();

        var result = _service.ApplyPalette(doc, "nonexistent");

        Assert.False(result.Success);
        Assert.Equal("PALETTE_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public void ListPalettes_ReturnsAllPalettes()
    {
        var palettes = _service.ListPalettes();

        Assert.Equal(3, palettes.Count);
        Assert.Contains(palettes, p => p.Name == "tailwind");
        Assert.Contains(palettes, p => p.Name == "material");
        Assert.Contains(palettes, p => p.Name == "colorbrewer-qualitative");
    }

    [Fact]
    public void ImportDtcg_ImportsColors()
    {
        var doc = CreateDocument();
        var json = """
            {
                "color": {
                    "primary": {
                        "$value": "#0066cc",
                        "$type": "color"
                    },
                    "secondary": {
                        "$value": "#ff0000",
                        "$type": "color"
                    }
                }
            }
            """;

        var result = _service.ImportDtcg(doc, json);

        Assert.True(result.Success);
        Assert.Equal(2, result.ColorCount);
        Assert.Equal("#0066cc", doc.Tokens!.Colors!["primary"]);
        Assert.Equal("#ff0000", doc.Tokens.Colors["secondary"]);
    }

    [Fact]
    public void ImportDtcg_ImportsTypography()
    {
        var doc = CreateDocument();
        var json = """
            {
                "typography": {
                    "body": {
                        "$value": {
                            "fontFamily": "Inter",
                            "fontSize": "16px",
                            "fontWeight": 400,
                            "lineHeight": 1.5
                        },
                        "$type": "typography"
                    }
                }
            }
            """;

        var result = _service.ImportDtcg(doc, json);

        Assert.True(result.Success);
        Assert.Equal(1, result.TypographyCount);
        var token = doc.Tokens!.Typography!["body"];
        Assert.Equal("Inter", token.FontFamily);
        Assert.Equal(16, token.FontSize);
    }

    [Fact]
    public void ImportDtcg_ReturnsError_OnInvalidJson()
    {
        var doc = CreateDocument();

        var result = _service.ImportDtcg(doc, "not valid json");

        Assert.False(result.Success);
        Assert.Equal("INVALID_JSON", result.ErrorCode);
    }

    [Fact]
    public void ExportDtcg_ExportsColors()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "primary", "#0066cc");

        var result = _service.ExportDtcg(doc);

        Assert.True(result.Success);
        Assert.NotNull(result.Content);
        Assert.Contains("\"$value\": \"#0066cc\"", result.Content);
        Assert.Contains("\"$type\": \"color\"", result.Content);
    }

    [Fact]
    public void ExportDtcg_ExportsTypography()
    {
        var doc = CreateDocument();
        _service.SetTypographyToken(doc, "body", new TypographyToken
        {
            FontFamily = "Inter",
            FontSize = 16
        });

        var result = _service.ExportDtcg(doc);

        Assert.True(result.Success);
        Assert.NotNull(result.Content);
        Assert.Contains("\"$type\": \"typography\"", result.Content);
        Assert.Contains("\"fontFamily\": \"Inter\"", result.Content);
    }

    [Fact]
    public void ResolveColorRef_ResolvesTokenRef()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "primary", "#0066cc");

        var resolved = _service.ResolveColorRef(doc, "{primary}");

        Assert.Equal("#0066cc", resolved);
    }

    [Fact]
    public void ResolveColorRef_ResolvesDirectName()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "primary", "#0066cc");

        var resolved = _service.ResolveColorRef(doc, "primary");

        Assert.Equal("#0066cc", resolved);
    }

    [Fact]
    public void ResolveColorRef_ReturnsOriginal_WhenNotFound()
    {
        var doc = CreateDocument();

        var resolved = _service.ResolveColorRef(doc, "{nonexistent}");

        Assert.Equal("{nonexistent}", resolved);
    }

    [Fact]
    public void ResolveColorRef_PassesThrough_HexColors()
    {
        var doc = CreateDocument();

        var resolved = _service.ResolveColorRef(doc, "#ff0000");

        Assert.Equal("#ff0000", resolved);
    }

    [Fact]
    public void ResolveNodeTokenRefs_AppliesTokenLineHeightOnlyWhenNodeHasNone()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Name = "typography",
            Version = 1,
            Revn = 1,
            Canvas = new Canvas { Width = 100, Height = 100 },
            Root = new FrameNode { Id = Guid.NewGuid(), Width = 100, Height = 100, Children = [] },
            Tokens = new DesignTokens
            {
                Typography = new Dictionary<string, TypographyToken> { ["body"] = new() { FontSize = 14, LineHeight = 1.6 } }
            }
        };
        var inherits = new TextNode { Id = Guid.NewGuid(), Text = "a", TypographyTokenRef = "body" };
        var overrides = new TextNode { Id = Guid.NewGuid(), Text = "b", TypographyTokenRef = "body", LineHeight = 1.1 };

        _service.ResolveNodeTokenRefs(doc, inherits);
        _service.ResolveNodeTokenRefs(doc, overrides);

        Assert.Equal(14, inherits.FontSize);
        Assert.Equal(1.6, inherits.LineHeight);
        Assert.Equal(1.1, overrides.LineHeight);
    }

    [Fact]
    public void ResolveNodeTokenRefs_ResolvesFillTokenRef()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "brand-red", "#dc2626");
        var node = new RectangleNode
        {
            Style = new Style
            {
                Fill = new Fill { TokenRef = "{brand-red}" }
            }
        };

        _service.ResolveNodeTokenRefs(doc, node);

        Assert.Equal("#dc2626", node.Style.Fill.Color);
    }

    [Fact]
    public void ResolveNodeTokenRefs_ResolvesStrokeTokenRef()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "border", "#e5e7eb");
        var node = new RectangleNode
        {
            Style = new Style
            {
                Stroke = new Stroke { TokenRef = "{border}" }
            }
        };

        _service.ResolveNodeTokenRefs(doc, node);

        Assert.Equal("#e5e7eb", node.Style.Stroke.Color);
    }

    [Fact]
    public void ResolveAllTokenRefs_CreatesResolvedCopy()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "fill-color", "#0066cc");
        doc.Root = new FrameNode
        {
            Children =
            [
                new RectangleNode
                {
                    Style = new Style
                    {
                        Fill = new Fill { TokenRef = "{fill-color}" }
                    }
                }
            ]
        };

        var resolved = _service.ResolveAllTokenRefs(doc);

        var rect = resolved.Root.Children[0] as RectangleNode;
        Assert.Equal("#0066cc", rect!.Style!.Fill!.Color);
    }

    [Fact]
    public void BundledPalettes_Tailwind_HasLicenseInfo()
    {
        var palette = BundledPalettes.Tailwind;

        Assert.Equal("MIT", palette.License);
        Assert.Contains("Tailwind", palette.Attribution);
    }

    [Fact]
    public void BundledPalettes_Material_HasLicenseInfo()
    {
        var palette = BundledPalettes.Material;

        Assert.Equal("Apache-2.0", palette.License);
        Assert.Contains("Google", palette.Attribution);
    }

    [Fact]
    public void BundledPalettes_ColorBrewer_HasLicenseInfo()
    {
        var palette = BundledPalettes.ColorBrewerQualitative;

        Assert.Equal("Apache-2.0", palette.License);
        Assert.Contains("Brewer", palette.Attribution);
    }

    [Fact]
    public void ImportDtcg_MalformedLateToken_IsTransactional()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "existing", "#123456");
        var revision = doc.Revn;
        const string json = """
            {
              "color": {
                "good": { "$type": "color", "$value": "#ffffff" },
                "bad": { "$type": "color", "$value": 42 }
              }
            }
            """;

        var result = _service.ImportDtcg(doc, json);

        Assert.False(result.Success);
        Assert.Equal("INVALID_TOKEN_VALUE", result.ErrorCode);
        Assert.Equal(revision, doc.Revn);
        Assert.Equal(new[] { "existing" }, doc.Tokens!.Colors!.Keys);
    }

    [Fact]
    public void ImportDtcg_DuplicateJsonProperty_ReturnsStructuredFailure()
    {
        var doc = CreateDocument();

        var result = _service.ImportDtcg(doc, """
            {
              "primary": {
                "$type": "color",
                "$value": "#ffffff",
                "$value": "#000000"
              }
            }
            """);

        Assert.False(result.Success);
        Assert.Equal("INVALID_JSON", result.ErrorCode);
        Assert.Contains("Duplicate JSON property", result.ErrorMessage);
        Assert.Null(doc.Tokens);
    }

    [Fact]
    public void ImportDtcg_SameValues_BumpsRevisionExactlyOnceOnlyOnChange()
    {
        var doc = CreateDocument();
        const string json = """
            { "colors": { "$type": "color", "primary": { "$value": "#0066cc" } } }
            """;

        var first = _service.ImportDtcg(doc, json);
        var revisionAfterFirst = doc.Revn;
        var second = _service.ImportDtcg(doc, json);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(2, revisionAfterFirst);
        Assert.Equal(revisionAfterFirst, doc.Revn);
        Assert.Equal("#0066cc", doc.Tokens!.Colors!["colors.primary"]);
    }

    [Fact]
    public void ImportDtcg_DimensionsUseInvariantCulture()
    {
        var doc = CreateDocument();
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = _service.ImportDtcg(doc, """
                {
                  "body": {
                    "$type": "typography",
                    "$value": { "fontFamily": "Inter", "fontSize": "16.5px", "lineHeight": 1.25 }
                  }
                }
                """);

            Assert.True(result.Success);
            Assert.Equal(16.5, doc.Tokens!.Typography!["body"].FontSize);
            Assert.Equal(1.25, doc.Tokens.Typography["body"].LineHeight);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    [Fact]
    public void ImportDtcg_MalformedTypography_ReturnsStructuredFailureWithoutMutation()
    {
        var doc = CreateDocument();
        var result = _service.ImportDtcg(doc, """
            {
              "body": {
                "$type": "typography",
                "$value": { "fontSize": "large-ish", "fontWeight": 5000 }
              }
            }
            """);

        Assert.False(result.Success);
        Assert.Equal("INVALID_TOKEN_VALUE", result.ErrorCode);
        Assert.Null(doc.Tokens);
        Assert.Equal(1, doc.Revn);
    }

    [Fact]
    public void ResolveColorRef_RecursivelyResolvesAliases()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "base", "#112233");
        _service.SetColorToken(doc, "semantic", "{base}");
        _service.SetColorToken(doc, "component", "{semantic}");

        var result = _service.ResolveColorRefWithDiagnostics(doc, "{component}");

        Assert.True(result.Success);
        Assert.Equal("#112233", result.Value);
    }

    [Fact]
    public void ResolveColorRef_RecursivelyResolvesBareAliases()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "base", "#112233");
        _service.SetColorToken(doc, "semantic", "base");
        _service.SetColorToken(doc, "component", "semantic");

        var result = _service.ResolveColorRefWithDiagnostics(doc, "{component}");

        Assert.True(result.Success);
        Assert.Equal("#112233", result.Value);
    }

    [Fact]
    public void ResolveColorRef_BareAliasCycleAndMissingReturnDiagnostics()
    {
        var cycleDocument = CreateDocument();
        _service.SetColorToken(cycleDocument, "base", "#112233");
        _service.SetColorToken(cycleDocument, "semantic", "base");
        _service.SetColorToken(cycleDocument, "base", "semantic");

        var missingDocument = CreateDocument();
        _service.SetColorToken(missingDocument, "base", "#112233");
        _service.SetColorToken(missingDocument, "semantic", "base");
        _service.RemoveColorToken(missingDocument, "base");

        var cycle = _service.ResolveColorRefWithDiagnostics(cycleDocument, "semantic");
        var missing = _service.ResolveColorRefWithDiagnostics(missingDocument, "semantic");

        Assert.False(cycle.Success);
        Assert.Equal("TOKEN_ALIAS_CYCLE", cycle.ErrorCode);
        Assert.False(missing.Success);
        Assert.Equal("TOKEN_NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public void ResolveColorRef_CycleAndMissingReturnDiagnostics()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "a", "{b}");
        _service.SetColorToken(doc, "b", "{a}");

        var cycle = _service.ResolveColorRefWithDiagnostics(doc, "{a}");
        var missing = _service.ResolveColorRefWithDiagnostics(doc, "{missing}");

        Assert.False(cycle.Success);
        Assert.Equal("TOKEN_ALIAS_CYCLE", cycle.ErrorCode);
        Assert.False(missing.Success);
        Assert.Equal("TOKEN_NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public void ResolveAllTokenRefs_ResolvesTypographyWithoutMutatingSource()
    {
        var doc = CreateDocument();
        _service.SetTypographyToken(doc, "heading", new TypographyToken
        {
            FontFamily = "Inter",
            FontSize = 32,
            FontWeight = 700
        });
        doc.Root.Children.Add(new TextNode
        {
            Text = "Title",
            FontFamily = "serif",
            FontSize = 12,
            FontWeight = 400,
            TypographyTokenRef = "{heading}"
        });

        var resolved = _service.ResolveAllTokenRefs(doc);
        var sourceText = Assert.IsType<TextNode>(doc.Root.Children.Single());
        var resolvedText = Assert.IsType<TextNode>(resolved.Root.Children.Single());

        Assert.Equal("serif", sourceText.FontFamily);
        Assert.Equal(12, sourceText.FontSize);
        Assert.Equal("Inter", resolvedText.FontFamily);
        Assert.Equal(32, resolvedText.FontSize);
        Assert.Equal(700, resolvedText.FontWeight);
    }

    [Fact]
    public void ExportDtcg_IsDeterministicAcrossInsertionOrder()
    {
        var first = CreateDocument();
        var second = CreateDocument();
        _service.SetColorToken(first, "z", "#000000");
        _service.SetColorToken(first, "a", "#ffffff");
        _service.SetColorToken(second, "a", "#ffffff");
        _service.SetColorToken(second, "z", "#000000");

        Assert.Equal(_service.ExportDtcg(first).Content, _service.ExportDtcg(second).Content);
    }

    [Fact]
    public void ExportDtcg_WithoutResolutionCanonicalizesBareAndBracedAliases()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "base", "#112233");
        _service.SetColorToken(doc, "bare", "base");
        _service.SetColorToken(doc, "braced", "{base}");

        var result = _service.ExportDtcg(doc);

        Assert.True(result.Success);
        using var json = JsonDocument.Parse(result.Content!);
        var colors = json.RootElement.GetProperty("color");
        Assert.Equal("{color.base}", colors.GetProperty("bare").GetProperty("$value").GetString());
        Assert.Equal("{color.base}", colors.GetProperty("braced").GetProperty("$value").GetString());
    }

    [Fact]
    public void ImportDtcg_ResolvesRecursiveTypographyAliasesTransactionally()
    {
        var doc = CreateDocument();
        var result = _service.ImportDtcg(doc, """
            {
              "typography": {
                "$type": "typography",
                "base": { "$value": { "fontFamily": "Inter", "fontSize": "18px", "fontWeight": 600 } },
                "semantic": { "$value": "{typography.base}" },
                "component": { "$value": "{typography.semantic}" }
              }
            }
            """);

        Assert.True(result.Success);
        Assert.Equal(3, result.TypographyCount);
        var component = doc.Tokens!.Typography!["component"];
        Assert.Equal("Inter", component.FontFamily);
        Assert.Equal(18, component.FontSize);
        Assert.Equal(600, component.FontWeight);
    }

    [Fact]
    public void ImportDtcg_TypographyAliasCycle_ReturnsDiagnosticWithoutMutation()
    {
        var doc = CreateDocument();
        var result = _service.ImportDtcg(doc, """
            {
              "typography": {
                "$type": "typography",
                "a": { "$value": "{typography.b}" },
                "b": { "$value": "{typography.a}" }
              }
            }
            """);

        Assert.False(result.Success);
        Assert.Equal("TOKEN_ALIAS_CYCLE", result.ErrorCode);
        Assert.Null(doc.Tokens);
        Assert.Equal(1, doc.Revn);
    }

    [Fact]
    public void ImportDtcg_ColorAliasCycle_IsRejectedTransactionally()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "existing", "#123456");
        var sourceBefore = FTPlus.AgentGraphicDesign.Core.Serialization.DocumentSerializer.Serialize(doc);
        var result = _service.ImportDtcg(doc, """
            {
              "color": {
                "$type": "color",
                "a": { "$value": "{color.b}" },
                "b": { "$value": "{color.a}" }
              }
            }
            """);

        Assert.False(result.Success);
        Assert.Equal("TOKEN_ALIAS_CYCLE", result.ErrorCode);
        Assert.Equal(sourceBefore, FTPlus.AgentGraphicDesign.Core.Serialization.DocumentSerializer.Serialize(doc));
    }

    [Fact]
    public void ImportDtcg_MissingColorAlias_IsRejectedTransactionally()
    {
        var doc = CreateDocument();
        var result = _service.ImportDtcg(doc, """
            { "color": { "$type": "color", "a": { "$value": "{color.missing}" } } }
            """);

        Assert.False(result.Success);
        Assert.Equal("TOKEN_NOT_FOUND", result.ErrorCode);
        Assert.Null(doc.Tokens);
        Assert.Equal(1, doc.Revn);
    }

    [Fact]
    public void ImportExportDtcg_RoundTripsCanonicalPathsAndAliases()
    {
        var first = CreateDocument();
        var imported = _service.ImportDtcg(first, """
            {
              "color": {
                "$type": "color",
                "brand": {
                  "base": { "$value": "#123456" },
                  "semantic": { "$value": "{color.brand.base}" }
                }
              },
              "typography": {
                "$type": "typography",
                "heading": { "$value": { "fontFamily": "Inter", "fontSize": "24px" } }
              }
            }
            """);

        Assert.True(imported.Success);
        Assert.Equal("{brand.base}", first.Tokens!.Colors!["brand.semantic"]);
        var exported = _service.ExportDtcg(first);
        Assert.True(exported.Success);

        var second = CreateDocument();
        var reimported = _service.ImportDtcg(second, exported.Content!);

        Assert.True(reimported.Success);
        Assert.Equal(
            first.Tokens.Colors.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            second.Tokens!.Colors!.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(first.Tokens.Typography!.Keys, second.Tokens!.Typography!.Keys);
        Assert.Equal(24, second.Tokens.Typography["heading"].FontSize);
    }

    [Fact]
    public void ExportDtcg_ResolveReferencesFailsOnCycleWithoutMutation()
    {
        var doc = CreateDocument();
        _service.SetColorToken(doc, "a", "{b}");
        _service.SetColorToken(doc, "b", "{a}");
        var sourceBefore = FTPlus.AgentGraphicDesign.Core.Serialization.DocumentSerializer.Serialize(doc);

        var result = _service.ExportDtcg(doc, resolveReferences: true);

        Assert.False(result.Success);
        Assert.Equal("TOKEN_ALIAS_CYCLE", result.ErrorCode);
        Assert.Equal(sourceBefore, FTPlus.AgentGraphicDesign.Core.Serialization.DocumentSerializer.Serialize(doc));
    }

    [Fact]
    public void DtcgTokenFileParse_RejectsNullRoot()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => DtcgTokenFile.Parse("null"));
    }

    [Fact]
    public void ImportDtcg_PreCancelled_DoesNotMutateSource()
    {
        var doc = CreateDocument();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _service.ImportDtcg(doc, "{}", cancellation.Token));
        Assert.Null(doc.Tokens);
        Assert.Equal(1, doc.Revn);
    }
}
