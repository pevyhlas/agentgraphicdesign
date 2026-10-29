using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Tests.Rendering;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class AssetInspectionBoundaryTests : IDisposable
{
    private readonly string _root = PlatformPaths.FullPath(Path.Combine(Path.GetTempPath(), "aib-" + Guid.NewGuid().ToString("N")[..10]));
    private readonly List<(string Path, FileAccess Access)> _opens = [];
    private readonly List<Socket> _sockets = [];
    private string? _trap;
    private bool _trapWrites;

    public AssetInspectionBoundaryTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_root + "-target", "outside source");
    }

    public void Dispose()
    {
        foreach (var socket in _sockets) socket.Dispose();
        Directory.Delete(_root, recursive: true);
        File.Delete(_root + "-target");
    }

    public static IEnumerable<object[]> PipelineCases() =>
        from kind in new[] { "fifo", "socket", "link", "dangling" }
        from role in new[] { "manifest", "source", "output", "inventory" }
        select new object[] { kind, role };

    [UnixInspectionTheory]
    [MemberData(nameof(PipelineCases))]
    public async Task Pipeline_RefusesSpecialEntriesBeforeLeafOpenAndPublication(string kind, string role)
    {
        var manifest = PipelineManifest();
        var unsafePath = Full(role switch
        {
            "manifest" => "manifest.json",
            "source" => "source.json",
            "output" => "generated/icon.svg",
            _ => "generated/orphan"
        });
        Special(kind, unsafePath);
        _trap = unsafePath;
        _trapWrites = true;
        var before = Snapshot();
        foreach (var check in new[] { true, false })
        {
            var engine = new DocumentEngine(Open);
            var pipeline = new AssetPipeline(engine, new VectorAssetProducer(engine), Open);
            var error = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, check));
            Assert.Equal(EngineErrors.PathEscape, error.Code);
            Assert.DoesNotContain(_opens, open => open.Path == unsafePath || open.Access != FileAccess.Read);
            Assert.Equal(before, Snapshot());
        }
    }

    public static IEnumerable<object[]> ArtifactCases() =>
        from kind in new[] { "fifo", "socket", "link", "dangling" }
        from role in new[] { "descriptor", "payload", "inventory" }
        select new object[] { kind, role };

    [UnixInspectionTheory]
    [MemberData(nameof(ArtifactCases))]
    public async Task Artifact_RefusesSpecialEntriesBeforeLeafOpen(string kind, string role)
    {
        var descriptor = ArtifactBundle();
        var unsafePath = Full(role switch
        {
            "descriptor" => "bundle.json",
            "payload" => "image.png",
            _ => "orphan"
        });
        Special(kind, unsafePath);
        _trap = unsafePath;
        var before = Snapshot();
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => ArtifactPreflight.RunAsync(descriptor, default, open: Open));
        Assert.Equal(role == "inventory" ? "ARTIFACT_INVENTORY_MISMATCH" : EngineErrors.PathEscape, error.Code);
        Assert.DoesNotContain(_opens, open => open.Path == unsafePath);
        Assert.Equal(before, Snapshot());
    }

    [UnixInspectionFact]
    public async Task ActualDevice_IsRefusedBeforeDescriptorOpen()
    {
        _trap = "/dev/null";
        Assert.Equal(0, UnixFileMetadata.LStat(_trap, out var status));
        Assert.Equal(0x2000, status.Mode & UnixFileMetadata.TypeMask);
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => ArtifactPreflight.RunAsync(_trap, default, open: Open));
        Assert.Equal(EngineErrors.PathEscape, error.Code);
        Assert.Empty(_opens);
    }

    [UnixInspectionTheory]
    [InlineData("manifest")]
    [InlineData("source")]
    [InlineData("output")]
    [InlineData("publication")]
    public async Task PipelineOracle_TripsAtEachRealBoundaryForOrdinaryFiles(string role)
    {
        var manifest = PipelineManifest();
        var engine = new DocumentEngine(Open);
        var pipeline = new AssetPipeline(engine, new VectorAssetProducer(engine), Open);
        Assert.True((await pipeline.RunAsync(manifest, false)).Success);
        _opens.Clear();
        _trap = Full(role switch { "manifest" => "manifest.json", "source" => "source.json", _ => "generated/icon.svg" });
        if (role == "publication")
        {
            _trap = null;
            _trapWrites = true;
            File.WriteAllText(Full("generated/icon.svg"), "stale");
        }
        await Assert.ThrowsAsync<UnexpectedLeafOpenException>(() => pipeline.RunAsync(manifest, false));
        Assert.Contains(_opens, open => role == "publication" ? open.Access == FileAccess.Write : open.Path == _trap);
        Assert.DoesNotContain(Directory.EnumerateFiles(Full("generated")), path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [UnixInspectionTheory]
    [InlineData("bundle.json")]
    [InlineData("image.png")]
    public async Task ArtifactOracle_TripsAtRealDescriptorAndPayloadBoundaries(string file)
    {
        var descriptor = ArtifactBundle();
        _trap = Full(file);
        await Assert.ThrowsAsync<UnexpectedLeafOpenException>(() => ArtifactPreflight.RunAsync(descriptor, default, open: Open));
        Assert.Contains(_opens, open => open.Path == _trap);
    }

    private Stream Open(string path, FileStreamOptions options)
    {
        _opens.Add((path, options.Access));
        if (path == _trap || _trapWrites && options.Access != FileAccess.Read) throw new UnexpectedLeafOpenException();
        return LeafFileIO.Open(path, options);
    }

    private string PipelineManifest()
    {
        File.WriteAllText(Full("source.json"), DocumentSerializer.Serialize(new DocumentEngine().CreateDocument("icon", 16, 16)));
        File.WriteAllText(Full("manifest.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "generated",
            assets = new[] { new { source = "source.json", output = "generated/icon.svg" } }
        }));
        return Full("manifest.json");
    }

    private string ArtifactBundle()
    {
        var fixtures = Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures");
        File.WriteAllBytes(Full("image.png"), Convert.FromBase64String(File.ReadAllText(Path.Combine(fixtures, "preflight-rgba.png.base64"))));
        File.Copy(Path.Combine(fixtures, "preflight-bundle.json"), Full("bundle.json"));
        return Full("bundle.json");
    }

    private void Special(string kind, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        switch (kind)
        {
            case "fifo": Assert.Equal(0, MakeFifo(path, 0x180)); break;
            case "socket":
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _sockets.Add(socket);
                // Short owned root keeps sockaddr_un within its bound in both native temp modes.
                socket.Bind(new UnixDomainSocketEndPoint(path));
                break;
            case "link": File.CreateSymbolicLink(path, _root + "-target"); break;
            case "dangling": File.CreateSymbolicLink(path, Full("missing")); break;
        }
    }

    private string[] Snapshot()
    {
        var rows = new List<string>();
        rows.Add(File.ReadAllText(_root + "-target") + "|" + File.GetLastWriteTimeUtc(_root + "-target").Ticks);
        var pending = new Stack<string>();
        pending.Push(_root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                Assert.Equal(0, UnixFileMetadata.LStat(path, out var status));
                var type = status.Mode & UnixFileMetadata.TypeMask;
                var payload = type == UnixFileMetadata.RegularType ? Convert.ToBase64String(File.ReadAllBytes(path)) : "";
                rows.Add($"{path}|{status.Mode}|{status.MTime}|{status.MTimeNsec}|{payload}");
                if (type == UnixFileMetadata.DirectoryType) pending.Push(path);
            }
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    }

    private string Full(string name) => Path.Combine(_root, name);
    private sealed class UnexpectedLeafOpenException : Exception;
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
