using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FTPlus.AgentGraphicDesign.Tests.Characterization;

/// <summary>One legacy (no-condition) contact sheet characterized before the S05 compositor change.</summary>
/// <param name="Name">Stable case key in the golden files.</param>
/// <param name="Documents">Fixture document basenames (without <c>.agd.json</c>), in grid order.</param>
/// <param name="Cell">Square cell size.</param>
/// <param name="Gap">Gap override, or null for today's default (CLI only).</param>
/// <param name="Background">Sheet background override, or null for today's default (CLI only).</param>
/// <param name="Columns">Column override, or null for <c>ceil(sqrt(count))</c>.</param>
/// <param name="Labels">Labels under the cells.</param>
/// <param name="FontDirectory">Pass <see cref="LegacyContactSheetFixture.FontDirectoryName"/> as the only font directory.</param>
/// <param name="Mcp">Whether today's MCP <c>render_contact_sheet</c> can express the case (it has no gap or background).</param>
internal sealed record LegacySheetCase(
    string Name,
    string[] Documents,
    int Cell,
    int? Gap,
    string? Background,
    int? Columns,
    bool Labels,
    bool FontDirectory,
    bool Mcp);

/// <summary>
/// Shared inputs and golden-file plumbing for the S05 T001 characterization of today's grid
/// (feature 012 plan D3, D25, D29, D32; AC-09 and the AC-25 <c>--font-dir</c> anchor). The goldens
/// under <c>tests/Fixtures/legacy-contact-sheet/</c> were recorded from the pre-change code and are
/// the oracle every later compositor change is compared with: they must never be re-recorded to
/// make a changed grid pass. PNG hashes, PNG byte sizes and image hashes are per platform
/// (recorded on Linux x64, the CI platform) and are compared only there; cell maps, text,
/// exit codes and refusal messages are compared on every platform.
/// </summary>
internal static class LegacyContactSheetFixture
{
    public const string FontDirectoryName = "fonts";
    public const string GoldenPlatform = "linux-x64";

    /// <summary>When set to a directory, the characterization tests write their actual values there instead of comparing.</summary>
    public const string RecordDirectoryVariable = "AGD_RECORD_CHARACTERIZATION_DIR";

    /// <summary>Legacy grids over the fixture documents, including the D3 wide-aspect stretch cases.</summary>
    public static IReadOnlyList<LegacySheetCase> Cases { get; } =
    [
        // 10x3 in a 128 cell: fitted box 128x38, tile rendered at 127x38 and stretched (D3).
        new("labels-off-128", ["square", "wide-10x3", "translucent-edge", "wide-100x30"], 128, null, null, null, false, false, true),
        new("labels-on-128", ["square", "wide-10x3", "translucent-edge", "wide-100x30"], 128, null, null, null, true, false, true),
        // 100x30 in a 64 cell: fitted box 64x19, tile rendered at 63x19 (D3); custom gap and background.
        new("custom-64", ["wide-100x30", "wide-10x3", "translucent-edge", "square"], 64, 3, "#1e293b", 4, true, false, false),
        // Font directory with a copy of the bundled Inter-Bold.ttf; text and labels at weight 400, so the
        // directory's Bold face wins over bundled Regular (AC-25 anchor; closure RC-05).
        new("font-dir", ["text-inter", "square", "translucent-edge", "text-inter"], 128, null, null, 2, true, true, true),
        // The same sheet without the font directory: its bytes must differ (sensitivity check).
        new("font-dir-control", ["text-inter", "square", "translucent-edge", "text-inter"], 128, null, null, 2, true, false, true)
    ];

    /// <summary>Today's legacy reference cap plus one (D29).</summary>
    public const int OverCapReferenceCount = 257;

    public static string Root { get; } = Path.Combine(FindRepositoryRoot(), "tests", "Fixtures", "legacy-contact-sheet");

    public static string DocumentsDirectory => Path.Combine(Root, "documents");

    public static string PlatformName =>
        $"{(OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsWindows() ? "win" : "other")}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    public static bool IsGoldenPlatform => PlatformName == GoldenPlatform;

    /// <summary>The fixture document path for <paramref name="name"/>.</summary>
    public static string DocumentPath(string name) => Path.Combine(DocumentsDirectory, name + ".agd.json");

    /// <summary>Copies every fixture document and the font directory into <paramref name="directory"/>.</summary>
    public static void CopyInputs(string directory)
    {
        foreach (var file in Directory.GetFiles(DocumentsDirectory, "*.agd.json"))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
        var fonts = Directory.CreateDirectory(Path.Combine(directory, FontDirectoryName));
        File.Copy(BundledBoldFontPath, Path.Combine(fonts.FullName, "Inter-Bold.ttf"), overwrite: true);
    }

    /// <summary>The repository copy of the bundled Inter Bold face (embedded into the Skia assembly at build).</summary>
    public static string BundledBoldFontPath =>
        Path.Combine(FindRepositoryRoot(), "src", "FTPlus.AgentGraphicDesign.Skia", "Fonts", "Inter-Bold.ttf");

    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static JsonObject LoadGolden(string surface)
    {
        var path = GoldenPath(surface);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Characterization golden missing: {path}");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    /// <summary>
    /// Writes <paramref name="actual"/> to the record directory when <see cref="RecordDirectoryVariable"/>
    /// is set and returns true; the caller then skips its comparison.
    /// </summary>
    public static bool TryRecord(string surface, JsonObject actual)
    {
        var directory = Environment.GetEnvironmentVariable(RecordDirectoryVariable);
        if (string.IsNullOrEmpty(directory))
            return false;
        Directory.CreateDirectory(directory);
        var document = new JsonObject
        {
            ["platform"] = PlatformName,
            ["note"] = "Pre-change characterization of the legacy contact-sheet grid (feature 012 T001). Never re-record to make a changed grid pass.",
            ["cases"] = actual
        };
        File.WriteAllText(Path.Combine(directory, surface + ".golden.json"),
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        return true;
    }

    /// <summary>
    /// Compares one case. On the golden platform the whole case must match; elsewhere the
    /// platform-dependent fields (<paramref name="platformFields"/>, and PNG <c>size</c> values inside
    /// recorded JSON text) are masked first.
    /// </summary>
    public static void AssertCase(JsonObject golden, string caseName, JsonObject actual, params string[] platformFields)
    {
        if (golden["platform"]?.GetValue<string>() != GoldenPlatform)
            throw new InvalidOperationException($"Golden was recorded on {golden["platform"]}, expected {GoldenPlatform}");
        var expected = golden["cases"]?[caseName]?.AsObject()
            ?? throw new InvalidOperationException($"Golden has no case '{caseName}'");
        var left = expected.DeepClone().AsObject();
        var right = actual.DeepClone().AsObject();
        if (!IsGoldenPlatform)
        {
            Mask(left, platformFields);
            Mask(right, platformFields);
        }
        if (!JsonNode.DeepEquals(left, right))
        {
            throw new Xunit.Sdk.XunitException(
                $"Legacy contact sheet '{caseName}' drifted from the pre-change characterization on {PlatformName}.\n" +
                $"Expected: {left.ToJsonString()}\nActual:   {right.ToJsonString()}");
        }
    }

    private static void Mask(JsonObject value, string[] platformFields)
    {
        foreach (var field in platformFields)
        {
            if (value.ContainsKey(field))
                value[field] = "platform-dependent";
        }
        foreach (var (key, node) in value.ToArray())
        {
            if (node is JsonValue text && text.TryGetValue<string>(out var s))
                value[key] = SizePattern().Replace(s, "\"size\": 0");
        }
    }

    private static string GoldenPath(string surface) => Path.Combine(Root, surface + ".golden.json");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FTPlus.AgentGraphicDesign.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }

    private static Regex SizePattern() => new("\"size\":\\s*\\d+", RegexOptions.CultureInvariant);
}
