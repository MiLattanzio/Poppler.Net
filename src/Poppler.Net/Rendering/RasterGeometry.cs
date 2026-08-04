namespace Poppler.Rendering;

internal readonly record struct RasterBounds(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public bool IsEmpty =>
        !double.IsFinite(Left) ||
        !double.IsFinite(Top) ||
        !double.IsFinite(Right) ||
        !double.IsFinite(Bottom) ||
        Right <= Left ||
        Bottom <= Top;

    public static RasterBounds Empty { get; } =
        new(double.PositiveInfinity, double.PositiveInfinity,
            double.NegativeInfinity, double.NegativeInfinity);

    public RasterBounds Include(PdfPoint point) =>
        new(
            Math.Min(Left, point.X),
            Math.Min(Top, point.Y),
            Math.Max(Right, point.X),
            Math.Max(Bottom, point.Y));
}

internal sealed record RasterSubpath(IReadOnlyList<PdfPoint> Points, bool Closed);

internal sealed class RasterPath
{
    public RasterPath(IReadOnlyList<RasterSubpath> subpaths, RasterBounds bounds)
    {
        Subpaths = subpaths;
        Bounds = bounds;
    }

    public IReadOnlyList<RasterSubpath> Subpaths { get; }
    public RasterBounds Bounds { get; }
    public static RasterPath Empty { get; } =
        new(Array.Empty<RasterSubpath>(), RasterBounds.Empty);
}

/// <summary>
/// Common flattened-path and nonzero/even-odd scanner used by fill, stroke
/// outlines and raster clips.
/// </summary>
internal static class RasterGeometry
{
    private const int MaximumCubicSubdivisionDepth = 16;

    public static RasterPath Flatten(
        PdfGraphicsPath path,
        PdfMatrix transform,
        RasterGeometryBudget? budget = null,
        bool temporaryClip = false,
        double flatness = 0.25) =>
        FlattenCore(
            path,
            transform,
            transform,
            budget,
            temporaryClip,
            flatness);

    /// <summary>
    /// Flattens into user space while measuring subdivision error after the
    /// complete user-to-device transform. Stroke expansion can therefore
    /// happen before the CTM without losing device-space curve accuracy.
    /// </summary>
    public static RasterPath FlattenStrokeCenterline(
        PdfGraphicsPath path,
        PdfMatrix userToDevice,
        RasterGeometryBudget budget,
        double flatness = 0.25) =>
        FlattenStrokeCenterline(
            path,
            PdfMatrix.Identity,
            userToDevice,
            budget,
            flatness);

    public static RasterPath FlattenStrokeCenterline(
        PdfGraphicsPath path,
        PdfMatrix centerlineToUser,
        PdfMatrix userToDevice,
        RasterGeometryBudget budget,
        double flatness = 0.25) =>
        FlattenCore(
            path,
            centerlineToUser,
            centerlineToUser.Multiply(userToDevice),
            budget,
            temporaryClip: false,
            flatness);

    public static RasterPath Transform(RasterPath path, PdfMatrix transform)
    {
        if (!transform.IsFinite || path.Subpaths.Count == 0)
            return RasterPath.Empty;

        var subpaths = new List<RasterSubpath>(path.Subpaths.Count);
        RasterBounds bounds = RasterBounds.Empty;
        foreach (RasterSubpath subpath in path.Subpaths)
        {
            var points = new PdfPoint[subpath.Points.Count];
            for (int index = 0; index < points.Length; index++)
            {
                PdfPoint source = subpath.Points[index];
                PdfPoint point = transform.Transform(source.X, source.Y);
                if (!IsFinite(point))
                    return RasterPath.Empty;
                points[index] = point;
                bounds = bounds.Include(point);
            }
            subpaths.Add(new RasterSubpath(points, subpath.Closed));
        }

        return new RasterPath(subpaths, bounds);
    }

    public static bool IsNearSingular(PdfMatrix matrix)
    {
        if (!matrix.IsFinite)
            return true;
        double first = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
        double second = Math.Sqrt(matrix.C * matrix.C + matrix.D * matrix.D);
        double scale = first * second;
        double determinant = Math.Abs(matrix.A * matrix.D - matrix.B * matrix.C);
        return !double.IsFinite(scale) ||
               !double.IsFinite(determinant) ||
               scale <= 1e-15 ||
               determinant <= Math.Max(1e-15, scale * 1e-12);
    }

    public static bool Contains(
        RasterPath path,
        double x,
        double y,
        PdfFillRule rule)
    {
        int winding = 0;
        bool odd = false;
        foreach (RasterSubpath subpath in path.Subpaths)
        {
            IReadOnlyList<PdfPoint> points = subpath.Points;
            if (points.Count < 2)
                continue;
            for (int index = 0; index < points.Count; index++)
            {
                PdfPoint first = points[index];
                PdfPoint second = points[(index + 1) % points.Count];
                if ((first.Y > y) == (second.Y > y))
                    continue;
                double intersection =
                    first.X + (y - first.Y) * (second.X - first.X) /
                    (second.Y - first.Y);
                if (intersection <= x)
                    continue;
                if (rule == PdfFillRule.EvenOdd)
                    odd = !odd;
                else
                    winding += second.Y > first.Y ? 1 : -1;
            }
        }

        return rule == PdfFillRule.EvenOdd ? odd : winding != 0;
    }

    public static bool TryInvert(PdfMatrix matrix, out PdfMatrix inverse)
    {
        double determinant = matrix.A * matrix.D - matrix.B * matrix.C;
        if (IsNearSingular(matrix))
        {
            inverse = PdfMatrix.Identity;
            return false;
        }

        inverse = new PdfMatrix(
            matrix.D / determinant,
            -matrix.B / determinant,
            -matrix.C / determinant,
            matrix.A / determinant,
            (matrix.C * matrix.F - matrix.D * matrix.E) / determinant,
            (matrix.B * matrix.E - matrix.A * matrix.F) / determinant);
        return inverse.IsFinite;
    }

    private static RasterPath FlattenCore(
        PdfGraphicsPath path,
        PdfMatrix outputTransform,
        PdfMatrix flatnessTransform,
        RasterGeometryBudget? budget,
        bool temporaryClip,
        double flatness)
    {
        if (!outputTransform.IsFinite || !flatnessTransform.IsFinite)
            return RasterPath.Empty;
        var subpaths = new List<RasterSubpath>();
        var points = new List<PdfPoint>();
        RasterBounds bounds = RasterBounds.Empty;
        PdfPoint sourceCurrent = default;
        PdfPoint sourceStart = default;
        bool hasCurrent = false;
        bool closed = false;

        void Finish()
        {
            if (points.Count > 0)
                subpaths.Add(new RasterSubpath(points.ToArray(), closed));
            points = new List<PdfPoint>();
            hasCurrent = false;
            closed = false;
        }

        void Add(PdfPoint point, bool producedSegment)
        {
            if (!IsFinite(point))
                return;
            if (producedSegment)
            {
                budget?.Consume(
                    temporaryClip ? 2 : 1,
                    temporaryClip
                        ? "flattened clip geometry"
                        : "flattened path geometry");
            }
            points.Add(point);
            bounds = bounds.Include(point);
            hasCurrent = true;
        }

        foreach (PdfPathSegment segment in path.Segments)
        {
            switch (segment)
            {
                case PdfMoveTo move:
                    Finish();
                    sourceStart = move.Point;
                    sourceCurrent = move.Point;
                    Add(outputTransform.Transform(move.Point.X, move.Point.Y), false);
                    break;
                case PdfLineTo line:
                    if (!hasCurrent)
                    {
                        sourceStart = line.Point;
                        sourceCurrent = line.Point;
                        Add(outputTransform.Transform(line.Point.X, line.Point.Y), false);
                    }
                    else
                    {
                        sourceCurrent = line.Point;
                        Add(outputTransform.Transform(line.Point.X, line.Point.Y), true);
                    }
                    break;
                case PdfCubicBezierTo curve when hasCurrent:
                    FlattenCubicTransformed(
                        sourceCurrent,
                        curve.Control1,
                        curve.Control2,
                        curve.End,
                        outputTransform,
                        flatnessTransform,
                        flatness,
                        depth: 0,
                        point => Add(point, true));
                    sourceCurrent = curve.End;
                    break;
                case PdfClosePath when hasCurrent:
                    if (DistanceSquared(sourceCurrent, sourceStart) > 1e-24)
                    {
                        Add(
                            outputTransform.Transform(sourceStart.X, sourceStart.Y),
                            true);
                    }
                    sourceCurrent = sourceStart;
                    closed = true;
                    Finish();
                    break;
            }
        }

        Finish();
        return new RasterPath(subpaths, bounds);
    }

    private static void FlattenCubicTransformed(
        PdfPoint start,
        PdfPoint control1,
        PdfPoint control2,
        PdfPoint end,
        PdfMatrix outputTransform,
        PdfMatrix flatnessTransform,
        double flatness,
        int depth,
        Action<PdfPoint> add)
    {
        PdfPoint deviceStart = flatnessTransform.Transform(start.X, start.Y);
        PdfPoint deviceControl1 = flatnessTransform.Transform(
            control1.X,
            control1.Y);
        PdfPoint deviceControl2 = flatnessTransform.Transform(
            control2.X,
            control2.Y);
        PdfPoint deviceEnd = flatnessTransform.Transform(end.X, end.Y);
        if (depth >= MaximumCubicSubdivisionDepth ||
            Math.Max(
                DistanceToLine(deviceControl1, deviceStart, deviceEnd),
                DistanceToLine(deviceControl2, deviceStart, deviceEnd)) <= flatness)
        {
            add(outputTransform.Transform(end.X, end.Y));
            return;
        }

        PdfPoint first = Midpoint(start, control1);
        PdfPoint middle1 = Midpoint(control1, control2);
        PdfPoint last = Midpoint(control2, end);
        PdfPoint middle2 = Midpoint(first, middle1);
        PdfPoint middle3 = Midpoint(middle1, last);
        PdfPoint center = Midpoint(middle2, middle3);
        FlattenCubicTransformed(
            start,
            first,
            middle2,
            center,
            outputTransform,
            flatnessTransform,
            flatness,
            depth + 1,
            add);
        FlattenCubicTransformed(
            center,
            middle3,
            last,
            end,
            outputTransform,
            flatnessTransform,
            flatness,
            depth + 1,
            add);
    }

    private static double DistanceToLine(
        PdfPoint point,
        PdfPoint first,
        PdfPoint second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        return length <= 1e-12
            ? Math.Sqrt(DistanceSquared(point, first))
            : Math.Abs(dy * point.X - dx * point.Y +
                       second.X * first.Y - second.Y * first.X) / length;
    }

    private static double DistanceSquared(PdfPoint first, PdfPoint second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        return x * x + y * y;
    }

    private static PdfPoint Midpoint(PdfPoint first, PdfPoint second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static bool IsFinite(PdfPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);
}
