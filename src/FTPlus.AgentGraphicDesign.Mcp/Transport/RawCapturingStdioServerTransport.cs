using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FTPlus.AgentGraphicDesign.Core.Wire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp.Transport;

/// <summary>
/// A stdio server transport that preserves each incoming message's ORIGINAL UTF-8 bytes
/// before the SDK parses <c>params</c> into a <see cref="System.Text.Json.Nodes.JsonObject"/>
/// (after which whitespace and escaped-key byte spelling are gone). The stock
/// <c>StdioServerTransport</c> is sealed with a private read loop that parses first, so the
/// only public place to keep the original bytes is a custom <see cref="TransportBase"/> read
/// loop. The literal per-message frame (minus its newline terminator) is stashed on the
/// message under <see cref="RawUtf8ItemKey"/>; a native pixel tool (or the admission filter)
/// reads it back from <c>ctx.JsonRpcRequest.Context.Items[RawUtf8ItemKey]</c>.
/// </summary>
public sealed class RawCapturingStdioServerTransport : TransportBase
{
    /// <summary>Key under which the original per-message UTF-8 bytes are stored on the message context.</summary>
    public const string RawUtf8ItemKey = "agd.native.rawMsgUtf8";

    private const string TransportName = "agd-mcp-stdio";
    private const int ReadChunkBytes = 16 * 1024;

    private static readonly JsonTypeInfo MessageTypeInfo =
        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage));

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _readCts = new();
    private readonly Task _readLoop;
    private int _disposed;

    public RawCapturingStdioServerTransport(Stream input, Stream output, ILoggerFactory? loggerFactory = null)
        : base(TransportName, loggerFactory ?? NullLoggerFactory.Instance)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        SetConnected();
        _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    /// <summary>Writes an OUTGOING message as UTF-8 JSON followed by a newline, serialized under a send lock.</summary>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, MessageTypeInfo);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _readCts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch
        {
            // The read loop reports its own disconnect reason; disposal never rethrows.
        }

        _readCts.Dispose();
        _sendLock.Dispose();
    }

    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);
        var pending = new ArrayBufferWriter<byte>(ReadChunkBytes);
        var skippingOversize = false;
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await _input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (read == 0)
                {
                    break; // EOF / peer disconnect.
                }

                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n')
                    {
                        continue;
                    }

                    var segment = buffer.AsSpan(start, i - start);
                    if (skippingOversize)
                    {
                        // The oversized line ends here; drop it and resume normally.
                        pending.Clear();
                        skippingOversize = false;
                    }
                    else if (PayloadLength(pending.WrittenSpan, segment) > AgdWireParser.MaxMessageBytes)
                    {
                        pending.Clear();
                        await SendOversizeErrorAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var raw = Combine(pending, segment);
                        pending.Clear();
                        await HandleFrameAsync(raw, cancellationToken).ConfigureAwait(false);
                    }

                    start = i + 1;
                }

                if (start < read && !skippingOversize)
                {
                    var tail = buffer.AsSpan(start, read - start);
                    var bufferedLength = pending.WrittenCount + tail.Length;
                    // A CR at the end of a read may be the first half of CRLF.
                    // Permit exactly that one provisional terminator byte, never
                    // another payload byte above the coarse message ceiling.
                    if (bufferedLength > AgdWireParser.MaxMessageBytes &&
                        !(bufferedLength == AgdWireParser.MaxMessageBytes + 1 && tail[^1] == (byte)'\r'))
                    {
                        pending.Clear();
                        skippingOversize = true;
                        await SendOversizeErrorAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        pending.Write(tail);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            SetDisconnected(failure);
        }
    }

    private static byte[] Combine(ArrayBufferWriter<byte> pending, ReadOnlySpan<byte> segment)
    {
        // Trim a single trailing CR from the framed line only (CRLF terminators).
        var trimmed = segment;
        if (pending.WrittenCount == 0)
        {
            if (trimmed.Length > 0 && trimmed[^1] == (byte)'\r')
            {
                trimmed = trimmed[..^1];
            }

            return trimmed.ToArray();
        }

        pending.Write(segment);
        var whole = pending.WrittenSpan;
        if (whole.Length > 0 && whole[^1] == (byte)'\r')
        {
            whole = whole[..^1];
        }

        return whole.ToArray();
    }

    private static int PayloadLength(ReadOnlySpan<byte> pending, ReadOnlySpan<byte> segment)
    {
        var length = pending.Length + segment.Length;
        var terminalCr = segment.Length > 0 ? segment[^1] == (byte)'\r'
            : pending.Length > 0 && pending[^1] == (byte)'\r';
        return length - (terminalCr ? 1 : 0);
    }

    private async Task HandleFrameAsync(byte[] raw, CancellationToken cancellationToken)
    {
        if (raw.Length == 0)
        {
            return; // Blank line.
        }

        JsonRpcMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(raw, MessageTypeInfo) as JsonRpcMessage;
        }
        catch (JsonException)
        {
            return; // Malformed line: drop silently, matching stock stdio parity.
        }

        if (message is null)
        {
            return;
        }

        message.Context ??= new JsonRpcMessageContext();
        (message.Context.Items ??= new Dictionary<string, object?>())[RawUtf8ItemKey] = raw;
        await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOversizeErrorAsync(CancellationToken cancellationToken)
    {
        // Best-effort JSON-RPC error for an unframeable oversized line (id is unknown, so
        // null per JSON-RPC 2.0). Never let a send failure kill the read loop.
        try
        {
            // SDK RequestId's default serializes as an empty string, not JSON null.
            // This transport-level response has no recoverable request identity.
            var bytes = "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32600,\"message\":\"Message exceeds the maximum accepted size.\"}}\n"u8.ToArray();
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallow: oversize is a coarse safety net and must not drop the connection.
        }
    }
}
