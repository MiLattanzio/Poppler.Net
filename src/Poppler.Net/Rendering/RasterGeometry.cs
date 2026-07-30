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
        PdfDashPattern dash,
        PdfLineCap lineCap,
        PdfLineJoin lineJoin,
        double miterLimit)
    {
        double radius = Math.Max(width / 2, 0.5);
        foreach (RasterStrokeSegment segment in EnumerateStrokeSegments(path, dash))
        {
            if (StrokeSegmentContains(segment, x, y, radius, lineCap))
                return true;
        }

        foreach (RasterSubpath subpath in path.Subpaths)
        {
            IReadOnlyList<PdfPoint> points = subpath.Points;
            if (points.Count < 3)
                continue;
            double distance = 0;
            for (int index = 1; index < points.Count - 1; index++)
            {
                distance += Distance(points[index - 1], points[index]);
                if (DashContinuesThrough(dash, distance) &&
                    JoinContains(
                        points[index - 1],
                        points[index],
                        points[index + 1],
                        x,
                        y,
                        radius,
                        lineJoin,
                        miterLimit))
                {
                    return true;
                }
            }

            if (subpath.Closed)
            {
                double totalLength = distance +
                    Distance(points[^2], points[^1]);
                if (DashContinuesAcrossSeam(dash, totalLength) &&
                    JoinContains(
                        points[^2],
                        points[0],
                        points[1],
                        x,
                        y,
                        radius,
                        lineJoin,
                        miterLimit))
                {
                    return true;
                }
            }
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

    private readonly record struct RasterStrokeSegment(
        PdfPoint First,
        PdfPoint Second,
        bool StartCap,
        bool EndCap);

    private static IEnumerable<RasterStrokeSegment> EnumerateStrokeSegments(
        RasterPath path,
        PdfDashPattern dash)
    {
        bool solid = !TryCreateDashCursor(dash, out DashCursor? initialCursor);
        foreach (RasterSubpath subpath in path.Subpaths)
        {
            IReadOnlyList<PdfPoint> points = subpath.Points;
            if (points.Count < 2)
                continue;
            DashCursor? cursor = initialCursor?.Clone();
            for (int index = 1; index < points.Count; index++)
            {
                PdfPoint first = points[index - 1];
                PdfPoint second = points[index];
                if (solid)
                {
                    bool open = !subpath.Closed;
                    yield return new RasterStrokeSegment(
                        first,
                        second,
                        open && index == 1,
                        open && index == points.Count - 1);
                    continue;
                }

                foreach (RasterStrokeSegment segment in DashSegment(
                             first,
                             second,
                             cursor!,
                             index == 1,
                             index == points.Count - 1,
                             subpath.Closed))
                {
                    yield return segment;
                }
            }
        }
    }

    private static IEnumerable<RasterStrokeSegment> DashSegment(
        PdfPoint first,
        PdfPoint second,
        DashCursor cursor,
        bool firstPathSegment,
        bool lastPathSegment,
        bool closed)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-12)
            yield break;

        double position = 0;
        int guard = 0;
        while (position < length && guard++ < 100_000)
        {
            cursor.SkipEmptyElements();
            double amount = Math.Min(length - position, cursor.Remaining);
            bool patternBoundary = amount >= cursor.Remaining - 1e-12;
            double end = position + amount;
            if (cursor.IsOn && end > position)
            {
                bool startsPath = firstPathSegment && position <= 1e-12;
                bool endsPath =
                    lastPathSegment && end >= length - 1e-12;
                yield return new RasterStrokeSegment(
                    Interpolate(first, second, position / length),
                    Interpolate(first, second, end / length),
                    startsPath || cursor.AtElementStart,
                    patternBoundary || (endsPath && !closed));
            }

            cursor.Consume(amount);
            position = end;
        }
    }

    private static bool StrokeSegmentContains(
        RasterStrokeSegment segment,
        double x,
        double y,
        double radius,
        PdfLineCap lineCap)
    {
        double dx = segment.Second.X - segment.First.X;
        double dy = segment.Second.Y - segment.First.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-12)
        {
            return (segment.StartCap || segment.EndCap) &&
                   lineCap == PdfLineCap.Round &&
                   DistanceSquared(new PdfPoint(x, y), segment.First) <= radius * radius;
        }

        double directionX = dx / length;
        double directionY = dy / length;
        double relativeX = x - segment.First.X;
        double relativeY = y - segment.First.Y;
        double along = relativeX * directionX + relativeY * directionY;
        double across = Math.Abs(relativeX * directionY - relativeY * directionX);
        double start = segment.StartCap && lineCap == PdfLineCap.Square ? -radius : 0;
        double end = segment.EndCap && lineCap == PdfLineCap.Square
            ? length + radius
            : length;
        if (along >= start && along <= end && across <= radius)
            return true;
        if (lineCap != PdfLineCap.Round)
            return false;
        double radiusSquared = radius * radius;
        return segment.StartCap &&
               DistanceSquared(new PdfPoint(x, y), segment.First) <= radiusSquared ||
               segment.EndCap &&
               DistanceSquared(new PdfPoint(x, y), segment.Second) <= radiusSquared;
    }

    private static bool JoinContains(
        PdfPoint previous,
        PdfPoint vertex,
        PdfPoint next,
        double x,
        double y,
        double radius,
        PdfLineJoin lineJoin,
        double miterLimit)
    {
        double firstLength = Distance(previous, vertex);
        double secondLength = Distance(vertex, next);
        if (firstLength <= 1e-12 || secondLength <= 1e-12)
            return false;
        if (lineJoin == PdfLineJoin.Round)
        {
            return DistanceSquared(new PdfPoint(x, y), vertex) <=
                   radius * radius;
        }

        double firstX = (vertex.X - previous.X) / firstLength;
        double firstY = (vertex.Y - previous.Y) / firstLength;
        double secondX = (next.X - vertex.X) / secondLength;
        double secondY = (next.Y - vertex.Y) / secondLength;
        double cross = firstX * secondY - firstY * secondX;
        if (Math.Abs(cross) <= 1e-12)
            return false;

        double side = cross > 0 ? -1 : 1;
        var firstOuter = new PdfPoint(
            vertex.X - firstY * radius * side,
            vertex.Y + firstX * radius * side);
        var secondOuter = new PdfPoint(
            vertex.X - secondY * radius * side,
            vertex.Y + secondX * radius * side);
        if (lineJoin == PdfLineJoin.Bevel)
        {
            return ContainsPolygon(
                [vertex, firstOuter, secondOuter],
                x,
                y);
        }

        double offsetX = secondOuter.X - firstOuter.X;
        double offsetY = secondOuter.Y - firstOuter.Y;
        double intersectionAmount =
            (offsetX * secondY - offsetY * secondX) / cross;
        var miter = new PdfPoint(
            firstOuter.X + intersectionAmount * firstX,
            firstOuter.Y + intersectionAmount * firstY);
        double maximumMiter = Math.Max(1, miterLimit) * radius;
        if (DistanceSquared(miter, vertex) >
            maximumMiter * maximumMiter)
        {
            return ContainsPolygon(
                [vertex, firstOuter, secondOuter],
                x,
                y);
        }

        return ContainsPolygon(
            [vertex, firstOuter, miter, secondOuter],
            x,
            y);
    }

    private static bool ContainsPolygon(
        IReadOnlyList<PdfPoint> points,
        double x,
        double y)
    {
        bool inside = false;
        for (int current = 0, previous = points.Count - 1;
             current < points.Count;
             previous = current++)
        {
            PdfPoint first = points[current];
            PdfPoint second = points[previous];
            if ((first.Y > y) == (second.Y > y))
                continue;
            double intersection =
                (second.X - first.X) * (y - first.Y) /
                (second.Y - first.Y) + first.X;
            if (x < intersection)
                inside = !inside;
        }

        return inside;
    }

    private static bool DashContinuesThrough(
        PdfDashPattern dash,
        double distance)
    {
        const double epsilon = 1e-7;
        return IsDashOn(dash, Math.Max(0, distance - epsilon)) &&
               IsDashOn(dash, distance + epsilon);
    }

    private static bool DashContinuesAcrossSeam(
        PdfDashPattern dash,
        double totalLength)
    {
        const double epsilon = 1e-7;
        return IsDashOn(dash, Math.Max(0, totalLength - epsilon)) &&
               IsDashOn(dash, epsilon);
    }

    private static bool IsDashOn(PdfDashPattern dash, double distance)
    {
        if (!TryCreateDashCursor(dash, out DashCursor? cursor))
            return true;
        cursor!.ConsumeDistance(distance);
        cursor.SkipEmptyElements();
        return cursor.IsOn;
    }

    private static bool TryCreateDashCursor(
        PdfDashPattern dash,
        out DashCursor? cursor)
    {
        int sourceCount = dash.Segments.Count;
        int effectiveCount = (sourceCount & 1) == 0
            ? sourceCount
            : sourceCount * 2;
        double patternLength = sourceCount == 0
            ? 0
            : dash.Segments.Sum() * (effectiveCount / sourceCount);
        if (effectiveCount == 0 || patternLength <= 1e-12)
        {
            cursor = null;
            return false;
        }

        cursor = new DashCursor(dash.Segments, effectiveCount);
        double phase = dash.Phase % patternLength;
        if (phase < 0)
            phase += patternLength;
        cursor.ConsumeDistance(phase);
        return true;
    }

    private sealed class DashCursor
    {
        private readonly IReadOnlyList<double> _segments;
        private readonly int _effectiveCount;
        private readonly double _patternLength;

        public DashCursor(
            IReadOnlyList<double> segments,
            int effectiveCount)
        {
            _segments = segments;
            _effectiveCount = effectiveCount;
            _patternLength = Enumerable.Range(0, effectiveCount)
                .Sum(index => SegmentLength(index));
            Remaining = SegmentLength(0);
        }

        private DashCursor(DashCursor source)
        {
            _segments = source._segments;
            _effectiveCount = source._effectiveCount;
            _patternLength = source._patternLength;
            Index = source.Index;
            Remaining = source.Remaining;
            AtElementStart = source.AtElementStart;
        }

        public int Index { get; private set; }
        public double Remaining { get; private set; }
        public bool AtElementStart { get; private set; } = true;
        public bool IsOn => (Index & 1) == 0;

        public DashCursor Clone() => new(this);

        public void SkipEmptyElements()
        {
            int guard = 0;
            while (Remaining <= 1e-12 && guard++ <= _effectiveCount)
                Advance();
        }

        public void Consume(double amount)
        {
            Remaining -= amount;
            AtElementStart = false;
            if (Remaining <= 1e-12)
                Advance();
        }

        public void ConsumeDistance(double distance)
        {
            distance %= _patternLength;
            int guard = 0;
            while (distance > 1e-12 && guard++ < 1_000_000)
            {
                SkipEmptyElements();
                double amount = Math.Min(distance, Remaining);
                Remaining -= amount;
                distance -= amount;
                AtElementStart = false;
                if (Remaining <= 1e-12)
                    Advance();
            }
        }

        private void Advance()
        {
            Index = (Index + 1) % _effectiveCount;
            Remaining = SegmentLength(Index);
            AtElementStart = true;
        }

        private double SegmentLength(int index) =>
            _segments[index % _segments.Count];
    }

    private static double Distance(PdfPoint first, PdfPoint second) =>
        Math.Sqrt(DistanceSquared(first, second));

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
