using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    /// <summary>Test-only same-ID/version extracted-cache fixture for approved-bootstrap qualification.</summary>
    private static int CreatePoisonCache(string package, string root)
    {
        using var archive = ZipFile.OpenRead(package);
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspec.Open();
        var metadata = XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "metadata");
        var id = metadata.Elements().Single(element => element.Name.LocalName == "id").Value.ToLowerInvariant();
        var version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value.ToLowerInvariant();
        var directory = Path.Combine(root, id, version);
        Directory.CreateDirectory(directory);
        ZipFile.ExtractToDirectory(package, directory);
        var cachedPackage = Path.Combine(directory, $"{id}.{version}.nupkg");
        File.Copy(package, cachedPackage, overwrite: true);
        using (var output = new FileStream(cachedPackage, FileMode.Append))
            output.Write("poison-cache-fixture"u8);
        using var input = File.OpenRead(cachedPackage);
        var hash = Convert.ToBase64String(SHA512.HashData(input));
        File.WriteAllText(cachedPackage + ".sha512", hash);
        File.WriteAllText(Path.Combine(directory, ".nupkg.metadata"), JsonSerializer.Serialize(new { version = 2, contentHash = hash, source = root }));
        // Invalid extracted entry must never be selected/executed from this ambient cache.
        var dll = Directory.EnumerateFiles(directory, "agd*.dll", SearchOption.AllDirectories).First();
        File.WriteAllText(dll, "poisoned extracted tool assembly");
        Console.WriteLine($"Created same-identity poisoned cache fixture: {id} {version}");
        return 0;
    }
}
