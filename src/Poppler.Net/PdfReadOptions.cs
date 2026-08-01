namespace Poppler;

/// <summary>Resource and recovery controls used while reading untrusted PDFs.</summary>
public sealed record PdfReadOptions
{
    public static PdfReadOptions Default { get; } = new();

    public long MaximumInputBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumDecodedStreamBytes { get; init; } = 256 * 1024 * 1024;
    public long MaximumCachedDecodedBytes { get; init; } = 64L * 1024 * 1024;
    public int MaximumObjects { get; init; } = 1_000_000;
    public int MaximumCollectionItems { get; init; } = 1_000_000;
    public int MaximumContentStreamsPerPage { get; init; } = 10_000;
    public int MaximumContentOperands { get; init; } = 250_000;
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
    public int MaximumAnnotationsPerPage { get; init; } = 100_000;
    public int MaximumAnnotationPoints { get; init; } = 250_000;
    public int MaximumAnnotationAppearanceDepth { get; init; } = 16;
    public int MaximumActions { get; init; } = 10_000;
    public int MaximumActionDepth { get; init; } = 32;
    public int MaximumActionScriptBytes { get; init; } = 1024 * 1024;
    public int MaximumOutlineItems { get; init; } = 100_000;
    public int MaximumOutlineDepth { get; init; } = 128;
    public int MaximumOutlineTitleBytes { get; init; } = 65_536;
    public int MaximumFormFields { get; init; } = 100_000;
    public int MaximumFormWidgets { get; init; } = 100_000;
    public int MaximumFormOptions { get; init; } = 250_000;
    public int MaximumFormFieldDepth { get; init; } = 128;
    public int MaximumFormDefaultAppearanceBytes { get; init; } = 65_536;
    public int MaximumOptionalContentGroups { get; init; } = 100_000;
    public int MaximumOptionalContentDepth { get; init; } = 128;
    public int MaximumOptionalContentExpressionNodes { get; init; } = 250_000;
    public long MaximumImagePixels { get; init; } = 100_000_000;
    public long MaximumRenderPixels { get; init; } = 100_000_000;
    public int MaximumImageComponents { get; init; } = 32;
    public int MaximumIccProfileBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumFunctionSamples { get; init; } = 1_000_000;
    public int MaximumPages { get; init; } = 10_000;
    public int MaximumObjectDepth { get; init; } = 64;
    public int MaximumTreeDepth { get; init; } = 128;
    public bool AttemptXrefRepair { get; init; } = true;
    public bool AttemptPageTreeRepair { get; init; } = true;
    public bool AttemptContentStreamRepair { get; init; } = true;

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
        if (MaximumCachedDecodedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumCachedDecodedBytes));
        if (MaximumObjects < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumObjects));
        if (MaximumCollectionItems < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumCollectionItems));
        if (MaximumContentStreamsPerPage < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumContentStreamsPerPage));
        if (MaximumContentOperands < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumContentOperands));
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
        if (MaximumAnnotationsPerPage < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumAnnotationsPerPage));
        if (MaximumAnnotationPoints < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumAnnotationPoints));
        if (MaximumAnnotationAppearanceDepth < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumAnnotationAppearanceDepth));
        }
        if (MaximumActions < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumActions));
        if (MaximumActionDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumActionDepth));
        if (MaximumActionScriptBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumActionScriptBytes));
        if (MaximumOutlineItems < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutlineItems));
        if (MaximumOutlineDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutlineDepth));
        if (MaximumOutlineTitleBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutlineTitleBytes));
        if (MaximumFormFields < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumFormFields));
        if (MaximumFormWidgets < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumFormWidgets));
        if (MaximumFormOptions < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumFormOptions));
        if (MaximumFormFieldDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumFormFieldDepth));
        if (MaximumFormDefaultAppearanceBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFormDefaultAppearanceBytes));
        }
        if (MaximumOptionalContentGroups < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumOptionalContentGroups));
        if (MaximumOptionalContentDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumOptionalContentDepth));
        if (MaximumOptionalContentExpressionNodes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumOptionalContentExpressionNodes));
        }
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
