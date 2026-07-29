using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GraphicsMatrix = global::Poppler.PdfMatrix;

namespace Poppler.Text;

/// <summary>
/// Managed reader for encrypted PFA/PFB Type 1 charstrings. The interpreter
/// implements the ordinary path, width and subroutine operators used by
/// embedded PDF Type 1 fonts.
/// </summary>
internal sealed partial class PdfType1Font
{
    private const int MaximumProgramBytes = 64 * 1024 * 1024;
    private const int MaximumSubroutines = 1_000_000;
    private const int MaximumDepth = 32;
    private const int MaximumOperations = 1_000_000;

    private readonly Dictionary<int, byte[]> _charStringByUnicode;
    private readonly IReadOnlyList<byte[]> _subroutines;
    private readonly GraphicsMatrix _fontMatrix;

    private PdfType1Font(
        Dictionary<int, byte[]> charStringByUnicode,
        IReadOnlyList<byte[]> subroutines,
        GraphicsMatrix fontMatrix)
    {
        _charStringByUnicode = charStringByUnicode;
        _subroutines = subroutines;
        _fontMatrix = fontMatrix;
    }

    public static PdfType1Font? TryParse(byte[] program)
    {
        try
        {
            byte[] normalized = NormalizePfb(program);
            if (normalized.Length == 0 || normalized.Length > MaximumProgramBytes)
                return null;
            int marker = IndexOfAscii(normalized, "eexec");
            byte[] decrypted = marker >= 0
                ? DecryptEexec(normalized, marker + 5)
                : normalized;
            string text = Encoding.Latin1.GetString(decrypted);
            int lenIv = ReadLenIv(text);
            GraphicsMatrix matrix = ReadFontMatrix(text);
            IReadOnlyList<byte[]> subroutines =
                ReadSubroutines(decrypted, text, lenIv);
            Dictionary<int, byte[]> charStrings =
                ReadCharStrings(decrypted, text, lenIv);
            return charStrings.Count == 0
                ? null
                : new PdfType1Font(charStrings, subroutines, matrix);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or
            IndexOutOfRangeException or
            OverflowException)
        {
            return null;
        }
    }

    public bool TryGetGlyph(
        Rune rune,
        out PdfGraphicsPath path,
        out double advance)
    {
        path = new PdfGraphicsPath(Array.Empty<PdfPathSegment>());
        advance = 0;
        if (!_charStringByUnicode.TryGetValue(rune.Value, out byte[]? program))
            return false;
        var state = new CharStringState(_fontMatrix);
        if (!Execute(program, state, depth: 0, subroutine: false))
            return false;
        state.CloseContour();
        path = new PdfGraphicsPath(state.Segments);
        advance = Math.Abs(
            _fontMatrix.Transform(state.WidthX, state.WidthY).X -
            _fontMatrix.Transform(0, 0).X);
        return !path.IsEmpty;
    }

    private bool Execute(
        ReadOnlySpan<byte> program,
        CharStringState state,
        int depth,
        bool subroutine)
    {
        if (depth > MaximumDepth)
            return false;
        int position = 0;
        while (position < program.Length)
        {
            if (++state.Operations > MaximumOperations)
                return false;
            byte value = program[position++];
            if (TryReadNumber(program, ref position, value, out double number))
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
                    state.Stack.Clear();
                    break;
                case 4:
                    if (!state.TryPop(out double verticalMove))
                        return false;
                    state.Move(0, verticalMove);
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
                    foreach (double dx in state.Stack)
                        state.Line(dx, 0);
                    state.Stack.Clear();
                    break;
                case 7:
                    foreach (double dy in state.Stack)
                        state.Line(0, dy);
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
                case 9:
                    state.CloseContour();
                    state.Stack.Clear();
                    break;
                case 10:
                    if (!state.TryPop(out double subroutineNumber))
                        return false;
                    int subroutineIndex = checked((int)subroutineNumber);
                    if ((uint)subroutineIndex >= (uint)_subroutines.Count ||
                        !Execute(
                            _subroutines[subroutineIndex],
                            state,
                            depth + 1,
                            subroutine: true))
                    {
                        return false;
                    }
                    break;
                case 11:
                    return subroutine;
                case 13:
                    if (state.Stack.Count < 2)
                        return false;
                    state.SetWidth(
                        state.Stack[^2],
                        0,
                        state.Stack[^1],
                        0);
                    state.Stack.Clear();
                    break;
                case 14:
                    state.Stack.Clear();
                    state.CloseContour();
                    return true;
                case 21:
                    if (state.Stack.Count < 2)
                        return false;
                    state.Move(state.Stack[^2], state.Stack[^1]);
                    state.Stack.Clear();
                    break;
                case 22:
                    if (!state.TryPop(out double horizontalMove))
                        return false;
                    state.Move(horizontalMove, 0);
                    state.Stack.Clear();
                    break;
                case 30:
                    if (!AlternatingCurve(state, verticalFirst: true))
                        return false;
                    break;
                case 31:
                    if (!AlternatingCurve(state, verticalFirst: false))
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

        return subroutine;
    }

    private static bool ExecuteEscape(byte operation, CharStringState state)
    {
        switch (operation)
        {
            case 0:
            case 1:
            case 2:
                state.Stack.Clear();
                return true;
            case 7:
                if (state.Stack.Count < 4)
                    return false;
                state.SetWidth(
                    state.Stack[^4],
                    state.Stack[^3],
                    state.Stack[^2],
                    state.Stack[^1]);
                state.Stack.Clear();
                return true;
            case 12:
                if (state.Stack.Count < 2)
                    return false;
                double denominator = state.Stack[^1];
                double numerator = state.Stack[^2];
                state.Stack.RemoveRange(state.Stack.Count - 2, 2);
                state.Stack.Add(denominator == 0 ? 0 : numerator / denominator);
                return true;
            case 16:
                if (state.Stack.Count < 2)
                    return false;
                int argumentCount = Math.Max(0, checked((int)state.Stack[^2]));
                int otherSubroutine = checked((int)state.Stack[^1]);
                if (argumentCount > state.Stack.Count - 2)
                    return false;
                int argumentStart = state.Stack.Count - argumentCount - 2;
                double[] arguments = state.Stack
                    .Skip(argumentStart)
                    .Take(argumentCount)
                    .ToArray();
                state.Stack.RemoveRange(
                    argumentStart,
                    argumentCount + 2);
                state.OtherSubroutineResults.Clear();
                if (otherSubroutine == 3 && arguments.Length > 0)
                {
                    state.OtherSubroutineResults.Add(arguments[0]);
                }
                else if (otherSubroutine == 0)
                {
                    state.OtherSubroutineResults.Add(state.X);
                    state.OtherSubroutineResults.Add(state.Y);
                }
                return true;
            case 17:
                double result = state.OtherSubroutineResults.Count > 0
                    ? state.OtherSubroutineResults[^1]
                    : 0;
                if (state.OtherSubroutineResults.Count > 0)
                {
                    state.OtherSubroutineResults.RemoveAt(
                        state.OtherSubroutineResults.Count - 1);
                }
                state.Stack.Add(result);
                return true;
            case 33:
                if (state.Stack.Count < 2)
                    return false;
                state.X = state.Stack[^2];
                state.Y = state.Stack[^1];
                state.Stack.Clear();
                return true;
            default:
                return false;
        }
    }

    private static bool AlternatingCurve(
        CharStringState state,
        bool verticalFirst)
    {
        if (state.Stack.Count < 4 || state.Stack.Count % 4 != 0)
            return false;
        bool vertical = verticalFirst;
        for (int index = 0; index < state.Stack.Count; index += 4)
        {
            if (vertical)
            {
                state.Curve(
                    0,
                    state.Stack[index],
                    state.Stack[index + 1],
                    state.Stack[index + 2],
                    state.Stack[index + 3],
                    0);
            }
            else
            {
                state.Curve(
                    state.Stack[index],
                    0,
                    state.Stack[index + 1],
                    state.Stack[index + 2],
                    0,
                    state.Stack[index + 3]);
            }
            vertical = !vertical;
        }
        state.Stack.Clear();
        return true;
    }

    private static bool TryReadNumber(
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
            number = BinaryPrimitives.ReadInt32BigEndian(bytes[position..]);
            position += 4;
            return true;
        }
        return false;
    }

    private static byte[] NormalizePfb(byte[] program)
    {
        if (program.Length < 6 || program[0] != 0x80)
            return program;
        using var output = new MemoryStream();
        int position = 0;
        while (position <= program.Length - 2 && program[position] == 0x80)
        {
            int type = program[position + 1];
            position += 2;
            if (type == 3)
                break;
            if (type is not (1 or 2) || position > program.Length - 4)
                return Array.Empty<byte>();
            int length = BinaryPrimitives.ReadInt32LittleEndian(program.AsSpan(position, 4));
            position += 4;
            if (length < 0 || position > program.Length - length)
                return Array.Empty<byte>();
            output.Write(program, position, length);
            position += length;
        }
        return output.ToArray();
    }

    private static byte[] DecryptEexec(byte[] program, int start)
    {
        while (start < program.Length && IsWhiteSpace(program[start]))
            start++;
        byte[] encrypted;
        if (LooksLikeHex(program, start))
        {
            var bytes = new List<byte>();
            int high = -1;
            for (int index = start; index < program.Length; index++)
            {
                int nibble = Hex(program[index]);
                if (nibble < 0)
                {
                    if (IsWhiteSpace(program[index]))
                        continue;
                    break;
                }
                if (high < 0)
                    high = nibble;
                else
                {
                    bytes.Add((byte)((high << 4) | nibble));
                    high = -1;
                }
            }
            encrypted = bytes.ToArray();
        }
        else
        {
            encrypted = program.AsSpan(start).ToArray();
        }

        byte[] plain = Decrypt(encrypted, 55665, discard: 4);
        byte[] prefix = program.AsSpan(0, Math.Min(start, program.Length)).ToArray();
        var result = new byte[prefix.Length + plain.Length];
        prefix.CopyTo(result, 0);
        plain.CopyTo(result, prefix.Length);
        return result;
    }

    private static byte[] Decrypt(
        ReadOnlySpan<byte> encrypted,
        ushort seed,
        int discard)
    {
        var plain = new byte[encrypted.Length];
        ushort state = seed;
        for (int index = 0; index < encrypted.Length; index++)
        {
            byte cipher = encrypted[index];
            plain[index] = (byte)(cipher ^ (state >> 8));
            state = unchecked((ushort)((cipher + state) * 52845 + 22719));
        }
        return discard >= plain.Length ? Array.Empty<byte>() : plain[discard..];
    }

    private static IReadOnlyList<byte[]> ReadSubroutines(
        byte[] bytes,
        string text,
        int lenIv)
    {
        int start = text.IndexOf("/Subrs", StringComparison.Ordinal);
        int end = text.IndexOf("/CharStrings", StringComparison.Ordinal);
        if (start < 0 || end <= start)
            return Array.Empty<byte[]>();
        var result = new Dictionary<int, byte[]>();
        foreach (Match match in SubroutineRegex().Matches(text, start))
        {
            if (match.Index >= end)
                break;
            int index = int.Parse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture);
            int length = int.Parse(
                match.Groups[2].Value,
                CultureInfo.InvariantCulture);
            int dataStart = match.Index + match.Length;
            if (index < 0 ||
                index >= MaximumSubroutines ||
                length < 0 ||
                dataStart > bytes.Length - length)
            {
                continue;
            }
            result[index] = DecodeCharString(
                bytes.AsSpan(dataStart, length),
                lenIv);
        }
        if (result.Count == 0)
            return Array.Empty<byte[]>();
        int count = checked(result.Keys.Max() + 1);
        var subroutines = new byte[count][];
        for (int index = 0; index < count; index++)
            subroutines[index] = result.GetValueOrDefault(index, Array.Empty<byte>());
        return subroutines;
    }

    private static Dictionary<int, byte[]> ReadCharStrings(
        byte[] bytes,
        string text,
        int lenIv)
    {
        int start = text.IndexOf("/CharStrings", StringComparison.Ordinal);
        var result = new Dictionary<int, byte[]>();
        if (start < 0)
            return result;
        foreach (Match match in CharStringRegex().Matches(text, start))
        {
            string name = match.Groups[1].Value;
            int length = int.Parse(
                match.Groups[2].Value,
                CultureInfo.InvariantCulture);
            int dataStart = match.Index + match.Length;
            if (length < 0 || dataStart > bytes.Length - length)
                continue;
            string unicode = PdfGlyphNames.ToUnicode(name);
            Rune rune = unicode.EnumerateRunes().FirstOrDefault();
            if (rune.Value == 0 || rune.Value == 0xFFFD)
                continue;
            result.TryAdd(
                rune.Value,
                DecodeCharString(bytes.AsSpan(dataStart, length), lenIv));
        }
        return result;
    }

    private static byte[] DecodeCharString(
        ReadOnlySpan<byte> bytes,
        int lenIv) =>
        lenIv < 0
            ? bytes.ToArray()
            : Decrypt(bytes, 4330, lenIv);

    private static int ReadLenIv(string text)
    {
        Match match = LenIvRegex().Match(text);
        return match.Success &&
               int.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out int value)
            ? Math.Max(-1, value)
            : 4;
    }

    private static GraphicsMatrix ReadFontMatrix(string text)
    {
        Match match = FontMatrixRegex().Match(text);
        if (!match.Success)
            return new GraphicsMatrix(0.001, 0, 0, 0.001, 0, 0);
        var values = new double[6];
        for (int index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(
                    match.Groups[index + 1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]))
            {
                return new GraphicsMatrix(0.001, 0, 0, 0.001, 0, 0);
            }
        }
        return new GraphicsMatrix(
            values[0], values[1], values[2],
            values[3], values[4], values[5]);
    }

    private static int IndexOfAscii(byte[] bytes, string text)
    {
        byte[] needle = Encoding.ASCII.GetBytes(text);
        return bytes.AsSpan().IndexOf(needle);
    }

    private static bool LooksLikeHex(byte[] bytes, int start)
    {
        int inspected = 0;
        for (int index = start; index < bytes.Length && inspected < 16; index++)
        {
            if (IsWhiteSpace(bytes[index]))
                continue;
            inspected++;
            if (Hex(bytes[index]) < 0)
                return false;
        }
        return inspected >= 8;
    }

    private static int Hex(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        _ => -1
    };

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or
            (byte)'\f' or (byte)'\r' or (byte)' ';

    [GeneratedRegex(@"dup[ \t\r\n]+([0-9]+)[ \t\r\n]+([0-9]+)[ \t\r\n]+(?:RD|-\|)[ \t\r\n]")]
    private static partial Regex SubroutineRegex();

    [GeneratedRegex(@"/([A-Za-z0-9_.]+)[ \t\r\n]+([0-9]+)[ \t\r\n]+(?:RD|-\|)[ \t\r\n]")]
    private static partial Regex CharStringRegex();

    [GeneratedRegex(@"/lenIV[ \t\r\n]+(-?[0-9]+)")]
    private static partial Regex LenIvRegex();

    [GeneratedRegex(@"/FontMatrix[ \t\r\n]*\[[ \t\r\n]*([-+0-9.eE]+)[ \t\r\n]+([-+0-9.eE]+)[ \t\r\n]+([-+0-9.eE]+)[ \t\r\n]+([-+0-9.eE]+)[ \t\r\n]+([-+0-9.eE]+)[ \t\r\n]+([-+0-9.eE]+)")]
    private static partial Regex FontMatrixRegex();

    private sealed class CharStringState
    {
        private readonly GraphicsMatrix _matrix;
        private bool _contourOpen;

        public CharStringState(GraphicsMatrix matrix) => _matrix = matrix;

        public List<double> Stack { get; } = new();
        public List<double> OtherSubroutineResults { get; } = new();
        public List<PdfPathSegment> Segments { get; } = new();
        public double X { get; set; }
        public double Y { get; set; }
        public double WidthX { get; private set; }
        public double WidthY { get; private set; }
        public int Operations { get; set; }

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

        public void SetWidth(double sideX, double sideY, double widthX, double widthY)
        {
            X = sideX;
            Y = sideY;
            WidthX = widthX;
            WidthY = widthY;
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
