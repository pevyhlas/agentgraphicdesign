using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private const int ProgressMaxBytes = 32 * 1024 * 1024;
    private const string ProgressIndex = ".config/macos-progress-baseline.json";
    private const string ProgressExceptions = ".config/macos-progress-exceptions.json";
    private static readonly string[] ProgressSuites = ["Core", "Skia", "Cli", "Mcp"];
    private static readonly string[] ProgressModes = ["default-tmpdir", "private-tmp"];
    private static readonly JsonSerializerOptions ProgressJson = new() { PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false, MaxDepth = 16 };
    private sealed record ProgressCase(string Suite, string Name, string Outcome);
    private sealed record ProgressHeader(int Version, string SourceCommit, string Stage, string Mode, string Profile,
        string Runtime, string Sdk, string OsVersion, int Passed, int Failed, int Skipped, string BodySha256);
    private sealed record ProgressRecord(ProgressHeader Header, ProgressCase[] Cases);
    private sealed record ProgressBaseline(string Mode, string SourceCommit, string Sha256, string Evidence);
    private sealed record ProgressIndexData(int Version, ProgressBaseline[] Records);
    private sealed record ProgressReason(string Mode, string Suite, string Name, string Reason);
    private sealed record ProgressRename(string Suite, string From, string To, string Reason);
    private sealed record ProgressOverrides(int Version, ProgressReason[] Skips, ProgressRename[] Renames);

    private static int MacosProgress(string[] args)
    {
        return args switch
        {
            ["run", var root, var output, var sha, var mode] => RunMacosProgress(Path.GetFullPath(root), Path.GetFullPath(output), sha, mode),
            ["check", var directory, var sha] => CheckMacosProgress(directory, sha),
            ["check-trx", var suite, var trx, var discovery, var exit] => CheckProgressTrx(suite, trx, discovery, int.Parse(exit, CultureInfo.InvariantCulture)),
            ["compare", var root, var baseSha, var previous, var current, var headSha] => CompareMacosProgress(Path.GetFullPath(root), baseSha, previous, current, headSha),
            _ => throw new ArgumentException("macos-progress run ROOT FRESH_OUTPUT HEAD_SHA MODE | check OUTPUT HEAD_SHA | compare ROOT ACTUAL_PR_BASE_SHA PREVIOUS_DIR CURRENT_DIR HEAD_SHA")
        };
    }

    // Bounded, strict UTF-8; a size check after an unbounded allocation is not a resource bound.
    private static string ProgressText(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > ProgressMaxBytes) throw new InvalidDataException("Progress input exceeds 32 MiB");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Progress input grew during read");
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static T ProgressDeserialize<T>(string text) => JsonSerializer.Deserialize<T>(text, ProgressJson)
        ?? throw new InvalidDataException("Null progress object");
    private static bool ProgressHash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string CaseKey(ProgressCase c) => c.Suite + "\t" + c.Name;
    private static void ValidateProgressCases(ProgressCase[] cases)
    {
        if (cases.Length is < 4 or > 30000 || !cases.Select(c => c.Suite).ToHashSet(StringComparer.Ordinal).SetEquals(ProgressSuites)
            || cases.Any(c => string.IsNullOrEmpty(c.Name) || c.Name.Length > 16000 || c.Name.Any(char.IsControl)
                || !c.Name.StartsWith($"FTPlus.AgentGraphicDesign.{c.Suite}.Tests.", StringComparison.Ordinal)
                || c.Outcome is not ("Passed" or "Failed" or "NotExecuted"))
            || cases.Select(CaseKey).Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidDataException("Progress requires four nonempty suites, unique bounded identities and known outcomes");
    }

    private static string SerializeProgress(ProgressRecord record)
    {
        ValidateProgressCases(record.Cases);
        var body = string.Concat(record.Cases.OrderBy(CaseKey, StringComparer.Ordinal)
            .Select(c => c.Suite + "\t" + JsonSerializer.Serialize(c.Name) + "\t" + c.Outcome + "\n"));
        var header = record.Header with
        {
            Passed = record.Cases.Count(c => c.Outcome == "Passed"),
            Failed = record.Cases.Count(c => c.Outcome == "Failed"),
            Skipped = record.Cases.Count(c => c.Outcome == "NotExecuted"),
            BodySha256 = Sha256Hex(Encoding.UTF8.GetBytes(body))
        };
        return "#agd-macos-progress\t" + JsonSerializer.Serialize(header) + "\n" + body;
    }

    private static ProgressRecord ParseProgress(string text, string expectedSha, string expectedMode)
    {
        if (Encoding.UTF8.GetByteCount(text) > ProgressMaxBytes || !text.EndsWith('\n') || text.Contains('\r')) throw new InvalidDataException("Truncated, oversized or noncanonical progress record");
        var lines = text.Split('\n');
        const string prefix = "#agd-macos-progress\t";
        if (!lines[0].StartsWith(prefix, StringComparison.Ordinal) || lines.Length > 30003) throw new InvalidDataException("Unknown progress format");
        var h = ProgressDeserialize<ProgressHeader>(lines[0][prefix.Length..]);
        if (h.Version != 1 || !IsFullCommit(expectedSha) || h.SourceCommit != expectedSha || h.Stage != "S0"
            || !ProgressModes.Contains(expectedMode) || h.Mode != expectedMode || h.Profile != "dotnet10-osx-arm64-macos26"
            || !Version.TryParse(h.Runtime, out var runtime) || runtime.Major != 10
            || !Version.TryParse(h.Sdk, out var sdk) || sdk.Major != 10
            || !Version.TryParse(h.OsVersion, out var os) || os.Major != 26 || !ProgressHash(h.BodySha256))
            throw new InvalidDataException("Progress source/stage/mode/profile/toolchain mismatch");
        var cases = lines[1..^1].Select(line =>
        {
            var parts = line.Split('\t');
            if (parts.Length != 3) throw new InvalidDataException("Malformed progress row");
            return new ProgressCase(parts[0], ProgressDeserialize<string>(parts[1]), parts[2]);
        }).ToArray();
        var record = new ProgressRecord(h, cases);
        ValidateProgressCases(cases);
        if (SerializeProgress(record) != text) throw new InvalidDataException("Progress order, counts, checksum or canonical encoding mismatch");
        return record;
    }

    private static ProgressCase[] ReadProgressTrx(string path, string suite, string[] discovery, int exit)
    {
        var text = ProgressText(path);
        if (text.StartsWith('\uFEFF')) text = text[1..]; // Observed VSTest UTF-8 transport BOM, XML only.
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ProgressMaxBytes });
        var doc = XDocument.Load(reader);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        if (doc.Root?.Name != ns + "TestRun") throw new InvalidDataException("Wrong TRX root/namespace");
        var results = doc.Root.Elements(ns + "Results").Single().Elements(ns + "UnitTestResult").ToArray();
        var cases = results.Select(r => new ProgressCase(suite, r.Attribute("testName")?.Value ?? "", r.Attribute("outcome")?.Value ?? "")).ToArray();
        if (discovery.Length is < 1 or > 30000 || discovery.Distinct(StringComparer.Ordinal).Count() != discovery.Length
            || !cases.Select(c => c.Name).Order(StringComparer.Ordinal).SequenceEqual(discovery.Order(StringComparer.Ordinal))
            || cases.Any(c => c.Outcome is not ("Passed" or "Failed" or "NotExecuted"))) throw new InvalidDataException(suite + ": missing, duplicate, substituted or unknown TRX cases");
        var summary = doc.Root.Elements(ns + "ResultSummary").Single();
        var failed = cases.Count(c => c.Outcome == "Failed");
        var passed = cases.Count(c => c.Outcome == "Passed");
        if (exit != (failed > 0 ? 1 : 0) || summary.Attribute("outcome")?.Value != (failed > 0 ? "Failed" : "Completed")
            || summary.Descendants(ns + "RunInfo").Any(r => r.Attribute("outcome")?.Value is not ("Information" or "Warning")))
            throw new InvalidDataException(suite + ": test exit/summary indicates abort, timeout, crash or inconsistent completion");
        var counters = summary.Elements(ns + "Counters").Single();
        foreach (var (name, value) in new[] { ("total", cases.Length), ("executed", passed + failed), ("passed", passed), ("failed", failed) })
            if (counters.Attribute(name)?.Value != value.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException(suite + ": inconsistent " + name);
        foreach (var attr in counters.Attributes())
        {
            if (attr.Name.LocalName is "total" or "executed" or "passed" or "failed") continue;
            // VSTest18/xUnit reports zero here even when NotExecuted case rows exist.
            if (attr.Name.LocalName == "notExecuted" && attr.Value == (cases.Length - passed - failed).ToString(CultureInfo.InvariantCulture)) continue;
            if (attr.Value != "0") throw new InvalidDataException(suite + ": nonzero incomplete counter " + attr.Name);
        }
        var times = doc.Root.Elements(ns + "Times").Single();
        if (!DateTimeOffset.TryParse(times.Attribute("start")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateTimeOffset.TryParse(times.Attribute("finish")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var finish) || finish < start)
            throw new InvalidDataException(suite + ": missing completion times");
        return cases;
    }

    private static int CheckProgressTrx(string suite, string trx, string discovery, int exit)
    {
        if (!ProgressSuites.Contains(suite)) throw new ArgumentException("Unknown suite");
        var cases = ReadProgressTrx(trx, suite, DiscoveryIdentities(ProgressText(discovery)), exit);
        Console.WriteLine($"{suite}: {cases.Length} unique discovery/TRX identities agree. Diagnostic only, no native record.");
        return 0;
    }

    private static int RunMacosProgress(string root, string output, string sha, string mode)
    {
        if (!IsFullCommit(sha) || !ProgressModes.Contains(mode)) throw new ArgumentException("Full head SHA and explicit root mode required");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a fresh output directory; retain previous attempts");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "state"), "RUNNING\n");
        try
        {
            string Required(string label, string exe, params string[] args)
            {
                var result = ProgressCommand(root, output, label, exe, args);
                if (result.Exit != 0) throw new InvalidDataException(label + " failed");
                return result.Output.Trim();
            }
            if (Required("head", "git", "rev-parse", "HEAD") != sha
                || Required("source-state", "git", "status", "--porcelain", "--untracked-files=all").Length != 0)
                throw new InvalidDataException("Progress requires a clean exact source head");
            if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64
                || RuntimeInformation.OSArchitecture != Architecture.Arm64 || Environment.OSVersion.Version.Major != 26 || Environment.Version.Major != 10)
                throw new InvalidDataException("Progress requires native macOS26 arm64 .NET10");
            var temp = Path.GetTempPath();
            var inherited = Environment.GetEnvironmentVariable("TMPDIR");
            if (temp != inherited || !Directory.Exists(temp)) throw new InvalidDataException("Tests must inherit the recorded TMPDIR");
            if (mode == "default-tmpdir")
            {
                if (temp != Required("default-root", "/usr/bin/getconf", "DARWIN_USER_TEMP_DIR")) throw new InvalidDataException("Default mode must use getconf DARWIN_USER_TEMP_DIR verbatim");
            }
            else
            {
                if (!temp.StartsWith("/private/tmp/agd-progress.", StringComparison.Ordinal)) throw new InvalidDataException("Private mode needs an isolated /private/tmp/agd-progress.* root");
                for (var part = new DirectoryInfo(temp); part is not null; part = part.Parent)
                    if (!part.Exists || part.LinkTarget is not null) throw new InvalidDataException("Private root is not physical");
                if ((File.GetUnixFileMode(temp) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                    throw new InvalidDataException("Private root permissions must be 0700");
            }
            var sdk = Required("sdk", "dotnet", "--version");
            Required("dotnet-info", "dotnet", "--info");
            Required("sw-vers", "/usr/bin/sw_vers");
            if (Required("uname", "/usr/bin/uname", "-m") != "arm64") throw new InvalidDataException("Wrong kernel architecture");
            File.WriteAllText(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(new
            {
                sha,
                mode,
                temp,
                inherited,
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.Version.ToString(),
                sdk,
                image = Environment.GetEnvironmentVariable("ImageVersion"),
                imageOs = Environment.GetEnvironmentVariable("ImageOS")
            }));
            var cases = new List<ProgressCase>();
            foreach (var suite in ProgressSuites)
            {
                var name = $"FTPlus.AgentGraphicDesign.{suite}.Tests";
                string[] common = ["test", $"tests/{name}/{name}.csproj", "--configuration", "Release", "--no-build", "--no-restore"];
                var discovery = DiscoveryIdentities(Required(suite + "-discovery", "dotnet", [.. common, "--list-tests"]));
                var execution = ProgressCommand(root, output, suite + "-tests", "dotnet", [.. common, "--results-directory", output,
                    "--logger", $"trx;LogFileName={suite}.trx", "--blame-hang-timeout", "4m", "--blame-hang-dump-type", "none", "--", "RunConfiguration.TreatNoTestsAsError=true"]);
                cases.AddRange(ReadProgressTrx(Path.Combine(output, suite + ".trx"), suite, discovery, execution.Exit));
            }
            if (Required("final-head", "git", "rev-parse", "HEAD") != sha || Required("final-state", "git", "status", "--porcelain", "--untracked-files=all").Length != 0)
                throw new InvalidDataException("Source changed during run");
            var header = new ProgressHeader(1, sha, "S0", mode, "dotnet10-osx-arm64-macos26", Environment.Version.ToString(), sdk, Environment.OSVersion.Version.ToString(), 0, 0, 0, "");
            var record = SerializeProgress(new(header, cases.ToArray()));
            _ = ParseProgress(record, sha, mode);
            File.WriteAllText(Path.Combine(output, mode + ".tsv"), record);
            File.WriteAllText(Path.Combine(output, "state"), "COMPLETE\n");
            Console.WriteLine($"Complete progress: {cases.Count(c => c.Outcome == "Passed")} passed, {cases.Count(c => c.Outcome == "Failed")} failed, {cases.Count(c => c.Outcome == "NotExecuted")} skipped. Not qualification.");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "state"), "INVALID\n" + ex.Message + "\n");
            throw;
        }
    }

    private static int CheckMacosProgress(string directory, string sha)
    {
        if (ProgressText(Path.Combine(directory, "state")) != "COMPLETE\n") throw new InvalidDataException("Incomplete progress run");
        if (!Directory.EnumerateFiles(directory, "*.trx").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(ProgressSuites.Select(s => s + ".trx")))
            throw new InvalidDataException("Missing or extra TRX suite");
        using var env = JsonDocument.Parse(ProgressText(Path.Combine(directory, "environment.json")), new JsonDocumentOptions { AllowDuplicateProperties = false });
        var mode = env.RootElement.GetProperty("mode").GetString()!;
        if (!ProgressModes.Contains(mode) || env.RootElement.GetProperty("sha").GetString() != sha) throw new InvalidDataException("Wrong run identity");
        var record = ParseProgress(ProgressText(Path.Combine(directory, mode + ".tsv")), sha, mode);
        foreach (var label in new[] { "head", "source-state", "sdk", "dotnet-info", "sw-vers", "uname", "final-head", "final-state" }.Concat(mode == "default-tmpdir" ? ["default-root"] : Array.Empty<string>()))
            if (ProgressText(Path.Combine(directory, label + ".exit")).Trim() != "0") throw new InvalidDataException(label + " failed");
        foreach (var label in new[] { "head", "final-head" })
            if (ProgressText(Path.Combine(directory, label + ".stdout.log")).Trim() != sha) throw new InvalidDataException("Wrong checked-out head");
        foreach (var label in new[] { "source-state", "final-state" })
            if (ProgressText(Path.Combine(directory, label + ".stdout.log")).Trim().Length != 0) throw new InvalidDataException("Dirty source");
        if (ProgressText(Path.Combine(directory, "sdk.stdout.log")).Trim() != record.Header.Sdk
            || ProgressText(Path.Combine(directory, "uname.stdout.log")).Trim() != "arm64"
            || env.RootElement.GetProperty("runtime").GetString() != record.Header.Runtime
            || env.RootElement.GetProperty("os").GetString() != record.Header.OsVersion
            || env.RootElement.GetProperty("sdk").GetString() != record.Header.Sdk)
            throw new InvalidDataException("Recorded environment differs from source record");
        var temp = env.RootElement.GetProperty("temp").GetString()!;
        if (temp != env.RootElement.GetProperty("inherited").GetString()
            || (mode == "default-tmpdir" ? temp != ProgressText(Path.Combine(directory, "default-root.stdout.log")).Trim() : !temp.StartsWith("/private/tmp/agd-progress.", StringComparison.Ordinal)))
            throw new InvalidDataException("Recorded root mode differs");
        var cases = new List<ProgressCase>();
        foreach (var suite in ProgressSuites)
        {
            if (ProgressText(Path.Combine(directory, suite + "-discovery.exit")).Trim() != "0") throw new InvalidDataException("Discovery failed");
            var identities = DiscoveryIdentities(ProgressText(Path.Combine(directory, suite + "-discovery.stdout.log")));
            var exit = int.Parse(ProgressText(Path.Combine(directory, suite + "-tests.exit")), CultureInfo.InvariantCulture);
            cases.AddRange(ReadProgressTrx(Path.Combine(directory, suite + ".trx"), suite, identities, exit));
        }
        if (SerializeProgress(new(record.Header, cases.ToArray())) != SerializeProgress(record)) throw new InvalidDataException("TRX differs from progress record");
        Console.WriteLine("Complete discovery/TRX record at " + sha + "; failures remain failures.");
        return 0;
    }

    private static void CompareProgress(ProgressRecord previous, ProgressRecord current, ProgressOverrides exceptions)
    {
        if (previous.Header.Mode != current.Header.Mode || previous.Header.Stage != current.Header.Stage || previous.Header.Profile != current.Header.Profile
            || previous.Header.Sdk != current.Header.Sdk || previous.Header.Runtime != current.Header.Runtime)
            throw new InvalidDataException("Incomparable progress mode/profile/toolchain");
        if (exceptions.Version != 1 || exceptions.Renames is null || exceptions.Skips is null) throw new InvalidDataException("Malformed progress exceptions");
        var old = previous.Cases.ToDictionary(CaseKey, StringComparer.Ordinal);
        var now = current.Cases.ToDictionary(CaseKey, StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rename in exceptions.Renames)
        {
            var from = rename.Suite + "\t" + rename.From;
            var to = rename.Suite + "\t" + rename.To;
            if (!UsableValue(rename.Reason) || !sources.Add(from) || !targets.Add(to) || !old.ContainsKey(from) || now.ContainsKey(from)
                || old.ContainsKey(to) || !now.ContainsKey(to)) throw new InvalidDataException("Rename must be reasoned, current and one-to-one without collisions/chains");
            var value = old[from]; old.Remove(from); old.Add(to, value with { Name = rename.To });
        }
        var skips = new HashSet<string>(StringComparer.Ordinal);
        if (exceptions.Skips.Any(skip => !ProgressModes.Contains(skip.Mode))
            || exceptions.Skips.Select(skip => skip.Mode + "\t" + skip.Suite + "\t" + skip.Name).Distinct(StringComparer.Ordinal).Count() != exceptions.Skips.Length)
            throw new InvalidDataException("Skip exceptions require unique explicit root modes");
        foreach (var skip in exceptions.Skips)
        {
            if (skip.Mode != current.Header.Mode) continue;
            var key = skip.Suite + "\t" + skip.Name;
            if (!UsableValue(skip.Reason) || !skips.Add(key) || !now.TryGetValue(key, out var c) || c.Outcome != "NotExecuted"
                || old.TryGetValue(key, out var p) && p.Outcome == "NotExecuted") throw new InvalidDataException("Skip exception is duplicate, stale, unreasoned or not a new skip");
        }
        if (old.Keys.Except(now.Keys).Any()) throw new InvalidDataException("Previous test disappeared");
        foreach (var (key, value) in now)
        {
            old.TryGetValue(key, out var prior);
            if (value.Outcome == "Failed" && prior?.Outcome != "Failed") throw new InvalidDataException("New failing test: " + key);
            if (value.Outcome == "NotExecuted" && prior?.Outcome != "NotExecuted" && !skips.Contains(key)) throw new InvalidDataException("New skip without reason: " + key);
        }
        if (current.Header.Failed > previous.Header.Failed) throw new InvalidDataException("Failure count grew");
    }

    private static int CompareMacosProgress(string root, string baseSha, string previousDirectory, string currentDirectory, string headSha)
    {
        if (!IsFullCommit(baseSha) || !IsFullCommit(headSha)) throw new ArgumentException("Exact PR base and head required");
        var capture = Path.Combine(root, ".prdkit/cache/evidence/026-macos-progress/compare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(capture);
        string Git(string label, params string[] args)
        {
            var result = ProgressCommand(root, capture, label, "git", ["--no-replace-objects", .. args]);
            if (result.Exit != 0) throw new InvalidDataException("Git identity/index lookup failed: " + label);
            return result.Output;
        }
        if (Git("head", "rev-parse", "HEAD").Trim() != headSha || Git("state", "status", "--porcelain", "--untracked-files=all").Trim().Length != 0)
            throw new InvalidDataException("Compare requires clean exact head");
        ValidateProgressTarget(baseSha, Git("target", "ls-remote", "--exit-code", "https://github.com/pevyhlas/agentgraphicdesign.git", "refs/heads/main"));
        _ = Git("ancestry", "merge-base", "--is-ancestor", baseSha, headSha);
        // Never load the proposed index from the working tree: the accepted target-base index owns comparison.
        var index = ProgressDeserialize<ProgressIndexData>(Git("baseline", "show", baseSha + ":" + ProgressIndex));
        ValidateProgressIndex(index);
        var exceptions = ProgressDeserialize<ProgressOverrides>(ProgressText(Path.Combine(root, ProgressExceptions)));
        foreach (var entry in index.Records)
        {
            var previousText = ProgressText(Path.Combine(previousDirectory, entry.Mode + ".tsv"));
            ValidateProgressPredecessor(entry, previousText);
            var previous = ParseProgress(previousText, entry.SourceCommit, entry.Mode);
            var current = ParseProgress(ProgressText(Path.Combine(currentDirectory, entry.Mode + ".tsv")), headSha, entry.Mode);
            CompareProgress(previous, current, exceptions);
        }
        Console.WriteLine($"Progress comparison passed against accepted index at {baseSha}, head {headSha}. Recheck the live PR base before merge; moved base requires recomparison. Not qualification.");
        return 0;
    }

    private static void ValidateProgressIndex(ProgressIndexData index)
    {
        if (index.Version != 1 || index.Records is not { Length: 2 } || !index.Records.Select(r => r.Mode).ToHashSet(StringComparer.Ordinal).SetEquals(ProgressModes)
            || index.Records.Any(r => !IsFullCommit(r.SourceCommit) || !ProgressHash(r.Sha256)
                || !Uri.TryCreate(r.Evidence, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "github.com"
                || !url.AbsolutePath.StartsWith("/pevyhlas/agentgraphicdesign/pull/", StringComparison.Ordinal) || string.IsNullOrEmpty(url.Fragment)))
            throw new InvalidDataException("Baseline must bind both modes to source/digest and durable PR evidence");
    }
    private static void ValidateProgressPredecessor(ProgressBaseline entry, string text)
    {
        if (Sha256Hex(Encoding.UTF8.GetBytes(text)) != entry.Sha256) throw new InvalidDataException("Wrong/stale predecessor digest");
        _ = ParseProgress(text, entry.SourceCommit, entry.Mode);
    }

    private static void ValidateProgressTarget(string baseSha, string advertised)
    {
        if (advertised.Trim() != baseSha + "\trefs/heads/main") throw new InvalidDataException("Stale/wrong target base; fetch the current PR base and recompute comparison");
    }
}
