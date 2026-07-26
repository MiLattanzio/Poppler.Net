namespace Poppler.Text;

internal readonly record struct PdfDecodedGlyph(
    uint CharacterCode,
    uint Cid,
    int BytesConsumed,
    string Text,
    double AdvanceX,
    double AdvanceY,
    double OriginX,
    double OriginY,
    bool IsWordSpace);
