using System.Buffers.Binary;

namespace Poppler.Text;

/// <summary>
/// Bounded reader for the non-contextual GSUB operations that are directly
/// useful while rendering PDF fallback fonts: standard ligatures and vertical
/// glyph alternates. PDF character positioning remains authoritative.
/// </summary>
internal sealed class PdfOpenTypeLayout
{
    private const int MaximumLookups = 16_384;
    private const int MaximumSubstitutions = 1_000_000;

    private readonly IReadOnlyList<Dictionary<uint, uint>> _verticalLookups;
    private readonly Dictionary<GlyphSequence, uint> _ligatures;

    private PdfOpenTypeLayout(
        IReadOnlyList<Dictionary<uint, uint>> verticalLookups,
        Dictionary<GlyphSequence, uint> ligatures)
    {
        _verticalLookups = verticalLookups;
        _ligatures = ligatures;
    }

    public static PdfOpenTypeLayout? TryParse(ReadOnlySpan<byte> font)
    {
        try
        {
            if (!TryFindTable(font, "GSUB"u8, out ReadOnlySpan<byte> gsub) ||
                gsub.Length < 10)
            {
                return null;
            }
            int featureListOffset = UInt16(gsub, 6);
            int lookupListOffset = UInt16(gsub, 8);
            if (featureListOffset <= 0 ||
                lookupListOffset <= 0 ||
                featureListOffset >= gsub.Length ||
                lookupListOffset >= gsub.Length)
            {
                return null;
            }

            Dictionary<uint, int[]> features =
                ReadFeatures(gsub, featureListOffset);
            int[] verticalIndexes = FeatureLookups(
                features,
                Tag("vert"),
                Tag("vrt2"));
            int[] ligatureIndexes = FeatureLookups(
                features,
                Tag("liga"),
                Tag("rlig"));
            var vertical = new List<Dictionary<uint, uint>>();
            var ligatures = new Dictionary<GlyphSequence, uint>();
            foreach (int lookup in verticalIndexes)
            {
                Dictionary<uint, uint>? map = ReadSingleLookup(
                    gsub,
                    lookupListOffset,
                    lookup);
                if (map is { Count: > 0 })
                    vertical.Add(map);
            }
            foreach (int lookup in ligatureIndexes)
                ReadLigatureLookup(gsub, lookupListOffset, lookup, ligatures);
            return vertical.Count == 0 && ligatures.Count == 0
                ? null
                : new PdfOpenTypeLayout(vertical, ligatures);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or
            OverflowException or
            IndexOutOfRangeException)
        {
            return null;
        }
    }

    public uint ApplyVertical(uint glyph)
    {
        foreach (Dictionary<uint, uint> lookup in _verticalLookups)
        {
            if (lookup.TryGetValue(glyph, out uint replacement))
                glyph = replacement;
        }
        return glyph;
    }

    public bool TryGetLigature(
        IReadOnlyList<uint> glyphs,
        out uint ligature)
    {
        if (glyphs.Count < 2)
        {
            ligature = 0;
            return false;
        }
        return _ligatures.TryGetValue(
            new GlyphSequence(glyphs.ToArray()),
            out ligature);
    }

    private static Dictionary<uint, int[]> ReadFeatures(
        ReadOnlySpan<byte> gsub,
        int offset)
    {
        int count = UInt16(gsub, offset);
        if (count > MaximumLookups ||
            offset + 2L + count * 6L > gsub.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        var result = new Dictionary<uint, int[]>();
        for (int index = 0; index < count; index++)
        {
            int record = offset + 2 + index * 6;
            uint tag = UInt32(gsub, record);
            int featureOffset = checked(offset + UInt16(gsub, record + 4));
            int lookupCount = UInt16(gsub, featureOffset + 2);
            if (lookupCount > MaximumLookups ||
                featureOffset + 4L + lookupCount * 2L > gsub.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            var lookups = new int[lookupCount];
            for (int item = 0; item < lookupCount; item++)
                lookups[item] = UInt16(gsub, featureOffset + 4 + item * 2);
            result[tag] = lookups;
        }
        return result;
    }

    private static int[] FeatureLookups(
        IReadOnlyDictionary<uint, int[]> features,
        params uint[] tags) =>
        tags.Where(features.ContainsKey)
            .SelectMany(tag => features[tag])
            .Distinct()
            .ToArray();

    private static Dictionary<uint, uint>? ReadSingleLookup(
        ReadOnlySpan<byte> gsub,
        int lookupListOffset,
        int lookupIndex)
    {
        if (!TryReadLookup(
                gsub,
                lookupListOffset,
                lookupIndex,
                out int lookupType,
                out int[] subtables))
        {
            return null;
        }
        var result = new Dictionary<uint, uint>();
        foreach (int subtable in subtables)
        {
            int effectiveType = lookupType;
            int effectiveOffset = subtable;
            if (!ResolveExtension(
                    gsub,
                    ref effectiveType,
                    ref effectiveOffset))
            {
                continue;
            }
            if (effectiveType != 1)
                continue;
            ReadSingleSubtable(gsub, effectiveOffset, result);
            if (result.Count > MaximumSubstitutions)
                throw new ArgumentOutOfRangeException(nameof(lookupIndex));
        }
        return result;
    }

    private static void ReadSingleSubtable(
        ReadOnlySpan<byte> gsub,
        int offset,
        Dictionary<uint, uint> result)
    {
        int format = UInt16(gsub, offset);
        int coverageOffset = checked(offset + UInt16(gsub, offset + 2));
        uint[] coverage = ReadCoverage(gsub, coverageOffset);
        if (format == 1)
        {
            short delta = Int16(gsub, offset + 4);
            foreach (uint glyph in coverage)
                result[glyph] = (ushort)(glyph + delta);
            return;
        }
        if (format != 2)
            return;
        int count = UInt16(gsub, offset + 4);
        if (count != coverage.Length ||
            offset + 6L + count * 2L > gsub.Length)
        {
            return;
        }
        for (int index = 0; index < count; index++)
            result[coverage[index]] = UInt16(gsub, offset + 6 + index * 2);
    }

    private static void ReadLigatureLookup(
        ReadOnlySpan<byte> gsub,
        int lookupListOffset,
        int lookupIndex,
        Dictionary<GlyphSequence, uint> result)
    {
        if (!TryReadLookup(
                gsub,
                lookupListOffset,
                lookupIndex,
                out int lookupType,
                out int[] subtables))
        {
            return;
        }
        foreach (int subtable in subtables)
        {
            int effectiveType = lookupType;
            int effectiveOffset = subtable;
            if (!ResolveExtension(
                    gsub,
                    ref effectiveType,
                    ref effectiveOffset) ||
                effectiveType != 4 ||
                UInt16(gsub, effectiveOffset) != 1)
            {
                continue;
            }

            int coverageOffset =
                checked(effectiveOffset + UInt16(gsub, effectiveOffset + 2));
            uint[] coverage = ReadCoverage(gsub, coverageOffset);
            int setCount = UInt16(gsub, effectiveOffset + 4);
            if (setCount != coverage.Length ||
                effectiveOffset + 6L + setCount * 2L > gsub.Length)
            {
                continue;
            }
            for (int set = 0; set < setCount; set++)
            {
                int setOffset = checked(
                    effectiveOffset +
                    UInt16(gsub, effectiveOffset + 6 + set * 2));
                int ligatureCount = UInt16(gsub, setOffset);
                if (setOffset + 2L + ligatureCount * 2L > gsub.Length)
                    continue;
                for (int item = 0; item < ligatureCount; item++)
                {
                    int ligatureOffset = checked(
                        setOffset + UInt16(gsub, setOffset + 2 + item * 2));
                    uint ligatureGlyph = UInt16(gsub, ligatureOffset);
                    int componentCount = UInt16(gsub, ligatureOffset + 2);
                    if (componentCount < 2 ||
                        componentCount > 64 ||
                        ligatureOffset + 4L + (componentCount - 1) * 2L >
                        gsub.Length)
                    {
                        continue;
                    }
                    var sequence = new uint[componentCount];
                    sequence[0] = coverage[set];
                    for (int component = 1;
                         component < componentCount;
                         component++)
                    {
                        sequence[component] = UInt16(
                            gsub,
                            ligatureOffset + 4 + (component - 1) * 2);
                    }
                    result.TryAdd(
                        new GlyphSequence(sequence),
                        ligatureGlyph);
                    if (result.Count > MaximumSubstitutions)
                        throw new ArgumentOutOfRangeException(nameof(lookupIndex));
                }
            }
        }
    }

    private static bool TryReadLookup(
        ReadOnlySpan<byte> gsub,
        int lookupListOffset,
        int lookupIndex,
        out int lookupType,
        out int[] subtables)
    {
        lookupType = 0;
        subtables = Array.Empty<int>();
        int lookupCount = UInt16(gsub, lookupListOffset);
        if ((uint)lookupIndex >= (uint)lookupCount ||
            lookupCount > MaximumLookups ||
            lookupListOffset + 2L + lookupCount * 2L > gsub.Length)
        {
            return false;
        }
        int lookupOffset = checked(
            lookupListOffset +
            UInt16(gsub, lookupListOffset + 2 + lookupIndex * 2));
        lookupType = UInt16(gsub, lookupOffset);
        int subtableCount = UInt16(gsub, lookupOffset + 4);
        if (subtableCount > MaximumLookups ||
            lookupOffset + 6L + subtableCount * 2L > gsub.Length)
        {
            return false;
        }
        subtables = new int[subtableCount];
        for (int index = 0; index < subtableCount; index++)
        {
            subtables[index] = checked(
                lookupOffset + UInt16(gsub, lookupOffset + 6 + index * 2));
        }
        return true;
    }

    private static bool ResolveExtension(
        ReadOnlySpan<byte> gsub,
        ref int lookupType,
        ref int offset)
    {
        if (lookupType != 7)
            return true;
        if (UInt16(gsub, offset) != 1)
            return false;
        lookupType = UInt16(gsub, offset + 2);
        uint relative = UInt32(gsub, offset + 4);
        if (relative > int.MaxValue ||
            (ulong)offset + relative >= (ulong)gsub.Length)
        {
            return false;
        }
        offset = checked(offset + (int)relative);
        return true;
    }

    private static uint[] ReadCoverage(ReadOnlySpan<byte> table, int offset)
    {
        int format = UInt16(table, offset);
        if (format == 1)
        {
            int count = UInt16(table, offset + 2);
            if (count > MaximumSubstitutions ||
                offset + 4L + count * 2L > table.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            var result = new uint[count];
            for (int index = 0; index < count; index++)
                result[index] = UInt16(table, offset + 4 + index * 2);
            return result;
        }
        if (format != 2)
            return Array.Empty<uint>();
        int rangeCount = UInt16(table, offset + 2);
        if (rangeCount > MaximumSubstitutions ||
            offset + 4L + rangeCount * 6L > table.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        var glyphs = new List<uint>();
        for (int range = 0; range < rangeCount; range++)
        {
            int record = offset + 4 + range * 6;
            int start = UInt16(table, record);
            int end = UInt16(table, record + 2);
            int startIndex = UInt16(table, record + 4);
            if (end < start ||
                startIndex != glyphs.Count ||
                glyphs.Count + end - start + 1 > MaximumSubstitutions)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            for (int glyph = start; glyph <= end; glyph++)
                glyphs.Add((uint)glyph);
        }
        return glyphs.ToArray();
    }

    private static bool TryFindTable(
        ReadOnlySpan<byte> font,
        ReadOnlySpan<byte> tag,
        out ReadOnlySpan<byte> table)
    {
        table = default;
        if (font.Length < 12)
            return false;
        int count = UInt16(font, 4);
        if (count < 1 ||
            count > 4096 ||
            12L + count * 16L > font.Length)
        {
            return false;
        }
        for (int index = 0; index < count; index++)
        {
            int record = 12 + index * 16;
            if (!font.Slice(record, 4).SequenceEqual(tag))
                continue;
            uint offset = UInt32(font, record + 8);
            uint length = UInt32(font, record + 12);
            if (offset > int.MaxValue ||
                length > int.MaxValue ||
                (ulong)offset + length > (ulong)font.Length)
            {
                return false;
            }
            table = font.Slice((int)offset, (int)length);
            return true;
        }
        return false;
    }

    private static uint Tag(string value) =>
        ((uint)value[0] << 24) |
        ((uint)value[1] << 16) |
        ((uint)value[2] << 8) |
        value[3];

    private static ushort UInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));

    private static short Int16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(offset, 2));

    private static uint UInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));

    private readonly struct GlyphSequence : IEquatable<GlyphSequence>
    {
        private readonly uint[] _glyphs;

        public GlyphSequence(uint[] glyphs) => _glyphs = glyphs;

        public bool Equals(GlyphSequence other) =>
            _glyphs.AsSpan().SequenceEqual(other._glyphs);

        public override bool Equals(object? value) =>
            value is GlyphSequence other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (uint glyph in _glyphs)
                hash.Add(glyph);
            return hash.ToHashCode();
        }
    }
}
