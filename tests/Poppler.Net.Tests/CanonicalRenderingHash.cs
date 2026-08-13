using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Poppler.Net.Tests;

internal static partial class CanonicalRenderingHash
{
    public const string PngMode = "canonical-png-content-v1";
    public const string SvgMode = "canonical-svg-content-v1";
    public const string HtmlMode = "canonical-html-content-v2";

    private static readonly byte[] PngSignature =
    {
        137, 80, 78, 71, 13, 10, 26, 10
    };

    private static readonly byte[] PngHashDomain =
        "Poppler.Net canonical PNG content v1\0"u8.ToArray();

    public static string Png(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < PngSignature.Length ||
            !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("The value is not a PNG image.");
        }

        byte[]? header = null;
        using var compressed = new MemoryStream();
        bool ended = false;
        int offset = PngSignature.Length;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
                throw new InvalidDataException("The PNG chunk is truncated.");

            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                png.AsSpan(offset, 4)));
            offset += 4;
            ReadOnlySpan<byte> type = png.AsSpan(offset, 4);
            offset += 4;
            if (length > png.Length - offset - 4)
                throw new InvalidDataException("The PNG chunk data is truncated.");

            ReadOnlySpan<byte> data = png.AsSpan(offset, length);
            offset += length;
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                png.AsSpan(offset, 4));
            offset += 4;
            if (ChunkCrc(type, data) != expectedCrc)
                throw new InvalidDataException("The PNG chunk CRC is invalid.");

            if (type.SequenceEqual("IHDR"u8))
            {
                if (header is not null || data.Length != 13)
                    throw new InvalidDataException("The PNG header is invalid.");
                header = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (header is null || ended)
                    throw new InvalidDataException("The PNG data order is invalid.");
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (data.Length != 0 || ended)
                    throw new InvalidDataException("The PNG end chunk is invalid.");
                ended = true;
                break;
            }
        }

        if (header is null || compressed.Length == 0 || !ended || offset != png.Length)
            throw new InvalidDataException("The PNG image is incomplete.");

        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        int components = header[9] switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException("The PNG color type is unsupported.")
        };
        if (width < 1 || height < 1 || header[8] != 8 ||
            header[10] != 0 || header[11] != 0 || header[12] != 0)
        {
            throw new InvalidDataException("The PNG encoding is unsupported.");
        }

        int rowLength = checked(width * components + 1);
        byte[] row = GC.AllocateUninitializedArray<byte>(rowLength);
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(PngHashDomain);
        hash.AppendData(header);
        for (int y = 0; y < height; y++)
        {
            zlib.ReadExactly(row);
            if (row[0] != 0)
            {
                throw new InvalidDataException(
                    "Canonical hashing requires the production PNG row filter.");
            }
            hash.AppendData(row);
        }
        if (zlib.ReadByte() != -1)
            throw new InvalidDataException("The PNG contains excess image data.");

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string Svg(string svg)
    {
        ArgumentNullException.ThrowIfNull(svg);
        return EmbeddedPngContent(svg);
    }

    public static string Html(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        string canonical = HtmlGenerator().Replace(
            html,
            "<meta name=\"generator\" content=\"Poppler.Net &lt;version&gt;\">");
        return EmbeddedPngContent(canonical);
    }

    private static string EmbeddedPngContent(string value)
    {
        string canonical = EmbeddedPng().Replace(value, match =>
        {
            byte[] png = Convert.FromBase64String(match.Groups[1].Value);
            return "data:image/png;canonical-sha256," + Png(png);
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static uint ChunkCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = UpdateCrc(0xFFFFFFFF, type);
        return ~UpdateCrc(crc, data);
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

    [GeneratedRegex("data:image/png;base64,([A-Za-z0-9+/]+={0,2})", RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedPng();

    [GeneratedRegex(
        "<meta name=\"generator\" content=\"Poppler\\.Net [^\"]+\">",
        RegexOptions.CultureInvariant)]
    private static partial Regex HtmlGenerator();
}
