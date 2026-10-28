using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Tests.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Tests.Rendering;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

/// <summary>Test-only owned POSIX session. The parent owns deadlines and all fixture cleanup.</summary>
internal static class FileProbeWorker
{
    internal static string? FixtureRoot { get; private set; }
    internal static string? OwnerRoot { get; private set; }
    internal static bool IsChild => FixtureRoot is not null;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 3 || args[0] != "--file-probe-worker" || !Path.IsPathFullyQualified(args[1])) return 2;
        OwnerRoot = args[1];
        if (!Directory.Exists(OwnerRoot) || new DirectoryInfo(OwnerRoot).LinkTarget is not null || SetSession() != Environment.ProcessId) return 3;
        File.WriteAllText(Path.Combine(OwnerRoot, "ready"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _ = Task.Run(() => { _ = Console.ReadLine(); _ = Kill(-Environment.ProcessId, 9); });
        FixtureRoot = Path.Combine(OwnerRoot, "work");
        Directory.CreateDirectory(FixtureRoot);
        var result = "PASS";
        try
        {
            switch (args[2])
            {
                case "pipeline-fifo":
                    using (var test = new AssetPipelineTests()) await test.UnixFifos_AreRejectedWithoutOpening(args[3]);
                    break;
                case "pipeline-socket":
                    using (var test = new AssetPipelineTests()) await test.UnixSocket_IsRejectedWithoutOpening();
                    break;
                case "artifact-fifo":
                    using (var test = new ArtifactPreflightTests()) await test.UnixFifos_AreRejectedWithoutBlockingOpen(args[3]);
                    break;
                case "artifact-socket":
                    using (var test = new ArtifactPreflightTests()) await test.UnixSocket_IsRejectedBeforeOpen();
                    break;
                case "surface":
                    await SpecialFileSurfaceTests.ProbeAsync(args[3], args[4], args[5]);
                    break;
                case "block":
                    using (var child = Process.Start("/bin/sleep", "60")!)
                    {
                        File.WriteAllText(Path.Combine(OwnerRoot, "descendant"), child.Id.ToString(CultureInfo.InvariantCulture));
                        File.WriteAllText(Path.Combine(FixtureRoot, "owned"), "cleanup control");
                        await Task.Delay(Timeout.Infinite);
                    }
                    break;
                default: throw new ArgumentException("Unknown file probe scenario");
            }
        }
        catch (Exception ex) { result = ex.ToString(); }
        File.WriteAllText(Path.Combine(OwnerRoot, "result.tmp"), result);
        File.Move(Path.Combine(OwnerRoot, "result.tmp"), Path.Combine(OwnerRoot, "result"));
        await Task.Delay(Timeout.Infinite); // group leader stays owned until parent termination/reap
        return 0;
    }

    internal static async Task RunAsync(string scenario, params string[] args)
    {
        var result = await RunOwnedAsync(scenario, args, scenario == "surface" ? 30_000 : 5000);
        if (result.Output.Length != 0) Console.WriteLine(result.Output);
        Assert.False(result.Timeout, $"Special-file worker timed out: {scenario}\n{result.Output}");
        Assert.Equal("PASS", result.Result);
    }

    internal static async Task<(bool Timeout, string Result, string Output)> RunOwnedAsync(string scenario, string[] args, int deadline)
    {
        var root = Path.Combine(Path.GetTempPath(), "agd-fw-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in new[] { typeof(FileProbeWorker).Assembly.Location, "--file-probe-worker", root, scenario }.Concat(args)) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Worker did not start");
            var stdout = ReadBounded(process.StandardOutput);
            var stderr = ReadBounded(process.StandardError);
            var watch = Stopwatch.StartNew();
            var resultPath = Path.Combine(root, "result");
            var timedOut = false;
            string result;
            try
            {
                while (!File.Exists(resultPath) && !process.HasExited && !stdout.IsFaulted && !stderr.IsFaulted && watch.ElapsedMilliseconds < deadline)
                    await Task.Delay(10);
                timedOut = watch.ElapsedMilliseconds >= deadline;
                result = File.Exists(resultPath) ? await ReadResult(resultPath) : "Worker did not report completion";
            }
            finally
            {
                var ready = Path.Combine(root, "ready");
                if (File.Exists(ready) && File.ReadAllText(ready) == process.Id.ToString(CultureInfo.InvariantCulture))
                {
                    if (Kill(-process.Id, 9) != 0 && Marshal.GetLastPInvokeError() != 3) throw new IOException("Could not terminate owned worker group");
                }
                else if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
            }
            var descendant = Path.Combine(root, "descendant");
            if (scenario == "block") Assert.True(File.Exists(descendant), "Control did not start its deliberately blocked descendant");
            if (File.Exists(descendant))
            {
                using var ps = Process.Start(new ProcessStartInfo("/bin/ps")
                {
                    ArgumentList = { "-o", "stat=", "-p", File.ReadAllText(descendant) },
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                })!;
                var state = ReadBounded(ps.StandardOutput);
                await ps.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var text = (await state).Trim();
                Assert.True(text.Length == 0 || text.StartsWith('Z'), "Worker descendant remained executing: " + text);
            }
            return (timedOut, result, await stdout + await stderr);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    private static async Task<string> ReadResult(string path)
    {
        using var reader = new StreamReader(path);
        return await ReadBounded(reader);
    }

    internal static async Task<string> ReadBounded(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) != 0)
        {
            if (text.Length + read > 1_048_576) throw new IOException("Worker output exceeded 1 MiB");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int SetSession();
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}

public sealed class FileProbeWorkerTests
{
    [UnixInspectionFact]
    public async Task DeliberatelyBlockedControl_IsKilledWithItsDescendantAndFixtures()
    {
        var result = await FileProbeWorker.RunOwnedAsync("block", [], 1500);
        Assert.True(result.Timeout);
        Assert.NotEqual("PASS", result.Result);
    }
}
