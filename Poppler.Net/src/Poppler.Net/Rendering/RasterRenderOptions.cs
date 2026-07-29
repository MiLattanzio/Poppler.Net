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
    /// Rasterize text elements in their exact position inside the graphics
    /// display list. Embedded TrueType, CFF and Type 1 outlines are interpreted
    /// by managed readers; Type 3 character procedures are handled by the
    /// graphics interpreter.
    /// </summary>
    public bool IncludeText { get; init; } = true;

    /// <summary>
    /// Retained for source compatibility with 0.7 alpha callers. Text now
    /// uses the fill and stroke brushes captured in its graphics state.
    /// </summary>
    public PdfColor TextColor { get; init; } = PdfColor.Black;

    /// <summary>
    /// Resolve missing Base-14 and other non-embedded fonts from managed font
    /// files. No platform font API or native rasterizer is used.
    /// </summary>
    public bool UseFontSubstitution { get; init; } = true;

    /// <summary>
    /// Optional font roots searched before standard operating-system font
    /// folders. Files are parsed by the managed TrueType/CFF readers.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } =
        Array.Empty<string>();

    /// <summary>
    /// Per-layer visibility overrides keyed by
    /// <see cref="PdfOptionalContentGroup.Id"/>.
    /// </summary>
    public IReadOnlyDictionary<string, bool> OptionalContentVisibility { get; init; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    internal RasterRenderOptions Snapshot()
    {
        Validate();
        return this with
        {
            FontDirectories = Array.AsReadOnly(FontDirectories.ToArray()),
            OptionalContentVisibility =
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, bool>(
                    new Dictionary<string, bool>(
                        OptionalContentVisibility,
                        StringComparer.Ordinal))
        };
    }

    internal void Validate()
    {
        if (!double.IsFinite(Dpi) || Dpi is < 1 or > 2400)
            throw new ArgumentOutOfRangeException(nameof(Dpi));
        if (Antialiasing is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(Antialiasing));
        if (FontDirectories is null ||
            FontDirectories.Any(directory => directory is null))
        {
            throw new ArgumentNullException(nameof(FontDirectories));
        }
        if (OptionalContentVisibility is null)
            throw new ArgumentNullException(nameof(OptionalContentVisibility));
        if (OptionalContentVisibility.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Optional-content group identifiers cannot be empty.",
                nameof(OptionalContentVisibility));
        }
    }
}
