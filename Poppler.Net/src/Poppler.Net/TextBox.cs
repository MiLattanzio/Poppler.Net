namespace Poppler;

public sealed record TextBox(
    string Text,
    PdfRectangle BoundingBox,
    int Rotation,
    bool HasSpaceAfter,
    string FontName,
    double FontSize)
{
    public FontWritingMode WritingMode { get; init; }
    public bool IsRightToLeft { get; init; }
}
