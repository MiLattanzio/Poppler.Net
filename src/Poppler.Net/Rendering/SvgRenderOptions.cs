namespace Poppler.Rendering;

/// <summary>Controls unsupported SVG graphics constructs.</summary>
public enum SvgFallbackMode
{
    /// <summary>Embed a deterministic managed PNG fallback.</summary>
    Rasterize,
    /// <summary>Explicitly omit unsupported graphics.</summary>
    Omit
}

public sealed record SvgRenderOptions
{
    public double Scale { get; init; } = 1;
    public string Background { get; init; } = "#ffffff";
    public string Foreground { get; init; } = "#111111";
    public bool IncludeVectorGraphics { get; init; } = true;
    public bool IncludeImages { get; init; } = true;
    public bool IncludeText { get; init; } = true;
    public bool DrawTextBounds { get; init; }
    public bool DrawImageBounds { get; init; }
    /// <summary>
    /// Policy for transparency groups and other graphics that cannot be
    /// represented with equivalent SVG compositing semantics.
    /// </summary>
    public SvgFallbackMode FallbackMode { get; init; } = SvgFallbackMode.Rasterize;
    /// <summary>Resolution of an embedded raster fallback.</summary>
    public double RasterFallbackDpi { get; init; } = 144;

    /// <summary>
    /// Per-layer visibility overrides keyed by
    /// <see cref="PdfOptionalContentGroup.Id"/>.
    /// </summary>
    public IReadOnlyDictionary<string, bool> OptionalContentVisibility { get; init; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    internal SvgRenderOptions Snapshot()
    {
        if (!double.IsFinite(Scale) || Scale <= 0 || Scale > 100)
            throw new ArgumentOutOfRangeException(nameof(Scale));
        if (!Enum.IsDefined(FallbackMode))
            throw new ArgumentOutOfRangeException(nameof(FallbackMode));
        if (!double.IsFinite(RasterFallbackDpi) ||
            RasterFallbackDpi is < 1 or > 2400)
        {
            throw new ArgumentOutOfRangeException(nameof(RasterFallbackDpi));
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
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, bool>(
                    new Dictionary<string, bool>(
                        OptionalContentVisibility,
                        StringComparer.Ordinal))
        };
    }
}
