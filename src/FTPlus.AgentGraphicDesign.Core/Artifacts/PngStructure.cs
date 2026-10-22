using System.Buffers.Binary;

namespace FTPlus.AgentGraphicDesign.Core.Artifacts;

/// <summary>
/// Bounded PNG framing and CRC checks for agd.png-rgba8-structure.v1.
/// IDAT is opaque: success does not establish decodability or validate any pixels.
/// The caller supplies owned bytes and already-validated metadata dimensions.
/// </summary>
internal static class PngStructure
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    // Default-bounds entry (64 MiB cap, cancellation only). Kept as the exact 4-parameter
    // signature the test suite binds to reflectively; delegates to the one walker below.
    internal static void Validate(ReadOnlySpan<byte> data, long width, long height, CancellationToken ct = default) =>
        ValidateBounded(data, width, height, ct, checkBudget: null, maxBytes: 64L * 1024 * 1024);

    // The single structural walker for agd.png-rgba8-structure.v1. Both the artifact preflight
    // (via Validate: compare-to-metadata dimensions, 64 MiB) and native InspectPng (observed
    // dimensions, 32 MiB, cooperative deadline) run this, so neither can silently drift.
    // checkBudget adds a cooperative deadline alongside cancellation; maxBytes caps input.
    internal static void ValidateBounded(ReadOnlySpan<byte> data, long width, long height,
        CancellationToken ct, Action? checkBudget, long maxBytes)
    {
        void Check()
        {
            ct.ThrowIfCancellationRequested();
            checkBudget?.Invoke();
        }
        Check();
        if (data.Length > maxBytes)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, "PNG input exceeds 64 MiB.");
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!data.StartsWith(signature))
            throw Invalid();

        var offset = 8;
        var chunks = 0;
        var colorTags = 0;
        var seenIdat = false;
        var hasIdatBytes = false;
        while (offset < data.Length)
        {
            Check();
            if (++chunks > 4096)
                throw new DocumentEngineException(EngineErrors.ResourceLimit, "PNG input exceeds 4096 chunks.");
            var remaining = data.Length - offset;
            if (remaining < 12)
                throw Invalid();
            var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            // Subtract framing before comparing; only narrow after proving the entire chunk fits.
            if (unsignedLength > (uint)(remaining - 12))
                throw Invalid();
            var length = (int)unsignedLength;
            var type = data.Slice(offset + 4, 4);
            foreach (var letter in type)
                if (letter is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                    throw Invalid();
            if (type[2] is < (byte)'A' or > (byte)'Z')
                throw Invalid();

            var body = data.Slice(offset + 8, length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8 + length, 4));
            var crc = uint.MaxValue;
            var crcBytes = data.Slice(offset + 4, length + 4);
            while (!crcBytes.IsEmpty)
            {
                Check();
                var blockLength = Math.Min(crcBytes.Length, 64 * 1024);
                foreach (var value in crcBytes[..blockLength])
                    crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
                crcBytes = crcBytes[blockLength..];
            }
            Check();
            if (~crc != expectedCrc)
                throw Invalid();

            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(type);
            if (chunks == 1 && chunkType != 0x49484452u)
                throw Invalid();
            offset += length + 12;
            switch (chunkType)
            {
                case 0x49484452u: // IHDR
                    if (chunks != 1 || length != 13)
                        throw Invalid();
                    var actualWidth = BinaryPrimitives.ReadUInt32BigEndian(body);
                    var actualHeight = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                    if (actualWidth is 0 or > int.MaxValue || actualHeight is 0 or > int.MaxValue
                        || actualWidth != width || actualHeight != height)
                        throw Invalid();
                    var depth = body[8];
                    var color = body[9];
                    var legalDepth = color switch
                    {
                        0 => depth is 1 or 2 or 4 or 8 or 16,
                        2 or 4 or 6 => depth is 8 or 16,
                        3 => depth is 1 or 2 or 4 or 8,
                        _ => false
                    };
                    if (!legalDepth || body[10] != 0 || body[11] != 0 || body[12] > 1)
                        throw Invalid();
                    if (depth != 8 || color != 6 || body[12] != 0)
                        throw Unsupported();
                    break;
                case 0x73524742u: // sRGB
                    if (seenIdat || (colorTags & 1) != 0 || length != 1 || body[0] > 3)
                        throw Invalid();
                    colorTags |= 1;
                    break;
                case 0x67414d41u: // gAMA
                    if (seenIdat || (colorTags & 2) != 0 || length != 4)
                        throw Invalid();
                    if (BinaryPrimitives.ReadUInt32BigEndian(body) != 45455u)
                        throw Unsupported();
                    colorTags |= 2;
                    break;
                case 0x6348524du: // cHRM
                    if (seenIdat || (colorTags & 4) != 0 || length != 32)
                        throw Invalid();
                    ReadOnlySpan<uint> chromaticities = [31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000];
                    for (var i = 0; i < chromaticities.Length; i++)
                        if (BinaryPrimitives.ReadUInt32BigEndian(body.Slice(i * 4, 4)) != chromaticities[i])
                            throw Unsupported();
                    colorTags |= 4;
                    break;
                case 0x49444154u: // IDAT
                    // All other accepted chunks after this point must be terminal IEND.
                    seenIdat = true;
                    hasIdatBytes |= length != 0;
                    break;
                case 0x49454e44u: // IEND
                    if (length != 0 || !hasIdatBytes || offset != data.Length)
                        throw Invalid();
                    return;
                default:
                    throw Unsupported();
            }
        }
        throw Invalid();
    }

    private static DocumentEngineException Invalid() =>
        new(ArtifactDescriptorContract.PngInvalid, "Invalid PNG structure.");

    private static DocumentEngineException Unsupported() =>
        new(ArtifactDescriptorContract.ProfileUnsupported, "PNG profile is unsupported by the structural policy.");

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var crc = (uint)i;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            table[i] = crc;
        }
        return table;
    }
}
