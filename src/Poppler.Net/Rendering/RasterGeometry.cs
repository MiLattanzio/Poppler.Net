namespace Poppler.Rendering;

internal readonly record struct RasterBounds(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public RasterBounds Expand(double amount) =>
        new(Left - amount, Top - amount, Right + amount, Bottom + amount);

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
}

internal static class RasterGeometry
{
    public static RasterPath Flatten(
        PdfGraphicsPath path,
        PdfMatrix transform,
        double flatness = 0.25)
    {
        var subpaths = new List<RasterSubpath>();
        var points = new List<PdfPoint>();
        RasterBounds bounds = RasterBounds.Empty;
        PdfPoint current = default;
        PdfPoint start = default;
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

        void Add(PdfPoint point)
        {
            points.Add(point);
            bounds = bounds.Include(point);
            current = point;
            hasCurrent = true;
        }

        foreach (PdfPathSegment segment in path.Segments)
        {
            switch (segment)
            {
                case PdfMoveTo move:
                    Finish();
                    start = transform.Transform(move.Point.X, move.Point.Y);
                    Add(start);
                    break;
                case PdfLineTo line:
                    if (!hasCurrent)
                    {
                        start = transform.Transform(line.Point.X, line.Point.Y);
                        Add(start);
                    }
                    else
                    {
                        Add(transform.Transform(line.Point.X, line.Point.Y));
                    }

                    break;
                case PdfCubicBezierTo curve when hasCurrent:
                {
                    PdfPoint control1 = transform.Transform(
                        curve.Control1.X,
                        curve.Control1.Y);
                    PdfPoint control2 = transform.Transform(
                        curve.Control2.X,
                        curve.Control2.Y);
                    PdfPoint end = transform.Transform(curve.End.X, curve.End.Y);
                    FlattenCubic(
                        current,
                        control1,
                        control2,
                        end,
                        flatness,
                        0,
                        Add);
                    break;
                }
                case PdfClosePath when hasCurrent:
                    if (DistanceSquared(current, start) > 1e-12)
                        Add(start);
                    closed = true;
                    Finish();
                    break;
            }
        }

        Finish();
        return new RasterPath(subpaths, bounds);
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
            int count = points.Count;
            for (int index = 0; index < count; index++)
            {
                PdfPoint first = points[index];
                PdfPoint second = points[(index + 1) % count];
                if ((first.Y > y) == (second.Y > y))
                    continue;
                double intersection =
                    first.X + (y - first.Y) * (second.X - first.X) /
                    (second.Y - first.Y);
                if (intersection <= x)
                    continue;
                if (rule == PdfFillRule.EvenOdd)
                {
                    odd = !odd;
                }
                else
                {
                    winding += second.Y > first.Y ? 1 : -1;
                }
            }
        }

        return rule == PdfFillRule.EvenOdd ? odd : winding != 0;
    }

    public static bool StrokeContains(
        RasterPath path,
        double x,
        double y,
        double width,
        PdfDashPattern dash)
    {
        double radiusSquared = width * width / 4;
        if (radiusSquared <= 0)
            radiusSquared = 0.25;
        foreach ((PdfPoint first, PdfPoint second) in EnumerateStrokeSegments(path, dash))
        {
            if (DistanceToSegmentSquared(x, y, first, second) <= radiusSquared)
                return true;
        }

        return false;
    }

    public static bool TryInvert(PdfMatrix matrix, out PdfMatrix inverse)
    {
        double determinant = matrix.A * matrix.D - matrix.B * matrix.C;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-15)
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

    public static double EffectiveScale(PdfMatrix matrix)
    {
        double first = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
        double second = Math.Sqrt(matrix.C * matrix.C + matrix.D * matrix.D);
        return Math.Sqrt(Math.Max(0, first * second));
    }

    private static IEnumerable<(PdfPoint First, PdfPoint Second)> EnumerateStrokeSegments(
        RasterPath path,
        PdfDashPattern dash)
    {
        bool solid = dash.Segments.Count == 0;
        double patternLength = dash.Segments.Sum();
        foreach (RasterSubpath subpath in path.Subpaths)
        {
            IReadOnlyList<PdfPoint> points = subpath.Points;
            for (int index = 1; index < points.Count; index++)
            {
                PdfPoint first = points[index - 1];
                PdfPoint second = points[index];
                if (solid || patternLength <= 0)
                {
                    yield return (first, second);
                    continue;
                }

                foreach ((PdfPoint dashStart, PdfPoint dashEnd) in DashSegment(
                             first,
                             second,
                             dash,
                             patternLength))
                {
                    yield return (dashStart, dashEnd);
                }
            }
        }
    }

    private static IEnumerable<(PdfPoint First, PdfPoint Second)> DashSegment(
        PdfPoint first,
        PdfPoint second,
        PdfDashPattern dash,
        double patternLength)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-12)
            yield break;
        double phase = dash.Phase % patternLength;
        if (phase < 0)
            phase += patternLength;
        int patternIndex = 0;
        while (phase >= dash.Segments[patternIndex] &&
               dash.Segments[patternIndex] > 0)
        {
            phase -= dash.Segments[patternIndex];
            patternIndex = (patternIndex + 1) % dash.Segments.Count;
        }

        double position = 0;
        double remaining = dash.Segments[patternIndex] - phase;
        int guard = 0;
        while (position < length && guard++ < 100_000)
        {
            if (remaining <= 1e-12)
            {
                patternIndex = (patternIndex + 1) % dash.Segments.Count;
                remaining = dash.Segments[patternIndex];
                continue;
            }

            double end = Math.Min(length, position + remaining);
            if ((patternIndex & 1) == 0 && end > position)
            {
                yield return (
                    Interpolate(first, second, position / length),
                    Interpolate(first, second, end / length));
            }

            remaining -= end - position;
            position = end;
        }
    }

    private static void FlattenCubic(
        PdfPoint start,
        PdfPoint control1,
        PdfPoint control2,
        PdfPoint end,
        double flatness,
        int depth,
        Action<PdfPoint> add)
    {
        if (depth >= 12 ||
            Math.Max(
                DistanceToLine(control1, start, end),
                DistanceToLine(control2, start, end)) <= flatness)
        {
            add(end);
            return;
        }

        PdfPoint first = Midpoint(start, control1);
        PdfPoint middle1 = Midpoint(control1, control2);
        PdfPoint last = Midpoint(control2, end);
        PdfPoint middle2 = Midpoint(first, middle1);
        PdfPoint middle3 = Midpoint(middle1, last);
        PdfPoint center = Midpoint(middle2, middle3);
        FlattenCubic(start, first, middle2, center, flatness, depth + 1, add);
        FlattenCubic(center, middle3, last, end, flatness, depth + 1, add);
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

    private static double DistanceToSegmentSquared(
        double x,
        double y,
        PdfPoint first,
        PdfPoint second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double denominator = dx * dx + dy * dy;
        double t = denominator <= 1e-20
            ? 0
            : ((x - first.X) * dx + (y - first.Y) * dy) / denominator;
        t = Math.Clamp(t, 0, 1);
        double nearestX = first.X + t * dx;
        double nearestY = first.Y + t * dy;
        double differenceX = x - nearestX;
        double differenceY = y - nearestY;
        return differenceX * differenceX + differenceY * differenceY;
    }

    private static double DistanceSquared(PdfPoint first, PdfPoint second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        return x * x + y * y;
    }

    private static PdfPoint Midpoint(PdfPoint first, PdfPoint second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static PdfPoint Interpolate(PdfPoint first, PdfPoint second, double amount) =>
        new(
            first.X + (second.X - first.X) * amount,
            first.Y + (second.Y - first.Y) * amount);
}
