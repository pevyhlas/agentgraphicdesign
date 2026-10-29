using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Tests.Rendering;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class SpecialFileSurfaceTests
{
    public static IEnumerable<object[]> Cases() =>
        from surface in new[] { "cli", "mcp" }
        from kind in new[] { "fifo", "socket" }
        from role in new[] { "descriptor", "payload" }
        select new object[] { surface, kind, role };

    [UnixInspectionTheory]
    [MemberData(nameof(Cases))]
    public Task RealSurface_RefusesSpecialFileAndRecoversReadOnly(string surface, string kind, string role) =>
        FileProbeWorker.RunAsync("surface", surface, kind, role);

    internal static async Task ProbeAsync(string surface, string kind, string role)
    {
        var root = FileProbeWorker.FixtureRoot!;
        var fixtures = Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures");
        var descriptor = Path.Combine(root, "bundle.json");
        var payload = Path.Combine(root, "image.png");
        var metadata = File.ReadAllBytes(Path.Combine(fixtures, "preflight-bundle.json"));
        var png = Convert.FromBase64String(File.ReadAllText(Path.Combine(fixtures, "preflight-rgba.png.base64")));
        File.WriteAllBytes(descriptor, metadata);
        File.WriteAllBytes(payload, png);
        var target = role == "descriptor" ? descriptor : payload;
        File.Delete(target);
        using var socket = kind == "socket" ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified) : null;
        if (socket is not null) socket.Bind(new UnixDomainSocketEndPoint(target));
        else Assert.Equal(0, MakeFifo(target, 0x180));
        var before = Snapshot(root);
        using var server = surface == "mcp" ? Start(surface, root, "--working-directory", root) : null;
        var stderr = server is null ? null : FileProbeWorker.ReadBounded(server.StandardError);
        var id = 0;
        Task<JsonElement> Request(string method, object parameters) => RequestCore(method, parameters).WaitAsync(TimeSpan.FromSeconds(5));
        async Task<JsonElement> RequestCore(string method, object parameters)
        {
            await server!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = ++id, method, @params = parameters }));
            await server.StandardInput.FlushAsync();
            var text = new StringBuilder();
            var character = new char[1];
            while (await server.StandardOutput.ReadAsync(character) != 0)
            {
                if (character[0] == '\n') break;
                if (text.Length == 1_048_576) throw new IOException("MCP response exceeds fixture bound");
                text.Append(character[0]);
            }
            using var json = JsonDocument.Parse(text.ToString());
            Assert.Equal(id, json.RootElement.GetProperty("id").GetInt32());
            return json.RootElement.GetProperty("result").Clone();
        }
        try
        {
            if (server is not null)
            {
                _ = await Request("initialize", new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "special-file-probe", version = "1" } });
                await server.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                await server.StandardInput.FlushAsync();
            }
            foreach (var valid in new[] { false, true })
            {
                if (valid)
                {
                    socket?.Dispose();
                    File.Delete(target);
                    File.WriteAllBytes(target, role == "descriptor" ? metadata : png);
                    before = Snapshot(root);
                }
                foreach (var verb in new[] { "preflight", "verify" })
                {
                    JsonElement envelope;
                    if (server is not null)
                    {
                        var response = await Request("tools/call", new { name = verb + "_artifact", arguments = new { descriptorPath = "bundle.json" } });
                        Assert.Equal(!valid, response.GetProperty("isError").GetBoolean());
                        envelope = response.GetProperty("structuredContent");
                        var block = Assert.Single(response.GetProperty("content").EnumerateArray());
                        Assert.Equal(envelope.GetRawText(), block.GetProperty("text").GetString());
                    }
                    else
                    {
                        using var cli = Start(surface, root, "assets", verb, descriptor, "-f", "json");
                        var output = FileProbeWorker.ReadBounded(cli.StandardOutput);
                        var error = FileProbeWorker.ReadBounded(cli.StandardError);
                        await cli.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        Assert.Equal(valid ? 0 : 1, cli.ExitCode);
                        Assert.Equal("", await error);
                        using var json = JsonDocument.Parse(await output);
                        envelope = json.RootElement.Clone();
                    }
                    Assert.Equal(valid, envelope.GetProperty("ok").GetBoolean());
                    if (!valid) Assert.Equal(EngineErrors.PathEscape, envelope.GetProperty("error").GetProperty("code").GetString());
                    else Assert.Equal(1, envelope.GetProperty("data").GetProperty("fileCount").GetInt32());
                    Assert.Equal(before, Snapshot(root));
                }
            }
        }
        finally
        {
            if (server is not null)
            {
                if (!server.HasExited) server.Kill(entireProcessTree: true);
                await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Console.Error.Write(await stderr!.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
    }

    private static Process Start(string surface, string root, params string[] args)
    {
        var packaged = Environment.GetEnvironmentVariable(surface == "mcp" ? "AGD_TEST_MCP_EXECUTABLE" : "AGD_TEST_CLI_EXECUTABLE");
        if (packaged is not null && (!Path.IsPathFullyQualified(packaged) || !File.Exists(packaged))) throw new IOException("Invalid installed tool path");
        var info = new ProcessStartInfo(packaged ?? "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (packaged is null)
            info.ArgumentList.Add(Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "src", "FTPlus.AgentGraphicDesign." + (surface == "mcp" ? "Mcp" : "Cli"), "bin", "Release", "net10.0", surface == "mcp" ? "agd-mcp.dll" : "agd.dll"));
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("Tool did not start");
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFileSystemEntries(root).Select(path =>
    {
        Assert.Equal(0, UnixFileMetadata.LStat(path, out var status));
        return $"{path}|{status.Mode}|{status.MTime}|{status.MTimeNsec}|" +
            ((status.Mode & UnixFileMetadata.TypeMask) == UnixFileMetadata.RegularType ? Convert.ToBase64String(File.ReadAllBytes(path)) : "");
    }).Order(StringComparer.Ordinal).ToArray();

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
