using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private const string EvidenceFile = ".config/qualification-evidence.json";
    private const string PlatformFixtureDirectory = "tests/FTPlus.AgentGraphicDesign.Core.Tests/Fixtures/platform";
    private const string GateAgreementTestFile = "tests/FTPlus.AgentGraphicDesign.Core.Tests/Platform/PlatformGateAgreementTests.cs";
    private const string GateAgreementTestMethod = "HostGates_AgreeWithTheRegistry";
    private const string NarrowingSeamOwner = "src/FTPlus.AgentGraphicDesign.Core/Platform/PlatformProfile.cs";
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);
    private static readonly string[] KnownCapabilities = ["vector", "rasterExport", "assetPipeline", "artifactVerify", "stillImport", "nativePixels"];
    private static readonly (string Name, OSPlatform Os, Architecture Architecture, Version Runtime, Version OsVersion)[] PlatformFixtureTuples =
    [
        ("dotnet10-linux-x64", OSPlatform.Linux, Architecture.X64, new(10, 0, 0), new(6, 8)),
        ("dotnet10-linux-arm64", OSPlatform.Linux, Architecture.Arm64, new(10, 0, 0), new(6, 8)),
        ("dotnet10-osx-arm64-26.0", OSPlatform.OSX, Architecture.Arm64, new(10, 0, 0), new(26, 0)),
        ("dotnet10-osx-arm64-15.7", OSPlatform.OSX, Architecture.Arm64, new(10, 0, 0), new(15, 7)),
        ("dotnet10-osx-x64-26.0", OSPlatform.OSX, Architecture.X64, new(10, 0, 0), new(26, 0)),
        ("dotnet10-windows-x64", OSPlatform.Windows, Architecture.X64, new(10, 0, 0), new(10, 0)),
        ("dotnet10-windows-arm64", OSPlatform.Windows, Architecture.Arm64, new(10, 0, 0), new(10, 0)),
        ("dotnet9-linux-x64", OSPlatform.Linux, Architecture.X64, new(9, 0, 0), new(6, 8)),
    ];

    private static int CheckQualificationEvidence(string root) => Report(ValidateQualificationEvidence(root),
        "Qualification evidence index passed: six Linux S0 pairs bind their own selected public attestations; linked executions still require independent review.");

    private static List<string> ValidateQualificationEvidence(string root)
    {
        var errors = new List<string>();
        var file = Path.Combine(root, EvidenceFile);
        if (!File.Exists(file)) return [$"{EvidenceFile} is missing"];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowDuplicateProperties = false });
            var evidence = doc.RootElement;
            if (evidence.GetProperty("version").GetInt32() != 1) return ["Evidence version must be 1"];
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in evidence.GetProperty("entries").EnumerateArray())
            {
                var profile = RequiredString(entry, "profile");
                var capability = RequiredString(entry, "capability");
                var kind = RequiredString(entry, "kind");
                var path = RequiredString(entry, "path");
                var id = RequiredString(entry, "attestationId");
                var source = RequiredString(entry, "sourceCommit");
                var label = $"{profile}/{capability}";
                if (!KnownCapabilities.Contains(capability, StringComparer.Ordinal)) errors.Add($"{label}: unknown capability");
                if (!pairs.Add(profile + "/" + capability)) errors.Add($"{label}: duplicate pair");
                if (!ids.Add(id)) errors.Add($"{label}: duplicate attestation id");
                sources.Add(source);
                if (kind != "pre-d10") errors.Add($"{label}: {(kind == "d10" ? "d10 is deferred to 008/T015" : "unknown kind")}; failing closed");
                if (profile != "dotnet10-linux-x64") errors.Add($"{label}: pre-d10 is Linux x64 only");
                if (!IsFullCommit(source)) errors.Add($"{label}: sourceCommit pending or malformed; expected 40 lowercase hex characters");
                if (path != "specs/records.md") { errors.Add($"{label}: invalid path; expected specs/records.md"); continue; }
                if (!Regex.IsMatch(id, @"^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant, PatternTimeout))
                { errors.Add($"{label}: malformed attestation id"); continue; }
                var record = Path.Combine(root, path);
                if (!File.Exists(record)) { errors.Add($"{label}: cited record is missing"); continue; }
                var selected = File.ReadLines(record).Where(l => l.StartsWith("ATTEST " + id + " ", StringComparison.Ordinal)).ToArray();
                if (selected.Length != 1) { errors.Add($"{label}: expected one selected attestation {id}, found {selected.Length}"); continue; }
                var line = selected[0];
                var fields = AttestationFields(line);
                foreach (var (key, value) in new[] { ("what", "platform-qualification"), ("profile", profile), ("capability", capability),
                    ("sourceCommit", source), ("version", source), ("env", profile), ("result", "PASS") })
                    if (fields.GetValueOrDefault(key) != value) errors.Add($"{label}: selected record {key} mismatch (expected {value})");
                if (fields.GetValueOrDefault("by") is not ("agent" or "owner")) errors.Add($"{label}: selected record needs by=agent or owner");
                if (!fields.TryGetValue("evaluator", out var evaluator) || !UsableValue(evaluator)) errors.Add($"{label}: selected record needs evaluator");
                if (!fields.TryGetValue("date", out var date) || !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) errors.Add($"{label}: selected record needs date");
                foreach (var key in new[] { "sourceEvidence", "installedEvidence", "reviewEvidence" })
                    if (!fields.TryGetValue(key, out var link) || !EvidenceLink(root, link)) errors.Add($"{label}: selected record needs valid {key}");
                if (capability == "stillImport")
                {
                    foreach (var key in new[] { "ac01", "ac02", "ac03" })
                        if (fields.GetValueOrDefault(key) != "PASS") errors.Add($"{label}: selected record needs {key}=PASS");
                    if (fields.GetValueOrDefault("ac04") != "PREMERGE_PROOF_PASS")
                        errors.Add($"{label}: selected record needs ac04=PREMERGE_PROOF_PASS; final integration remains independently required");
                }
                // Only the selected record is evaluated. Unrelated historical FAIL records do not poison it.
                if (line.Contains("Failed!", StringComparison.Ordinal) || Regex.IsMatch(line, @"\|\s*FAIL\s*\|", RegexOptions.CultureInvariant, PatternTimeout))
                    errors.Add($"{label}: selected record contains a failed test line or FAIL result column");
                var patterns = entry.GetProperty("passPatterns").EnumerateArray().Select(p =>
                    p.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.GetString()) ? p.GetString()!
                    : throw new InvalidOperationException("passPatterns must contain non-empty strings")).ToArray();
                if (patterns.Length == 0) errors.Add($"{label}: passPatterns must be non-empty");
                foreach (var pattern in patterns)
                {
                    if (!pattern.StartsWith('^') || !pattern.EndsWith('$')) { errors.Add($"{label}: pattern must be whole-line anchored"); continue; }
                    var matched = Regex.Match(line, pattern, RegexOptions.CultureInvariant, PatternTimeout);
                    if (!matched.Success || matched.Index != 0 || matched.Length != line.Length) errors.Add($"{label}: unmatched whole-line passPattern");
                }
            }
            if (!pairs.SetEquals(KnownCapabilities.Select(c => "dotnet10-linux-x64/" + c))) errors.Add("Evidence pairs must equal exactly the six qualified S0 pairs (missing/extra pair)");
            if (sources.Count != 1) errors.Add("All six pairs must name one exact evaluated source commit");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or RegexMatchTimeoutException)
        { errors.Add($"Malformed qualification evidence: {ex.Message}"); }
        return errors;
    }

    private static Dictionary<string, string> AttestationFields(string line)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var header = line.Split(" — ", 2, StringSplitOptions.None)[0];
        foreach (var token in header.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(2))
        {
            var parts = token.Split('=', 2);
            if (parts.Length != 2 || !fields.TryAdd(parts[0], parts[1])) throw new InvalidOperationException("Malformed or duplicate attestation field");
        }
        return fields;
    }
    private static bool UsableValue(string value) => !string.IsNullOrWhiteSpace(value) && !value.Contains("pending", StringComparison.OrdinalIgnoreCase);
    private static bool EvidenceLink(string root, string value) => UsableValue(value) &&
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com"
        || value.StartsWith(".prdkit/cache/evidence/", StringComparison.Ordinal) && !value.Contains('\\')
            && !value.Split('/').Any(p => p is "" or "." or "..") && File.Exists(Path.Combine(root, value)));
    private static bool IsFullCommit(string value) => value.Length == 40 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static int CheckPlatformRules(string root)
    {
        var errors = new List<string>();
        var test = Path.Combine(root, GateAgreementTestFile);
        if (!File.Exists(test)) errors.Add($"{GateAgreementTestFile} is missing");
        else ValidateGateAgreementAttribute(File.ReadAllText(test), errors);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!relative.Contains("/obj/", StringComparison.Ordinal) && !relative.Contains("/bin/", StringComparison.Ordinal))
                ValidateNarrowingSeamUse(relative, File.ReadAllText(file), errors);
        }
        _ = ReadP0Inventory(root);
        ValidatePlatformP0Workflow(File.ReadAllText(Path.Combine(root, ".github/workflows/ci.yml")), errors);
        return Report(errors, "Platform rules passed: plain unskipped host fact and no product use of the narrowing seam.");
    }

    private static void ValidatePlatformP0Workflow(string text, ICollection<string> errors)
    {
        var match = Regex.Match(text, @"(?ms)^  platform-p0:\r?\n(?<job>.*?)(?=^  [A-Za-z0-9_-]+:|\z)", RegexOptions.CultureInvariant, PatternTimeout);
        var job = match.Groups["job"].Value;
        var timeout = Regex.Match(job, @"(?m)^    timeout-minutes: (?<minutes>[0-9]+)\s*$", RegexOptions.CultureInvariant, PatternTimeout);
        if (!int.TryParse(timeout.Groups["minutes"].Value, out var minutes) || minutes is < 1 or > 35) errors.Add("P0 workflow needs a bounded 1-35 minute job timeout");
        foreach (var required in new[] { "runs-on: macos-26", "timeout-minutes:",
            "ref: ${{ github.event.pull_request.head.sha || github.sha }}", "fetch-depth: 0", "persist-credentials: false",
            "mktemp -d /private/tmp/", "pwd -P", "TMPDIR=", "platform-p0-run", "platform-p0-check", "--macos", "if: always()" })
            if (!job.Contains(required, StringComparison.Ordinal)) errors.Add("P0 workflow missing " + required);
        if (Regex.IsMatch(job, @"(?m)^    (if|continue-on-error):", RegexOptions.CultureInvariant, PatternTimeout)
            || job.Contains("continue-on-error: true", StringComparison.Ordinal)) errors.Add("P0 workflow must not be conditional or advisory");
        var gates = Regex.Match(text, @"(?ms)^  repository-gates:\r?\n(?<job>.*?)(?=^  [A-Za-z0-9_-]+:|\z)", RegexOptions.CultureInvariant, PatternTimeout).Groups["job"].Value;
        foreach (var required in new[] { "fetch-depth: 0", "qualification-evidence .", "platform-rules .", "platform-fixtures . --check" })
            if (!gates.Contains(required, StringComparison.Ordinal)) errors.Add("Repository gates missing " + required);
        foreach (var required in new[] { "    needs: platform-p0", "    if: ${{ always() }}",
            "P0_RESULT: ${{ needs.platform-p0.result }}", "run: test \"$P0_RESULT\" = success" })
            if (!gates.Contains(required, StringComparison.Ordinal)) errors.Add("Required repository gate does not enforce native P0: missing " + required);
    }
    private static void ValidateGateAgreementAttribute(string source, ICollection<string> errors)
    {
        var lines = source.Split('\n').Select(l => l.Trim()).ToArray();
        var method = Array.FindIndex(lines, l => Regex.IsMatch(l, $@"\b(void|Task)\s+{GateAgreementTestMethod}\s*\(", RegexOptions.CultureInvariant, PatternTimeout));
        if (method < 0) { errors.Add("Host gate agreement method missing"); return; }
        var attributes = new List<string>();
        for (var i = method - 1; i >= 0 && lines[i].StartsWith('['); i--) attributes.Add(lines[i]);
        if (attributes.Count != 1 || attributes[0] != "[Fact]") errors.Add("Host gate agreement must carry exactly one plain [Fact]");
    }
    private static void ValidateNarrowingSeamUse(string relative, string source, ICollection<string> errors)
    {
        if (relative != NarrowingSeamOwner && source.Contains("NarrowForTests", StringComparison.Ordinal)) errors.Add($"{relative}: product code uses test-only NarrowForTests");
    }

    private static int GeneratePlatformFixtures(string root, bool checkOnly)
    {
        var assemblyPath = Path.Combine(root, "src/FTPlus.AgentGraphicDesign.Core/bin/Release/net10.0/FTPlus.AgentGraphicDesign.Core.dll");
        var context = new AssemblyLoadContext("agd-platform-fixtures", isCollectible: true);
        try
        {
            var type = context.LoadFromAssemblyPath(assemblyPath).GetType("FTPlus.AgentGraphicDesign.Core.Platform.PlatformProfile", throwOnError: true)!;
            var factory = type.GetMethod("For", [typeof(OSPlatform), typeof(Architecture), typeof(Version), typeof(Version)])!;
            var project = type.GetMethod("ToCapabilitiesJson")!;
            var directory = Path.Combine(root, PlatformFixtureDirectory);
            if (!checkOnly) Directory.CreateDirectory(directory);
            var errors = new List<string>();
            foreach (var tuple in PlatformFixtureTuples)
            {
                var node = (JsonNode)project.Invoke(factory.Invoke(null, [tuple.Os, tuple.Architecture, tuple.Runtime, tuple.OsVersion]), null)!;
                var bytes = Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, IndentSize = 2, NewLine = "\n" }) + "\n");
                var file = Path.Combine(directory, $"platform-{tuple.Name}.json");
                if (!checkOnly) File.WriteAllBytes(file, bytes);
                else if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes)) errors.Add($"{file} differs from registry");
            }
            var expected = PlatformFixtureTuples.Select(t => $"platform-{t.Name}.json").ToHashSet(StringComparer.Ordinal);
            if (!Directory.Exists(directory) || !expected.SetEquals(Directory.EnumerateFiles(directory, "*.json").Select(p => Path.GetFileName(p)))) errors.Add("Platform fixture set mismatch");
            return Report(errors, checkOnly ? "Eight platform fixtures match the registry." : "Eight platform fixtures regenerated explicitly.");
        }
        finally { context.Unload(); }
    }

    private static void RunPlatformSelfTests(ICollection<string> errors)
    {
        var root = Path.Combine(Path.GetTempPath(), "agd-evidence-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".config"));
        Directory.CreateDirectory(Path.Combine(root, "specs"));
        try { RunEvidenceSelfTests(root, errors); }
        finally { Directory.Delete(root, recursive: true); }
        var method = $"public void {GateAgreementTestMethod}()";
        foreach (var (source, passes) in new[] { ($"[Fact]\n{method}", true), ($"[Fact(Skip = \"macOS\")]\n{method}", false),
            ($"[PlatformFact]\n{method}", false), ($"[Fact]\n[Trait(\"Skip\", \"yes\")]\n{method}", false), ($"[Theory]\n{method}", false), ("[Fact]\npublic void Other()", false) })
        {
            var result = new List<string>();
            ValidateGateAgreementAttribute(source, result);
            if (passes != (result.Count == 0)) errors.Add("Platform rule self-test failed: " + source);
        }
        var seam = new List<string>();
        ValidateNarrowingSeamUse(NarrowingSeamOwner, "NarrowForTests", seam);
        ValidateNarrowingSeamUse("src/Other.cs", "no seam", seam);
        if (seam.Count != 0) errors.Add("Allowed seam source was refused");
        ValidateNarrowingSeamUse("src/Other.cs", "NarrowForTests", seam);
        if (seam.Count != 1) errors.Add("Product seam use was not refused");
        const string workflow = """
              platform-p0:
                runs-on: macos-26
                timeout-minutes: 30
                steps:
                  - ref: ${{ github.event.pull_request.head.sha || github.sha }}
                    fetch-depth: 0
                    persist-credentials: false
                  - run: mktemp -d /private/tmp/example; pwd -P; TMPDIR=example
                  - run: platform-p0-run --macos
                  - run: platform-p0-check --macos
                    if: always()
              repository-gates:
                needs: platform-p0
                if: ${{ always() }}
                steps:
                  - fetch-depth: 0
                  - run: qualification-evidence .
                  - run: platform-rules .
                  - run: platform-fixtures . --check
                  - env:
                      P0_RESULT: ${{ needs.platform-p0.result }}
                    run: test "$P0_RESULT" = success
            """;
        foreach (var (name, text, passes) in new[]
        {
            ("required lane", workflow, true),
            ("advisory lane", workflow.Replace("    timeout-minutes: 30", "    continue-on-error: true\n    timeout-minutes: 30", StringComparison.Ordinal), false),
            ("conditional lane", workflow.Replace("    timeout-minutes: 30", "    if: false\n    timeout-minutes: 30", StringComparison.Ordinal), false),
            ("unbounded timeout", workflow.Replace("timeout-minutes: 30", "timeout-minutes: 0", StringComparison.Ordinal), false),
            ("wrong checkout", workflow.Replace("ref: ${{ github.event.pull_request.head.sha || github.sha }}", "ref: main", StringComparison.Ordinal), false),
            ("no physical root", workflow.Replace("mktemp -d /private/tmp/", "mktemp -d /tmp/", StringComparison.Ordinal), false),
            ("shallow gates", workflow.Replace("fetch-depth: 0", "fetch-depth: 1", StringComparison.Ordinal), false),
            ("no completeness check", workflow.Replace("platform-p0-check", "other-command", StringComparison.Ordinal), false),
            ("P0 not a required dependency", workflow.Replace("needs: platform-p0", "needs: unrelated", StringComparison.Ordinal), false),
            ("failed P0 skips required gate", workflow.Replace("if: ${{ always() }}", "if: ${{ success() }}", StringComparison.Ordinal), false),
            ("required gate ignores P0 result", workflow.Replace("run: test \"$P0_RESULT\" = success", "run: true", StringComparison.Ordinal), false)
        })
        {
            var result = new List<string>();
            ValidatePlatformP0Workflow(text, result);
            if (passes != (result.Count == 0)) errors.Add($"P0 workflow self-test '{name}': {string.Join("; ", result)}");
        }
    }

    private static void RunEvidenceSelfTests(string root, ICollection<string> errors)
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        JsonObject Entry(string c) => new()
        {
            ["profile"] = "dotnet10-linux-x64",
            ["capability"] = c,
            ["kind"] = "pre-d10",
            ["path"] = "specs/records.md",
            ["sourceCommit"] = commit,
            ["attestationId"] = "test-" + c.ToLowerInvariant(),
            ["passPatterns"] = new JsonArray("^ATTEST test-" + c.ToLowerInvariant() + " .*$")
        };
        var baseline = new JsonObject { ["version"] = 1, ["entries"] = new JsonArray(KnownCapabilities.Select(c => (JsonNode)Entry(c)).ToArray()) };
        var records = KnownCapabilities.Select(c => $"ATTEST test-{c.ToLowerInvariant()} by=agent what=platform-qualification profile=dotnet10-linux-x64 capability={c} sourceCommit={commit} evaluator=independent-review result=PASS sourceEvidence=https://github.com/example/source installedEvidence=https://github.com/example/installed reviewEvidence=https://github.com/example/reviews env=dotnet10-linux-x64 version={commit} date=2026-10-26{(c == "stillImport" ? " ac01=PASS ac02=PASS ac03=PASS ac04=PREMERGE_PROOF_PASS" : "")} — inspected public proof").ToArray();
        var count = 0;
        void Fixture(string name, Action<JsonObject> change, Func<string[], string[]>? recordChange = null, bool passes = false, bool missingFile = false)
        {
            var doc = (JsonObject)baseline.DeepClone();
            change(doc);
            var file = Path.Combine(root, EvidenceFile);
            File.WriteAllText(file, doc.ToJsonString());
            if (missingFile) File.Delete(file);
            File.WriteAllLines(Path.Combine(root, "specs/records.md"), recordChange?.Invoke(records.ToArray()) ?? records);
            var result = ValidateQualificationEvidence(root);
            if (passes != (result.Count == 0)) errors.Add($"Evidence self-test '{name}' expected {(passes ? "pass" : "rejection")}: {string.Join("; ", result)}");
            count++;
        }
        JsonObject First(JsonObject d) => d["entries"]![0]!.AsObject();
        Fixture("complete six records", _ => { }, passes: true);
        Fixture("unrelated historical FAIL", _ => { }, r => r.Append("ATTEST old result=FAIL — Failed! | FAIL | " + commit).ToArray(), passes: true);
        Fixture("missing evidence", _ => { }, missingFile: true);
        Fixture("missing record", _ => { }, _ => []);
        Fixture("source mismatch", _ => { }, r => r.Select(l => l.Replace(commit, new string('f', 40), StringComparison.Ordinal)).ToArray());
        Fixture("SHA from another record", _ => { }, r => { r[0] = r[0].Replace(commit, new string('f', 40), StringComparison.Ordinal); return r; });
        Fixture("swapped pair", d => First(d)["attestationId"] = "test-rasterexport");
        Fixture("FAIL with PASS prose", _ => { }, r => { r[0] = r[0].Replace("result=PASS", "result=FAIL", StringComparison.Ordinal) + " PASS"; return r; });
        Fixture("FAIL result column", _ => { }, r => { r[0] += " | FAIL | PASS"; return r; });
        Fixture("failed test line", _ => { }, r => { r[0] += " Failed! - Failed: 1"; return r; });
        Fixture("unanchored", d => First(d)["passPatterns"] = new JsonArray("PASS"));
        Fixture("unmatched", d => First(d)["passPatterns"] = new JsonArray("^not-this-record$"));
        Fixture("anchor alternation cannot match a prefix", d => First(d)["passPatterns"] = new JsonArray("^|PASS$"));
        Fixture("null pattern", d => First(d)["passPatterns"] = new JsonArray((JsonNode?)null));
        Fixture("invalid regex", d => First(d)["passPatterns"] = new JsonArray("^[$"));
        Fixture("unknown kind", d => First(d)["kind"] = "manual");
        Fixture("d10 refused", d => First(d)["kind"] = "d10");
        Fixture("non-Linux pre-d10", d => First(d)["profile"] = "dotnet10-osx-arm64");
        Fixture("malformed source", d => First(d)["sourceCommit"] = "PENDING");
        Fixture("invalid path", d => First(d)["path"] = "specs/../records.md");
        Fixture("absolute path", d => First(d)["path"] = "/specs/records.md");
        Fixture("malformed pair", d => First(d)["capability"] = 3);
        Fixture("unknown capability", d => First(d)["capability"] = "pixels");
        Fixture("duplicate pair", d => d["entries"]!.AsArray().Add(First(d).DeepClone()));
        Fixture("missing pair", d => d["entries"]!.AsArray().RemoveAt(0));
        Fixture("extra pair", d => { var e = Entry("vector"); e["profile"] = "dotnet10-windows-x64"; d["entries"]!.AsArray().Add(e); });
        Fixture("missing evaluator", _ => { }, r => { r[0] = r[0].Replace(" evaluator=independent-review", "", StringComparison.Ordinal); return r; });
        Fixture("missing installed evidence", _ => { }, r => { r[0] = r[0].Replace("installedEvidence=https://github.com/example/installed", "installedEvidence=PENDING", StringComparison.Ordinal); return r; });
        Fixture("still-import incomplete", _ => { }, r => r.Select(l => l.Replace("ac04=PREMERGE_PROOF_PASS", "ac04=NOT_RUN", StringComparison.Ordinal)).ToArray());
        Fixture("still-import generic PASS is not premerge proof", _ => { }, r => r.Select(l => l.Replace("ac04=PREMERGE_PROOF_PASS", "ac04=PASS", StringComparison.Ordinal)).ToArray());
        Fixture("still-import missing premerge proof", _ => { }, r => r.Select(l => l.Replace(" ac04=PREMERGE_PROOF_PASS", "", StringComparison.Ordinal)).ToArray());
        Fixture("still-import pending premerge proof", _ => { }, r => r.Select(l => l.Replace("ac04=PREMERGE_PROOF_PASS", "ac04=PENDING", StringComparison.Ordinal)).ToArray());
        Fixture("still-import unknown premerge proof", _ => { }, r => r.Select(l => l.Replace("ac04=PREMERGE_PROOF_PASS", "ac04=COMPLETE", StringComparison.Ordinal)).ToArray());
        Fixture("still-import failed premerge proof with PASS prose", _ => { }, r => r.Select(l => l.Replace("ac04=PREMERGE_PROOF_PASS", "ac04=FAIL", StringComparison.Ordinal) + " PASS PREMERGE_PROOF_PASS").ToArray());
        foreach (var key in new[] { "ac01", "ac02", "ac03" })
            Fixture($"still-import {key} still requires runtime PASS", _ => { }, r => r.Select(l => l.Replace($"{key}=PASS", $"{key}=PREMERGE_PROOF_PASS", StringComparison.Ordinal)).ToArray());
        Fixture("duplicate selected line", _ => { }, r => r.Append(r[0]).ToArray());
        Console.WriteLine($"Qualification evidence self-tests: {count} synthetic positive/negative fixtures.");
    }
}
