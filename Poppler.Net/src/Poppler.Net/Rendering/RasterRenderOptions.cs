namespace Poppler.Rendering;

/// <summary>Controls managed raster rendering of a PDF page.</summary>
public sealed record RasterRenderOptions
{
    /// <summary>Resolution used to convert PDF points to pixels.</summary>
    public double Dpi { get; init; } = 96;

    /// <summary>Page boundary used for the output surface.</summary>
    public PageBox PageBox { get; init; } = PageBox.CropBox;

    /// <summary>
    /// Supersampling grid edge. Accepted values are 1, 2, 4 and 8; four
    /// produces sixteen coverage samples per edge pixel.
    /// </summary>
    public int Antialiasing { get; init; } = 4;

    /// <summary>Background color used when <see cref="Transparent"/> is false.</summary>
    public PdfColor Background { get; init; } = PdfColor.Rgb(1, 1, 1);

    /// <summary>Leave the initial surface transparent instead of opaque.</summary>
    public bool Transparent { get; init; }

    /// <summary>
    /// Rasterize embedded TrueType glyph outlines after the graphics display
    /// list. Unsupported font programs are left out rather than substituted by
    /// a platform-dependent native font.
    /// </summary>
    public bool IncludeText { get; init; } = true;

    /// <summary>Fallback color used by the first managed text-outline slice.</summary>
    public PdfColor TextColor { get; init; } = PdfColor.Black;

    internal void Validate()
    {
        if (!double.IsFinite(Dpi) || Dpi is < 1 or > 2400)
            throw new ArgumentOutOfRangeException(nameof(Dpi));
        if (Antialiasing is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(Antialiasing));
    }
}
