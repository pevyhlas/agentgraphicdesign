using System.Buffers.Binary;
using System.Text;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

/// <summary>
/// Original test-only TrueType face: one triangle mapped to A, plus empty .notdef.
/// No external font data/code. Layout follows the OpenType table specifications:
/// https://learn.microsoft.com/typography/opentype/spec/otff
/// </summary>
internal static class SessionTestFont
{
    internal const string Family = "AGD Session Fixture";

    internal static byte[] Create()
    {
        var head = new byte[54];
        U32(head, 0, 0x10000); U32(head, 4, 0x10000); U32(head, 12, 0x5F0F3CF5);
        U16(head, 16, 3); U16(head, 18, 1000); U16(head, 40, 600); U16(head, 42, 700);
        U16(head, 46, 8); U16(head, 48, 2);
        var hhea = new byte[36];
        U32(hhea, 0, 0x10000); U16(hhea, 4, 800); U16(hhea, 6, -200);
        U16(hhea, 10, 700); U16(hhea, 14, 100); U16(hhea, 16, 600);
        U16(hhea, 18, 1); U16(hhea, 34, 2);
        var maxp = new byte[32];
        U32(maxp, 0, 0x10000); U16(maxp, 4, 2); U16(maxp, 6, 3); U16(maxp, 8, 1); U16(maxp, 14, 1);
        var glyf = new byte[30];
        U16(glyf, 0, 1); U16(glyf, 6, 600); U16(glyf, 8, 700); U16(glyf, 10, 2);
        glyf[14] = glyf[15] = glyf[16] = 1; // Three on-curve points, signed 16-bit deltas.
        U16(glyf, 19, 600); U16(glyf, 21, -300); U16(glyf, 27, 700);
        var loca = new byte[6]; U16(loca, 4, 15);
        var hmtx = new byte[8]; U16(hmtx, 0, 600); U16(hmtx, 4, 700);
        var os2 = new byte[78];
        U16(os2, 2, 700); U16(os2, 4, 400); U16(os2, 6, 5); U32(os2, 42, 1);
        Encoding.ASCII.GetBytes("AGDT").CopyTo(os2, 58);
        U16(os2, 62, 64); U16(os2, 64, 65); U16(os2, 66, 65);
        U16(os2, 68, 800); U16(os2, 70, -200); U16(os2, 74, 800); U16(os2, 76, 200);
        var post = new byte[32]; U32(post, 0, 0x30000);
        var cmap = new byte[44]; // Windows Unicode BMP, format4: A→1 and sentinel→0.
        U16(cmap, 2, 1); U16(cmap, 4, 3); U16(cmap, 6, 1); U32(cmap, 8, 12);
        U16(cmap, 12, 4); U16(cmap, 14, 32); U16(cmap, 18, 4);
        U16(cmap, 20, 4); U16(cmap, 22, 1);
        U16(cmap, 26, 65); U16(cmap, 28, 65535); U16(cmap, 32, 65); U16(cmap, 34, 65535);
        U16(cmap, 36, -64); U16(cmap, 38, 1);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = head,
            ["hhea"] = hhea,
            ["maxp"] = maxp,
            ["glyf"] = glyf,
            ["loca"] = loca,
            ["hmtx"] = hmtx,
            ["OS/2"] = os2,
            ["post"] = post,
            ["cmap"] = cmap,
            ["name"] = Names()
        };
        var offset = 12 + tables.Count * 16;
        var font = new byte[offset + tables.Values.Sum(bytes => Align(bytes.Length))];
        U32(font, 0, 0x10000); U16(font, 4, tables.Count);
        U16(font, 6, 128); U16(font, 8, 3); U16(font, 10, tables.Count * 16 - 128);
        var record = 12;
        var headOffset = 0;
        foreach (var (tag, bytes) in tables)
        {
            Encoding.ASCII.GetBytes(tag).CopyTo(font, record);
            U32(font, record + 4, Checksum(bytes)); U32(font, record + 8, (uint)offset);
            U32(font, record + 12, (uint)bytes.Length);
            bytes.CopyTo(font, offset);
            if (tag == "head") headOffset = offset;
            offset += Align(bytes.Length);
            record += 16;
        }
        U32(font, headOffset + 8, unchecked(0xB1B0AFBA - Checksum(font)));
        return font;
    }

    private static byte[] Names()
    {
        (int Id, string Value)[] names = [(1, Family), (2, "Regular"), (3, "AGDSessionFixture-1"),
            (4, Family + " Regular"), (6, "AGDSessionFixture-Regular")];
        var start = 6 + names.Length * 12;
        var data = new byte[start + names.Sum(n => Encoding.BigEndianUnicode.GetByteCount(n.Value))];
        U16(data, 2, names.Length); U16(data, 4, start);
        var offset = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(names[i].Value);
            var record = 6 + i * 12;
            U16(data, record, 3); U16(data, record + 2, 1); U16(data, record + 4, 0x409);
            U16(data, record + 6, names[i].Id); U16(data, record + 8, bytes.Length); U16(data, record + 10, offset);
            bytes.CopyTo(data, start + offset); offset += bytes.Length;
        }
        return data;
    }

    private static int Align(int size) => (size + 3) & ~3;
    private static void U16(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), unchecked((ushort)value));
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static uint Checksum(byte[] bytes)
    {
        uint sum = 0;
        for (var i = 0; i < bytes.Length; i++)
            sum = unchecked(sum + ((uint)bytes[i] << (24 - 8 * (i % 4))));
        return sum;
    }
}
