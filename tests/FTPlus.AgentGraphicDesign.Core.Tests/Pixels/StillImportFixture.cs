using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Pixels;

/// <summary>Independent PNG/input producer for receiver tests; never uses AGD rendering or decoding.</summary>
internal static class StillImportFixture
{
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string Write(string bundle, int width, int height, byte[] rgba, string pngPath = "image.png", bool badAdler = false)
    {
        byte[] png = Png(width, height, rgba, badAdler);
        string hash = Hash(png);
        string metadata = $$$"""
            {
              "schema":"agd.provided-artifact", "version":1,
              "profile":"png8-rgba-srgb-straight-fixed-canvas-v1", "assetId":"tiny",
              "canvas":{"width":{{{width.ToString(CultureInfo.InvariantCulture)}}},"height":{{{height.ToString(CultureInfo.InvariantCulture)}}},"coordinates":"top-left-pixel-edges-x-right-y-down"},
              "cropOrigin":{"x":0,"y":0},
              "files":[{"sha256":"{{{hash}}}","path":"{{{pngPath}}}","byteLength":{{{png.Length.ToString(CultureInfo.InvariantCulture)}}}}],
              "still":{"name":"rest","fileSha256":"{{{hash}}}","pivot":{"x":0.500000,"y":-0.000000}},
              "provenance":{"producer":{"id":"unverified-maker","version":"1"}}
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(bundle, pngPath))!);
        File.WriteAllBytes(Path.Combine(bundle, pngPath), png);
        string descriptor = Path.Combine(bundle, "artifact.json");
        File.WriteAllText(descriptor, metadata, new UTF8Encoding(false));
        return descriptor;
    }

    internal static byte[] Png(int width, int height, byte[] rgba, bool badAdler = false)
    {
        if (rgba.Length != checked(4 * width * height)) throw new ArgumentException("Fixture sample count");
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba.AsSpan(y * width * 4, width * 4));
            }
        byte[] member = compressed.ToArray();
        if (badAdler) member[^1] ^= 1;
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        return [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", header), .. Chunk("IDAT", member), .. Chunk("IEND", [])];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (byte value in chunk.AsSpan(4, data.Length + 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        return chunk;
    }
}
