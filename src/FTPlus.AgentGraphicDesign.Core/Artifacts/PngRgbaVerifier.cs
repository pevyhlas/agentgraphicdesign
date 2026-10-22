using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FTPlus.AgentGraphicDesign.Core.Artifacts;

/// <summary>PNG-only RFC 1950/1951 verification over owned, PngStructure-validated RGBA8 bytes.</summary>
internal static class PngRgbaVerifier
{
    internal static string Verify(ReadOnlySpan<byte> png, long width, long height, Action checkBudget)
    {
        checkBudget();
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || width * height > 16_777_216)
            throw Limit();
        var expected = checked((int)(height * (1 + 4 * width)));
        if (expected > 67_117_056)
            throw Limit();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var decoder = new Decoder(png, checked((int)width * 4), expected, checkBudget, hash);
        return decoder.Verify();
    }

    /// <summary>
    /// Import-only owned output. The caller owns structurally validated PNG bytes; no samples
    /// escape until the SAME decoder has checked the complete zlib stream, Adler and EOF.
    /// Reservations precede output, history, row and Huffman allocation. No second codec.
    /// </summary>
    internal static DecodedPng DecodeOwned(ReadOnlySpan<byte> png, int width, int height,
        Action checkBudget, Action<long> reserve)
    {
        checkBudget();
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 4_194_304)
            throw Limit();
        int rowBytes = checked(width * 4);
        int expected = checked(height * (1 + rowBytes));
        reserve((long)rowBytes * height + 32_768 + 2L * rowBytes + 16_384);
        var rgba = new byte[checked(rowBytes * height)];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var decoder = new Decoder(png, rowBytes, expected, checkBudget, hash, rgba);
        string digest = decoder.Verify();
        return new DecodedPng(rgba, digest);
    }

    private static DocumentEngineException Invalid() => new(ArtifactDescriptorContract.PngInvalid, "Invalid PNG compressed samples.");
    private static DocumentEngineException Limit() => new(EngineErrors.ResourceLimit, "PNG verification resource limit exceeded.");

    private ref struct Decoder
    {
        private IdatReader _reader;
        private readonly Action _checkBudget;
        private readonly IncrementalHash _hash;
        private readonly byte[]? _output;
        private int _outputOffset;
        private readonly int _expected;
        private readonly byte[] _history = new byte[32768];
        private byte[] _row;
        private byte[] _previous;
        private readonly Huffman _codeLengths = new(19);
        private readonly Huffman _literals = new(288);
        private readonly Huffman _distances = new(32);
        private int _produced;
        private int _column = -1;
        private int _filter;
        private int _symbolsSinceCheck;
        private int _bytesSinceCheck;
        private uint _adlerLow = 1;
        private uint _adlerHigh;

        internal Decoder(ReadOnlySpan<byte> png, int rowBytes, int expected, Action checkBudget,
            IncrementalHash hash, byte[]? output = null)
        {
            _reader = new IdatReader(png, checkBudget);
            _row = new byte[rowBytes];
            _previous = new byte[rowBytes];
            _expected = expected;
            _checkBudget = checkBudget;
            _hash = hash;
            _output = output;
        }

        internal string Verify()
        {
            var cmf = _reader.ReadBits(8);
            var flg = _reader.ReadBits(8);
            if ((cmf & 15) != 8 || (cmf >> 4) > 7 || ((cmf << 8) | flg) % 31 != 0 || (flg & 32) != 0)
                throw Invalid();
            var window = 1 << ((cmf >> 4) + 8);
            ReadOnlySpan<ushort> lengthBases = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
                35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
            ReadOnlySpan<byte> lengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
                3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
            ReadOnlySpan<ushort> distanceBases = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
                257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
            var blocks = 0;
            bool final;
            do
            {
                Budget();
                if (++blocks > 65536)
                    throw Limit();
                final = _reader.ReadBits(1) != 0;
                var type = _reader.ReadBits(2);
                if (type == 0)
                {
                    _reader.Align();
                    var length = _reader.ReadBits(16);
                    if ((length ^ _reader.ReadBits(16)) != 65535)
                        throw Invalid();
                    for (var i = 0; i < length; i++)
                        Emit((byte)_reader.ReadBits(8));
                    continue;
                }
                if (type == 3)
                    throw Invalid();
                SetTrees(type);
                while (true)
                {
                    var symbol = ReadSymbol(_literals);
                    if (symbol < 256)
                    {
                        Emit((byte)symbol);
                        continue;
                    }
                    if (symbol == 256)
                        break;
                    if (symbol > 285)
                        throw Invalid();
                    var length = lengthBases[symbol - 257] + _reader.ReadBits(lengthExtra[symbol - 257]);
                    // RFC 1951 assigns length 258 to symbol 285, not 284 with extra bits 31.
                    if (symbol == 284 && length == 258)
                        throw Invalid();
                    var distanceSymbol = ReadSymbol(_distances);
                    if (distanceSymbol > 29)
                        throw Invalid();
                    var distance = distanceBases[distanceSymbol]
                        + _reader.ReadBits(distanceSymbol < 4 ? 0 : distanceSymbol / 2 - 1);
                    if (distance > window || distance > _produced)
                        throw Invalid();
                    // History is filtered data, including filter bytes. Re-read on each overlapping copy.
                    for (var i = 0; i < length; i++)
                        Emit(_history[(_produced - distance) & 32767]);
                }
            } while (!final);

            _reader.Align(); // RFC alignment bits need not be zero; no trailer bytes have been prefetched.
            uint adler = 0;
            for (var i = 0; i < 4; i++)
                adler = (adler << 8) | (uint)_reader.ReadBits(8);
            if (adler != ((_adlerHigh << 16) | _adlerLow) || _produced != _expected || _reader.TryReadByte(out _))
                throw Invalid();
            Budget();
            return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }

        private void SetTrees(int type)
        {
            Span<byte> lengths = stackalloc byte[320];
            if (type == 1)
            {
                lengths[..144].Fill(8);
                lengths[144..256].Fill(9);
                lengths[256..280].Fill(7);
                lengths[280..288].Fill(8);
                lengths[288..].Fill(5);
                _literals.Build(lengths[..288], allowSingle: true);
                _distances.Build(lengths[288..], allowSingle: true);
                return;
            }

            var literalCount = _reader.ReadBits(5) + 257;
            var distanceCount = _reader.ReadBits(5) + 1;
            var codeLengthCount = _reader.ReadBits(4) + 4;
            if (literalCount > 286)
                throw Invalid();
            ReadOnlySpan<byte> order = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
            Span<byte> codeLengths = stackalloc byte[19];
            codeLengths.Clear();
            for (var i = 0; i < codeLengthCount; i++)
                codeLengths[order[i]] = (byte)_reader.ReadBits(3);
            _codeLengths.Build(codeLengths, allowSingle: false);
            var count = literalCount + distanceCount;
            var index = 0;
            while (index < count)
            {
                var symbol = ReadSymbol(_codeLengths);
                if (symbol <= 15)
                {
                    lengths[index++] = (byte)symbol;
                    continue;
                }
                if (symbol == 16 && index == 0)
                    throw Invalid();
                var repeat = symbol switch
                {
                    16 => 3 + _reader.ReadBits(2),
                    17 => 3 + _reader.ReadBits(3),
                    _ => 11 + _reader.ReadBits(7)
                };
                if (repeat > count - index)
                    throw Invalid();
                var value = symbol == 16 ? lengths[index - 1] : (byte)0;
                lengths.Slice(index, repeat).Fill(value);
                index += repeat;
            }
            if (lengths[256] == 0)
                throw Invalid();
            _literals.Build(lengths[..literalCount], allowSingle: true);
            _distances.Build(lengths.Slice(literalCount, distanceCount), allowSingle: true, allowEmpty: true);
        }

        private int ReadSymbol(Huffman tree)
        {
            if (++_symbolsSinceCheck == 1024)
                Budget();
            var code = 0;
            var offset = 0;
            for (var length = 1; length <= tree.MaxLength; length++)
            {
                // Huffman code bits are MSB-first, unlike other fields read through the LSB bit reader.
                code = (code << 1) | _reader.ReadBits(1);
                var index = code - tree.First[length];
                if (index >= 0 && index < tree.Counts[length])
                    return tree.Symbols[offset + index];
                offset += tree.Counts[length];
            }
            throw Invalid();
        }

        private void Emit(byte value)
        {
            if (_produced == _expected)
                throw Invalid();
            _history[_produced++ & 32767] = value;
            _adlerLow += value;
            if (_adlerLow >= 65521)
                _adlerLow -= 65521;
            _adlerHigh += _adlerLow;
            if (_adlerHigh >= 65521)
                _adlerHigh -= 65521;
            if (++_bytesSinceCheck == 1024)
                Budget();
            if (_column == -1)
            {
                if (value > 4)
                    throw Invalid();
                _filter = value;
                _column = 0;
                return;
            }
            _row[_column++] = value;
            if (_column != _row.Length)
                return;

            for (var start = 0; start < _row.Length; start += 1024)
            {
                Budget();
                var end = Math.Min(start + 1024, _row.Length);
                for (var i = start; i < end; i++)
                {
                    var left = i < 4 ? 0 : _row[i - 4];
                    var up = _previous[i];
                    var corner = i < 4 ? 0 : _previous[i - 4];
                    var predictor = _filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        _ => Paeth(left, up, corner)
                    };
                    _row[i] = unchecked((byte)(_row[i] + predictor));
                }
                _hash.AppendData(_row.AsSpan(start, end - start));
                if (_output is not null)
                    _row.AsSpan(start, end - start).CopyTo(_output.AsSpan(_outputOffset + start));
            }
            _outputOffset += _row.Length;
            Budget();
            (_row, _previous) = (_previous, _row);
            _column = -1;
        }

        private static int Paeth(int left, int up, int corner)
        {
            var p = left + up - corner;
            var a = Math.Abs(p - left);
            var b = Math.Abs(p - up);
            var c = Math.Abs(p - corner);
            return a <= b && a <= c ? left : b <= c ? up : corner;
        }

        private void Budget()
        {
            _checkBudget();
            _bytesSinceCheck = 0;
            _symbolsSinceCheck = 0;
        }
    }

    private sealed class Huffman(int capacity)
    {
        internal readonly int[] Counts = new int[16];
        internal readonly int[] First = new int[16];
        internal readonly int[] Symbols = new int[capacity];
        internal int MaxLength;

        internal void Build(ReadOnlySpan<byte> lengths, bool allowSingle, bool allowEmpty = false)
        {
            Array.Clear(Counts);
            MaxLength = 0;
            foreach (var length in lengths)
            {
                if (length > 15)
                    throw Invalid();
                if (length != 0)
                {
                    Counts[length]++;
                    MaxLength = Math.Max(MaxLength, length);
                }
            }
            if (MaxLength == 0)
            {
                if (!allowEmpty)
                    throw Invalid();
                return;
            }
            var remaining = 1;
            Span<int> next = stackalloc int[16];
            var total = 0;
            var code = 0;
            for (var length = 1; length <= 15; length++)
            {
                remaining = (remaining << 1) - Counts[length];
                if (remaining < 0)
                    throw Invalid();
                code = (code + Counts[length - 1]) << 1;
                First[length] = code;
                next[length] = total;
                total += Counts[length];
            }
            if (remaining != 0 && !(allowSingle && MaxLength == 1 && Counts[1] == 1))
                throw Invalid();
            for (var symbol = 0; symbol < lengths.Length; symbol++)
                if (lengths[symbol] != 0)
                    Symbols[next[lengths[symbol]]++] = symbol;
        }
    }

    private ref struct IdatReader(ReadOnlySpan<byte> png, Action checkBudget)
    {
        private readonly ReadOnlySpan<byte> _png = png;
        private readonly Action _checkBudget = checkBudget;
        private int _chunks;
        private int _nextChunk = 8;
        private int _position;
        private int _end;
        private int _bits;
        private int _bitCount;
        private bool _seenIdat;
        private bool _finished;

        // All callers request 0..16 bits. Only the byte containing the last requested bit is consumed.
        internal int ReadBits(int count)
        {
            var result = 0;
            var shift = 0;
            while (count > 0)
            {
                if (_bitCount == 0)
                {
                    if (!TryReadByte(out var value))
                        throw Invalid();
                    _bits = value;
                    _bitCount = 8;
                }
                var take = Math.Min(count, _bitCount);
                result |= (_bits & ((1 << take) - 1)) << shift;
                _bits >>= take;
                _bitCount -= take;
                count -= take;
                shift += take;
            }
            return result;
        }

        internal void Align()
        {
            _bits = 0;
            _bitCount = 0;
        }

        internal bool TryReadByte(out byte value)
        {
            value = 0;
            if (_finished)
                return false;
            while (_position == _end)
            {
                // Empty IDAT runs do source work without emitting bytes or decoding symbols.
                if ((++_chunks & 63) == 0)
                    _checkBudget();
                // Framing/allowlist/CRC were already validated. These subtraction checks also keep
                // accidental truncated framing from escaping as a span/index exception.
                if (_nextChunk > _png.Length - 12)
                    throw Invalid();
                var length = BinaryPrimitives.ReadUInt32BigEndian(_png.Slice(_nextChunk, 4));
                if (length > (uint)(_png.Length - _nextChunk - 12))
                    throw Invalid();
                var type = BinaryPrimitives.ReadUInt32BigEndian(_png.Slice(_nextChunk + 4, 4));
                _position = _nextChunk + 8;
                _end = _position + (int)length;
                _nextChunk = _end + 4;
                if (type == 0x49444154u)
                {
                    _seenIdat = true;
                    continue;
                }
                _position = _end;
                if (_seenIdat || type == 0x49454e44u)
                {
                    _finished = true;
                    return false;
                }
            }
            value = _png[_position++];
            return true;
        }
    }
}

internal sealed record DecodedPng(byte[] Rgba, string RgbaSha256);
