using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Infrastructure;

/// <summary>
/// Spectre.Console.Cli type resolver backed by an IServiceProvider.
/// Uses ActivatorUtilities to create types that aren't explicitly registered,
/// allowing constructor injection for commands.
/// </summary>
public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        // Try to resolve from registered services first
        var service = _provider.GetService(type);
        if (service is not null)
        {
            return service;
        }

        // Fall back to ActivatorUtilities for types not registered (e.g., commands)
        return ActivatorUtilities.CreateInstance(_provider, type);
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
