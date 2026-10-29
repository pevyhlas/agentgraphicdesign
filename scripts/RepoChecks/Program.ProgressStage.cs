using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private const string ProgressFixture = "tests/FTPlus.AgentGraphicDesign.Core.Tests/Fixtures/platform/platform-dotnet10-osx-arm64-26.0.json";
    private sealed record ProgressBuild(string SourceCommit, string Root, string CheckerMvid, Dictionary<string, string> Assemblies);

    private static string ProgressStage(string text)
    {
        if (text.Length > 16_384) throw new InvalidDataException("Stage fixture exceeds its bound");
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 8 });
        var root = doc.RootElement;
        if (root.GetProperty("architecture").GetString() != "arm64" || root.GetProperty("os").GetString() != "osx"
            || root.GetProperty("osVersion").GetString() != "26.0" || root.GetProperty("runtimeVersion").GetString() != "10.0.0"
            || root.GetProperty("profile").GetString() != "dotnet10-osx-arm64") throw new InvalidDataException("Wrong stage fixture profile");
        var capabilities = root.GetProperty("capabilities").EnumerateArray().ToArray();
        if (capabilities.Length != KnownCapabilities.Length) throw new InvalidDataException("Wrong stage capability inventory");
        var states = new List<string>();
        for (var i = 0; i < capabilities.Length; i++)
        {
            var cap = capabilities[i];
            if (cap.GetProperty("capability").GetString() != KnownCapabilities[i] || cap.GetProperty("requires").EnumerateObject().Any())
                throw new InvalidDataException("Stage capability identity/requirements differ");
            states.Add(cap.GetProperty("qualification").GetString()!);
        }
        var qualified = root.GetProperty("qualifiedProfiles").EnumerateArray().ToArray();
        if (qualified.Length != 1 || qualified[0].GetProperty("profile").GetString() != "dotnet10-linux-x64"
            || qualified[0].GetProperty("minOsVersion").ValueKind != JsonValueKind.Null
            || qualified[0].GetProperty("requires").EnumerateObject().Any()
            || !qualified[0].GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()).SequenceEqual(KnownCapabilities))
            throw new InvalidDataException("Stage fixture changes qualified profiles");
        return string.Join(',', states) switch
        {
            "unqualified,unqualified,refused,refused,refused,refused" => "S0",
            "unqualified,unqualified,unqualified,unqualified,refused,refused" => "S1",
            _ => throw new InvalidDataException("Unknown stage capability policy")
        };
    }

    private static bool ProgressStageTransition(string[] options) => options switch
    {
        [] => false,
        ["--stage-transition", "S0:S1"] => true,
        _ => throw new ArgumentException("Only explicit --stage-transition S0:S1 is supported")
    };

    private static void ValidateProgressStages(string previous, string current, string baseStage, string headStage, bool transition)
    {
        if (previous != baseStage || current != headStage || previous is not ("S0" or "S1") || current is not ("S0" or "S1")
            || (transition ? previous != "S0" || current != "S1" : previous != current))
            throw new InvalidDataException("Source-bound progress stage/transition mismatch");
    }

    private static Dictionary<string, string> ProgressAssemblyPaths()
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["checker"] = "scripts/RepoChecks/bin/Release/net10.0/RepoChecks.dll"
        };
        foreach (var name in new[] { "Core", "Skia", "Cli", "Mcp" })
        {
            var prefix = "FTPlus.AgentGraphicDesign." + name;
            paths["src-" + name] = $"src/{prefix}/bin/Release/net10.0/{(name == "Cli" ? "agd" : name == "Mcp" ? "agd-mcp" : prefix)}.dll";
            paths["test-" + name] = $"tests/{prefix}.Tests/bin/Release/net10.0/{prefix}.Tests.dll";
            paths["test-core-" + name] = $"tests/{prefix}.Tests/bin/Release/net10.0/FTPlus.AgentGraphicDesign.Core.dll";
            if (name != "Core") paths["src-core-" + name] = $"src/{prefix}/bin/Release/net10.0/FTPlus.AgentGraphicDesign.Core.dll";
        }
        return paths;
    }

    private static string ReadMvid(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D");
    }

    private static ProgressBuild CaptureProgressBuild(string root, string sha, string executingMvid)
    {
        var paths = ProgressAssemblyPaths();
        var checker = Path.GetFullPath(Path.Combine(root, paths["checker"]));
        if (typeof(Program).Assembly.Location != checker || ReadMvid(checker) != executingMvid)
            throw new InvalidDataException("Relaunch the freshly built exact-checkout checker; running image differs");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths.Values)
        {
            using var stream = File.OpenRead(Path.Combine(root, path));
            if (stream.Length > ProgressMaxBytes) throw new InvalidDataException("Progress assembly exceeds its bound");
            hashes.Add(path, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }
        var record = new ProgressBuild(sha, root, ReadMvid(checker), hashes);
        ValidateProgressBuild(record, sha);
        return record;
    }

    private static void ValidateProgressBuild(ProgressBuild build, string sha)
    {
        var paths = ProgressAssemblyPaths();
        if (build.SourceCommit != sha || !Path.IsPathFullyQualified(build.Root) || !Guid.TryParseExact(build.CheckerMvid, "D", out _)
            || build.Assemblies is null || !build.Assemblies.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(paths.Values)
            || build.Assemblies.Values.Any(hash => !ProgressHash(hash))) throw new InvalidDataException("Invalid build identity capture");
        foreach (var (key, path) in paths.Where(pair => pair.Key.Contains("-core-", StringComparison.Ordinal)))
            if (build.Assemblies[path] != build.Assemblies[paths["src-Core"]]) throw new InvalidDataException("Stale tested Core copy: " + key);
    }

    private static void ValidateProgressTestAssembly(string trx, string suite, ProgressBuild build)
    {
        var text = ProgressText(trx);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ProgressMaxBytes });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var methods = document.Descendants(ns + "TestMethod").ToArray();
        var expected = Path.Combine(build.Root, ProgressAssemblyPaths()["test-" + suite]);
        if (methods.Length == 0 || methods.Any(method => method.Attribute("codeBase")?.Value != expected))
            throw new InvalidDataException("TRX did not execute the captured test assembly: " + suite);
    }

    private static void ValidateProgressCommand(string directory, string label, string root, string executable, string[] args)
    {
        using var doc = JsonDocument.Parse(ProgressText(Path.Combine(directory, label + ".command.json")), new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 8 });
        var capture = doc.RootElement;
        if (capture.GetProperty("root").GetString() != root || capture.GetProperty("executable").GetString() != executable
            || !capture.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).SequenceEqual(args)
            || ProgressText(Path.Combine(directory, label + ".exit")).Trim() != "0")
            throw new InvalidDataException("Wrong or failed captured command: " + label);
        _ = ProgressText(Path.Combine(directory, label + ".stdout.log"));
        _ = ProgressText(Path.Combine(directory, label + ".stderr.log"));
    }
}
