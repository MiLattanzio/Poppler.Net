namespace Poppler;

/// <summary>PDF font technology identified from the font and descriptor dictionaries.</summary>
public enum PdfFontType
{
    Unknown,
    Type1,
    Type1C,
    Type3,
    TrueType,
    OpenType,
    CidType0,
    CidType2
}

/// <summary>Container used by an embedded font program.</summary>
public enum EmbeddedFontFormat
{
    None,
    Type1,
    Cff,
    TrueType,
    OpenType
}

/// <summary>Direction in which glyph advances are applied in PDF text space.</summary>
public enum FontWritingMode
{
    Horizontal,
    Vertical
}

/// <summary>Read-only information about a font resource used by a page.</summary>
public sealed record FontInfo(
    string ResourceName,
    string Name,
    PdfFontType Type,
    string Encoding,
    FontWritingMode WritingMode,
    bool IsEmbedded,
    EmbeddedFontFormat EmbeddedFormat,
    int EmbeddedLength,
    bool IsSubset,
    bool HasToUnicode,
    string? Collection)
{
    internal ReadOnlyMemory<byte> GetEmbeddedData() => EmbeddedFontProgramStore.Get(this);
}

internal static class EmbeddedFontProgramStore
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FontInfo, ProgramData> Programs = new();

    public static void Set(FontInfo font, byte[] data) => Programs.Add(font, new ProgramData(data));

    public static ReadOnlyMemory<byte> Get(FontInfo font) =>
        Programs.TryGetValue(font, out ProgramData? data)
            ? data.Bytes
            : ReadOnlyMemory<byte>.Empty;

    private sealed class ProgramData(byte[] bytes)
    {
        public ReadOnlyMemory<byte> Bytes { get; } = bytes;
    }
}
