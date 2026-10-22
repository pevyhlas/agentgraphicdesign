using System.Globalization;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>Central resource and grammar limits for persisted document strings.</summary>
public static class DocumentLimits
{
    public const int MaxNameLength = 256;
    public const int MaxNodeOrderLength = 1_024;
    public const int MaxTextLength = 1_000_000;
    public const int MaxPathDataLength = 1_000_000;
    public const int MaxImageSourceLength = 2_000_000;
    public const int MaxFontFamilyLength = 1_024;
    public const int MaxColorLength = 64;
    public const int MaxTokenNameLength = 512;
    public const int MaxTokenReferenceLength = 514;
    public const int MaxTokenAliasDepth = 64;
    public const int MaxDesignTokensPerCategory = 50_000;
    public const int MaxTokenValidationDiagnostics = 256;
    public const int MaxPersistedStringDiagnostics = 256;
    public const int MaxStyleStringLength = 128;
    public const int MaxSchemaLength = 2_048;
    public const int MaxExtensionPropertyNameLength = 1_024;
    public const int MaxExtensionStringLength = 1_000_000;

    public static bool IsValidColor(string? color)
    {
        if (string.IsNullOrEmpty(color) || color.Length > MaxColorLength)
        {
            return false;
        }

        if (color[0] == '#')
        {
            var hex = color[1..];
            return hex.Length is 3 or 4 or 6 or 8 && hex.All(char.IsAsciiHexDigit);
        }

        if (TryGetFunctionArguments(color, "rgb", out var rgb))
        {
            return TryParseRgbComponents(rgb, requireAlpha: false);
        }

        return TryGetFunctionArguments(color, "rgba", out var rgba) &&
            TryParseRgbComponents(rgba, requireAlpha: true);
    }

    public static bool IsFullyTransparentColor(string? color)
    {
        if (!IsValidColor(color))
            return false;

        if (color![0] == '#')
        {
            return color.Length == 5 && color[^1] == '0' ||
                color.Length == 9 && color.EndsWith("00", StringComparison.OrdinalIgnoreCase);
        }

        if (!TryGetFunctionArguments(color, "rgba", out var rgba))
            return false;

        var alpha = rgba[(rgba.LastIndexOf(',') + 1)..].Trim();
        return double.TryParse(alpha, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
            value == 0;
    }

    public static bool IsValidTokenName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxTokenNameLength)
        {
            return false;
        }

        return name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    }

    public static bool TryGetTokenReference(string? value, out string tokenName)
    {
        tokenName = string.Empty;
        if (value is null || value.Length is < 3 or > MaxTokenReferenceLength ||
            value[0] != '{' || value[^1] != '}')
        {
            return false;
        }

        tokenName = value[1..^1];
        return IsValidTokenName(tokenName);
    }

    internal static IEnumerable<PersistedStringViolation> FindPersistedStringViolations(Document document)
    {
        if (document.Schema is { } schema && schema.Length > MaxSchemaLength)
            yield return new("$.schema", MaxSchemaLength);
        if (document.Name.Length > MaxNameLength)
            yield return new("$.name", MaxNameLength);
        if (document.Canvas.Background is { } background && background.Length > MaxColorLength)
            yield return new("$.canvas.background", MaxColorLength);

        foreach (var violation in FindExtensionViolations(document.ExtensionData, "$"))
            yield return violation;
        foreach (var violation in FindExtensionViolations(document.Canvas.ExtensionData, "$.canvas"))
            yield return violation;

        if (document.Tokens is { } tokens)
        {
            foreach (var violation in FindExtensionViolations(tokens.ExtensionData, "$.tokens"))
                yield return violation;

            if (tokens.Colors is not null)
            {
                foreach (var (name, value) in tokens.Colors)
                {
                    if (name.Length > MaxTokenNameLength)
                        yield return new($"$.tokens.colors.{name}", MaxTokenNameLength);
                    if (value is not null && value.Length > MaxTokenReferenceLength)
                        yield return new($"$.tokens.colors.{name}", MaxTokenReferenceLength);
                }
            }

            if (tokens.Typography is not null)
            {
                foreach (var (name, token) in tokens.Typography)
                {
                    if (name.Length > MaxTokenNameLength)
                        yield return new($"$.tokens.typography.{name}", MaxTokenNameLength);
                    if (token?.FontFamily is { } family && family.Length > MaxFontFamilyLength)
                        yield return new($"$.tokens.typography.{name}.fontFamily", MaxFontFamilyLength);
                    if (token is not null)
                    {
                        foreach (var violation in FindExtensionViolations(token.ExtensionData, $"$.tokens.typography.{name}"))
                            yield return violation;
                    }
                }
            }
        }

        foreach (var violation in FindNodeViolations(document.Root, "$.root"))
            yield return violation;
    }

    internal static void ThrowIfPersistedStringsExceedLimits(Document document)
    {
        var violation = FindPersistedStringViolations(document).FirstOrDefault();
        if (violation is not null)
        {
            throw new JsonException(
                $"String at {violation.Path} exceeds maximum length ({violation.MaximumLength})",
                violation.Path,
                null,
                null);
        }
    }

    private static IEnumerable<PersistedStringViolation> FindNodeViolations(Node node, string path)
    {
        if (node.Order.Length > MaxNodeOrderLength)
            yield return new($"{path}.order", MaxNodeOrderLength);
        if (node.Name is { } name && name.Length > MaxNameLength)
            yield return new($"{path}.name", MaxNameLength);
        foreach (var violation in FindExtensionViolations(node.ExtensionData, path))
            yield return violation;

        if (node.Style is { } style)
        {
            if (style.BlendMode is { } blendMode && blendMode.Length > MaxStyleStringLength)
                yield return new($"{path}.style.blendMode", MaxStyleStringLength);
            if (style.FillRule is { } fillRule && fillRule.Length > MaxStyleStringLength)
                yield return new($"{path}.style.fillRule", MaxStyleStringLength);
            if (style.Fill?.Color is { } fillColor && fillColor.Length > MaxColorLength)
                yield return new($"{path}.style.fill.color", MaxColorLength);
            if (style.Fill?.TokenRef is { } fillRef && fillRef.Length > MaxTokenReferenceLength)
                yield return new($"{path}.style.fill.tokenRef", MaxTokenReferenceLength);
            if (style.Stroke?.Color is { } strokeColor && strokeColor.Length > MaxColorLength)
                yield return new($"{path}.style.stroke.color", MaxColorLength);
            if (style.Stroke?.TokenRef is { } strokeRef && strokeRef.Length > MaxTokenReferenceLength)
                yield return new($"{path}.style.stroke.tokenRef", MaxTokenReferenceLength);
            foreach (var violation in FindExtensionViolations(style.ExtensionData, $"{path}.style"))
                yield return violation;
            foreach (var violation in FindExtensionViolations(style.Fill?.ExtensionData, $"{path}.style.fill"))
                yield return violation;
            foreach (var violation in FindExtensionViolations(style.Stroke?.ExtensionData, $"{path}.style.stroke"))
                yield return violation;

            if (style.Fill?.Gradient is { } gradient)
            {
                for (var i = 0; i < gradient.Stops.Count; i++)
                {
                    if (gradient.Stops[i].Color.Length > MaxColorLength)
                        yield return new($"{path}.style.fill.gradient.stops[{i}].color", MaxColorLength);
                }
            }
        }

        switch (node)
        {
            case PathNode pathNode when pathNode.D.Length > MaxPathDataLength:
                yield return new($"{path}.d", MaxPathDataLength);
                break;
            case TextNode textNode:
                if (textNode.Text.Length > MaxTextLength)
                    yield return new($"{path}.text", MaxTextLength);
                if (textNode.FontFamily is { } family && family.Length > MaxFontFamilyLength)
                    yield return new($"{path}.fontFamily", MaxFontFamilyLength);
                if (textNode.TypographyTokenRef is { } tokenRef && tokenRef.Length > MaxTokenReferenceLength)
                    yield return new($"{path}.typographyTokenRef", MaxTokenReferenceLength);
                break;
            case ImageNode imageNode when imageNode.Src.Length > MaxImageSourceLength:
                yield return new($"{path}.src", MaxImageSourceLength);
                break;
            case UnknownNode unknownNode:
                if (unknownNode.OriginalType is { } originalType && originalType.Length > MaxNameLength)
                    yield return new($"{path}.originalType", MaxNameLength);
                if (unknownNode.SourceDiscriminator is { } discriminator && discriminator.Length > MaxNameLength)
                    yield return new($"{path}.type", MaxNameLength);
                break;
        }

        if (node is IContainerNode container)
        {
            for (var i = 0; i < container.Children.Count; i++)
            {
                foreach (var violation in FindNodeViolations(container.Children[i], $"{path}.children[{i}]"))
                    yield return violation;
            }
        }
    }

    private static IEnumerable<PersistedStringViolation> FindExtensionViolations(
        Dictionary<string, JsonElement>? extensionData,
        string path)
    {
        if (extensionData is null)
            yield break;

        foreach (var (property, value) in extensionData)
        {
            if (property.Length > MaxExtensionPropertyNameLength)
                yield return new($"{path}.{property}", MaxExtensionPropertyNameLength);
            foreach (var violation in FindJsonElementViolations(value, $"{path}.{property}"))
                yield return violation;
        }
    }

    private static IEnumerable<PersistedStringViolation> FindJsonElementViolations(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.String &&
            element.GetString() is { } value && value.Length > MaxExtensionStringLength)
        {
            yield return new(path, MaxExtensionStringLength);
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Length > MaxExtensionPropertyNameLength)
                    yield return new($"{path}.{property.Name}", MaxExtensionPropertyNameLength);
                foreach (var violation in FindJsonElementViolations(property.Value, $"{path}.{property.Name}"))
                    yield return violation;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                foreach (var violation in FindJsonElementViolations(item, $"{path}[{index++}]"))
                    yield return violation;
            }
        }
    }

    private static bool TryGetFunctionArguments(string value, string name, out string arguments)
    {
        arguments = string.Empty;
        var prefix = name + "(";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || value[^1] != ')')
            return false;
        arguments = value[prefix.Length..^1];
        return true;
    }

    private static bool TryParseRgbComponents(string arguments, bool requireAlpha)
    {
        var parts = arguments.Split(',');
        if (parts.Length != (requireAlpha ? 4 : 3))
            return false;

        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var component) ||
                component is < 0 or > 255)
            {
                return false;
            }
        }

        return !requireAlpha ||
            double.TryParse(parts[3].Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var alpha) &&
            double.IsFinite(alpha) && alpha is >= 0 and <= 1;
    }
}

internal sealed record PersistedStringViolation(string Path, int MaximumLength);
