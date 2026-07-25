namespace Poppler;

/// <summary>Resource and recovery controls used while reading untrusted PDFs.</summary>
public sealed record PdfReadOptions
{
    public static PdfReadOptions Default { get; } = new();

    public long MaximumInputBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumDecodedStreamBytes { get; init; } = 256 * 1024 * 1024;
    public int MaximumObjects { get; init; } = 1_000_000;
    public int MaximumCollectionItems { get; init; } = 1_000_000;
    public int MaximumPages { get; init; } = 10_000;
    public int MaximumObjectDepth { get; init; } = 64;
    public int MaximumTreeDepth { get; init; } = 128;
    public bool AttemptXrefRepair { get; init; } = true;

    internal void Validate()
    {
        if (MaximumInputBytes < 8)
            throw new ArgumentOutOfRangeException(nameof(MaximumInputBytes));
        if (MaximumDecodedStreamBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumDecodedStreamBytes));
        if (MaximumObjects < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumObjects));
        if (MaximumCollectionItems < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumCollectionItems));
        if (MaximumPages < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumPages));
        if (MaximumObjectDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumObjectDepth));
        if (MaximumTreeDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumTreeDepth));
    }
}
