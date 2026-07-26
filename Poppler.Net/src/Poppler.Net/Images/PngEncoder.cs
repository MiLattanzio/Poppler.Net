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
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)image.Height));
        header[8] = 8;
        header[9] = image.Format switch
        {
            PdfPixelFormat.Gray8 => 0,
            PdfPixelFormat.Rgb24 => 2,
            PdfPixelFormat.Rgba32 => 6,
            _ => throw new PdfUnsupportedFeatureException($"PNG pixel format {image.Format}")
        };
        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ReadOnlySpan<byte> pixels = image.Data.Span;
            for (int row = 0; row < image.Height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels.Slice(row * image.BytesPerRow, image.BytesPerRow));
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
