using System.Reflection;
using System.Text;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Mcp.Resources;
using FTPlus.AgentGraphicDesign.Mcp.Tools;
using FTPlus.AgentGraphicDesign.Mcp.Transport;
using FTPlus.AgentGraphicDesign.Skia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp;

public static class Program
{
    public const int UsageExitCode = 2;

    public static async Task<int> Main(string[] args)
    {
        var parsed = ParseArguments(args);
        if (!parsed.Success)
        {
            Console.Error.WriteLine($"agd-mcp: {parsed.Error}");
            Console.Error.WriteLine("Try 'agd-mcp --help' for usage.");
            return UsageExitCode;
        }

        if (parsed.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (parsed.ShowVersion)
        {
            Console.WriteLine($"agd-mcp {GetVersion()}");
            return 0;
        }

        try
        {
            var workingDirectory = Path.GetFullPath(parsed.WorkingDirectory ?? Environment.CurrentDirectory);
            var builder = Host.CreateEmptyApplicationBuilder(settings: null);

            builder.Services.AddSingleton(_ => new PathConfinement(workingDirectory));
            // Native pixel operation surface: construction is I/O-free (no discovery, store or hidden state).
            builder.Services.AddSingleton<NativePixelService>();
            builder.Services.AddSingleton<IDocumentStore>(_ => new FileDocumentStore(workingDirectory));
            builder.Services.AddSingleton<IRenderer, SkiaRenderer>();
            builder.Services.AddSingleton<ITextMeasurer, SkiaTextMeasurer>();
            builder.Services.AddSingleton<IContactSheetRenderer>(sp => new SkiaContactSheetRenderer(sp.GetRequiredService<IRenderer>()));
            builder.Services.AddSingleton<IDocumentEngine>(sp => new DocumentEngine(
                sp.GetRequiredService<IDocumentStore>(),
                exportAdapters: null,
                sp.GetRequiredService<IRenderer>(),
                sp.GetRequiredService<ITextMeasurer>(),
                sp.GetRequiredService<IContactSheetRenderer>(),
                measurerFactory: policy => new SkiaTextMeasurer(policy)));

            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "agd-mcp",
                        Version = GetVersion()
                    };
                })
                .WithRequestFilters(filters =>
                {
                    filters.AddCallToolFilter(next => async (request, cancellationToken) =>
                    {
                        // Native pixel ingress seam: validate the ORIGINAL argument bytes preserved by
                        // RawCapturingStdioServerTransport before typed binding can erase the evidence.
                        // No-op for non-native tools (and until the native tools are registered).
                        var admission = NativePixelToolGate.Admit(
                            request.Params?.Name,
                            request.JsonRpcRequest.Context?.Items);
                        if (admission is not null)
                        {
                            return admission;
                        }

                        try
                        {
                            return await next(request, cancellationToken);
                        }
                        catch (JsonException)
                        {
                            return ToolResult.Fail(EngineErrors.InvalidArguments, "Arguments do not match the discovered tool schema");
                        }
                        catch (Exception ex) when (ToolResult.IsExpectedException(ex))
                        {
                            return ToolResult.FromException(ex, "TOOL_FAILED");
                        }
                    });
                })
                .WithToolsFromAssembly()
                .WithResourcesFromAssembly();

            // Replace the SDK's sealed stdio transport (which parses params first, losing the
            // original bytes) with a raw-capturing transport, and run the server ourselves since
            // the SDK's single-session hosted service is only wired by WithStdioServerTransport().
            builder.Services.AddSingleton<ITransport>(sp => new RawCapturingStdioServerTransport(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                sp.GetService<ILoggerFactory>()));
            builder.Services.AddSingleton<McpServer>(sp => McpServer.Create(
                sp.GetRequiredService<ITransport>(),
                sp.GetRequiredService<IOptions<McpServerOptions>>().Value,
                sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                sp));
            builder.Services.AddHostedService<AgdMcpServerRunner>();

            var app = builder.Build();
            await app.RunAsync();
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine("agd-mcp: unable to start with the requested working directory");
            return UsageExitCode;
        }
    }

    public static StartupArguments ParseArguments(IReadOnlyList<string> args)
    {
        string? workingDirectory = null;
        var showHelp = false;
        var showVersion = false;
        var seenWorkingDirectory = false;
        var seenHelp = false;
        var seenVersion = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    if (seenHelp)
                    {
                        return StartupArguments.Fail("Duplicate help option");
                    }
                    seenHelp = true;
                    showHelp = true;
                    break;

                case "-v" or "--version":
                    if (seenVersion)
                    {
                        return StartupArguments.Fail("Duplicate version option");
                    }
                    seenVersion = true;
                    showVersion = true;
                    break;

                case "-w" or "--working-directory":
                    if (seenWorkingDirectory)
                    {
                        return StartupArguments.Fail("Duplicate working-directory option");
                    }
                    if (i + 1 >= args.Count || LooksLikeOption(args[i + 1]))
                    {
                        return StartupArguments.Fail("Working-directory option requires a path value");
                    }
                    seenWorkingDirectory = true;
                    workingDirectory = args[++i];
                    break;

                default:
                    return StartupArguments.Fail($"Unknown option '{arg}'");
            }
        }

        if ((showHelp || showVersion) && args.Count != 1)
        {
            return StartupArguments.Fail("Help and version options must be used alone");
        }

        return new StartupArguments(true, showHelp, showVersion, workingDirectory, null);
    }

    private static bool LooksLikeOption(string value) => value.StartsWith('-');

    private static void PrintHelp()
    {
        Console.WriteLine($"agd-mcp {GetVersion()} - Agent Graphic Design MCP Server");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    agd-mcp [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    -w, --working-directory <PATH>  Working directory for documents (default: current directory)");
        Console.WriteLine("    -h, --help                      Show this help message");
        Console.WriteLine("    -v, --version                   Show version information");
        Console.WriteLine();
        Console.WriteLine("AVAILABLE TOOLS:");
        foreach (var toolName in GetToolNames())
        {
            Console.WriteLine($"    {toolName}");
        }
        Console.WriteLine();
        Console.WriteLine("AVAILABLE RESOURCES (read-only):");
        foreach (var (uri, mimeType) in GetResourceUris())
        {
            Console.WriteLine($"    {uri}  ({mimeType})");
        }
    }

    private static IEnumerable<(string Uri, string MimeType)> GetResourceUris()
    {
        return typeof(DocumentResources).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Select(method => method.GetCustomAttribute<McpServerResourceAttribute>())
            .Where(attribute => attribute?.UriTemplate is not null)
            .Select(attribute => (attribute!.UriTemplate!, attribute.MimeType ?? "application/octet-stream"))
            .OrderBy(item => item.Item1, StringComparer.Ordinal);
    }

    internal static IEnumerable<string> GetToolNames()
    {
        return typeof(Program).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(item => item.Attribute is not null)
            .Select(item => item.Attribute!.Name ?? ToSnakeCase(item.Method.Name))
            .OrderBy(name => name, StringComparer.Ordinal);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0 &&
                (char.IsLower(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(current));
        }
        return builder.ToString();
    }

    internal static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}

public sealed record StartupArguments(
    bool Success,
    bool ShowHelp,
    bool ShowVersion,
    string? WorkingDirectory,
    string? Error)
{
    public static StartupArguments Fail(string error) => new(false, false, false, null, error);
}
