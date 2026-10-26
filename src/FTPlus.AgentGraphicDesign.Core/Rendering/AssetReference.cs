namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>The document arm of review references. Other asset domains are not admitted here.</summary>
public sealed record AssetReference(string DocumentPath, Guid? NodeId = null, string? Label = null)
{
    public static AssetReference Parse(string value, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var hash = value.LastIndexOf('#');
        if (hash >= 0 && value.Length - hash - 1 == 36 && Guid.TryParseExact(value.AsSpan(hash + 1), "D", out var node))
            return new AssetReference(value[..hash], node, label);
        return new AssetReference(value, Label: label);
    }
}
