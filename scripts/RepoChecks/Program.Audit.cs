using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    private const string AuditSource = "https://api.nuget.org/v3/index.json";
    private const string ReviewedAuditConfig = """
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </packageSources>
          <auditSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </auditSources>
        </configuration>
        """;

    private static readonly IReadOnlyDictionary<string, string> ReviewedRestoreProperties = new Dictionary<string, string>
    {
        ["NuGetAudit"] = "true",
        ["NuGetAuditMode"] = "all",
        ["NuGetAuditLevel"] = "low",
        ["TreatWarningsAsErrors"] = "true",
        ["NoWarn"] = "",
        ["WarningsNotAsErrors"] = "",
        ["HideWarningsAndErrors"] = "false",
        ["RestoreIgnoreFailedSources"] = "false",
        ["RestoreForce"] = "true",
        ["RestoreForceEvaluate"] = "false",
        ["DisableImplicitLibraryPacksFolder"] = "true",
        ["DisableImplicitNuGetFallbackFolder"] = "true",
        ["RestoreFallbackFolders"] = "",
        ["RestoreAdditionalProjectSources"] = "",
        ["RestoreAdditionalProjectFallbackFolders"] = ""
    };

    private static HashSet<string> RunReviewedAuditRestore(string root)
    {
        var configPath = Path.Combine(root, "scripts", "RepoChecks", "NuGet.Config");
        ValidateAuditConfig(XElement.Load(configPath));
        var projects = XDocument.Load(Path.Combine(root, "FTPlus.AgentGraphicDesign.slnx")).Descendants("Project")
            .Select(project => Path.GetFullPath(project.Attribute("Path")!.Value.Replace('\\', '/'), root)).ToArray();
        if (projects.Length == 0 || projects.Distinct(StringComparer.OrdinalIgnoreCase).Count() != projects.Length)
        {
            throw new InvalidDataException("Audit requires a nonempty solution with unique project paths");
        }

        // NuGet 7.0.3 VulnerabilityInformationProvider uses its own SourceCacheContext,
        // ignoring restore's --no-http-cache. Isolate that cache for this qualification.
        var cache = Directory.CreateTempSubdirectory("agd-nuget-audit-");
        var audited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var project in projects)
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                start.Environment["NUGET_HTTP_CACHE_PATH"] = cache.FullName;
                foreach (var argument in new[]
                {
                    "restore", project, "--configfile", configPath, "--source", AuditSource,
                    "--locked-mode", "--force", "--no-dependencies", "--no-http-cache", "--disable-build-servers",
                    "-warnaserror", "--verbosity", "quiet"
                }) start.ArgumentList.Add(argument);
                foreach (var (name, value) in ReviewedRestoreProperties) start.ArgumentList.Add($"-p:{name}={value}");
                start.ArgumentList.Add("-getProperty:RestoreProjectCount,RestoreSkippedCount,RestoreProjectsAuditedCount,NETCoreSdkVersion," +
                    string.Join(',', ReviewedRestoreProperties.Keys));

                // Solution -getProperty is unsupported. Restore each entry without recursive
                // restores, so the task must attest exactly one non-skipped audit per entry.
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start reviewed restore");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(output, error);
                ValidateRestoreAuditResult(process.ExitCode, output.Result, error.Result);
                using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json")));
                ValidateRestoredAuditProfile(assets.RootElement, configPath);
                audited.Add(project);
            }
            ValidateAuditConfig(XElement.Load(configPath));
        }
        finally
        {
            cache.Delete(recursive: true);
        }
        Console.WriteLine($"Reviewed restore audit: {audited.Count} projects restored and audited, 0 skipped, nuget.org only, all severities, no suppressions.");
        return audited;
    }

    private static void ValidateAuditConfig(XElement config)
    {
        if (!XNode.DeepEquals(config, XElement.Parse(ReviewedAuditConfig)))
        {
            throw new InvalidDataException("Unreviewed NuGet configuration; qualification requires the isolated nuget.org package/audit profile");
        }
    }

    private static void ValidateRestoreAuditResult(int exitCode, string output, string error)
    {
        // AuditUtility replays GetVulnerabilityInfoResult.Exceptions as NU1900 even
        // with partial data. Missing explicit audit-source data yields NU1905.
        if (exitCode != 0 || !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidDataException($"Reviewed restore audit failed (exit {exitCode}): {error}\n{output}");
        }
        using var result = JsonDocument.Parse(output, new JsonDocumentOptions { AllowDuplicateProperties = false });
        var properties = result.RootElement.GetProperty("Properties");
        if (RequiredString(properties, "NETCoreSdkVersion") != "10.0.113" ||
            RequiredString(properties, "RestoreProjectCount") != "1" ||
            RequiredString(properties, "RestoreSkippedCount") != "0" ||
            RequiredString(properties, "RestoreProjectsAuditedCount") != "1")
        {
            throw new InvalidDataException("Restore did not attest one fresh audit using the reviewed SDK");
        }
        foreach (var (name, value) in ReviewedRestoreProperties)
        {
            if (properties.GetProperty(name).GetString() != value)
            {
                throw new InvalidDataException($"Unreviewed effective restore property: {name}");
            }
        }
    }

    private static void ValidateRestoredAuditProfile(JsonElement assets, string configPath)
    {
        var project = assets.GetProperty("project");
        var restore = project.GetProperty("restore");
        var configs = restore.GetProperty("configFilePaths").EnumerateArray().ToArray();
        var sources = restore.GetProperty("sources").EnumerateObject().ToArray();
        var audit = restore.GetProperty("restoreAuditProperties");
        var warnings = restore.GetProperty("warningProperties");
        var locks = restore.GetProperty("restoreLockProperties");
        if (RequiredString(restore, "projectStyle") != "PackageReference" ||
            configs.Length != 1 || configs[0].GetString() != configPath ||
            sources.Length != 1 || sources[0].Name != AuditSource ||
            RequiredString(audit, "enableAudit") != "true" || RequiredString(audit, "auditMode") != "all" ||
            RequiredString(audit, "auditLevel") != "low" ||
            !warnings.GetProperty("allWarningsAsErrors").GetBoolean() ||
            !locks.GetProperty("restoreLockedMode").GetBoolean() || RequiredString(locks, "restorePackagesWithLockFile") != "true" ||
            OptionalArray(warnings, "noWarn").Any() || OptionalArray(warnings, "warnNotAsError").Any() ||
            OptionalArray(restore, "fallbackFolders").Any() ||
            OptionalArray(assets, "logs").Any() ||
            (audit.TryGetProperty("suppressedAdvisories", out var suppressed) && suppressed.EnumerateObject().Any()))
        {
            throw new InvalidDataException("Restored assets have an unreviewed source/configuration/audit/suppression profile or diagnostics");
        }
        foreach (var framework in project.GetProperty("frameworks").EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies)) continue;
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (OptionalArray(dependency.Value, "noWarn").Any())
                {
                    throw new InvalidDataException($"Package-specific warning suppression is not allowed: {dependency.Name}");
                }
            }
        }
    }

    private static void RunRestoreAuditSelfTests(ICollection<string> errors)
    {
        // A clean filtered report cannot substitute for this restore evidence, especially
        // when list's TryAddSourceVulnerabilityInfo has silently ignored a fetch exception.
        var properties = new JsonObject
        {
            ["RestoreProjectCount"] = "1",
            ["RestoreSkippedCount"] = "0",
            ["RestoreProjectsAuditedCount"] = "1",
            ["NETCoreSdkVersion"] = "10.0.113"
        };
        foreach (var (name, value) in ReviewedRestoreProperties) properties[name] = value;
        var summary = new JsonObject { ["Properties"] = properties };
        ValidateRestoreAuditResult(0, summary.ToJsonString(), "");
        ValidateAuditConfig(XElement.Parse(ReviewedAuditConfig));
        foreach (var (name, value) in new[]
        {
            ("RestoreProjectsAuditedCount", "0"), ("RestoreSkippedCount", "1"), ("RestoreProjectCount", "0"),
            ("NETCoreSdkVersion", "10.0.111"), ("NuGetAudit", "false"), ("NuGetAuditMode", "direct"),
            ("NuGetAuditLevel", "high"), ("NoWarn", "NU1900"), ("WarningsNotAsErrors", "NU1905"),
            ("TreatWarningsAsErrors", "false"), ("RestoreForce", "false")
        })
        {
            var invalid = summary.DeepClone();
            invalid["Properties"]![name] = value;
            try
            {
                ValidateRestoreAuditResult(0, invalid.ToJsonString(), "");
                errors.Add($"Restore evidence accepted invalid {name}");
            }
            catch (InvalidDataException) { }
        }
        foreach (var (exitCode, stderr) in new[] { (1, ""), (0, "retrieval diagnostic") })
        {
            try
            {
                ValidateRestoreAuditResult(exitCode, summary.ToJsonString(), stderr);
                errors.Add("Restore retrieval failure was accepted despite otherwise-clean audit counters");
            }
            catch (InvalidDataException) { }
        }
        foreach (var invalid in new[]
        {
            ReviewedAuditConfig.Replace("https://api.nuget.org/v3/index.json", "https://example.test/index.json", StringComparison.Ordinal),
            ReviewedAuditConfig.Replace("<clear />", "", StringComparison.Ordinal),
            ReviewedAuditConfig.Replace("</configuration>", "<disabledPackageSources><add key=\"nuget.org\" value=\"true\" /></disabledPackageSources></configuration>", StringComparison.Ordinal)
        })
        {
            try
            {
                ValidateAuditConfig(XElement.Parse(invalid));
                errors.Add("Unreviewed audit configuration was accepted");
            }
            catch (InvalidDataException) { }
        }

        const string restoredProfile = """
            {"project":{"restore":{
              "projectStyle":"PackageReference","configFilePaths":["qualified.config"],
              "sources":{"https://api.nuget.org/v3/index.json":{}},
              "restoreAuditProperties":{"enableAudit":"true","auditMode":"all","auditLevel":"low"},
              "warningProperties":{"allWarningsAsErrors":true},
              "restoreLockProperties":{"restoreLockedMode":true,"restorePackagesWithLockFile":"true"}},
              "frameworks":{"net10.0":{"dependencies":{"Example":{"version":"[1.0.0, )"}}}}}}
            """;
        using var validProfile = JsonDocument.Parse(restoredProfile);
        ValidateRestoredAuditProfile(validProfile.RootElement, "qualified.config");
        var invalidProfiles = new Action<JsonNode>[]
        {
            node => node["project"]!["restore"]!["configFilePaths"]![0] = "ambient.config",
            node => node["project"]!["restore"]!["sources"]!["https://example.test/index.json"] = new JsonObject(),
            node => node["project"]!["restore"]!["fallbackFolders"] = JsonNode.Parse("""["unreviewed-cache"]"""),
            node => node["project"]!["restore"]!["restoreAuditProperties"]!["enableAudit"] = "false",
            node => node["project"]!["restore"]!["restoreAuditProperties"]!["auditMode"] = "direct",
            node => node["project"]!["restore"]!["restoreAuditProperties"]!["auditLevel"] = "high",
            node => node["project"]!["restore"]!["restoreAuditProperties"]!["suppressedAdvisories"] = JsonNode.Parse("""{"https://example.test/advisory":{}}"""),
            node => node["project"]!["restore"]!["warningProperties"]!["allWarningsAsErrors"] = false,
            node => node["project"]!["restore"]!["warningProperties"]!["noWarn"] = JsonNode.Parse("""["NU1900"]"""),
            node => node["project"]!["restore"]!["warningProperties"]!["warnNotAsError"] = JsonNode.Parse("""["NU1905"]"""),
            node => node["project"]!["restore"]!["restoreLockProperties"]!["restoreLockedMode"] = false,
            node => node["project"]!["frameworks"]!["net10.0"]!["dependencies"]!["Example"]!["noWarn"] = JsonNode.Parse("""["NU1903"]"""),
            node => node["logs"] = JsonNode.Parse("""[{"code":"NU1900","level":"Error","message":"Retrieval exception"}]""")
        };
        foreach (var change in invalidProfiles)
        {
            var profile = JsonNode.Parse(restoredProfile)!;
            change(profile);
            using var document = JsonDocument.Parse(profile.ToJsonString());
            try
            {
                ValidateRestoredAuditProfile(document.RootElement, "qualified.config");
                errors.Add("Unreviewed restored audit/suppression profile was accepted");
            }
            catch (InvalidDataException) { }
        }

        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "agd-repochecks-self-test"));
        var projectPath = Path.Combine(root, "Example.csproj");
        var expected = new Dictionary<string, Dictionary<string, HashSet<string>>>
        {
            [projectPath] = new() { ["net10.0"] = new(["Example/1.0.0"]) }
        };
        using var cleanFiltered = JsonDocument.Parse("""
            {"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],
              "projects":[{"path":"Example.csproj"}]}
            """);
        foreach (var (exitCode, completed, shouldPass) in new[] { (0, "1", true), (1, "1", false), (0, "0", false) })
        {
            var outcome = summary.DeepClone();
            outcome["Properties"]!["RestoreProjectsAuditedCount"] = completed;
            try
            {
                ValidateRestoreAuditResult(exitCode, outcome.ToJsonString(), "");
                ValidateRestoredAuditProfile(validProfile.RootElement, "qualified.config");
                ValidatePackageList(cleanFiltered.RootElement, root, true, expected, new HashSet<string> { projectPath });
                if (!shouldPass) errors.Add("Hidden retrieval failure passed an otherwise-clean filtered audit");
            }
            catch (InvalidDataException) when (!shouldPass) { }
        }
        Console.WriteLine("Checked restore-audit counters, failure-before-list ordering, isolated configuration, and 13 invalid restored profiles.");
    }
}
