using System.Security.Cryptography;
using FTPlus.AgentGraphicDesign.Core.Artifacts;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

/// <summary>Success-only privately owned input. Never exposes supplier paths as live dependencies.</summary>
internal sealed record CapturedStill(
    byte[] Metadata, ProvidedArtifactDescriptor Descriptor, byte[] Png, byte[] Rgba,
    string MetadataDigest, string DescriptorSha256, string RgbaSha256);

internal static class NativeStillInput
{
    internal const int MaxEncodedBytes = 33_554_432;

    internal static CapturedStill Capture(string descriptorPath, NativeOperationScope scope)
    {
        byte[]? metadata = null;
        ProvidedArtifactDescriptor? descriptor = null;
        byte[]? png = null;
        DecodedPng? decoded = null;
        var policy = new ArtifactCapturePolicy(MaxEncodedBytes, scope.Budget, scope.Reserve,
            (bytes, model) =>
            {
                if (model.Still is null || model.Clip is not null || model.Files.Length != 1)
                    throw new NativePixelException(ArtifactDescriptorContract.ProfileUnsupported,
                        "Native still import requires one untimed still.");
                if (model.Canvas.Width * model.Canvas.Height > 4_194_304 || model.Files[0].ByteLength > MaxEncodedBytes)
                    throw new NativePixelException(EngineErrors.ResourceLimit, "Still input exceeds native import limits.");
                if (model.Files[0].Path.StartsWith("descriptor.json/", StringComparison.Ordinal))
                    throw new NativePixelException(ArtifactDescriptorContract.ProfileUnsupported,
                        "The PNG parent conflicts with the retained descriptor filename.", "$.files[0].path");
                metadata = bytes;
                descriptor = model;
            });

        // The runner's awaits do not capture synchronization context. All callbacks operate on
        // its private, complete captured bytes; the synchronous native storage lifetime owns them.
        var facts = ArtifactPreflight.RunAsync(descriptorPath, scope.Caller,
            (bytes, _, canvas) =>
            {
                decoded = PngRgbaVerifier.DecodeOwned(bytes, checked((int)canvas.Width),
                    checked((int)canvas.Height), scope.Budget, scope.Reserve);
                png = bytes;
            }, policy).GetAwaiter().GetResult();
        scope.Check();
        if (metadata is null || descriptor is null || png is null || decoded is null)
            throw new NativePixelException(EngineErrors.StoreUnavailable, "Still capture did not complete.");
        scope.Reserve(16_384);
        return new CapturedStill(metadata, descriptor, png, decoded.Rgba, facts.MetadataDigest,
            Hash(metadata, scope), decoded.RgbaSha256);
    }

    internal static string Hash(byte[] bytes, NativeOperationScope scope)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < bytes.Length; offset += 65_536)
        {
            scope.Check();
            hash.AppendData(bytes.AsSpan(offset, Math.Min(65_536, bytes.Length - offset)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
