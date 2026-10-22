using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using FTPlus.AgentGraphicDesign.Core.Artifacts;

namespace FTPlus.AgentGraphicDesign.Core.Pixels;

internal sealed record NativePngObservation(
    int Width, int Height, long EncodedBytes, string FileSha256, string RgbaSha256);

// Original MIT-licensed implementation of the approved native evaluation/PNG contracts.
// Inputs are the service's validated capture: framing, kind, dimensions and payload
// identity are storage responsibilities. Only references to those buffers are retained.
internal static class NativePixelRenderer
{
    private const uint Q = NativePixelTables.Q;
    private const int MaximumPngBytes = 32 * 1024 * 1024;
    private const int BlockBytes = 64 * 1024;
    private const int PngFixedBytes = 58; // Signature, IHDR, sRGB and IEND, including CRCs.
    private static readonly uint[] CrcTable = CreateCrcTable();
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    internal static string CompositeHash(NativePixelDocument doc,
        IReadOnlyDictionary<string, byte[]> payloads, PixelRect rect, Action checkBudget) =>
        CompositeRows(doc, payloads, rect, checkBudget, null);

    // Let M=maximumEncodedBytes, N=actual PNG length, W=rect.Width, L=layer count.
    // Evaluation owns M + (4*W+1) + 24*L array-content bytes on x64. After evaluation
    // and zlib disposal, assembly owns M+N. A conservative reservation is therefore
    // 2*M + 32,769 + 384 bytes, plus the shared 1,024-byte CRC table. No growable
    // stream, full RGBA canvas, ToArray, or payload copy. Codec/hash/object/framework
    // overhead is separate; this is not a bound on GC retention or process RSS.
    internal static (byte[] Data, string RgbaSha256) Encode(NativePixelDocument doc,
        IReadOnlyDictionary<string, byte[]> payloads, PixelRect rect,
        int maximumEncodedBytes, Action checkBudget)
    {
        checkBudget();
        ValidateGeometry(doc, rect);
        if (maximumEncodedBytes < PngFixedBytes + 12 || maximumEncodedBytes > MaximumPngBytes)
            throw Limit();
        checkBudget();
        using var compressed = new BoundedCompressedStream(maximumEncodedBytes, checkBudget);
        string rgbaHash;
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            rgbaHash = CompositeRows(doc, payloads, rect, checkBudget, zlib.Write);

        checkBudget();
        var png = new byte[EncodedLength(compressed.Count)];
        Signature.CopyTo(png);
        var offset = Signature.Length;
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, rect.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], rect.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, ref offset, "IHDR"u8, header, checkBudget);
        WriteChunk(png, ref offset, "sRGB"u8, [0], checkBudget);
        // The 32 MiB encoded cap implies at most 512 IDATs and 515 total chunks.
        for (var start = 0; start < compressed.Count; start += BlockBytes)
        {
            checkBudget();
            var count = Math.Min(BlockBytes, compressed.Count - start);
            WriteChunk(png, ref offset, "IDAT"u8, compressed.Buffer.AsSpan(start, count), checkBudget);
        }
        WriteChunk(png, ref offset, "IEND"u8, [], checkBudget);
        checkBudget();
        return (png, rgbaHash);
    }

    internal static NativePngObservation InspectPng(ReadOnlyMemory<byte> data,
        Action checkBudget, CancellationToken ct = default)
    {
        void Check()
        {
            ct.ThrowIfCancellationRequested();
            checkBudget();
        }

        Check();
        if (data.Length > MaximumPngBytes)
            throw Limit();
        var png = data.Span;
        if (png.Length < 33 || !png.StartsWith(Signature)
            || BinaryPrimitives.ReadUInt32BigEndian(png[8..]) != 13
            || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw InvalidPng();
        var width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        if (width == 0 || height == 0)
            throw InvalidPng();
        if (width > 8192 || height > 8192 || checked((long)width * height) > 4_194_304)
            throw Limit();

        // No capture copy. The reused decoder owns two 4*width rows, 32 KiB history
        // and 1,740 bytes of Huffman array contents: <=100,044 bytes on any canvas.
        // Candidate dimensions are not acceptance. Structural admission shares the one
        // budget-aware PngStructure seam (observed dimensions, the 32 MiB native cap, and
        // the cooperative deadline in addition to cancellation).
        try
        {
            PngStructure.ValidateBounded(png, width, height, ct, checkBudget, MaximumPngBytes);
            var rgbaHash = PngRgbaVerifier.Verify(png, width, height, Check);
            Check();
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var start = 0; start < png.Length; start += BlockBytes)
            {
                Check();
                fileHash.AppendData(png.Slice(start, Math.Min(BlockBytes, png.Length - start)));
            }
            Check();
            return new NativePngObservation((int)width, (int)height, data.Length,
                Convert.ToHexStringLower(fileHash.GetHashAndReset()), rgbaHash);
        }
        catch (DocumentEngineException error)
        {
            throw new NativePixelException(error.Code, error.Message);
        }
    }

    private delegate void WriteRow(ReadOnlySpan<byte> filteredRow);
    private readonly record struct Source(byte[] Pixels, byte[]? Mask, uint Opacity);

    private static string CompositeRows(NativePixelDocument doc,
        IReadOnlyDictionary<string, byte[]> payloads, PixelRect rect,
        Action checkBudget, WriteRow? writeRow)
    {
        checked
        {
            checkBudget();
            ValidateGeometry(doc, rect);
            var sources = new Source[doc.Layers.Length];
            var count = 0;
            foreach (var layer in doc.Layers)
            {
                checkBudget();
                if (layer.Opacity is < 0 or > 1)
                    throw new NativePixelException(EngineErrors.InvalidProperty, "Invalid layer opacity.");
                var millionths = layer.Opacity * 1_000_000m;
                if (millionths != decimal.Truncate(millionths))
                    throw new NativePixelException(EngineErrors.InvalidProperty, "Invalid layer opacity.");
                if (!layer.Visible)
                    continue;
                if (!payloads.TryGetValue(layer.PixelsSha256, out var pixels) || pixels is null
                    || pixels.Length != 24L + (long)doc.Width * doc.Height * 4)
                    throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "Invalid captured pixel payload.");
                byte[]? mask = null;
                if (layer.MaskSha256 is not null
                    && (!payloads.TryGetValue(layer.MaskSha256, out mask) || mask is null
                        || mask.Length != 24L + (long)doc.Width * doc.Height))
                    throw new NativePixelException(NativePixelErrors.PixelContentMismatch, "Invalid captured mask payload.");
                sources[count++] = new Source(pixels, mask, (uint)millionths);
            }

            checkBudget();
            var row = new byte[rect.Width * 4 + 1]; // Filter 0; only row[1..] enters the RGBA hash.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var evaluated = 0;
            var emitted = 0;
            var decode = NativePixelTables.F;
            for (var y = 0; y < rect.Height; y++)
            {
                checkBudget();
                var position = (int)((long)(rect.Y + y) * doc.Width + rect.X);
                for (var x = 0; x < rect.Width; x++, position++)
                {
                    uint red = 0, green = 0, blue = 0, alpha = 0;
                    var pixelOffset = 24 + position * 4;
                    for (var layerIndex = 0; layerIndex < count; layerIndex++)
                    {
                        ref readonly var source = ref sources[layerIndex];
                        // Widen before every product. The maximum numerator plus bias
                        // is 1,090,938,502,912,500,000; all other products are <= Q^2.
                        var a = source.Pixels[pixelOffset + 3];
                        var m = source.Mask is null ? 255 : source.Mask[24 + position];
                        var coverage = Round((long)a * m * source.Opacity * Q, 65_025_000_000L);
                        if (coverage != 0)
                        {
                            var remainder = Q - coverage;
                            red = Round((long)decode[source.Pixels[pixelOffset]] * coverage, Q)
                                + Round((long)red * remainder, Q);
                            green = Round((long)decode[source.Pixels[pixelOffset + 1]] * coverage, Q)
                                + Round((long)green * remainder, Q);
                            blue = Round((long)decode[source.Pixels[pixelOffset + 2]] * coverage, Q)
                                + Round((long)blue * remainder, Q);
                            alpha = coverage + Round((long)alpha * remainder, Q);
                            if (alpha > Q || red > alpha || green > alpha || blue > alpha)
                                throw new InvalidOperationException("Native compositor invariant failed.");
                        }
                        if (++evaluated == 1024)
                        {
                            checkBudget();
                            evaluated = 0;
                        }
                    }
                    var target = 1 + x * 4;
                    var alpha8 = (byte)Round(255L * alpha, Q);
                    row[target] = alpha8 == 0 ? (byte)0 : NativePixelTables.Encode(Round((long)red * Q, alpha));
                    row[target + 1] = alpha8 == 0 ? (byte)0 : NativePixelTables.Encode(Round((long)green * Q, alpha));
                    row[target + 2] = alpha8 == 0 ? (byte)0 : NativePixelTables.Encode(Round((long)blue * Q, alpha));
                    row[target + 3] = alpha8;
                    if (++emitted == 1024)
                    {
                        checkBudget();
                        emitted = 0;
                    }
                }
                checkBudget();
                hash.AppendData(row.AsSpan(1));
                writeRow?.Invoke(row);
                checkBudget();
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
    }

    private static uint Round(long numerator, long denominator) =>
        checked((uint)((numerator + denominator / 2) / denominator));

    private static void ValidateGeometry(NativePixelDocument doc, PixelRect rect)
    {
        if (doc.Width <= 0 || doc.Height <= 0)
            throw new NativePixelException(EngineErrors.InvalidDimensions, "Invalid native canvas dimensions.");
        if (doc.Width > 8192 || doc.Height > 8192 || checked((long)doc.Width * doc.Height) > 4_194_304)
            throw Limit();
        if (doc.Layers.IsDefaultOrEmpty)
            throw new NativePixelException(EngineErrors.InvalidProperty, "Native layer list is empty.");
        if (doc.Layers.Length > 16)
            throw Limit();
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
            || checked((long)rect.X + rect.Width) > doc.Width
            || checked((long)rect.Y + rect.Height) > doc.Height)
            throw new NativePixelException(EngineErrors.InvalidArguments, "Invalid native composite rectangle.");
    }

    private static int EncodedLength(int compressedBytes) =>
        checked(PngFixedBytes + compressedBytes + 12 * ((compressedBytes + BlockBytes - 1) / BlockBytes));

    // A single allocation; reject the future PNG size (including IDAT framing)
    // before writing. In particular, MemoryStream capacity doubling is impossible.
    private sealed class BoundedCompressedStream(int maximumBytes, Action checkBudget) : Stream
    {
        internal byte[] Buffer { get; } = new byte[maximumBytes];
        internal int Count { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() => checkBudget();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            checkBudget();
            if (buffer.Length > maximumBytes - Count || EncodedLength(checked(Count + buffer.Length)) > maximumBytes)
                throw Limit();
            while (!buffer.IsEmpty)
            {
                checkBudget();
                var length = Math.Min(buffer.Length, BlockBytes);
                buffer[..length].CopyTo(Buffer.AsSpan(Count, length));
                Count = checked(Count + length);
                buffer = buffer[length..];
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static void WriteChunk(Span<byte> png, ref int offset, ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> body, Action checkBudget)
    {
        checkBudget();
        var chunk = png.Slice(offset, checked(body.Length + 12));
        BinaryPrimitives.WriteInt32BigEndian(chunk, body.Length);
        type.CopyTo(chunk[4..]);
        body.CopyTo(chunk[8..]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[(8 + body.Length)..], Crc32(chunk.Slice(4, body.Length + 4), checkBudget));
        offset = checked(offset + chunk.Length);
    }

    private static uint Crc32(ReadOnlySpan<byte> data, Action checkBudget)
    {
        var crc = uint.MaxValue;
        while (!data.IsEmpty)
        {
            checkBudget();
            var count = Math.Min(data.Length, BlockBytes);
            foreach (var value in data[..count])
                crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 255];
            data = data[count..];
        }
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            var remainder = value;
            for (var bit = 0; bit < 8; bit++)
                remainder = (remainder >> 1) ^ (0xedb88320u * (remainder & 1));
            table[value] = remainder;
        }
        return table;
    }

    private static NativePixelException Limit() =>
        new(EngineErrors.ResourceLimit, "Native PNG evaluation resource limit exceeded.");
    private static NativePixelException InvalidPng() =>
        new(ArtifactDescriptorContract.PngInvalid, "Invalid PNG structure.");
}
