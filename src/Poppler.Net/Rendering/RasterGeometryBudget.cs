namespace Poppler.Rendering;

/// <summary>
/// Per-render geometry budget. Calls are deliberately made before growing
/// temporary geometry collections so hostile paths fail before large
/// allocations are committed.
/// </summary>
internal sealed class RasterGeometryBudget
{
    private readonly int _maximum;
    private int _used;

    public RasterGeometryBudget(int maximum) => _maximum = maximum;

    public void Consume(int count, string kind)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return;
        if (count > _maximum - _used)
        {
            throw new PdfLimitException(
                $"Raster geometry exceeds the configured {_maximum}-segment " +
                $"limit while producing {kind}.");
        }

        _used += count;
    }
}
