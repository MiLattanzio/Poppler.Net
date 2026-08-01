using System.Collections.ObjectModel;

namespace Poppler;

/// <summary>Immutable bookmark metadata and navigation target.</summary>
public sealed class PdfOutlineItem
{
    internal PdfOutlineItem(
        string title,
        IEnumerable<PdfOutlineItem> children,
        PdfDestination? destination,
        PdfAnnotationAction action,
        bool isOpen,
        bool isBold,
        bool isItalic,
        PdfColor? color)
    {
        Title = title;
        Children = new ReadOnlyCollection<PdfOutlineItem>(children.ToArray());
        Destination = destination;
        Action = action;
        IsOpen = isOpen;
        IsBold = isBold;
        IsItalic = isItalic;
        Color = color;
    }

    public string Title { get; }
    public IReadOnlyList<PdfOutlineItem> Children { get; }
    public PdfDestination? Destination { get; }
    public PdfAnnotationAction Action { get; }
    public bool IsOpen { get; }
    public bool IsBold { get; }
    public bool IsItalic { get; }
    public PdfColor? Color { get; }
}
