using System.ComponentModel;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Mcp.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// Proves the raw-capturing stdio transport preserves the ORIGINAL per-message UTF-8 bytes
/// (with a zero-loss round-trip to a tool), silently drops malformed lines while surviving,
/// and that the native pixel admission gate reads the capture back correctly.
/// </summary>
public class RawCaptureIngressTests
{
    private const string TrickyRequest =
        "{\"expectedSnapshot\":{\"assetId\":\"2a4b8fde-468e-4bbb-a11f-a2b20169060a\"," +
        "\"revn\":9007199254740993," + // above 2^53: must survive exactly
        "\"sourceDigest\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}," +
        "\"operations\":[{\"op\":\"setOpacity\",\"layer\":\"@x\",\"opacity\":0.5000000}]}"; // excess precision, kept verbatim

    private static byte[] ToolCallLine(string toolName, string argumentsJson) => Encoding.UTF8.GetBytes(
        "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"" +
        toolName + "\",\"arguments\":" + argumentsJson + "}}");

    [Fact]
    public async Task Transport_CapturesOriginalBytesVerbatim_OnMessageReader()
    {
        // A message whose request carries leading whitespace, excess opacity precision and a
        // >2^53 integer — all of which typed/number-normalizing binding would erase.
        var line = ToolCallLine("edit_pixel_asset",
            "{\"assetDirectory\":\"art\",\"request\":   " + TrickyRequest + "}");

        var input = new Pipe();
        var output = new Pipe();
        await using var transport = new RawCapturingStdioServerTransport(
            input.Reader.AsStream(), output.Writer.AsStream(), NullLoggerFactory.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await WriteFrameAsync(input.Writer, line, appendCrLf: false);

        var message = await transport.MessageReader.ReadAsync(cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var raw = ReadCapture(message);

        // The captured frame is byte-for-byte the input line minus its newline terminator.
        Assert.Equal(line, raw);

        // The request VALUE slice survives verbatim, including internal excess precision and the big integer.
        using var document = JsonDocument.Parse(raw);
        var request = document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("request");
        var requestRaw = JsonMarshal.GetRawUtf8Value(request).ToArray();
        Assert.Equal(Encoding.UTF8.GetBytes(TrickyRequest), requestRaw);
        var rawText = Encoding.UTF8.GetString(raw);
        Assert.Contains("0.5000000", rawText);
        Assert.Contains("9007199254740993", rawText);
    }

    [Fact]
    public async Task Transport_PreservesEscapedDuplicateKeys_ForTheAdmissionGate()
    {
        // Outer escaped-duplicate argument key ("assetDirectory" twice) must reach the gate intact.
        var line = ToolCallLine("create_pixel_asset",
            "{\"assetDirectory\":\"a\",\"assetDirecto\\u0072y\":\"b\"}");

        var input = new Pipe();
        var output = new Pipe();
        await using var transport = new RawCapturingStdioServerTransport(
            input.Reader.AsStream(), output.Writer.AsStream(), NullLoggerFactory.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await WriteFrameAsync(input.Writer, line, appendCrLf: true); // also exercise CRLF trimming

        var message = await transport.MessageReader.ReadAsync(cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var raw = ReadCapture(message);
        Assert.Equal(line, raw); // trailing CR trimmed, no data loss

        // The gate, fed the captured bytes, rejects the escaped duplicate.
        var result = NativePixelToolGate.Admit("create_pixel_asset", message.Context!.Items);
        Assert.NotNull(result);
        Assert.True(result!.IsError);
        Assert.Equal("INVALID_ARGUMENTS", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transport_DropsMalformedLine_AndSurvives()
    {
        var input = new Pipe();
        var output = new Pipe();
        await using var transport = new RawCapturingStdioServerTransport(
            input.Reader.AsStream(), output.Writer.AsStream(), NullLoggerFactory.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // A malformed line, then a well-formed one.
        await WriteFrameAsync(input.Writer, Encoding.UTF8.GetBytes("this is not json {"), appendCrLf: false);
        var valid = ToolCallLine("inspect_pixel_asset", "{\"assetDirectory\":\"art\"}");
        await WriteFrameAsync(input.Writer, valid, appendCrLf: false);

        // The first (and only) message read is the valid one: the malformed line was dropped.
        var message = await transport.MessageReader.ReadAsync(cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(valid, ReadCapture(message));
    }

    [Fact]
    public async Task McpServer_OverRawTransport_DeliversCaptureToTool_EndToEnd()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var transport = new RawCapturingStdioServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), NullLoggerFactory.Instance);

        var services = new ServiceCollection();
        services.AddMcpServer(options => options.ServerInfo = new() { Name = "test", Version = "1.0.0" })
            .WithTools<EchoCaptureTool>();
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var server = McpServer.Create(transport, options, NullLoggerFactory.Instance, provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runTask = server.RunAsync(cts.Token);

        var toServer = clientToServer.Writer.AsStream();
        using var fromServer = new StreamReader(serverToClient.Reader.AsStream(), Encoding.UTF8);

        try
        {
            await SendAsync(toServer,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"," +
                "\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}");
            using (var initialize = await ReadResponseAsync(fromServer, id: 1))
            {
                Assert.True(initialize.RootElement.TryGetProperty("result", out _));
            }

            await SendAsync(toServer, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

            var toolCall = ToolCallLine(EchoCaptureTool.ToolName,
                "{\"assetDirectory\":\"art\",\"request\":   " + TrickyRequest + "}");
            await SendAsync(toServer, Encoding.UTF8.GetString(toolCall));

            using var response = await ReadResponseAsync(fromServer, id: 2);
            Assert.False(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            var data = ToolData(response.RootElement.GetProperty("result"));
            Assert.True(data.GetProperty("captured").GetBoolean());
            Assert.Equal(toolCall.Length, data.GetProperty("length").GetInt32());

            // The tool observed the exact original frame, proving zero-loss capture end to end.
            var echoed = Convert.FromBase64String(data.GetProperty("rawBase64").GetString()!);
            Assert.Equal(toolCall, echoed);
            using var echoedDocument = JsonDocument.Parse(echoed);
            var request = echoedDocument.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("request");
            Assert.Equal(Encoding.UTF8.GetBytes(TrickyRequest), JsonMarshal.GetRawUtf8Value(request).ToArray());
        }
        finally
        {
            await clientToServer.Writer.CompleteAsync();
            cts.Cancel();
            try { await runTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* shutdown */ }
        }
    }

    [Fact]
    public void Gate_NonNativeTool_ProceedsWithoutCapture()
    {
        Assert.Null(NativePixelToolGate.Admit("create_document", contextItems: null));
        Assert.False(NativePixelToolGate.IsNativePixelTool("create_document"));
        Assert.True(NativePixelToolGate.IsNativePixelTool("edit_pixel_asset"));
    }

    [Fact]
    public void Gate_NativeToolWithoutCapture_FailsClosed()
    {
        var result = NativePixelToolGate.Admit("edit_pixel_asset", contextItems: null);
        Assert.NotNull(result);
        Assert.True(result!.IsError);
        Assert.Equal(EngineErrors.InvalidArguments, result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Gate_NativeToolWithValidCapture_Proceeds()
    {
        var raw = ToolCallLine("edit_pixel_asset",
            "{\"assetDirectory\":\"art\",\"request\":" + TrickyRequest.Replace("0.5000000", "0.5") + "}");
        var items = new Dictionary<string, object?> { [RawCapturingStdioServerTransport.RawUtf8ItemKey] = raw };
        Assert.Null(NativePixelToolGate.Admit("edit_pixel_asset", items));
    }

    private static byte[] ReadCapture(JsonRpcMessage message)
    {
        Assert.NotNull(message.Context);
        Assert.NotNull(message.Context!.Items);
        Assert.True(message.Context.Items!.TryGetValue(RawCapturingStdioServerTransport.RawUtf8ItemKey, out var value));
        return Assert.IsType<byte[]>(value);
    }

    private static async Task WriteFrameAsync(PipeWriter writer, byte[] payload, bool appendCrLf)
    {
        await writer.WriteAsync(payload);
        await writer.WriteAsync(appendCrLf ? "\r\n"u8.ToArray() : "\n"u8.ToArray());
        await writer.FlushAsync();
    }

    private static async Task SendAsync(Stream toServer, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await toServer.WriteAsync(bytes);
        await toServer.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(StreamReader reader, int id)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(line is null, "server closed the stream before responding");
            var document = JsonDocument.Parse(line!);
            if (document.RootElement.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.Number && idElement.GetInt32() == id)
            {
                return document;
            }

            document.Dispose(); // a notification or unrelated message; keep reading
        }
    }

    private static JsonElement ToolData(JsonElement result)
    {
        var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString()!;
        using var envelope = JsonDocument.Parse(text);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        return envelope.RootElement.GetProperty("data").Clone();
    }
}

/// <summary>Test-only MCP tool that echoes the captured original request bytes as base64.</summary>
[McpServerToolType]
public sealed class EchoCaptureTool
{
    public const string ToolName = "echo_capture";

    [McpServerTool(Name = ToolName, Title = "Echo Capture")]
    [Description("Test-only: returns the captured original message bytes as base64.")]
    public static CallToolResult Echo(RequestContext<CallToolRequestParams> context)
    {
        var items = context.JsonRpcRequest.Context?.Items;
        byte[]? raw = null;
        if (items is not null && items.TryGetValue(RawCapturingStdioServerTransport.RawUtf8ItemKey, out var value))
        {
            raw = value as byte[];
        }

        return ToolResult.Success(new
        {
            captured = raw is not null,
            length = raw?.Length ?? -1,
            rawBase64 = raw is null ? null : Convert.ToBase64String(raw),
        });
    }
}
