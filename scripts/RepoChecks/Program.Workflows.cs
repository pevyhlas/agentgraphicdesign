using System.Text.RegularExpressions;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private static readonly Regex RunsOnLine = new(@"^\s*runs-on:\s*(?<value>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex UsesLine = new(@"^\s*(?:-\s+)?uses:\s*(?<ref>\S+)\s*(?:#\s*(?<comment>\S.*))?$", RegexOptions.CultureInvariant);
    private static readonly Regex HostedLabel = new(@"^(ubuntu|macos|windows)-[A-Za-z0-9.-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex PinnedAction = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionComment = new(@"^v?\d+(\.\d+)*\b", RegexOptions.CultureInvariant);
    private static readonly Regex PrivilegedTrigger = new(@"\b(?<trigger>pull_request_target|workflow_run)\b", RegexOptions.CultureInvariant);
    private static readonly Regex SecretsReference = new(@"\bsecrets\b", RegexOptions.CultureInvariant);
    private static readonly Regex PermissionsLine = new(@"^(?<indent>\s*)permissions:\s*(?<value>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex WritePermission = new(@"\bwrite(-all)?\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Public CI policy: GitHub-hosted runners only; no <c>pull_request_target</c> or
    /// <c>workflow_run</c> trigger; a top-level <c>permissions</c> block that is exactly
    /// <c>contents: read</c> or <c>{}</c>; no job-level write permission; no <c>secrets</c>
    /// reference; and every action pinned by full commit SHA with a version comment.
    /// </summary>
    private static int CheckWorkflows(string root)
    {
        var errors = new List<string>();
        var github = Path.Combine(root, ".github");
        var workflowDirectory = Path.Combine(github, "workflows");
        var workflows = Directory.Exists(workflowDirectory)
            ? Directory.EnumerateFiles(workflowDirectory)
                .Where(path => path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
        if (workflows.Count == 0)
        {
            errors.Add($"no workflows found under {workflowDirectory}");
        }

        foreach (var file in Directory.EnumerateFiles(github, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            ValidateNoSelfHostedReference(Path.GetRelativePath(root, file), File.ReadAllText(file), errors);
        }

        foreach (var workflow in workflows)
        {
            ValidateWorkflow(Path.GetRelativePath(root, workflow), File.ReadAllText(workflow), errors);
        }

        return Report(errors,
            $"Workflow policy passed: {workflows.Count} workflow(s) on GitHub-hosted runners, read-only token, no secrets, no pull_request_target or workflow_run, every action pinned by commit SHA.");
    }

    private static void ValidateNoSelfHostedReference(string name, string text, ICollection<string> errors)
    {
        if (text.Contains("self-hosted", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{name}: refers to a self-hosted runner; public CI uses GitHub-hosted runners only");
        }
    }

    private static void ValidateWorkflow(string name, string text, ICollection<string> errors)
    {
        var topLevelPermissions = 0;
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var where = $"{name}:{index + 1}";
            var code = YamlCode(line);

            var trigger = PrivilegedTrigger.Match(code);
            if (trigger.Success)
            {
                errors.Add($"{where}: {trigger.Groups["trigger"].Value} is not allowed");
            }

            if (SecretsReference.IsMatch(code))
            {
                errors.Add($"{where}: secrets are not available to public CI; remove the secrets reference");
            }

            var permissions = PermissionsLine.Match(code);
            if (permissions.Success)
            {
                var entries = PermissionEntries(lines, index, permissions.Groups["value"].Value.Trim());
                if (permissions.Groups["indent"].Length == 0)
                {
                    topLevelPermissions++;
                    if (entries is not ["{}"] and not ["contents: read"])
                    {
                        errors.Add($"{where}: top-level permissions must be exactly 'contents: read' or '{{}}', not '{string.Join(", ", entries)}'");
                    }
                }
                else if (entries.Any(entry => WritePermission.IsMatch(entry)))
                {
                    errors.Add($"{where}: job-level permissions must not grant write access ('{string.Join(", ", entries)}')");
                }
            }

            var runsOn = RunsOnLine.Match(line);
            if (runsOn.Success)
            {
                var label = StripYamlComment(runsOn.Groups["value"].Value).Trim().Trim('"', '\'');
                if (!HostedLabel.IsMatch(label))
                {
                    errors.Add($"{where}: runs-on must be one GitHub-hosted image label such as ubuntu-24.04, not '{label}'");
                }
            }

            var uses = UsesLine.Match(line);
            if (uses.Success)
            {
                var reference = uses.Groups["ref"].Value.Trim('"', '\'');
                if (reference.StartsWith("./", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!PinnedAction.IsMatch(reference))
                {
                    errors.Add($"{where}: '{reference}' must be pinned as owner/repo@<40-hex commit SHA>");
                }
                else if (!uses.Groups["comment"].Success || !VersionComment.IsMatch(uses.Groups["comment"].Value))
                {
                    errors.Add($"{where}: '{reference}' needs a version comment such as '# v5.1.0'");
                }
            }
        }

        if (topLevelPermissions != 1)
        {
            errors.Add($"{name}: needs exactly one top-level permissions block (contents: read), found {topLevelPermissions}");
        }
    }

    // A permissions value is either inline ('{}', 'read-all', '{ contents: read }') or the
    // more-indented mapping lines that follow the key, normalized to 'scope: level' entries.
    private static List<string> PermissionEntries(string[] lines, int keyIndex, string inlineValue)
    {
        if (inlineValue.Length > 0)
        {
            var inline = inlineValue.Trim('"', '\'');
            if (inline.StartsWith('{') && inline.EndsWith('}') && inline.Length > 2)
            {
                return inline[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(NormalizePermissionEntry).ToList();
            }

            return [inline.Replace(" ", "", StringComparison.Ordinal)];
        }

        var keyIndent = lines[keyIndex].Length - lines[keyIndex].TrimStart().Length;
        var entries = new List<string>();
        for (var index = keyIndex + 1; index < lines.Length; index++)
        {
            var code = YamlCode(lines[index]);
            if (code.Trim().Length == 0)
            {
                continue;
            }

            if (code.Length - code.TrimStart().Length <= keyIndent)
            {
                break;
            }

            entries.Add(NormalizePermissionEntry(code.Trim()));
        }

        return entries;
    }

    private static string NormalizePermissionEntry(string entry)
    {
        var colon = entry.IndexOf(':', StringComparison.Ordinal);
        return colon < 0
            ? entry
            : $"{entry[..colon].Trim()}: {entry[(colon + 1)..].Trim().Trim('"', '\'')}";
    }

    // The YAML content of a line with any full-line or trailing comment removed.
    private static string YamlCode(string line) =>
        line.TrimStart().StartsWith('#') ? string.Empty : StripYamlComment(line).TrimEnd();

    private static string StripYamlComment(string value)
    {
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? value[..hash] : value;
    }

    private static void RunWorkflowPolicySelfTests(ICollection<string> errors)
    {
        const string valid = """
            # No job needs secrets; workflow_run and pull_request_target stay out.
            name: CI
            on:
              pull_request:
              push:
                branches: [main]
            permissions:
              contents: read
            jobs:
              test:
                runs-on: ubuntu-24.04
                permissions:
                  contents: read
                steps:
                  - uses: actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09 # v5.1.0
                  - uses: ./.github/actions/local
                  - run: echo ok
              mac:
                runs-on: macos-latest
                steps:
                  - name: Setup
                    uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
            """;
        var validEmptyPermissions = valid.Replace("permissions:\n  contents: read\njobs:", "permissions: {}\njobs:", StringComparison.Ordinal);
        foreach (var (fixtureName, fixture) in new[] { ("valid.yml", valid), ("valid-empty-permissions.yml", validEmptyPermissions) })
        {
            if (fixtureName != "valid.yml" && string.Equals(fixture, valid, StringComparison.Ordinal))
            {
                errors.Add($"workflow policy self-test fixture {fixtureName} did not change the valid workflow");
            }

            var baseline = new List<string>();
            ValidateWorkflow(fixtureName, fixture, baseline);
            ValidateNoSelfHostedReference(fixtureName, fixture, baseline);
            foreach (var error in baseline)
            {
                errors.Add($"workflow policy rejected a valid workflow: {error}");
            }
        }

        (string Name, string Text, string Expected)[] mutations =
        [
            ("self-hosted runs-on", valid.Replace("runs-on: ubuntu-24.04", "runs-on: [self-hosted, linux]", StringComparison.Ordinal), "self-hosted"),
            ("custom runner label", valid.Replace("runs-on: ubuntu-24.04", "runs-on: build-box", StringComparison.Ordinal), "GitHub-hosted image label"),
            ("pull_request_target trigger", valid.Replace("  pull_request:\n", "  pull_request_target:\n", StringComparison.Ordinal), "pull_request_target is not allowed"),
            ("workflow_run trigger", valid.Replace("  pull_request:\n", "  workflow_run:\n    workflows: [CI]\n", StringComparison.Ordinal), "workflow_run is not allowed"),
            ("tag-pinned action", valid.Replace("actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09 # v5.1.0", "actions/checkout@v5", StringComparison.Ordinal), "must be pinned"),
            ("short SHA", valid.Replace("@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09", "@fbc6f39", StringComparison.Ordinal), "must be pinned"),
            ("SHA without version comment", valid.Replace(" # v6.0.0", "", StringComparison.Ordinal), "needs a version comment"),
            ("docker action", valid.Replace("actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09", "docker://alpine:3", StringComparison.Ordinal), "must be pinned"),
            ("missing top-level permissions", valid.Replace("permissions:\n  contents: read\njobs:", "jobs:", StringComparison.Ordinal), "exactly one top-level permissions block"),
            ("write-all top-level permissions", valid.Replace("permissions:\n  contents: read\njobs:", "permissions: write-all\njobs:", StringComparison.Ordinal), "top-level permissions must be exactly"),
            ("read-all top-level permissions", valid.Replace("permissions:\n  contents: read\njobs:", "permissions: read-all\njobs:", StringComparison.Ordinal), "top-level permissions must be exactly"),
            ("contents write top-level permission", valid.Replace("permissions:\n  contents: read\njobs:", "permissions:\n  contents: write\njobs:", StringComparison.Ordinal), "top-level permissions must be exactly"),
            ("extra top-level permission", valid.Replace("permissions:\n  contents: read\njobs:", "permissions:\n  contents: read\n  id-token: write\njobs:", StringComparison.Ordinal), "top-level permissions must be exactly"),
            ("job-level write-all permissions", valid.Replace("    permissions:\n      contents: read\n", "    permissions: write-all\n", StringComparison.Ordinal), "job-level permissions must not grant write"),
            ("job-level write permission", valid.Replace("    permissions:\n      contents: read\n", "    permissions:\n      contents: read\n      packages: write\n", StringComparison.Ordinal), "job-level permissions must not grant write"),
            ("job-level inline write permission", valid.Replace("    permissions:\n      contents: read\n", "    permissions: { pull-requests: write }\n", StringComparison.Ordinal), "job-level permissions must not grant write"),
            ("secrets expression", valid.Replace("- run: echo ok", "- run: echo ${{ secrets.NUGET_TOKEN }}", StringComparison.Ordinal), "remove the secrets reference"),
            ("inherited secrets", valid.Replace("    runs-on: macos-latest\n", "    runs-on: macos-latest\n    secrets: inherit\n", StringComparison.Ordinal), "remove the secrets reference")
        ];
        foreach (var (mutationName, text, expected) in mutations)
        {
            if (string.Equals(text, valid, StringComparison.Ordinal))
            {
                errors.Add($"workflow policy self-test mutation '{mutationName}' did not change the fixture");
                continue;
            }

            var found = new List<string>();
            ValidateWorkflow("mutated.yml", text, found);
            ValidateNoSelfHostedReference("mutated.yml", text, found);
            if (!found.Any(error => error.Contains(expected, StringComparison.Ordinal)))
            {
                errors.Add($"workflow policy accepted a workflow with a {mutationName} (expected an error containing '{expected}', got: {string.Join("; ", found)})");
            }
        }
    }
}
