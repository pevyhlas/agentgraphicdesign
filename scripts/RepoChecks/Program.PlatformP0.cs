using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private const string P0Inventory = ".config/platform-p0-tests.json";
    private sealed record P0Project(string Id, string Project, string Filter, Dictionary<string, int> Methods);
    private static readonly string[] P0Checks = ["self-test", "qualification-evidence", "platform-rules", "platform-fixtures"];

    private static P0Project[] ReadP0Inventory(string root)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, P0Inventory)), new JsonDocumentOptions { AllowDuplicateProperties = false });
        if (doc.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("P0 inventory version must be 1");
        var projects = new List<P0Project>();
        foreach (var item in doc.RootElement.GetProperty("projects").EnumerateArray())
        {
            var id = RequiredString(item, "id");
            var path = RequiredString(item, "project");
            var ns = RequiredString(item, "namespace");
            var expectedNs = id switch { "core" => "FTPlus.AgentGraphicDesign.Core.Tests", "cli" => "FTPlus.AgentGraphicDesign.Cli.Tests", "mcp" => "FTPlus.AgentGraphicDesign.Mcp.Tests", _ => throw new InvalidDataException("Unknown P0 project") };
            if (ns != expectedNs || path != $"tests/{ns}/{ns}.csproj") throw new InvalidDataException("Unexpected P0 project path or namespace");
            var methods = item.GetProperty("methods").EnumerateObject().ToDictionary(p => ns + "." + p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
            if (methods.Count == 0 || methods.Any(p => p.Value <= 0 || !System.Text.RegularExpressions.Regex.IsMatch(p.Key, @"^[A-Za-z0-9_.]+$"))) throw new InvalidDataException("P0 methods must have explicit positive case counts");
            var classes = item.GetProperty("wholeClasses").EnumerateArray().Select(c => ns + "." + c.GetString()).ToArray();
            if (classes.Distinct().Count() != classes.Length || classes.Any(c => !methods.Keys.Any(m => m.StartsWith(c + ".", StringComparison.Ordinal)))) throw new InvalidDataException("P0 whole classes must have inventoried methods");
            string[] requiredClasses = id switch
            {
                "core" => ["Platform.PlatformRegistryTests", "Platform.PlatformArchitectureTests", "Platform.PlatformAssetEntryPointRefusalTests", "Platform.PlatformArtifactVerifyRefusalTests", "Platform.PlatformPixelEntryPointRefusalTests"],
                "cli" => ["Commands.PlatformGateCommandTests"],
                _ => []
            };
            string[] requiredPartial = id switch
            {
                "core" => ["Platform.PlatformGateAgreementTests.HostGates_AgreeWithTheRegistry", "Assets.AssetPipelineTests.PlatformGuard_FollowsTheRegistryGate"],
                "mcp" => ["ProtocolTests.GetCapabilities_PlatformMemberEqualsTheRegistryAndReplacesTheOldFields", "ProtocolTests.PreflightArtifact_HostGateAndRefusalTextAgreeWithTheRegistry", "ProtocolTests.ToolResultFail_PassesEveryRegistryRefusalTextThroughUnchanged"],
                _ => []
            };
            if (!classes.ToHashSet(StringComparer.Ordinal).SetEquals(requiredClasses.Select(c => ns + "." + c))
                || requiredPartial.Any(m => !methods.ContainsKey(ns + "." + m))) throw new InvalidDataException("P0 inventory reduced the required original008/T002 scope");
            var filter = string.Join('|', classes.Select(c => "FullyQualifiedName~" + c + ".").Concat(methods.Keys
                .Where(m => !classes.Any(c => m.StartsWith(c + ".", StringComparison.Ordinal))).Select(m => "FullyQualifiedName=" + m)));
            projects.Add(new(id, path, filter, methods));
        }
        if (projects.Count != 3 || !projects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal).SetEquals(["core", "cli", "mcp"])) throw new InvalidDataException("P0 requires Core, CLI and MCP exactly once");
        if (!doc.RootElement.GetProperty("repositoryChecks").EnumerateArray().Select(v => v.GetString()).SequenceEqual(P0Checks)) throw new InvalidDataException("P0 repository checks incomplete");
        return projects.ToArray();
    }

    // Every subprocess retains its exact argv, exit code, stdout and stderr; no shell or pipe hides failure.
    private static (int Exit, string Output) P0Command(string root, string directory, string label, string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        File.WriteAllText(Path.Combine(directory, label + ".command.json"), JsonSerializer.Serialize(new { executable, args }));
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(600_000);
        if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        Task.WaitAll(stdout, stderr);
        File.WriteAllText(Path.Combine(directory, label + ".stdout.log"), stdout.Result);
        File.WriteAllText(Path.Combine(directory, label + ".stderr.log"), stderr.Result);
        var exit = exited ? process.ExitCode : 124;
        File.WriteAllText(Path.Combine(directory, label + ".exit"), exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine($"P0 {label}: exit {exit}");
        return (exit, stdout.Result);
    }

    private static int RunPlatformP0(string root, string directory, string expectedSha, bool macos)
    {
        if (!IsFullCommit(expectedSha)) throw new ArgumentException("P0 expected head must be a full lowercase SHA");
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory)) throw new InvalidOperationException("P0 output must be a fresh directory; preserve previous attempts");
        Directory.CreateDirectory(directory);
        var report = new JsonObject
        {
            ["status"] = "RUNNING",
            ["expectedSha"] = expectedSha,
            ["macosRequired"] = macos,
            ["scope"] = macos ? "native-p0" : "diagnostic-working-tree",
            ["runtime"] = Environment.Version.ToString(),
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["osVersion"] = Environment.OSVersion.Version.ToString(),
            ["osDescription"] = RuntimeInformation.OSDescription,
            ["os"] = OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : "other",
            ["tempRoot"] = Path.GetTempPath(),
            ["inheritedTmpdir"] = Environment.GetEnvironmentVariable("TMPDIR"),
            ["inventorySha256"] = Sha256Hex(File.ReadAllBytes(Path.Combine(root, P0Inventory)))
        };
        var reportFile = Path.Combine(directory, "run.json");
        File.WriteAllText(reportFile, report.ToJsonString());
        var errors = new List<string>();
        try
        {
            var git = P0Command(root, directory, "head", "git", "rev-parse", "HEAD");
            report["sourceCommit"] = git.Output.Trim();
            if (git.Exit != 0 || git.Output.Trim() != expectedSha) throw new InvalidDataException("Checked-out SHA differs from expected PR head");
            var state = P0Command(root, directory, "source-state", "git", "status", "--porcelain", "--untracked-files=no");
            report["trackedSourceClean"] = state.Exit == 0 && string.IsNullOrWhiteSpace(state.Output);
            if (state.Exit != 0 || macos && !string.IsNullOrWhiteSpace(state.Output)) throw new InvalidDataException("Native P0 requires a clean checked-out head");
            ValidateP0Environment(report, errors);
            if (errors.Count != 0) throw new InvalidDataException(string.Join("; ", errors));
            foreach (var label in new[] { "sdk", "dotnet-info" })
                if (P0Command(root, directory, label, "dotnet", label == "sdk" ? "--version" : "--info").Exit != 0) errors.Add("Cannot record SDK identity");
            if (macos)
            {
                if (P0Command(root, directory, "sw-vers", "/usr/bin/sw_vers").Exit != 0) errors.Add("Cannot record macOS identity");
                if (P0Command(root, directory, "uname", "/usr/bin/uname", "-m").Output.Trim() != "arm64") errors.Add("macOS kernel architecture differs");
            }
            foreach (var project in ReadP0Inventory(root))
            {
                var common = new[] { "test", project.Project, "--configuration", "Release", "--no-build", "--no-restore", "--filter", project.Filter };
                var discovery = P0Command(root, directory, project.Id + "-discovery", "dotnet", [.. common, "--list-tests"]);
                if (discovery.Exit != 0) errors.Add(project.Id + " discovery failed");
                var discovered = DiscoveryIdentities(discovery.Output);
                File.WriteAllLines(Path.Combine(directory, project.Id + ".discovered.txt"), discovered);
                ValidateP0Identities(project, discovered, "discovery", errors);
                var execution = P0Command(root, directory, project.Id + "-tests", "dotnet", [.. common, "--results-directory", directory,
                    "--logger", $"trx;LogFileName={project.Id}.trx", "--", "RunConfiguration.TreatNoTestsAsError=true"]);
                if (execution.Exit != 0) errors.Add(project.Id + " tests failed");
                ValidateP0Trx(project, Path.Combine(directory, project.Id + ".trx"), discovered, errors);
            }
            foreach (var check in P0Checks)
            {
                var args = new[] { typeof(Program).Assembly.Location, check };
                if (check != "self-test") args = [.. args, root];
                if (check == "platform-fixtures") args = [.. args, "--check"];
                if (P0Command(root, directory, check, "dotnet", args).Exit != 0) errors.Add(check + " failed");
            }
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        report["status"] = errors.Count == 0 ? "PASS" : "FAIL";
        report["errors"] = new JsonArray(errors.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray());
        File.WriteAllText(reportFile, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return Report(errors, "P0 selected tests and repository commands passed; this is not macOS platform qualification.");
    }

    private static string[] DiscoveryIdentities(string output) => output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("FTPlus.AgentGraphicDesign.", StringComparison.Ordinal)).ToArray();
    private static string MethodIdentity(string identity) => identity.Split('(', 2)[0];
    private static void ValidateP0Identities(P0Project project, string[] identities, string phase, ICollection<string> errors)
    {
        if (identities.Length == 0 || identities.Distinct(StringComparer.Ordinal).Count() != identities.Length) errors.Add($"{project.Id}: {phase} zero or duplicate identities");
        var counts = identities.GroupBy(MethodIdentity).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var (method, expected) in project.Methods)
            if (counts.GetValueOrDefault(method) != expected) errors.Add($"{project.Id}: {phase} {method}: expected {expected}, got {counts.GetValueOrDefault(method)}");
        foreach (var extra in counts.Keys.Except(project.Methods.Keys)) errors.Add($"{project.Id}: {phase} uninventoried method {extra}");
    }

    private static void ValidateP0Trx(P0Project project, string path, string[] discovered, ICollection<string> errors)
    {
        if (!File.Exists(path)) { errors.Add(project.Id + ": missing TRX"); return; }
        var doc = XDocument.Load(path);
        var ns = doc.Root!.Name.Namespace;
        var results = doc.Descendants(ns + "UnitTestResult").ToArray();
        var executed = results.Select(r => r.Attribute("testName")?.Value ?? "").ToArray();
        ValidateP0Identities(project, executed, "execution", errors);
        if (!discovered.Order(StringComparer.Ordinal).SequenceEqual(executed.Order(StringComparer.Ordinal))) errors.Add(project.Id + ": discovered/executed case identities differ");
        if (results.Any(r => r.Attribute("outcome")?.Value != "Passed")) errors.Add(project.Id + ": failed, skipped or not-executed result");
        var counters = doc.Descendants(ns + "Counters").SingleOrDefault();
        foreach (var key in new[] { "total", "executed", "passed" })
            if (counters?.Attribute(key)?.Value != results.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)) errors.Add(project.Id + ": incomplete TRX " + key);
        foreach (var counter in counters?.Attributes() ?? [])
            if (counter.Name.LocalName is not ("total" or "executed" or "passed") && counter.Value != "0") errors.Add(project.Id + ": nonzero TRX " + counter.Name.LocalName);
        if (doc.Descendants(ns + "ResultSummary").SingleOrDefault()?.Attribute("outcome")?.Value != "Completed") errors.Add(project.Id + ": TRX did not complete");
    }

    private static void ValidateP0Environment(JsonObject report, ICollection<string> errors)
    {
        if (!Version.TryParse(report["runtime"]?.GetValue<string>(), out var runtime) || runtime.Major != 10) errors.Add("P0 requires runtime 10");
        if (report["macosRequired"]!.GetValue<bool>())
        {
            if (report["os"]?.GetValue<string>() != "osx" || report["architecture"]?.GetValue<string>() != "Arm64"
                || !Version.TryParse(report["osVersion"]?.GetValue<string>(), out var os) || os.Major != 26) errors.Add("P0 requires macOS 26 arm64 process");
            var temp = report["tempRoot"]!.GetValue<string>();
            var inherited = report["inheritedTmpdir"]?.GetValue<string>();
            if (!temp.StartsWith("/private/tmp/", StringComparison.Ordinal) || Path.TrimEndingDirectorySeparator(temp) != Path.TrimEndingDirectorySeparator(inherited ?? "")) errors.Add("P0 must inherit its real /private/tmp root");
            for (var current = new DirectoryInfo(temp); current is not null; current = current.Parent)
                if (!current.Exists || current.LinkTarget is not null) errors.Add("P0 temporary root is not a physical directory");
            if (OperatingSystem.IsMacOS() && Directory.Exists(temp) && (File.GetUnixFileMode(temp) &
                (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                errors.Add("P0 temporary root must be private to its user");
        }
    }

    private static int CheckPlatformP0(string root, string directory, string expectedSha, bool macos)
    {
        var errors = new List<string>();
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "run.json")))!.AsObject();
        ValidateP0RunHeader(report, expectedSha, macos, Sha256Hex(File.ReadAllBytes(Path.Combine(root, P0Inventory))), errors);
        var head = P0Command(root, directory, "check-head", "git", "rev-parse", "HEAD");
        if (head.Exit != 0 || head.Output.Trim() != expectedSha) errors.Add("P0 checkout is not the expected PR head");
        ValidateP0Environment(report, errors);
        foreach (var project in ReadP0Inventory(root))
        {
            foreach (var phase in new[] { "discovery", "tests" })
                if (File.ReadAllText(Path.Combine(directory, project.Id + "-" + phase + ".exit")).Trim() != "0") errors.Add(project.Id + " " + phase + " failed");
            var discovered = DiscoveryIdentities(File.ReadAllText(Path.Combine(directory, project.Id + "-discovery.stdout.log")));
            ValidateP0Identities(project, discovered, "discovery", errors);
            ValidateP0Trx(project, Path.Combine(directory, project.Id + ".trx"), discovered, errors);
        }
        foreach (var command in P0Checks.Concat(["sdk", "dotnet-info", "head", "source-state"]).Concat(macos ? ["sw-vers", "uname"] : Array.Empty<string>()))
            if (File.ReadAllText(Path.Combine(directory, command + ".exit")).Trim() != "0") errors.Add(command + " did not succeed");
        return Report(errors, "P0 evidence complete: every selected method/case executed, zero failures/skips/not-executed, commands successful at the expected head.");
    }

    private static void ValidateP0RunHeader(JsonObject report, string expectedSha, bool macos, string inventoryHash, ICollection<string> errors)
    {
        if (report["status"]?.GetValue<string>() != "PASS" || report["errors"] is not JsonArray { Count: 0 }) errors.Add("P0 run failed, was cancelled, timed out or never completed");
        if (!IsFullCommit(expectedSha) || report["expectedSha"]?.GetValue<string>() != expectedSha || report["sourceCommit"]?.GetValue<string>() != expectedSha) errors.Add("P0 head mismatch");
        if (report["macosRequired"]?.GetValue<bool>() != macos) errors.Add("P0 root mode mismatch");
        if (macos && report["trackedSourceClean"]?.GetValue<bool>() != true) errors.Add("P0 was not run on clean committed source");
        if (report["inventorySha256"]?.GetValue<string>() != inventoryHash) errors.Add("P0 inventory changed");
    }

    private static void RunP0SelfTests(ICollection<string> errors)
    {
        var project = new P0Project("fixture", "fixture.csproj", "", new(StringComparer.Ordinal) { ["Example.Test"] = 2 });
        var count = 0;
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        foreach (var (name, change, passes) in new (string, Action<JsonObject>, bool)[]
        {
            ("complete header", _ => { }, true),
            ("cancelled before completion", r => r["status"] = "RUNNING", false),
            ("failed or timed out", r => r["status"] = "FAIL", false),
            ("error despite PASS", r => r["errors"] = new JsonArray("timeout"), false),
            ("wrong source", r => r["sourceCommit"] = new string('f', 40), false),
            ("changed inventory", r => r["inventorySha256"] = "different", false),
            ("wrong root mode", r => r["macosRequired"] = false, false),
            ("uncommitted source", r => r["trackedSourceClean"] = false, false)
        })
        {
            var report = new JsonObject
            {
                ["status"] = "PASS",
                ["errors"] = new JsonArray(),
                ["expectedSha"] = sha,
                ["sourceCommit"] = sha,
                ["inventorySha256"] = "fixture-hash",
                ["macosRequired"] = true,
                ["trackedSourceClean"] = true
            };
            change(report);
            var result = new List<string>();
            ValidateP0RunHeader(report, sha, true, "fixture-hash", result);
            if (passes != (result.Count == 0)) errors.Add("P0 header self-test failed: " + name);
            count++;
        }
        foreach (var (name, identities, passes) in new (string, string[], bool)[]
        {
            ("complete", ["Example.Test(x: 1)", "Example.Test(x: 2)"], true), ("zero", [], false),
            ("missing case", ["Example.Test(x: 1)"], false), ("duplicate case", ["Example.Test(x: 1)", "Example.Test(x: 1)"], false),
            ("unknown method", ["Example.Other(x: 1)", "Example.Other(x: 2)"], false)
        })
        {
            var result = new List<string>();
            ValidateP0Identities(project, identities, name, result);
            if (passes != (result.Count == 0)) errors.Add("P0 identity self-test failed: " + name);
            count++;
        }
        var root = Path.Combine(Path.GetTempPath(), "agd-p0-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string valid = "<TestRun><Results><UnitTestResult testName=\"Example.Test(x: 1)\" outcome=\"Passed\"/><UnitTestResult testName=\"Example.Test(x: 2)\" outcome=\"Passed\"/></Results><ResultSummary outcome=\"Completed\"><Counters total=\"2\" executed=\"2\" passed=\"2\" failed=\"0\"/></ResultSummary></TestRun>";
            foreach (var (name, text, passes) in new[]
            {
                ("passed", valid, true),
                ("failed", valid.Replace("outcome=\"Passed\"", "outcome=\"Failed\"", StringComparison.Ordinal), false),
                ("not executed", valid.Replace("outcome=\"Passed\"", "outcome=\"NotExecuted\"", StringComparison.Ordinal), false),
                ("skipped", valid.Replace("outcome=\"Passed\"", "outcome=\"Skipped\"", StringComparison.Ordinal), false),
                ("aborted", valid.Replace("Completed", "Aborted", StringComparison.Ordinal), false),
                ("counter mismatch", valid.Replace("executed=\"2\"", "executed=\"0\"", StringComparison.Ordinal), false),
                ("nonzero skipped counter", valid.Replace("failed=\"0\"", "notExecuted=\"1\"", StringComparison.Ordinal), false),
                ("zero results", "<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"0\" executed=\"0\" passed=\"0\"/></ResultSummary></TestRun>", false)
            })
            {
                var path = Path.Combine(root, "fixture.trx");
                File.WriteAllText(path, text);
                var result = new List<string>();
                ValidateP0Trx(project, path, ["Example.Test(x: 1)", "Example.Test(x: 2)"], result);
                if (passes != (result.Count == 0)) errors.Add("P0 outcome self-test failed: " + name);
                count++;
            }
            var missing = new List<string>();
            ValidateP0Trx(project, Path.Combine(root, "absent.trx"), [], missing);
            if (missing.Count == 0) errors.Add("P0 missing TRX accepted");
        }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine($"P0 self-tests: {count + 1} header/identity/TRX fixtures.");
    }
}
