namespace Poppler.Graphics;

internal enum PdfMeshPatchEdgeSide
{
    Top,
    Right,
    Bottom,
    Left
}

/// <summary>
/// One oriented cubic boundary shared by adjacent type 6 or 7 mesh patches.
/// The orientation follows the clockwise PDF patch boundary.
/// </summary>
internal sealed class PdfMeshPatchEdge
{
    private const int ControlPointCount = 4;
    private readonly PdfPoint[] _controlPoints;

    internal PdfMeshPatchEdge(
        IReadOnlyList<PdfPoint> controlPoints,
        PdfColor startColor,
        PdfColor endColor)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        if (controlPoints.Count != ControlPointCount)
            throw new ArgumentException("A patch edge requires four control points.", nameof(controlPoints));
        _controlPoints = controlPoints.ToArray();
        StartColor = startColor;
        EndColor = endColor;
    }

    internal PdfColor StartColor { get; }
    internal PdfColor EndColor { get; }

    internal PdfPoint GetControlPoint(int index)
    {
        if ((uint)index >= ControlPointCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _controlPoints[index];
    }

    internal PdfPoint EvaluatePoint(double parameter)
    {
        double inverse = 1 - parameter;
        double first = inverse * inverse * inverse;
        double second = 3 * parameter * inverse * inverse;
        double third = 3 * parameter * parameter * inverse;
        double fourth = parameter * parameter * parameter;
        return new PdfPoint(
            _controlPoints[0].X * first +
            _controlPoints[1].X * second +
            _controlPoints[2].X * third +
            _controlPoints[3].X * fourth,
            _controlPoints[0].Y * first +
            _controlPoints[1].Y * second +
            _controlPoints[2].Y * third +
            _controlPoints[3].Y * fourth);
    }

    internal PdfColor EvaluateColor(double parameter)
    {
        (double startRed, double startGreen, double startBlue) =
            StartColor.ToRgb();
        (double endRed, double endGreen, double endBlue) = EndColor.ToRgb();
        return PdfColor.Rgb(
            Lerp(startRed, endRed, parameter),
            Lerp(startGreen, endGreen, parameter),
            Lerp(startBlue, endBlue, parameter));
    }

    internal bool Matches(
        IReadOnlyList<PdfPoint> controlPoints,
        PdfColor startColor,
        PdfColor endColor)
    {
        if (controlPoints.Count != ControlPointCount ||
            StartColor != startColor ||
            EndColor != endColor)
        {
            return false;
        }
        for (int index = 0; index < ControlPointCount; index++)
        {
            if (_controlPoints[index] != controlPoints[index])
                return false;
        }
        return true;
    }

    private static double Lerp(double first, double second, double amount) =>
        first + (second - first) * amount;
}

/// <summary>
/// Immutable parametric representation of one Coons or tensor-product patch.
/// Control points are row-major in the patch's (u, v) parameter space.
/// </summary>
internal sealed class PdfMeshPatch
{
    private const int ControlPointCount = 4;
    private const int CornerColorCount = 4;
    private readonly PdfPoint[] _controlPoints;
    private readonly PdfColor[] _cornerColors;
    private readonly PdfMeshPatchEdge[] _edges;

    internal PdfMeshPatch(
        PdfPoint[,] controlPoints,
        IReadOnlyList<PdfColor> cornerColors,
        PdfMeshPatchEdge? sharedTopEdge = null)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        ArgumentNullException.ThrowIfNull(cornerColors);
        if (controlPoints.GetLength(0) != ControlPointCount ||
            controlPoints.GetLength(1) != ControlPointCount)
        {
            throw new ArgumentException(
                "A mesh patch requires a 4x4 control-point grid.",
                nameof(controlPoints));
        }
        if (cornerColors.Count != CornerColorCount)
        {
            throw new ArgumentException(
                "A mesh patch requires four corner colors.",
                nameof(cornerColors));
        }

        _controlPoints = new PdfPoint[ControlPointCount * ControlPointCount];
        for (int row = 0; row < ControlPointCount; row++)
        {
            for (int column = 0; column < ControlPointCount; column++)
            {
                _controlPoints[row * ControlPointCount + column] =
                    controlPoints[row, column];
            }
        }
        _cornerColors = cornerColors.ToArray();

        PdfPoint[] top = EdgePoints(PdfMeshPatchEdgeSide.Top);
        if (sharedTopEdge is not null &&
            !sharedTopEdge.Matches(top, _cornerColors[0], _cornerColors[1]))
        {
            throw new ArgumentException(
                "The shared edge does not match the patch boundary.",
                nameof(sharedTopEdge));
        }
        _edges = new[]
        {
            sharedTopEdge ?? new PdfMeshPatchEdge(
                top,
                _cornerColors[0],
                _cornerColors[1]),
            new PdfMeshPatchEdge(
                EdgePoints(PdfMeshPatchEdgeSide.Right),
                _cornerColors[1],
                _cornerColors[2]),
            new PdfMeshPatchEdge(
                EdgePoints(PdfMeshPatchEdgeSide.Bottom),
                _cornerColors[2],
                _cornerColors[3]),
            new PdfMeshPatchEdge(
                EdgePoints(PdfMeshPatchEdgeSide.Left),
                _cornerColors[3],
                _cornerColors[0])
        };
    }

    internal PdfPoint GetControlPoint(int row, int column)
    {
        if ((uint)row >= ControlPointCount)
            throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)column >= ControlPointCount)
            throw new ArgumentOutOfRangeException(nameof(column));
        return _controlPoints[row * ControlPointCount + column];
    }

    internal PdfColor GetCornerColor(int index)
    {
        if ((uint)index >= CornerColorCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _cornerColors[index];
    }

    internal PdfMeshPatchEdge GetEdge(PdfMeshPatchEdgeSide side)
    {
        if ((uint)side >= _edges.Length)
            throw new ArgumentOutOfRangeException(nameof(side));
        return _edges[(int)side];
    }

    internal PdfPoint EvaluatePoint(double u, double v)
    {
        (double u0, double u1, double u2, double u3) = Bernstein(u);
        (double v0, double v1, double v2, double v3) = Bernstein(v);
        Span<double> bu = stackalloc[] { u0, u1, u2, u3 };
        Span<double> bv = stackalloc[] { v0, v1, v2, v3 };
        double x = 0;
        double y = 0;
        for (int row = 0; row < ControlPointCount; row++)
        {
            for (int column = 0; column < ControlPointCount; column++)
            {
                double weight = bu[row] * bv[column];
                PdfPoint point = GetControlPoint(row, column);
                x += point.X * weight;
                y += point.Y * weight;
            }
        }
        return new PdfPoint(x, y);
    }

    internal PdfColor EvaluateColor(double u, double v)
    {
        (double r00, double g00, double b00) = _cornerColors[0].ToRgb();
        (double r01, double g01, double b01) = _cornerColors[1].ToRgb();
        (double r11, double g11, double b11) = _cornerColors[2].ToRgb();
        (double r10, double g10, double b10) = _cornerColors[3].ToRgb();
        return PdfColor.Rgb(
            Bilinear(r00, r01, r11, r10, u, v),
            Bilinear(g00, g01, g11, g10, u, v),
            Bilinear(b00, b01, b11, b10, u, v));
    }

    private PdfPoint[] EdgePoints(PdfMeshPatchEdgeSide side)
    {
        var result = new PdfPoint[ControlPointCount];
        for (int index = 0; index < ControlPointCount; index++)
        {
            result[index] = side switch
            {
                PdfMeshPatchEdgeSide.Top => GetControlPoint(0, index),
                PdfMeshPatchEdgeSide.Right => GetControlPoint(index, 3),
                PdfMeshPatchEdgeSide.Bottom => GetControlPoint(3, 3 - index),
                _ => GetControlPoint(3 - index, 0)
            };
        }
        return result;
    }

    private static (double First, double Second, double Third, double Fourth)
        Bernstein(double value)
    {
        double inverse = 1 - value;
        return (
            inverse * inverse * inverse,
            3 * value * inverse * inverse,
            3 * value * value * inverse,
            value * value * value);
    }

    private static double Bilinear(
        double topLeft,
        double topRight,
        double bottomRight,
        double bottomLeft,
        double u,
        double v) =>
        (1 - u) * ((1 - v) * topLeft + v * topRight) +
        u * ((1 - v) * bottomLeft + v * bottomRight);
}
