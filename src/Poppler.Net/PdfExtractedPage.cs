namespace Poppler;

/// <summary>A standalone PDF produced from one source page.</summary>
public sealed class PdfExtractedPage
{
    private readonly byte[] _data;

    internal PdfExtractedPage(int sourcePageIndex, byte[] data)
    {
        SourcePageIndex = sourcePageIndex;
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Zero-based page index in the source document.</summary>
    public int SourcePageIndex { get; }

    /// <summary>One-based page number in the source document.</summary>
    public int SourcePageNumber => SourcePageIndex + 1;

    /// <summary>Complete bytes of the autonomous, unencrypted PDF.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Saves the extracted PDF to a file.</summary>
    public void SaveTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, _data);
    }
}
