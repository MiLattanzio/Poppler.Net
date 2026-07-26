using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Poppler.Images;

internal static class PngEncoder
{
    private static readonly byte[] Signature =
    {
        137, 80, 78, 71, 13, 10, 26, 10
    };

    public static byte[] Encode(PdfImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Encode(
            image.Width,
            image.Height,
            image.Format,
            image.BytesPerRow,
            image.Data.Span);
    }

    public static byte[] Encode(
        int width,
        int height,
        PdfPixelFormat format,
        int bytesPerRow,
        ReadOnlySpan<byte> pixels)
    {
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
        if (bytesPerRow != checked(width * components))
            throw new ArgumentOutOfRangeException(nameof(bytesPerRow));
        if (pixels.Length != checked(bytesPerRow * height))
            throw new ArgumentException(
                "Pixel data length does not match the image dimensions.",
                nameof(pixels));

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = format switch
        {
            PdfPixelFormat.Gray8 => 0,
            PdfPixelFormat.Rgb24 => 2,
            PdfPixelFormat.Rgba32 => 6,
            _ => throw new PdfUnsupportedFeatureException($"PNG pixel format {format}")
        };
        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels.Slice(row * bytesPerRow, bytesPerRow));
            }
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(
        Stream output,
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)data.Length));
        output.Write(size);
        output.Write(type);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(size, ~crc);
        output.Write(size);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320 & mask);
            }
        }

        return crc;
    }
}
