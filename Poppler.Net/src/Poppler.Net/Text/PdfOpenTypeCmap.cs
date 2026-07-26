using System.Buffers.Binary;

namespace Poppler.Text;

/// <summary>
/// Minimal, allocation-bounded sfnt cmap reader used only as a Unicode
/// fallback when a PDF omitted ToUnicode. It supports cmap formats 4 and 12.
/// </summary>
internal sealed class PdfOpenTypeCmap
{
    private readonly Dictionary<uint, int> _unicodeByGlyph;

    private PdfOpenTypeCmap(Dictionary<uint, int> unicodeByGlyph)
    {
        _unicodeByGlyph = unicodeByGlyph;
    }

    public static PdfOpenTypeCmap? TryParse(ReadOnlySpan<byte> bytes, int maximumMappings)
    {
        if (bytes.Length < 12)
            return null;
        uint signature = ReadUInt32(bytes, 0);
        if (signature != 0x00010000 &&
            signature != 0x74727565 &&
            signature != 0x4F54544F)
            return null;

        int tableCount = ReadUInt16(bytes, 4);
        if (tableCount < 1 || tableCount > 4096 || 12 + tableCount * 16 > bytes.Length)
            return null;

        int cmapOffset = -1;
        int cmapLength = 0;
        for (int index = 0; index < tableCount; index++)
        {
            int record = 12 + index * 16;
            if (ReadUInt32(bytes, record) != 0x636D6170)
                continue;
            uint offset = ReadUInt32(bytes, record + 8);
            uint length = ReadUInt32(bytes, record + 12);
            if (offset <= int.MaxValue &&
                length <= int.MaxValue &&
                (ulong)offset + length <= (ulong)bytes.Length)
            {
                cmapOffset = (int)offset;
                cmapLength = (int)length;
            }

            break;
        }

        if (cmapOffset < 0 || cmapLength < 4)
            return null;
        ReadOnlySpan<byte> cmap = bytes.Slice(cmapOffset, cmapLength);
        int encodingCount = ReadUInt16(cmap, 2);
        if (encodingCount < 1 || encodingCount > 1024 || 4 + encodingCount * 8 > cmap.Length)
            return null;

        var candidates = new List<(int Rank, int Offset)>();
        for (int index = 0; index < encodingCount; index++)
        {
            int record = 4 + index * 8;
            int platform = ReadUInt16(cmap, record);
            int encoding = ReadUInt16(cmap, record + 2);
            uint offset = ReadUInt32(cmap, record + 4);
            if (offset > int.MaxValue || (ulong)offset + 2 > (ulong)cmap.Length)
                continue;
            int format = ReadUInt16(cmap, (int)offset);
            int rank = format switch
            {
                12 when platform == 3 && encoding == 10 => 0,
                12 when platform == 0 => 1,
                4 when platform == 3 && encoding is 1 or 0 => 2,
                4 when platform == 0 => 3,
                _ => int.MaxValue
            };
            if (rank != int.MaxValue)
                candidates.Add((rank, (int)offset));
        }

        var map = new Dictionary<uint, int>();
        foreach ((int _, int offset) in candidates.OrderBy(candidate => candidate.Rank))
        {
            int format = ReadUInt16(cmap, offset);
            bool parsed = format switch
            {
                4 => ParseFormat4(cmap[offset..], map, maximumMappings),
                12 => ParseFormat12(cmap[offset..], map, maximumMappings),
                _ => false
            };
            if (parsed && map.Count > 0)
                return new PdfOpenTypeCmap(map);
            map.Clear();
        }

        return null;
    }

    public bool TryGetUnicode(uint glyphId, out int scalar) =>
        _unicodeByGlyph.TryGetValue(glyphId, out scalar);

    private static bool ParseFormat12(
        ReadOnlySpan<byte> table,
        Dictionary<uint, int> map,
        int maximumMappings)
    {
        if (table.Length < 16)
            return false;
        uint length = ReadUInt32(table, 4);
        uint groups = ReadUInt32(table, 12);
        if (length > (uint)table.Length ||
            groups > int.MaxValue ||
            16UL + groups * 12UL > length)
        {
            return false;
        }

        for (int group = 0; group < (int)groups; group++)
        {
            int offset = 16 + group * 12;
            uint start = ReadUInt32(table, offset);
            uint end = ReadUInt32(table, offset + 4);
            uint firstGlyph = ReadUInt32(table, offset + 8);
            if (start > end || end > 0x10FFFF)
                return false;
            ulong count = (ulong)end - start + 1;
            if ((ulong)map.Count + count > (ulong)maximumMappings)
                return false;
            for (uint scalar = start; scalar <= end; scalar++)
            {
                ulong glyphValue = (ulong)firstGlyph + scalar - start;
                if (glyphValue > uint.MaxValue)
                    return false;
                uint glyph = (uint)glyphValue;
                map.TryAdd(glyph, checked((int)scalar));
                if (scalar == uint.MaxValue)
                    break;
            }
        }

        return true;
    }

    private static bool ParseFormat4(
        ReadOnlySpan<byte> table,
        Dictionary<uint, int> map,
        int maximumMappings)
    {
        if (table.Length < 16)
            return false;
        int length = ReadUInt16(table, 2);
        int segmentCount = ReadUInt16(table, 6) / 2;
        if (length > table.Length || segmentCount < 1 || segmentCount > 8192)
            return false;

        int endCodes = 14;
        int startCodes = endCodes + segmentCount * 2 + 2;
        int deltas = startCodes + segmentCount * 2;
        int rangeOffsets = deltas + segmentCount * 2;
        if (rangeOffsets + segmentCount * 2 > length)
            return false;

        for (int segment = 0; segment < segmentCount; segment++)
        {
            int start = ReadUInt16(table, startCodes + segment * 2);
            int end = ReadUInt16(table, endCodes + segment * 2);
            short delta = ReadInt16(table, deltas + segment * 2);
            int rangeOffset = ReadUInt16(table, rangeOffsets + segment * 2);
            if (start > end)
                continue;
            for (int scalar = start; scalar <= end && scalar != 0xFFFF; scalar++)
            {
                if (map.Count >= maximumMappings)
                    return false;
                uint glyph;
                if (rangeOffset == 0)
                {
                    glyph = (ushort)(scalar + delta);
                }
                else
                {
                    int wordOffset =
                        rangeOffsets + segment * 2 +
                        rangeOffset +
                        (scalar - start) * 2;
                    if (wordOffset < 0 || wordOffset + 2 > length)
                        continue;
                    int value = ReadUInt16(table, wordOffset);
                    glyph = value == 0 ? 0U : (ushort)(value + delta);
                }

                if (glyph != 0)
                    map.TryAdd(glyph, scalar);
            }
        }

        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));

    private static short ReadInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
}
