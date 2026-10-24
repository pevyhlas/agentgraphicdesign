using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Skia;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

/// <summary>
/// Feature 012 T003 / plan C1: the exhaustive characterization of the authoritative
/// <c>Rgba8</c> conversion (Skia <c>ReadPixels</c> to unpremultiplied RGBA8888, through the
/// renderer's own read-back) over every premultiplied pair <c>(α, c ≤ α)</c> — 32 896 pairs in one
/// 256×256 surface — against (a) the PNG encoder followed by the Core managed decoder and (b) the
/// closed form <c>round(p·255/α)</c>, half away from zero. The outcome is recorded under
/// the ignored evidence directory named by <c>AGD_RECORD_CHARACTERIZATION_DIR</c>.
/// <para>
/// Recorded outcome (macOS arm64 and Linux x64, SkiaSharp 4.148): the closed form misses 291 pairs,
/// every one an exact half (<c>p·255/α = n + ½</c>) that the read-back rounds down, so no closed form
/// in exact arithmetic is the rule. The read-back equals <see cref="SkiaRule"/> — Skia's float32
/// raster-pipeline unpremultiply, <c>rne((p·(1/255)) · (1 / (α·(1/255))) · 255)</c> with
/// round-half-to-even — on every pair, and the PNG encoder plus the Core decoder equal the read-back on
/// every pair. AC-01 therefore expects every channel exactly under <see cref="SkiaRule"/> where the
/// premultiplied value is known, and compares <c>Rgba8</c> with the decoder on every pixel.
/// </para>
/// </summary>
public sealed class Rgba8UnpremultiplyCharacterizationTests
{
    private const int Side = 256;
    internal const int PairCount = 256 * 257 / 2;

    [Fact]
    public void ReadPixelsUnpremul_MatchesTheRecordedRuleAndThePngDecoderOnEveryPair()
    {
        var characterization = Characterize();

        if (Environment.GetEnvironmentVariable("AGD_RECORD_CHARACTERIZATION_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "rgba8-unpremultiply.json"),
                characterization.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        Assert.Equal(PairCount, characterization.Pairs);
        // Alpha is never changed by either conversion, and α 0 always reads back as (0, 0, 0, 0).
        Assert.Equal(0, characterization.AlphaMismatches);
        Assert.Equal(0, characterization.TransparentNonZero);
        // The recorded rule is exact on every pair.
        Assert.True(characterization.RuleMismatches.Count == 0,
            $"ReadPixels differs from the recorded Skia rule on {characterization.RuleMismatches.Count} pairs, first: {Describe(characterization.RuleMismatches)}");
        // The closed form in exact arithmetic is not the rule: it misses exactly the exact-half pairs
        // that float32 rounds down (291 pairs, three channels each).
        Assert.Equal(291 * 3, characterization.ClosedFormMismatches.Count);
        Assert.All(characterization.ClosedFormMismatches, m =>
        {
            Assert.True(2 * m.Premultiplied * 255 % (2 * m.Alpha) == m.Alpha, $"α={m.Alpha} p={m.Premultiplied} is not an exact half");
            Assert.Equal(m.Other - 1, m.ReadBack);
        });
        // The PNG encoder unpremultiplies identically, so the decoder oracle applies on every pair.
        Assert.True(characterization.PngMismatches.Count == 0,
            $"PNG encode → decode differs from ReadPixels on {characterization.PngMismatches.Count} pairs, first: {Describe(characterization.PngMismatches)}");
    }

    [Fact]
    public void ClosedForm_IsTheHalfAwayFromZeroRoundingOfTheExactQuotient()
    {
        // Independent restatement of the oracle so a wrong integer formula cannot hide.
        for (var alpha = 1; alpha < 256; alpha++)
        {
            for (var premultiplied = 0; premultiplied <= alpha; premultiplied++)
            {
                var exact = (int)Math.Round(premultiplied * 255m / alpha, MidpointRounding.AwayFromZero);
                Assert.Equal(exact, ClosedForm(premultiplied, alpha));
            }
        }
        Assert.Equal(0, ClosedForm(0, 0));
    }

    /// <summary>
    /// The recorded read-back rule: Skia's float32 raster-pipeline unpremultiply of an 8-bit
    /// premultiplied channel — normalise by <c>1/255</c>, multiply by <c>1/a</c>, scale by 255 and
    /// round half to even. Written here independently of Skia, in IEEE single precision.
    /// </summary>
    internal static int SkiaRule(int premultiplied, int alpha)
    {
        if (alpha == 0)
            return 0;
        const float inverse255 = 1f / 255f;
        var value = premultiplied * inverse255;
        var scale = 1f / (alpha * inverse255);
        return (int)MathF.Round(value * scale * 255f, MidpointRounding.ToEven);
    }

    /// <summary><c>round(p·255/α)</c>, half away from zero, in integers; 0 for α 0.</summary>
    internal static int ClosedForm(int premultiplied, int alpha) =>
        alpha == 0 ? 0 : (2 * premultiplied * 255 + alpha) / (2 * alpha);

    internal static Characterization Characterize()
    {
        // Pixel (x = c, y = α) holds the premultiplied pair (c, c, c, α) for c ≤ α. The unused upper
        // triangle repeats the diagonal (α, α, α, α) so every stored value is a valid premultiplied pixel.
        var premultiplied = new byte[Side * Side * 4];
        for (var alpha = 0; alpha < Side; alpha++)
        {
            for (var x = 0; x < Side; x++)
            {
                var channel = (byte)Math.Min(x, alpha);
                var offset = (alpha * Side + x) * 4;
                premultiplied[offset] = channel;
                premultiplied[offset + 1] = channel;
                premultiplied[offset + 2] = channel;
                premultiplied[offset + 3] = (byte)alpha;
            }
        }

        byte[] readBack;
        byte[] png;
        var info = new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(premultiplied, GCHandleType.Pinned);
        try
        {
            // Wrap the premultiplied bytes as the renderer's surface type, then use the renderer's own
            // read-back and encoder on its snapshot, exactly as SkiaRenderer.Render does.
            using var surface = SKSurface.Create(info, handle.AddrOfPinnedObject(), info.RowBytes)
                ?? throw new InvalidOperationException("Cannot wrap the characterization surface");
            using var image = surface.Snapshot();
            readBack = SkiaRenderer.ReadUnpremultiplied(image);
            png = SkiaRenderer.EncodeImage(image, Core.Rendering.RenderFormat.Png);
        }
        finally
        {
            handle.Free();
        }

        var decoded = PngRgbaVerifier.DecodeOwned(png, Side, Side, static () => { }, static _ => { }).Rgba;

        var result = new Characterization();
        for (var alpha = 0; alpha < Side; alpha++)
        {
            for (var c = 0; c <= alpha; c++)
            {
                result.Pairs++;
                var offset = (alpha * Side + c) * 4;
                if (readBack[offset + 3] != alpha || decoded[offset + 3] != alpha)
                    result.AlphaMismatches++;
                if (alpha == 0 && (readBack[offset] | readBack[offset + 1] | readBack[offset + 2]) != 0)
                    result.TransparentNonZero++;
                for (var channel = 0; channel < 3; channel++)
                {
                    var actual = readBack[offset + channel];
                    var expected = ClosedForm(c, alpha);
                    if (actual != expected)
                        result.ClosedFormMismatches.Add(new Mismatch(alpha, c, channel, actual, expected));
                    if (actual != SkiaRule(c, alpha))
                        result.RuleMismatches.Add(new Mismatch(alpha, c, channel, actual, SkiaRule(c, alpha)));
                    if (decoded[offset + channel] != actual)
                        result.PngMismatches.Add(new Mismatch(alpha, c, channel, actual, decoded[offset + channel]));
                }
            }
        }
        return result;
    }

    private static string Describe(List<Mismatch> mismatches) =>
        string.Join("; ", mismatches.Take(8).Select(m => $"α={m.Alpha} p={m.Premultiplied} ch={m.Channel} readBack={m.ReadBack} other={m.Other}"));

    internal sealed record Mismatch(int Alpha, int Premultiplied, int Channel, int ReadBack, int Other);

    internal sealed class Characterization
    {
        public int Pairs { get; set; }

        public int AlphaMismatches { get; set; }

        public int TransparentNonZero { get; set; }

        public List<Mismatch> ClosedFormMismatches { get; } = [];

        public List<Mismatch> PngMismatches { get; } = [];

        public List<Mismatch> RuleMismatches { get; } = [];

        public JsonObject ToJson()
        {
            static JsonArray List(IEnumerable<Mismatch> mismatches) => new(mismatches
                .Select(m => (JsonNode)new JsonArray(m.Alpha, m.Premultiplied, m.Channel, m.ReadBack, m.Other)).ToArray());
            return new JsonObject
            {
                ["schema"] = "agd-012-rgba8-unpremultiply/1",
                ["platform"] = $"{(OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsWindows() ? "win" : "other")}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}",
                ["skiaSharp"] = typeof(SKImage).Assembly.GetName().Version?.ToString(),
                ["pairs"] = Pairs,
                ["alphaMismatches"] = AlphaMismatches,
                ["transparentNonZero"] = TransparentNonZero,
                ["closedForm"] = "round(p*255/alpha), half away from zero; 0 for alpha 0",
                ["closedFormMismatchCount"] = ClosedFormMismatches.Count,
                ["recordedRule"] = "rne((p*(1/255f)) * (1f/(alpha*(1/255f))) * 255f) in IEEE float32, round half to even; 0 for alpha 0",
                ["recordedRuleMismatchCount"] = RuleMismatches.Count,
                ["pngVsReadBackMismatchCount"] = PngMismatches.Count,
                ["mismatchColumns"] = new JsonArray("alpha", "premultiplied", "channel", "readBack", "other"),
                ["closedFormMismatches"] = List(ClosedFormMismatches),
                ["pngVsReadBackMismatches"] = List(PngMismatches)
            };
        }
    }
}
