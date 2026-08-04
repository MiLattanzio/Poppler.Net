namespace Poppler.Rendering;

/// <summary>
/// Converts a PDF stroke to closed fill geometry. Non-hairline outlines are
/// built in user space and transformed only after cap, join and dash geometry
/// is complete, preserving anisotropic, sheared and reflected CTMs.
/// </summary>
internal static class RasterStrokeOutliner
{
    private const double Epsilon = 1e-12;

    public static RasterPath Create(
        PdfGraphicsPath path,
        PdfMatrix userToDevice,
        double lineWidth,
        PdfDashPattern dash,
        PdfLineCap lineCap,
        PdfLineJoin lineJoin,
        double miterLimit,
        RasterGeometryBudget budget) =>
        Create(
            path,
            PdfMatrix.Identity,
            userToDevice,
            lineWidth,
            dash,
            lineCap,
            lineJoin,
            miterLimit,
            budget);

    public static RasterPath Create(
        PdfGraphicsPath path,
        PdfMatrix centerlineToUser,
        PdfMatrix userToDevice,
        double lineWidth,
        PdfDashPattern dash,
        PdfLineCap lineCap,
        PdfLineJoin lineJoin,
        double miterLimit,
        RasterGeometryBudget budget)
    {
        if (RasterGeometry.IsNearSingular(userToDevice) ||
            !centerlineToUser.IsFinite ||
            !double.IsFinite(lineWidth) ||
            lineWidth < 0)
        {
            return RasterPath.Empty;
        }

        RasterPath centerline = RasterGeometry.FlattenStrokeCenterline(
            path,
            centerlineToUser,
            userToDevice,
            budget);
        IReadOnlyList<StrokeRun> runs = CreateRuns(centerline, dash, budget);
        if (runs.Count == 0)
            return RasterPath.Empty;

        // PDF zero-width strokes are device hairlines. Their dash positions
        // are still computed in user space, but the one-pixel outline is the
        // sole case that must be expanded after the CTM.
        if (lineWidth <= Epsilon)
        {
            IReadOnlyList<StrokeRun> deviceRuns = runs
                .Select(run => Transform(run, userToDevice))
                .ToArray();
            return BuildOutline(
                deviceRuns,
                PdfMatrix.Identity,
                0.5,
                lineCap,
                lineJoin,
                miterLimit,
                budget);
        }

        RasterPath userOutline = BuildOutline(
            runs,
            userToDevice,
            lineWidth / 2,
            lineCap,
            lineJoin,
            miterLimit,
            budget);
        return RasterGeometry.Transform(userOutline, userToDevice);
    }

    private static RasterPath BuildOutline(
        IReadOnlyList<StrokeRun> runs,
        PdfMatrix errorTransform,
        double radius,
        PdfLineCap lineCap,
        PdfLineJoin lineJoin,
        double miterLimit,
        RasterGeometryBudget budget)
    {
        if (!double.IsFinite(radius) || radius <= 0)
            return RasterPath.Empty;
        var outline = new OutlineAssembler(errorTransform, budget);
        foreach (StrokeRun run in runs)
            outline.AddRun(run, radius, lineCap, lineJoin, miterLimit);
        return outline.Build();
    }

    private static IReadOnlyList<StrokeRun> CreateRuns(
        RasterPath centerline,
        PdfDashPattern dash,
        RasterGeometryBudget budget)
    {
        bool solid = !DashCursor.TryCreate(dash, out DashCursor? initial);
        var result = new List<StrokeRun>();
        foreach (RasterSubpath subpath in centerline.Subpaths)
        {
            List<PdfPoint> points = NormalizeSubpath(subpath);
            if (points.Count == 0)
                continue;
            if (solid)
            {
                result.Add(new StrokeRun(points, subpath.Closed));
                continue;
            }

            AppendDashedRuns(
                points,
                subpath.Closed,
                initial!.Clone(),
                budget,
                result);
        }

        return result;
    }

    private static void AppendDashedRuns(
        IReadOnlyList<PdfPoint> points,
        bool closed,
        DashCursor cursor,
        RasterGeometryBudget budget,
        List<StrokeRun> result)
    {
        int firstRun = result.Count;
        List<PdfPoint>? current = null;
        bool traversedLength = false;
        EmitZeroOnElements(cursor, points[0], budget, result);
        bool startsOn = cursor.IsOn;
        int edgeCount = closed ? points.Count : points.Count - 1;
        if (edgeCount <= 0)
        {
            if (!closed && startsOn)
                result.Add(new StrokeRun(new[] { points[0] }, false));
            return;
        }

        for (int edge = 0; edge < edgeCount; edge++)
        {
            PdfPoint first = points[edge];
            PdfPoint second = points[(edge + 1) % points.Count];
            double length = Distance(first, second);
            if (length <= Epsilon)
                continue;
            traversedLength = true;

            double position = 0;
            while (position < length - Epsilon)
            {
                PdfPoint positionPoint = Interpolate(
                    first,
                    second,
                    position / length);
                EmitZeroOnElements(cursor, positionPoint, budget, result);
                bool wasOn = cursor.IsOn;
                double amount = Math.Min(length - position, cursor.Remaining);
                if (amount <= Epsilon)
                {
                    cursor.Advance();
                    continue;
                }

                double end = position + amount;
                if (wasOn)
                {
                    budget.Consume(1, "dash fragments");
                    PdfPoint fragmentStart = Interpolate(
                        first,
                        second,
                        position / length);
                    PdfPoint fragmentEnd = Interpolate(
                        first,
                        second,
                        end / length);
                    current ??= new List<PdfPoint> { fragmentStart };
                    AddDistinct(current, fragmentStart);
                    AddDistinct(current, fragmentEnd);
                }

                bool boundary = amount >= cursor.Remaining - Epsilon;
                cursor.Consume(amount);
                position = end;
                if (!boundary)
                    continue;
                PdfPoint boundaryPoint = Interpolate(
                    first,
                    second,
                    position / length);
                EmitZeroOnElements(cursor, boundaryPoint, budget, result);
                if (wasOn && !cursor.IsOn && current is not null)
                {
                    result.Add(new StrokeRun(current.ToArray(), false));
                    current = null;
                }
            }
        }

        if (current is not null)
            result.Add(new StrokeRun(current.ToArray(), false));

        if (!traversedLength && !closed && startsOn)
        {
            result.Add(new StrokeRun(new[] { points[0] }, false));
            return;
        }

        bool endsOn = cursor.IsOn;
        int runCount = result.Count - firstRun;
        if (!closed || !startsOn || !endsOn || runCount == 0)
            return;

        if (runCount == 1)
        {
            StrokeRun only = result[firstRun];
            if (only.Points.Count > 2 &&
                SamePoint(only.Points[0], only.Points[^1]))
            {
                result[firstRun] = new StrokeRun(
                    only.Points.Take(only.Points.Count - 1).ToArray(),
                    true);
            }
            return;
        }

        StrokeRun firstRunValue = result[firstRun];
        StrokeRun lastRunValue = result[^1];
        if (firstRunValue.Points.Count < 2 || lastRunValue.Points.Count < 2)
            return;
        var merged = new List<PdfPoint>(
            lastRunValue.Points.Count + firstRunValue.Points.Count);
        foreach (PdfPoint point in lastRunValue.Points)
            AddDistinct(merged, point);
        foreach (PdfPoint point in firstRunValue.Points)
            AddDistinct(merged, point);
        result.RemoveAt(result.Count - 1);
        result.RemoveAt(firstRun);
        result.Insert(firstRun, new StrokeRun(merged.ToArray(), false));
    }

    private static void EmitZeroOnElements(
        DashCursor cursor,
        PdfPoint point,
        RasterGeometryBudget budget,
        List<StrokeRun> result)
    {
        int guard = 0;
        while (cursor.Remaining <= Epsilon && guard++ <= cursor.ElementCount)
        {
            if (cursor.IsOn)
            {
                budget.Consume(1, "zero-length dash fragments");
                result.Add(new StrokeRun(new[] { point }, false));
            }
            cursor.Advance();
        }
    }

    private static List<PdfPoint> NormalizeSubpath(RasterSubpath subpath)
    {
        var points = new List<PdfPoint>(subpath.Points.Count);
        foreach (PdfPoint point in subpath.Points)
            AddDistinct(points, point);
        if (subpath.Closed && points.Count > 1 &&
            SamePoint(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }
        return points;
    }

    private static StrokeRun Transform(StrokeRun run, PdfMatrix matrix)
    {
        var points = new PdfPoint[run.Points.Count];
        for (int index = 0; index < points.Length; index++)
        {
            PdfPoint source = run.Points[index];
            points[index] = matrix.Transform(source.X, source.Y);
        }
        return new StrokeRun(points, run.Closed);
    }

    private static void AddDistinct(List<PdfPoint> points, PdfPoint point)
    {
        if (points.Count == 0 || !SamePoint(points[^1], point))
            points.Add(point);
    }

    private static bool SamePoint(PdfPoint first, PdfPoint second) =>
        DistanceSquared(first, second) <= Epsilon * Epsilon;

    private static double Distance(PdfPoint first, PdfPoint second) =>
        Math.Sqrt(DistanceSquared(first, second));

    private static double DistanceSquared(PdfPoint first, PdfPoint second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        return x * x + y * y;
    }

    private static PdfPoint Interpolate(
        PdfPoint first,
        PdfPoint second,
        double amount) =>
        new(
            first.X + (second.X - first.X) * amount,
            first.Y + (second.Y - first.Y) * amount);

    private sealed record StrokeRun(IReadOnlyList<PdfPoint> Points, bool Closed);

    private sealed class DashCursor
    {
        private readonly double[] _segments;
        private readonly double _patternLength;

        private DashCursor(double[] segments, double phase)
        {
            _segments = segments;
            _patternLength = segments.Sum();
            Remaining = segments[0];
            double normalized = phase % _patternLength;
            if (normalized < 0)
                normalized += _patternLength;
            ConsumeDistance(normalized);
        }

        private DashCursor(DashCursor source)
        {
            _segments = source._segments;
            _patternLength = source._patternLength;
            Index = source.Index;
            Remaining = source.Remaining;
        }

        public int Index { get; private set; }
        public double Remaining { get; private set; }
        public bool IsOn => (Index & 1) == 0;
        public int ElementCount => _segments.Length;

        public static bool TryCreate(
            PdfDashPattern dash,
            out DashCursor? cursor)
        {
            if (dash.Segments.Count == 0)
            {
                cursor = null;
                return false;
            }

            int count = (dash.Segments.Count & 1) == 0
                ? dash.Segments.Count
                : dash.Segments.Count * 2;
            var segments = new double[count];
            double total = 0;
            for (int index = 0; index < segments.Length; index++)
            {
                double value = dash.Segments[index % dash.Segments.Count];
                value = double.IsFinite(value) ? Math.Max(0, value) : 0;
                segments[index] = value;
                total += value;
            }
            if (!double.IsFinite(total) || total <= Epsilon)
            {
                cursor = null;
                return false;
            }

            cursor = new DashCursor(segments, dash.Phase);
            return true;
        }

        public DashCursor Clone() => new(this);

        public void SkipZeroElements()
        {
            int guard = 0;
            while (Remaining <= Epsilon && guard++ <= _segments.Length)
                Advance();
        }

        public void Consume(double amount)
        {
            Remaining -= amount;
            if (Remaining <= Epsilon)
                Advance();
        }

        public void Advance()
        {
            Index = (Index + 1) % _segments.Length;
            Remaining = _segments[Index];
        }

        private void ConsumeDistance(double distance)
        {
            distance %= _patternLength;
            int guard = 0;
            while (distance > Epsilon && guard++ < _segments.Length * 2 + 1)
            {
                SkipZeroElements();
                double amount = Math.Min(distance, Remaining);
                Remaining -= amount;
                distance -= amount;
                if (Remaining <= Epsilon)
                    Advance();
            }
        }
    }

    private sealed class OutlineAssembler
    {
        private const int MaximumArcSegments = 4096;
        private const double ArcFlatness = 0.05;
        private readonly PdfMatrix _errorTransform;
        private readonly RasterGeometryBudget _budget;
        private readonly List<RasterSubpath> _subpaths = new();
        private RasterBounds _bounds = RasterBounds.Empty;

        public OutlineAssembler(
            PdfMatrix errorTransform,
            RasterGeometryBudget budget)
        {
            _errorTransform = errorTransform;
            _budget = budget;
        }

        public void AddRun(
            StrokeRun run,
            double radius,
            PdfLineCap lineCap,
            PdfLineJoin lineJoin,
            double miterLimit)
        {
            List<PdfPoint> points = NormalizeRun(run.Points, run.Closed);
            if (points.Count == 0)
                return;
            if (points.Count == 1)
            {
                if (!run.Closed)
                    AddDegenerateCap(points[0], radius, lineCap);
                return;
            }

            int edgeCount = run.Closed ? points.Count : points.Count - 1;
            for (int edge = 0; edge < edgeCount; edge++)
            {
                PdfPoint first = points[edge];
                PdfPoint second = points[(edge + 1) % points.Count];
                AddSegment(first, second, radius);
            }

            if (run.Closed)
            {
                for (int index = 0; index < points.Count; index++)
                {
                    AddJoin(
                        points[(index + points.Count - 1) % points.Count],
                        points[index],
                        points[(index + 1) % points.Count],
                        radius,
                        lineJoin,
                        miterLimit);
                }
            }
            else
            {
                for (int index = 1; index < points.Count - 1; index++)
                {
                    AddJoin(
                        points[index - 1],
                        points[index],
                        points[index + 1],
                        radius,
                        lineJoin,
                        miterLimit);
                }
                AddCap(points[0], points[1], radius, lineCap);
                AddCap(
                    points[^1],
                    points[^2],
                    radius,
                    lineCap);
            }
        }

        public RasterPath Build() =>
            _subpaths.Count == 0
                ? RasterPath.Empty
                : new RasterPath(_subpaths.ToArray(), _bounds);

        private void AddSegment(PdfPoint first, PdfPoint second, double radius)
        {
            if (!TryDirection(first, second, out double x, out double y))
                return;
            double normalX = -y * radius;
            double normalY = x * radius;
            AddPolygon(
                new PdfPoint(first.X + normalX, first.Y + normalY),
                new PdfPoint(first.X - normalX, first.Y - normalY),
                new PdfPoint(second.X - normalX, second.Y - normalY),
                new PdfPoint(second.X + normalX, second.Y + normalY));
        }

        private void AddJoin(
            PdfPoint previous,
            PdfPoint vertex,
            PdfPoint next,
            double radius,
            PdfLineJoin lineJoin,
            double miterLimit)
        {
            if (!TryDirection(previous, vertex, out double firstX, out double firstY) ||
                !TryDirection(vertex, next, out double secondX, out double secondY))
            {
                return;
            }

            double cross = firstX * secondY - firstY * secondX;
            double dot = firstX * secondX + firstY * secondY;
            if (Math.Abs(cross) <= 1e-10)
            {
                if (dot < 0)
                {
                    if (lineJoin == PdfLineJoin.Round)
                        AddCircle(vertex, radius);
                }
                return;
            }

            if (lineJoin == PdfLineJoin.Round)
            {
                // The union of the two segment rectangles and this disk is
                // exactly a round join, including acute cusps.
                AddCircle(vertex, radius);
                return;
            }

            double side = cross > 0 ? -1 : 1;
            var firstOuter = new PdfPoint(
                vertex.X - firstY * radius * side,
                vertex.Y + firstX * radius * side);
            var secondOuter = new PdfPoint(
                vertex.X - secondY * radius * side,
                vertex.Y + secondX * radius * side);
            if (lineJoin == PdfLineJoin.Bevel)
            {
                AddPolygon(vertex, firstOuter, secondOuter);
                return;
            }

            double lineCross = firstX * secondY - firstY * secondX;
            double deltaX = secondOuter.X - firstOuter.X;
            double deltaY = secondOuter.Y - firstOuter.Y;
            double amount =
                (deltaX * secondY - deltaY * secondX) / lineCross;
            var miter = new PdfPoint(
                firstOuter.X + amount * firstX,
                firstOuter.Y + amount * firstY);
            double limit = Math.Max(1, miterLimit) * radius;
            if (!double.IsFinite(miter.X) ||
                !double.IsFinite(miter.Y) ||
                DistanceSquared(vertex, miter) > limit * limit)
            {
                AddPolygon(vertex, firstOuter, secondOuter);
                return;
            }
            AddPolygon(vertex, firstOuter, miter, secondOuter);
        }

        private void AddCap(
            PdfPoint endpoint,
            PdfPoint adjacent,
            double radius,
            PdfLineCap lineCap)
        {
            if (lineCap == PdfLineCap.Butt ||
                !TryDirection(adjacent, endpoint, out double x, out double y))
            {
                return;
            }
            if (lineCap == PdfLineCap.Round)
            {
                AddCircle(endpoint, radius);
                return;
            }

            AddOrientedSquare(endpoint, x, y, radius);
        }

        private void AddDegenerateCap(
            PdfPoint point,
            double radius,
            PdfLineCap lineCap)
        {
            if (lineCap == PdfLineCap.Round)
                AddCircle(point, radius);
        }

        private void AddOrientedSquare(
            PdfPoint center,
            double directionX,
            double directionY,
            double radius)
        {
            double normalX = -directionY;
            double normalY = directionX;
            AddPolygon(
                new PdfPoint(
                    center.X + directionX * radius + normalX * radius,
                    center.Y + directionY * radius + normalY * radius),
                new PdfPoint(
                    center.X - directionX * radius + normalX * radius,
                    center.Y - directionY * radius + normalY * radius),
                new PdfPoint(
                    center.X - directionX * radius - normalX * radius,
                    center.Y - directionY * radius - normalY * radius),
                new PdfPoint(
                    center.X + directionX * radius - normalX * radius,
                    center.Y + directionY * radius - normalY * radius));
        }

        private void AddCircle(PdfPoint center, double radius)
        {
            int count = ArcSegments(radius, 2 * Math.PI);
            var points = new List<PdfPoint>(Math.Min(count, 256));
            for (int index = 0; index < count; index++)
            {
                double angle = 2 * Math.PI * index / count;
                AddPoint(
                    points,
                    new PdfPoint(
                        center.X + Math.Cos(angle) * radius,
                        center.Y + Math.Sin(angle) * radius));
            }
            CommitPolygon(points);
        }

        private int ArcSegments(double radius, double angle)
        {
            double scale = Math.Sqrt(
                _errorTransform.A * _errorTransform.A +
                _errorTransform.B * _errorTransform.B +
                _errorTransform.C * _errorTransform.C +
                _errorTransform.D * _errorTransform.D);
            double deviceRadius = radius * scale;
            if (!double.IsFinite(deviceRadius) || deviceRadius <= ArcFlatness)
                return 16;
            double cosine = Math.Clamp(1 - ArcFlatness / deviceRadius, -1, 1);
            double step = 2 * Math.Acos(cosine);
            int count = step <= Epsilon
                ? MaximumArcSegments
                : (int)Math.Ceiling(angle / step);
            return Math.Clamp(count, 16, MaximumArcSegments);
        }

        private void AddPolygon(params PdfPoint[] points)
        {
            var list = new List<PdfPoint>(points.Length);
            foreach (PdfPoint point in points)
                AddPoint(list, point);
            CommitPolygon(list);
        }

        private void AddPoint(List<PdfPoint> points, PdfPoint point)
        {
            _budget.Consume(1, "stroke outline geometry");
            points.Add(point);
        }

        private void CommitPolygon(List<PdfPoint> points)
        {
            if (points.Count < 3)
                return;
            double area = 0;
            for (int index = 0; index < points.Count; index++)
            {
                PdfPoint first = points[index];
                PdfPoint second = points[(index + 1) % points.Count];
                area += first.X * second.Y - second.X * first.Y;
            }
            if (area < 0)
                points.Reverse();
            foreach (PdfPoint point in points)
                _bounds = _bounds.Include(point);
            _subpaths.Add(new RasterSubpath(points.ToArray(), true));
        }

        private static List<PdfPoint> NormalizeRun(
            IReadOnlyList<PdfPoint> source,
            bool closed)
        {
            var result = new List<PdfPoint>(source.Count);
            foreach (PdfPoint point in source)
                AddDistinct(result, point);
            if (closed && result.Count > 1 && SamePoint(result[0], result[^1]))
                result.RemoveAt(result.Count - 1);
            return result;
        }

        private static bool TryDirection(
            PdfPoint first,
            PdfPoint second,
            out double x,
            out double y)
        {
            x = second.X - first.X;
            y = second.Y - first.Y;
            double length = Math.Sqrt(x * x + y * y);
            if (!double.IsFinite(length) || length <= Epsilon)
            {
                x = y = 0;
                return false;
            }
            x /= length;
            y /= length;
            return true;
        }
    }
}
