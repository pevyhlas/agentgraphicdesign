using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Lint;

/// <summary>
/// Lint findings are advisory: <see cref="Warning"/> marks something that will
/// probably look wrong, <see cref="Info"/> marks something worth a second look.
/// Neither blocks validation or export.
/// </summary>
public enum LintSeverity
{
    Info,
    Warning
}

/// <summary>One design-lint finding. <see cref="NodeId"/> is null for document-level findings.</summary>
public sealed record LintDiagnostic
{
    private static readonly IReadOnlyDictionary<string, object?> NoDetails =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public required string Code { get; init; }

    public required LintSeverity Severity { get; init; }

    public required string Message { get; init; }

    public Guid? NodeId { get; init; }

    /// <summary>Rule-specific values (ratios, thresholds, boxes) for programmatic use.</summary>
    public IReadOnlyDictionary<string, object?> Details { get; init; } = NoDetails;
}

/// <summary>Result of <see cref="IDocumentEngine"/> lint, with the effective options echoed back.</summary>
public sealed record LintResult
{
    public required IReadOnlyList<LintDiagnostic> Diagnostics { get; init; }

    public required ValidationProfile Profile { get; init; }

    /// <summary>The pixel size the hairline rule evaluated against (the larger canvas dimension by default).</summary>
    public required double TargetSize { get; init; }

    public required bool StrictGrid { get; init; }

    /// <summary>Whether the off-grid rule ran (icon-sized canvas or the web-icon profile).</summary>
    public required bool GridChecked { get; init; }

    /// <summary>True when the diagnostic list hit the diagnostic cap (10,000 findings) and was cut.</summary>
    public bool Truncated { get; init; }

    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == LintSeverity.Warning);

    public int InfoCount => Diagnostics.Count(diagnostic => diagnostic.Severity == LintSeverity.Info);
}

/// <summary>Stable lint codes shared by all surfaces.</summary>
public static class LintCodes
{
    /// <summary>Text fill contrast against the background behind it is below WCAG 2.x AA.</summary>
    public const string TextContrast = "LINT001";

    /// <summary>A stroke renders thinner than one pixel at the target size.</summary>
    public const string HairlineStroke = "LINT002";

    /// <summary>Icon geometry is not on the half-pixel (or, strictly, whole-pixel) grid.</summary>
    public const string OffGrid = "LINT003";

    /// <summary>World bounds lie fully or partially outside the canvas.</summary>
    public const string OutsideCanvas = "LINT004";

    /// <summary>A color or typography token is referenced by no node.</summary>
    public const string UnusedToken = "LINT005";

    /// <summary>Content that cannot be seen: zero opacity, hidden containers, zero-size shapes, empty text.</summary>
    public const string InvisibleContent = "LINT006";

    /// <summary>Web-icon fills that a uniform currentColor export would recolor differently than drawn.</summary>
    public const string SymbolReadiness = "LINT007";

    public static readonly string[] All =
    [
        TextContrast, HairlineStroke, OffGrid, OutsideCanvas, UnusedToken, InvisibleContent, SymbolReadiness
    ];
}

/// <summary>String forms of <see cref="LintSeverity"/> shared by the CLI and MCP surfaces.</summary>
public static class LintSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";

    public static string Name(LintSeverity severity) => severity == LintSeverity.Warning ? Warning : Info;

    public static bool TryParse(string? value, out LintSeverity severity)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case Warning or "warn" or "warnings":
                severity = LintSeverity.Warning;
                return true;
            case Info or "infos" or "information":
                severity = LintSeverity.Info;
                return true;
            default:
                severity = default;
                return false;
        }
    }
}
