using FTPlus.AgentGraphicDesign.Core.Rendering;

namespace FTPlus.AgentGraphicDesign.Core.Assets;

/// <summary>
/// Trusted in-process production. Borrowed inputs and budget must not be retained after
/// completion; returned storage must remain stable until the host has consumed it.
/// Overlapping calls require thread-safe dependencies and nonconflicting result storage.
/// </summary>
public interface IAssetProducer<in TRequest>
{
    Task<AssetProducerResult> ProduceAsync(
        TRequest request, IAssetProductionBudget budget,
        CancellationToken cancellationToken = default);
}

/// <summary>Cooperative per-run work limits, charged before validation or raster work.</summary>
public interface IAssetProductionBudget
{
    void ChargeNodes(int count);
    void ChargeRasterPixels(long count);
}

/// <summary>Transfer views, not ownership guarantees. The host bounds and copies accepted results.</summary>
public sealed record AssetProducerResult(
    ReadOnlyMemory<byte> Data,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
