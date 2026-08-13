using Poppler.Images;

namespace Poppler;

/// <summary>Pixel layout used by a decoded PDF image.</summary>
public enum PdfPixelFormat
{
    Gray8,
    Rgb24,
    Rgba32
}

/// <summary>Compression that supplied the decoded PDF image samples.</summary>
public enum PdfImageCompression
{
    Raw,
    Flate,
    Lzw,
    RunLength,
    Jpeg,
    Jpeg2000,
    Jbig2,
    CcittFax
}

/// <summary>Reusable representation selected for an exported PDF image.</summary>
public sealed record PdfImageExport(
    string Extension,
    string MediaType,
    ReadOnlyMemory<byte> Data,
    bool IsOriginal,
    string? FallbackReason);

/// <summary>
/// Immutable, tightly packed pixels decoded from an Image XObject or image
/// mask. Rows are stored from top to bottom.
/// </summary>
public sealed class PdfImage
{
    private readonly byte[] _data;
    private readonly byte[]? _originalData;

    internal PdfImage(
        string resourceName,
        int width,
        int height,
        PdfPixelFormat format,
        string colorSpace,
        int sourceBitsPerComponent,
        PdfImageCompression compression,
        bool interpolate,
        byte[] data,
        byte[]? originalData = null,
        string? originalExtension = null,
        string? originalMediaType = null,
        string? originalFallbackReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorSpace);
        ArgumentNullException.ThrowIfNull(data);
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1)
            throw new ArgumentOutOfRangeException(nameof(height));

        int components = format switch
        {
            PdfPixelFormat.Gray8 => 1,
            PdfPixelFormat.Rgb24 => 3,
            PdfPixelFormat.Rgba32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        int expected = checked(width * height * components);
        if (data.Length != expected)
            throw new ArgumentException("Pixel data length does not match the image dimensions.", nameof(data));

        ResourceName = resourceName;
        Width = width;
        Height = height;
        Format = format;
        ColorSpace = colorSpace;
        SourceBitsPerComponent = sourceBitsPerComponent;
        Compression = compression;
        Interpolate = interpolate;
        BytesPerRow = checked(width * components);
        _data = data;
        _originalData = originalData;
        OriginalExtension = originalExtension;
        OriginalMediaType = originalMediaType;
        OriginalFallbackReason = originalFallbackReason;
    }

    public string ResourceName { get; }
    public int Width { get; }
    public int Height { get; }
    public PdfPixelFormat Format { get; }
    public string ColorSpace { get; }
    public int SourceBitsPerComponent { get; }
    public PdfImageCompression Compression { get; }
    public bool Interpolate { get; }
    public bool CanExportOriginal => _originalData is not null;
    public string? OriginalExtension { get; }
    public string? OriginalMediaType { get; }
    public string? OriginalFallbackReason { get; }

    /// <summary>
    /// Exact number of bytes between consecutive rows. Decoded images are
    /// tightly packed and do not contain implicit alignment padding.
    /// </summary>
    public int BytesPerRow { get; }

    public bool HasAlpha => Format == PdfPixelFormat.Rgba32;
    public ReadOnlyMemory<byte> Data => _data;

    public byte[] ToPngBytes() => PngEncoder.Encode(this);

    /// <summary>
    /// Selects an independently reusable original encoded payload when that
    /// preserves PDF image semantics; otherwise returns a decoded PNG.
    /// </summary>
    public PdfImageExport Export(bool preferOriginal = true)
    {
        if (preferOriginal &&
            _originalData is not null &&
            OriginalExtension is not null &&
            OriginalMediaType is not null)
        {
            return new PdfImageExport(
                OriginalExtension,
                OriginalMediaType,
                _originalData,
                true,
                null);
        }

        return new PdfImageExport(
            "png",
            "image/png",
            ToPngBytes(),
            false,
            OriginalFallbackReason ??
            (preferOriginal ? "The original encoded payload is not independently reusable." : null));
    }

    public void SavePng(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        File.WriteAllBytes(fileName, ToPngBytes());
    }
}
