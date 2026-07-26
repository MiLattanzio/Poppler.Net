namespace Poppler.Text;

/// <summary>
/// Internal bridge between the content interpreter and raster backend. The
/// matrix maps normalized font outline coordinates directly into PDF page user
/// space before the page-to-device transform is applied.
/// </summary>
internal sealed record PdfTextGlyphPlacement(
    PdfDecodedGlyph Glyph,
    global::Poppler.PdfMatrix Transform);
