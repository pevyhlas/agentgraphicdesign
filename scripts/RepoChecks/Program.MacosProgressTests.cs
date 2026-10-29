using System.Text;
using System.Text.Json;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private static void RunProgressSelfTests(ICollection<string> errors)
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        var header = new ProgressHeader(1, sha, "S0", "private-tmp", "dotnet10-osx-arm64-macos26", "10.0.14", "10.0.113", "26.6.2", 0, 0, 0, "");
        var cases = ProgressSuites.Select(s => new ProgressCase(s, $"FTPlus.AgentGraphicDesign.{s}.Tests.Example.Case", "Passed")).ToArray();
        cases[0] = cases[0] with { Outcome = "Failed" };
        var baseline = ParseProgress(SerializeProgress(new(header, cases)), sha, "private-tmp");
        var count = 0;
        void Check(string name, bool passes, Action action)
        {
            count++;
            try { action(); if (!passes) errors.Add("Progress self-test accepted " + name); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or System.Xml.XmlException or InvalidOperationException or ArgumentException or IOException)
            { if (passes) errors.Add("Progress self-test rejected " + name + ": " + ex.Message); }
        }
        var text = SerializeProgress(baseline);
        Check("valid record", true, () => ParseProgress(text, sha, "private-tmp"));
        Check("truncated", false, () => ParseProgress(text[..^1], sha, "private-tmp"));
        Check("TSV BOM refused", false, () => ParseProgress("\uFEFF" + text, sha, "private-tmp"));
        Check("tampered", false, () => ParseProgress(text.Replace("Failed\n", "Passed\n", StringComparison.Ordinal), sha, "private-tmp"));
        Check("wrong source", false, () => ParseProgress(text, new string('f', 40), "private-tmp"));
        Check("wrong mode", false, () => ParseProgress(text, sha, "default-tmpdir"));
        Check("wrong profile", false, () => ParseProgress(text.Replace("osx-arm64", "linux-x64", StringComparison.Ordinal), sha, "private-tmp"));
        Check("wrong count", false, () => ParseProgress(text.Replace("\"Failed\":1", "\"Failed\":2", StringComparison.Ordinal), sha, "private-tmp"));
        Check("duplicate header key", false, () => ParseProgress(text.Replace("\"Version\":1", "\"Version\":1,\"Version\":1", StringComparison.Ordinal), sha, "private-tmp"));
        Check("oversized", false, () => ParseProgress(new string('a', ProgressMaxBytes + 1), sha, "private-tmp"));
        Check("duplicate case", false, () => SerializeProgress(new(header, [.. cases, cases[0]])));
        Check("missing suite", false, () => SerializeProgress(new(header, cases[1..])));
        Check("unknown outcome", false, () => SerializeProgress(new(header, cases.Select(c => c with { Outcome = "Aborted" }).ToArray())));
        var empty = new ProgressOverrides(1, [], []);
        ProgressRecord Current(ProgressCase[] rows) => ParseProgress(SerializeProgress(new(header, rows)), sha, "private-tmp");
        Check("equal", true, () => CompareProgress(baseline, baseline, empty));
        Check("shrinking", true, () => CompareProgress(baseline, Current(cases.Select(c => c with { Outcome = "Passed" }).ToArray()), empty));
        Check("new failure", false, () => CompareProgress(baseline, Current([.. cases, cases[0] with { Name = cases[0].Name + "New" }]), empty));
        Check("failure moved", false, () => CompareProgress(baseline, Current(cases.Select(c => c with { Outcome = c.Suite == "Cli" ? "Failed" : "Passed" }).ToArray()), empty));
        var skipped = cases.Select(c => c.Suite == "Core" ? c with { Outcome = "NotExecuted" } : c).ToArray();
        Check("new skip", false, () => CompareProgress(baseline, Current(skipped), empty));
        var reason = new ProgressReason("private-tmp", "Core", cases[0].Name, "Reviewed platform fixture conversion, 008/T013");
        Check("reasoned skip", true, () => CompareProgress(baseline, Current(skipped), new(1, [reason], [])));
        Check("blank skip reason", false, () => CompareProgress(baseline, Current(skipped), new(1, [reason with { Reason = "" }], [])));
        Check("stale skip reason", false, () => CompareProgress(baseline, baseline, new(1, [reason], [])));
        var otherMode = baseline with { Header = baseline.Header with { Mode = "default-tmpdir" } };
        Check("asymmetric skip both modes", true, () =>
        {
            CompareProgress(baseline, Current(skipped), new(1, [reason], []));
            CompareProgress(otherMode, otherMode, new(1, [reason], []));
        });
        Check("preexisting skip in other mode", true, () =>
        {
            var priorSkip = Current(skipped) with { Header = Current(skipped).Header with { Mode = "default-tmpdir" } };
            CompareProgress(baseline, Current(skipped), new(1, [reason], []));
            CompareProgress(priorSkip, priorSkip, new(1, [reason], []));
        });
        Check("unknown skip mode", false, () => CompareProgress(baseline, Current(skipped), new(1, [reason with { Mode = "both" }], [])));
        Check("duplicate scoped skip", false, () => CompareProgress(baseline, Current(skipped), new(1, [reason, reason], [])));
        var renamed = cases.Select(c => c.Suite == "Core" ? c with { Name = c.Name + "Renamed" } : c).ToArray();
        var rename = new ProgressRename("Core", cases[0].Name, renamed[0].Name, "Reviewed test rename");
        Check("unmapped disappearance", false, () => CompareProgress(baseline, Current(renamed), empty));
        Check("mapped rename", true, () => CompareProgress(baseline, Current(renamed), new(1, [], [rename])));
        Check("duplicate rename", false, () => CompareProgress(baseline, Current(renamed), new(1, [], [rename, rename])));
        Check("blank rename reason", false, () => CompareProgress(baseline, Current(renamed), new(1, [], [rename with { Reason = "" }])));
        Check("different SDK", false, () => CompareProgress(baseline, baseline with { Header = header with { Sdk = "10.0.114" } }, empty));
        var predecessor = new ProgressBaseline("private-tmp", sha, Sha256Hex(Encoding.UTF8.GetBytes(text)), "https://github.com/pevyhlas/agentgraphicdesign/pull/19#issuecomment-1");
        Check("accepted predecessor", true, () => ValidateProgressPredecessor(predecessor, text));
        Check("stale predecessor", false, () => ValidateProgressPredecessor(predecessor with { Sha256 = new string('f', 64) }, text));
        Check("borrowed predecessor source", false, () => ValidateProgressPredecessor(predecessor with { SourceCommit = new string('f', 40) }, text));
        Check("current target base", true, () => ValidateProgressTarget(sha, sha + "\trefs/heads/main\n"));
        Check("moved target base", false, () => ValidateProgressTarget(sha, new string('f', 40) + "\trefs/heads/main\n"));
        Check("wrong target branch", false, () => ValidateProgressTarget(sha, sha + "\trefs/heads/other\n"));
        Check("both baseline modes", true, () => ValidateProgressIndex(new(1, [predecessor, predecessor with { Mode = "default-tmpdir" }])));
        Check("duplicate baseline mode", false, () => ValidateProgressIndex(new(1, [predecessor, predecessor])));
        Check("expiring artifact locator", false, () => ValidateProgressIndex(new(1, [predecessor with { Evidence = "https://github.com/pevyhlas/agentgraphicdesign/actions/runs/1" }, predecessor with { Mode = "default-tmpdir" }])));

        var dir = Path.Combine(Path.GetTempPath(), "agd-progress-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string valid = """
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Times start="2026-10-28T00:00:00Z" finish="2026-10-28T00:00:01Z"/>
                  <Results>
                    <UnitTestResult testName="FTPlus.AgentGraphicDesign.Core.Tests.Example.A(x: 1)" outcome="Passed"/>
                    <UnitTestResult testName="FTPlus.AgentGraphicDesign.Core.Tests.Example.A(x: 2)" outcome="Failed"/>
                    <UnitTestResult testName="FTPlus.AgentGraphicDesign.Core.Tests.Example.B" outcome="NotExecuted"/>
                  </Results>
                  <ResultSummary outcome="Failed"><Counters total="3" executed="2" passed="1" failed="1" notExecuted="0" aborted="0"/></ResultSummary>
                </TestRun>
                """;
            string[] discovery = ["FTPlus.AgentGraphicDesign.Core.Tests.Example.A(x: 1)", "FTPlus.AgentGraphicDesign.Core.Tests.Example.A(x: 2)", "FTPlus.AgentGraphicDesign.Core.Tests.Example.B"];
            var path = Path.Combine(dir, "test.trx");
            var adapterFailure = "<RunInfos><RunInfo outcome=\"Error\"><Text>[xUnit.net 00:00:01.23]     FTPlus.AgentGraphicDesign.Core.Tests.Example.A(x: 2) [FAIL]</Text></RunInfo></RunInfos>";
            var completedWithDiagnostic = valid.Replace("</ResultSummary>", adapterFailure + "</ResultSummary>", StringComparison.Ordinal);
            foreach (var (name, xml, exit, passes) in new[]
            {
                ("completed failed with xunit skips", valid, 1, true),
                ("observed UTF8 XML BOM", "\uFEFF" + valid, 1, true),
                ("observed adapter failure", completedWithDiagnostic, 1, true),
                ("diagnostic unknown case", completedWithDiagnostic.Replace("x: 2) [FAIL]", "x: 3) [FAIL]", StringComparison.Ordinal), 1, false),
                ("diagnostic passed case", completedWithDiagnostic.Replace("x: 2) [FAIL]", "x: 1) [FAIL]", StringComparison.Ordinal), 1, false),
                ("diagnostic skipped case", completedWithDiagnostic.Replace("Example.A(x: 2) [FAIL]", "Example.B [FAIL]", StringComparison.Ordinal), 1, false),
                ("diagnostic partial match", completedWithDiagnostic.Replace("[FAIL]</Text>", "[FAIL] extra</Text>", StringComparison.Ordinal), 1, false),
                ("diagnostic multiline", completedWithDiagnostic.Replace("]     FTPlus", "]\nFTPlus", StringComparison.Ordinal), 1, false),
                ("explicit skipped count", valid.Replace("notExecuted=\"0\"", "notExecuted=\"1\"", StringComparison.Ordinal), 1, true),
                ("all passed", valid.Replace("outcome=\"Failed\"", "outcome=\"Passed\"", StringComparison.Ordinal).Replace("ResultSummary outcome=\"Passed\"", "ResultSummary outcome=\"Completed\"", StringComparison.Ordinal).Replace("passed=\"1\" failed=\"1\"", "passed=\"2\" failed=\"0\"", StringComparison.Ordinal), 0, true),
                ("wrong exit", valid, 0, false), ("timeout", valid, 124, false), ("crash", valid, 139, false),
                ("aborted summary", valid.Replace("ResultSummary outcome=\"Failed\"", "ResultSummary outcome=\"Aborted\"", StringComparison.Ordinal), 1, false),
                ("incomplete counters", valid.Replace("executed=\"2\"", "executed=\"1\"", StringComparison.Ordinal), 1, false),
                ("aborted counter", valid.Replace("aborted=\"0\"", "aborted=\"1\"", StringComparison.Ordinal), 1, false),
                ("unknown case outcome", valid.Replace("NotExecuted", "Timeout", StringComparison.Ordinal), 1, false),
                ("substituted theory case", valid.Replace("x: 2", "x: 3", StringComparison.Ordinal), 1, false),
                ("duplicate theory case", valid.Replace("x: 2", "x: 1", StringComparison.Ordinal), 1, false),
                ("runinfo error", valid.Replace("</ResultSummary>", "<RunInfos><RunInfo outcome=\"Error\"/></RunInfos></ResultSummary>", StringComparison.Ordinal), 1, false),
                ("missing completion", valid.Replace("finish=\"2026-10-28T00:00:01Z\"", "", StringComparison.Ordinal), 1, false),
                ("DTD", "<!DOCTYPE TestRun [<!ENTITY x 'text'>]>" + valid, 1, false), ("truncated XML", valid[..^12], 1, false)
            })
            {
                File.WriteAllText(path, xml);
                Check("TRX " + name, passes, () => ReadProgressTrx(path, "Core", discovery, exit));
            }
            File.WriteAllText(path, valid);
            Check("incomplete discovery", false, () => ReadProgressTrx(path, "Core", discovery[..^1], 1));
            Check("duplicate discovery", false, () => ReadProgressTrx(path, "Core", [.. discovery, discovery[0]], 1));
            Check("absent TRX", false, () => ReadProgressTrx(Path.Combine(dir, "absent.trx"), "Core", discovery, 1));
            using (var file = File.Create(Path.Combine(dir, "huge.trx"))) file.SetLength(ProgressMaxBytes + 1L);
            Check("oversized TRX", false, () => ReadProgressTrx(Path.Combine(dir, "huge.trx"), "Core", discovery, 1));
        }
        finally { Directory.Delete(dir, recursive: true); }
        RunProgressProcessSelfTests(errors);
        RunProgressStageSelfTests(errors);
        Console.WriteLine($"macOS progress self-tests: {count} record/comparison/predecessor/TRX fixtures.");
    }
}
