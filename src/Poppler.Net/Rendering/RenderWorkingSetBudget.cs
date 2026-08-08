namespace Poppler.Rendering;

/// <summary>
/// Per-render accounting for high-precision raster surfaces. Reservations are
/// made before array allocation and released when a temporary surface dies.
/// </summary>
internal sealed class RenderWorkingSetBudget
{
    private readonly long _maximumBytes;
    private long _currentBytes;

    public RenderWorkingSetBudget(long maximumBytes)
    {
        if (maximumBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
    }

    public void Reserve(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        long requested;
        try
        {
            requested = checked(_currentBytes + bytes);
        }
        catch (OverflowException)
        {
            throw LimitExceeded();
        }
        if (requested > _maximumBytes)
            throw LimitExceeded();
        _currentBytes = requested;
    }

    public void Release(long bytes)
    {
        if (bytes < 0 || bytes > _currentBytes)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        _currentBytes -= bytes;
    }

    private PdfLimitException LimitExceeded() => new(
        $"Raster working surfaces exceed the configured {_maximumBytes}-byte limit.");
}
