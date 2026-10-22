using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp;

/// <summary>
/// Runs the single-session <see cref="McpServer"/> for the process lifetime and stops the
/// host when the session ends. The SDK's own <c>SingleSessionMcpServerHostedService</c> is
/// internal and is only registered by <c>WithStdioServerTransport()</c>, which this server
/// replaces with a custom raw-capturing transport, so it supplies an equivalent runner.
/// </summary>
public sealed class AgdMcpServerRunner : BackgroundService
{
    private readonly McpServer _server;
    private readonly IHostApplicationLifetime _lifetime;

    public AgdMcpServerRunner(McpServer server, IHostApplicationLifetime lifetime)
    {
        _server = server;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
