namespace FTPlus.AgentGraphicDesign.Core.Validation;

public enum ValidationProfile
{
    Default,
    WebIcon
}

public sealed record ValidationOptions
{
    public static ValidationOptions Default { get; } = new();
    public static ValidationOptions WebIcon { get; } = new() { Profile = ValidationProfile.WebIcon };

    public ValidationProfile Profile { get; init; }
    public string? BackgroundOverride { get; init; }
}

public static class ValidationProfiles
{
    public const string WebIcon = "web-icon";

    public static bool TryParse(string? value, out ValidationProfile profile)
    {
        profile = ValidationProfile.Default;
        if (value is null)
            return true;
        if (!value.Equals(WebIcon, StringComparison.OrdinalIgnoreCase))
            return false;

        profile = ValidationProfile.WebIcon;
        return true;
    }
}

public static class WebIconValidationCodes
{
    public const string Background = "WEBICON001";
    public const string TextNode = "WEBICON002";
    public const string ImageNode = "WEBICON003";
    public const string UnknownNode = "WEBICON004";
}
