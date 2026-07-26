using System.Globalization;

namespace Poppler;

public readonly record struct PdfRectangle(double Left, double Bottom, double Right, double Top)
{
    public double Width => Math.Abs(Right - Left);
    public double Height => Math.Abs(Top - Bottom);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) =>
        x >= Math.Min(Left, Right) &&
        x <= Math.Max(Left, Right) &&
        y >= Math.Min(Bottom, Top) &&
        y <= Math.Max(Bottom, Top);

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"[{Left:0.###}, {Bottom:0.###}, {Right:0.###}, {Top:0.###}]");
}
