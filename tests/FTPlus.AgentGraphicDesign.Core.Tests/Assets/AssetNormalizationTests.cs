using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Tests.Rendering;
using FTPlus.AgentGraphicDesign.Skia;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class AssetNormalizationTests : IDisposable
{
    private readonly string _root = PlatformPaths.FullPath(Path.Combine(Path.GetTempPath(), "agd-normalization-" + Guid.NewGuid().ToString("N")));
    private readonly DocumentEngine _engine = new(renderer: new SkiaRenderer());
    public AssetNormalizationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [InspectionFact]
    public async Task SharedParentInventories_AreChargedOncePerPhaseAcrossTheWholePipeline()
    {
        File.WriteAllText(Path.Combine(_root, "source.json"), DocumentSerializer.Serialize(_engine.CreateDocument("icon", 16, 16)));
        Directory.CreateDirectory(Path.Combine(_root, "generated"));
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "generated",
            assets = Enumerable.Range(0, 256).Select(i => new { source = "source.json", output = $"generated/{i}.svg" })
        }));
        for (var i = 0; i < 4093; i++) File.WriteAllText(Path.Combine(_root, "f" + i), "");
        var roots = 0;
        var scanned = 0;
        var writers = 0;
        IEnumerable<string> Enumerate(string parent)
        {
            if (parent == _root) roots++;
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent)) { scanned++; yield return entry; }
        }
        // Real source loading, inspection, planning and comparison; only the inventory observer
        // and explicit macOS policy selection are injected so this regression also runs on Linux.
        AssetPathPolicy Policy() => new(true, Enumerate, path => File.Exists(path) || Directory.Exists(path),
            (path, ct) => AssetPaths.Inspect(path, true, true, PlatformCapability.AssetPipeline, ct));
        var pipeline = new AssetPipeline(_engine, new VectorAssetProducer(_engine), (path, options) =>
        {
            if (options.Access != FileAccess.Read) writers++;
            return LeafFileIO.Open(path, options);
        }, Policy);
        var before = Snapshot(_root);
        var result = await pipeline.RunAsync(manifest, true);
        Assert.Equal(256, result.Files.Count);
        Assert.All(result.Files, file => Assert.Equal("missing", file.Status));
        Assert.Equal(2, roots); // planning and fresh comparison, not once per output
        Assert.InRange(scanned, 8192, AssetPathPolicy.MaxScannedEntries);
        Assert.Equal(0, writers);
        Assert.Equal(before, Snapshot(_root));
        File.WriteAllText(Path.Combine(_root, "too-many"), "");
        before = Snapshot(_root);
        Assert.Equal(EngineErrors.ResourceLimit,
            (await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false))).Code);
        Assert.Equal(0, writers);
        Assert.Equal(before, Snapshot(_root));
    }

    [InspectionFact]
    public async Task CanonicalEquivalentPlansAndExistingNames_PreserveSourcesBeforeAnyPublication()
    {
        foreach (var reversed in new[] { false, true })
        {
            var first = reversed ? "cafe\u0301" : "caf\u00e9";
            var second = reversed ? "caf\u00e9" : "cafe\u0301";
            foreach (var scenario in new[] { "leaf-plan", "parent-plan", "leaf-existing", "parent-existing", "source-overlap", "font-overlap" })
            {
                var dir = Path.Combine(_root, (reversed ? "reverse-" : "forward-") + scenario);
                Directory.CreateDirectory(dir);
                var source = "source.json";
                var outputRoot = "generated";
                string[] outputs = scenario switch
                {
                    "leaf-plan" => [$"generated/{first}.svg", $"generated/{second}.svg"],
                    "parent-plan" => [$"generated/{first}/one.svg", $"generated/{second}/two.svg"],
                    "parent-existing" => [$"generated/{second}/new.svg"],
                    "source-overlap" => [$"{second}/source.svg"],
                    "font-overlap" => [$"{second}/new.png"],
                    _ => [$"generated/{second}.svg"]
                };
                if (scenario == "source-overlap") { source = first + "/source.svg"; outputRoot = second; }
                if (scenario == "font-overlap") { Directory.CreateDirectory(Path.Combine(dir, first)); outputRoot = second; }
                if (scenario.EndsWith("existing", StringComparison.Ordinal))
                {
                    var stored = Path.Combine(dir, "generated", scenario == "leaf-existing" ? first + ".svg" : first + "/old.svg");
                    Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
                    File.WriteAllText(stored, "previous output");
                }
                var sourcePath = Path.Combine(dir, source);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                File.WriteAllText(sourcePath, DocumentSerializer.Serialize(_engine.CreateDocument("icon", 16, 16)));
                var original = File.ReadAllBytes(sourcePath);
                var manifest = Path.Combine(dir, "manifest.json");
                File.WriteAllText(manifest, JsonSerializer.Serialize(new
                {
                    version = 1,
                    outputRoot,
                    assets = outputs.Select(output => new
                    {
                        source,
                        output,
                        options = new { fontDirs = scenario == "font-overlap" ? new[] { first } : null }
                    })
                }));
                var before = Snapshot(dir);
                var pipeline = new AssetPipeline(_engine, new VectorAssetProducer(_engine));
                foreach (var check in new[] { true, false })
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, check));
                        Assert.Contains(error.Code, new[] { EngineErrors.PathEscape, EngineErrors.InvalidArguments });
                        Assert.Equal(before, Snapshot(dir));
                    }
                    else
                    {
                        // These spellings are distinct on the Linux qualification filesystem.
                        var result = await pipeline.RunAsync(manifest, check);
                        Assert.Equal(outputs.Length, result.Files.Count(file => file.Status != "unexpected"));
                    }
                    Assert.Equal(original, File.ReadAllBytes(sourcePath));
                }
            }
        }
    }

    [InspectionFact]
    public async Task UniqueDecomposedSpelling_SurvivesBuildCheckAndFurtherPublication()
    {
        var source = Path.Combine(_root, "cafe\u0301.json");
        File.WriteAllText(source, DocumentSerializer.Serialize(_engine.CreateDocument("icon", 16, 16)));
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "cafe\u0301",
            assets = new[]
            {
                new { source = "cafe\u0301.json", output = "cafe\u0301/first.svg" },
                new { source = "cafe\u0301.json", output = "cafe\u0301/second.svg" }
            }
        }));
        var pipeline = new AssetPipeline(_engine, new VectorAssetProducer(_engine));
        Assert.True((await pipeline.RunAsync(manifest, false)).Success);
        var before = Snapshot(_root);
        Assert.True((await pipeline.RunAsync(manifest, true)).Success);
        Assert.True((await pipeline.RunAsync(manifest, false)).Success);
        Assert.Equal(before, Snapshot(_root));
        Assert.Equal("cafe\u0301", Assert.Single(Directory.EnumerateDirectories(_root)).Split(Path.DirectorySeparatorChar)[^1]);
    }

    private static string[] Snapshot(string directory) => Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
        .Select(path => path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + (Directory.Exists(path) ? "directory" : Convert.ToBase64String(File.ReadAllBytes(path))))
        .Order(StringComparer.Ordinal).ToArray();
}
