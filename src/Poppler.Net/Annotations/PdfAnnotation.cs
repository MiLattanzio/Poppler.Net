using System.Collections.ObjectModel;

namespace Poppler;

public enum PdfAnnotationType
{
    Unknown,
    Link,
    Text,
    FreeText,
    Highlight,
    Underline,
    Squiggly,
    StrikeOut,
    Square,
    Circle,
    Line,
    Polygon,
    PolyLine,
    Ink,
    Stamp
}

[Flags]
public enum PdfAnnotationFlags
{
    None = 0,
    Invisible = 1 << 0,
    Hidden = 1 << 1,
    Print = 1 << 2,
    NoZoom = 1 << 3,
    NoRotate = 1 << 4,
    NoView = 1 << 5,
    ReadOnly = 1 << 6,
    Locked = 1 << 7,
    ToggleNoView = 1 << 8,
    LockedContents = 1 << 9
}

public enum PdfAnnotationBorderStyleKind
{
    Solid,
    Dashed,
    Beveled,
    Inset,
    Underline
}

public sealed class PdfAnnotationBorderStyle
{
    internal PdfAnnotationBorderStyle(
        double width,
        double horizontalRadius,
        double verticalRadius,
        PdfAnnotationBorderStyleKind style,
        IEnumerable<double> dashPattern)
    {
        Width = width;
        HorizontalRadius = horizontalRadius;
        VerticalRadius = verticalRadius;
        Style = style;
        DashPattern = new ReadOnlyCollection<double>(dashPattern.ToArray());
    }

    public double Width { get; }
    public double HorizontalRadius { get; }
    public double VerticalRadius { get; }
    public PdfAnnotationBorderStyleKind Style { get; }
    public IReadOnlyList<double> DashPattern { get; }
}

public enum PdfDestinationType
{
    Unknown,
    Xyz,
    Fit,
    FitHorizontal,
    FitVertical,
    FitRectangle,
    FitBoundingBox,
    FitBoundingBoxHorizontal,
    FitBoundingBoxVertical
}

public sealed class PdfDestination
{
    internal PdfDestination(
        int pageIndex,
        PdfDestinationType type,
        double? left,
        double? top,
        double? right,
        double? bottom,
        double? zoom,
        string? namedDestination)
    {
        PageIndex = pageIndex;
        Type = type;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        Zoom = zoom;
        NamedDestination = namedDestination;
    }

    public int PageIndex { get; }
    public int PageNumber => PageIndex + 1;
    public PdfDestinationType Type { get; }
    public double? Left { get; }
    public double? Top { get; }
    public double? Right { get; }
    public double? Bottom { get; }
    public double? Zoom { get; }
    public string? NamedDestination { get; }
}

public enum PdfAnnotationActionType
{
    None,
    GoTo,
    Uri,
    Named,
    Unsupported
}

public sealed class PdfAnnotationAction
{
    internal PdfAnnotationAction(
        PdfAnnotationActionType type,
        string? uri,
        PdfDestination? destination,
        string? namedTarget)
    {
        Type = type;
        Uri = uri;
        Destination = destination;
        NamedTarget = namedTarget;
    }

    public PdfAnnotationActionType Type { get; }
    public string? Uri { get; }
    public PdfDestination? Destination { get; }
    public string? NamedTarget { get; }
}

/// <summary>Immutable page annotation metadata and resolved link target.</summary>
public sealed class PdfAnnotation
{
    internal PdfAnnotation(
        PdfAnnotationType type,
        string subtype,
        PdfRectangle rectangle,
        string contents,
        string name,
        string title,
        string subject,
        string iconName,
        DateTimeOffset? modificationDate,
        PdfAnnotationFlags flags,
        PdfColor? color,
        PdfColor? interiorColor,
        double opacity,
        PdfAnnotationBorderStyle border,
        IEnumerable<PdfPoint> quadPoints,
        IEnumerable<PdfPoint> vertices,
        IEnumerable<PdfPoint> linePoints,
        IEnumerable<IEnumerable<PdfPoint>> inkPaths,
        PdfAnnotationAction action,
        bool hasAppearance)
    {
        Type = type;
        Subtype = subtype;
        Rectangle = rectangle;
        Contents = contents;
        Name = name;
        Title = title;
        Subject = subject;
        IconName = iconName;
        ModificationDate = modificationDate;
        Flags = flags;
        Color = color;
        InteriorColor = interiorColor;
        Opacity = opacity;
        Border = border;
        QuadPoints = new ReadOnlyCollection<PdfPoint>(quadPoints.ToArray());
        Vertices = new ReadOnlyCollection<PdfPoint>(vertices.ToArray());
        LinePoints = new ReadOnlyCollection<PdfPoint>(linePoints.ToArray());
        InkPaths = new ReadOnlyCollection<IReadOnlyList<PdfPoint>>(
            inkPaths
                .Select(path =>
                    (IReadOnlyList<PdfPoint>)new ReadOnlyCollection<PdfPoint>(
                        path.ToArray()))
                .ToArray());
        Action = action;
        HasAppearance = hasAppearance;
    }

    public PdfAnnotationType Type { get; }
    public string Subtype { get; }
    public PdfRectangle Rectangle { get; }
    public string Contents { get; }
    public string Name { get; }
    public string Title { get; }
    public string Subject { get; }
    public string IconName { get; }
    public DateTimeOffset? ModificationDate { get; }
    public PdfAnnotationFlags Flags { get; }
    public PdfColor? Color { get; }
    public PdfColor? InteriorColor { get; }
    public double Opacity { get; }
    public PdfAnnotationBorderStyle Border { get; }
    public IReadOnlyList<PdfPoint> QuadPoints { get; }
    public IReadOnlyList<PdfPoint> Vertices { get; }
    public IReadOnlyList<PdfPoint> LinePoints { get; }
    public IReadOnlyList<IReadOnlyList<PdfPoint>> InkPaths { get; }
    public PdfAnnotationAction Action { get; }
    public bool HasAppearance { get; }
}
