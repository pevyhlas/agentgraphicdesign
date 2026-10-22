using SkiaSharp;

namespace FTPlus.AgentGraphicDesign.Skia.TextShaping;

/// <summary>
/// The default text family embedded in this assembly so raster output does not depend on
/// host fonts: Inter (SIL Open Font License 1.1), static Regular and Bold faces from the
/// rsms/inter v4.1 release. Provenance is recorded in <c>.config/dependencies.json</c> and
/// the licence in <c>THIRD-PARTY-NOTICES.txt</c>.
/// </summary>
public static class BundledFonts
{
    public const string FamilyName = "Inter";
    public const int RegularWeight = 400;
    public const int BoldWeight = 700;

    /// <summary>Requested weights at or above this value map to the Bold face.</summary>
    public const int BoldThreshold = 600;

    public const string RegularResourceName = "FTPlus.AgentGraphicDesign.Skia.Fonts.Inter-Regular.ttf";
    public const string BoldResourceName = "FTPlus.AgentGraphicDesign.Skia.Fonts.Inter-Bold.ttf";

    private static readonly Lazy<SKData> RegularData = new(() => LoadResource(RegularResourceName));
    private static readonly Lazy<SKData> BoldData = new(() => LoadResource(BoldResourceName));

    /// <summary>The bundled weight that serves a requested CSS weight.</summary>
    public static int SelectWeight(int requestedWeight) =>
        requestedWeight >= BoldThreshold ? BoldWeight : RegularWeight;

    /// <summary>
    /// Creates a typeface over the shared, immutable font bytes. The caller owns and
    /// disposes the returned typeface; the bytes stay resident for the process.
    /// </summary>
    public static SKTypeface CreateTypeface(int requestedWeight)
    {
        var data = SelectWeight(requestedWeight) == BoldWeight ? BoldData.Value : RegularData.Value;
        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException("The bundled Inter font data could not be parsed");
    }

    private static SKData LoadResource(string name)
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded font resource is missing: {name}");
        return SKData.Create(stream)
            ?? throw new InvalidOperationException($"Embedded font resource could not be read: {name}");
    }
}
