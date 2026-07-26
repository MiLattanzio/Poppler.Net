using System.Buffers.Binary;
using System.Text;

namespace Poppler.Color;

/// <summary>
/// Managed ICC matrix/shaper reader. LUT-based profiles intentionally fall
/// back to the PDF /Alternate color space in this release.
/// </summary>
internal sealed class PdfIccProfile
{
    private readonly int _components;
    private readonly double[][] _curves;
    private readonly double[][]? _matrixColumns;

    private PdfIccProfile(
        int components,
        string? description,
        double[][] curves,
        double[][]? matrixColumns)
    {
        _components = components;
        Description = description;
        _curves = curves;
        _matrixColumns = matrixColumns;
    }

    public string? Description { get; }

    public static PdfIccProfile? TryParse(ReadOnlySpan<byte> data, int components)
    {
        if (data.Length < 132 ||
            !data.Slice(36, 4).SequenceEqual("acsp"u8) ||
            components is not (1 or 3))
        {
            return null;
        }

        int declaredSize = ReadInt32(data, 0);
        if (declaredSize < 132 || declaredSize > data.Length)
            return null;
        int tagCount = ReadInt32(data, 128);
        if (tagCount is < 0 or > 4096 || 132L + tagCount * 12L > declaredSize)
            return null;

        var tags = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        byte[] owned = data[..declaredSize].ToArray();
        ReadOnlySpan<byte> profile = owned;
        for (int index = 0; index < tagCount; index++)
        {
            int position = 132 + index * 12;
            string signature = Encoding.ASCII.GetString(profile.Slice(position, 4));
            int offset = ReadInt32(profile, position + 4);
            int length = ReadInt32(profile, position + 8);
            if (offset < 0 || length < 0 || offset > declaredSize - length)
                return null;
            tags[signature] = owned.AsMemory(offset, length);
        }

        string? description = ReadDescription(tags);
        if (components == 1)
        {
            if (!TryReadCurve(tags, "kTRC", out double[]? grayCurve))
                return null;
            return new PdfIccProfile(1, description, new[] { grayCurve! }, null);
        }

        if (!TryReadCurve(tags, "rTRC", out double[]? redCurve) ||
            !TryReadCurve(tags, "gTRC", out double[]? greenCurve) ||
            !TryReadCurve(tags, "bTRC", out double[]? blueCurve) ||
            !TryReadXyz(tags, "rXYZ", out double[]? redColumn) ||
            !TryReadXyz(tags, "gXYZ", out double[]? greenColumn) ||
            !TryReadXyz(tags, "bXYZ", out double[]? blueColumn))
        {
            return null;
        }

        return new PdfIccProfile(
            3,
            description,
            new[] { redCurve!, greenCurve!, blueCurve! },
            new[] { redColumn!, greenColumn!, blueColumn! });
    }

    public PdfColor Convert(ReadOnlySpan<double> components)
    {
        if (_components == 1)
        {
            double value = EvaluateCurve(_curves[0], components[0]);
            return PdfColorMath.XyzToColor(
                value * PdfColorMath.D50[0],
                value,
                value * PdfColorMath.D50[2],
                PdfColorMath.D50);
        }

        double red = EvaluateCurve(_curves[0], components[0]);
        double green = EvaluateCurve(_curves[1], components[1]);
        double blue = EvaluateCurve(_curves[2], components[2]);
        double x = _matrixColumns![0][0] * red +
                   _matrixColumns[1][0] * green +
                   _matrixColumns[2][0] * blue;
        double y = _matrixColumns[0][1] * red +
                   _matrixColumns[1][1] * green +
                   _matrixColumns[2][1] * blue;
        double z = _matrixColumns[0][2] * red +
                   _matrixColumns[1][2] * green +
                   _matrixColumns[2][2] * blue;
        return PdfColorMath.XyzToColor(x, y, z, PdfColorMath.D50);
    }

    private static bool TryReadXyz(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> tags,
        string name,
        out double[]? result)
    {
        result = null;
        if (!tags.TryGetValue(name, out ReadOnlyMemory<byte> memory))
            return false;
        ReadOnlySpan<byte> data = memory.Span;
        if (data.Length < 20 || !data[..4].SequenceEqual("XYZ "u8))
            return false;
        result = new[]
        {
            ReadS15Fixed16(data, 8),
            ReadS15Fixed16(data, 12),
            ReadS15Fixed16(data, 16)
        };
        return result.All(double.IsFinite);
    }

    private static bool TryReadCurve(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> tags,
        string name,
        out double[]? result)
    {
        result = null;
        if (!tags.TryGetValue(name, out ReadOnlyMemory<byte> memory))
            return false;
        ReadOnlySpan<byte> data = memory.Span;
        if (data.Length < 12)
            return false;
        if (data[..4].SequenceEqual("para"u8))
            return TryReadParametricCurve(data, out result);
        if (!data[..4].SequenceEqual("curv"u8))
            return false;
        int count = ReadInt32(data, 8);
        if (count < 0 || count > 1_000_000 || 12L + count * 2L > data.Length)
            return false;
        if (count == 0)
        {
            result = new[] { 1d };
            return true;
        }

        if (count == 1)
        {
            result = new[] { BinaryPrimitives.ReadUInt16BigEndian(data[12..]) / 256d };
            return true;
        }

        result = new double[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt16BigEndian(
                data.Slice(12 + index * 2, 2)) / 65535d;
        }

        return true;
    }

    private static bool TryReadParametricCurve(
        ReadOnlySpan<byte> data,
        out double[]? result)
    {
        result = null;
        if (data.Length < 16)
            return false;
        int type = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        int parameterCount = type switch
        {
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 5,
            4 => 7,
            _ => 0
        };
        if (parameterCount == 0 || data.Length < 12 + parameterCount * 4)
            return false;

        var parameters = new double[parameterCount];
        for (int index = 0; index < parameters.Length; index++)
        {
            parameters[index] = ReadS15Fixed16(data, 12 + index * 4);
            if (!double.IsFinite(parameters[index]))
                return false;
        }
        if (type is 1 or 2 && parameters[1] == 0)
            return false;

        const int points = 4097;
        result = new double[points];
        for (int index = 0; index < result.Length; index++)
        {
            double x = index / (double)(points - 1);
            double y = type switch
            {
                0 => Math.Pow(x, parameters[0]),
                1 => x >= -parameters[2] / parameters[1]
                    ? Math.Pow(parameters[1] * x + parameters[2], parameters[0])
                    : 0,
                2 => x >= -parameters[2] / parameters[1]
                    ? Math.Pow(parameters[1] * x + parameters[2], parameters[0]) +
                      parameters[3]
                    : parameters[3],
                3 => x >= parameters[4]
                    ? Math.Pow(parameters[1] * x + parameters[2], parameters[0])
                    : parameters[3] * x,
                _ => x >= parameters[4]
                    ? Math.Pow(parameters[1] * x + parameters[2], parameters[0]) +
                      parameters[5]
                    : parameters[3] * x + parameters[6]
            };
            if (!double.IsFinite(y))
            {
                result = null;
                return false;
            }

            result[index] = Math.Clamp(y, 0, 1);
        }

        return true;
    }

    private static string? ReadDescription(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> tags)
    {
        if (!tags.TryGetValue("desc", out ReadOnlyMemory<byte> memory))
            return null;
        ReadOnlySpan<byte> data = memory.Span;
        if (data.Length < 12 || !data[..4].SequenceEqual("desc"u8))
            return null;
        int count = ReadInt32(data, 8);
        if (count <= 1 || count > data.Length - 12)
            return null;
        return Encoding.ASCII.GetString(data.Slice(12, count - 1));
    }

    private static double EvaluateCurve(IReadOnlyList<double> curve, double value)
    {
        value = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        if (curve.Count == 1)
            return Math.Pow(value, curve[0]);
        double position = value * (curve.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = Math.Min(lower + 1, curve.Count - 1);
        double fraction = position - lower;
        return curve[lower] + fraction * (curve[upper] - curve[lower]);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));

    private static double ReadS15Fixed16(ReadOnlySpan<byte> data, int offset) =>
        ReadInt32(data, offset) / 65536d;
}
