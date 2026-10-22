using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

/// <summary>Adapts the existing engine without cloning inputs or admitting paths/graphs.</summary>
public sealed class VectorAssetProducer : IAssetProducer<VectorAssetRequest>
{
    private readonly IDocumentEngine _engine;

    public VectorAssetProducer(IDocumentEngine engine) =>
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public async Task<AssetProducerResult> ProduceAsync(
        VectorAssetRequest request, IAssetProductionBudget budget,
        CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(request.Document);
        if (string.IsNullOrWhiteSpace(request.SourceName))
            throw AssetManifest.Invalid("A diagnostic source name is required");
        if (request.Format is not (VectorAssetFormat.Svg or VectorAssetFormat.Png or VectorAssetFormat.Jpeg or VectorAssetFormat.Webp))
            throw AssetManifest.Invalid("Unknown vector asset format");
        if (request.Profile is not (ValidationProfile.Default or ValidationProfile.WebIcon) ||
            request.CurrentColor is not null && !DocumentLimits.IsValidColor(request.CurrentColor))
            throw AssetManifest.Invalid("Invalid profile or currentColor option");
        if (!double.IsFinite(request.Scale) || request.Scale is < 0.1 or > 4 ||
            request.Width is < 1 or > 8192 || request.Height is < 1 or > 8192)
            throw AssetManifest.Invalid("Scale must be 0.1-4 and dimensions must be 1-8192");
        if (request.FontDirectories.IsDefault)
            throw AssetManifest.Invalid("Font directories must be an initialized immutable sequence");
        if (request.FontDirectories.Length > 16)
            throw AssetManifest.Limit();
        if (request.FontDirectories.Any(string.IsNullOrWhiteSpace))
            throw AssetManifest.Invalid("Font directories must be nonempty strings");
        // These are effective options, not manifest presence flags; SVG ignores font booleans.
        if (request.Format == VectorAssetFormat.Svg && (request.Scale != 1 ||
            request.Width is not null || request.Height is not null || !request.FontDirectories.IsEmpty))
            throw AssetManifest.Invalid("Sizing and font-directory overrides only apply to raster outputs");
        if (request.Format != VectorAssetFormat.Svg && (request.CurrentColor is not null || request.AsSymbol))
            throw AssetManifest.Invalid("currentColor and symbols only apply to SVG outputs");

        var document = request.Document;
        budget.ChargeNodes(_engine.CountNodes(document));
        var validation = _engine.Validate(document, new ValidationOptions { Profile = request.Profile });
        if (validation.Errors.FirstOrDefault() is { } error)
            throw new DocumentEngineException(error.Code, $"{request.SourceName}: {error.Message} ({error.Path})");
        if (request.Strict && validation.Warnings.FirstOrDefault() is { } warning)
            throw new DocumentEngineException(warning.Code, $"{request.SourceName}: {warning.Message} ({warning.Path})");
        ct.ThrowIfCancellationRequested();
        try
        {
            if (request.Format == VectorAssetFormat.Svg)
            {
                if (request.AsSymbol && !SvgExportAdapter.IsValidSymbolId(document.Name))
                    throw new DocumentEngineException(EngineErrors.InvalidProperty, $"Invalid sprite document name: {document.Name}");
                var exported = await _engine.ExportAsync(document, new ExportOptions
                {
                    Format = "svg",
                    CurrentColor = request.CurrentColor,
                    SymbolId = request.AsSymbol ? document.Name : null
                }, ct);
                return new AssetProducerResult(exported.Data, []);
            }

            var width = (request.Width ?? document.Canvas.Width) * request.Scale;
            var height = (request.Height ?? document.Canvas.Height) * request.Scale;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 0.5 or > 8192 || height is < 0.5 or > 8192)
                throw new DocumentEngineException(EngineErrors.InvalidDimensions, "Effective raster dimensions must be 1-8192 pixels");
            budget.ChargeRasterPixels((long)Math.Round(width, MidpointRounding.AwayFromZero) *
                (long)Math.Round(height, MidpointRounding.AwayFromZero));
            var rendered = _engine.Render(document, new RenderOptions
            {
                Format = request.Format switch
                {
                    VectorAssetFormat.Png => RenderFormat.Png,
                    VectorAssetFormat.Jpeg => RenderFormat.Jpeg,
                    VectorAssetFormat.Webp => RenderFormat.Webp,
                    _ => throw AssetManifest.Invalid("Unknown raster asset format")
                },
                Scale = request.Scale,
                Width = request.Width,
                Height = request.Height,
                FontDirectories = request.FontDirectories,
                SystemFonts = request.SystemFonts,
                StrictFonts = request.StrictFonts
            }, ct);
            return new AssetProducerResult(rendered.Data, rendered.Diagnostics);
        }
        catch (Exception ex) when (ex is not (DocumentEngineException or OperationCanceledException or OutOfMemoryException))
        {
            throw new DocumentEngineException("ASSET_EXPORT_FAILED", $"{request.SourceName}: {ex.Message}");
        }
    }
}
