using System.Buffers.Binary;
using GraphicsMatrix = global::Poppler.PdfMatrix;

namespace Poppler.Text;

/// <summary>
/// Bounded managed reader for the outline and horizontal-metrics tables of a
/// TrueType sfnt. CFF outlines deliberately remain outside this first raster
/// slice.
/// </summary>
internal sealed class PdfTrueTypeFont
{
    private const ushort OnCurve = 0x01;
    private const ushort XShort = 0x02;
    private const ushort YShort = 0x04;
    private const ushort Repeat = 0x08;
    private const ushort XSame = 0x10;
    private const ushort YSame = 0x20;
    private const ushort ArgsAreWords = 0x0001;
    private const ushort ArgsAreXyValues = 0x0002;
    private const ushort WeHaveScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveXyScale = 0x0040;
    private const ushort WeHaveTwoByTwo = 0x0080;

    private readonly byte[] _data;
    private readonly Table _glyf;
    private readonly int[] _glyphOffsets;
    private readonly ushort[] _advanceWidths;
    private readonly int _unitsPerEm;

    private PdfTrueTypeFont(
        byte[] data,
        Table glyf,
        int[] glyphOffsets,
        ushort[] advanceWidths,
        int unitsPerEm,
        double ascent,
        double descent)
    {
        _data = data;
        _glyf = glyf;
        _glyphOffsets = glyphOffsets;
        _advanceWidths = advanceWidths;
        _unitsPerEm = unitsPerEm;
        Ascent = ascent;
        Descent = descent;
    }

    public double Ascent { get; }
    public double Descent { get; }

    public static PdfTrueTypeFont? TryParse(byte[] data)
    {
        try
        {
            if (data.Length < 12)
                return null;
            ReadOnlySpan<byte> bytes = data;
            uint signature = UInt32(bytes, 0);
            if (signature is not (0x00010000 or 0x74727565))
                return null;
            int tableCount = UInt16(bytes, 4);
            if (tableCount < 1 || tableCount > 4096 ||
                checked(12 + tableCount * 16) > bytes.Length)
            {
                return null;
            }

            var tables = new Dictionary<uint, Table>();
            for (int index = 0; index < tableCount; index++)
            {
                int offset = 12 + index * 16;
                uint tag = UInt32(bytes, offset);
                uint tableOffset = UInt32(bytes, offset + 8);
                uint tableLength = UInt32(bytes, offset + 12);
                if (tableOffset <= int.MaxValue &&
                    tableLength <= int.MaxValue &&
                    (ulong)tableOffset + tableLength <= (ulong)bytes.Length)
                {
                    tables[tag] = new Table((int)tableOffset, (int)tableLength);
                }
            }

            if (!tables.TryGetValue(Tag("head"), out Table head) ||
                !tables.TryGetValue(Tag("maxp"), out Table maxp) ||
                !tables.TryGetValue(Tag("loca"), out Table loca) ||
                !tables.TryGetValue(Tag("glyf"), out Table glyf) ||
                !tables.TryGetValue(Tag("hhea"), out Table hhea) ||
                !tables.TryGetValue(Tag("hmtx"), out Table hmtx) ||
                head.Length < 54 ||
                maxp.Length < 6 ||
                hhea.Length < 36)
            {
                return null;
            }

            int unitsPerEm = UInt16(bytes, head.Offset + 18);
            int glyphCount = UInt16(bytes, maxp.Offset + 4);
            int locationFormat = Int16(bytes, head.Offset + 50);
            int metricCount = UInt16(bytes, hhea.Offset + 34);
            if (unitsPerEm is < 16 or > 16384 ||
                glyphCount < 1 ||
                metricCount < 1 ||
                metricCount > glyphCount)
            {
                return null;
            }

            int[] offsets = ReadLocations(
                bytes,
                loca,
                glyphCount,
                locationFormat,
                glyf.Length);
            ushort[] widths = ReadWidths(bytes, hmtx, glyphCount, metricCount);
            double ascent = Int16(bytes, hhea.Offset + 4) / (double)unitsPerEm;
            double descent = Int16(bytes, hhea.Offset + 6) / (double)unitsPerEm;
            return new PdfTrueTypeFont(
                data,
                glyf,
                offsets,
                widths,
                unitsPerEm,
                ascent,
                descent);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or
            OverflowException or
            IndexOutOfRangeException)
        {
            return null;
        }
    }

    public bool TryGetGlyph(
        uint glyphId,
        out PdfGraphicsPath path,
        out double advance)
    {
        path = new PdfGraphicsPath(Array.Empty<PdfPathSegment>());
        advance = 0;
        if (glyphId >= _glyphOffsets.Length - 1)
            return false;
        advance = _advanceWidths[(int)glyphId] / (double)_unitsPerEm;
        var segments = new List<PdfPathSegment>();
        var active = new HashSet<uint>();
        if (!ReadGlyph(glyphId, GraphicsMatrix.Identity, segments, active, depth: 0))
            return false;
        path = new PdfGraphicsPath(segments);
        return !path.IsEmpty;
    }

    private bool ReadGlyph(
        uint glyphId,
        GraphicsMatrix transform,
        List<PdfPathSegment> output,
        HashSet<uint> active,
        int depth)
    {
        if (depth > 16 ||
            glyphId >= _glyphOffsets.Length - 1 ||
            !active.Add(glyphId))
        {
            return false;
        }

        try
        {
            int relativeStart = _glyphOffsets[(int)glyphId];
            int relativeEnd = _glyphOffsets[(int)glyphId + 1];
            if (relativeStart == relativeEnd)
                return true;
            if (relativeStart < 0 ||
                relativeEnd < relativeStart ||
                relativeEnd > _glyf.Length)
            {
                return false;
            }

            int start = checked(_glyf.Offset + relativeStart);
            int end = checked(_glyf.Offset + relativeEnd);
            if (end - start < 10)
                return false;
            ReadOnlySpan<byte> bytes = _data;
            short contourCount = Int16(bytes, start);
            return contourCount >= 0
                ? ReadSimple(bytes, start, end, contourCount, transform, output)
                : ReadComposite(bytes, start, end, transform, output, active, depth);
        }
        finally
        {
            active.Remove(glyphId);
        }
    }

    private bool ReadSimple(
        ReadOnlySpan<byte> bytes,
        int start,
        int end,
        int contourCount,
        GraphicsMatrix transform,
        List<PdfPathSegment> output)
    {
        if (contourCount == 0)
            return true;
        int position = start + 10;
        if (position + contourCount * 2 > end)
            return false;
        var contourEnds = new ushort[contourCount];
        for (int index = 0; index < contourCount; index++)
        {
            contourEnds[index] = UInt16(bytes, position);
            position += 2;
        }

        int pointCount = contourEnds[^1] + 1;
        if (pointCount < 1 || pointCount > 1_000_000 || position + 2 > end)
            return false;
        int instructionLength = UInt16(bytes, position);
        position = checked(position + 2 + instructionLength);
        if (position > end)
            return false;

        byte[] flags = ReadFlags(bytes, ref position, end, pointCount);
        short[] x = ReadCoordinates(bytes, ref position, end, flags, xAxis: true);
        short[] y = ReadCoordinates(bytes, ref position, end, flags, xAxis: false);
        var points = new TrueTypePoint[pointCount];
        for (int index = 0; index < pointCount; index++)
        {
            PdfPoint normalized = transform.Transform(
                x[index] / (double)_unitsPerEm,
                y[index] / (double)_unitsPerEm);
            points[index] = new TrueTypePoint(
                normalized,
                (flags[index] & OnCurve) != 0);
        }

        int first = 0;
        foreach (int last in contourEnds)
        {
            if (last < first || last >= points.Length)
                return false;
            AppendContour(points.AsSpan(first, last - first + 1), output);
            first = last + 1;
        }

        return true;
    }

    private bool ReadComposite(
        ReadOnlySpan<byte> bytes,
        int start,
        int end,
        GraphicsMatrix parent,
        List<PdfPathSegment> output,
        HashSet<uint> active,
        int depth)
    {
        int position = start + 10;
        ushort flags;
        int components = 0;
        do
        {
            if (position + 4 > end || components++ > 4096)
                return false;
            flags = UInt16(bytes, position);
            uint glyph = UInt16(bytes, position + 2);
            position += 4;
            int argument1;
            int argument2;
            if ((flags & ArgsAreWords) != 0)
            {
                if (position + 4 > end)
                    return false;
                argument1 = Int16(bytes, position);
                argument2 = Int16(bytes, position + 2);
                position += 4;
            }
            else
            {
                if (position + 2 > end)
                    return false;
                argument1 = unchecked((sbyte)bytes[position]);
                argument2 = unchecked((sbyte)bytes[position + 1]);
                position += 2;
            }

            if ((flags & ArgsAreXyValues) == 0)
                return false;
            double a = 1;
            double b = 0;
            double c = 0;
            double d = 1;
            if ((flags & WeHaveScale) != 0)
            {
                if (position + 2 > end)
                    return false;
                a = d = F2Dot14(bytes, position);
                position += 2;
            }
            else if ((flags & WeHaveXyScale) != 0)
            {
                if (position + 4 > end)
                    return false;
                a = F2Dot14(bytes, position);
                d = F2Dot14(bytes, position + 2);
                position += 4;
            }
            else if ((flags & WeHaveTwoByTwo) != 0)
            {
                if (position + 8 > end)
                    return false;
                a = F2Dot14(bytes, position);
                b = F2Dot14(bytes, position + 2);
                c = F2Dot14(bytes, position + 4);
                d = F2Dot14(bytes, position + 6);
                position += 8;
            }

            var component = new GraphicsMatrix(
                a,
                b,
                c,
                d,
                argument1 / (double)_unitsPerEm,
                argument2 / (double)_unitsPerEm);
            if (!ReadGlyph(
                    glyph,
                    component.Multiply(parent),
                    output,
                    active,
                    depth + 1))
            {
                return false;
            }
        }
        while ((flags & MoreComponents) != 0);

        return true;
    }

    private static void AppendContour(
        ReadOnlySpan<TrueTypePoint> contour,
        List<PdfPathSegment> output)
    {
        if (contour.IsEmpty)
            return;
        TrueTypePoint first = contour[0];
        TrueTypePoint last = contour[^1];
        PdfPoint start = first.OnCurve
            ? first.Point
            : last.OnCurve
                ? last.Point
                : Midpoint(last.Point, first.Point);
        output.Add(new PdfMoveTo(start));
        PdfPoint current = start;
        int index = first.OnCurve ? 1 : 0;
        int consumed = 0;
        while (consumed < contour.Length)
        {
            TrueTypePoint point = contour[index % contour.Length];
            if (point.OnCurve)
            {
                if (DistanceSquared(current, point.Point) > 1e-20)
                    output.Add(new PdfLineTo(point.Point));
                current = point.Point;
                index++;
                consumed++;
                continue;
            }

            TrueTypePoint following = contour[(index + 1) % contour.Length];
            PdfPoint end = following.OnCurve
                ? following.Point
                : Midpoint(point.Point, following.Point);
            output.Add(Quadratic(current, point.Point, end));
            current = end;
            if (following.OnCurve)
            {
                index += 2;
                consumed += 2;
            }
            else
            {
                index++;
                consumed++;
            }
        }

        output.Add(new PdfClosePath());
    }

    private static PdfCubicBezierTo Quadratic(
        PdfPoint start,
        PdfPoint control,
        PdfPoint end) =>
        new(
            new PdfPoint(
                start.X + (control.X - start.X) * 2 / 3,
                start.Y + (control.Y - start.Y) * 2 / 3),
            new PdfPoint(
                end.X + (control.X - end.X) * 2 / 3,
                end.Y + (control.Y - end.Y) * 2 / 3),
            end);

    private static byte[] ReadFlags(
        ReadOnlySpan<byte> bytes,
        ref int position,
        int end,
        int pointCount)
    {
        var result = new byte[pointCount];
        int index = 0;
        while (index < pointCount)
        {
            if (position >= end)
                throw new ArgumentOutOfRangeException(nameof(position));
            byte flag = bytes[position++];
            result[index++] = flag;
            if ((flag & Repeat) == 0)
                continue;
            if (position >= end)
                throw new ArgumentOutOfRangeException(nameof(position));
            int repeat = bytes[position++];
            if (index + repeat > pointCount)
                throw new ArgumentOutOfRangeException(nameof(pointCount));
            for (int count = 0; count < repeat; count++)
                result[index++] = flag;
        }

        return result;
    }

    private static short[] ReadCoordinates(
        ReadOnlySpan<byte> bytes,
        ref int position,
        int end,
        IReadOnlyList<byte> flags,
        bool xAxis)
    {
        var result = new short[flags.Count];
        int value = 0;
        ushort shortFlag = xAxis ? XShort : YShort;
        ushort sameFlag = xAxis ? XSame : YSame;
        for (int index = 0; index < flags.Count; index++)
        {
            byte flag = flags[index];
            if ((flag & shortFlag) != 0)
            {
                if (position >= end)
                    throw new ArgumentOutOfRangeException(nameof(position));
                int delta = bytes[position++];
                value += (flag & sameFlag) != 0 ? delta : -delta;
            }
            else if ((flag & sameFlag) == 0)
            {
                if (position + 2 > end)
                    throw new ArgumentOutOfRangeException(nameof(position));
                value += Int16(bytes, position);
                position += 2;
            }

            if (value is < short.MinValue or > short.MaxValue)
                throw new OverflowException("TrueType coordinate overflow.");
            result[index] = (short)value;
        }

        return result;
    }

    private static int[] ReadLocations(
        ReadOnlySpan<byte> bytes,
        Table loca,
        int glyphCount,
        int format,
        int glyfLength)
    {
        var result = new int[checked(glyphCount + 1)];
        int itemSize = format == 0 ? 2 : 4;
        if (format is not (0 or 1) ||
            checked(result.Length * itemSize) > loca.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(loca));
        }

        for (int index = 0; index < result.Length; index++)
        {
            int offset = loca.Offset + index * itemSize;
            result[index] = format == 0
                ? checked(UInt16(bytes, offset) * 2)
                : checked((int)UInt32(bytes, offset));
            if (result[index] < 0 ||
                result[index] > glyfLength ||
                index > 0 && result[index] < result[index - 1])
            {
                throw new ArgumentOutOfRangeException(nameof(loca));
            }
        }

        return result;
    }

    private static ushort[] ReadWidths(
        ReadOnlySpan<byte> bytes,
        Table hmtx,
        int glyphCount,
        int metricCount)
    {
        if (checked(metricCount * 4 + (glyphCount - metricCount) * 2) > hmtx.Length)
            throw new ArgumentOutOfRangeException(nameof(hmtx));
        var result = new ushort[glyphCount];
        ushort last = 0;
        for (int glyph = 0; glyph < glyphCount; glyph++)
        {
            if (glyph < metricCount)
            {
                last = UInt16(bytes, hmtx.Offset + glyph * 4);
            }

            result[glyph] = last;
        }

        return result;
    }

    private static double F2Dot14(ReadOnlySpan<byte> bytes, int offset) =>
        Int16(bytes, offset) / 16384.0;

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

    private static PdfPoint Midpoint(PdfPoint first, PdfPoint second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static double DistanceSquared(PdfPoint first, PdfPoint second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        return x * x + y * y;
    }

    private readonly record struct Table(int Offset, int Length);
    private readonly record struct TrueTypePoint(PdfPoint Point, bool OnCurve);
}
