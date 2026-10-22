using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Lint;

/// <summary>Input for <see cref="IDocumentEngine"/> lint.</summary>
public sealed record LintOptions
{
    public static LintOptions Default { get; } = new();

    /// <summary>
    /// <see cref="ValidationProfile.WebIcon"/> forces the off-grid rule and enables the
    /// symbol-readiness rule. Lint does not run the validator itself.
    /// </summary>
    public ValidationProfile Profile { get; init; }

    /// <summary>
    /// The pixel size the document will be shown at, along its larger dimension.
    /// Strokes are scaled by <c>TargetSize / max(canvas.width, canvas.height)</c>
    /// before the hairline check. Null means native scale.
    /// </summary>
    public double? TargetSize { get; init; }

    /// <summary>Off-grid rule: require whole pixels (warning) instead of half pixels (info).</summary>
    public bool StrictGrid { get; init; }
}
