using System.Buffers.Binary;
using System.Text;

namespace Poppler.Rendering;

/// <summary>
/// Builds a deliberately small, deterministic TrueType font from the managed
/// outlines already decoded by Poppler.Net. The generated cmap uses a private
/// supplementary range; the HTML layer keeps the source Unicode in a separate
/// selectable span, so visual glyph identity and copy/search text cannot
/// conflict when a PDF subset reuses Unicode values.
/// </summary>
internal static class ManagedTrueTypeFontBuilder
{
    private const int UnitsPerEm = 1000;
    private const int PrivateUseStart = 0xF0000;
    private const double QuadraticTolerance = 0.0005;
    private const int MaximumCurveDepth = 10;

    public static ManagedFontResult Build(
        string familyName,
        IReadOnlyList<ManagedFontGlyph> glyphs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        ArgumentNullException.ThrowIfNull(glyphs);
        if (glyphs.Count is < 1 or > 65_534)
            throw new ArgumentOutOfRangeException(nameof(glyphs));

        var outlines = new List<TrueTypeGlyph>(glyphs.Count + 1)
        {
            TrueTypeGlyph.Empty
        };
        outlines.AddRange(glyphs.Select(CreateGlyph));

        byte[] glyf = BuildGlyf(outlines, out uint[] offsets);
        short globalXMin = outlines.Min(value => value.XMin);
        short globalYMin = outlines.Min(value => value.YMin);
        short globalXMax = outlines.Max(value => value.XMax);
        short globalYMax = outlines.Max(value => value.YMax);
        ushort[] advances = [0, .. glyphs.Select(value => Advance(value.Advance))];
        short[] bearings = outlines.Select(value => value.XMin).ToArray();

        var tables = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["OS/2"] = BuildOs2(advances),
            ["cmap"] = BuildCmap(glyphs.Count),
            ["glyf"] = glyf,
            ["head"] = BuildHead(globalXMin, globalYMin, globalXMax, globalYMax),
            ["hhea"] = BuildHhea(advances, outlines),
            ["hmtx"] = BuildHmtx(advances, bearings),
            ["loca"] = BuildLoca(offsets),
            ["maxp"] = BuildMaxp(outlines),
            ["name"] = BuildName(familyName),
            ["post"] = BuildPost()
        };
        byte[] font = BuildSfnt(tables);
        var mapping = new Dictionary<PdfFontGlyphKey, Rune>();
        for (int index = 0; index < glyphs.Count; index++)
        {
            mapping[glyphs[index].Key] = new Rune(PrivateUseStart + index);
        }
        return new ManagedFontResult(font, mapping);
    }

    private static TrueTypeGlyph CreateGlyph(ManagedFontGlyph glyph)
    {
        var contours = new List<List<TrueTypePoint>>();
        List<TrueTypePoint>? current = null;
        PdfPoint currentPoint = default;
        PdfPoint startPoint = default;
        bool hasCurrent = false;

        void FinishContour()
        {
            if (current is { Count: > 0 })
            {
                RemoveDuplicateEnd(current);
                if (current.Count > 0)
                    contours.Add(current);
            }
            current = null;
            hasCurrent = false;
        }

        foreach (PdfPathSegment segment in glyph.Path.Segments)
        {
            switch (segment)
            {
                case PdfMoveTo move:
                    FinishContour();
                    current = [];
                    currentPoint = startPoint = move.Point;
                    AddPoint(current, move.Point, onCurve: true);
                    hasCurrent = true;
                    break;
                case PdfLineTo line when hasCurrent && current is not null:
                    AddPoint(current, line.Point, onCurve: true);
                    currentPoint = line.Point;
                    break;
                case PdfCubicBezierTo curve when hasCurrent && current is not null:
                    AppendCubic(
                        current,
                        currentPoint,
                        curve.Control1,
                        curve.Control2,
                        curve.End,
                        depth: 0);
                    currentPoint = curve.End;
                    break;
                case PdfClosePath when hasCurrent:
                    currentPoint = startPoint;
                    FinishContour();
                    break;
            }
        }
        FinishContour();

        return TrueTypeGlyph.Create(contours);
    }

    private static void AppendCubic(
        List<TrueTypePoint> output,
        PdfPoint start,
        PdfPoint control1,
        PdfPoint control2,
        PdfPoint end,
        int depth)
    {
        PdfPoint firstControl = new(
            start.X + 1.5 * (control1.X - start.X),
            start.Y + 1.5 * (control1.Y - start.Y));
        PdfPoint secondControl = new(
            end.X + 1.5 * (control2.X - end.X),
            end.Y + 1.5 * (control2.Y - end.Y));
        if (depth >= MaximumCurveDepth ||
            Distance(firstControl, secondControl) <= QuadraticTolerance)
        {
            AddPoint(output, Midpoint(firstControl, secondControl), onCurve: false);
            AddPoint(output, end, onCurve: true);
            return;
        }

        PdfPoint first = Midpoint(start, control1);
        PdfPoint middle1 = Midpoint(control1, control2);
        PdfPoint last = Midpoint(control2, end);
        PdfPoint middle2 = Midpoint(first, middle1);
        PdfPoint middle3 = Midpoint(middle1, last);
        PdfPoint center = Midpoint(middle2, middle3);
        AppendCubic(output, start, first, middle2, center, depth + 1);
        AppendCubic(output, center, middle3, last, end, depth + 1);
    }

    private static void AddPoint(
        List<TrueTypePoint> output,
        PdfPoint point,
        bool onCurve)
    {
        short x = Coordinate(point.X);
        short y = Coordinate(point.Y);
        if (output.Count > 0 &&
            output[^1].X == x &&
            output[^1].Y == y &&
            output[^1].OnCurve == onCurve)
        {
            return;
        }
        output.Add(new TrueTypePoint(x, y, onCurve));
    }

    private static void RemoveDuplicateEnd(List<TrueTypePoint> contour)
    {
        if (contour.Count > 1 &&
            contour[0].X == contour[^1].X &&
            contour[0].Y == contour[^1].Y)
        {
            contour.RemoveAt(contour.Count - 1);
        }
    }

    private static byte[] BuildGlyf(
        IReadOnlyList<TrueTypeGlyph> glyphs,
        out uint[] offsets)
    {
        using var writer = new BigEndianWriter();
        offsets = new uint[glyphs.Count + 1];
        for (int index = 0; index < glyphs.Count; index++)
        {
            offsets[index] = checked((uint)writer.Length);
            WriteGlyph(writer, glyphs[index]);
            writer.Pad(2);
        }
        offsets[^1] = checked((uint)writer.Length);
        return writer.ToArray();
    }

    private static void WriteGlyph(BigEndianWriter writer, TrueTypeGlyph glyph)
    {
        if (glyph.Contours.Count == 0)
            return;
        writer.Int16(checked((short)glyph.Contours.Count));
        writer.Int16(glyph.XMin);
        writer.Int16(glyph.YMin);
        writer.Int16(glyph.XMax);
        writer.Int16(glyph.YMax);

        int pointCount = 0;
        foreach (IReadOnlyList<TrueTypePoint> contour in glyph.Contours)
        {
            pointCount = checked(pointCount + contour.Count);
            writer.UInt16(checked((ushort)(pointCount - 1)));
        }
        writer.UInt16(0);

        TrueTypePoint[] points = glyph.Contours.SelectMany(value => value).ToArray();
        foreach (TrueTypePoint point in points)
            writer.Byte(point.OnCurve ? (byte)0x01 : (byte)0x00);

        short previous = 0;
        foreach (TrueTypePoint point in points)
        {
            writer.Int16(checked((short)(point.X - previous)));
            previous = point.X;
        }
        previous = 0;
        foreach (TrueTypePoint point in points)
        {
            writer.Int16(checked((short)(point.Y - previous)));
            previous = point.Y;
        }
    }

    private static byte[] BuildCmap(int glyphCount)
    {
        using var writer = new BigEndianWriter();
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(3);
        writer.UInt16(10);
        writer.UInt32(12);
        writer.UInt16(12);
        writer.UInt16(0);
        writer.UInt32(28);
        writer.UInt32(0);
        writer.UInt32(1);
        writer.UInt32(PrivateUseStart);
        writer.UInt32(checked((uint)(PrivateUseStart + glyphCount - 1)));
        writer.UInt32(1);
        return writer.ToArray();
    }

    private static byte[] BuildHead(
        short xMin,
        short yMin,
        short xMax,
        short yMax)
    {
        using var writer = new BigEndianWriter();
        writer.UInt32(0x00010000);
        writer.UInt32(0x00010000);
        writer.UInt32(0);
        writer.UInt32(0x5F0F3CF5);
        writer.UInt16(0x000B);
        writer.UInt16(UnitsPerEm);
        writer.UInt64(0);
        writer.UInt64(0);
        writer.Int16(xMin);
        writer.Int16(yMin);
        writer.Int16(xMax);
        writer.Int16(yMax);
        writer.UInt16(0);
        writer.UInt16(8);
        writer.Int16(2);
        writer.Int16(1);
        writer.Int16(0);
        return writer.ToArray();
    }

    private static byte[] BuildHhea(
        IReadOnlyList<ushort> advances,
        IReadOnlyList<TrueTypeGlyph> glyphs)
    {
        using var writer = new BigEndianWriter();
        writer.UInt32(0x00010000);
        writer.Int16(800);
        writer.Int16(-200);
        writer.Int16(0);
        writer.UInt16(advances.Max());
        writer.Int16(glyphs.Min(value => value.XMin));
        writer.Int16(checked((short)Math.Max(
            short.MinValue,
            advances.Select((advance, index) =>
                (int)advance - glyphs[index].XMax).Min())));
        writer.Int16(checked((short)Math.Min(
            short.MaxValue,
            glyphs.Max(value => (int)value.XMax))));
        writer.Int16(1);
        writer.Int16(0);
        writer.Int16(0);
        for (int index = 0; index < 4; index++)
            writer.Int16(0);
        writer.Int16(0);
        writer.UInt16(checked((ushort)advances.Count));
        return writer.ToArray();
    }

    private static byte[] BuildHmtx(
        IReadOnlyList<ushort> advances,
        IReadOnlyList<short> bearings)
    {
        using var writer = new BigEndianWriter();
        for (int index = 0; index < advances.Count; index++)
        {
            writer.UInt16(advances[index]);
            writer.Int16(bearings[index]);
        }
        return writer.ToArray();
    }

    private static byte[] BuildLoca(IEnumerable<uint> offsets)
    {
        using var writer = new BigEndianWriter();
        foreach (uint offset in offsets)
            writer.UInt32(offset);
        return writer.ToArray();
    }

    private static byte[] BuildMaxp(IReadOnlyList<TrueTypeGlyph> glyphs)
    {
        using var writer = new BigEndianWriter();
        writer.UInt32(0x00010000);
        writer.UInt16(checked((ushort)glyphs.Count));
        writer.UInt16(checked((ushort)glyphs.Max(value => value.PointCount)));
        writer.UInt16(checked((ushort)glyphs.Max(value => value.Contours.Count)));
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(2);
        for (int index = 0; index < 8; index++)
            writer.UInt16(0);
        return writer.ToArray();
    }

    private static byte[] BuildName(string familyName)
    {
        string displayName = new string(familyName
            .Where(character => !char.IsControl(character))
            .Take(255)
            .ToArray());
        if (displayName.Length == 0)
            displayName = "Poppler.Net Managed Font";
        string postScript = new string(displayName
            .Where(char.IsLetterOrDigit)
            .Take(63)
            .ToArray());
        if (postScript.Length == 0)
            postScript = "PopplerNetManagedFont";
        var names = new (ushort Id, string Value)[]
        {
            (1, displayName),
            (2, "Regular"),
            (4, displayName),
            (6, postScript)
        };
        byte[][] encoded = names
            .Select(value => Encoding.BigEndianUnicode.GetBytes(value.Value))
            .ToArray();
        using var writer = new BigEndianWriter();
        writer.UInt16(0);
        writer.UInt16(checked((ushort)names.Length));
        writer.UInt16(checked((ushort)(6 + names.Length * 12)));
        int offset = 0;
        for (int index = 0; index < names.Length; index++)
        {
            writer.UInt16(3);
            writer.UInt16(1);
            writer.UInt16(0x0409);
            writer.UInt16(names[index].Id);
            writer.UInt16(checked((ushort)encoded[index].Length));
            writer.UInt16(checked((ushort)offset));
            offset += encoded[index].Length;
        }
        foreach (byte[] value in encoded)
            writer.Bytes(value);
        return writer.ToArray();
    }

    private static byte[] BuildOs2(IReadOnlyList<ushort> advances)
    {
        using var writer = new BigEndianWriter();
        writer.UInt16(0);
        writer.Int16(checked((short)Math.Clamp(
            (int)Math.Round(advances.Average(value => (double)value)),
            short.MinValue,
            short.MaxValue)));
        writer.UInt16(400);
        writer.UInt16(5);
        writer.UInt16(0);
        for (int index = 0; index < 10; index++)
            writer.Int16(0);
        writer.Int16(0);
        writer.Bytes(new byte[10]);
        writer.UInt32(0);
        writer.UInt32(1u << 28);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.Bytes("PPLR"u8);
        writer.UInt16(0x0040);
        writer.UInt16(0xFFFF);
        writer.UInt16(0xFFFF);
        writer.Int16(800);
        writer.Int16(-200);
        writer.Int16(0);
        writer.UInt16(800);
        writer.UInt16(200);
        return writer.ToArray();
    }

    private static byte[] BuildPost()
    {
        using var writer = new BigEndianWriter();
        writer.UInt32(0x00030000);
        writer.UInt32(0);
        writer.Int16(-75);
        writer.Int16(50);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt32(0);
        return writer.ToArray();
    }

    private static byte[] BuildSfnt(IReadOnlyDictionary<string, byte[]> tables)
    {
        string[] tags = tables.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        ushort count = checked((ushort)tags.Length);
        ushort power = 1;
        ushort selector = 0;
        while (power * 2 <= count)
        {
            power *= 2;
            selector++;
        }
        ushort searchRange = checked((ushort)(power * 16));
        ushort rangeShift = checked((ushort)(count * 16 - searchRange));

        int offset = 12 + count * 16;
        var records = new List<TableRecord>(count);
        foreach (string tag in tags)
        {
            byte[] data = tables[tag];
            offset = Align4(offset);
            records.Add(new TableRecord(tag, Checksum(data), offset, data.Length));
            offset = checked(offset + data.Length);
        }

        byte[] output = new byte[Align4(offset)];
        BinaryPrimitives.WriteUInt32BigEndian(output, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), count);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), selector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), rangeShift);
        for (int index = 0; index < records.Count; index++)
        {
            TableRecord record = records[index];
            int directory = 12 + index * 16;
            Encoding.ASCII.GetBytes(record.Tag, output.AsSpan(directory, 4));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(directory + 4), record.Checksum);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(directory + 8), checked((uint)record.Offset));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(directory + 12), checked((uint)record.Length));
            tables[record.Tag].CopyTo(output, record.Offset);
        }

        TableRecord head = records.Single(value => value.Tag == "head");
        uint adjustment = unchecked(0xB1B0AFBAu - Checksum(output));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(head.Offset + 8), adjustment);
        return output;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        Span<byte> word = stackalloc byte[4];
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            word.Clear();
            data.Slice(offset, Math.Min(4, data.Length - offset)).CopyTo(word);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(word));
        }
        return sum;
    }

    private static ushort Advance(double value) => checked((ushort)Math.Clamp(
        (int)Math.Round(Math.Max(0, value) * UnitsPerEm),
        0,
        ushort.MaxValue));

    private static short Coordinate(double value) => checked((short)Math.Clamp(
        (int)Math.Round(value * UnitsPerEm),
        short.MinValue,
        short.MaxValue));

    private static PdfPoint Midpoint(PdfPoint first, PdfPoint second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static double Distance(PdfPoint first, PdfPoint second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static int Align4(int value) => checked((value + 3) & ~3);

    private sealed record TrueTypeGlyph(
        IReadOnlyList<IReadOnlyList<TrueTypePoint>> Contours,
        short XMin,
        short YMin,
        short XMax,
        short YMax,
        int PointCount)
    {
        public static TrueTypeGlyph Empty { get; } =
            new(Array.Empty<IReadOnlyList<TrueTypePoint>>(), 0, 0, 0, 0, 0);

        public static TrueTypeGlyph Create(IReadOnlyList<List<TrueTypePoint>> contours)
        {
            TrueTypePoint[] points = contours.SelectMany(value => value).ToArray();
            if (points.Length == 0)
                return Empty;
            return new TrueTypeGlyph(
                contours.Select(value => (IReadOnlyList<TrueTypePoint>)value.ToArray()).ToArray(),
                points.Min(value => value.X),
                points.Min(value => value.Y),
                points.Max(value => value.X),
                points.Max(value => value.Y),
                points.Length);
        }
    }

    private readonly record struct TrueTypePoint(short X, short Y, bool OnCurve);
    private readonly record struct TableRecord(string Tag, uint Checksum, int Offset, int Length);

    private sealed class BigEndianWriter : IDisposable
    {
        private readonly MemoryStream _stream = new();

        public long Length => _stream.Length;
        public void Byte(byte value) => _stream.WriteByte(value);
        public void Bytes(ReadOnlySpan<byte> value) => _stream.Write(value);
        public void UInt16(int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)value));
            _stream.Write(bytes);
        }
        public void Int16(int value) => UInt16(unchecked((ushort)checked((short)value)));
        public void UInt32(long value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
            _stream.Write(bytes);
        }
        public void UInt64(ulong value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            _stream.Write(bytes);
        }
        public void Pad(int alignment)
        {
            while (_stream.Length % alignment != 0)
                _stream.WriteByte(0);
        }
        public byte[] ToArray() => _stream.ToArray();
        public void Dispose() => _stream.Dispose();
    }
}

internal readonly record struct PdfFontGlyphKey(
    uint CharacterCode,
    uint Cid,
    string Text)
{
    public static PdfFontGlyphKey From(Text.PdfDecodedGlyph glyph) =>
        new(glyph.CharacterCode, glyph.Cid, glyph.Text);
}

internal sealed record ManagedFontGlyph(
    PdfFontGlyphKey Key,
    PdfGraphicsPath Path,
    double Advance);

internal sealed record ManagedFontResult(
    byte[] Data,
    IReadOnlyDictionary<PdfFontGlyphKey, Rune> Mapping);
