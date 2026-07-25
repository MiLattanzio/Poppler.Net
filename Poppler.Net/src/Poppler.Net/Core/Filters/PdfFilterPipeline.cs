using System.Buffers;
using System.IO.Compression;

namespace Poppler.Core.Filters;

internal static class PdfFilterPipeline
{
    public static byte[] Decode(
        PdfStream stream,
        PdfDocumentCore document,
        PdfReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] current = stream.EncodedBytes.ToArray();
        IReadOnlyList<PdfObject> filters = GetSequence(stream.Dictionary.GetValueOrNull("Filter"), document);
        IReadOnlyList<PdfObject> parameters =
            GetSequence(stream.Dictionary.GetValueOrNull("DecodeParms"), document);

        for (int index = 0; index < filters.Count; index++)
        {
            string? name = filters[index].AsName(document);
            PdfDictionary? parameter = index < parameters.Count
                ? parameters[index].AsDictionary(document)
                : null;

            current = name switch
            {
                null => current,
                "FlateDecode" or "Fl" => DecodeFlate(current, options),
                "LZWDecode" or "LZW" => LzwDecoder.Decode(
                    current,
                    parameter?.GetValueOrNull("EarlyChange").AsInteger(document) ?? 1,
                    options.MaximumDecodedStreamBytes),
                "ASCIIHexDecode" or "AHx" => DecodeAsciiHex(current, options),
                "ASCII85Decode" or "A85" => DecodeAscii85(current, options),
                "RunLengthDecode" or "RL" => DecodeRunLength(current, options),
                "Crypt" when parameter is null ||
                             parameter.GetValueOrNull("Name").AsName(document) is null or "Identity" => current,
                _ => throw new PdfUnsupportedFeatureException($"stream filter {name}")
            };

            if (parameter is not null &&
                name is ("FlateDecode" or "Fl" or "LZWDecode" or "LZW"))
                current = PdfPredictor.Decode(current, parameter, document, options);
            EnsureLimit(current.Length, options.MaximumDecodedStreamBytes);
        }

        EnsureLimit(current.Length, options.MaximumDecodedStreamBytes);
        return current;
    }

    private static IReadOnlyList<PdfObject> GetSequence(PdfObject? value, PdfDocumentCore document)
    {
        if (value is null || value.Resolve(document) is PdfNull)
            return Array.Empty<PdfObject>();
        PdfObject resolved = value.Resolve(document);
        return resolved is PdfArray array ? array : new[] { resolved };
    }

    private static byte[] DecodeFlate(byte[] source, PdfReadOptions options)
    {
        try
        {
            using var input = new MemoryStream(source, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            return ReadBounded(zlib, options.MaximumDecodedStreamBytes);
        }
        catch (InvalidDataException zlibException)
        {
            try
            {
                using var input = new MemoryStream(source, writable: false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                return ReadBounded(deflate, options.MaximumDecodedStreamBytes);
            }
            catch (InvalidDataException deflateException)
            {
                throw new PdfFormatException(
                    "Invalid Flate stream.",
                    new AggregateException(zlibException, deflateException));
            }
        }
    }

    private static byte[] DecodeAsciiHex(byte[] source, PdfReadOptions options)
    {
        using var output = new MemoryStream(Math.Min(source.Length / 2 + 1, options.MaximumDecodedStreamBytes));
        int high = -1;
        foreach (byte value in source)
        {
            if (IsWhiteSpace(value))
                continue;
            if (value == '>')
                break;
            int nibble = HexValue(value);
            if (nibble < 0)
                throw new PdfFormatException("Invalid ASCIIHex stream.");
            if (high < 0)
                high = nibble;
            else
            {
                output.WriteByte((byte)((high << 4) | nibble));
                high = -1;
                EnsureLimit(output.Length, options.MaximumDecodedStreamBytes);
            }
        }

        if (high >= 0)
            output.WriteByte((byte)(high << 4));
        return output.ToArray();
    }

    private static byte[] DecodeAscii85(byte[] source, PdfReadOptions options)
    {
        using var output = new MemoryStream(Math.Min(source.Length, options.MaximumDecodedStreamBytes));
        ulong tuple = 0;
        int count = 0;
        bool started = false;

        for (int index = 0; index < source.Length; index++)
        {
            byte value = source[index];
            if (IsWhiteSpace(value))
                continue;
            if (!started && value == '<' && index + 1 < source.Length && source[index + 1] == '~')
            {
                started = true;
                index++;
                continue;
            }

            started = true;
            if (value == '~')
                break;
            if (value == 'z')
            {
                if (count != 0)
                    throw new PdfFormatException("Invalid 'z' inside an ASCII85 tuple.");
                WriteUInt32(output, 0, 4);
                EnsureLimit(output.Length, options.MaximumDecodedStreamBytes);
                continue;
            }

            if (value is < (byte)'!' or > (byte)'u')
                throw new PdfFormatException("Invalid ASCII85 stream.");
            tuple = tuple * 85 + (uint)(value - '!');
            count++;
            if (count == 5)
            {
                if (tuple > uint.MaxValue)
                    throw new PdfFormatException("ASCII85 tuple exceeds 32 bits.");
                WriteUInt32(output, (uint)tuple, 4);
                tuple = 0;
                count = 0;
                EnsureLimit(output.Length, options.MaximumDecodedStreamBytes);
            }
        }

        if (count == 1)
            throw new PdfFormatException("Invalid final ASCII85 tuple.");
        if (count > 1)
        {
            for (int index = count; index < 5; index++)
                tuple = tuple * 85 + 84;
            if (tuple > uint.MaxValue)
                throw new PdfFormatException("ASCII85 tuple exceeds 32 bits.");
            WriteUInt32(output, (uint)tuple, count - 1);
        }

        EnsureLimit(output.Length, options.MaximumDecodedStreamBytes);
        return output.ToArray();
    }

    private static byte[] DecodeRunLength(byte[] source, PdfReadOptions options)
    {
        using var output = new MemoryStream(Math.Min(source.Length * 2, options.MaximumDecodedStreamBytes));
        int index = 0;
        while (index < source.Length)
        {
            int length = source[index++];
            if (length == 128)
                break;
            if (length <= 127)
            {
                int count = length + 1;
                if (index > source.Length - count)
                    throw new PdfFormatException("Truncated RunLength stream.");
                output.Write(source, index, count);
                index += count;
            }
            else
            {
                if (index >= source.Length)
                    throw new PdfFormatException("Truncated RunLength stream.");
                int count = 257 - length;
                byte repeated = source[index++];
                for (int repeat = 0; repeat < count; repeat++)
                    output.WriteByte(repeated);
            }

            EnsureLimit(output.Length, options.MaximumDecodedStreamBytes);
        }

        return output.ToArray();
    }

    private static byte[] ReadBounded(Stream input, int maximumBytes)
    {
        using var output = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                EnsureLimit(output.Length + read, maximumBytes);
                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.ToArray();
    }

    private static void WriteUInt32(Stream output, uint value, int bytes)
    {
        Span<byte> buffer = stackalloc byte[4]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        };
        output.Write(buffer[..bytes]);
    }

    private static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        _ => -1
    };

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';

    private static void EnsureLimit(long length, int maximumBytes)
    {
        if (length > maximumBytes)
            throw new PdfLimitException($"Decoded stream exceeds {maximumBytes} bytes.");
    }
}
