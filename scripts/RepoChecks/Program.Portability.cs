using System.Text;
using System.Text.RegularExpressions;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private const string PortabilityInventory = ".config/portability-migrations.json";
    private sealed record PortabilityEntry(string Path, string Rule, string Sha256, string Reason);
    private sealed record PortabilityAllowlist(int Version, PortabilityEntry[] Entries);
    // Deliberately conservative: bind the entire file, not a line number which can silently move.
    // A file with both OS selection and a return needs review even if dataflow makes it harmless.
    // This detects compound/multiline/aliased-condition returns without pretending to parse C#.
    private static string[] PortabilityRules(string path, string text)
    {
        var rules = new List<string>();
        bool Has(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, PatternTimeout);
        if (path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)
            && path != "src/FTPlus.AgentGraphicDesign.Core/Platform/PlatformPaths.cs")
        {
            if (Has(@"\b(?:GetFullPath|GetRelativePath)\s*\(")) rules.Add("canonical-path");
            if (Has(@"\.\s*FullName\b")) rules.Add("fileinfo-fullname");
        }
        if (path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)
            && Has(@"\b(?:OperatingSystem|OSPlatform|IsOSPlatform|PlatformProfile)\b") && Has(@"\breturn\b")) rules.Add("os-return");
        if (path.StartsWith("scripts/", StringComparison.Ordinal) && path.EndsWith(".sh", StringComparison.Ordinal))
        {
            if (Has(@"\b(?:mapfile|readarray)\b|\b(?:declare|local|typeset)\s+-[A-Za-z]*[An]|\$\{[^}\r\n]*[,^]{2}|\bwait\s+-[A-Za-z]*n")) rules.Add("bash32");
            if (Has(@"\breadlink\s+-[A-Za-z]*[ef]|\brealpath\b|\b(?:mv|cp)\s+-[A-Za-z]*T|\bstat\s+-[A-Za-z]*c|\bgrep\s+-[A-Za-z]*P|\bsort\s+-[A-Za-z]*[zV]|\bfind\b[^\r\n]*-printf")
                || Has("\\bsed\\b[^\\r\\n;]*?\\s-i(?:[ \\t]+(?![ \\t]|''(?:\\s|$)|\"\"(?:\\s|$))|[ \\t]*(?=;|\\r?\\n|$))")) rules.Add("bsd-tools");
        }
        return rules.ToArray();
    }

    private static PortabilityEntry[] ScanPortability(string root)
    {
        return new[] { "src", "tests", "scripts" }.SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*", SearchOption.AllDirectories))
            .Select(file => (File: file, Relative: Path.GetRelativePath(root, file).Replace('\\', '/')))
            .Where(p => !p.Relative.Contains("/obj/", StringComparison.Ordinal) && !p.Relative.Contains("/bin/", StringComparison.Ordinal)
                && (p.Relative.EndsWith(".cs", StringComparison.Ordinal) || p.Relative.EndsWith(".sh", StringComparison.Ordinal)))
            .SelectMany(p =>
            {
                var text = ProgressText(p.File);
                return PortabilityRules(p.Relative, text).Select(rule => new PortabilityEntry(p.Relative, rule, Sha256Hex(Encoding.UTF8.GetBytes(text)), ""));
            }).OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Rule, StringComparer.Ordinal).ToArray();
    }

    private static void ValidatePortabilityInventory(PortabilityEntry[] actual, PortabilityAllowlist allowed, bool bashOnly)
    {
        if (allowed.Version != 1 || allowed.Entries is null || allowed.Entries.Length > 1000) throw new InvalidDataException("Malformed portability migration inventory");
        string Key(PortabilityEntry e) => e.Path + "\t" + e.Rule;
        if (allowed.Entries.Select(Key).Distinct(StringComparer.Ordinal).Count() != allowed.Entries.Length
            || allowed.Entries.Any(e => !ProgressHash(e.Sha256) || !UsableValue(e.Reason) || e.Reason.Length < 20 || e.Path.Contains('\\')
                || e.Path.Split('/').Any(p => p is "" or "." or "..") || e.Rule is not ("canonical-path" or "fileinfo-fullname" or "os-return" or "bash32" or "bsd-tools")))
            throw new InvalidDataException("Inventory needs unique exact files, hashes and reviewed migration reasons");
        bool Included(PortabilityEntry e) => !bashOnly || e.Rule is "bash32" or "bsd-tools";
        var expected = allowed.Entries.Where(Included).ToDictionary(Key, StringComparer.Ordinal);
        foreach (var entry in actual.Where(Included))
        {
            if (!expected.Remove(Key(entry), out var previous) || previous.Sha256 != entry.Sha256)
                throw new InvalidDataException($"{entry.Path}: {entry.Rule} is new or changed; migrate it or explicitly review the reasoned inventory update");
        }
        if (expected.Count != 0) throw new InvalidDataException("Stale portability inventory entries must be removed: " + string.Join(", ", expected.Keys));
    }

    private static int CheckPortabilityRules(string root, bool bashOnly = false)
    {
        var actual = ScanPortability(root);
        var allowed = ProgressDeserialize<PortabilityAllowlist>(ProgressText(Path.Combine(root, PortabilityInventory)));
        ValidatePortabilityInventory(actual, allowed, bashOnly);
        var syntaxOutput = Path.Combine(root, ".prdkit/cache/evidence/026-macos-progress/bash-syntax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(syntaxOutput);
        var scripts = Directory.EnumerateFiles(Path.Combine(root, "scripts"), "*.sh", SearchOption.AllDirectories)
            .Where(p => !p.Contains("/obj/", StringComparison.Ordinal) && !p.Contains("/bin/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < scripts.Length; i++)
            CheckBashSyntax(root, scripts[i], syntaxOutput, "syntax-" + i);
        if (!bashOnly)
        {
            ValidateProgressIndex(ProgressDeserialize<ProgressIndexData>(ProgressText(Path.Combine(root, ProgressIndex))));
            var errors = new List<string>();
            ValidateProgressWorkflow(File.ReadAllText(Path.Combine(root, ".github/workflows/ci.yml")), errors);
            if (errors.Count != 0) return Report(errors, "");
        }
        Console.WriteLine($"Portability lint passed with {actual.Count(e => !bashOnly || e.Rule is "bash32" or "bsd-tools")} exact migration entries; not platform qualification.");
        return 0;
    }

    private static void CheckBashSyntax(string root, string file, string output, string label)
    {
        var syntax = ProgressCommandBounded(root, output, label, "/bin/bash", ["-n", "--", file], 10000, ProgressMaxBytes);
        if (syntax.Exit != 0) throw new InvalidDataException("Bash syntax check failed: " + file);
    }

    private static void ValidateProgressWorkflow(string text, ICollection<string> errors)
    {
        string Job(string name) => Regex.Match(text, @"(?ms)^  " + name + @":\r?\n(?<job>.*?)(?=^  [A-Za-z0-9_-]+:|\z)", RegexOptions.CultureInvariant, PatternTimeout).Groups["job"].Value;
        var progress = Job("macos-progress");
        var package = Job("package-smoke");
        // Strip comments so a deleted checkout/command cannot be replaced with a matching comment.
        progress = string.Join('\n', progress.Split('\n').Select(YamlCode));
        package = string.Join('\n', package.Split('\n').Select(YamlCode));
        foreach (var required in new[] { "runs-on: macos-26", "timeout-minutes: 35", "if: ${{ github.event_name == 'workflow_dispatch' }}",
            "mode: [default-tmpdir, private-tmp]", "fail-fast: false", "ref: ${{ github.event.pull_request.head.sha || github.sha }}",
            "fetch-depth: 0", "persist-credentials: false", "getconf DARWIN_USER_TEMP_DIR", "mktemp -d /private/tmp/agd-progress.",
            "macos-progress run", "macos-progress check", "dotnet restore FTPlus.AgentGraphicDesign.slnx --locked-mode",
            "dotnet build FTPlus.AgentGraphicDesign.slnx", "if: always()", "actions/upload-artifact@", "if-no-files-found: error" })
            if (!progress.Contains(required, StringComparison.Ordinal)) errors.Add("macOS progress workflow missing " + required);
        if (progress.Contains("continue-on-error", StringComparison.Ordinal)) errors.Add("Progress completeness may not be advisory");
        if (!package.Contains("ref: ${{ github.event.pull_request.head.sha || github.sha }}", StringComparison.Ordinal)) errors.Add("Package-smoke must check out the explicit PR head");
        ValidateWorkflow("ci.yml", text, errors);
    }

    private static void RunPortabilitySelfTests(ICollection<string> errors)
    {
        var count = 0;
        foreach (var (path, source, rule) in new[]
        {
            ("src/Test.cs", "Path.\nGetFullPath(x)", "canonical-path"), ("src/Test.cs", "using static System.IO.Path; GetRelativePath(a,b)", "canonical-path"),
            ("src/Test.cs", "directory.\n FullName", "fileinfo-fullname"),
            ("tests/Test.cs", "if (!OperatingSystem.IsLinux()) return;", "os-return"),
            ("tests/Test.cs", "if (x &&\n RuntimeInformation.IsOSPlatform(OSPlatform.Windows))\n {\n return; }", "os-return"),
            ("tests/Test.cs", "bool skip = OperatingSystem.IsWindows();\n if (skip) return;", "os-return"),
            ("scripts/test.sh", "mapfile -t list < file", "bash32"), ("scripts/test.sh", "declare -Ar data", "bash32"),
            ("scripts/test.sh", "local -n ref=other", "bash32"), ("scripts/test.sh", "echo ${name,,}", "bash32"),
            ("scripts/test.sh", "echo ${name^^}", "bash32"), ("scripts/test.sh", "readlink -f file", "bsd-tools"),
            ("scripts/test.sh", "readlink -e file", "bsd-tools"), ("scripts/test.sh", "sed -i 's/a/b/' file", "bsd-tools"),
            ("scripts/test.sh", "sed -E -i -e 's/a/b/' file", "bsd-tools"),
            ("scripts/test.sh", "sed -i\necho next", "bsd-tools"),
            ("scripts/test.sh", "mv -fT a b", "bsd-tools"), ("scripts/test.sh", "find . -printf '%p'", "bsd-tools")
        })
        {
            if (!PortabilityRules(path, source).Contains(rule)) errors.Add("Portability lint missed " + source);
            count++;
        }
        if (PortabilityRules("src/FTPlus.AgentGraphicDesign.Core/Platform/PlatformPaths.cs", "Path.GetFullPath(x)").Length != 0
            || PortabilityRules("scripts/test.sh", "lower=$(printf '%s' \"$x\" | tr '[:upper:]' '[:lower:]')").Length != 0) errors.Add("Portability positive fixture rejected");
        foreach (var source in new[] { "sed -i.bak 's/a/b/' file", "sed -i '' 's/a/b/' file", "sed -i \"\" 's/a/b/' file", "sed -i  '' 's/a/b/' file", "readlink file" })
        {
            if (PortabilityRules("scripts/test.sh", source).Length != 0) errors.Add("Supported BSD alternative rejected: " + source);
            count++;
        }
        var syntaxDir = Path.Combine(Path.GetTempPath(), "agd-bash-syntax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(syntaxDir);
        try
        {
            foreach (var (name, source, passes) in new[] { ("valid", "if true; then\n echo ok\nfi\n", true), ("invalid", "if true; then\n echo bad\n", false) })
            {
                var file = Path.Combine(syntaxDir, name + ".sh"); File.WriteAllText(file, source);
                try { CheckBashSyntax(syntaxDir, file, syntaxDir, name); if (!passes) errors.Add("Invalid shell syntax accepted"); }
                catch (InvalidDataException) { if (passes) errors.Add("Valid shell syntax rejected"); }
                count++;
            }
        }
        finally { Directory.Delete(syntaxDir, recursive: true); }
        var entry = new PortabilityEntry("tests/Test.cs", "os-return", new string('a', 64), "008/T013: convert existing early return to explicit platform fixture.");
        foreach (var (name, actual, allowed, passes) in new (string, PortabilityEntry[], PortabilityEntry[], bool)[]
        {
            ("exact", [entry], [entry], true), ("new", [entry], [], false), ("stale", [], [entry], false),
            ("mutated", [entry with { Sha256 = new string('b', 64) }], [entry], false),
            ("unreasoned", [entry], [entry with { Reason = "" }], false), ("duplicate", [entry], [entry, entry], false)
        })
        {
            try { ValidatePortabilityInventory(actual, new(1, allowed), false); if (!passes) errors.Add("Inventory accepted " + name); }
            catch (InvalidDataException) { if (passes) errors.Add("Inventory rejected " + name); }
            count++;
        }
        var root = GetWorkingRoot([], 0);
        var workflow = File.ReadAllText(Path.Combine(root, ".github/workflows/ci.yml"));
        foreach (var (name, from, to) in new[]
        {
            ("wrong head", "ref: ${{ github.event.pull_request.head.sha || github.sha }}", "ref: main"),
            ("missing check", "macos-progress check", "unrelated check"), ("one mode", "mode: [default-tmpdir, private-tmp]", "mode: [private-tmp]"),
            ("wrong root", "getconf DARWIN_USER_TEMP_DIR", "pwd"), ("no logs", "if-no-files-found: error", "if-no-files-found: ignore")
        })
        {
            var result = new List<string>();
            ValidateProgressWorkflow(workflow.Replace(from, to, StringComparison.Ordinal), result);
            if (result.Count == 0) errors.Add("Progress workflow accepted " + name);
            count++;
        }
        var valid = new List<string>(); ValidateProgressWorkflow(workflow, valid);
        if (valid.Count != 0) errors.Add("Actual progress workflow invalid: " + string.Join("; ", valid));
        Console.WriteLine($"Portability self-tests: {count + 3} source/inventory/workflow fixtures.");
    }
}
