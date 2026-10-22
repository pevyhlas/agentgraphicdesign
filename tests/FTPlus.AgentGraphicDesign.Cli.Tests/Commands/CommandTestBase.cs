using System.Diagnostics;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public abstract class CommandTestBase : IDisposable
{
    private static readonly string CliProjectPath = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "FTPlus.AgentGraphicDesign.Cli"));

    protected readonly string TestDir;

    protected CommandTestBase()
    {
        TestDir = Path.Combine(Path.GetTempPath(), $"agd-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TestDir);
    }

    protected (int ExitCode, string Output, string Error) RunCommand(params string[] args) =>
        RunCommandWithInput(standardInput: null, args);

    protected (int ExitCode, string Output, string Error) RunCommandWithInput(string? standardInput, params string[] args)
    {
        var packagedCli = Environment.GetEnvironmentVariable("AGD_TEST_CLI_EXECUTABLE");
        if (packagedCli is not null && (!Path.IsPathFullyQualified(packagedCli) || !File.Exists(packagedCli)))
            throw new InvalidOperationException("AGD_TEST_CLI_EXECUTABLE must name an existing absolute executable path.");
        var psi = new ProcessStartInfo
        {
            FileName = packagedCli ?? "dotnet",
            WorkingDirectory = TestDir,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (packagedCli is null)
        {
            // The suite is built before --no-build execution. Launch the actual CLI
            // directly, like the MCP wire suite, instead of paying MSBuild evaluation
            // for every command. Installed-package qualification still uses its exact shim.
            var assembly = Path.Combine(CliProjectPath, "bin", "Release", "net10.0", "agd.dll");
            if (!File.Exists(assembly))
                throw new InvalidOperationException("Build the Release CLI before running black-box command tests.");
            psi.ArgumentList.Add(assembly);
        }
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            throw new TimeoutException("CLI test process did not exit within 60 seconds.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    protected string CreateTestDocument(string name = "test", int width = 800, int height = 600)
    {
        var path = Path.Combine(TestDir, $"{name}.agd.json");
        var (exitCode, _, stderr) = RunCommand("create", name, "-o", path, "-w", width.ToString(), "-h", height.ToString());
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create test document: exit code {exitCode}, stderr: {stderr}");
        }
        return path;
    }

    protected string CreateInvalidJsonFile(string name = "invalid")
    {
        var path = Path.Combine(TestDir, $"{name}.agd.json");
        File.WriteAllText(path, "{ not valid json }");
        return path;
    }

    protected string CreateMalformedDocument(string name = "malformed")
    {
        var path = Path.Combine(TestDir, $"{name}.agd.json");
        File.WriteAllText(path, """
        {
          "version": 1,
          "id": "00000000-0000-0000-0000-000000000000",
          "name": "",
          "revn": 0,
          "canvas": { "width": 0, "height": -1 },
          "root": {
            "type": "frame",
            "id": "00000000-0000-0000-0000-000000000001",
            "name": "root",
            "width": 100,
            "height": 100,
            "children": []
          }
        }
        """);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(TestDir))
        {
            try
            {
                Directory.Delete(TestDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }
}
