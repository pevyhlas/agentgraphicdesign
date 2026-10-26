using FTPlus.AgentGraphicDesign.Core.Pixels.Storage;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Platform;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

public sealed partial class NativePixelService
{
    public Task<ImportPixelAssetResult> ImportAsync(ImportPixelAssetRequest request,
        CancellationToken cancellationToken = default, string? pathRoot = null) => RunImport(scope =>
    {
        if (request is null) throw new NativePixelException(EngineErrors.InvalidArguments, "Import request is required.");
        var confined = request with
        {
            AssetDirectory = ImportPath(request.AssetDirectory, pathRoot, "$.assetDirectory"),
            DescriptorPath = ImportPath(request.DescriptorPath, pathRoot, "$.descriptorPath")
        };
        return NativePixelStore.Import(confined, scope);
    }, cancellationToken);

    public Task<ImportPixelLayerResult> ImportLayerAsync(ImportPixelLayerRequest request,
        CancellationToken cancellationToken = default, string? pathRoot = null) => RunImport(scope =>
    {
        if (request is null) throw new NativePixelException(EngineErrors.InvalidArguments, "Import request is required.");
        var confined = request with
        {
            AssetDirectory = ImportPath(request.AssetDirectory, pathRoot, "$.assetDirectory"),
            DescriptorPath = ImportPath(request.DescriptorPath, pathRoot, "$.descriptorPath")
        };
        return NativePixelStore.ImportLayer(confined, scope);
    }, cancellationToken);

    public Task<ImportPixelLayerResult> ImportLayerFileAsync(string assetDirectory, string requestPath,
        string inputBaseDirectory, CancellationToken cancellationToken = default, string? pathRoot = null) => RunImport(scope =>
    {
        string asset = NativePixelWire.NormalizePath(assetDirectory, inputBaseDirectory, pathRoot, "$.assetDirectory");
        string file = NativePixelWire.NormalizePath(requestPath, inputBaseDirectory, pathRoot, "$.requestPath");
        byte[] bytes;
        try
        {
            scope.Check();
            AssetPaths.Inspect(file, false, required: true, PlatformCapability.StillImport, scope.Caller);
            bytes = NativePixelFileSystem.ReadMetadata(file, NativePixelFormat.MaxJsonBytes, scope);
        }
        catch (DocumentEngineException error) when (error.Code == EngineErrors.DocumentNotFound)
        {
            throw new NativePixelException(NativePixelErrors.PixelInputUnavailable, "The import request file is absent or unreadable.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new NativePixelException(NativePixelErrors.PixelInputUnavailable, "The import request file is absent or unreadable.");
        }
        return ImportOriginalLayer(bytes, asset, inputBaseDirectory, pathRoot, scope);
    }, cancellationToken);

    public Task<ImportPixelLayerResult> ImportLayerWireAsync(string assetDirectory, ReadOnlyMemory<byte> requestBytes,
        string inputBaseDirectory, CancellationToken cancellationToken = default, string? pathRoot = null) => RunImport(scope =>
    {
        string asset = NativePixelWire.NormalizePath(assetDirectory, inputBaseDirectory, pathRoot, "$.assetDirectory");
        if (requestBytes.Length > NativePixelFormat.MaxJsonBytes)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Import request exceeds its byte limit.");
        scope.Reserve(requestBytes.Length + 65_536L);
        var owned = new byte[requestBytes.Length];
        for (int offset = 0; offset < owned.Length; offset += 65_536)
        {
            scope.Check();
            requestBytes.Span.Slice(offset, Math.Min(65_536, owned.Length - offset)).CopyTo(owned.AsSpan(offset));
        }
        return ImportOriginalLayer(owned, asset, inputBaseDirectory, pathRoot, scope);
    }, cancellationToken);

    private static ImportPixelLayerResult ImportOriginalLayer(byte[] bytes, string asset, string inputBaseDirectory,
        string? root, NativeOperationScope scope)
    {
        scope.Check();
        scope.Reserve(32L * bytes.Length + 262_144);
        var request = NativePixelWire.ParseImportLayer(bytes, asset, inputBaseDirectory, root, scope.Budget);
        return NativePixelStore.ImportLayer(request, scope); // same admitted lifetime; never enter a second scope.
    }

    private static string ImportPath(string path, string? root, string location)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new NativePixelException(EngineErrors.InvalidArguments, "Import requires absolute local paths.", location);
        return NativePixelWire.NormalizePath(path, root ?? Path.GetPathRoot(path)!, root, location);
    }

    private static Task<T> RunImport<T>(Func<NativeOperationScope, T> operation, CancellationToken cancellationToken)
    {
        try
        {
            var platform = PlatformProfile.Current;
            if (!platform.GateAdmits(PlatformCapability.StillImport))
                throw new NativePixelException(NativePixelErrors.AssetPlatformUnsupported,
                    platform.RefusalMessage(PlatformCapability.StillImport));
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(operation(scope));
        }
        catch (DocumentEngineException error)
        {
            return Task.FromException<T>(new NativePixelException(error.Code, error.Message));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Task.FromException<T>(new NativePixelException(EngineErrors.StoreUnavailable, "Native import storage could not be accessed or published."));
        }
        catch (Exception error)
        {
            return Task.FromException<T>(error);
        }
    }
}
