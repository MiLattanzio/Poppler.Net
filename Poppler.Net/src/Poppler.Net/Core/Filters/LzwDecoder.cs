namespace Poppler.Core.Filters;

internal static class LzwDecoder
{
    public static byte[] Decode(byte[] source, int earlyChange, int maximumBytes)
    {
        if (earlyChange is not 0 and not 1)
            throw new PdfFormatException("LZW EarlyChange must be 0 or 1.");

        var reader = new BitReader(source);
        using var output = new MemoryStream();
        var table = new byte[4096][];
        Reset(table, out int nextCode, out int codeWidth);
        byte[]? previous = null;

        while (reader.TryRead(codeWidth, out int code))
        {
            if (code == 256)
            {
                Reset(table, out nextCode, out codeWidth);
                previous = null;
                continue;
            }

            if (code == 257)
                break;

            byte[] entry;
            if (code < nextCode && table[code] is not null)
            {
                entry = table[code];
            }
            else if (code == nextCode && previous is not null)
            {
                entry = Append(previous, previous[0]);
            }
            else
            {
                throw new PdfFormatException("Invalid LZW code.");
            }

            output.Write(entry);
            if (output.Length > maximumBytes)
                throw new PdfLimitException($"Decoded stream exceeds {maximumBytes} bytes.");

            if (previous is not null && nextCode < 4096)
            {
                table[nextCode++] = Append(previous, entry[0]);
                if (codeWidth < 12 && nextCode + earlyChange == (1 << codeWidth))
                    codeWidth++;
            }

            previous = entry;
        }

        return output.ToArray();
    }

    private static void Reset(byte[][] table, out int nextCode, out int codeWidth)
    {
        Array.Clear(table);
        for (int value = 0; value < 256; value++)
            table[value] = new[] { (byte)value };
        nextCode = 258;
        codeWidth = 9;
    }

    private static byte[] Append(byte[] value, byte suffix)
    {
        var result = new byte[value.Length + 1];
        value.CopyTo(result, 0);
        result[^1] = suffix;
        return result;
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bitPosition;

        public BitReader(byte[] data) => _data = data;

        public bool TryRead(int width, out int value)
        {
            if (_bitPosition > _data.Length * 8 - width)
            {
                value = 0;
                return false;
            }

            value = 0;
            for (int index = 0; index < width; index++)
            {
                int byteIndex = _bitPosition >> 3;
                int bitIndex = 7 - (_bitPosition & 7);
                value = (value << 1) | ((_data[byteIndex] >> bitIndex) & 1);
                _bitPosition++;
            }

            return true;
        }
    }
}
