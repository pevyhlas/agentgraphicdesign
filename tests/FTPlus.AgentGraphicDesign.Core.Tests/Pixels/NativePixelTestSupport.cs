using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

// Native pixel service/store tests share the one host-lifetime admission slot, so they must
// not run in parallel with each other or with any other collection.
[CollectionDefinition(NativePixelCollection.Name, DisableParallelization = true)]
public sealed class NativePixelCollection
{
    public const string Name = "NativePixelStorage";
}

/// <summary>
/// Independent literal reference bytes/hashes from native-pixels-v1.md and on-disk fixture
/// helpers. Golden values are hard-coded here, never derived by the production serializer.
/// </summary>
internal static class PixelFixture
{
    public const string AssetId = "11111111-1111-4111-8111-111111111111";
    public const string LayerId = "22222222-2222-4222-8222-222222222222";

    // The four 1x1 framed payload vectors (complete bytes + SHA-256) from §Literal Vectors.
    public const string TransparentHex = "4147445049584c0a0000000100000001000000010000000100000000";
    public const string TransparentHash = "dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b";
    public const string HiddenHex = "4147445049584c0a0000000100000001000000010000000111223300";
    public const string HiddenHash = "b6a504dc8edff031f684f4a317efd10f6626ec3d9802c0291e12711c1a7677cf";
    public const string FullMaskHex = "4147445049584c0a00000001000000020000000100000001ff";
    public const string FullMaskHash = "acaa4a126cbcef28d29bf07df0b4ca22fe3561515f79145914717a7d58bb5a77";
    public const string ZeroMaskHex = "4147445049584c0a0000000100000002000000010000000100";
    public const string ZeroMaskHash = "270d14708db43fb170ef64ce3d984f7c60781198a2875149e4f4df9c5f2edb29";

    // The base 392-byte canonical metadata and its three semantic-change source digests.
    public const string BaseMetadata =
        """{"schema":"agd.native-pixels","version":1,"profile":"agd.pixels.rgba8-linear-over.v1","assetId":"11111111-1111-4111-8111-111111111111","revn":1,"name":"Pixel","canvas":{"width":1,"height":1},"layers":[{"id":"22222222-2222-4222-8222-222222222222","name":"Layer 1","visible":true,"opacity":1,"pixelsSha256":"dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b","maskSha256":null}]}""";
    public const string BaseDigest = "5183bc41cda41700726ef8584f6be9977b2f59a1602e101ef9e20554b46a6b91";
    public const string OpacityDigest = "dc5a3216fb4da08ac9e9a5a6203cb2055b368eb978c4c9f34e3bf5b2fbd8b0cd";
    public const string MaskDigest = "e77ebd1bf294c40179233e2a82fe196a85eea3bf530185e20db8c0f4c5195754";
    public const string HiddenDigest = "80de8ffac45996eeedbc18e0f4ddfaa5b4d4f97c4193232889d0e5e14783a78c";

    [DllImport("libc", SetLastError = true)]
    private static extern int mkfifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    /// <summary>Writes a native asset directory from literal metadata bytes and literal payload hex.</summary>
    public static void WriteAsset(string assetDirectory, string metadata, params (string Hash, string Hex)[] payloads)
    {
        Directory.CreateDirectory(assetDirectory);
        string payloadDirectory = Path.Combine(assetDirectory, "payloads");
        Directory.CreateDirectory(payloadDirectory);
        File.WriteAllBytes(Path.Combine(assetDirectory, "asset.agd-pixels.json"), Encoding.UTF8.GetBytes(metadata));
        foreach (var (hash, hex) in payloads)
            File.WriteAllBytes(Path.Combine(payloadDirectory, hash + ".bin"), Convert.FromHexString(hex));
    }

    public static void WritePayload(string assetDirectory, string hash, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(assetDirectory, "payloads", hash + ".bin"), bytes);

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex);

    public static string LockPath(string parent, string leaf) =>
        Path.Combine(parent, ".agd-pixels-lock-" +
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(leaf))) + ".lock");

    public static bool TryMakeFifo(string path) => mkfifo(path, 0b110_100_100) == 0;

    /// <summary>Builds metadata for N layers (all referencing one pixels hash), optionally masked.</summary>
    public static string MultiLayerMetadata(int layers, int width, int height, string pixelsHash, string? maskHash = null)
    {
        var text = new StringBuilder();
        text.Append("{\"schema\":\"agd.native-pixels\",\"version\":1,\"profile\":\"agd.pixels.rgba8-linear-over.v1\",")
            .Append("\"assetId\":\"").Append(Guid.NewGuid().ToString("D")).Append("\",\"revn\":1,\"name\":\"Multi\",")
            .Append("\"canvas\":{\"width\":").Append(width).Append(",\"height\":").Append(height).Append("},\"layers\":[");
        for (int i = 0; i < layers; i++)
        {
            if (i != 0) text.Append(',');
            text.Append("{\"id\":\"").Append(Guid.NewGuid().ToString("D")).Append("\",\"name\":\"L")
                .Append(i).Append("\",\"visible\":true,\"opacity\":1,\"pixelsSha256\":\"").Append(pixelsHash)
                .Append("\",\"maskSha256\":").Append(maskHash is null ? "null" : "\"" + maskHash + "\"").Append('}');
        }
        text.Append("]}");
        return text.ToString();
    }
}

/// <summary>A disposable temp-root manager that cleans workspaces after each test class.</summary>
public abstract class NativePixelTestBase : IDisposable
{
    private readonly List<string> _roots = [];

    protected string NewWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "agdpx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    public void Dispose()
    {
        foreach (string root in _roots)
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        GC.SuppressFinalize(this);
    }
}
