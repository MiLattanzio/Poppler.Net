using Poppler.Images;

namespace Poppler.Rendering;

/// <summary>
/// Immutable, tightly packed RGBA page raster. Rows run from top to bottom and
/// color channels use straight (not premultiplied) alpha.
/// </summary>
public sealed class PdfBitmap
{
    private readonly byte[] _data;

    internal PdfBitmap(int width, int height, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1)
            throw new ArgumentOutOfRangeException(nameof(height));
        int expected = checked(width * height * 4);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                "Pixel data length does not match the bitmap dimensions.",
                nameof(data));
        }

        Width = width;
        Height = height;
        BytesPerRow = checked(width * 4);
        _data = data;
    }

    public int Width { get; }
    public int Height { get; }
    public int BytesPerRow { get; }
    public PdfPixelFormat Format => PdfPixelFormat.Rgba32;
    public ReadOnlyMemory<byte> Data => _data;

    public byte[] ToPngBytes() =>
        PngEncoder.Encode(Width, Height, Format, BytesPerRow, _data);

    public void SavePng(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        File.WriteAllBytes(fileName, ToPngBytes());
    }
}
