namespace Poppler;

public sealed record TextBox(
    string Text,
    PdfRectangle BoundingBox,
    int Rotation,
    bool HasSpaceAfter,
    string FontName,
    double FontSize);
