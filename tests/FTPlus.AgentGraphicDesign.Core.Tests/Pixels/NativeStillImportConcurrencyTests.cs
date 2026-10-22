using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;
using Microsoft.Win32.SafeHandles;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

[Collection(NativePixelCollection.Name)]
public sealed class NativeStillImportConcurrencyTests : NativePixelTestBase
{
    private readonly NativePixelService _service = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateAdmittedImportProcessesSerializeCreateOrSameSnapshotAppend(bool append)
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        CreatePixelAssetResult? existing = append ? await _service.CreateAsync(new(asset, "Original", 1, 1)) : null;
        string snapshot = Path.Combine(root, "snapshot.json");
        if (existing is not null) File.WriteAllText(snapshot, JsonSerializer.Serialize(existing.State.Snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        string workerAssembly = await BuildWorker(root);
        // Both children prove actual native admission while this real OS lock prevents publication.
        using var held = new FileStream(PixelFixture.LockPath(root, "chosen"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var first = StartWorker(workerAssembly, append, asset, descriptor, snapshot);
        using var second = StartWorker(workerAssembly, append, asset, descriptor, snapshot);
        var ready = await Task.WhenAll(first.Process.StandardOutput.ReadLineAsync(), second.Process.StandardOutput.ReadLineAsync())
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(ready, line => Assert.Equal("ADMITTED", line));
        Assert.False(first.Process.HasExited);
        Assert.False(second.Process.HasExited);
        if (append) Assert.Equal(existing!.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        else Assert.False(Directory.Exists(asset));
        held.Dispose();
        {
            var results = await Task.WhenAll(ReadWorker(first), ReadWorker(second));
            var winner = Assert.Single(results, result => result.GetProperty("ok").GetBoolean());
            var loser = Assert.Single(results, result => !result.GetProperty("ok").GetBoolean());
            Assert.Equal(append ? "REVISION_CONFLICT" : "PIXEL_ALREADY_EXISTS", loser.GetProperty("error").GetProperty("code").GetString());
            var state = winner.GetProperty("data").GetProperty("state");
            var current = await _service.InspectAsync(new(asset));
            Assert.Equal(append ? 2 : 1, current.Snapshot.Revn);
            Assert.Equal(append ? 2 : 1, current.Layers.Length);
            Assert.Equal(state.GetProperty("snapshot").GetProperty("sourceDigest").GetString(), current.Snapshot.SourceDigest);
            Assert.Equal(StillImportFixture.Hash([17, 34, 51, 0]), winner.GetProperty("data").GetProperty("import").GetProperty("rgbaSha256").GetString());
            Assert.Single(Directory.GetDirectories(Path.Combine(asset, "imports")));
            Assert.Empty(Directory.GetDirectories(root, ".agd-pixels-stage-*"));
            Assert.Empty(Directory.GetDirectories(asset, ".agd-pixels-import-stage-*"));
            Assert.IsType<ValidPixelAssetResult>(await _service.ValidateAsync(new(asset)));
        }
    }

    [Fact]
    public async Task ValidAdmittedAppendCanceledOnRealHeldWriterLockPreservesTokenSourcesAndRecovery()
    {
        string root = NewWorkspace(), asset = Path.Combine(root, "chosen");
        var existing = await _service.CreateAsync(new(asset, "Asset", 1, 1));
        string descriptor = StillImportFixture.Write(Path.Combine(root, "supplier"), 1, 1, [17, 34, 51, 0]);
        var before = Directory.GetFiles(asset, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using var cancellation = new CancellationTokenSource();
        Task<ImportPixelLayerResult> operation;
        using (var held = new FileStream(PixelFixture.LockPath(root, "chosen"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var events = new SafeFileHandle((IntPtr)InotifyInit(0x80800), ownsHandle: true); // NONBLOCK | CLOEXEC.
            Assert.False(events.IsInvalid);
            Assert.True(InotifyWatch(events, PixelFixture.LockPath(root, "chosen"), 0x20) >= 0); // IN_OPEN.
            operation = Task.Run(() => _service.ImportLayerAsync(new(asset, descriptor, existing.State.Snapshot, "Canceled"), cancellation.Token));
            var slot = typeof(NativePixelService).Assembly.GetType("FTPlus.AgentGraphicDesign.Core.Pixels.NativeOperationScope", true)!
                .GetField("_hostSlot", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.True(SpinWait.SpinUntil(() => (int)slot.GetValue(null)! == 1 || operation.IsCompleted, TimeSpan.FromSeconds(5)));
            Assert.False(operation.IsCompleted); // a valid admitted operation is blocked, not pre-cancelled.
            Assert.Equal(1, slot.GetValue(null));
            var buffer = new byte[256];
            // Type/stat probes never open the lock file. Observe the actual acquisition attempt
            // while our FileShare.None owner prevents it, not merely the earlier host admission.
            Assert.True(SpinWait.SpinUntil(() => ReadEvents(events, buffer, (nuint)buffer.Length) >= 16 || operation.IsCompleted,
                TimeSpan.FromSeconds(5)));
            Assert.False(operation.IsCompleted);
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(0, slot.GetValue(null));
            Assert.False(Directory.Exists(Path.Combine(asset, "imports")));
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        Assert.Equal(existing.State.Snapshot, (await _service.InspectAsync(new(asset))).Snapshot);
        Assert.Equal(2, (await _service.ImportLayerAsync(new(asset, descriptor, existing.State.Snapshot, "Recovered"))).State.Snapshot.Revn);
    }

    private static async Task<string> BuildWorker(string root)
    {
        string directory = Path.Combine(root, "worker");
        Directory.CreateDirectory(directory);
        string fixture = Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "tests/FTPlus.AgentGraphicDesign.Core.Tests/Pixels/Fixtures/import-worker.cs.txt");
        File.WriteAllText(Path.Combine(directory, "Program.cs"), File.ReadAllText(fixture));
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"), new XElement("ImplicitUsings", "enable")),
            new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "FTPlus.AgentGraphicDesign.Core"),
                new XElement("HintPath", typeof(NativePixelService).Assembly.Location))))).Save(Path.Combine(directory, "Worker.csproj"));
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        await BuildCommand(directory, "restore", "Worker.csproj", "--configfile", Path.Combine(directory, "NuGet.Config"));
        await BuildCommand(directory, "build", "Worker.csproj", "--configuration", "Release", "--no-restore", "--disable-build-servers");
        return Path.Combine(directory, "bin/Release/net10.0/Worker.dll");
    }

    private static async Task BuildCommand(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    private static Worker StartWorker(string assembly, bool append, string asset, string descriptor, string snapshot)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { assembly, append ? "append" : "create", asset, descriptor, snapshot }) start.ArgumentList.Add(argument);
        var process = Process.Start(start)!;
        return new Worker(process, process.StandardError.ReadToEndAsync());
    }

    private static async Task<JsonElement> ReadWorker(Worker worker)
    {
        string? line = await worker.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(worker.Process.ExitCode is 0 or 1, await worker.Error);
        Assert.False(string.IsNullOrEmpty(line), await worker.Error);
        using var json = JsonDocument.Parse(line!);
        return json.RootElement.Clone();
    }

    private sealed record Worker(Process Process, Task<string> Error) : IDisposable
    {
        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                if (!Process.WaitForExit(5000)) throw new TimeoutException("Worker did not terminate before workspace cleanup.");
            }
            Process.Dispose();
        }
    }

    [DllImport("libc", EntryPoint = "inotify_init1", SetLastError = true)]
    private static extern int InotifyInit(int flags);
    [DllImport("libc", EntryPoint = "inotify_add_watch", SetLastError = true)]
    private static extern int InotifyWatch(SafeFileHandle fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mask);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint ReadEvents(SafeFileHandle fd, byte[] buffer, nuint count);
}
