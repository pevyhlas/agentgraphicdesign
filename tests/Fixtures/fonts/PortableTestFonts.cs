using System.Security.Cryptography;
using Xunit;

namespace FTPlus.AgentGraphicDesign.TestFixtures;

internal static class PortableTestFonts
{
    internal const string SansFamily = "Noto Sans";
    internal const string ArabicFamily = "Noto Sans Arabic";
    // Keep the >1-unit policy assertion sensitive with the approved Noto/Inter pair.
    internal const string WidthProbe = "Hello world iiiiiiiiiiiiiiii";

    internal static string VerifiedDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fonts");
        foreach (var (file, digest) in new[]
        {
            ("NotoSans-Regular.ttf", "b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5"),
            ("NotoSansArabic-Regular.ttf", "ceea25b464a656dc3b26849bab9356740401af62aedf1bfa8b7f0d9b75925b1b"),
            ("LICENSE", "0dab92d0544f7b233403f14b84a663bdbfa746982eda629e7f4f9ffe1b036feb")
        })
        {
            using var input = File.OpenRead(Path.Combine(directory, file));
            Assert.Equal(digest, Convert.ToHexStringLower(SHA256.HashData(input)));
        }
        return directory;
    }
}
