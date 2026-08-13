namespace Poppler;

/// <summary>Controls deterministic structured document and page exports.</summary>
public sealed record StructuredExportOptions
{
    /// <summary>Zero-based first page included in a document export.</summary>
    public int FirstPageIndex { get; init; }

    /// <summary>Number of pages to export; null includes every remaining page.</summary>
    public int? PageCount { get; init; }

    /// <summary>Ordering used for text boxes and joined page text.</summary>
    public TextLayout TextLayout { get; init; } = TextLayout.Physical;

    /// <summary>Include image metadata and image files in bundle exports.</summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>
    /// Prefer independently reusable JPEG, JPEG 2000, or JBIG2 source payloads.
    /// PNG is used whenever PDF-only masks, color transforms, globals, or
    /// filters are required.
    /// </summary>
    public bool PreferOriginalImages { get; init; } = true;

    /// <summary>Maximum files produced by one structured bundle.</summary>
    public int MaximumFiles { get; init; } = 10_000;

    /// <summary>Maximum cumulative bytes produced by one structured bundle.</summary>
    public long MaximumOutputBytes { get; init; } = 256L * 1024 * 1024;

    internal StructuredExportOptions Snapshot()
    {
        if (FirstPageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(FirstPageIndex));
        if (PageCount is < 1)
            throw new ArgumentOutOfRangeException(nameof(PageCount));
        if (!Enum.IsDefined(TextLayout))
            throw new ArgumentOutOfRangeException(nameof(TextLayout));
        if (MaximumFiles < 4)
            throw new ArgumentOutOfRangeException(nameof(MaximumFiles));
        if (MaximumOutputBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutputBytes));
        return this with { };
    }
}
