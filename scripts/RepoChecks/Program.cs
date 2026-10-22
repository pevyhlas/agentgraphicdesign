using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                return Usage();
            }

            return args[0] switch
            {
                "dependencies" => CheckDependencies(GetRoot(args, 1)),
                "vulnerabilities" => CheckVulnerabilities(GetRoot(args, 1)),
                "coverage" when args.Length >= 3 => CheckCoverage(args[1], args[2]),
                "package" when args.Length >= 7 => CheckPackage(args[1], args[2], args[3], args[4], args[5], args[6]),
                "readiness" when args.Length >= 3 => CheckReadiness(args[1], args[2]),
                "poison-cache" when args.Length == 3 => CreatePoisonCache(args[1], args[2]),
                "workflows" => CheckWorkflows(GetRoot(args, 1)),
                "self-test" => RunSelfTests(),
                _ => Usage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int CheckDependencies(string root)
    {
        RunReviewedAuditRestore(root);
        var errors = new List<string>();
        var policyPath = Path.Combine(root, ".config", "dependencies.json");
        using var policyDocument = JsonDocument.Parse(File.ReadAllText(policyPath));
        var policy = policyDocument.RootElement;
        var allowedLicenses = policy.GetProperty("policy").GetProperty("allowedLicenses")
            .EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        var prohibitedLicenses = policy.GetProperty("policy").GetProperty("prohibitedLicenses")
            .EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);

        var approvals = new Dictionary<string, Approval>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in policy.GetProperty("approvedPackages").EnumerateArray())
        {
            var approval = new Approval(
                item.GetProperty("id").GetString()!,
                item.GetProperty("version").GetString()!,
                item.GetProperty("license").GetString()!,
                item.GetProperty("scopes").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal),
                item.TryGetProperty("noticeMarker", out var marker) ? marker.GetString() : null);

            if (!approvals.TryAdd(approval.Id, approval))
            {
                errors.Add($"duplicate approval for {approval.Id}");
            }

            if (!allowedLicenses.Contains(approval.License) || prohibitedLicenses.Contains(approval.License))
            {
                errors.Add($"{approval.Id} uses disallowed license {approval.License}");
            }
        }

        var centralVersions = XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .ToDictionary(
                element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value
                    ?? throw new InvalidDataException("PackageVersion is missing Include/Update"),
                element => element.Attribute("Version")?.Value
                    ?? throw new InvalidDataException("PackageVersion is missing Version"),
                StringComparer.OrdinalIgnoreCase);

        using var packageList = RunPackageList(root, vulnerable: false);
        ValidatePackageList(packageList.RootElement, root, vulnerable: false, GetExpectedPackageGraph(root));
        var listedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observedApprovals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observedDirect = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packageFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in packageList.RootElement.GetProperty("projects").EnumerateArray())
        {
            var projectPath = Path.GetFullPath(project.GetProperty("path").GetString()!);
            var relativeProject = Path.GetRelativePath(root, projectPath).Replace('\\', '/');
            listedProjects.Add(relativeProject);
            CollectPackageFolders(projectPath, packageFolders);
            var scope = relativeProject.StartsWith("tests/", StringComparison.Ordinal) ? "test" : "runtime";

            foreach (var framework in project.GetProperty("frameworks").EnumerateArray())
            {
                if (framework.TryGetProperty("topLevelPackages", out var topLevelPackages))
                {
                    foreach (var package in topLevelPackages.EnumerateArray())
                    {
                        var id = package.GetProperty("id").GetString()!;
                        var requested = package.GetProperty("requestedVersion").GetString()!;
                        var resolved = package.GetProperty("resolvedVersion").GetString()!;
                        RecordResolvedPackage(resolvedPackages, errors, id, resolved);
                        observedDirect.Add(id);
                        ValidatePackage(approvals, observedApprovals, errors, relativeProject, scope, id, resolved);

                        if (!centralVersions.TryGetValue(id, out var centralVersion))
                        {
                            errors.Add($"{relativeProject}: direct package {id} is not centrally pinned");
                        }
                        else if (!string.Equals(centralVersion, requested, StringComparison.Ordinal)
                            || !string.Equals(centralVersion, resolved, StringComparison.Ordinal))
                        {
                            errors.Add($"{relativeProject}: {id} central/requested/resolved versions differ ({centralVersion}/{requested}/{resolved})");
                        }
                    }
                }

                if (framework.TryGetProperty("transitivePackages", out var transitivePackages))
                {
                    foreach (var package in transitivePackages.EnumerateArray())
                    {
                        var id = package.GetProperty("id").GetString()!;
                        var resolved = package.GetProperty("resolvedVersion").GetString()!;
                        RecordResolvedPackage(resolvedPackages, errors, id, resolved);
                        ValidatePackage(
                            approvals,
                            observedApprovals,
                            errors,
                            relativeProject,
                            scope,
                            id,
                            resolved);
                    }
                }
            }
        }

        foreach (var unusedPin in centralVersions.Keys.Except(observedDirect, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"central package pin is not directly referenced: {unusedPin}");
        }
        foreach (var staleApproval in approvals.Keys.Except(observedApprovals, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"approved package is not present in the resolved solution graph: {staleApproval}");
        }
        var validatedLicenseMetadata = ValidatePackageMetadata(
            packageFolders, resolvedPackages, approvals, errors);

        var noticeFile = policy.GetProperty("policy").GetProperty("noticeFile").GetString()!;
        var notices = File.ReadAllText(Path.Combine(root, noticeFile));
        foreach (var approval in approvals.Values.Where(item => item.NoticeMarker is not null))
        {
            if (!notices.Contains(approval.NoticeMarker!, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"notice marker for {approval.Id} is missing: {approval.NoticeMarker}");
            }
        }
        var noticeTexts = new Dictionary<string, string>(StringComparer.Ordinal) { [noticeFile] = notices };
        ValidateUpstreamNotices(
            root, policy, packageFolders, resolvedPackages, approvals, noticeTexts, errors);
        ValidateRequiredNotices(
            policy.GetProperty("requiredNotices"), noticeFile, noticeTexts, allowedLicenses, prohibitedLicenses, errors);

        return Report(errors, $"Dependency policy passed: {listedProjects.Count} projects, {observedApprovals.Count} exact package approvals, {validatedLicenseMetadata} package license metadata records checked, all notice markers present.");
    }

    private static void CollectPackageFolders(string projectPath, ISet<string> packageFolders)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        foreach (var folder in assets.RootElement.GetProperty("packageFolders").EnumerateObject())
        {
            packageFolders.Add(Path.GetFullPath(folder.Name));
        }
    }

    private static void RecordResolvedPackage(
        IDictionary<string, string> resolvedPackages,
        ICollection<string> errors,
        string id,
        string version)
    {
        if (resolvedPackages.TryGetValue(id, out var existing) && !string.Equals(existing, version, StringComparison.Ordinal))
        {
            errors.Add($"package {id} resolves to multiple versions ({existing}, {version})");
        }
        else
        {
            resolvedPackages[id] = version;
        }
    }

    private static int ValidatePackageMetadata(
        IReadOnlyCollection<string> packageFolders,
        IReadOnlyDictionary<string, string> resolvedPackages,
        IReadOnlyDictionary<string, Approval> approvals,
        ICollection<string> errors)
    {
        var checkedRecords = 0;
        foreach (var (id, version) in resolvedPackages.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!approvals.TryGetValue(id, out var approval))
            {
                continue;
            }

            var packageDirectory = packageFolders
                .Select(folder => Path.Combine(folder, id.ToLowerInvariant(), version.ToLowerInvariant()))
                .FirstOrDefault(Directory.Exists);
            if (packageDirectory is null)
            {
                errors.Add($"restored package metadata directory was not found for {id} {version}");
                continue;
            }

            var nuspecPath = Directory.EnumerateFiles(packageDirectory, "*.nuspec", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();
            if (nuspecPath is null)
            {
                errors.Add($"restored package has no nuspec metadata: {id} {version}");
                continue;
            }

            var nuspec = XDocument.Load(nuspecPath);
            var metadata = nuspec.Descendants().Single(element => element.Name.LocalName == "metadata");
            var metadataId = metadata.Elements().Single(element => element.Name.LocalName == "id").Value;
            var metadataVersion = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
            if (!string.Equals(metadataId, id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(metadataVersion, version, StringComparison.Ordinal))
            {
                errors.Add($"nuspec identity mismatch for {id} {version}: {metadataId} {metadataVersion}");
                continue;
            }

            var license = metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "license");
            if (license?.Attribute("type")?.Value.Equals("expression", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!string.Equals(license.Value.Trim(), approval.License, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{id} nuspec license expression {license.Value.Trim()} differs from approved {approval.License}");
                }
                checkedRecords++;
                continue;
            }

            if (license?.Attribute("type")?.Value.Equals("file", StringComparison.OrdinalIgnoreCase) == true)
            {
                var licensePath = Path.GetFullPath(Path.Combine(packageDirectory, license.Value));
                var packagePrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory)) + Path.DirectorySeparatorChar;
                if (!licensePath.StartsWith(packagePrefix, StringComparison.Ordinal) ||
                    !File.Exists(licensePath) || new FileInfo(licensePath).Length == 0)
                {
                    errors.Add($"{id} nuspec license file is missing or invalid: {license.Value}");
                }
                else if (!LicenseFileMatchesApproval(File.ReadAllText(licensePath), approval.License))
                {
                    errors.Add($"{id} license file does not contain the expected marker for {approval.License}");
                }
                checkedRecords++;
                continue;
            }

            // Older packages expose only licenseUrl. Presence is checked, but the
            // approval remains a manual legal-review assertion rather than fetched CI input.
            var licenseUrl = metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "licenseUrl")?.Value;
            if (string.IsNullOrWhiteSpace(licenseUrl) || !Uri.TryCreate(licenseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"{id} exposes no usable package license metadata");
            }
            checkedRecords++;
        }

        return checkedRecords;
    }

    private static bool LicenseFileMatchesApproval(string content, string license) => license switch
    {
        "MIT" => content.Contains("Permission is hereby granted", StringComparison.OrdinalIgnoreCase),
        "Apache-2.0" => content.Contains("Apache License", StringComparison.OrdinalIgnoreCase) &&
            content.Contains("Version 2.0", StringComparison.OrdinalIgnoreCase),
        "BSL-1.0" => content.Contains("Boost Software License", StringComparison.OrdinalIgnoreCase),
        "BSD-3-Clause" => content.Contains("Redistribution and use in source and binary forms", StringComparison.OrdinalIgnoreCase),
        _ => content.Length > 0
    };

    // Each required notice covers a component that ships inside a package binary or a bundled
    // asset, so it names the licenses it reproduces; each must be allowed and not prohibited,
    // like a package approval's license. Its marker lives in the policy notice file unless it
    // names a declared upstream notice file, and must occur there exactly once.
    private static void ValidateRequiredNotices(
        JsonElement requiredNotices,
        string defaultNoticeFile,
        IReadOnlyDictionary<string, string> noticeTexts,
        IReadOnlySet<string> allowedLicenses,
        IReadOnlySet<string> prohibitedLicenses,
        ICollection<string> errors)
    {
        var noticeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var notice in requiredNotices.EnumerateArray())
        {
            var id = notice.GetProperty("id").GetString()!;
            var marker = notice.GetProperty("marker").GetString()!;
            if (!noticeIds.Add(id))
            {
                errors.Add($"duplicate required notice ID: {id}");
            }

            if (!notice.TryGetProperty("licenses", out var licenses)
                || licenses.ValueKind != JsonValueKind.Array
                || licenses.GetArrayLength() == 0)
            {
                errors.Add($"required notice for {id} does not list its licenses");
            }
            else
            {
                foreach (var license in licenses.EnumerateArray())
                {
                    var name = license.ValueKind == JsonValueKind.String ? license.GetString()! : license.GetRawText();
                    if (!allowedLicenses.Contains(name) || prohibitedLicenses.Contains(name))
                    {
                        errors.Add($"required notice for {id} uses disallowed license {name}");
                    }
                }
            }

            var file = notice.TryGetProperty("file", out var fileElement) ? fileElement.GetString()! : defaultNoticeFile;
            if (!noticeTexts.TryGetValue(file, out var notices))
            {
                errors.Add($"required notice for {id} names an undeclared notice file: {file}");
                continue;
            }

            var occurrences = CountOccurrences(notices, marker);
            if (occurrences == 0)
            {
                errors.Add($"required notice for {id} is missing marker in {file}: {marker}");
            }
            else if (occurrences > 1)
            {
                errors.Add($"required notice marker for {id} occurs {occurrences} times in {file}; expected one reviewed section");
            }
        }
    }

    // An upstream notice is a package's own third-party notice file, redistributed byte for byte
    // beside the policy notice file. The repository copy and the copy in every listed package
    // must hash to the reviewed SHA-256, so a package upgrade that changes the notice fails until
    // the new file is reviewed and copied. Required notices may then point their markers into it.
    private static void ValidateUpstreamNotices(
        string root,
        JsonElement policy,
        IReadOnlyCollection<string> packageFolders,
        IReadOnlyDictionary<string, string> resolvedPackages,
        IReadOnlyDictionary<string, Approval> approvals,
        IDictionary<string, string> noticeTexts,
        ICollection<string> errors)
    {
        if (!policy.TryGetProperty("upstreamNotices", out var upstreamNotices))
        {
            return;
        }

        foreach (var upstream in upstreamNotices.EnumerateArray())
        {
            var id = upstream.GetProperty("id").GetString()!;
            var file = upstream.GetProperty("file").GetString()!;
            var packageFile = upstream.GetProperty("packageFile").GetString()!;
            var expectedHash = upstream.GetProperty("sha256").GetString()!;
            var packageIds = upstream.GetProperty("packages").EnumerateArray().Select(value => value.GetString()!).ToList();
            if (packageIds.Count == 0)
            {
                errors.Add($"upstream notice {id} lists no packages");
            }

            var repositoryPath = Path.Combine(root, file);
            if (!File.Exists(repositoryPath))
            {
                errors.Add($"upstream notice {id} file is missing: {file}");
                continue;
            }

            var bytes = File.ReadAllBytes(repositoryPath);
            if (!string.Equals(Sha256Hex(bytes), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"upstream notice {id} file {file} does not match its reviewed SHA-256");
            }
            if (!noticeTexts.TryAdd(file, System.Text.Encoding.UTF8.GetString(bytes)))
            {
                errors.Add($"upstream notice {id} reuses notice file {file}");
            }

            foreach (var packageId in packageIds)
            {
                if (!approvals.ContainsKey(packageId) || !resolvedPackages.TryGetValue(packageId, out var version))
                {
                    errors.Add($"upstream notice {id} names a package that is not an approved resolved dependency: {packageId}");
                    continue;
                }

                var packageNotice = packageFolders
                    .Select(folder => Path.Combine(folder, packageId.ToLowerInvariant(), version.ToLowerInvariant(), packageFile))
                    .FirstOrDefault(File.Exists);
                if (packageNotice is null)
                {
                    errors.Add($"{packageId} {version} has no {packageFile} for upstream notice {id}");
                }
                else if (!string.Equals(Sha256Hex(File.ReadAllBytes(packageNotice)), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{packageId} {version} {packageFile} differs from upstream notice {id}; review it and update {file} and its SHA-256");
                }
            }
        }
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RunRequiredNoticeSelfTests(ICollection<string> errors)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "MIT", "LicenseRef-Example" };
        var prohibited = new HashSet<string>(StringComparer.Ordinal) { "LGPL-2.1" };
        var noticeTexts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NOTICES.txt"] = "Alpha (MIT)\nBeta (Example License)\n",
            ["UPSTREAM.txt"] = "# gamma\r\n# END: gamma\r\n"
        };
        (string Name, string Json, string? Failure)[] fixtures =
        [
            ("listed allowed licenses", """[{"id":"a","marker":"Alpha (MIT)","licenses":["MIT"]},{"id":"b","marker":"Beta (Example License)","licenses":["LicenseRef-Example","MIT"]}]""", null),
            ("marker in a declared upstream file", """[{"id":"g","file":"UPSTREAM.txt","marker":"# gamma","licenses":["MIT"]}]""", null),
            ("missing licenses", """[{"id":"a","marker":"Alpha (MIT)"}]""", "does not list its licenses"),
            ("empty licenses", """[{"id":"a","marker":"Alpha (MIT)","licenses":[]}]""", "does not list its licenses"),
            ("unlisted license", """[{"id":"a","marker":"Alpha (MIT)","licenses":["LicenseRef-Unreviewed"]}]""", "disallowed license LicenseRef-Unreviewed"),
            ("prohibited license", """[{"id":"a","marker":"Alpha (MIT)","licenses":["LGPL-2.1"]}]""", "disallowed license LGPL-2.1"),
            ("missing marker", """[{"id":"a","marker":"Gamma (MIT)","licenses":["MIT"]}]""", "missing marker"),
            ("marker only in another file", """[{"id":"g","marker":"# gamma","licenses":["MIT"]}]""", "missing marker"),
            ("undeclared notice file", """[{"id":"g","file":"OTHER.txt","marker":"# gamma","licenses":["MIT"]}]""", "undeclared notice file"),
            ("repeated marker", """[{"id":"g","file":"UPSTREAM.txt","marker":"gamma","licenses":["MIT"]}]""", "expected one reviewed section"),
            ("duplicate id", """[{"id":"a","marker":"Alpha (MIT)","licenses":["MIT"]},{"id":"A","marker":"Beta (Example License)","licenses":["MIT"]}]""", "duplicate required notice ID")
        ];

        foreach (var fixture in fixtures)
        {
            using var document = JsonDocument.Parse(fixture.Json);
            var fixtureErrors = new List<string>();
            ValidateRequiredNotices(document.RootElement, "NOTICES.txt", noticeTexts, allowed, prohibited, fixtureErrors);
            if (fixture.Failure is null && fixtureErrors.Count != 0)
            {
                errors.Add($"required-notice fixture {fixture.Name}: unexpected rejection: {string.Join("; ", fixtureErrors)}");
            }
            else if (fixture.Failure is not null
                && !fixtureErrors.Any(error => error.Contains(fixture.Failure, StringComparison.Ordinal)))
            {
                errors.Add($"required-notice fixture was incorrectly accepted: {fixture.Name}");
            }
        }

        RunUpstreamNoticeSelfTests(errors);
    }

    private static void RunUpstreamNoticeSelfTests(ICollection<string> errors)
    {
        var root = Path.Combine(Path.GetTempPath(), "agd-repochecks-upstream-" + Guid.NewGuid().ToString("N"));
        try
        {
            var packages = Path.Combine(root, "packages");
            var reviewed = System.Text.Encoding.UTF8.GetBytes("# gamma\r\nCopyright © Example\r\n");
            var hash = Sha256Hex(reviewed);
            Directory.CreateDirectory(Path.Combine(packages, "example.native", "1.0.0"));
            File.WriteAllBytes(Path.Combine(packages, "example.native", "1.0.0", "THIRD-PARTY-NOTICES.txt"), reviewed);
            var approvals = new Dictionary<string, Approval>(StringComparer.OrdinalIgnoreCase)
            {
                ["Example.Native"] = new("Example.Native", "1.0.0", "MIT", ["runtime"], null)
            };
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Example.Native"] = "1.0.0" };

            (string Name, byte[] RepositoryCopy, string PackageId, string? Failure)[] fixtures =
            [
                ("byte-identical copies", reviewed, "Example.Native", null),
                ("normalized repository copy", System.Text.Encoding.UTF8.GetBytes("# gamma\nCopyright © Example\n"), "Example.Native", "does not match its reviewed SHA-256"),
                ("unapproved package", reviewed, "Example.Other", "not an approved resolved dependency")
            ];
            foreach (var fixture in fixtures)
            {
                File.WriteAllBytes(Path.Combine(root, "UPSTREAM.txt"), fixture.RepositoryCopy);
                using var policy = JsonDocument.Parse($$"""{"upstreamNotices":[{"id":"example","file":"UPSTREAM.txt","packageFile":"THIRD-PARTY-NOTICES.txt","sha256":"{{hash}}","packages":["{{fixture.PackageId}}"]}]}""");
                var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                var fixtureErrors = new List<string>();
                ValidateUpstreamNotices(root, policy.RootElement, [packages], resolved, approvals, texts, fixtureErrors);
                if (fixture.Failure is null && (fixtureErrors.Count != 0 || !texts.ContainsKey("UPSTREAM.txt")))
                {
                    errors.Add($"upstream-notice fixture {fixture.Name}: unexpected rejection: {string.Join("; ", fixtureErrors)}");
                }
                else if (fixture.Failure is not null
                    && !fixtureErrors.Any(error => error.Contains(fixture.Failure, StringComparison.Ordinal)))
                {
                    errors.Add($"upstream-notice fixture was incorrectly accepted: {fixture.Name}");
                }
            }

            File.WriteAllBytes(Path.Combine(root, "UPSTREAM.txt"), reviewed);
            File.WriteAllText(Path.Combine(packages, "example.native", "1.0.0", "THIRD-PARTY-NOTICES.txt"), "# gamma\r\n# delta\r\n");
            using var changedPolicy = JsonDocument.Parse($$"""{"upstreamNotices":[{"id":"example","file":"UPSTREAM.txt","packageFile":"THIRD-PARTY-NOTICES.txt","sha256":"{{hash}}","packages":["Example.Native"]}]}""");
            var changedErrors = new List<string>();
            ValidateUpstreamNotices(root, changedPolicy.RootElement, [packages], resolved, approvals, new Dictionary<string, string>(StringComparer.Ordinal), changedErrors);
            if (!changedErrors.Any(error => error.Contains("differs from upstream notice", StringComparison.Ordinal)))
            {
                errors.Add("upstream-notice fixture was incorrectly accepted: changed package notice");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static int CountOccurrences(string content, string marker)
    {
        var count = 0;
        var start = 0;
        while ((start = content.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            start += marker.Length;
        }
        return count;
    }

    private static void ValidatePackage(
        IReadOnlyDictionary<string, Approval> approvals,
        ISet<string> observed,
        ICollection<string> errors,
        string project,
        string scope,
        string id,
        string version)
    {
        if (!approvals.TryGetValue(id, out var approval))
        {
            errors.Add($"{project}: {id} {version} ({scope}) has no approval record");
            return;
        }

        observed.Add(id);
        if (!string.Equals(approval.Version, version, StringComparison.Ordinal))
        {
            errors.Add($"{project}: {id} resolved {version}, approved {approval.Version}");
        }
        if (!approval.Scopes.Contains(scope))
        {
            errors.Add($"{project}: {id} is used in {scope} scope but approved for [{string.Join(", ", approval.Scopes)}]");
        }
    }

    private static int CheckVulnerabilities(string root)
    {
        var auditedProjects = RunReviewedAuditRestore(root);
        using var dependencies = RunPackageList(root, vulnerable: false);
        var expectedProjects = ValidatePackageList(
            dependencies.RootElement, root, vulnerable: false, GetExpectedPackageGraph(root));
        using var result = RunPackageList(root, vulnerable: true);
        ValidatePackageList(result.RootElement, root, vulnerable: true, expectedProjects, auditedProjects);
        Console.WriteLine($"Qualified NuGet audit passed: {expectedProjects.Count} freshly audited projects, no diagnostics or vulnerable packages reported.");
        return 0;
    }

    private static Dictionary<string, Dictionary<string, HashSet<string>>> GetExpectedPackageGraph(string root)
    {
        var projects = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in XDocument.Load(Path.Combine(root, "FTPlus.AgentGraphicDesign.slnx")).Descendants("Project"))
        {
            var path = Path.GetFullPath(project.Attribute("Path")!.Value.Replace('\\', '/'), root);
            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "obj", "project.assets.json")));
            // NuGet 7.0's framework names match the non-RID targets in the restored assets file.
            var frameworks = assets.RootElement.GetProperty("targets").EnumerateObject()
                .Where(target => !target.Name.Contains('/'))
                .ToDictionary(target => target.Name,
                    target => target.Value.EnumerateObject()
                        .Where(library => RequiredString(library.Value, "type") == "package")
                        .Select(library => library.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            if (frameworks.Count == 0 || !projects.TryAdd(path, frameworks))
            {
                throw new InvalidDataException($"Missing restored frameworks or duplicate solution project: {path}");
            }
        }
        if (projects.Count == 0) throw new InvalidDataException("The solution contains no projects");
        return projects;
    }

    private static Dictionary<string, Dictionary<string, HashSet<string>>> ValidatePackageList(
        JsonElement report, string root, bool vulnerable,
        IReadOnlyDictionary<string, Dictionary<string, HashSet<string>>> expectedProjects,
        IReadOnlySet<string>? auditedProjects = null)
    {
        if (vulnerable && (auditedProjects is null || !auditedProjects.SetEquals(expectedProjects.Keys)))
        {
            throw new InvalidDataException("A complete reviewed restore audit is required before accepting a filtered report");
        }
        // NuGet.Client 7.0.3.1 ListPackageJsonRenderer/ListPackageCommandRunner (D-01):
        // warnings can accompany exit 0; filtered clean audits can omit frameworks entirely.
        if (report.GetProperty("version").GetInt32() != 1 ||
            RequiredString(report, "parameters") != (vulnerable ? "--vulnerable --include-transitive" : "--include-transitive"))
        {
            throw new InvalidDataException("Unexpected NuGet JSON version or report parameters");
        }
        foreach (var problem in OptionalArray(report, "problems"))
        {
            var level = RequiredString(problem, "level");
            var text = RequiredString(problem, "text");
            var project = problem.TryGetProperty("project", out _) ? RequiredString(problem, "project") : "solution";
            throw new InvalidDataException($"NuGet {level} ({project}): {text}");
        }
        if (vulnerable)
        {
            var sources = report.GetProperty("sources").EnumerateArray().ToArray();
            if (sources.Length != 1 || sources[0].ValueKind != JsonValueKind.String || sources[0].GetString() != AuditSource)
            {
                throw new InvalidDataException("NuGet audit sources differ from the reviewed nuget.org-only profile");
            }
        }

        var projects = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        var advisoryCount = 0;
        foreach (var project in report.GetProperty("projects").EnumerateArray())
        {
            var path = Path.GetFullPath(RequiredString(project, "path"), root);
            var frameworks = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (!expectedProjects.TryGetValue(path, out var expectedFrameworks) || !projects.TryAdd(path, frameworks))
            {
                throw new InvalidDataException($"Unexpected or duplicate NuGet project: {path}");
            }
            foreach (var framework in OptionalArray(project, "frameworks"))
            {
                var name = RequiredString(framework, "framework");
                var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!expectedFrameworks.TryGetValue(name, out var expectedPackages) || !frameworks.TryAdd(name, packages))
                {
                    throw new InvalidDataException($"Unexpected or duplicate NuGet framework: {path} {name}");
                }
                foreach (var group in new[] { "topLevelPackages", "transitivePackages" })
                {
                    foreach (var package in OptionalArray(framework, group))
                    {
                        var identity = RequiredString(package, "id") + "/" + RequiredString(package, "resolvedVersion");
                        if (group == "topLevelPackages") _ = RequiredString(package, "requestedVersion");
                        if (!expectedPackages.Contains(identity) || !packages.Add(identity))
                        {
                            throw new InvalidDataException($"Unexpected or duplicate NuGet package: {path} {name} {identity}");
                        }
                        var advisories = OptionalArray(package, "vulnerabilities").ToArray();
                        if (vulnerable && advisories.Length == 0)
                        {
                            throw new InvalidDataException($"Filtered audit package has no advisory data: {identity}");
                        }
                        foreach (var advisory in advisories)
                        {
                            _ = RequiredString(advisory, "severity");
                            _ = RequiredString(advisory, "advisoryurl");
                            advisoryCount++;
                        }
                    }
                }
                if (!vulnerable && !expectedPackages.SetEquals(packages))
                {
                    throw new InvalidDataException($"Incomplete NuGet package listing: {path} {name}");
                }
            }
            if (!vulnerable && !expectedFrameworks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(frameworks.Keys))
            {
                throw new InvalidDataException($"Incomplete NuGet framework listing: {path}");
            }
        }
        if (projects.Count == 0 || !expectedProjects.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(projects.Keys))
        {
            throw new InvalidDataException("Incomplete NuGet project listing; expected every solution project exactly once");
        }
        if (advisoryCount != 0)
        {
            throw new InvalidDataException($"Structured NuGet audit found {advisoryCount} vulnerable package advisory record(s)");
        }
        return projects;
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { } value && !string.IsNullOrWhiteSpace(value)
            ? value : throw new InvalidDataException($"NuGet JSON has an empty {name}");

    private static IEnumerable<JsonElement> OptionalArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.EnumerateArray() : [];

    private static JsonDocument RunPackageList(string root, bool vulnerable)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("package");
        startInfo.ArgumentList.Add("list");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add("FTPlus.AgentGraphicDesign.slnx");
        if (vulnerable)
        {
            startInfo.ArgumentList.Add("--vulnerable");
            startInfo.ArgumentList.Add("--configfile");
            startInfo.ArgumentList.Add(Path.Combine(root, "scripts", "RepoChecks", "NuGet.Config"));
        }
        startInfo.ArgumentList.Add("--include-transitive");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--output-version");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--no-restore");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet package list");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(errorTask.Result))
        {
            throw new InvalidOperationException($"dotnet package list failed or emitted diagnostics (exit {process.ExitCode}): {errorTask.Result.Trim()}\n{outputTask.Result.Trim()}");
        }

        return JsonDocument.Parse(outputTask.Result, new JsonDocumentOptions { AllowDuplicateProperties = false });
    }

    private static int CheckCoverage(string directory, string thresholdText)
    {
        if (!decimal.TryParse(thresholdText, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold))
        {
            throw new ArgumentException($"Invalid coverage threshold: {thresholdText}");
        }
        if (threshold is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdText), "Coverage threshold must be between 0 and 100");
        }

        var files = Directory.GetFiles(Path.GetFullPath(directory), "coverage.cobertura.xml", SearchOption.AllDirectories)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidDataException("No coverage.cobertura.xml files found");
        }

        var totals = MergeCoverage(files.Select(XDocument.Load));

        if (totals.Valid == 0)
        {
            throw new InvalidDataException("Coverage reports contain no valid lines");
        }

        var percentage = totals.Covered * 100m / totals.Valid;
        Console.WriteLine($"Combined unique-source line coverage: {percentage:F2}% ({totals.Covered}/{totals.Valid}) across {files.Length} report(s); required: {threshold:F2}%.");
        return percentage >= threshold ? 0 : 1;
    }

    private static CoverageTotals MergeCoverage(IEnumerable<XDocument> reports)
    {
        var lines = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var report in reports)
        {
            var root = report.Root ?? throw new InvalidDataException("Coverage XML has no root");
            var source = root.Descendants("source").Select(element => element.Value).FirstOrDefault() ?? string.Empty;
            foreach (var @class in root.Descendants("class"))
            {
                var filename = @class.Attribute("filename")?.Value;
                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                var normalizedPath = NormalizeCoveragePath(source, filename);
                if (IsGeneratedCoveragePath(normalizedPath))
                {
                    continue;
                }

                var classLines = @class.Element("lines")?.Elements("line") ?? [];
                foreach (var line in classLines)
                {
                    if (!int.TryParse(line.Attribute("number")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    {
                        throw new InvalidDataException($"Coverage line has an invalid number in {filename}");
                    }
                    if (!long.TryParse(line.Attribute("hits")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hits))
                    {
                        throw new InvalidDataException($"Coverage line {number} has invalid hits in {filename}");
                    }

                    var key = $"{normalizedPath}\0{number}";
                    lines[key] = hits > 0 || (lines.TryGetValue(key, out var covered) && covered);
                }
            }
        }

        return new CoverageTotals(lines.Values.LongCount(value => value), lines.Count);
    }

    private static string NormalizeCoveragePath(string source, string filename)
    {
        var path = Path.IsPathRooted(filename) ? filename : Path.Combine(source, filename);
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    private static bool IsGeneratedCoveragePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase);
    }

    // Every tool package carries the license, the README, the policy notice file and each
    // upstream notice file the dependency policy declares, at the package root.
    private static IReadOnlyList<string> PackagedLegalFiles(string root)
    {
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetFullPath(root), ".config", "dependencies.json")));
        var files = new List<string> { "LICENSE", "README.md", policy.RootElement.GetProperty("policy").GetProperty("noticeFile").GetString()! };
        if (policy.RootElement.TryGetProperty("upstreamNotices", out var upstreamNotices))
        {
            files.AddRange(upstreamNotices.EnumerateArray().Select(upstream => upstream.GetProperty("file").GetString()!));
        }
        return files;
    }

    private static int CheckPackage(
        string packagePath,
        string expectedId,
        string expectedVersion,
        string expectedCommand,
        string root,
        string expectedCommit)
    {
        using var archive = ZipFile.OpenRead(Path.GetFullPath(packagePath));
        var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var nuspecStream = nuspecEntry.Open();
        var nuspec = XDocument.Load(nuspecStream);
        var metadata = nuspec.Descendants().Single(element => element.Name.LocalName == "metadata");
        var id = metadata.Elements().Single(element => element.Name.LocalName == "id").Value;
        var version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
        var packageType = metadata.Descendants().SingleOrDefault(element => element.Name.LocalName == "packageType")?.Attribute("name")?.Value;
        var repository = metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "repository");
        var expectedRepositoryUrl = XDocument.Load(Path.Combine(Path.GetFullPath(root), "Directory.Build.props"))
            .Descendants("RepositoryUrl").Single().Value;

        var errors = new List<string>();
        if (!string.Equals(id, expectedId, StringComparison.Ordinal)) errors.Add($"package ID is {id}, expected {expectedId}");
        if (!string.Equals(version, expectedVersion, StringComparison.Ordinal)) errors.Add($"package version is {version}, expected {expectedVersion}");
        if (!string.Equals(packageType, "DotnetTool", StringComparison.Ordinal)) errors.Add($"package type is {packageType ?? "missing"}, expected DotnetTool");
        if (repository is null)
        {
            errors.Add("package repository metadata is missing");
        }
        else
        {
            var repositoryType = repository.Attribute("type")?.Value;
            var repositoryUrl = repository.Attribute("url")?.Value;
            var repositoryCommit = repository.Attribute("commit")?.Value;
            if (!string.Equals(repositoryType, "git", StringComparison.Ordinal))
                errors.Add($"package repository type is {repositoryType ?? "missing"}, expected git");
            if (!string.Equals(repositoryUrl, expectedRepositoryUrl, StringComparison.Ordinal))
                errors.Add($"package repository URL is {repositoryUrl ?? "missing"}, expected {expectedRepositoryUrl}");
            if (!string.Equals(repositoryCommit, expectedCommit, StringComparison.Ordinal))
                errors.Add($"package repository commit is {repositoryCommit ?? "missing"}, expected {expectedCommit}");
        }

        foreach (var legalFile in PackagedLegalFiles(root))
        {
            var entry = archive.Entries.SingleOrDefault(candidate => string.Equals(candidate.FullName, legalFile, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                errors.Add($"package is missing root file {legalFile}");
                continue;
            }

            // Byte comparison: an upstream notice is redistributed verbatim, line endings included.
            var repositoryFile = Path.Combine(Path.GetFullPath(root), legalFile);
            using var packaged = new MemoryStream();
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(packaged);
            }
            if (!packaged.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(repositoryFile)))
            {
                errors.Add($"packaged {legalFile} differs from repository file");
            }
        }

        var settingsEntry = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith("DotnetToolSettings.xml", StringComparison.OrdinalIgnoreCase));
        if (settingsEntry is null)
        {
            errors.Add("package is missing DotnetToolSettings.xml");
        }
        else
        {
            using var settingsStream = settingsEntry.Open();
            var command = XDocument.Load(settingsStream).Descendants("Command").SingleOrDefault()?.Attribute("Name")?.Value;
            if (!string.Equals(command, expectedCommand, StringComparison.Ordinal))
            {
                errors.Add($"tool command is {command ?? "missing"}, expected {expectedCommand}");
            }
        }

        return Report(errors, $"Package contract passed: {id} {version} ({expectedCommand}), repository provenance and legal files match.");
    }

    private static int CheckReadiness(string artifactsDirectory, string expectedVersion)
    {
        var root = Path.GetFullPath(artifactsDirectory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var errors = new List<string>();
        ValidateReadinessVersion(manifest.RootElement, expectedVersion, errors);
        var generatedText = manifest.RootElement.GetProperty("generated").GetString();
        if (!DateTimeOffset.TryParse(generatedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var generated))
        {
            errors.Add($"manifest generated value is not an ISO timestamp: {generatedText}");
        }
        else if (generated < DateTimeOffset.UtcNow.AddDays(-1) || generated > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            errors.Add($"manifest timestamp is not current: {generated:O}");
        }

        foreach (var artifact in manifest.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            foreach (var file in artifact.GetProperty("files").EnumerateArray())
            {
                var relative = file.GetString()!;
                var fullPath = Path.GetFullPath(Path.Combine(root, relative));
                if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(fullPath))
                {
                    errors.Add($"manifest artifact is missing or escapes output directory: {relative}");
                }
            }
        }

        var nestedPath = Path.Combine(root, "nested-groups.agd.json");
        if (File.Exists(nestedPath))
        {
            using var nested = JsonDocument.Parse(File.ReadAllText(nestedPath));
            var maxDepth = GetNodeDepth(nested.RootElement.GetProperty("root"), 0);
            if (maxDepth < 3)
            {
                errors.Add($"nested-groups hierarchy depth is {maxDepth}, expected at least 3");
            }
        }
        else
        {
            errors.Add("nested-groups.agd.json is missing");
        }

        return Report(errors, "Readiness manifest passed: current timestamp, complete files, and nested scene hierarchy.");
    }

    private static void ValidateReadinessVersion(JsonElement manifest, string expectedVersion, ICollection<string> errors)
    {
        var actualVersion = manifest.TryGetProperty("version", out var version) ? version.GetString() : null;
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal))
        {
            errors.Add($"manifest version is {actualVersion ?? "missing"}, expected {expectedVersion}");
        }
    }

    private static int RunSelfTests()
    {
        var errors = new List<string>();
        var source = Path.Combine(Path.GetTempPath(), "agd-repochecks-self-test", "src");
        var first = CoverageFixture(source,
            ("Product/A.cs", 10, 1),
            ("Product/A.cs", 11, 0),
            ("Product/obj/Generated.g.cs", 1, 1));
        var second = CoverageFixture(source,
            ("Product/./A.cs", 10, 0),
            ("Product/A.cs", 11, 2),
            ("Product/A.cs", 12, 0));
        var totals = MergeCoverage([first, second]);
        if (totals != new CoverageTotals(2, 3))
        {
            errors.Add($"coverage merge returned {totals.Covered}/{totals.Valid}, expected 2/3");
        }

        using var matchingManifest = JsonDocument.Parse("""{"version":"1.2.3"}""");
        ValidateReadinessVersion(matchingManifest.RootElement, "1.2.3", errors);
        using var staleManifest = JsonDocument.Parse("""{"version":"1.2.2"}""");
        var staleErrors = new List<string>();
        ValidateReadinessVersion(staleManifest.RootElement, "1.2.3", staleErrors);
        if (staleErrors.Count != 1)
        {
            errors.Add("readiness version validation did not reject a stale manifest");
        }

        RunPackageListSelfTests(errors);
        RunRestoreAuditSelfTests(errors);
        RunWorkflowPolicySelfTests(errors);
        RunRequiredNoticeSelfTests(errors);
        return Report(errors, "RepoChecks self-tests passed: coverage, readiness, NuGet report validation, restore-audit qualification, workflow policy, required-notice licenses and upstream notices.");
    }

    private static void RunPackageListSelfTests(ICollection<string> errors)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "agd-repochecks-self-test"));
        var expected = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(root, "App.csproj")] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["net10.0"] = new(["Example/1.0.0", "Native/2.0.0"], StringComparer.OrdinalIgnoreCase),
                ["net9.0"] = new(["Example/1.0.0"], StringComparer.OrdinalIgnoreCase)
            },
            [Path.Combine(root, "Probe.csproj")] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["net10.0"] = new(["Native/2.0.0"], StringComparer.OrdinalIgnoreCase)
            }
        };
        const string listing = """
            {"version":1,"parameters":"--include-transitive","projects":[
              {"path":"App.csproj","frameworks":[
                {"framework":"net10.0","topLevelPackages":[{"id":"Example","requestedVersion":"1.0.0","resolvedVersion":"1.0.0"}],
                  "transitivePackages":[{"id":"Native","resolvedVersion":"2.0.0"}]},
                {"framework":"net9.0","topLevelPackages":[{"id":"Example","requestedVersion":"1.0.0","resolvedVersion":"1.0.0"}]}]},
              {"path":"Probe.csproj","frameworks":[{"framework":"net10.0","transitivePackages":[{"id":"Native","resolvedVersion":"2.0.0"}]}]}]}
            """;
        const string cleanAudit = """
            {"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],
              "projects":[{"path":"App.csproj"},{"path":"Probe.csproj"}]}
            """;
        const string vulnerableAudit = """
            {"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],
              "projects":[{"path":"App.csproj","frameworks":[{"framework":"net10.0",
                "topLevelPackages":[{"id":"Example","requestedVersion":"1.0.0","resolvedVersion":"1.0.0",
                  "vulnerabilities":[{"severity":"High","advisoryurl":"https://example.test/advisory"}]}],
                "transitivePackages":[{"id":"Native","resolvedVersion":"2.0.0",
                  "vulnerabilities":[{"severity":"Moderate","advisoryurl":"https://example.test/native"}]}]}]},
                {"path":"Probe.csproj"}]}
            """;
        var fixtures = new List<(string Name, string Json, bool Audit, string? Failure)>
        {
            ("unfiltered listing with omitted optional arrays", listing, false, null),
            ("clean audit with omitted frameworks/problems", cleanAudit, true, null),
            ("top-level and transitive vulnerabilities", vulnerableAudit, true, "2 vulnerable package advisory"),
            ("duplicate report version", cleanAudit.Replace("\"version\":1", "\"version\":2,\"version\":1", StringComparison.Ordinal), true, ""),
            ("duplicate problems masking warning", cleanAudit.Replace("\"version\":1",
                "\"version\":1,\"problems\":[{\"level\":\"warning\",\"text\":\"No audit data\"}],\"problems\":[]", StringComparison.Ordinal), true, ""),
            ("truncated JSON", "{", true, ""),
            ("empty object", "{}", true, ""),
            ("null report", "null", true, "")
        };
        void Fixture(string name, string json, bool audit, Action<JsonNode> change, string? failure = "")
        {
            var node = JsonNode.Parse(json)!;
            change(node);
            fixtures.Add((name, node.ToJsonString(), audit, failure));
        }

        Fixture("clean explicit empty arrays", cleanAudit, true, node =>
        {
            node["problems"] = new JsonArray();
            node["projects"]![0]!["frameworks"] = JsonNode.Parse("""[{"framework":"net10.0","topLevelPackages":[]}]""");
            node["projects"]![1]!["frameworks"] = new JsonArray();
        }, failure: null);
        foreach (var audit in new[] { false, true })
        {
            var json = audit ? cleanAudit : listing;
            Fixture("source unavailable / warning", json, audit, node => node["problems"] = JsonNode.Parse(
                """[{"level":"warning","text":"Audit source has no data","project":"App.csproj"}]"""), "NuGet warning");
            Fixture("global error", json, audit, node => node["problems"] = JsonNode.Parse(
                """[{"level":"error","text":"Source unavailable"}]"""), "NuGet error");
            Fixture("malformed problems", json, audit, node => node["problems"] = new JsonObject());
            Fixture("malformed diagnostic", json, audit, node => node["problems"] = JsonNode.Parse("""[{"level":"warning"}]"""));
            Fixture("unknown diagnostic level", json, audit, node => node["problems"] = JsonNode.Parse("""[{"level":"unknown","text":"Diagnostic"}]"""));
            Fixture("partial projects", json, audit, node => node["projects"]!.AsArray().RemoveAt(1), "Incomplete NuGet project");
            Fixture("duplicate project", json, audit, node => node["projects"]!.AsArray().Add(node["projects"]![0]!.DeepClone()), "duplicate NuGet project");
            Fixture("unexpected project", json, audit, node => node["projects"]![1]!["path"] = "Other.csproj", "Unexpected");
            Fixture("empty projects", json, audit, node => node["projects"] = new JsonArray());
            Fixture("missing projects", json, audit, node => node.AsObject().Remove("projects"));
            Fixture("null projects", json, audit, node => node["projects"] = null);
            Fixture("empty project path", json, audit, node => node["projects"]![0]!["path"] = "");
            Fixture("null frameworks", json, audit, node => node["projects"]![0]!["frameworks"] = null);
            Fixture("wrong output version", json, audit, node => node["version"] = 2);
            Fixture("wrong parameters", json, audit, node => node["parameters"] = "--vulnerable");
        }
        Fixture("missing audit sources", cleanAudit, true, node => node.AsObject().Remove("sources"));
        Fixture("empty audit sources", cleanAudit, true, node => node["sources"] = new JsonArray());
        Fixture("null audit source", cleanAudit, true, node => node["sources"] = JsonNode.Parse("[null]"));
        Fixture("unreviewed audit source", cleanAudit, true, node => node["sources"]![0] = "https://example.test/index.json");
        Fixture("missing dependency frameworks", listing, false, node => node["projects"]![0]!.AsObject().Remove("frameworks"));
        Fixture("partial dependency frameworks", listing, false, node => node["projects"]![0]!["frameworks"]!.AsArray().RemoveAt(1));
        Fixture("duplicate framework", listing, false, node => node["projects"]![0]!["frameworks"]!.AsArray().Add(node["projects"]![0]!["frameworks"]![0]!.DeepClone()));
        Fixture("missing package group", listing, false, node => node["projects"]![0]!["frameworks"]![0]!.AsObject().Remove("transitivePackages"));
        Fixture("malformed package array", listing, false, node => node["projects"]![0]!["frameworks"]![0]!["topLevelPackages"] = new JsonObject());
        Fixture("missing package version", listing, false, node => node["projects"]![0]!["frameworks"]![0]!["topLevelPackages"]![0]!.AsObject().Remove("resolvedVersion"));
        Fixture("missing requested version", listing, false, node => node["projects"]![0]!["frameworks"]![0]!["topLevelPackages"]![0]!.AsObject().Remove("requestedVersion"));
        Fixture("missing advisory array", vulnerableAudit, true, node => node["projects"]![0]!["frameworks"]![0]!["topLevelPackages"]![0]!.AsObject().Remove("vulnerabilities"));
        Fixture("malformed advisory array", vulnerableAudit, true, node => node["projects"]![0]!["frameworks"]![0]!["topLevelPackages"]![0]!["vulnerabilities"] = new JsonObject());
        Fixture("malformed advisory", vulnerableAudit, true, node => node["projects"]![0]!["frameworks"]![0]!["topLevelPackages"]![0]!["vulnerabilities"] = JsonNode.Parse("[{}]"));

        foreach (var fixture in fixtures)
        {
            try
            {
                using var document = JsonDocument.Parse(fixture.Json, new JsonDocumentOptions { AllowDuplicateProperties = false });
                ValidatePackageList(document.RootElement, root, fixture.Audit, expected, expected.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
                if (fixture.Failure is not null) errors.Add($"NuGet fixture was incorrectly accepted: {fixture.Name}");
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
            {
                if (fixture.Failure is null || !ex.Message.Contains(fixture.Failure, StringComparison.Ordinal))
                {
                    errors.Add($"NuGet fixture {fixture.Name}: unexpected rejection: {ex.Message}");
                }
            }
        }
        using var otherwiseClean = JsonDocument.Parse(cleanAudit);
        foreach (var evidence in new IReadOnlySet<string>?[] { null, new HashSet<string>(), new HashSet<string> { Path.Combine(root, "App.csproj") } })
        {
            try
            {
                ValidatePackageList(otherwiseClean.RootElement, root, true, expected, evidence);
                errors.Add("An otherwise-clean filtered report passed without complete restore-audit evidence");
            }
            catch (InvalidDataException) { }
        }
        Console.WriteLine($"Checked {fixtures.Count} structured NuGet report fixtures.");
    }

    private static XDocument CoverageFixture(string source, params (string File, int Line, long Hits)[] entries)
    {
        return new XDocument(
            new XElement("coverage",
                new XElement("sources", new XElement("source", source)),
                new XElement("packages",
                    new XElement("package",
                        new XElement("classes",
                            entries.GroupBy(entry => entry.File, StringComparer.Ordinal)
                                .Select(group => new XElement("class",
                                    new XAttribute("filename", group.Key),
                                    new XElement("lines", group.Select(entry => new XElement("line",
                                        new XAttribute("number", entry.Line),
                                        new XAttribute("hits", entry.Hits)))))))))));
    }

    private static int GetNodeDepth(JsonElement node, int depth)
    {
        var maxDepth = depth;
        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                maxDepth = Math.Max(maxDepth, GetNodeDepth(child, depth + 1));
            }
        }
        return maxDepth;
    }

    private static int Report(IReadOnlyCollection<string> errors, string successMessage)
    {
        if (errors.Count == 0)
        {
            Console.WriteLine(successMessage);
            return 0;
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }
        return 1;
    }

    private static string GetRoot(string[] args, int index) =>
        Path.GetFullPath(args.Length > index ? args[index] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: RepoChecks dependencies|vulnerabilities [ROOT]");
        Console.Error.WriteLine("       RepoChecks coverage <DIRECTORY> <MINIMUM_PERCENT>");
        Console.Error.WriteLine("       RepoChecks package <NUPKG> <ID> <VERSION> <COMMAND> <ROOT> <COMMIT>");
        Console.Error.WriteLine("       RepoChecks readiness <ARTIFACTS_DIRECTORY> <EXPECTED_VERSION>");
        Console.Error.WriteLine("       RepoChecks workflows [ROOT]");
        Console.Error.WriteLine("       RepoChecks self-test");
        return 2;
    }

    private sealed record Approval(string Id, string Version, string License, HashSet<string> Scopes, string? NoticeMarker);
    private sealed record CoverageTotals(long Covered, long Valid);
}
