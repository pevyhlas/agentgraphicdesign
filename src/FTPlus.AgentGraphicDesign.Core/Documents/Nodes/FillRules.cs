namespace FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

/// <summary>
/// Canonical <c>style.fillRule</c> values. A null fill rule means <see cref="NonZero"/>
/// and is omitted from canonical JSON. The rule is meaningful for path and polygon
/// nodes; other node types ignore it.
/// </summary>
public static class FillRules
{
    public const string NonZero = "nonzero";
    public const string EvenOdd = "evenodd";

    /// <summary>True for null (default nonzero) or one of the canonical lowercase values.</summary>
    public static bool IsValid(string? fillRule) =>
        fillRule is null or NonZero or EvenOdd;

    public static bool IsEvenOdd(string? fillRule) =>
        string.Equals(fillRule, EvenOdd, StringComparison.Ordinal);

    /// <summary>Case-insensitive parse of a surface input to the canonical lowercase value.</summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null)
            return false;
        if (value.Equals(NonZero, StringComparison.OrdinalIgnoreCase))
        {
            normalized = NonZero;
            return true;
        }
        if (value.Equals(EvenOdd, StringComparison.OrdinalIgnoreCase))
        {
            normalized = EvenOdd;
            return true;
        }
        return false;
    }
}
