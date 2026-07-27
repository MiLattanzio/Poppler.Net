using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using GraphicsMatrix = global::Poppler.PdfMatrix;

namespace Poppler.Text;

/// <summary>
/// Bounded CFF1/CFF2 Type 2 charstring reader. It covers raw Type1C,
/// CIDFontType0C and CFF/CFF2 tables inside OpenType without FreeType.
/// </summary>
internal sealed class PdfCffFont
{
    private const int MaximumEntries = 1_000_000;
    private const int MaximumSubroutineDepth = 32;
    private const int MaximumCharStringOperations = 1_000_000;

    private static readonly string[] BasicStandardStrings =
    {
        ".notdef", "space", "exclam", "quotedbl", "numbersign", "dollar",
        "percent", "ampersand", "quoteright", "parenleft", "parenright",
        "asterisk", "plus", "comma", "hyphen", "period", "slash", "zero",
        "one", "two", "three", "four", "five", "six", "seven", "eight",
        "nine", "colon", "semicolon", "less", "equal", "greater", "question",
        "at", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L",
        "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y",
        "Z", "bracketleft", "backslash", "bracketright", "asciicircum",
        "underscore", "quoteleft", "a", "b", "c", "d", "e", "f", "g", "h",
        "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u",
        "v", "w", "x", "y", "z", "braceleft", "bar", "braceright",
        "asciitilde"
    };

    private readonly IReadOnlyList<byte[]> _charStrings;
    private readonly IReadOnlyList<byte[]> _globalSubroutines;
    private readonly PrivateData[] _privateData;
    private readonly int[] _fontDictionaryByGlyph;
    private readonly Dictionary<uint, int> _glyphByCid;
    private readonly Dictionary<int, int> _glyphByUnicode;
    private readonly GraphicsMatrix _fontMatrix;
    private readonly int[] _variationRegionCounts;
    private readonly bool _isCff2;

    private PdfCffFont(
        IReadOnlyList<byte[]> charStrings,
        IReadOnlyList<byte[]> globalSubroutines,
        PrivateData[] privateData,
        int[] fontDictionaryByGlyph,
        Dictionary<uint, int> glyphByCid,
        Dictionary<int, int> glyphByUnicode,
        GraphicsMatrix fontMatrix,
        int[] variationRegionCounts,
        bool isCff2)
    {
        _charStrings = charStrings;
        _globalSubroutines = globalSubroutines;
        _privateData = privateData;
        _fontDictionaryByGlyph = fontDictionaryByGlyph;
        _glyphByCid = glyphByCid;
        _glyphByUnicode = glyphByUnicode;
        _fontMatrix = fontMatrix;
        _variationRegionCounts = variationRegionCounts;
        _isCff2 = isCff2;
    }

    public static PdfCffFont? TryParse(byte[] program)
    {
        try
        {
            ReadOnlyMemory<byte> cffMemory = FindCff(program);
            ReadOnlySpan<byte> cff = cffMemory.Span;
            if (cff.Length < 4 || cff[0] is not (1 or 2))
                return null;
            if (cff[0] == 2)
                return ParseCff2(program, cff);
            int position = cff[2];
            if (position < 4 || position > cff.Length)
                return null;

            _ = ReadIndex(cff, ref position);
            IReadOnlyList<byte[]> topIndex = ReadIndex(cff, ref position);
            IReadOnlyList<byte[]> strings = ReadIndex(cff, ref position);
            IReadOnlyList<byte[]> globalSubroutines = ReadIndex(cff, ref position);
            if (topIndex.Count == 0)
                return null;
            Dictionary<int, double[]> top = ReadDictionary(topIndex[0]);
            int charStringsOffset = Integer(top, 17);
            if (charStringsOffset <= 0 || charStringsOffset >= cff.Length)
                return null;
            int charStringsPosition = charStringsOffset;
            IReadOnlyList<byte[]> charStrings = ReadIndex(cff, ref charStringsPosition);
            if (charStrings.Count == 0 || charStrings.Count > MaximumEntries)
                return null;

            bool cidFont = top.ContainsKey(1230);
            GraphicsMatrix fontMatrix = ReadFontMatrix(top);
            ushort[] charset = ReadCharset(
                cff,
                Integer(top, 15),
                charStrings.Count);
            var glyphByCid = new Dictionary<uint, int>();
            var glyphByUnicode = new Dictionary<int, int>();
            for (int glyph = 0; glyph < charset.Length; glyph++)
            {
                ushort value = charset[glyph];
                if (cidFont)
                {
                    glyphByCid.TryAdd(value, glyph);
                }
                else
                {
                    string? name = ResolveString(value, strings);
                    if (name is null)
                        continue;
                    string unicode = PdfGlyphNames.ToUnicode(name);
                    Rune rune = unicode.EnumerateRunes().FirstOrDefault();
                    if (rune.Value != 0 && rune.Value != 0xFFFD)
                        glyphByUnicode.TryAdd(rune.Value, glyph);
                }
            }

            PrivateData[] privateData;
            int[] fdByGlyph;
            int fdArrayOffset = Integer(top, 1236);
            if (cidFont && fdArrayOffset > 0)
            {
                int fdPosition = fdArrayOffset;
                IReadOnlyList<byte[]> fdIndex = ReadIndex(cff, ref fdPosition);
                if (fdIndex.Count == 0 || fdIndex.Count > 256)
                    return null;
                privateData = new PrivateData[fdIndex.Count];
                for (int index = 0; index < fdIndex.Count; index++)
                {
                    privateData[index] =
                        ReadPrivateData(cff, ReadDictionary(fdIndex[index]));
                }
                fdByGlyph = ReadFdSelect(
                    cff,
                    Integer(top, 1237),
                    charStrings.Count,
                    privateData.Length);
            }
            else
            {
                privateData = new[] { ReadPrivateData(cff, top) };
                fdByGlyph = new int[charStrings.Count];
            }

            return new PdfCffFont(
                charStrings,
                globalSubroutines,
                privateData,
                fdByGlyph,
                glyphByCid,
                glyphByUnicode,
                fontMatrix,
                Array.Empty<int>(),
                isCff2: false);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or
            IndexOutOfRangeException or
            OverflowException or
            PdfFormatException)
        {
            return null;
        }
    }

    private static PdfCffFont? ParseCff2(
        byte[] program,
        ReadOnlySpan<byte> cff)
    {
        if (cff.Length < 5 || cff[1] != 0)
            return null;
        int headerSize = cff[2];
        int topLength = BinaryPrimitives.ReadUInt16BigEndian(cff[3..]);
        if (headerSize < 5 ||
            topLength < 1 ||
            headerSize > cff.Length - topLength)
        {
            return null;
        }

        Dictionary<int, double[]> top =
            ReadDictionary(cff.Slice(headerSize, topLength));
        int position = headerSize + topLength;
        IReadOnlyList<byte[]> globalSubroutines =
            ReadIndex(cff, ref position, cff2: true);
        int charStringsOffset = Integer(top, 17);
        if (charStringsOffset <= 0 || charStringsOffset >= cff.Length)
            return null;
        int charStringsPosition = charStringsOffset;
        IReadOnlyList<byte[]> charStrings =
            ReadIndex(cff, ref charStringsPosition, cff2: true);
        if (charStrings.Count == 0 || charStrings.Count > MaximumEntries)
            return null;

        PrivateData[] privateData;
        int[] fdByGlyph;
        int fdArrayOffset = Integer(top, 1236);
        if (fdArrayOffset > 0)
        {
            int fdPosition = fdArrayOffset;
            IReadOnlyList<byte[]> fdIndex =
                ReadIndex(cff, ref fdPosition, cff2: true);
            if (fdIndex.Count == 0 || fdIndex.Count > 256)
                return null;
            privateData = new PrivateData[fdIndex.Count];
            for (int index = 0; index < fdIndex.Count; index++)
            {
                privateData[index] = ReadPrivateData(
                    cff,
                    ReadDictionary(fdIndex[index]),
                    cff2: true);
            }
            fdByGlyph = ReadFdSelect(
                cff,
                Integer(top, 1237),
                charStrings.Count,
                privateData.Length);
        }
        else
        {
            privateData = new[]
            {
                ReadPrivateData(cff, top, cff2: true)
            };
            fdByGlyph = new int[charStrings.Count];
        }

        var glyphByCid = new Dictionary<uint, int>();
        for (int glyph = 0; glyph < charStrings.Count; glyph++)
            glyphByCid[(uint)glyph] = glyph;
        GraphicsMatrix fontMatrix = ReadFontMatrix(top);
        if (!top.ContainsKey(1207) &&
            TryReadUnitsPerEm(program, out int unitsPerEm))
        {
            double scale = 1.0 / unitsPerEm;
            fontMatrix = new GraphicsMatrix(scale, 0, 0, scale, 0, 0);
        }
        int[] variationRegionCounts = ReadVariationRegionCounts(
            cff,
            Integer(top, 24));
        return new PdfCffFont(
            charStrings,
            globalSubroutines,
            privateData,
            fdByGlyph,
            glyphByCid,
            new Dictionary<int, int>(),
            fontMatrix,
            variationRegionCounts,
            isCff2: true);
    }

    public bool TryGetGlyphByCid(
        uint cid,
        out PdfGraphicsPath path,
        out double advance)
    {
        if (!_glyphByCid.TryGetValue(cid, out int glyph))
        {
            path = EmptyPath();
            advance = 0;
            return false;
        }

        return TryGetGlyph((uint)glyph, out path, out advance);
    }

    public bool TryGetGlyph(
        Rune rune,
        out PdfGraphicsPath path,
        out double advance)
    {
        if (!_glyphByUnicode.TryGetValue(rune.Value, out int glyph))
        {
            path = EmptyPath();
            advance = 0;
            return false;
        }

        return TryGetGlyph((uint)glyph, out path, out advance);
    }

    public bool TryGetGlyph(
        uint glyphId,
        out PdfGraphicsPath path,
        out double advance)
    {
        path = EmptyPath();
        advance = 0;
        if (glyphId >= (uint)_charStrings.Count)
            return false;
        int fd = _fontDictionaryByGlyph[(int)glyphId];
        if ((uint)fd >= (uint)_privateData.Length)
            return false;
        PrivateData privateData = _privateData[fd];
        var state = new CharStringState(
            _fontMatrix,
            privateData.DefaultWidth,
            privateData.NominalWidth,
            hasWidths: !_isCff2,
            _variationRegionCounts);
        if (!Execute(
                _charStrings[(int)glyphId],
                privateData,
                state,
                depth: 0,
                subroutine: false))
        {
            return false;
        }

        state.CloseContour();
        path = new PdfGraphicsPath(state.Segments);
        advance = Math.Abs(
            _fontMatrix.Transform(state.Width, 0).X -
            _fontMatrix.Transform(0, 0).X);
        return !path.IsEmpty;
    }

    private bool Execute(
        ReadOnlySpan<byte> program,
        PrivateData privateData,
        CharStringState state,
        int depth,
        bool subroutine)
    {
        if (depth > MaximumSubroutineDepth)
            return false;
        int position = 0;
        while (position < program.Length)
        {
            if (++state.OperationCount > MaximumCharStringOperations)
                return false;
            byte value = program[position++];
            if (TryReadCharStringNumber(program, ref position, value, out double number))
            {
                if (state.Stack.Count >= 96)
                    return false;
                state.Stack.Add(number);
                continue;
            }

            switch (value)
            {
                case 1:
                case 3:
                case 18:
                case 23:
                    state.TakeWidthForStem();
                    state.HintCount += state.Stack.Count / 2;
                    state.Stack.Clear();
                    break;
                case 4:
                    state.TakeWidthForMove(1);
                    if (!state.TryPop(out double vertical))
                        return false;
                    state.Move(0, vertical);
                    state.Stack.Clear();
                    break;
                case 5:
                    if (state.Stack.Count < 2 || state.Stack.Count % 2 != 0)
                        return false;
                    for (int index = 0; index < state.Stack.Count; index += 2)
                        state.Line(state.Stack[index], state.Stack[index + 1]);
                    state.Stack.Clear();
                    break;
                case 6:
                case 7:
                    if (state.Stack.Count == 0)
                        return false;
                    bool horizontal = value == 6;
                    foreach (double delta in state.Stack)
                    {
                        state.Line(horizontal ? delta : 0, horizontal ? 0 : delta);
                        horizontal = !horizontal;
                    }
                    state.Stack.Clear();
                    break;
                case 8:
                    if (state.Stack.Count < 6 || state.Stack.Count % 6 != 0)
                        return false;
                    for (int index = 0; index < state.Stack.Count; index += 6)
                    {
                        state.Curve(
                            state.Stack[index],
                            state.Stack[index + 1],
                            state.Stack[index + 2],
                            state.Stack[index + 3],
                            state.Stack[index + 4],
                            state.Stack[index + 5]);
                    }
                    state.Stack.Clear();
                    break;
                case 10:
                    if (!state.TryPop(out double localSubroutine))
                        return false;
                    int localIndex = checked(
                        (int)localSubroutine + SubroutineBias(privateData.Subroutines.Count));
                    if ((uint)localIndex >= (uint)privateData.Subroutines.Count ||
                        !Execute(
                            privateData.Subroutines[localIndex],
                            privateData,
                            state,
                            depth + 1,
                            subroutine: true))
                    {
                        return false;
                    }
                    if (state.Ended)
                        return true;
                    break;
                case 11:
                    return subroutine;
                case 14:
                    if (_isCff2)
                        return false;
                    state.TakeWidthForEnd();
                    state.Stack.Clear();
                    state.CloseContour();
                    state.Ended = true;
                    return true;
                case 15 when _isCff2:
                    if (!state.SetVariationStoreIndex())
                        return false;
                    break;
                case 16 when _isCff2:
                    if (!state.ApplyBlend())
                        return false;
                    break;
                case 19:
                case 20:
                    state.TakeWidthForStem();
                    state.HintCount += state.Stack.Count / 2;
                    state.Stack.Clear();
                    int maskBytes = (state.HintCount + 7) / 8;
                    if (position > program.Length - maskBytes)
                        return false;
                    position += maskBytes;
                    break;
                case 21:
                    state.TakeWidthForMove(2);
                    if (state.Stack.Count != 2)
                        return false;
                    state.Move(state.Stack[0], state.Stack[1]);
                    state.Stack.Clear();
                    break;
                case 22:
                    state.TakeWidthForMove(1);
                    if (!state.TryPop(out double horizontalMove))
                        return false;
                    state.Move(horizontalMove, 0);
                    state.Stack.Clear();
                    break;
                case 24:
                    if (state.Stack.Count < 8 ||
                        (state.Stack.Count - 2) % 6 != 0)
                    {
                        return false;
                    }
                    int curveEnd = state.Stack.Count - 2;
                    for (int index = 0; index < curveEnd; index += 6)
                    {
                        state.Curve(
                            state.Stack[index],
                            state.Stack[index + 1],
                            state.Stack[index + 2],
                            state.Stack[index + 3],
                            state.Stack[index + 4],
                            state.Stack[index + 5]);
                    }
                    state.Line(state.Stack[^2], state.Stack[^1]);
                    state.Stack.Clear();
                    break;
                case 25:
                    if (state.Stack.Count < 8 ||
                        (state.Stack.Count - 6) % 2 != 0)
                    {
                        return false;
                    }
                    int lineEnd = state.Stack.Count - 6;
                    for (int index = 0; index < lineEnd; index += 2)
                        state.Line(state.Stack[index], state.Stack[index + 1]);
                    state.Curve(
                        state.Stack[lineEnd],
                        state.Stack[lineEnd + 1],
                        state.Stack[lineEnd + 2],
                        state.Stack[lineEnd + 3],
                        state.Stack[lineEnd + 4],
                        state.Stack[lineEnd + 5]);
                    state.Stack.Clear();
                    break;
                case 26:
                    if (!StraightCurves(state, vertical: true))
                        return false;
                    break;
                case 27:
                    if (!StraightCurves(state, vertical: false))
                        return false;
                    break;
                case 28:
                    if (position > program.Length - 2)
                        return false;
                    state.Stack.Add(BinaryPrimitives.ReadInt16BigEndian(program[position..]));
                    position += 2;
                    break;
                case 29:
                    if (!state.TryPop(out double globalSubroutine))
                        return false;
                    int globalIndex = checked(
                        (int)globalSubroutine + SubroutineBias(_globalSubroutines.Count));
                    if ((uint)globalIndex >= (uint)_globalSubroutines.Count ||
                        !Execute(
                            _globalSubroutines[globalIndex],
                            privateData,
                            state,
                            depth + 1,
                            subroutine: true))
                    {
                        return false;
                    }
                    if (state.Ended)
                        return true;
                    break;
                case 30:
                    if (!AlternatingCurves(state, verticalFirst: true))
                        return false;
                    break;
                case 31:
                    if (!AlternatingCurves(state, verticalFirst: false))
                        return false;
                    break;
                case 12:
                    if (position >= program.Length ||
                        !ExecuteEscape(program[position++], state))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        return _isCff2 || subroutine || state.Ended;
    }

    private static bool StraightCurves(
        CharStringState state,
        bool vertical)
    {
        if (state.Stack.Count < 4)
            return false;
        int index = 0;
        double firstOrthogonal = state.Stack.Count % 4 == 1
            ? state.Stack[index++]
            : 0;
        while (index + 3 < state.Stack.Count)
        {
            if (vertical)
            {
                state.Curve(
                    firstOrthogonal,
                    state.Stack[index],
                    state.Stack[index + 1],
                    state.Stack[index + 2],
                    0,
                    state.Stack[index + 3]);
            }
            else
            {
                state.Curve(
                    state.Stack[index],
                    firstOrthogonal,
                    state.Stack[index + 1],
                    state.Stack[index + 2],
                    state.Stack[index + 3],
                    0);
            }
            firstOrthogonal = 0;
            index += 4;
        }

        bool consumed = index == state.Stack.Count;
        state.Stack.Clear();
        return consumed;
    }

    private static bool AlternatingCurves(
        CharStringState state,
        bool verticalFirst)
    {
        if (state.Stack.Count < 4 ||
            state.Stack.Count % 4 is not (0 or 1))
        {
            return false;
        }
        bool hasFinal = state.Stack.Count % 4 == 1;
        int end = state.Stack.Count - (hasFinal ? 1 : 0);
        double final = hasFinal ? state.Stack[^1] : 0;
        bool vertical = verticalFirst;
        for (int index = 0; index < end; index += 4)
        {
            bool last = index + 4 == end;
            if (vertical)
            {
                state.Curve(
                    0,
                    state.Stack[index],
                    state.Stack[index + 1],
                    state.Stack[index + 2],
                    state.Stack[index + 3],
                    last ? final : 0);
            }
            else
            {
                state.Curve(
                    state.Stack[index],
                    0,
                    state.Stack[index + 1],
                    state.Stack[index + 2],
                    last ? final : 0,
                    state.Stack[index + 3]);
            }
            vertical = !vertical;
        }
        state.Stack.Clear();
        return true;
    }

    private static bool ExecuteEscape(byte operation, CharStringState state)
    {
        switch (operation)
        {
            case 3:
                if (!state.Binary(static (left, right) =>
                        left != 0 && right != 0 ? 1 : 0))
                    return false;
                return true;
            case 4:
                if (!state.Binary(static (left, right) =>
                        left != 0 || right != 0 ? 1 : 0))
                    return false;
                return true;
            case 5:
                if (!state.Unary(static value => value == 0 ? 1 : 0))
                    return false;
                return true;
            case 9:
                if (!state.Unary(Math.Abs))
                    return false;
                return true;
            case 10:
                if (!state.Binary(static (left, right) => left + right))
                    return false;
                return true;
            case 11:
                if (!state.Binary(static (left, right) => left - right))
                    return false;
                return true;
            case 12:
                if (!state.Binary(static (left, right) =>
                        right == 0 ? double.NaN : left / right))
                    return false;
                return double.IsFinite(state.Stack[^1]);
            case 14:
                if (!state.Unary(static value => -value))
                    return false;
                return true;
            case 15:
                if (!state.Binary(static (left, right) => left == right ? 1 : 0))
                    return false;
                return true;
            case 18:
                return state.TryPop(out _);
            case 20:
                return state.PutTransient();
            case 21:
                return state.GetTransient();
            case 22:
                return state.IfElse();
            case 23:
                state.Stack.Add(0.5);
                return true;
            case 24:
                if (!state.Binary(static (left, right) => left * right))
                    return false;
                return double.IsFinite(state.Stack[^1]);
            case 26:
                if (!state.Unary(static value =>
                        value < 0 ? double.NaN : Math.Sqrt(value)))
                    return false;
                return double.IsFinite(state.Stack[^1]);
            case 27:
                if (state.Stack.Count == 0 || state.Stack.Count >= 96)
                    return false;
                state.Stack.Add(state.Stack[^1]);
                return true;
            case 28:
                if (state.Stack.Count < 2)
                    return false;
                (state.Stack[^2], state.Stack[^1]) =
                    (state.Stack[^1], state.Stack[^2]);
                return true;
            case 29:
                return state.Index();
            case 30:
                return state.Roll();
            case 34 when state.Stack.Count == 7:
                state.Curve(
                    state.Stack[0], 0,
                    state.Stack[1], state.Stack[2],
                    state.Stack[3], 0);
                state.Curve(
                    state.Stack[4], 0,
                    state.Stack[5], -state.Stack[2],
                    state.Stack[6], 0);
                break;
            case 35 when state.Stack.Count == 13:
                state.Curve(
                    state.Stack[0], state.Stack[1],
                    state.Stack[2], state.Stack[3],
                    state.Stack[4], state.Stack[5]);
                state.Curve(
                    state.Stack[6], state.Stack[7],
                    state.Stack[8], state.Stack[9],
                    state.Stack[10], state.Stack[11]);
                break;
            case 36 when state.Stack.Count == 9:
                state.Curve(
                    state.Stack[0], state.Stack[1],
                    state.Stack[2], state.Stack[3],
                    state.Stack[4], 0);
                state.Curve(
                    state.Stack[5], 0,
                    state.Stack[6], state.Stack[7],
                    state.Stack[8],
                    -(state.Stack[1] + state.Stack[3] + state.Stack[7]));
                break;
            case 37 when state.Stack.Count == 11:
            {
                double dx = state.Stack[0] + state.Stack[2] + state.Stack[4] +
                            state.Stack[6] + state.Stack[8];
                double dy = state.Stack[1] + state.Stack[3] + state.Stack[5] +
                            state.Stack[7] + state.Stack[9];
                double lastX = Math.Abs(dx) > Math.Abs(dy)
                    ? state.Stack[10]
                    : -dx;
                double lastY = Math.Abs(dx) > Math.Abs(dy)
                    ? -dy
                    : state.Stack[10];
                state.Curve(
                    state.Stack[0], state.Stack[1],
                    state.Stack[2], state.Stack[3],
                    state.Stack[4], state.Stack[5]);
                state.Curve(
                    state.Stack[6], state.Stack[7],
                    state.Stack[8], state.Stack[9],
                    lastX, lastY);
                break;
            }
            default:
                return false;
        }

        state.Stack.Clear();
        return true;
    }

    private static ReadOnlyMemory<byte> FindCff(byte[] program)
    {
        if (program.Length >= 4 && program[0] is 1 or 2)
            return program;
        if (program.Length < 12 ||
            !program.AsSpan(0, 4).SequenceEqual("OTTO"u8))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(4, 2));
        if (tableCount < 1 ||
            tableCount > 4096 ||
            checked(12 + tableCount * 16) > program.Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        for (int index = 0; index < tableCount; index++)
        {
            int record = 12 + index * 16;
            if (!program.AsSpan(record, 4).SequenceEqual("CFF "u8) &&
                !program.AsSpan(record, 4).SequenceEqual("CFF2"u8))
                continue;
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(
                program.AsSpan(record + 8, 4));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(
                program.AsSpan(record + 12, 4));
            if (offset <= int.MaxValue &&
                length <= int.MaxValue &&
                (ulong)offset + length <= (ulong)program.Length)
            {
                return program.AsMemory((int)offset, (int)length);
            }
        }

        return ReadOnlyMemory<byte>.Empty;
    }

    private static IReadOnlyList<byte[]> ReadIndex(
        ReadOnlySpan<byte> cff,
        ref int position,
        bool cff2 = false)
    {
        int countSize = cff2 ? 4 : 2;
        if (position > cff.Length - countSize)
            throw new PdfFormatException("CFF INDEX is truncated.");
        uint rawCount = cff2
            ? BinaryPrimitives.ReadUInt32BigEndian(cff[position..])
            : BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
        position += countSize;
        if (rawCount > int.MaxValue)
            throw new PdfFormatException("CFF INDEX is too large.");
        int count = (int)rawCount;
        if (count == 0)
            return Array.Empty<byte[]>();
        if (count > MaximumEntries || position >= cff.Length)
            throw new PdfFormatException("CFF INDEX is invalid.");
        int offsetSize = cff[position++];
        if (offsetSize is < 1 or > 4 ||
            position > cff.Length - checked((count + 1) * offsetSize))
        {
            throw new PdfFormatException("CFF INDEX offsets are invalid.");
        }

        var offsets = new int[count + 1];
        for (int index = 0; index <= count; index++)
        {
            uint offset = 0;
            for (int part = 0; part < offsetSize; part++)
                offset = (offset << 8) | cff[position++];
            if (offset == 0 || offset > int.MaxValue)
                throw new PdfFormatException("CFF INDEX contains an invalid offset.");
            offsets[index] = (int)offset - 1;
        }

        int dataLength = offsets[^1];
        if (dataLength < 0 || position > cff.Length - dataLength)
            throw new PdfFormatException("CFF INDEX data is truncated.");
        var result = new byte[count][];
        for (int index = 0; index < count; index++)
        {
            int start = offsets[index];
            int end = offsets[index + 1];
            if (start < 0 || end < start || end > dataLength)
                throw new PdfFormatException("CFF INDEX range is invalid.");
            result[index] = cff.Slice(position + start, end - start).ToArray();
        }

        position += dataLength;
        return result;
    }

    private static Dictionary<int, double[]> ReadDictionary(ReadOnlySpan<byte> bytes)
    {
        var result = new Dictionary<int, double[]>();
        var operands = new List<double>();
        int position = 0;
        while (position < bytes.Length)
        {
            byte value = bytes[position++];
            if (TryReadDictionaryNumber(bytes, ref position, value, out double number))
            {
                operands.Add(number);
                continue;
            }
            int operation = value == 12
                ? 1200 + bytes[position++]
                : value;
            result[operation] = operands.ToArray();
            operands.Clear();
        }

        return result;
    }

    private static bool TryReadDictionaryNumber(
        ReadOnlySpan<byte> bytes,
        ref int position,
        byte value,
        out double number)
    {
        number = 0;
        if (value is >= 32 and <= 246)
        {
            number = value - 139;
            return true;
        }
        if (value is >= 247 and <= 250)
        {
            number = (value - 247) * 256 + bytes[position++] + 108;
            return true;
        }
        if (value is >= 251 and <= 254)
        {
            number = -(value - 251) * 256 - bytes[position++] - 108;
            return true;
        }
        if (value == 28)
        {
            number = BinaryPrimitives.ReadInt16BigEndian(bytes[position..]);
            position += 2;
            return true;
        }
        if (value == 29)
        {
            number = BinaryPrimitives.ReadInt32BigEndian(bytes[position..]);
            position += 4;
            return true;
        }
        if (value == 30)
        {
            var builder = new System.Text.StringBuilder();
            bool done = false;
            while (!done && position < bytes.Length)
            {
                byte packed = bytes[position++];
                AppendRealNibble(packed >> 4, builder, ref done);
                if (!done)
                    AppendRealNibble(packed & 15, builder, ref done);
            }
            return double.TryParse(
                builder.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number);
        }

        return false;
    }

    private static void AppendRealNibble(
        int nibble,
        System.Text.StringBuilder builder,
        ref bool done)
    {
        switch (nibble)
        {
            case <= 9:
                builder.Append((char)('0' + nibble));
                break;
            case 10:
                builder.Append('.');
                break;
            case 11:
                builder.Append('E');
                break;
            case 12:
                builder.Append("E-");
                break;
            case 14:
                builder.Append('-');
                break;
            case 15:
                done = true;
                break;
        }
    }

    private static bool TryReadCharStringNumber(
        ReadOnlySpan<byte> bytes,
        ref int position,
        byte value,
        out double number)
    {
        number = 0;
        if (value is >= 32 and <= 246)
        {
            number = value - 139;
            return true;
        }
        if (value is >= 247 and <= 250)
        {
            if (position >= bytes.Length)
                return false;
            number = (value - 247) * 256 + bytes[position++] + 108;
            return true;
        }
        if (value is >= 251 and <= 254)
        {
            if (position >= bytes.Length)
                return false;
            number = -(value - 251) * 256 - bytes[position++] - 108;
            return true;
        }
        if (value == 255)
        {
            if (position > bytes.Length - 4)
                return false;
            number = BinaryPrimitives.ReadInt32BigEndian(bytes[position..]) / 65536.0;
            position += 4;
            return true;
        }

        return false;
    }

    private static PrivateData ReadPrivateData(
        ReadOnlySpan<byte> cff,
        IReadOnlyDictionary<int, double[]> dictionary,
        bool cff2 = false)
    {
        if (!dictionary.TryGetValue(18, out double[]? range) || range.Length < 2)
            return PrivateData.Empty;
        int size = checked((int)range[0]);
        int offset = checked((int)range[1]);
        if (size < 0 || offset < 0 || offset > cff.Length - size)
            throw new PdfFormatException("CFF Private DICT is invalid.");
        Dictionary<int, double[]> privateDictionary =
            ReadDictionary(cff.Slice(offset, size));
        double defaultWidth = Number(privateDictionary, 20, 0);
        double nominalWidth = Number(privateDictionary, 21, 0);
        IReadOnlyList<byte[]> subroutines = Array.Empty<byte[]>();
        int relativeSubroutines = Integer(privateDictionary, 19);
        if (relativeSubroutines > 0)
        {
            int subroutinePosition = checked(offset + relativeSubroutines);
            subroutines = ReadIndex(cff, ref subroutinePosition, cff2);
        }

        return new PrivateData(defaultWidth, nominalWidth, subroutines);
    }

    private static ushort[] ReadCharset(
        ReadOnlySpan<byte> cff,
        int offset,
        int glyphCount)
    {
        var charset = new ushort[glyphCount];
        if (glyphCount <= 1)
            return charset;
        if (offset == 0)
        {
            for (int glyph = 1; glyph < glyphCount; glyph++)
                charset[glyph] = checked((ushort)glyph);
            return charset;
        }
        if (offset is 1 or 2 || offset < 0 || offset >= cff.Length)
            return charset;
        int position = offset;
        int format = cff[position++];
        int glyphIndex = 1;
        if (format == 0)
        {
            while (glyphIndex < glyphCount)
            {
                charset[glyphIndex++] =
                    BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
                position += 2;
            }
            return charset;
        }
        if (format is not (1 or 2))
            return charset;
        while (glyphIndex < glyphCount)
        {
            ushort first = BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
            position += 2;
            int left = format == 1
                ? cff[position++]
                : BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
            if (format == 2)
                position += 2;
            for (int index = 0; index <= left && glyphIndex < glyphCount; index++)
                charset[glyphIndex++] = checked((ushort)(first + index));
        }

        return charset;
    }

    private static int[] ReadFdSelect(
        ReadOnlySpan<byte> cff,
        int offset,
        int glyphCount,
        int dictionaryCount)
    {
        var result = new int[glyphCount];
        if (offset <= 0 || offset >= cff.Length)
            return result;
        int position = offset;
        int format = cff[position++];
        if (format == 0)
        {
            for (int glyph = 0; glyph < glyphCount; glyph++)
            {
                int fd = cff[position++];
                if (fd >= dictionaryCount)
                    throw new PdfFormatException("CFF FDSelect references an invalid FD.");
                result[glyph] = fd;
            }
            return result;
        }
        if (format != 3)
        {
            if (format == 4)
                return ReadFdSelectFormat4(
                    cff,
                    ref position,
                    glyphCount,
                    dictionaryCount);
            throw new PdfFormatException("Unsupported CFF FDSelect format.");
        }
        int ranges = BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
        position += 2;
        int previousFirst = -1;
        int previousFd = 0;
        for (int range = 0; range < ranges; range++)
        {
            int first = BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
            int fd = cff[position + 2];
            position += 3;
            if (fd >= dictionaryCount || first < previousFirst)
                throw new PdfFormatException("CFF FDSelect range is invalid.");
            if (previousFirst >= 0)
            {
                for (int glyph = previousFirst; glyph < first && glyph < glyphCount; glyph++)
                    result[glyph] = previousFd;
            }
            previousFirst = first;
            previousFd = fd;
        }
        int sentinel = BinaryPrimitives.ReadUInt16BigEndian(cff[position..]);
        if (sentinel < previousFirst)
            throw new PdfFormatException("CFF FDSelect sentinel is invalid.");
        for (int glyph = Math.Max(0, previousFirst);
             glyph < sentinel && glyph < glyphCount;
             glyph++)
        {
            result[glyph] = previousFd;
        }
        return result;
    }

    private static int[] ReadFdSelectFormat4(
        ReadOnlySpan<byte> cff,
        ref int position,
        int glyphCount,
        int dictionaryCount)
    {
        var result = new int[glyphCount];
        uint rawRanges = BinaryPrimitives.ReadUInt32BigEndian(cff[position..]);
        position += 4;
        if (rawRanges > int.MaxValue || rawRanges > (uint)glyphCount + 1)
            throw new PdfFormatException("CFF2 FDSelect range count is invalid.");
        int previousFirst = -1;
        int previousFd = 0;
        for (int range = 0; range < (int)rawRanges; range++)
        {
            uint rawFirst = BinaryPrimitives.ReadUInt32BigEndian(cff[position..]);
            int fd = BinaryPrimitives.ReadUInt16BigEndian(cff[(position + 4)..]);
            position += 6;
            if (rawFirst > int.MaxValue ||
                fd >= dictionaryCount ||
                (int)rawFirst < previousFirst)
            {
                throw new PdfFormatException("CFF2 FDSelect range is invalid.");
            }
            int first = (int)rawFirst;
            if (previousFirst >= 0)
            {
                for (int glyph = previousFirst;
                     glyph < first && glyph < glyphCount;
                     glyph++)
                {
                    result[glyph] = previousFd;
                }
            }
            previousFirst = first;
            previousFd = fd;
        }
        uint rawSentinel = BinaryPrimitives.ReadUInt32BigEndian(cff[position..]);
        if (rawSentinel > int.MaxValue || (int)rawSentinel < previousFirst)
            throw new PdfFormatException("CFF2 FDSelect sentinel is invalid.");
        int sentinel = (int)rawSentinel;
        for (int glyph = Math.Max(0, previousFirst);
             glyph < sentinel && glyph < glyphCount;
             glyph++)
        {
            result[glyph] = previousFd;
        }
        return result;
    }

    private static int[] ReadVariationRegionCounts(
        ReadOnlySpan<byte> cff,
        int offset)
    {
        if (offset <= 0 || offset > cff.Length - 8)
            return Array.Empty<int>();
        int dataCount = BinaryPrimitives.ReadUInt16BigEndian(cff[(offset + 6)..]);
        if (dataCount < 0 ||
            dataCount > 4096 ||
            offset + 8L + dataCount * 4L > cff.Length)
        {
            return Array.Empty<int>();
        }
        var result = new int[dataCount];
        for (int index = 0; index < dataCount; index++)
        {
            uint relative = BinaryPrimitives.ReadUInt32BigEndian(
                cff[(offset + 8 + index * 4)..]);
            ulong itemOffset = (ulong)offset + relative;
            if (itemOffset + 6 > (ulong)cff.Length)
                return Array.Empty<int>();
            result[index] = BinaryPrimitives.ReadUInt16BigEndian(
                cff[((int)itemOffset + 4)..]);
            if (result[index] > 4096)
                return Array.Empty<int>();
        }
        return result;
    }

    private static bool TryReadUnitsPerEm(byte[] program, out int unitsPerEm)
    {
        unitsPerEm = 0;
        if (program.Length < 12 ||
            !program.AsSpan(0, 4).SequenceEqual("OTTO"u8))
        {
            return false;
        }
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(4, 2));
        if (tableCount < 1 ||
            tableCount > 4096 ||
            12L + tableCount * 16L > program.Length)
        {
            return false;
        }
        for (int index = 0; index < tableCount; index++)
        {
            int record = 12 + index * 16;
            if (!program.AsSpan(record, 4).SequenceEqual("head"u8))
                continue;
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(
                program.AsSpan(record + 8, 4));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(
                program.AsSpan(record + 12, 4));
            if (length < 20 || offset + 20UL > (ulong)program.Length)
                return false;
            unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(
                program.AsSpan((int)offset + 18, 2));
            return unitsPerEm is >= 16 and <= 16384;
        }
        return false;
    }

    private static GraphicsMatrix ReadFontMatrix(
        IReadOnlyDictionary<int, double[]> dictionary)
    {
        if (dictionary.TryGetValue(1207, out double[]? values) &&
            values.Length >= 6 &&
            values.Take(6).All(double.IsFinite))
        {
            return new GraphicsMatrix(
                values[0], values[1], values[2],
                values[3], values[4], values[5]);
        }

        return new GraphicsMatrix(0.001, 0, 0, 0.001, 0, 0);
    }

    private static string? ResolveString(
        int sid,
        IReadOnlyList<byte[]> strings)
    {
        if ((uint)sid < (uint)BasicStandardStrings.Length)
            return BasicStandardStrings[sid];
        int custom = sid - 391;
        return (uint)custom < (uint)strings.Count
            ? System.Text.Encoding.ASCII.GetString(strings[custom])
            : null;
    }

    private static int SubroutineBias(int count) =>
        count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    private static int Integer(
        IReadOnlyDictionary<int, double[]> dictionary,
        int operation)
        => checked((int)Number(dictionary, operation, 0));

    private static double Number(
        IReadOnlyDictionary<int, double[]> dictionary,
        int operation,
        double fallback)
        => dictionary.TryGetValue(operation, out double[]? values) &&
           values.Length > 0 &&
           double.IsFinite(values[^1])
            ? values[^1]
            : fallback;

    private static PdfGraphicsPath EmptyPath() =>
        new(Array.Empty<PdfPathSegment>());

    private sealed record PrivateData(
        double DefaultWidth,
        double NominalWidth,
        IReadOnlyList<byte[]> Subroutines)
    {
        public static PrivateData Empty { get; } =
            new(0, 0, Array.Empty<byte[]>());
    }

    private sealed class CharStringState
    {
        private readonly GraphicsMatrix _matrix;
        private readonly double _defaultWidth;
        private readonly double _nominalWidth;
        private readonly bool _hasWidths;
        private readonly int[] _variationRegionCounts;
        private readonly double[] _transient = new double[32];
        private bool _widthRead;
        private bool _contourOpen;

        public CharStringState(
            GraphicsMatrix matrix,
            double defaultWidth,
            double nominalWidth,
            bool hasWidths,
            int[] variationRegionCounts)
        {
            _matrix = matrix;
            _defaultWidth = defaultWidth;
            _nominalWidth = nominalWidth;
            _hasWidths = hasWidths;
            _variationRegionCounts = variationRegionCounts;
            Width = defaultWidth;
        }

        public List<double> Stack { get; } = new();
        public List<PdfPathSegment> Segments { get; } = new();
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; private set; }
        public int HintCount { get; set; }
        public int OperationCount { get; set; }
        public bool Ended { get; set; }
        public int VariationStoreIndex { get; private set; }

        public void TakeWidthForStem()
        {
            if (!_hasWidths)
            {
                _widthRead = true;
                return;
            }
            if (!_widthRead && Stack.Count % 2 == 1)
            {
                Width = _nominalWidth + Stack[0];
                Stack.RemoveAt(0);
            }
            _widthRead = true;
        }

        public void TakeWidthForMove(int expected)
        {
            if (!_hasWidths)
            {
                _widthRead = true;
                return;
            }
            if (!_widthRead && Stack.Count > expected)
            {
                Width = _nominalWidth + Stack[0];
                Stack.RemoveAt(0);
            }
            _widthRead = true;
        }

        public void TakeWidthForEnd()
        {
            if (!_hasWidths)
            {
                _widthRead = true;
                return;
            }
            if (!_widthRead && Stack.Count is 1 or 5)
                Width = _nominalWidth + Stack[0];
            _widthRead = true;
        }

        public bool TryPop(out double value)
        {
            if (Stack.Count == 0)
            {
                value = 0;
                return false;
            }
            value = Stack[^1];
            Stack.RemoveAt(Stack.Count - 1);
            return true;
        }

        public bool Unary(Func<double, double> operation)
        {
            if (!TryPop(out double value))
                return false;
            Stack.Add(operation(value));
            return true;
        }

        public bool Binary(Func<double, double, double> operation)
        {
            if (!TryPop(out double right) || !TryPop(out double left))
                return false;
            Stack.Add(operation(left, right));
            return true;
        }

        public bool PutTransient()
        {
            if (!TryPop(out double value) ||
                !TryPop(out double rawIndex) ||
                rawIndex != Math.Truncate(rawIndex) ||
                rawIndex is < 0 or >= 32)
            {
                return false;
            }
            _transient[(int)rawIndex] = value;
            return true;
        }

        public bool GetTransient()
        {
            if (!TryPop(out double rawIndex) ||
                rawIndex != Math.Truncate(rawIndex) ||
                rawIndex is < 0 or >= 32)
            {
                return false;
            }
            Stack.Add(_transient[(int)rawIndex]);
            return true;
        }

        public bool IfElse()
        {
            if (!TryPop(out double secondComparison) ||
                !TryPop(out double firstComparison) ||
                !TryPop(out double secondValue) ||
                !TryPop(out double firstValue))
            {
                return false;
            }
            Stack.Add(firstComparison <= secondComparison
                ? firstValue
                : secondValue);
            return true;
        }

        public bool Index()
        {
            if (!TryPop(out double rawIndex) ||
                rawIndex != Math.Truncate(rawIndex) ||
                Stack.Count == 0)
            {
                return false;
            }
            int index = Math.Clamp((int)rawIndex, 0, Stack.Count - 1);
            Stack.Add(Stack[Stack.Count - 1 - index]);
            return true;
        }

        public bool Roll()
        {
            if (!TryPop(out double rawShift) ||
                !TryPop(out double rawCount) ||
                rawShift != Math.Truncate(rawShift) ||
                rawCount != Math.Truncate(rawCount) ||
                rawCount is < 0 or > 96 ||
                rawCount > Stack.Count)
            {
                return false;
            }
            int count = (int)rawCount;
            if (count <= 1)
                return true;
            int shift = (int)rawShift % count;
            if (shift < 0)
                shift += count;
            if (shift == 0)
                return true;
            int start = Stack.Count - count;
            double[] values = Stack.GetRange(start, count).ToArray();
            for (int index = 0; index < count; index++)
                Stack[start + (index + shift) % count] = values[index];
            return true;
        }

        public bool SetVariationStoreIndex()
        {
            if (!TryPop(out double rawIndex) ||
                rawIndex != Math.Truncate(rawIndex) ||
                rawIndex < 0 ||
                rawIndex > int.MaxValue)
            {
                return false;
            }
            VariationStoreIndex = (int)rawIndex;
            return VariationStoreIndex < _variationRegionCounts.Length ||
                   _variationRegionCounts.Length == 0 &&
                   VariationStoreIndex == 0;
        }

        public bool ApplyBlend()
        {
            if (!TryPop(out double rawBlendCount) ||
                rawBlendCount != Math.Truncate(rawBlendCount) ||
                rawBlendCount is < 0 or > 96)
            {
                return false;
            }
            int blendCount = (int)rawBlendCount;
            int regionCount = _variationRegionCounts.Length == 0
                ? 0
                : _variationRegionCounts[VariationStoreIndex];
            int deltaCount = checked(blendCount * regionCount);
            if (Stack.Count < blendCount + deltaCount)
                return false;
            int deltaStart = Stack.Count - deltaCount;
            if (deltaCount > 0)
                Stack.RemoveRange(deltaStart, deltaCount);
            return true;
        }

        public void Move(double dx, double dy)
        {
            CloseContour();
            X += dx;
            Y += dy;
            Segments.Add(new PdfMoveTo(Point(X, Y)));
            _contourOpen = true;
        }

        public void Line(double dx, double dy)
        {
            X += dx;
            Y += dy;
            Segments.Add(new PdfLineTo(Point(X, Y)));
        }

        public void Curve(
            double dx1,
            double dy1,
            double dx2,
            double dy2,
            double dx3,
            double dy3)
        {
            double control1X = X + dx1;
            double control1Y = Y + dy1;
            double control2X = control1X + dx2;
            double control2Y = control1Y + dy2;
            X = control2X + dx3;
            Y = control2Y + dy3;
            Segments.Add(new PdfCubicBezierTo(
                Point(control1X, control1Y),
                Point(control2X, control2Y),
                Point(X, Y)));
        }

        public void CloseContour()
        {
            if (!_contourOpen)
                return;
            Segments.Add(new PdfClosePath());
            _contourOpen = false;
        }

        private PdfPoint Point(double x, double y) =>
            _matrix.Transform(x, y);
    }
}
