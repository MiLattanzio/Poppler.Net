namespace Poppler;

/// <summary>Safety limits for standalone PDF page extraction.</summary>
public sealed record PdfPageExtractionOptions
{
    /// <summary>Maximum number of objects, including the generated catalog and page tree.</summary>
    public int MaximumObjects { get; init; } = 100_000;

    /// <summary>Maximum direct-object and indirect-reference traversal depth.</summary>
    public int MaximumDepth { get; init; } = 64;

    /// <summary>Maximum cumulative stream bytes copied or decoded into one extracted PDF.</summary>
    public long MaximumStreamBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Maximum size of one extracted PDF.</summary>
    public long MaximumOutputBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Maximum pages retained by one <c>ExtractPages</c> call.</summary>
    public int MaximumPages { get; init; } = 10_000;

    /// <summary>
    /// Maximum cumulative bytes retained by one <c>ExtractPages</c> call.
    /// The remaining budget also bounds each writer before it grows.
    /// </summary>
    public long MaximumTotalOutputBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Preserve annotations that can remain valid on the isolated page.</summary>
    public bool PreserveAnnotations { get; init; } = true;

    internal PdfPageExtractionOptions Snapshot()
    {
        if (MaximumObjects < 3)
            throw new ArgumentOutOfRangeException(nameof(MaximumObjects));
        if (MaximumDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumDepth));
        if (MaximumStreamBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumStreamBytes));
        if (MaximumOutputBytes is < 64 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutputBytes));
        if (MaximumPages < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumPages));
        if (MaximumTotalOutputBytes is < 64 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumTotalOutputBytes));
        return this with { };
    }
}
