using System.Collections.ObjectModel;

namespace Poppler.Rendering;

/// <summary>Controls how selectable PDF text is represented in HTML.</summary>
public enum HtmlTextLayerMode
{
    /// <summary>
    /// Keep the exact managed SVG rendering visible and place transparent,
    /// selectable text above it.
    /// </summary>
    InvisibleOverlay,

    /// <summary>
    /// Reconstruct supported glyphs as positioned HTML using managed web fonts.
    /// Text that needs PDF compositing remains in the graphical background.
    /// </summary>
    Visible
}

/// <summary>Controls fixed-layout HTML rendering of a PDF page.</summary>
public sealed record HtmlRenderOptions
{
    /// <summary>CSS pixels per PDF point.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Page background used by the managed SVG renderer.</summary>
    public string Background { get; init; } = "#ffffff";

    /// <summary>Fallback browser text color used when PDF paint cannot be recovered.</summary>
    public string Foreground { get; init; } = "#111111";

    /// <summary>Determines whether glyphs use the native HTML reconstruction pipeline.</summary>
    public HtmlTextLayerMode TextLayerMode { get; init; } =
        HtmlTextLayerMode.Visible;

    /// <summary>
    /// Ordering used by the compatibility overlay. Native glyphs retain PDF
    /// paint order so visual stacking remains deterministic.
    /// </summary>
    public TextLayout TextLayout { get; init; } = TextLayout.Physical;

    /// <summary>Include supported vector graphics in the page background.</summary>
    public bool IncludeVectorGraphics { get; init; } = true;

    /// <summary>Include decoded PDF images in the page background.</summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>
    /// Generate reusable TrueType web fonts from the outlines decoded by the
    /// managed PDF font pipeline. Missing outlines retain CSS fallbacks.
    /// </summary>
    public bool EmbedFonts { get; init; } = true;

    /// <summary>Policy for graphics that cannot be represented faithfully in SVG.</summary>
    public SvgFallbackMode FallbackMode { get; init; } = SvgFallbackMode.Rasterize;

    /// <summary>Resolution of bounded raster regions embedded in the SVG background.</summary>
    public double RasterFallbackDpi { get; init; } = 144;

    /// <summary>Maximum UTF-8 bytes in one HTML result or complete bundle.</summary>
    public long MaximumOutputBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Maximum unique reusable font bytes embedded in one export.</summary>
    public long MaximumEmbeddedFontBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Per-layer visibility overrides keyed by optional-content group id.</summary>
    public IReadOnlyDictionary<string, bool> OptionalContentVisibility { get; init; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    internal HtmlRenderOptions Snapshot()
    {
        if (!double.IsFinite(Scale) || Scale is < 0.1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(Scale));
        if (string.IsNullOrWhiteSpace(Background))
            throw new ArgumentException("HTML background cannot be empty.", nameof(Background));
        if (string.IsNullOrWhiteSpace(Foreground))
            throw new ArgumentException("HTML foreground cannot be empty.", nameof(Foreground));
        ValidateCssColor(Background, nameof(Background));
        ValidateCssColor(Foreground, nameof(Foreground));
        if (!Enum.IsDefined(TextLayerMode))
            throw new ArgumentOutOfRangeException(nameof(TextLayerMode));
        if (!Enum.IsDefined(TextLayout))
            throw new ArgumentOutOfRangeException(nameof(TextLayout));
        if (!Enum.IsDefined(FallbackMode))
            throw new ArgumentOutOfRangeException(nameof(FallbackMode));
        if (!double.IsFinite(RasterFallbackDpi) ||
            RasterFallbackDpi is < 1 or > 2400)
        {
            throw new ArgumentOutOfRangeException(nameof(RasterFallbackDpi));
        }
        if (MaximumOutputBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutputBytes));
        if (MaximumEmbeddedFontBytes < 0 ||
            MaximumEmbeddedFontBytes > MaximumOutputBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumEmbeddedFontBytes));
        }
        if (OptionalContentVisibility is null)
            throw new ArgumentNullException(nameof(OptionalContentVisibility));
        if (OptionalContentVisibility.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Optional-content group identifiers cannot be empty.",
                nameof(OptionalContentVisibility));
        }

        return this with
        {
            OptionalContentVisibility =
                new ReadOnlyDictionary<string, bool>(
                    new Dictionary<string, bool>(
                        OptionalContentVisibility,
                        StringComparer.Ordinal))
        };
    }

    private static void ValidateCssColor(string value, string parameter)
    {
        if (value.IndexOfAny([';', '{', '}', '<', '>', '\r', '\n']) >= 0 ||
            value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("@import", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/*", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "HTML colors cannot contain CSS statements, blocks, comments, or URLs.",
                parameter);
        }
    }
}

/// <summary>Controls document selection and metadata for HTML export.</summary>
public sealed record HtmlExportOptions
{
    /// <summary>Zero-based index of the first page to export.</summary>
    public int FirstPageIndex { get; init; }

    /// <summary>
    /// Number of pages to export, or <see langword="null"/> for every page
    /// from <see cref="FirstPageIndex"/> to the end of the document.
    /// </summary>
    public int? PageCount { get; init; }

    /// <summary>HTML document title. The PDF title is used when this is empty.</summary>
    public string? Title { get; init; }

    /// <summary>Page rendering options shared by every selected page.</summary>
    public HtmlRenderOptions PageOptions { get; init; } = new();

    internal (HtmlExportOptions Options, int Count) Snapshot(int totalPages)
    {
        if (totalPages <= 0)
            throw new InvalidOperationException("The PDF contains no pages.");
        if ((uint)FirstPageIndex >= (uint)totalPages)
            throw new ArgumentOutOfRangeException(nameof(FirstPageIndex));
        if (PageCount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(PageCount));

        int count = PageCount ?? totalPages - FirstPageIndex;
        if (count > totalPages - FirstPageIndex)
            throw new ArgumentOutOfRangeException(nameof(PageCount));
        if (PageOptions is null)
            throw new ArgumentNullException(nameof(PageOptions));

        return (this with { PageOptions = PageOptions.Snapshot() }, count);
    }
}
