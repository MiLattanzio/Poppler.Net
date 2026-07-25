using System.Globalization;
using System.Text;

namespace Poppler.Core;

internal sealed class PdfSyntaxReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private readonly PdfReadOptions _options;

    public PdfSyntaxReader(byte[] data, int offset, int length, PdfReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _data = data;
        Position = offset;
        _end = offset + length;
        _options = options;
    }

    public int Position { get; set; }
    public bool AtEnd => Position >= _end;

    public PdfObject ReadObject(int depth = 0)
    {
        if (depth > _options.MaximumObjectDepth)
            throw new PdfLimitException($"PDF object nesting exceeds {_options.MaximumObjectDepth}.");

        SkipTrivia();
        if (AtEnd)
            throw Error("Unexpected end of data while reading an object");

        return _data[Position] switch
        {
            (byte)'/' => ReadName(),
            (byte)'(' => ReadLiteralString(),
            (byte)'<' when Peek(1) == '<' => ReadDictionary(depth + 1),
            (byte)'<' => ReadHexString(),
            (byte)'[' => ReadArray(depth + 1),
            (byte)']' => throw Error("Unexpected array terminator"),
            _ => ReadNumberReferenceOrKeyword()
        };
    }

    public PdfIndirectObject ReadIndirectObject(Func<PdfObject, int?>? resolveLength = null)
    {
        SkipTrivia();
        int objectOffset = Position;
        string objectNumberToken = ReadRawToken();
        string generationToken = ReadRawToken();
        string marker = ReadRawToken();
        if (!int.TryParse(objectNumberToken, NumberStyles.None, CultureInfo.InvariantCulture, out int objectNumber) ||
            !int.TryParse(generationToken, NumberStyles.None, CultureInfo.InvariantCulture, out int generation) ||
            marker != "obj")
        {
            throw new PdfFormatException("Expected an indirect object header", objectOffset);
        }

        PdfObject value = ReadObject();
        SkipTrivia();
        if (value is PdfDictionary dictionary && TryReadKeyword("stream"))
        {
            ConsumeStreamLineEnding();
            int streamStart = Position;
            int? declaredLength = null;
            if (dictionary.TryGetValue("Length", out PdfObject? lengthObject))
            {
                declaredLength = lengthObject is PdfNumber number && number.IsInteger
                    ? checked((int)number.Value)
                    : resolveLength?.Invoke(lengthObject);
            }

            int streamLength;
            if (declaredLength is >= 0 && streamStart <= _end - declaredLength.Value)
            {
                streamLength = declaredLength.Value;
                Position = streamStart + streamLength;
                SkipTrivia();
                if (!TryReadKeyword("endstream"))
                {
                    Position = streamStart;
                    streamLength = FindKeyword("endstream") - streamStart;
                    Position = streamStart + streamLength;
                    TrimTrailingLineEnding(streamStart, ref streamLength);
                    if (!TryReadKeyword("endstream"))
                        throw Error("Missing endstream marker");
                }
            }
            else
            {
                int markerOffset = FindKeyword("endstream");
                streamLength = markerOffset - streamStart;
                Position = markerOffset;
                TrimTrailingLineEnding(streamStart, ref streamLength);
                if (!TryReadKeyword("endstream"))
                    throw Error("Missing endstream marker");
            }

            if (streamLength < 0)
                throw Error("Invalid stream length");
            value = new PdfStream(dictionary, _data.AsSpan(streamStart, streamLength));
        }

        SkipTrivia();
        _ = TryReadKeyword("endobj");
        return new PdfIndirectObject(objectNumber, generation, value, objectOffset, Position);
    }

    public string ReadRawToken()
    {
        SkipTrivia();
        if (AtEnd)
            throw Error("Unexpected end of data while reading a token");

        int start = Position;
        if (IsDelimiter(_data[Position]))
        {
            Position++;
            if (Position < _end &&
                ((_data[start] == '<' && _data[Position] == '<') ||
                 (_data[start] == '>' && _data[Position] == '>')))
            {
                Position++;
            }
        }
        else
        {
            while (Position < _end &&
                   !IsWhiteSpace(_data[Position]) &&
                   !IsDelimiter(_data[Position]))
            {
                Position++;
            }
        }

        return Encoding.ASCII.GetString(_data, start, Position - start);
    }

    public bool TryReadKeyword(string keyword)
    {
        int saved = Position;
        SkipTrivia();
        int start = Position;
        while (Position < _end &&
               !IsWhiteSpace(_data[Position]) &&
               !IsDelimiter(_data[Position]))
        {
            Position++;
        }

        bool matches = Position - start == keyword.Length &&
                       _data.AsSpan(start, keyword.Length).SequenceEqual(Encoding.ASCII.GetBytes(keyword));
        if (!matches)
            Position = saved;
        return matches;
    }

    public void SkipTrivia()
    {
        while (Position < _end)
        {
            if (IsWhiteSpace(_data[Position]))
            {
                Position++;
                continue;
            }

            if (_data[Position] != '%')
                break;

            while (Position < _end && _data[Position] is not (byte)'\r' and not (byte)'\n')
                Position++;
        }
    }

    private PdfObject ReadNumberReferenceOrKeyword()
    {
        int saved = Position;
        string first = ReadRawToken();
        if (TryParseNumber(first, out PdfNumber? number))
        {
            if (number.IsInteger)
            {
                int afterFirst = Position;
                try
                {
                    string second = ReadRawToken();
                    if (int.TryParse(second, NumberStyles.None, CultureInfo.InvariantCulture, out int generation))
                    {
                        string marker = ReadRawToken();
                        if (marker == "R" &&
                            number.Value is >= 0 and <= int.MaxValue &&
                            generation >= 0)
                        {
                            return new PdfReference((int)number.Value, generation);
                        }
                    }
                }
                catch (PdfFormatException)
                {
                    // The first number remains a valid object at the end of a buffer.
                }

                Position = afterFirst;
            }

            return number;
        }

        return first switch
        {
            "true" => new PdfBoolean(true),
            "false" => new PdfBoolean(false),
            "null" => PdfNull.Instance,
            "" => throw new PdfFormatException("Empty PDF token", saved),
            _ => new PdfKeyword(first)
        };
    }

    private PdfName ReadName()
    {
        Position++;
        var bytes = new List<byte>();
        while (Position < _end &&
               !IsWhiteSpace(_data[Position]) &&
               !IsDelimiter(_data[Position]))
        {
            byte current = _data[Position++];
            if (current == '#' && Position + 1 < _end &&
                TryHex(_data[Position], out int high) &&
                TryHex(_data[Position + 1], out int low))
            {
                bytes.Add((byte)((high << 4) | low));
                Position += 2;
            }
            else
            {
                bytes.Add(current);
            }
        }

        return new PdfName(Encoding.Latin1.GetString(bytes.ToArray()));
    }

    private PdfString ReadLiteralString()
    {
        Position++;
        int nesting = 1;
        var bytes = new List<byte>();
        while (Position < _end && nesting > 0)
        {
            byte current = _data[Position++];
            if (current == '(')
            {
                nesting++;
                bytes.Add(current);
            }
            else if (current == ')')
            {
                nesting--;
                if (nesting > 0)
                    bytes.Add(current);
            }
            else if (current == '\\')
            {
                if (Position >= _end)
                    break;
                byte escaped = _data[Position++];
                switch (escaped)
                {
                    case (byte)'n':
                        bytes.Add((byte)'\n');
                        break;
                    case (byte)'r':
                        bytes.Add((byte)'\r');
                        break;
                    case (byte)'t':
                        bytes.Add((byte)'\t');
                        break;
                    case (byte)'b':
                        bytes.Add((byte)'\b');
                        break;
                    case (byte)'f':
                        bytes.Add((byte)'\f');
                        break;
                    case (byte)'\r':
                        if (Position < _end && _data[Position] == '\n')
                            Position++;
                        break;
                    case (byte)'\n':
                        break;
                    case >= (byte)'0' and <= (byte)'7':
                    {
                        int value = escaped - '0';
                        int digits = 1;
                        while (digits < 3 &&
                               Position < _end &&
                               _data[Position] is >= (byte)'0' and <= (byte)'7')
                        {
                            value = (value << 3) + (_data[Position++] - '0');
                            digits++;
                        }

                        bytes.Add((byte)value);
                        break;
                    }
                    default:
                        bytes.Add(escaped);
                        break;
                }
            }
            else
            {
                bytes.Add(current);
            }
        }

        if (nesting != 0)
            throw Error("Unterminated literal string");
        return new PdfString(bytes.ToArray());
    }

    private PdfString ReadHexString()
    {
        Position++;
        var nibbles = new List<int>();
        while (Position < _end)
        {
            byte current = _data[Position++];
            if (current == '>')
                break;
            if (IsWhiteSpace(current))
                continue;
            if (!TryHex(current, out int value))
                throw Error("Invalid hexadecimal string digit");
            nibbles.Add(value);
        }

        if (nibbles.Count % 2 != 0)
            nibbles.Add(0);
        var bytes = new byte[nibbles.Count / 2];
        for (int index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)((nibbles[index * 2] << 4) | nibbles[index * 2 + 1]);
        return new PdfString(bytes);
    }

    private PdfArray ReadArray(int depth)
    {
        Position++;
        var items = new List<PdfObject>();
        while (true)
        {
            SkipTrivia();
            if (AtEnd)
                throw Error("Unterminated array");
            if (_data[Position] == ']')
            {
                Position++;
                return new PdfArray(items);
            }

            items.Add(ReadObject(depth));
        }
    }

    private PdfDictionary ReadDictionary(int depth)
    {
        Position += 2;
        var items = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        while (true)
        {
            SkipTrivia();
            if (Position + 1 < _end && _data[Position] == '>' && _data[Position + 1] == '>')
            {
                Position += 2;
                return new PdfDictionary(items);
            }

            if (AtEnd)
                throw Error("Unterminated dictionary");
            if (ReadObject(depth) is not PdfName name)
                throw Error("Dictionary key is not a name");
            items[name.Value] = ReadObject(depth);
        }
    }

    private void ConsumeStreamLineEnding()
    {
        if (Position < _end && _data[Position] == '\r')
        {
            Position++;
            if (Position < _end && _data[Position] == '\n')
                Position++;
        }
        else if (Position < _end && _data[Position] == '\n')
        {
            Position++;
        }
        else
        {
            throw Error("The stream keyword is not followed by an end-of-line marker");
        }
    }

    private void TrimTrailingLineEnding(int streamStart, ref int length)
    {
        int end = streamStart + length;
        if (length > 0 && _data[end - 1] == '\n')
        {
            length--;
            if (length > 0 && _data[streamStart + length - 1] == '\r')
                length--;
        }
        else if (length > 0 && _data[end - 1] == '\r')
        {
            length--;
        }
    }

    private int FindKeyword(string keyword)
    {
        ReadOnlySpan<byte> needle = Encoding.ASCII.GetBytes(keyword);
        int relative = _data.AsSpan(Position, _end - Position).IndexOf(needle);
        if (relative < 0)
            throw Error($"Missing {keyword} marker");
        return Position + relative;
    }

    private byte Peek(int distance) =>
        Position + distance < _end ? _data[Position + distance] : (byte)0;

    private PdfFormatException Error(string message) => new(message, Position);

    private static bool TryParseNumber(string token, out PdfNumber number)
    {
        bool isInteger = token.Length > 0 &&
                         token.All(character => char.IsAsciiDigit(character) || character is '+' or '-');
        if (double.TryParse(
                token,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double value))
        {
            number = new PdfNumber(value, isInteger);
            return true;
        }

        number = null!;
        return false;
    }

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';

    private static bool IsDelimiter(byte value) =>
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
            (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or
            (byte)'/' or (byte)'%';

    private static bool TryHex(byte value, out int result)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            result = value - '0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            result = value - 'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            result = value - 'a' + 10;
            return true;
        }

        result = 0;
        return false;
    }
}

internal sealed record PdfIndirectObject(
    int ObjectNumber,
    int Generation,
    PdfObject Value,
    int StartOffset,
    int EndOffset);
