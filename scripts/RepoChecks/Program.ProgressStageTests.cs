using System.Text.Json;
using System.Text.Json.Nodes;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private static void RunProgressStageSelfTests(ICollection<string> errors)
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        const string s0 = """
            {"architecture":"arm64","os":"osx","osVersion":"26.0","profile":"dotnet10-osx-arm64","runtimeVersion":"10.0.0",
            "capabilities":[
            {"capability":"vector","qualification":"unqualified","requires":{}},
            {"capability":"rasterExport","qualification":"unqualified","requires":{}},
            {"capability":"assetPipeline","qualification":"refused","requires":{}},
            {"capability":"artifactVerify","qualification":"refused","requires":{}},
            {"capability":"stillImport","qualification":"refused","requires":{}},
            {"capability":"nativePixels","qualification":"refused","requires":{}}],
            "qualifiedProfiles":[{"capabilities":["vector","rasterExport","assetPipeline","artifactVerify","stillImport","nativePixels"],
            "minOsVersion":null,"profile":"dotnet10-linux-x64","requires":{}}]}
            """;
        var count = 0;
        void Check(string name, bool pass, Action action)
        {
            count++;
            try { action(); if (!pass) errors.Add("Stage self-test accepted " + name); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            { if (pass) errors.Add("Stage self-test rejected " + name + ": " + ex.Message); }
        }
        string Mutate(Action<JsonNode> edit)
        {
            var node = JsonNode.Parse(s0)!; edit(node); return node.ToJsonString();
        }
        var s1 = Mutate(node => { node["capabilities"]![2]!["qualification"] = "unqualified"; node["capabilities"]![3]!["qualification"] = "unqualified"; });
        Check("literal S0", true, () => { if (ProgressStage(s0) != "S0") throw new InvalidDataException(); });
        Check("literal S1", true, () => { if (ProgressStage(s1) != "S1") throw new InvalidDataException(); });
        foreach (var (name, text) in new[]
        {
            ("malformed", "{"), ("oversized", new string(' ', 16385)),
            ("duplicate key", s0.Replace("\"os\":\"osx\"", "\"os\":\"osx\",\"os\":\"osx\"", StringComparison.Ordinal)),
            ("duplicate capability", Mutate(n => n["capabilities"]![1]!["capability"] = "vector")),
            ("unknown capability", Mutate(n => n["capabilities"]![1]!["capability"] = "other")),
            ("missing capability", Mutate(n => n["capabilities"]!.AsArray().RemoveAt(1))),
            ("qualified macOS", Mutate(n => n["capabilities"]![0]!["qualification"] = "qualified")),
            ("requirements", Mutate(n => n["capabilities"]![2]!["requires"]!["filesystem"] = "apfs")),
            ("native admission", s1.Replace("\"capability\":\"nativePixels\",\"qualification\":\"refused\"", "\"capability\":\"nativePixels\",\"qualification\":\"unqualified\"", StringComparison.Ordinal)),
            ("new qualified profile", Mutate(n => n["qualifiedProfiles"]![0]!["profile"] = "dotnet10-osx-arm64"))
        }) Check(name, false, () => ProgressStage(text));

        // Invoke the command's real parser. Valid syntaxes reach the exact-SHA validation; invalid
        // syntaxes must fail earlier. No fake Git executable or network operation is needed.
        foreach (var (options, valid) in new (string[], bool)[]
        {
            ([], true), (["--stage-transition", "S0:S1"], true),
            (["--stage-transition", "S1:S0"], false), (["--stage-transition", "S1:S1"], false),
            (["--stage-transition", "S0:S2"], false), (["--stage-transition"], false),
            (["--ignore-stage"], false), (["--stage-transition", "S0:S1", "extra"], false)
        })
        {
            count++;
            try { _ = MacosProgress(["compare", ".", "bad-base", ".", ".", "bad-head", .. options]); errors.Add("Invalid SHA parsed"); }
            catch (ArgumentException ex)
            {
                if ((ex.Message == "Exact PR base and head required") != valid) errors.Add("Wrong CLI stage option classification");
            }
        }
        foreach (var (prior, nextStage, baseStage, headStage, flag, pass) in new[]
        {
            ("S0", "S0", "S0", "S0", false, true), ("S1", "S1", "S1", "S1", false, true),
            ("S0", "S1", "S0", "S1", true, true), ("S0", "S1", "S0", "S1", false, false),
            ("S1", "S0", "S1", "S0", true, false), ("S0", "S0", "S0", "S0", true, false),
            ("S1", "S1", "S1", "S1", true, false), ("S0", "S1", "S0", "S0", true, false),
            ("S0", "S1", "S1", "S1", true, false), ("S0", "S2", "S0", "S2", true, false)
        }) Check("source-bound transition", pass, () => ValidateProgressStages(prior, nextStage, baseStage, headStage, flag));

        var header = new ProgressHeader(1, sha, "S0", "private-tmp", "dotnet10-osx-arm64-macos26", "10.0.14", "10.0.113", "26.6.2", 0, 0, 0, "");
        var cases = ProgressSuites.Select(s => new ProgressCase(s, $"FTPlus.AgentGraphicDesign.{s}.Tests.Example.Case", "Passed")).ToArray();
        var bare = new ProgressCase("Core", "FTPlus.AgentGraphicDesign.Core.Tests.Rendering.ReviewInputTests.FifoReferenceAndFontEntriesRefuseWithBoundedWatchdog", "NotExecuted");
        ProgressRecord Record(string stage, params ProgressCase[] rows) => ParseProgress(SerializeProgress(new(header with { Stage = stage }, rows)), sha, "private-tmp");
        var previous = Record("S0", [.. cases, bare]);
        var expanded = new[] { false, true }.Select(value => bare with { Name = bare.Name + $"(fontEntry: {value}, name: \"pipe.txt\")", Outcome = "Passed" }).ToArray();
        var current = Record("S1", [.. cases, bare with { Outcome = "Passed" }, .. expanded]);
        Check("stable wrapper plus executed rows", true, () => CompareProgress(previous, current, new(1, [], []), true));
        Check("theory expansion loses bare identity", false, () => CompareProgress(previous, Record("S1", [.. cases, .. expanded]), new(1, [], []), true));
        Check("transition retains failure rejection", false, () => CompareProgress(previous, Record("S1", current.Cases.Select(c => c with { Outcome = "Failed" }).ToArray()), new(1, [], []), true));
        Check("transition retains skip rejection", false, () => CompareProgress(previous, Record("S1", current.Cases.Select(c => c with { Outcome = "NotExecuted" }).ToArray()), new(1, [], []), true));
        Check("transition retains toolchain rejection", false, () => CompareProgress(previous, current with { Header = current.Header with { Sdk = "10.0.114" } }, new(1, [], []), true));

        var directory = Path.Combine(Path.GetTempPath(), "agd-stage-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = ProgressAssemblyPaths();
            var build = new ProgressBuild(sha, directory, Guid.NewGuid().ToString("D"), paths.Values.ToDictionary(p => p, _ => new string('a', 64)));
            Check("complete assembly inventory", true, () => ValidateProgressBuild(build, sha));
            Check("wrong build source", false, () => ValidateProgressBuild(build, new string('f', 40)));
            var mismatch = build with { Assemblies = new(build.Assemblies) };
            mismatch.Assemblies[paths["test-core-Core"]] = new string('b', 64);
            Check("stale test Core copy", false, () => ValidateProgressBuild(mismatch, sha));
            mismatch.Assemblies.Remove(paths["test-Core"]);
            Check("missing test assembly", false, () => ValidateProgressBuild(mismatch, sha));
            const string label = "fixture-check";
            string[] args = [Path.Combine(directory, paths["checker"]), "platform-fixtures", directory, "--check"];
            void Capture(string executable = "dotnet", string exit = "0")
            {
                File.WriteAllText(Path.Combine(directory, label + ".command.json"), JsonSerializer.Serialize(new { executable, root = directory, args }));
                File.WriteAllText(Path.Combine(directory, label + ".stdout.log"), "Eight platform fixtures match the registry.\n");
                File.WriteAllText(Path.Combine(directory, label + ".stderr.log"), "");
                File.WriteAllText(Path.Combine(directory, label + ".exit"), exit);
            }
            void Validate() => ValidateProgressCommand(directory, label, directory, "dotnet", args);
            Capture(); Check("successful fixture capture", true, Validate);
            Capture(exit: "1"); Check("failed fixture capture", false, Validate);
            Capture(exit: "124"); Check("timed out fixture capture", false, Validate);
            Capture(executable: "other"); Check("wrong fixture command", false, Validate);
            Capture(); File.Delete(Path.Combine(directory, label + ".stderr.log")); Check("missing fixture stderr", false, Validate);
            Capture(); File.Delete(Path.Combine(directory, label + ".command.json")); Check("missing fixture command", false, Validate);
            var trx = Path.Combine(directory, "test.trx");
            var expected = Path.Combine(directory, paths["test-Core"]);
            var xml = new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("TestRun", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"),
                new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("TestMethod", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"), new System.Xml.Linq.XAttribute("codeBase", expected)));
            File.WriteAllText(trx, xml.ToString());
            Check("executed captured assembly", true, () => ValidateProgressTestAssembly(trx, "Core", build));
            xml.Elements().Single().SetAttributeValue("codeBase", expected + ".stale"); File.WriteAllText(trx, xml.ToString());
            Check("executed stale assembly", false, () => ValidateProgressTestAssembly(trx, "Core", build));

            // Exercise the public `check` path with a whole new-run capture, not just its helpers.
            var run = Path.Combine(directory, "complete");
            Directory.CreateDirectory(run);
            void Write(string name, string value) => File.WriteAllText(Path.Combine(run, name), value);
            void Command(string name, string executable, string[] arguments, string output)
            {
                Write(name + ".command.json", JsonSerializer.Serialize(new { executable, root = directory, args = arguments }));
                Write(name + ".stdout.log", output); Write(name + ".stderr.log", ""); Write(name + ".exit", "0");
            }
            Write("state", "COMPLETE\n");
            Write("environment.json", JsonSerializer.Serialize(new { mode = "private-tmp", sha, runtime = "10.0.14", os = "26.6.2", sdk = "10.0.113", temp = "/private/tmp/agd-progress.synthetic/", inherited = "/private/tmp/agd-progress.synthetic/" }));
            Write("build-before.json", JsonSerializer.Serialize(build)); Write("build-after.json", JsonSerializer.Serialize(build));
            foreach (var name in new[] { "head", "final-head", "source-state", "final-state", "sdk", "uname", "sw-vers", "dotnet-info" })
                Command(name, "fixture", [], name is "head" or "final-head" ? sha : name == "sdk" ? "10.0.113" : name == "uname" ? "arm64" : "");
            Command("restore", "dotnet", ["restore", "FTPlus.AgentGraphicDesign.slnx", "--locked-mode"], "");
            Command("build", "dotnet", ["build", "FTPlus.AgentGraphicDesign.slnx", "--configuration", "Release", "--no-restore", "--no-incremental"], "");
            Command("checker-build", "dotnet", ["build", "scripts/RepoChecks/RepoChecks.csproj", "--configuration", "Release", "--no-incremental"], "");
            Command("fixture-check", "dotnet", args, "Eight platform fixtures match the registry.\n");
            Command("stage-fixture", "git", ["--no-replace-objects", "show", sha + ":" + ProgressFixture], s1);
            var runRecord = Record("S1", cases);
            Write("private-tmp.tsv", SerializeProgress(runRecord));
            foreach (var item in cases)
            {
                Write(item.Suite + "-discovery.exit", "0"); Write(item.Suite + "-tests.exit", "0");
                Write(item.Suite + "-discovery.stdout.log", "The following Tests are available:\n    " + item.Name + "\n");
                var assembly = System.Security.SecurityElement.Escape(Path.Combine(directory, paths["test-" + item.Suite]));
                Write(item.Suite + ".trx", $"""
                    <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                    <Times start="2026-10-28T00:00:00Z" finish="2026-10-28T00:00:01Z"/>
                    <TestDefinitions><UnitTest><TestMethod codeBase="{assembly}"/></UnitTest></TestDefinitions>
                    <Results><UnitTestResult testName="{item.Name}" outcome="Passed"/></Results>
                    <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0"/></ResultSummary>
                    </TestRun>
                    """);
            }
            Check("complete S1 check CLI", true, () => MacosProgress(["check", run, sha]));
            void Broken(string name, string file, string? replacement)
            {
                var original = File.ReadAllText(Path.Combine(run, file));
                if (replacement is null) File.Delete(Path.Combine(run, file)); else Write(file, replacement);
                Check(name, false, () => MacosProgress(["check", run, sha]));
                Write(file, original);
            }
            Broken("header/fixture mismatch", "stage-fixture.stdout.log", s0);
            Broken("missing build", "build-before.json", null);
            Broken("changed binaries", "build-after.json", "{}");
            Broken("wrong fixture source", "stage-fixture.command.json", JsonSerializer.Serialize(new { executable = "git", root = directory, args = new[] { "show", "other:" + ProgressFixture } }));
            Broken("missing check", "fixture-check.exit", null);
            Broken("nonzero check", "fixture-check.exit", "1");
            Broken("timeout check", "fixture-check.exit", "124");
            Broken("not a fixturecheck", "fixture-check.stdout.log", "PASS");
            Broken("wrong new-run source", "build-before.json", JsonSerializer.Serialize(build with { SourceCommit = new string('f', 40) }));
            Broken("wrong executable test image", "Core.trx", File.ReadAllText(Path.Combine(run, "Core.trx")).Replace("Tests.dll", "Tests.stale.dll", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Progress stage self-tests: {count} fixture/CLI/source/binary/capture/identity checks.");
    }
}
