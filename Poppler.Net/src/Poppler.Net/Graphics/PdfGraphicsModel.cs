using System.Collections.ObjectModel;
using System.Globalization;

namespace Poppler;

/// <summary>A point in PDF user space.</summary>
public readonly record struct PdfPoint(double X, double Y);

/// <summary>A six-value affine transform using the PDF matrix convention.</summary>
public readonly record struct PdfMatrix(
    double A,
    double B,
    double C,
    double D,
    double E,
    double F)
{
    public static PdfMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsFinite =>
        double.IsFinite(A) &&
        double.IsFinite(B) &&
        double.IsFinite(C) &&
        double.IsFinite(D) &&
        double.IsFinite(E) &&
        double.IsFinite(F);

    public PdfPoint Transform(double x, double y) =>
        new(A * x + C * y + E, B * x + D * y + F);

    public PdfMatrix Multiply(PdfMatrix other) => new(
        A * other.A + B * other.C,
        A * other.B + B * other.D,
        C * other.A + D * other.C,
        C * other.B + D * other.D,
        E * other.A + F * other.C + other.E,
        E * other.B + F * other.D + other.F);

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"[{A:0.###} {B:0.###} {C:0.###} {D:0.###} {E:0.###} {F:0.###}]");
}

public enum PdfColorSpace
{
    Unknown,
    DeviceGray,
    DeviceRgb,
    DeviceCmyk,
    Pattern
}

/// <summary>
/// A color in one of the device color spaces supported by the 0.5 graphics
/// slice. Calibrated and ICC color conversion is intentionally deferred.
/// </summary>
public readonly record struct PdfColor(
    PdfColorSpace Space,
    double Component1,
    double Component2,
    double Component3,
    double Component4)
{
    public static PdfColor Black { get; } = Gray(0);

    public static PdfColor Gray(double gray) =>
        new(PdfColorSpace.DeviceGray, Clamp(gray), 0, 0, 0);

    public static PdfColor Rgb(double red, double green, double blue) =>
        new(
            PdfColorSpace.DeviceRgb,
            Clamp(red),
            Clamp(green),
            Clamp(blue),
            0);

    public static PdfColor Cmyk(double cyan, double magenta, double yellow, double black) =>
        new(
            PdfColorSpace.DeviceCmyk,
            Clamp(cyan),
            Clamp(magenta),
            Clamp(yellow),
            Clamp(black));

    public (double Red, double Green, double Blue) ToRgb() => Space switch
    {
        PdfColorSpace.DeviceGray =>
            (Component1, Component1, Component1),
        PdfColorSpace.DeviceRgb =>
            (Component1, Component2, Component3),
        PdfColorSpace.DeviceCmyk =>
            (
                1 - Math.Min(1, Component1 * (1 - Component4) + Component4),
                1 - Math.Min(1, Component2 * (1 - Component4) + Component4),
                1 - Math.Min(1, Component3 * (1 - Component4) + Component4)),
        _ => (0, 0, 0)
    };

    private static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

public enum PdfLineCap
{
    Butt,
    Round,
    Square
}

public enum PdfLineJoin
{
    Miter,
    Round,
    Bevel
}

public enum PdfFillRule
{
    NonZero,
    EvenOdd
}

[Flags]
public enum PdfPaintMode
{
    None = 0,
    Fill = 1,
    Stroke = 2
}

public sealed record PdfDashPattern
{
    public static PdfDashPattern Solid { get; } = new(Array.Empty<double>(), 0);

    public PdfDashPattern(IEnumerable<double> segments, double phase)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = Array.AsReadOnly(segments.Select(value => Math.Max(0, value)).ToArray());
        Phase = double.IsFinite(phase) ? phase : 0;
    }

    public IReadOnlyList<double> Segments { get; }
    public double Phase { get; }
}

public abstract record PdfBrush
{
}

public sealed record PdfSolidBrush(PdfColor Color) : PdfBrush;

public enum PdfShadingKind
{
    Axial,
    Radial
}

public sealed record PdfGradientStop(double Offset, PdfColor Color);

public sealed record PdfGradientBrush : PdfBrush
{
    public PdfGradientBrush(
        PdfShadingKind kind,
        IEnumerable<double> coordinates,
        IEnumerable<PdfGradientStop> stops,
        bool extendStart,
        bool extendEnd,
        PdfMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(stops);
        Kind = kind;
        Coordinates = Array.AsReadOnly(coordinates.ToArray());
        Stops = Array.AsReadOnly(stops.ToArray());
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
        Matrix = matrix;
    }

    public PdfShadingKind Kind { get; }
    public IReadOnlyList<double> Coordinates { get; }
    public IReadOnlyList<PdfGradientStop> Stops { get; }
    public bool ExtendStart { get; }
    public bool ExtendEnd { get; }
    public PdfMatrix Matrix { get; }
}

public abstract record PdfPathSegment
{
}

public sealed record PdfMoveTo(PdfPoint Point) : PdfPathSegment;

public sealed record PdfLineTo(PdfPoint Point) : PdfPathSegment;

public sealed record PdfCubicBezierTo(
    PdfPoint Control1,
    PdfPoint Control2,
    PdfPoint End) : PdfPathSegment;

public sealed record PdfClosePath : PdfPathSegment
{
}

public sealed class PdfGraphicsPath
{
    internal PdfGraphicsPath(IEnumerable<PdfPathSegment> segments)
    {
        SegmentList = segments.ToArray();
        Segments = Array.AsReadOnly(SegmentList);
    }

    internal PdfPathSegment[] SegmentList { get; }
    public IReadOnlyList<PdfPathSegment> Segments { get; }
    public bool IsEmpty => Segments.Count == 0;
}

public sealed record PdfClipPath(
    PdfGraphicsPath Path,
    PdfMatrix Transform,
    PdfFillRule FillRule);

public sealed record PdfGraphicsState
{
    public PdfMatrix Transform { get; init; } = PdfMatrix.Identity;
    public PdfBrush Fill { get; init; } = new PdfSolidBrush(PdfColor.Black);
    public PdfBrush Stroke { get; init; } = new PdfSolidBrush(PdfColor.Black);
    public double LineWidth { get; init; } = 1;
    public PdfLineCap LineCap { get; init; }
    public PdfLineJoin LineJoin { get; init; }
    public double MiterLimit { get; init; } = 10;
    public PdfDashPattern Dash { get; init; } = PdfDashPattern.Solid;
    public double FillAlpha { get; init; } = 1;
    public double StrokeAlpha { get; init; } = 1;
    public string BlendMode { get; init; } = "Normal";
}

public abstract record PdfGraphicsElement(
    PdfGraphicsState State,
    IReadOnlyList<PdfClipPath> ClipPaths,
    string? SourceResource);

public sealed record PdfPathElement(
    PdfGraphicsPath Path,
    PdfPaintMode PaintMode,
    PdfFillRule FillRule,
    PdfGraphicsState State,
    IReadOnlyList<PdfClipPath> ClipPaths,
    string? SourceResource = null)
    : PdfGraphicsElement(State, ClipPaths, SourceResource);

public sealed record PdfImageElement(
    string ResourceName,
    int Width,
    int Height,
    int BitsPerComponent,
    string ColorSpace,
    bool IsImageMask,
    PdfGraphicsState State,
    IReadOnlyList<PdfClipPath> ClipPaths,
    string? SourceResource = null)
    : PdfGraphicsElement(State, ClipPaths, SourceResource);

public sealed record PdfShadingElement(
    string ResourceName,
    PdfGradientBrush Shading,
    PdfGraphicsState State,
    IReadOnlyList<PdfClipPath> ClipPaths,
    string? SourceResource = null)
    : PdfGraphicsElement(State, ClipPaths, SourceResource);

public sealed record PdfTilingPatternBrush : PdfBrush
{
    public PdfTilingPatternBrush(
        string resourceName,
        PdfRectangle boundingBox,
        double xStep,
        double yStep,
        PdfMatrix matrix,
        IEnumerable<PdfGraphicsElement> elements)
    {
        ResourceName = resourceName;
        BoundingBox = boundingBox;
        XStep = xStep;
        YStep = yStep;
        Matrix = matrix;
        Elements = new ReadOnlyCollection<PdfGraphicsElement>(elements.ToArray());
    }

    public string ResourceName { get; }
    public PdfRectangle BoundingBox { get; }
    public double XStep { get; }
    public double YStep { get; }
    public PdfMatrix Matrix { get; }
    public IReadOnlyList<PdfGraphicsElement> Elements { get; }
}
