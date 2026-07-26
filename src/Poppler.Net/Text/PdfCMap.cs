using System.Globalization;
using System.Text;

namespace Poppler.Text;

/// <summary>
/// Bounded parser for the CMap constructs used by Type 0 encodings and
/// ToUnicode maps. It intentionally keeps CID ranges compressed.
/// </summary>
internal sealed class PdfCMap
{
    private readonly Dictionary<string, string> _unicode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _cidCharacters = new(StringComparer.Ordinal);
    private readonly List<CidRange> _cidRanges = new();
    private readonly List<CodeSpaceRange> _codeSpaces = new();
    private readonly SortedSet<int> _knownLengths = new();
    private readonly int _maximumMappings;

    private PdfCMap(int maximumMappings)
    {
        _maximumMappings = maximumMappings;
    }

    public bool HasUnicodeMappings => _unicode.Count > 0;
    public bool HasCidMappings => _cidCharacters.Count > 0 || _cidRanges.Count > 0;
    public FontWritingMode WritingMode { get; private set; }
    public string Name { get; private set; } = "Custom";

    public static PdfCMap Empty(int maximumMappings) => new(maximumMappings);

    public static PdfCMap Identity(FontWritingMode writingMode, int maximumMappings)
    {
        var result = new PdfCMap(maximumMappings)
        {
            Name = writingMode == FontWritingMode.Vertical ? "Identity-V" : "Identity-H",
            WritingMode = writingMode
        };
        result._codeSpaces.Add(new CodeSpaceRange(2, 0, 0xFFFF));
        result._knownLengths.Add(2);
        return result;
    }

    public static PdfCMap Parse(byte[] bytes, int maximumMappings)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var result = new PdfCMap(maximumMappings);
        List<string> tokens = Tokenize(bytes);

        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (token == "def" && index >= 2)
            {
                if (tokens[index - 2] == "/WMode" &&
                    tokens[index - 1] == "1")
                {
                    result.WritingMode = FontWritingMode.Vertical;
                }
                else if (tokens[index - 2] == "/CMapName" &&
                         tokens[index - 1].StartsWith('/'))
                {
                    result.Name = tokens[index - 1][1..];
                }
            }
            else if (token == "usecmap" && index > 0)
            {
                string baseName = tokens[index - 1].TrimStart('/');
                if (baseName is "Identity-H" or "Identity-V")
                {
                    result.Name = baseName;
                    result.WritingMode = baseName.EndsWith("-V", StringComparison.Ordinal)
                        ? FontWritingMode.Vertical
                        : FontWritingMode.Horizontal;
                    result.AddCodeSpace("<0000>", "<FFFF>");
                }
            }
            else if (TrySectionCount(tokens, index, "begincodespacerange", out int count))
            {
                int cursor = index + 1;
                for (int item = 0; item < count && cursor + 1 < tokens.Count; item++)
                    result.AddCodeSpace(tokens[cursor++], tokens[cursor++]);
            }
            else if (TrySectionCount(tokens, index, "beginbfchar", out count))
            {
                int cursor = index + 1;
                for (int item = 0; item < count && cursor + 1 < tokens.Count; item++)
                    result.AddUnicode(tokens[cursor++], tokens[cursor++]);
            }
            else if (TrySectionCount(tokens, index, "beginbfrange", out count))
            {
                int cursor = index + 1;
                for (int item = 0; item < count && cursor + 2 < tokens.Count; item++)
                    cursor = result.AddUnicodeRange(tokens, cursor);
            }
            else if (TrySectionCount(tokens, index, "begincidchar", out count))
            {
                int cursor = index + 1;
                for (int item = 0; item < count && cursor + 1 < tokens.Count; item++)
                    result.AddCidCharacter(tokens[cursor++], tokens[cursor++]);
            }
            else if (TrySectionCount(tokens, index, "begincidrange", out count))
            {
                int cursor = index + 1;
                for (int item = 0; item < count && cursor + 2 < tokens.Count; item++)
                    result.AddCidRange(tokens[cursor++], tokens[cursor++], tokens[cursor++]);
            }
        }

        if (result._knownLengths.Count == 0)
        {
            foreach (string key in result._unicode.Keys.Concat(result._cidCharacters.Keys))
                result._knownLengths.Add(key.Length / 2);
        }

        return result;
    }

    public PdfCharCode ReadCode(ReadOnlySpan<byte> bytes, int position, int fallbackLength)
    {
        if ((uint)position >= (uint)bytes.Length)
            return default;

        foreach (int length in _knownLengths)
        {
            if (length < 1 || length > 4 || position > bytes.Length - length)
                continue;
            uint value = ToUInt(bytes.Slice(position, length));
            if (_codeSpaces.Count == 0 ||
                _codeSpaces.Any(range =>
                    range.Length == length && value >= range.Start && value <= range.End))
            {
                return new PdfCharCode(value, length);
            }
        }

        int available = bytes.Length - position;
        int consumed = Math.Clamp(fallbackLength, 1, Math.Min(4, available));
        return new PdfCharCode(ToUInt(bytes.Slice(position, consumed)), consumed);
    }

    public bool TryGetUnicode(ReadOnlySpan<byte> source, out string text) =>
        _unicode.TryGetValue(Convert.ToHexString(source), out text!);

    public uint GetCid(ReadOnlySpan<byte> source, uint fallback)
    {
        string key = Convert.ToHexString(source);
        if (_cidCharacters.TryGetValue(key, out uint cid))
            return cid;

        uint code = ToUInt(source);
        foreach (CidRange range in _cidRanges)
        {
            if (range.Length == source.Length && code >= range.Start && code <= range.End)
            {
                ulong mapped = (ulong)range.FirstCid + code - range.Start;
                return mapped <= uint.MaxValue ? (uint)mapped : fallback;
            }
        }

        return fallback;
    }

    private static bool TrySectionCount(
        IReadOnlyList<string> tokens,
        int index,
        string expected,
        out int count)
    {
        count = 0;
        return tokens[index] == expected &&
               index > 0 &&
               int.TryParse(
                   tokens[index - 1],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out count) &&
               count >= 0;
    }

    private void AddCodeSpace(string startToken, string endToken)
    {
        if (!TryHexBytes(startToken, out byte[] start) ||
            !TryHexBytes(endToken, out byte[] end) ||
            start.Length is < 1 or > 4 ||
            start.Length != end.Length)
        {
            return;
        }

        uint first = ToUInt(start);
        uint last = ToUInt(end);
        if (first > last)
            return;
        _codeSpaces.Add(new CodeSpaceRange(start.Length, first, last));
        _knownLengths.Add(start.Length);
    }

    private void AddUnicode(string sourceToken, string destinationToken)
    {
        if (!TryHexBytes(sourceToken, out byte[] source) ||
            !TryHexBytes(destinationToken, out byte[] destination) ||
            source.Length is < 1 or > 4)
        {
            return;
        }

        EnsureMappingLimit((ulong)_unicode.Count + 1);
        _unicode[Convert.ToHexString(source)] = DecodeUnicode(destination);
        _knownLengths.Add(source.Length);
    }

    private int AddUnicodeRange(IReadOnlyList<string> tokens, int cursor)
    {
        string startToken = tokens[cursor++];
        string endToken = tokens[cursor++];
        string destination = tokens[cursor++];
        if (!TryHexBytes(startToken, out byte[] startBytes) ||
            !TryHexBytes(endToken, out byte[] endBytes) ||
            startBytes.Length is < 1 or > 4 ||
            startBytes.Length != endBytes.Length)
        {
            return cursor;
        }

        uint start = ToUInt(startBytes);
        uint end = ToUInt(endBytes);
        if (end < start)
            return cursor;

        ulong rangeSize = (ulong)end - start + 1;
        EnsureMappingLimit((ulong)_unicode.Count + rangeSize);
        if (destination == "[")
        {
            uint sourceCode = start;
            while (cursor < tokens.Count && tokens[cursor] != "]" && sourceCode <= end)
            {
                AddUnicode(ToHex(sourceCode, startBytes.Length), tokens[cursor++]);
                if (sourceCode == uint.MaxValue)
                    break;
                sourceCode++;
            }

            if (cursor < tokens.Count && tokens[cursor] == "]")
                cursor++;
            return cursor;
        }

        if (!TryHexBytes(destination, out byte[] destinationBytes))
            return cursor;
        for (uint sourceCode = start; sourceCode <= end; sourceCode++)
        {
            byte[] mapped = IncrementBigEndian(destinationBytes, sourceCode - start);
            AddUnicode(ToHex(sourceCode, startBytes.Length), $"<{Convert.ToHexString(mapped)}>");
            if (sourceCode == uint.MaxValue)
                break;
        }

        return cursor;
    }

    private void AddCidCharacter(string sourceToken, string cidToken)
    {
        if (!TryHexBytes(sourceToken, out byte[] source) ||
            source.Length is < 1 or > 4 ||
            !uint.TryParse(cidToken, NumberStyles.None, CultureInfo.InvariantCulture, out uint cid))
        {
            return;
        }

        EnsureMappingLimit((ulong)_cidCharacters.Count + 1);
        _cidCharacters[Convert.ToHexString(source)] = cid;
        _knownLengths.Add(source.Length);
    }

    private void AddCidRange(string startToken, string endToken, string cidToken)
    {
        if (!TryHexBytes(startToken, out byte[] start) ||
            !TryHexBytes(endToken, out byte[] end) ||
            start.Length is < 1 or > 4 ||
            start.Length != end.Length ||
            !uint.TryParse(cidToken, NumberStyles.None, CultureInfo.InvariantCulture, out uint cid))
        {
            return;
        }

        uint first = ToUInt(start);
        uint last = ToUInt(end);
        if (first > last)
            return;
        EnsureMappingLimit((ulong)_cidCharacters.Count + (ulong)_cidRanges.Count + 1);
        _cidRanges.Add(new CidRange(start.Length, first, last, cid));
        _knownLengths.Add(start.Length);
    }

    private void EnsureMappingLimit(ulong count)
    {
        if (count > (ulong)_maximumMappings)
            throw new PdfLimitException($"CMap exceeds the {_maximumMappings} mapping limit.");
    }

    private static List<string> Tokenize(byte[] bytes)
    {
        string text = Encoding.Latin1.GetString(bytes);
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

            if (text[position] == '<' && position + 1 < text.Length && text[position + 1] != '<')
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

            if (position == tokenStart)
            {
                position++;
                continue;
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
        if (bytes.Length >= 2 && bytes.Length % 2 == 0)
            return Encoding.BigEndianUnicode.GetString(bytes);
        return Encoding.Latin1.GetString(bytes);
    }

    private static uint ToUInt(ReadOnlySpan<byte> bytes)
    {
        uint value = 0;
        foreach (byte item in bytes)
            value = (value << 8) | item;
        return value;
    }

    private static byte[] IncrementBigEndian(byte[] bytes, uint amount)
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

    private static string ToHex(uint value, int width)
    {
        var bytes = new byte[width];
        for (int index = width - 1; index >= 0; index--)
        {
            bytes[index] = (byte)value;
            value >>= 8;
        }

        return $"<{Convert.ToHexString(bytes)}>";
    }

    private readonly record struct CodeSpaceRange(int Length, uint Start, uint End);
    private readonly record struct CidRange(int Length, uint Start, uint End, uint FirstCid);
}

internal readonly record struct PdfCharCode(uint Value, int Length);
