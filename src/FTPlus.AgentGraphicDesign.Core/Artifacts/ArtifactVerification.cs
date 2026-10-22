using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Artifacts;

/// <summary>
/// Read-only decoded RGBA8 observations in a trusted, quiescent workspace, not admission or preservation.
/// Managed in-process execution has a cooperative deadline, not an OS sandbox or hard termination guarantee.
/// </summary>
public static class ArtifactVerification
{
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(60);
    private static int _activeVerification;

    public static async Task<ArtifactVerificationResult> CheckAsync(
        string descriptorPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _activeVerification, 1, 0) != 0)
            throw new DocumentEngineException("ARTIFACT_VERIFY_BUSY", "Another artifact verification is active.");

        try
        {
            using var deadline = new CancellationTokenSource(VerificationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var linkedToken = linked.Token;
            Action checkBudget = linkedToken.ThrowIfCancellationRequested;
            try
            {
                var files = new List<ArtifactPixelDigest>();
                var facts = await ArtifactPreflight.RunAsync(descriptorPath, linkedToken, (data, claim, canvas) =>
                {
                    // The runner has checked the hash and structure of this same privately captured buffer.
                    var rgbaSha256 = PngRgbaVerifier.Verify(data, canvas.Width, canvas.Height, checkBudget);
                    files.Add(new(claim.Sha256, rgbaSha256));
                }).ConfigureAwait(false);

                checkBudget();
                files.Sort((left, right) => StringComparer.Ordinal.Compare(left.FileSha256, right.FileSha256));
                var result = new ArtifactVerificationResult(facts.MetadataDigest, facts.Profile, facts.AssetId,
                    facts.Width, facts.Height, facts.FileCount, facts.FrameCount, facts.TotalBytes, files.ToImmutableArray());
                checkBudget();
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || deadline.IsCancellationRequested)
            {
                // Linked tokens are implementation details; caller cancellation must retain the caller's token.
                cancellationToken.ThrowIfCancellationRequested();
                throw new DocumentEngineException(EngineErrors.ResourceLimit, "Artifact verification exceeded its cooperative time budget.");
            }
        }
        finally
        {
            Volatile.Write(ref _activeVerification, 0);
        }
    }
}

/// <summary>
/// Bounded observations of captured bytes and decoded samples, not a preservation certificate,
/// global artifact identity, provenance proof or retained payload ownership.
/// </summary>
public sealed record ArtifactVerificationResult(
    string MetadataDigest, string Profile, string AssetId, long Width, long Height,
    int FileCount, int FrameCount, long TotalBytes, ImmutableArray<ArtifactPixelDigest> Files)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Verification => "decoded-rgba8";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string DecoderProfile => "agd.png-rgba8-managed.v1";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Execution => "managed-in-process";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool PixelsDecoded => true;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Admitted => false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool ProvenanceVerified => false;
}

/// <summary>Encoded-file and raw RGBA sample hashes under the result's explicit dimensions and profile.</summary>
public sealed record ArtifactPixelDigest(string FileSha256, string RgbaSha256);
