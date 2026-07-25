using System.Globalization;
using System.Text;

namespace Poppler.Text;

internal sealed class PdfCMap
{
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
    private readonly SortedSet<int> _codeLengths = new(Comparer<int>.Create((left, right) => right.CompareTo(left)));

    public bool HasMappings => _map.Count > 0;

    public static PdfCMap Parse(byte[] bytes)
    {
        var result = new PdfCMap();
        List<string> tokens = Tokenize(bytes);
        for (int index = 0; index < tokens.Count; index++)
        {
            if (tokens[index] == "beginbfchar" &&
                index > 0 &&
                int.TryParse(tokens[index - 1], NumberStyles.None, CultureInfo.InvariantCulture, out int charCount))
            {
                int cursor = index + 1;
                for (int item = 0; item < charCount && cursor + 1 < tokens.Count; item++)
                {
                    string source = tokens[cursor++];
                    string target = tokens[cursor++];
                    result.Add(source, target);
                }
            }
            else if (tokens[index] == "beginbfrange" &&
                     index > 0 &&
                     int.TryParse(tokens[index - 1], NumberStyles.None, CultureInfo.InvariantCulture, out int rangeCount))
            {
                int cursor = index + 1;
                for (int item = 0; item < rangeCount && cursor + 2 < tokens.Count; item++)
                {
                    string startToken = tokens[cursor++];
                    string endToken = tokens[cursor++];
                    string destination = tokens[cursor++];
                    if (!TryHexBytes(startToken, out byte[] startBytes) ||
                        !TryHexBytes(endToken, out byte[] endBytes))
                    {
                        continue;
                    }

                    uint start = ToUInt(startBytes);
                    uint end = ToUInt(endBytes);
                    if (destination == "[")
                    {
                        uint sourceCode = start;
                        while (cursor < tokens.Count && tokens[cursor] != "]" && sourceCode <= end)
                        {
                            result.Add(FromUInt(sourceCode, startBytes.Length), tokens[cursor++]);
                            sourceCode++;
                        }

                        if (cursor < tokens.Count && tokens[cursor] == "]")
                            cursor++;
                    }
                    else if (TryHexBytes(destination, out byte[] destinationBytes))
                    {
                        for (uint sourceCode = start; sourceCode <= end; sourceCode++)
                        {
                            result.Add(
                                FromUInt(sourceCode, startBytes.Length),
                                ToHex(IncrementLastCodePoint(destinationBytes, sourceCode - start)));
                            if (sourceCode == uint.MaxValue)
                                break;
                        }
                    }
                }
            }
        }

        return result;
    }

    public string Decode(ReadOnlySpan<byte> bytes, Func<byte, string> fallback)
    {
        var builder = new StringBuilder();
        int position = 0;
        while (position < bytes.Length)
        {
            bool matched = false;
            foreach (int length in _codeLengths)
            {
                if (position > bytes.Length - length)
                    continue;
                string key = Convert.ToHexString(bytes.Slice(position, length));
                if (_map.TryGetValue(key, out string? text))
                {
                    builder.Append(text);
                    position += length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                builder.Append(fallback(bytes[position]));
                position++;
            }
        }

        return builder.ToString();
    }

    private void Add(string sourceToken, string destinationToken)
    {
        if (!TryHexBytes(sourceToken, out byte[] source) ||
            !TryHexBytes(destinationToken, out byte[] destination) ||
            source.Length == 0)
        {
            return;
        }

        string key = Convert.ToHexString(source);
        _map[key] = DecodeUnicode(destination);
        _codeLengths.Add(source.Length);
    }

    private static List<string> Tokenize(byte[] bytes)
    {
        string text = Encoding.ASCII.GetString(bytes);
        var result = new List<string>();
        int position = 0;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            if (text[position] == '%')
            {
                while (position < text.Length && text[position] is not '\r' and not '\n')
                    position++;
                continue;
            }

            if (text[position] is '[' or ']')
            {
                result.Add(text[position++].ToString());
                continue;
            }

            if (text[position] == '<')
            {
                int start = position++;
                while (position < text.Length && text[position] != '>')
                    position++;
                if (position < text.Length)
                    position++;
                result.Add(text[start..position]);
                continue;
            }

            int tokenStart = position;
            while (position < text.Length &&
                   !char.IsWhiteSpace(text[position]) &&
                   text[position] is not '[' and not ']' and not '<')
            {
                position++;
            }

            result.Add(text[tokenStart..position]);
        }

        return result;
    }

    private static bool TryHexBytes(string token, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (token.Length < 2 || token[0] != '<' || token[^1] != '>')
            return false;
        string hex = string.Concat(token[1..^1].Where(character => !char.IsWhiteSpace(character)));
        if (hex.Length % 2 != 0)
            hex += "0";
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string DecodeUnicode(byte[] bytes)
    {
        if (bytes.Length % 2 == 0)
            return Encoding.BigEndianUnicode.GetString(bytes);
        return Encoding.Latin1.GetString(bytes);
    }

    private static uint ToUInt(byte[] bytes)
    {
        uint value = 0;
        foreach (byte item in bytes.TakeLast(4))
            value = (value << 8) | item;
        return value;
    }

    private static string FromUInt(uint value, int width)
    {
        var bytes = new byte[width];
        for (int index = width - 1; index >= 0; index--)
        {
            bytes[index] = (byte)value;
            value >>= 8;
        }

        return ToHex(bytes);
    }

    private static byte[] IncrementLastCodePoint(byte[] bytes, uint amount)
    {
        var result = bytes.ToArray();
        for (int index = result.Length - 1; index >= 0 && amount > 0; index--)
        {
            uint value = result[index] + (amount & 0xFF);
            result[index] = (byte)value;
            amount = (amount >> 8) + (value >> 8);
        }

        return result;
    }

    private static string ToHex(byte[] bytes) => $"<{Convert.ToHexString(bytes)}>";
}
