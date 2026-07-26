using Poppler.Text;

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

    // Keep the source PDF glyph codes inside the assembly. Rasterization must
    // not reconstruct glyph IDs from extracted Unicode: subset fonts can use
    // arbitrary character codes and may map one glyph to multiple scalars.
    internal IReadOnlyList<PdfDecodedGlyph> DecodedGlyphs { get; init; } =
        Array.Empty<PdfDecodedGlyph>();

    // Null means that no explicit non-stroking color operator preceded the
    // run, so RasterRenderOptions.TextColor remains the fallback.
    internal PdfColor? FillColor { get; init; }
}
