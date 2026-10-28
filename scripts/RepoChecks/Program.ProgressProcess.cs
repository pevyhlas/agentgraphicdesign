using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FTPlus.AgentGraphicDesign.RepoChecks;

internal static partial class Program
{
    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int ProgressSetSession();
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int ProgressKill(int pid, int signal);

    // A small owned helper keeps the session/group leader alive even after the direct command
    // exits. Its PID cannot be reused while the parent terminates the group, including orphaned
    // grandchildren retaining redirected pipes. This is cleanup, not a sandbox against setsid.
    private static int ProgressExec(string readyFile, string exitFile, string executable, string[] args)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || ProgressSetSession() != Environment.ProcessId)
            throw new InvalidOperationException("Cannot create owned POSIX command session");
        ProgressSignal(readyFile, Environment.ProcessId);
        // Parent death/cancellation closes its control pipe. Clean up even while the direct
        // command is still running; the child command itself never consumes this control pipe.
        _ = Task.Run(() => { _ = Console.ReadLine(); _ = ProgressKill(-Environment.ProcessId, 9); });
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.RedirectStandardInput = true;
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var child = Process.Start(info) ?? throw new IOException("Cannot start " + executable);
        child.StandardInput.Close();
        child.WaitForExit();
        ProgressSignal(exitFile, child.ExitCode);
        Thread.Sleep(Timeout.Infinite); // parent owns the group until it observes the direct exit
        return 0;
    }

    private static void ProgressSignal(string path, int value)
    {
        File.WriteAllText(path + ".tmp", value.ToString(CultureInfo.InvariantCulture));
        File.Move(path + ".tmp", path, overwrite: false);
    }

    private static (int Exit, string Output) ProgressCommand(string root, string output, string label, string executable, params string[] args) =>
        ProgressCommandBounded(root, output, label, executable, args, 300_000, ProgressMaxBytes);

    private static (int Exit, string Output) ProgressCommandBounded(string root, string output, string label, string executable, string[] args, int timeoutMs, int logLimit)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) throw new PlatformNotSupportedException("Progress command ownership requires POSIX sessions");
        var ready = Path.Combine(output, label + ".group");
        var completed = Path.Combine(output, label + ".command-exit");
        if (File.Exists(ready) || File.Exists(completed)) throw new IOException("Preserve previous command attempts");
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in new[] { typeof(Program).Assembly.Location, "_progress-exec", ready, completed, executable }.Concat(args)) info.ArgumentList.Add(arg);
        File.WriteAllText(Path.Combine(output, label + ".command.json"), JsonSerializer.Serialize(new { executable, args, root, helper = info.ArgumentList.ToArray() }));
        // Sinks are opened before any process starts. A creation failure cannot leak a command.
        using var stdout = File.Create(Path.Combine(output, label + ".stdout.log"));
        using var stderr = File.Create(Path.Combine(output, label + ".stderr.log"));
        using var process = Process.Start(info) ?? throw new IOException("Cannot start progress helper");
        var copy = Task.WhenAll(CopyProgressLog(process.StandardOutput.BaseStream, stdout, logLimit), CopyProgressLog(process.StandardError.BaseStream, stderr, logLimit));
        var exit = 125;
        var clock = Stopwatch.StartNew();
        try
        {
            while (!File.Exists(completed) && !process.HasExited && !copy.IsFaulted && clock.ElapsedMilliseconds < timeoutMs)
                process.WaitForExit(20);
            if (copy.IsFaulted) copy.GetAwaiter().GetResult();
            if (clock.ElapsedMilliseconds >= timeoutMs) exit = 124;
            else if (!File.Exists(completed) || process.HasExited) throw new IOException("Progress helper exited before command completion");
            else exit = int.Parse(ProgressText(completed), CultureInfo.InvariantCulture);
        }
        finally
        {
            try
            {
                var ownsGroup = false;
                try { ownsGroup = File.Exists(ready) && ProgressText(ready) == process.Id.ToString(CultureInfo.InvariantCulture); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fall back to the live helper tree */ }
                if (ownsGroup)
                {
                    if (ProgressKill(-process.Id, 9) != 0 && Marshal.GetLastPInvokeError() != 3)
                        throw new IOException("Cannot terminate owned progress group");
                }
                else if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(10_000)) throw new TimeoutException("Progress group leader did not terminate");
                if (!copy.Wait(10_000)) throw new TimeoutException("Progress output did not close after group termination");
            }
            finally { File.WriteAllText(Path.Combine(output, label + ".exit"), exit.ToString(CultureInfo.InvariantCulture)); }
        }
        stdout.Dispose(); stderr.Dispose();
        Console.WriteLine($"macos-progress {label}: exit {exit}");
        return (exit, ProgressText(Path.Combine(output, label + ".stdout.log")));
    }

    private static async Task CopyProgressLog(Stream input, Stream output, int limit)
    {
        var buffer = new byte[8192];
        var total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) != 0)
        {
            total = checked(total + read);
            if (total > limit) throw new InvalidDataException("Subprocess log exceeded its bound; run invalid");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static void RunProgressProcessSelfTests(ICollection<string> errors)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) throw new PlatformNotSupportedException("Process lifecycle fixtures require a POSIX host");
        var dir = Path.Combine(Path.GetTempPath(), "agd-progress-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string publish = "sleep 60 & printf '%s %s' \"$$\" \"$!\" > \"$1\"; : > \"$2\"; ";
            var descendant = Path.Combine(dir, "descendant.pid");
            var result = ProgressCommandBounded(dir, dir, "descendant", "/bin/sh",
                ["-c", publish + "printf done", "fixture", descendant, Path.Combine(dir, "descendant.ready")], 10000, 1024);
            if (result != (0, "done")) errors.Add("Direct command exit/output was not preserved");
            AssertProgressFixtureStopped(descendant, errors);
            var timeoutPids = Path.Combine(dir, "timeout.pids");
            var timeout = ProgressCommandBounded(dir, dir, "timeout", "/bin/sh", ["-c", publish + "wait", "fixture", timeoutPids, Path.Combine(dir, "timeout.ready")], 3000, 1024);
            if (timeout.Exit != 124) errors.Add("Command timeout was not classified explicitly");
            AssertProgressFixtureStopped(timeoutPids, errors);
            var cancelledReady = Path.Combine(dir, "cancelled.child-ready");
            var cancelledPids = Path.Combine(dir, "cancelled.pids");
            using (var helper = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { typeof(Program).Assembly.Location, "_progress-exec", Path.Combine(dir, "cancelled.group"), Path.Combine(dir, "cancelled.command-exit"), "/bin/sh", "-c", publish + "wait", "fixture", cancelledPids, cancelledReady },
                RedirectStandardInput = true,
                UseShellExecute = false
            })!)
            {
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(cancelledReady) && !helper.HasExited && deadline.ElapsedMilliseconds < 10000) helper.WaitForExit(20);
                if (!File.Exists(cancelledReady)) errors.Add("Cancellation fixture never started its command/grandchild");
                helper.StandardInput.Close();
                if (!helper.WaitForExit(2000))
                {
                    helper.Kill(entireProcessTree: true);
                    errors.Add("Helper did not terminate its group when parent control pipe closed");
                }
                if (File.Exists(Path.Combine(dir, "cancelled.command-exit"))) errors.Add("Cancelled command reported normal completion");
            }
            AssertProgressFixtureStopped(cancelledPids, errors);
            Directory.CreateDirectory(Path.Combine(dir, "sink.stdout.log"));
            var marker = Path.Combine(dir, "should-not-start");
            try
            {
                _ = ProgressCommandBounded(dir, dir, "sink", "/bin/sh", ["-c", "touch \"$1\"", "fixture", marker], 10000, 1024);
                errors.Add("Log creation failure unexpectedly succeeded");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (File.Exists(marker) || File.Exists(Path.Combine(dir, "sink.group"))) errors.Add("Command started before log sinks were created");
            try
            {
                _ = ProgressCommandBounded(dir, dir, "overflow", "/bin/sh", ["-c", publish + "while :; do printf 0123456789; done", "fixture", Path.Combine(dir, "overflow.pids"), Path.Combine(dir, "overflow.ready")], 10000, 64);
                errors.Add("Log overflow unexpectedly succeeded");
            }
            catch (Exception ex) when (ex is InvalidDataException or AggregateException) { }
            if (new FileInfo(Path.Combine(dir, "overflow.stdout.log")).Length > 64 || !File.Exists(Path.Combine(dir, "overflow.exit")))
                errors.Add("Log overflow bound/exit evidence missing");
            AssertProgressFixtureStopped(Path.Combine(dir, "overflow.pids"), errors);
        }
        finally { Directory.Delete(dir, recursive: true); }
        Console.WriteLine("Progress subprocess fixtures: orphaned pipe holder, deadline, parent cancellation, prelaunch sink failure and bounded log overflow.");
    }

    private static void AssertProgressFixtureStopped(string file, ICollection<string> errors)
    {
        if (!File.Exists(file)) { errors.Add("Lifecycle fixture did not publish started child/grandchild PIDs: " + Path.GetFileName(file)); return; }
        var pids = File.ReadAllText(file).Split(' ').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (pids.Length != 2 || pids.Distinct().Count() != 2 || pids.Any(p => p <= 1)) throw new InvalidDataException("Bad lifecycle fixture identities");
        foreach (var pid in pids)
        {
            var deadline = Stopwatch.StartNew();
            var running = true;
            while (deadline.ElapsedMilliseconds < 2000)
            {
                using var ps = Process.Start(new ProcessStartInfo("/bin/ps") { ArgumentList = { "-o", "stat=", "-p", pid.ToString(CultureInfo.InvariantCulture) }, RedirectStandardOutput = true, UseShellExecute = false })!;
                if (!ps.WaitForExit(1000)) { ps.Kill(); break; }
                var state = ps.StandardOutput.ReadToEnd().Trim();
                running = state.Length != 0 && !state.StartsWith('Z');
                if (!running) break;
                Thread.Sleep(20);
            }
            if (running)
            {
                _ = ProgressKill(pid, 9); // clean up the fixture even when the implementation regresses
                errors.Add("Lifecycle descendant remains executing: " + Path.GetFileName(file));
            }
        }
    }
}
