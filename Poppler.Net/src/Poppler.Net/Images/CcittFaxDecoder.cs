namespace Poppler.Images;

/// <summary>
/// CCITT Modified Huffman, Group 3 and Group 4 decoder. The code tables and
/// state machine follow ITU-T T.4/T.6. This implementation was informed by
/// the Apache PDFBox decoder and its Apache-2.0 C# port in PdfPig.
/// </summary>
internal static class CcittFaxDecoder
{
    public static byte[] Decode(
        ReadOnlySpan<byte> input,
        int columns,
        int rows,
        int k,
        bool endOfLine,
        bool encodedByteAlign,
        bool blackIs1)
    {
        if (columns < 1)
            throw new PdfFormatException("CCITT /Columns must be positive.");
        if (rows < 1)
            throw new PdfFormatException("CCITT /Rows must be positive.");

        var decoder = new Decoder(input, columns, k, endOfLine, encodedByteAlign);
        int stride = checked((columns + 7) / 8);
        var result = new byte[checked(stride * rows)];
        for (int row = 0; row < rows; row++)
        {
            Span<byte> destination = result.AsSpan(row * stride, stride);
            decoder.DecodeRow(destination);
            if (!blackIs1)
            {
                for (int index = 0; index < destination.Length; index++)
                    destination[index] = (byte)~destination[index];
                int unused = stride * 8 - columns;
                if (unused > 0)
                    destination[^1] &= (byte)(0xFF << unused);
            }
        }

        return result;
    }

    private sealed class Decoder
    {
        private readonly BitReader _reader;
        private readonly int _columns;
        private readonly int _k;
        private readonly bool _endOfLine;
        private readonly bool _byteAligned;
        private int[] _referenceChanges;
        private int[] _currentChanges;
        private int _referenceCount;
        private int _currentCount;
        private int _lastChangingElement;

        public Decoder(
            ReadOnlySpan<byte> input,
            int columns,
            int k,
            bool endOfLine,
            bool byteAligned)
        {
            _reader = new BitReader(input.ToArray());
            _columns = columns;
            _k = k;
            _endOfLine = endOfLine;
            _byteAligned = byteAligned;
            _referenceChanges = new int[checked(columns + 2)];
            _currentChanges = new int[checked(columns + 2)];
        }

        public void DecodeRow(Span<byte> destination)
        {
            destination.Clear();
            if (_byteAligned)
                _reader.Align();

            if (_k < 0)
            {
                Decode2D();
            }
            else if (_k == 0)
            {
                if (_endOfLine)
                    ReadEndOfLine();
                Decode1D();
            }
            else
            {
                ReadEndOfLine();
                bool oneDimensional = _reader.ReadBit();
                if (oneDimensional)
                    Decode1D();
                else
                    Decode2D();
            }

            WriteRow(destination);
        }

        private void Decode1D()
        {
            int position = 0;
            bool white = true;
            _currentCount = 0;
            while (position < _columns)
            {
                position = checked(position + DecodeRun(white ? WhiteRunTree : BlackRunTree));
                if (position > _columns)
                    throw new PdfFormatException("CCITT run exceeds the row width.");
                _currentChanges[_currentCount++] = position;
                white = !white;
            }
        }

        private void Decode2D()
        {
            _referenceCount = _currentCount;
            (_referenceChanges, _currentChanges) = (_currentChanges, _referenceChanges);
            bool white = true;
            int position = 0;
            _currentCount = 0;
            _lastChangingElement = 0;

            while (position < _columns)
            {
                int mode = DecodeCode(ModeTree, "CCITT 2D mode");
                switch (mode)
                {
                    case HorizontalMode:
                        position = AddRun(position, DecodeRun(white ? WhiteRunTree : BlackRunTree));
                        AddChange(position);
                        position = AddRun(position, DecodeRun(white ? BlackRunTree : WhiteRunTree));
                        AddChange(position);
                        break;
                    case PassMode:
                    {
                        int change = GetNextChangingElement(position, white) + 1;
                        position = change >= _referenceCount
                            ? _columns
                            : _referenceChanges[change];
                        break;
                    }
                    default:
                    {
                        int change = GetNextChangingElement(position, white);
                        position = change < 0 || change >= _referenceCount
                            ? _columns + mode
                            : _referenceChanges[change] + mode;
                        if (position < 0 || position > _columns)
                            throw new PdfFormatException("Invalid CCITT vertical transition.");
                        AddChange(position);
                        white = !white;
                        break;
                    }
                }
            }
        }

        private int GetNextChangingElement(int a0, bool white)
        {
            int start = (_lastChangingElement & ~1) + (white ? 0 : 1);
            if (start > 2)
                start -= 2;
            if (a0 == 0)
                return start;
            for (int index = start; index < _referenceCount; index += 2)
            {
                if (a0 < _referenceChanges[index])
                {
                    _lastChangingElement = index;
                    return index;
                }
            }

            return -1;
        }

        private int AddRun(int position, int run)
        {
            int result = checked(position + run);
            if (result > _columns)
                throw new PdfFormatException("CCITT run exceeds the row width.");
            return result;
        }

        private void AddChange(int position)
        {
            if (_currentCount >= _currentChanges.Length)
                throw new PdfFormatException("Too many CCITT changing elements.");
            _currentChanges[_currentCount++] = position;
        }

        private int DecodeRun(CodeTree tree)
        {
            int total = 0;
            while (true)
            {
                int value = DecodeCode(tree, "CCITT run");
                if (value < 0)
                    throw new PdfFormatException("Unexpected CCITT EOL inside a run.");
                total = checked(total + value);
                if (value < 64)
                    return total;
            }
        }

        private int DecodeCode(CodeTree tree, string description)
        {
            CodeNode? node = tree.Root;
            while (node is not null && !node.IsLeaf)
                node = _reader.ReadBit() ? node.One : node.Zero;
            return node is { IsLeaf: true }
                ? node.Value
                : throw new PdfFormatException($"Unknown {description} code.");
        }

        private void ReadEndOfLine()
        {
            int zeros = 0;
            while (true)
            {
                if (_reader.ReadBit())
                {
                    if (zeros >= 11)
                        return;
                    zeros = 0;
                }
                else
                {
                    zeros++;
                }
            }
        }

        private void WriteRow(Span<byte> destination)
        {
            int position = 0;
            bool white = true;
            for (int index = 0; index <= _currentCount; index++)
            {
                int next = index == _currentCount
                    ? _columns
                    : Math.Min(_columns, _currentChanges[index]);
                if (next < position)
                    throw new PdfFormatException("CCITT changing elements are not ordered.");
                if (!white)
                {
                    for (int pixel = position; pixel < next; pixel++)
                        destination[pixel >> 3] |= (byte)(0x80 >> (pixel & 7));
                }

                position = next;
                white = !white;
            }

            if (position != _columns)
                throw new PdfFormatException("CCITT runs do not fill the row.");
        }
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bitPosition;

        public BitReader(byte[] data) => _data = data;

        public bool ReadBit()
        {
            if (_bitPosition >= _data.Length * 8)
                throw new PdfFormatException("Unexpected end of CCITT data.");
            bool result = (_data[_bitPosition >> 3] &
                           (0x80 >> (_bitPosition & 7))) != 0;
            _bitPosition++;
            return result;
        }

        public void Align() => _bitPosition = (_bitPosition + 7) & ~7;
    }

    private sealed class CodeNode
    {
        public CodeNode? Zero { get; set; }
        public CodeNode? One { get; set; }
        public bool IsLeaf { get; set; }
        public int Value { get; set; }
    }

    private sealed class CodeTree
    {
        public CodeNode Root { get; } = new();

        public void Add(int length, int code, int value)
        {
            CodeNode node = Root;
            for (int bit = length - 1; bit >= 0; bit--)
            {
                bool one = (code & (1 << bit)) != 0;
                CodeNode? next = one ? node.One : node.Zero;
                if (next is null)
                {
                    next = new CodeNode();
                    if (one)
                        node.One = next;
                    else
                        node.Zero = next;
                }

                node = next;
            }

            if (node.IsLeaf)
                throw new InvalidOperationException("Duplicate CCITT code.");
            node.IsLeaf = true;
            node.Value = value;
        }
    }

    private const int PassMode = -3000;
    private const int HorizontalMode = -4000;
    private static readonly CodeTree BlackRunTree = BuildBlackTree();
    private static readonly CodeTree WhiteRunTree = BuildWhiteTree();
    private static readonly CodeTree ModeTree = BuildModeTree();

    private static CodeTree BuildModeTree()
    {
        var tree = new CodeTree();
        tree.Add(4, 0x1, PassMode);
        tree.Add(3, 0x1, HorizontalMode);
        tree.Add(1, 0x1, 0);
        tree.Add(3, 0x3, 1);
        tree.Add(6, 0x3, 2);
        tree.Add(7, 0x3, 3);
        tree.Add(3, 0x2, -1);
        tree.Add(6, 0x2, -2);
        tree.Add(7, 0x2, -3);
        return tree;
    }

    private static CodeTree BuildBlackTree() =>
        BuildRunTree(
            firstLength: 2,
            new[]
            {
                new[] { 0x2, 0x3 },
                new[] { 0x2, 0x3 },
                new[] { 0x2, 0x3 },
                new[] { 0x3 },
                new[] { 0x4, 0x5 },
                new[] { 0x4, 0x5, 0x7 },
                new[] { 0x4, 0x7 },
                new[] { 0x18 },
                new[] { 0x17, 0x18, 0x37, 0x8, 0xF },
                new[] { 0x17, 0x18, 0x28, 0x37, 0x67, 0x68, 0x6C, 0x8, 0xC, 0xD },
                new[]
                {
                    0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x1C, 0x1D, 0x1E, 0x1F,
                    0x24, 0x27, 0x28, 0x2B, 0x2C, 0x33, 0x34, 0x35, 0x37, 0x38,
                    0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x5B,
                    0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D,
                    0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xD2, 0xD3, 0xD4, 0xD5,
                    0xD6, 0xD7, 0xDA, 0xDB
                },
                new[]
                {
                    0x4A, 0x4B, 0x4C, 0x4D, 0x52, 0x53, 0x54, 0x55, 0x5A, 0x5B,
                    0x64, 0x65, 0x6C, 0x6D, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77
                }
            },
            new[]
            {
                new[] { 3, 2 },
                new[] { 1, 4 },
                new[] { 6, 5 },
                new[] { 7 },
                new[] { 9, 8 },
                new[] { 10, 11, 12 },
                new[] { 13, 14 },
                new[] { 15 },
                new[] { 16, 17, 0, 18, 64 },
                new[] { 24, 25, 23, 22, 19, 20, 21, 1792, 1856, 1920 },
                new[]
                {
                    1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560,
                    52, 55, 56, 59, 60, 320, 384, 448, 53, 54, 50, 51, 44, 45,
                    46, 47, 57, 58, 61, 256, 48, 49, 62, 63, 30, 31, 32, 33, 40,
                    41, 128, 192, 26, 27, 28, 29, 34, 35, 36, 37, 38, 39, 42, 43
                },
                new[]
                {
                    640, 704, 768, 832, 1280, 1344, 1408, 1472, 1536, 1600,
                    1664, 1728, 512, 576, 896, 960, 1024, 1088, 1152, 1216
                }
            });

    private static CodeTree BuildWhiteTree() =>
        BuildRunTree(
            firstLength: 4,
            new[]
            {
                new[] { 0x7, 0x8, 0xB, 0xC, 0xE, 0xF },
                new[] { 0x12, 0x13, 0x14, 0x1B, 0x7, 0x8 },
                new[] { 0x17, 0x18, 0x2A, 0x2B, 0x3, 0x34, 0x35, 0x7, 0x8 },
                new[] { 0x13, 0x17, 0x18, 0x24, 0x27, 0x28, 0x2B, 0x3, 0x37, 0x4, 0x8, 0xC },
                new[]
                {
                    0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x1A, 0x1B, 0x2, 0x24,
                    0x25, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x3, 0x32, 0x33,
                    0x34, 0x35, 0x36, 0x37, 0x4, 0x4A, 0x4B, 0x5, 0x52, 0x53,
                    0x54, 0x55, 0x58, 0x59, 0x5A, 0x5B, 0x64, 0x65, 0x67, 0x68,
                    0xA, 0xB
                },
                new[]
                {
                    0x98, 0x99, 0x9A, 0x9B, 0xCC, 0xCD, 0xD2, 0xD3,
                    0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xDB
                },
                Array.Empty<int>(),
                new[] { 0x8, 0xC, 0xD },
                new[] { 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x1C, 0x1D, 0x1E, 0x1F }
            },
            new[]
            {
                new[] { 2, 3, 4, 5, 6, 7 },
                new[] { 128, 8, 9, 64, 10, 11 },
                new[] { 192, 1664, 16, 17, 13, 14, 15, 1, 12 },
                new[] { 26, 21, 28, 27, 18, 24, 25, 22, 256, 23, 20, 19 },
                new[]
                {
                    33, 34, 35, 36, 37, 38, 31, 32, 29, 53, 54, 39, 40, 41,
                    42, 43, 44, 30, 61, 62, 63, 0, 320, 384, 45, 59, 60, 46,
                    49, 50, 51, 52, 55, 56, 57, 58, 448, 512, 640, 576, 47, 48
                },
                new[]
                {
                    1472, 1536, 1600, 1728, 704, 768, 832, 896,
                    960, 1024, 1088, 1152, 1216, 1280, 1344, 1408
                },
                Array.Empty<int>(),
                new[] { 1792, 1856, 1920 },
                new[] { 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560 }
            });

    private static CodeTree BuildRunTree(
        int firstLength,
        IReadOnlyList<int[]> codes,
        IReadOnlyList<int[]> values)
    {
        var tree = new CodeTree();
        for (int group = 0; group < codes.Count; group++)
        {
            if (codes[group].Length != values[group].Length)
                throw new InvalidOperationException("Invalid CCITT table.");
            for (int index = 0; index < codes[group].Length; index++)
                tree.Add(firstLength + group, codes[group][index], values[group][index]);
        }

        return tree;
    }
}
