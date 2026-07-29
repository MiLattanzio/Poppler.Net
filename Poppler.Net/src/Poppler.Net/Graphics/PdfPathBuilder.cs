namespace Poppler.Graphics;

internal sealed class PdfPathBuilder
{
    private readonly List<PdfPathSegment> _segments = new();
    private PdfPoint? _currentPoint;
    private PdfPoint? _subpathStart;

    public int Count => _segments.Count;
    public bool IsEmpty => _segments.Count == 0;
    public PdfPoint? CurrentPoint => _currentPoint;

    public void MoveTo(double x, double y)
    {
        var point = new PdfPoint(x, y);
        _segments.Add(new PdfMoveTo(point));
        _currentPoint = point;
        _subpathStart = point;
    }

    public void LineTo(double x, double y)
    {
        if (_currentPoint is null)
            return;
        var point = new PdfPoint(x, y);
        _segments.Add(new PdfLineTo(point));
        _currentPoint = point;
    }

    public void CurveTo(
        double x1,
        double y1,
        double x2,
        double y2,
        double x3,
        double y3)
    {
        if (_currentPoint is null)
            return;
        var end = new PdfPoint(x3, y3);
        _segments.Add(new PdfCubicBezierTo(
            new PdfPoint(x1, y1),
            new PdfPoint(x2, y2),
            end));
        _currentPoint = end;
    }

    public void CurveToV(double x2, double y2, double x3, double y3)
    {
        if (_currentPoint is not { } current)
            return;
        CurveTo(current.X, current.Y, x2, y2, x3, y3);
    }

    public void CurveToY(double x1, double y1, double x3, double y3) =>
        CurveTo(x1, y1, x3, y3, x3, y3);

    public void Rectangle(double x, double y, double width, double height)
    {
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        Close();
    }

    public void Close()
    {
        if (_currentPoint is null || _subpathStart is null)
            return;
        _segments.Add(new PdfClosePath());
        _currentPoint = _subpathStart;
    }

    public PdfGraphicsPath Snapshot() => new(_segments);

    public void Clear()
    {
        _segments.Clear();
        _currentPoint = null;
        _subpathStart = null;
    }
}
