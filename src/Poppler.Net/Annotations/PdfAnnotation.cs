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
    Stamp,
    Widget,
    Caret,
    Popup,
    FileAttachment,
    Sound,
    Movie,
    Screen,
    PrinterMark,
    TrapNet,
    Watermark,
    ThreeD,
    Redact
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
    GoToRemote,
    Launch,
    JavaScript,
    SubmitForm,
    ResetForm,
    ImportData,
    Hide,
    SetOptionalContentState,
    Rendition,
    Transition,
    GoToThreeDView,
    Unsupported
}

public sealed class PdfAnnotationAction
{
    internal PdfAnnotationAction(
        PdfAnnotationActionType type,
        string? uri,
        PdfDestination? destination,
        string? namedTarget,
        string? fileName = null,
        bool? newWindow = null,
        string? script = null,
        int flags = 0,
        bool? isHidden = null,
        IEnumerable<string>? fields = null,
        IEnumerable<string>? stateChanges = null,
        IEnumerable<PdfAnnotationAction>? nextActions = null)
    {
        Type = type;
        Uri = uri;
        Destination = destination;
        NamedTarget = namedTarget;
        FileName = fileName;
        NewWindow = newWindow;
        Script = script;
        Flags = flags;
        IsHidden = isHidden;
        Fields = new ReadOnlyCollection<string>((fields ?? []).ToArray());
        StateChanges = new ReadOnlyCollection<string>((stateChanges ?? []).ToArray());
        NextActions = new ReadOnlyCollection<PdfAnnotationAction>(
            (nextActions ?? []).ToArray());
    }

    public PdfAnnotationActionType Type { get; }
    public string? Uri { get; }
    public PdfDestination? Destination { get; }
    public string? NamedTarget { get; }
    public string? FileName { get; }
    public bool? NewWindow { get; }
    public string? Script { get; }
    public int Flags { get; }
    public bool? IsHidden { get; }
    public IReadOnlyList<string> Fields { get; }
    public IReadOnlyList<string> StateChanges { get; }
    public IReadOnlyList<PdfAnnotationAction> NextActions { get; }
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
        string id,
        string parentId,
        string popupId,
        string replyType,
        string state,
        string stateModel,
        string intent,
        bool isOpen,
        string richText,
        string defaultStyle,
        PdfAnnotationFlags flags,
        PdfColor? color,
        PdfColor? interiorColor,
        double opacity,
        PdfAnnotationBorderStyle border,
        IEnumerable<PdfPoint> quadPoints,
        IEnumerable<PdfPoint> vertices,
        IEnumerable<PdfPoint> linePoints,
        IEnumerable<PdfPoint> calloutLine,
        IEnumerable<IEnumerable<PdfPoint>> inkPaths,
        IEnumerable<string> lineEndingStyles,
        IEnumerable<double> rectangleDifferences,
        EmbeddedFile? attachment,
        PdfAnnotationAction action,
        bool hasAppearance,
        bool isVisible)
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
        Id = id;
        ParentId = parentId;
        PopupId = popupId;
        ReplyType = replyType;
        State = state;
        StateModel = stateModel;
        Intent = intent;
        IsOpen = isOpen;
        RichText = richText;
        DefaultStyle = defaultStyle;
        Flags = flags;
        Color = color;
        InteriorColor = interiorColor;
        Opacity = opacity;
        Border = border;
        QuadPoints = new ReadOnlyCollection<PdfPoint>(quadPoints.ToArray());
        Vertices = new ReadOnlyCollection<PdfPoint>(vertices.ToArray());
        LinePoints = new ReadOnlyCollection<PdfPoint>(linePoints.ToArray());
        CalloutLine = new ReadOnlyCollection<PdfPoint>(calloutLine.ToArray());
        InkPaths = new ReadOnlyCollection<IReadOnlyList<PdfPoint>>(
            inkPaths
                .Select(path =>
                    (IReadOnlyList<PdfPoint>)new ReadOnlyCollection<PdfPoint>(
                        path.ToArray()))
                .ToArray());
        LineEndingStyles = new ReadOnlyCollection<string>(
            lineEndingStyles.ToArray());
        RectangleDifferences = new ReadOnlyCollection<double>(
            rectangleDifferences.ToArray());
        Attachment = attachment;
        Action = action;
        HasAppearance = hasAppearance;
        IsVisible = isVisible;
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
    public string Id { get; }
    public string ParentId { get; }
    public string PopupId { get; }
    public string ReplyType { get; }
    public string State { get; }
    public string StateModel { get; }
    public string Intent { get; }
    public bool IsOpen { get; }
    public string RichText { get; }
    public string DefaultStyle { get; }
    public PdfAnnotationFlags Flags { get; }
    public PdfColor? Color { get; }
    public PdfColor? InteriorColor { get; }
    public double Opacity { get; }
    public PdfAnnotationBorderStyle Border { get; }
    public IReadOnlyList<PdfPoint> QuadPoints { get; }
    public IReadOnlyList<PdfPoint> Vertices { get; }
    public IReadOnlyList<PdfPoint> LinePoints { get; }
    public IReadOnlyList<PdfPoint> CalloutLine { get; }
    public IReadOnlyList<IReadOnlyList<PdfPoint>> InkPaths { get; }
    public IReadOnlyList<string> LineEndingStyles { get; }
    public IReadOnlyList<double> RectangleDifferences { get; }
    public EmbeddedFile? Attachment { get; }
    public PdfAnnotationAction Action { get; }
    public bool HasAppearance { get; }
    /// <summary>Visibility under flags and the default optional-content configuration.</summary>
    public bool IsVisible { get; }
}
