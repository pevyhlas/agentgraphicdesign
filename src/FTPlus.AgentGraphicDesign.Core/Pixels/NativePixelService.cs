using FTPlus.AgentGraphicDesign.Core.Pixels.Storage;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

/// <summary>
/// The native pixel operation surface (P6). Construction is I/O-free: there is no native
/// discovery, registration or hidden persistence. Each method is a heavy native call admitted
/// through the one host-lifetime shared guard; competing calls fail immediately with
/// <c>PIXEL_BUSY</c>. Create, Inspect, InspectRegion, Validate, Edit, Preview, Export and
/// CheckExport share the same lifetime as the managed-local Import and ImportLayer operations.
/// </summary>
public sealed partial class NativePixelService
{
    public NativePixelService()
    {
        // Intentionally empty: no discovery, no store initialization, no I/O.
    }

    public Task<CreatePixelAssetResult> CreateAsync(
        CreatePixelAssetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            var created = NativePixelStore.Create(request.AssetDirectory, request.Name, request.Width, request.Height, scope);
            return Task.FromResult(new CreatePixelAssetResult(NativePixelStore.Info(created.Source), created.LayerId));
        }
        catch (Exception error)
        {
            return Task.FromException<CreatePixelAssetResult>(error);
        }
    }

    public Task<PixelAssetInfo> InspectAsync(
        InspectPixelAssetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(NativePixelStore.Info(NativePixelStore.Read(request.AssetDirectory, scope)));
        }
        catch (Exception error)
        {
            return Task.FromException<PixelAssetInfo>(error);
        }
    }

    public Task<PixelRegionResult> InspectRegionAsync(
        InspectPixelRegionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(NativePixelStore.Region(request, scope));
        }
        catch (Exception error)
        {
            return Task.FromException<PixelRegionResult>(error);
        }
    }

    public Task<ValidatePixelAssetResult> ValidateAsync(
        ValidatePixelAssetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            try
            {
                var source = NativePixelStore.Read(request.AssetDirectory, scope);
                return Task.FromResult<ValidatePixelAssetResult>(new ValidPixelAssetResult(NativePixelStore.Info(source)));
            }
            catch (NativePixelException intrinsic) when (IsIntrinsicSourceFailure(intrinsic.Code))
            {
                return Task.FromResult<ValidatePixelAssetResult>(new InvalidPixelAssetResult(
                    [new PixelDiagnostic(intrinsic.Code, intrinsic.Message, intrinsic.Path)]));
            }
        }
        catch (Exception error)
        {
            return Task.FromException<ValidatePixelAssetResult>(error);
        }
    }

    public Task<EditPixelAssetResult> EditAsync(
        EditPixelAssetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(NativePixelStore.Edit(request, scope));
        }
        catch (Exception error)
        {
            return Task.FromException<EditPixelAssetResult>(error);
        }
    }

    public Task<PixelPreviewResult> PreviewAsync(
        PreviewPixelAssetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(NativePixelStore.Preview(request, scope));
        }
        catch (Exception error)
        {
            return Task.FromException<PixelPreviewResult>(error);
        }
    }

    public Task<PixelExportResult> ExportAsync(
        ExportPixelAssetRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(NativePixelStore.Export(request, scope));
        }
        catch (Exception error)
        {
            return Task.FromException<PixelExportResult>(error);
        }
    }

    public Task<PixelExportCheckResult> CheckExportAsync(
        CheckPixelExportRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            using var scope = NativeOperationScope.Enter(cancellationToken);
            return Task.FromResult(NativePixelStore.Check(request, scope));
        }
        catch (Exception error)
        {
            return Task.FromException<PixelExportCheckResult>(error);
        }
    }

    // Intrinsic invalid-source failures become validate's invalid report; missing asset,
    // unsafe path, unsupported version/profile, busy, resource exhaustion and cancellation
    // remain execution failures (exceptions).
    private static bool IsIntrinsicSourceFailure(string code) => code is
        EngineErrors.ParseError or EngineErrors.InvalidId or EngineErrors.InvalidProperty
        or EngineErrors.InvalidDimensions or NativePixelErrors.PixelInventoryMismatch
        or NativePixelErrors.PixelContentMismatch;
}
