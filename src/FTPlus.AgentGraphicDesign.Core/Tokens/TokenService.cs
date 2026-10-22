using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Tokens;

/// <summary>
/// Design token CRUD, transactional DTCG import/export, and non-mutating
/// reference resolution support for output adapters.
/// </summary>
public sealed partial class TokenService
{
    public const int MaxDtcgInputCharacters = 4_000_000;
    public const int MaxDtcgDepth = 64;
    public const int MaxDtcgTokenCount = DocumentLimits.MaxDesignTokensPerCategory;
    public const int MaxDtcgNameCharacters = 512;
    public const int MaxDtcgOutputCharacters = 8_000_000;

    private static readonly JsonSerializerOptions DtcgSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [GeneratedRegex(@"^\{([a-zA-Z0-9_.-]+)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRefPattern();

    [GeneratedRegex(@"^[+-]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][+-]?\d+)?(?:px|pt)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimensionPattern();

    public void SetColorToken(Document document, string name, string color)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(color);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            ValidateTokenName(name);
            var isReference = DocumentLimits.TryGetTokenReference(color, out _) ||
                DocumentLimits.IsValidTokenName(color) && document.Tokens?.Colors?.ContainsKey(color) == true;
            if (!DocumentLimits.IsValidColor(color) && !isReference)
                throw InvalidProperty("Color token value must be a strict color or token reference");

            if (document.Tokens?.Colors?.TryGetValue(name, out var existing) == true && existing == color)
                return;

            EnsureTokensInitialized(document);
            document.Tokens!.Colors ??= [];
            document.Tokens.Colors[name] = color;
            DocumentSynchronization.IncrementRevision(document);
        }
    }

    public bool RemoveColorToken(Document document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            ValidateTokenName(name);
            if (document.Tokens?.Colors is null || !document.Tokens.Colors.Remove(name))
                return false;
            DocumentSynchronization.IncrementRevision(document);
            return true;
        }
    }

    public void SetTypographyToken(Document document, string name, TypographyToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(token);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            ValidateTokenName(name);
            ValidateTypographyToken(token);
            if (document.Tokens?.Typography?.TryGetValue(name, out var existing) == true && TypographyEquals(existing, token))
                return;

            EnsureTokensInitialized(document);
            document.Tokens!.Typography ??= [];
            document.Tokens.Typography[name] = token;
            DocumentSynchronization.IncrementRevision(document);
        }
    }

    public bool RemoveTypographyToken(Document document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            ValidateTokenName(name);
            if (document.Tokens?.Typography is null || !document.Tokens.Typography.Remove(name))
                return false;
            DocumentSynchronization.IncrementRevision(document);
            return true;
        }
    }

    public IReadOnlyDictionary<string, string> GetColorTokens(Document document) =>
        document.Tokens?.Colors ?? new Dictionary<string, string>();

    public IReadOnlyDictionary<string, TypographyToken> GetTypographyTokens(Document document) =>
        document.Tokens?.Typography ?? new Dictionary<string, TypographyToken>();

    public PaletteApplyResult ApplyPalette(Document document, string paletteName)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            if (!BundledPalettes.All.TryGetValue(paletteName.ToLowerInvariant(), out var palette))
            {
                return new PaletteApplyResult(
                    false,
                    paletteName,
                    0,
                    "PALETTE_NOT_FOUND",
                    $"Unknown palette: {paletteName}. Available: {string.Join(", ", BundledPalettes.All.Keys)}");
            }

            var changed = palette.Colors.Any(pair =>
                document.Tokens?.Colors?.TryGetValue(pair.Key, out var existing) != true || existing != pair.Value);
            if (changed)
            {
                EnsureTokensInitialized(document);
                document.Tokens!.Colors ??= [];
                foreach (var (name, color) in palette.Colors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    document.Tokens.Colors[name] = color;
                DocumentSynchronization.IncrementRevision(document);
            }

            return new PaletteApplyResult(true, palette.Name, palette.Colors.Count);
        }
    }

    public IReadOnlyList<BundledPaletteInfo> ListPalettes() =>
        BundledPalettes.All.Values
            .OrderBy(palette => palette.Name, StringComparer.Ordinal)
            .Select(palette => new BundledPaletteInfo(
                palette.Name,
                palette.Description,
                palette.License,
                palette.Colors.Count))
            .ToList();

    /// <summary>
    /// Validates all supported tokens before applying any changes. A successful
    /// import increments the document revision once only when values changed.
    /// </summary>
    public TokenImportResult ImportDtcg(Document document, string json) =>
        ImportDtcg(document, json, CancellationToken.None);

    public TokenImportResult ImportDtcg(
        Document document,
        string json,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (json is null)
            return ImportFailure("INVALID_JSON", "DTCG JSON is null");
        if (json.Length > MaxDtcgInputCharacters)
            return ImportFailure("RESOURCE_LIMIT", $"DTCG JSON exceeds maximum length ({MaxDtcgInputCharacters} characters)");

        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                DocumentSerializer.ValidateNoDuplicateProperties(json);
                var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxDtcgDepth
                });
                if (root is not JsonObject rootObject)
                    return ImportFailure("INVALID_DTCG", "DTCG root must be a JSON object");

                var colors = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var typography = new SortedDictionary<string, TypographyToken>(StringComparer.Ordinal);
                var typographyAliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var budget = new TokenImportBudget(cancellationToken);
                ProcessDtcgNode(rootObject, string.Empty, null, 0, colors, typography, typographyAliases, budget);
                ValidateColorAliases(document, colors, cancellationToken);
                ResolveTypographyAliases(typography, typographyAliases, cancellationToken);
                ValidateImportedTypography(typography);

                var changed = colors.Any(pair =>
                        document.Tokens?.Colors?.TryGetValue(pair.Key, out var existing) != true || existing != pair.Value)
                    || typography.Any(pair =>
                        document.Tokens?.Typography?.TryGetValue(pair.Key, out var existing) != true
                        || existing is null
                        || !TypographyEquals(existing, pair.Value));

                if (changed)
                {
                    EnsureTokensInitialized(document);
                    document.Tokens!.Colors ??= [];
                    document.Tokens.Typography ??= [];
                    foreach (var pair in colors)
                        document.Tokens.Colors[pair.Key] = pair.Value;
                    foreach (var pair in typography)
                        document.Tokens.Typography[pair.Key] = pair.Value;
                    DocumentSynchronization.IncrementRevision(document);
                }

                return new TokenImportResult(true, colors.Count, typography.Count);
            }
            catch (JsonException ex)
            {
                return ImportFailure("INVALID_JSON", ex.Message);
            }
            catch (TokenImportException ex)
            {
                return ImportFailure(ex.Code, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ImportFailure("INVALID_TOKEN_VALUE", ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                return ImportFailure("INVALID_JSON", ex.Message);
            }
        }
    }

    public TokenExportResult ExportDtcg(Document document, bool resolveReferences = false) =>
        ExportDtcg(document, resolveReferences, CancellationToken.None);

    public TokenExportResult ExportDtcg(
        Document document,
        bool resolveReferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            return ExportDtcgCore(document, resolveReferences, cancellationToken);
        }
    }

    private TokenExportResult ExportDtcgCore(
        Document document,
        bool resolveReferences,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = new JsonObject();
            var budget = new TokenExportBudget(cancellationToken);
            if (document.Tokens?.Colors is { Count: > 0 })
            {
                var colorGroup = new JsonObject { ["$type"] = DtcgTokenTypes.Color };
                foreach (var (name, color) in document.Tokens.Colors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = color;
                    if (resolveReferences)
                    {
                        var resolved = ResolveColorRefWithDiagnostics(document, color);
                        if (!resolved.Success || resolved.Value is null)
                            return new TokenExportResult(false, null, resolved.ErrorCode, resolved.ErrorMessage);
                        value = resolved.Value;
                    }
                    else if (TryGetTokenName(value, out var aliasName))
                    {
                        value = ToDtcgColorReference(aliasName);
                    }
                    else if (DocumentLimits.IsValidTokenName(value) && document.Tokens.Colors.ContainsKey(value))
                    {
                        value = ToDtcgColorReference(value);
                    }
                    else if (!DocumentLimits.IsValidColor(value))
                    {
                        return new TokenExportResult(false, null, "INVALID_TOKEN_VALUE", $"Color token '{name}' is not a safe literal color or alias");
                    }

                    budget.ChargeToken(name, value.Length);
                    AddDtcgToken(colorGroup, name, new JsonObject { ["$value"] = value });
                }
                root["color"] = colorGroup;
            }

            if (document.Tokens?.Typography is { Count: > 0 })
            {
                var typographyGroup = new JsonObject { ["$type"] = DtcgTokenTypes.Typography };
                foreach (var (name, token) in document.Tokens.Typography.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateTypographyForExport(name, token);
                    budget.ChargeToken(name, token.FontFamily?.Length ?? 0, 128);
                    var value = new JsonObject();
                    if (token.FontFamily is not null)
                        value["fontFamily"] = token.FontFamily;
                    if (token.FontSize is not null)
                        value["fontSize"] = $"{token.FontSize.Value.ToString("0.####", CultureInfo.InvariantCulture)}px";
                    if (token.FontWeight is not null)
                        value["fontWeight"] = token.FontWeight;
                    if (token.LineHeight is not null)
                        value["lineHeight"] = token.LineHeight;

                    AddDtcgToken(typographyGroup, name, new JsonObject { ["$value"] = value });
                }
                root["typography"] = typographyGroup;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var content = root.ToJsonString(DtcgSerializerOptions);
            if (content.Length > MaxDtcgOutputCharacters)
                return new TokenExportResult(false, null, "RESOURCE_LIMIT", $"DTCG output exceeds maximum length ({MaxDtcgOutputCharacters} characters)");
            return new TokenExportResult(true, content);
        }
        catch (TokenImportException ex)
        {
            return new TokenExportResult(false, null, ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// Resolves direct names and braced references recursively. This compatibility
    /// method returns the original input on a missing reference or alias cycle.
    /// </summary>
    public string? ResolveColorRef(Document document, string? reference)
    {
        var result = ResolveColorRefWithDiagnostics(document, reference);
        return result.Success ? result.Value : reference;
    }

    public TokenResolutionResult<string> ResolveColorRefWithDiagnostics(Document document, string? reference)
    {
        if (reference is null)
            return TokenResolutionResult<string>.Ok(null);
        if (reference.Length == 0)
            return TokenResolutionResult<string>.Ok(string.Empty);

        var requiredReference = TryGetTokenName(reference, out var tokenName);
        if (!requiredReference)
        {
            tokenName = reference;
            if (document.Tokens?.Colors?.ContainsKey(tokenName) != true)
                return TokenResolutionResult<string>.Ok(reference);
        }

        return ResolveColorToken(document, tokenName, new HashSet<string>(StringComparer.Ordinal), 0);
    }

    public TokenResolutionResult<TypographyToken> ResolveTypographyRefWithDiagnostics(Document document, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return TokenResolutionResult<TypographyToken>.Fail("INVALID_REFERENCE", "Typography token reference is empty");

        var requiredReference = TryGetTokenName(reference, out var tokenName);
        if (!requiredReference)
            tokenName = reference;
        if (document.Tokens?.Typography?.TryGetValue(tokenName, out var token) != true)
            return TokenResolutionResult<TypographyToken>.Fail("TOKEN_NOT_FOUND", $"Typography token '{tokenName}' was not found");
        return TokenResolutionResult<TypographyToken>.Ok(token);
    }

    public void ResolveNodeTokenRefs(Document document, Node node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureSupportedVersion(document);
            ResolveNodeTokenRefsCore(document, node);
        }
    }

    private void ResolveNodeTokenRefsCore(Document document, Node node)
    {
        if (node.Style?.Fill?.TokenRef is { } fillReference)
        {
            var resolved = ResolveColorRefWithDiagnostics(document, fillReference);
            if (resolved.Success)
                node.Style.Fill.Color = resolved.Value;
        }
        if (node.Style?.Stroke?.TokenRef is { } strokeReference)
        {
            var resolved = ResolveColorRefWithDiagnostics(document, strokeReference);
            if (resolved.Success)
                node.Style.Stroke.Color = resolved.Value;
        }
        if (node is TextNode text && text.TypographyTokenRef is { } typographyReference)
        {
            var resolved = ResolveTypographyRefWithDiagnostics(document, typographyReference);
            if (resolved is { Success: true, Value: { } typographyToken })
            {
                text.FontFamily = typographyToken.FontFamily ?? text.FontFamily;
                text.FontSize = typographyToken.FontSize ?? text.FontSize;
                text.FontWeight = typographyToken.FontWeight ?? text.FontWeight;
                // Line height precedence is node, then token, then default; keep the node value.
                if (text.LineHeight is null && typographyToken.LineHeight is > 0)
                    text.LineHeight = typographyToken.LineHeight;
            }
        }
    }

    public Document ResolveAllTokenRefs(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document resolved;
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureSupportedVersion(document);
            resolved = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))!;
        }
        ResolveNodeTokenRefsRecursive(resolved, resolved.Root);
        return resolved;
    }

    private TokenResolutionResult<string> ResolveColorToken(
        Document document,
        string tokenName,
        HashSet<string> visited,
        int depth)
    {
        if (depth >= DocumentLimits.MaxTokenAliasDepth)
            return TokenResolutionResult<string>.Fail("TOKEN_ALIAS_DEPTH", $"Color token alias depth exceeds {DocumentLimits.MaxTokenAliasDepth}");
        if (!visited.Add(tokenName))
            return TokenResolutionResult<string>.Fail("TOKEN_ALIAS_CYCLE", $"Color token alias cycle detected at '{tokenName}'");
        if (document.Tokens?.Colors?.TryGetValue(tokenName, out var value) != true)
            return TokenResolutionResult<string>.Fail("TOKEN_NOT_FOUND", $"Color token '{tokenName}' was not found");
        if (value is null)
            return TokenResolutionResult<string>.Fail("INVALID_TOKEN_VALUE", $"Color token '{tokenName}' has a null value");

        if (TryGetTokenName(value, out var aliasName))
        {
            return ResolveColorToken(document, aliasName, visited, depth + 1);
        }
        if (DocumentLimits.IsValidTokenName(value))
        {
            return ResolveColorToken(document, value, visited, depth + 1);
        }

        return TokenResolutionResult<string>.Ok(value);
    }

    private void ResolveNodeTokenRefsRecursive(Document document, Node node)
    {
        ResolveNodeTokenRefsCore(document, node);
        if (node is IContainerNode container)
        {
            foreach (var child in container.Children)
                ResolveNodeTokenRefsRecursive(document, child);
        }
    }

    private static void ProcessDtcgNode(
        JsonObject obj,
        string path,
        string? inheritedType,
        int depth,
        SortedDictionary<string, string> colors,
        SortedDictionary<string, TypographyToken> typography,
        SortedDictionary<string, string> typographyAliases,
        TokenImportBudget budget)
    {
        budget.ChargeObject();
        if (depth > MaxDtcgDepth)
            throw new TokenImportException("RESOURCE_LIMIT", $"DTCG nesting exceeds maximum ({MaxDtcgDepth})");

        var currentType = ReadOptionalString(obj["$type"], "$type", path) ?? inheritedType;
        if (obj.TryGetPropertyValue("$value", out var valueNode))
        {
            var externalTokenName = path.TrimStart('.');
            var tokenName = CanonicalizeImportedName(externalTokenName, currentType);
            if (tokenName.Length == 0)
                throw new TokenImportException("INVALID_DTCG", "A token at the root has no name");
            budget.ChargeToken(tokenName);
            if (valueNode is null)
                throw InvalidValue(tokenName, "$value must not be null");

            switch (currentType)
            {
                case DtcgTokenTypes.Color:
                    if (!TryReadString(valueNode, out var color) || string.IsNullOrWhiteSpace(color))
                        throw InvalidValue(tokenName, "color $value must be a non-empty string");
                    if (TryGetTokenName(color, out var colorAlias))
                        color = $"{{{CanonicalizeImportedName(colorAlias, DtcgTokenTypes.Color)}}}";
                    else if (!DocumentLimits.IsValidColor(color))
                        throw InvalidValue(tokenName, "color $value must be a strict literal color or token alias");
                    if (!colors.TryAdd(tokenName, color))
                        throw new TokenImportException("INVALID_DTCG", $"Duplicate color token path '{tokenName}'");
                    break;
                case DtcgTokenTypes.Typography:
                    if (TryReadString(valueNode, out var typographyReference)
                        && TryGetTokenName(typographyReference, out var aliasedTokenName))
                    {
                        aliasedTokenName = CanonicalizeImportedName(aliasedTokenName, DtcgTokenTypes.Typography);
                        if (!typographyAliases.TryAdd(tokenName, aliasedTokenName))
                            throw new TokenImportException("INVALID_DTCG", $"Duplicate typography token path '{tokenName}'");
                    }
                    else
                    {
                        if (!typography.TryAdd(tokenName, ParseTypography(valueNode, tokenName)))
                            throw new TokenImportException("INVALID_DTCG", $"Duplicate typography token path '{tokenName}'");
                    }
                    break;
                case null:
                    throw InvalidValue(tokenName, "$type is required or must be inherited from a group");
                default:
                    // Unsupported valid DTCG types are intentionally ignored.
                    break;
            }
            return;
        }

        foreach (var (key, child) in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (key.StartsWith('$'))
                continue;
            budget.ValidatePathSegment(key);
            if (child is null)
                throw new TokenImportException("INVALID_DTCG", $"DTCG group or token '{JoinPath(path, key)}' must not be null");
            if (child is not JsonObject childObject)
                throw new TokenImportException("INVALID_DTCG", $"DTCG group '{JoinPath(path, key)}' must be an object");
            ProcessDtcgNode(childObject, JoinPath(path, key), currentType, depth + 1, colors, typography, typographyAliases, budget);
        }
    }

    private static void ResolveTypographyAliases(
        SortedDictionary<string, TypographyToken> typography,
        SortedDictionary<string, string> aliases,
        CancellationToken cancellationToken)
    {
        foreach (var aliasName in aliases.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            typography[aliasName] = ResolveTypographyAlias(
                aliasName,
                typography,
                aliases,
                new HashSet<string>(StringComparer.Ordinal),
                0,
                cancellationToken);
        }
    }

    private static void ValidateColorAliases(
        Document document,
        SortedDictionary<string, string> stagedColors,
        CancellationToken cancellationToken)
    {
        foreach (var tokenName in stagedColors.Keys)
        {
            ResolveImportedColorAlias(
                document,
                stagedColors,
                tokenName,
                new HashSet<string>(StringComparer.Ordinal),
                0,
                cancellationToken);
        }
    }

    private static string ResolveImportedColorAlias(
        Document document,
        IReadOnlyDictionary<string, string> stagedColors,
        string tokenName,
        HashSet<string> resolving,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth >= DocumentLimits.MaxTokenAliasDepth)
            throw new TokenImportException("TOKEN_ALIAS_DEPTH", $"Color token alias depth exceeds {DocumentLimits.MaxTokenAliasDepth}");
        if (!resolving.Add(tokenName))
            throw new TokenImportException("TOKEN_ALIAS_CYCLE", $"Color token alias cycle detected at '{tokenName}'");

        string value;
        if (stagedColors.TryGetValue(tokenName, out var stagedValue))
            value = stagedValue;
        else if (document.Tokens?.Colors?.TryGetValue(tokenName, out var existingValue) == true)
            value = existingValue;
        else
            throw new TokenImportException("TOKEN_NOT_FOUND", $"Color token '{tokenName}' was not found");

        string result;
        if (TryGetTokenName(value, out var aliasName))
        {
            result = ResolveImportedColorAlias(
                document,
                stagedColors,
                aliasName,
                resolving,
                depth + 1,
                cancellationToken);
        }
        else if (DocumentLimits.IsValidColor(value))
        {
            result = value;
        }
        else
        {
            throw InvalidValue(tokenName, "color value must be a strict literal color or token alias");
        }

        resolving.Remove(tokenName);
        return result;
    }

    private static TypographyToken ResolveTypographyAlias(
        string tokenName,
        SortedDictionary<string, TypographyToken> typography,
        SortedDictionary<string, string> aliases,
        HashSet<string> resolving,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth >= DocumentLimits.MaxTokenAliasDepth)
            throw new TokenImportException("TOKEN_ALIAS_DEPTH", $"Typography token alias depth exceeds {DocumentLimits.MaxTokenAliasDepth}");
        if (!resolving.Add(tokenName))
            throw new TokenImportException("TOKEN_ALIAS_CYCLE", $"Typography token alias cycle detected at '{tokenName}'");

        TypographyToken resolved;
        if (typography.TryGetValue(tokenName, out var concrete))
        {
            resolved = concrete;
        }
        else if (aliases.TryGetValue(tokenName, out var target))
        {
            resolved = ResolveTypographyAlias(target, typography, aliases, resolving, depth + 1, cancellationToken);
        }
        else
        {
            throw new TokenImportException("TOKEN_NOT_FOUND", $"Typography token '{tokenName}' was not found");
        }

        resolving.Remove(tokenName);
        return new TypographyToken
        {
            FontFamily = resolved.FontFamily,
            FontSize = resolved.FontSize,
            FontWeight = resolved.FontWeight,
            LineHeight = resolved.LineHeight
        };
    }

    private static TypographyToken ParseTypography(JsonNode node, string tokenName)
    {
        if (node is not JsonObject value)
            throw InvalidValue(tokenName, "typography $value must be an object");

        var fontFamily = ParseFontFamily(value, tokenName);
        var fontSize = ParseOptionalDimension(value, "fontSize", tokenName);
        var fontWeight = ParseOptionalFontWeight(value, tokenName);
        var lineHeight = ParseOptionalLineHeight(value, tokenName);
        if (fontFamily is null && fontSize is null && fontWeight is null && lineHeight is null)
            throw InvalidValue(tokenName, "typography $value must contain at least one supported property");

        return new TypographyToken
        {
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontWeight = fontWeight,
            LineHeight = lineHeight
        };
    }

    private static string? ParseFontFamily(JsonObject value, string tokenName)
    {
        if (!value.TryGetPropertyValue("fontFamily", out var node))
            return null;
        if (node is null)
            throw InvalidValue(tokenName, "fontFamily must not be null");
        if (TryReadString(node, out var family) && !string.IsNullOrWhiteSpace(family))
        {
            if (family.Length > DocumentLimits.MaxFontFamilyLength)
                throw InvalidValue(tokenName, $"fontFamily exceeds maximum length ({DocumentLimits.MaxFontFamilyLength})");
            return family;
        }
        if (node is JsonArray array && array.Count > 0)
        {
            var families = new List<string>(array.Count);
            foreach (var item in array)
            {
                if (item is null || !TryReadString(item, out var arrayFamily) || string.IsNullOrWhiteSpace(arrayFamily))
                    throw InvalidValue(tokenName, "fontFamily array must contain only non-empty strings");
                if (arrayFamily.Length > DocumentLimits.MaxFontFamilyLength)
                    throw InvalidValue(tokenName, $"fontFamily exceeds maximum length ({DocumentLimits.MaxFontFamilyLength})");
                families.Add(arrayFamily);
            }
            var joined = string.Join(", ", families);
            if (joined.Length > DocumentLimits.MaxFontFamilyLength)
                throw InvalidValue(tokenName, $"fontFamily exceeds maximum length ({DocumentLimits.MaxFontFamilyLength})");
            return joined;
        }
        throw InvalidValue(tokenName, "fontFamily must be a non-empty string or string array");
    }

    private static double? ParseOptionalDimension(JsonObject value, string propertyName, string tokenName)
    {
        if (!value.TryGetPropertyValue(propertyName, out var node))
            return null;
        if (!TryParseDimension(node, out var result) || result < 0)
            throw InvalidValue(tokenName, $"{propertyName} must be a finite non-negative number or px/pt dimension");
        return result;
    }

    private static int? ParseOptionalFontWeight(JsonObject value, string tokenName)
    {
        if (!value.TryGetPropertyValue("fontWeight", out var node))
            return null;
        if (node is null)
            throw InvalidValue(tokenName, "fontWeight must not be null");

        int? weight = null;
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var intValue))
            weight = intValue;
        else if (TryReadString(node, out var stringValue))
        {
            weight = stringValue.ToLowerInvariant() switch
            {
                "thin" => 100,
                "extralight" or "extra-light" => 200,
                "light" => 300,
                "normal" or "regular" => 400,
                "medium" => 500,
                "semibold" or "semi-bold" => 600,
                "bold" => 700,
                "extrabold" or "extra-bold" => 800,
                "black" => 900,
                _ => int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
            };
        }

        if (weight is < 1 or > 1000 || weight is null)
            throw InvalidValue(tokenName, "fontWeight must be a known name or integer in the range [1, 1000]");
        return weight;
    }

    private static double? ParseOptionalLineHeight(JsonObject value, string tokenName)
    {
        if (!value.TryGetPropertyValue("lineHeight", out var node))
            return null;
        if (!TryParseDimension(node, out var lineHeight) || lineHeight < 0)
            throw InvalidValue(tokenName, "lineHeight must be a finite non-negative number or px/pt dimension");
        return lineHeight;
    }

    private static bool TryParseDimension(JsonNode? node, out double result)
    {
        result = 0;
        if (node is not JsonValue value)
            return false;
        if (value.TryGetValue<double>(out result))
            return double.IsFinite(result);
        if (!value.TryGetValue<string>(out var text) || !DimensionPattern().IsMatch(text))
            return false;

        var multiplier = text.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? 4d / 3d : 1d;
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) || text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            text = text[..^2];
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && double.IsFinite(result = result * multiplier);
    }

    private static string? ReadOptionalString(JsonNode? node, string property, string path)
    {
        if (node is null)
            return null;
        if (TryReadString(node, out var value))
            return value;
        throw new TokenImportException("INVALID_DTCG", $"{property} on '{path}' must be a string");
    }

    private static bool TryReadString(JsonNode node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetTokenName(string reference, out string tokenName)
    {
        var match = TokenRefPattern().Match(reference);
        tokenName = match.Success ? match.Groups[1].Value : string.Empty;
        return match.Success;
    }

    private static string ToDtcgColorReference(string tokenName) => $"{{color.{tokenName}}}";

    private static string CanonicalizeImportedName(string name, string? tokenType)
    {
        var prefix = tokenType switch
        {
            DtcgTokenTypes.Color => "color.",
            DtcgTokenTypes.Typography => "typography.",
            _ => null
        };
        return prefix is not null && name.StartsWith(prefix, StringComparison.Ordinal)
            ? name[prefix.Length..]
            : name;
    }

    private static void AddDtcgToken(JsonObject typeGroup, string tokenName, JsonObject token)
    {
        if (string.IsNullOrWhiteSpace(tokenName) || tokenName.Length > MaxDtcgNameCharacters)
            throw new TokenImportException("INVALID_DTCG", "Token name is empty or exceeds the DTCG name limit");
        var segments = tokenName.Split('.');
        var current = typeGroup;
        for (var index = 0; index < segments.Length; index++)
        {
            ValidatePathSegment(segments[index]);
            if (index == segments.Length - 1)
            {
                if (current.ContainsKey(segments[index]))
                    throw new TokenImportException("INVALID_DTCG", $"Token path '{tokenName}' conflicts with another token or group");
                current[segments[index]] = token;
                return;
            }

            if (!current.TryGetPropertyValue(segments[index], out var child))
            {
                var group = new JsonObject();
                current[segments[index]] = group;
                current = group;
            }
            else if (child is JsonObject childObject && !childObject.ContainsKey("$value"))
            {
                current = childObject;
            }
            else
            {
                throw new TokenImportException("INVALID_DTCG", $"Token path '{tokenName}' conflicts with another token or group");
            }
        }
    }

    private static void ValidateTypographyForExport(string name, TypographyToken token)
    {
        try
        {
            ValidateTypographyToken(token);
        }
        catch (DocumentEngineException ex)
        {
            throw InvalidValue(name, ex.Message);
        }
    }

    private static void ValidatePathSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment) || segment.Length > MaxDtcgNameCharacters)
            throw new TokenImportException("INVALID_DTCG", "DTCG path segment is empty or too long");
        foreach (var character in segment)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                throw new TokenImportException(
                    "INVALID_DTCG",
                    $"DTCG path segment '{segment}' contains unsupported characters");
            }
        }
    }

    private static bool TypographyEquals(TypographyToken left, TypographyToken right) =>
        left.FontFamily == right.FontFamily
        && left.FontSize == right.FontSize
        && left.FontWeight == right.FontWeight
        && left.LineHeight == right.LineHeight;

    private static void ValidateImportedTypography(IReadOnlyDictionary<string, TypographyToken> typography)
    {
        foreach (var (name, token) in typography)
        {
            if (!DocumentLimits.IsValidTokenName(name))
                throw InvalidValue(name, "token name is invalid or oversized");
            try
            {
                ValidateTypographyToken(token);
            }
            catch (DocumentEngineException ex)
            {
                throw InvalidValue(name, ex.Message);
            }
        }
    }

    private static void ValidateTokenName(string name)
    {
        if (!DocumentLimits.IsValidTokenName(name))
            throw InvalidProperty($"Token name is invalid or exceeds maximum length ({DocumentLimits.MaxTokenNameLength})");
    }

    private static void ValidateTypographyToken(TypographyToken token)
    {
        if (token.FontFamily is { } family &&
            (string.IsNullOrWhiteSpace(family) || family.Length > DocumentLimits.MaxFontFamilyLength))
        {
            throw InvalidProperty($"Font family must be nonblank and at most {DocumentLimits.MaxFontFamilyLength} characters");
        }
        if (token.FontSize is { } fontSize && (!double.IsFinite(fontSize) || fontSize <= 0))
            throw InvalidProperty("Typography font size must be finite and positive");
        if (token.FontWeight is { } fontWeight && fontWeight is < 100 or > 900)
            throw InvalidProperty("Typography font weight must be between 100 and 900");
        if (token.LineHeight is { } lineHeight && (!double.IsFinite(lineHeight) || lineHeight < 0))
            throw InvalidProperty("Typography line height must be finite and non-negative");
    }

    private static DocumentEngineException InvalidProperty(string message) =>
        new(EngineErrors.InvalidProperty, message);

    private static string JoinPath(string path, string key) =>
        string.IsNullOrEmpty(path) ? key : $"{path}.{key}";

    private static TokenImportException InvalidValue(string tokenName, string message) =>
        new("INVALID_TOKEN_VALUE", $"Token '{tokenName}': {message}");

    private static TokenImportResult ImportFailure(string code, string message) =>
        new(false, 0, 0, code, message);

    private static void EnsureTokensInitialized(Document document) =>
        document.Tokens ??= new DesignTokens();

    private sealed class TokenImportBudget(CancellationToken cancellationToken)
    {
        private int _objectCount;
        private int _tokenCount;

        public void ChargeObject()
        {
            if ((_objectCount++ & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (_objectCount > MaxDtcgTokenCount * 2)
                throw new TokenImportException("RESOURCE_LIMIT", $"DTCG object count exceeds maximum ({MaxDtcgTokenCount * 2})");
        }

        public void ChargeToken(string tokenName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_tokenCount > MaxDtcgTokenCount)
                throw new TokenImportException("RESOURCE_LIMIT", $"DTCG token count exceeds maximum ({MaxDtcgTokenCount})");
            if (tokenName.Length > MaxDtcgNameCharacters)
                throw new TokenImportException("RESOURCE_LIMIT", $"DTCG token name exceeds maximum length ({MaxDtcgNameCharacters})");
        }

        public void ValidatePathSegment(string segment) => TokenService.ValidatePathSegment(segment);
    }

    private sealed class TokenExportBudget(CancellationToken cancellationToken)
    {
        private int _tokenCount;
        private int _estimatedCharacters;

        public void ChargeToken(string name, params int[] valueLengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_tokenCount > MaxDtcgTokenCount)
                throw new TokenImportException("RESOURCE_LIMIT", $"DTCG token count exceeds maximum ({MaxDtcgTokenCount})");
            var additional = checked(name.Length + valueLengths.Sum() + 128);
            if (_estimatedCharacters > MaxDtcgOutputCharacters - additional)
                throw new TokenImportException("RESOURCE_LIMIT", $"DTCG output exceeds maximum length ({MaxDtcgOutputCharacters} characters)");
            _estimatedCharacters += additional;
        }
    }

    private sealed class TokenImportException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
