namespace Poppler;

/// <summary>Resource and recovery controls used while reading untrusted PDFs.</summary>
public sealed record PdfReadOptions
{
    public static PdfReadOptions Default { get; } = new();

    public long MaximumInputBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumDecodedStreamBytes { get; init; } = 256 * 1024 * 1024;
    public int MaximumObjects { get; init; } = 1_000_000;
    public int MaximumCollectionItems { get; init; } = 1_000_000;
    public int MaximumCMapMappings { get; init; } = 250_000;
    public int MaximumExternalCMapBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumCMapUseDepth { get; init; } = 16;
    public bool UseSystemCMaps { get; init; } = true;
    public IReadOnlyList<string> CMapDirectories { get; init; } =
        Array.Empty<string>();
    public int MaximumGraphicsOperations { get; init; } = 1_000_000;
    public int MaximumGraphicsElements { get; init; } = 250_000;
    public int MaximumPathSegments { get; init; } = 1_000_000;
    public int MaximumGraphicsStateDepth { get; init; } = 256;
    public int MaximumXObjectDepth { get; init; } = 32;
    public int MaximumTransparencyGroupDepth { get; init; } = 32;
    public int MaximumShadingStops { get; init; } = 33;
    public int MaximumMeshTriangles { get; init; } = 65_536;
    public long MaximumImagePixels { get; init; } = 100_000_000;
    public long MaximumRenderPixels { get; init; } = 100_000_000;
    public int MaximumImageComponents { get; init; } = 32;
    public int MaximumIccProfileBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumFunctionSamples { get; init; } = 1_000_000;
    public int MaximumPages { get; init; } = 10_000;
    public int MaximumObjectDepth { get; init; } = 64;
    public int MaximumTreeDepth { get; init; } = 128;
    public bool AttemptXrefRepair { get; init; } = true;

    internal PdfReadOptions Snapshot()
    {
        Validate();
        return this with
        {
            CMapDirectories = Array.AsReadOnly(CMapDirectories.ToArray())
        };
    }

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
        if (MaximumCMapMappings < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumCMapMappings));
        if (MaximumExternalCMapBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumExternalCMapBytes));
        if (MaximumCMapUseDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumCMapUseDepth));
        if (CMapDirectories is null ||
            CMapDirectories.Any(directory => directory is null))
        {
            throw new ArgumentNullException(nameof(CMapDirectories));
        }
        if (MaximumGraphicsOperations < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumGraphicsOperations));
        if (MaximumGraphicsElements < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumGraphicsElements));
        if (MaximumPathSegments < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumPathSegments));
        if (MaximumGraphicsStateDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumGraphicsStateDepth));
        if (MaximumXObjectDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumXObjectDepth));
        if (MaximumTransparencyGroupDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumTransparencyGroupDepth));
        if (MaximumShadingStops < 2)
            throw new ArgumentOutOfRangeException(nameof(MaximumShadingStops));
        if (MaximumMeshTriangles < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumMeshTriangles));
        if (MaximumImagePixels < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumImagePixels));
        if (MaximumRenderPixels < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumRenderPixels));
        if (MaximumImageComponents is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumImageComponents));
        if (MaximumIccProfileBytes < 128)
            throw new ArgumentOutOfRangeException(nameof(MaximumIccProfileBytes));
        if (MaximumFunctionSamples < 2)
            throw new ArgumentOutOfRangeException(nameof(MaximumFunctionSamples));
        if (MaximumPages < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumPages));
        if (MaximumObjectDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumObjectDepth));
        if (MaximumTreeDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumTreeDepth));
    }
}
