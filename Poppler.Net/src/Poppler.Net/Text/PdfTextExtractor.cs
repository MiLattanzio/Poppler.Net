using System.Text;
using Poppler.Core;
using Poppler.DocumentModel;

namespace Poppler.Text;

internal sealed class PdfTextExtractor
{
    private readonly PdfDocumentCore _document;
    private readonly PdfPageNode _page;
    private readonly Dictionary<string, PdfFontDecoder> _fonts = new(StringComparer.Ordinal);
    private readonly PdfFontDecoder _fallbackFont;

    public PdfTextExtractor(PdfDocumentCore document, PdfPageNode page)
    {
        _document = document;
        _page = page;
        _fallbackFont = new PdfFontDecoder(
            new PdfDictionary(new Dictionary<string, PdfObject>(StringComparer.Ordinal)),
            document);
        ReadFonts();
    }

    public IReadOnlyList<TextBox> Extract(TextLayout layout)
    {
        byte[] content = GetContentBytes();
        var results = new List<TextBox>();
        var state = new TextState { Font = _fallbackFont };
        var graphicsStack = new Stack<TextState>();
        bool inText = false;

        foreach (PdfContentOperation operation in PdfContentReader.Read(content, _document.Options))
        {
            IReadOnlyList<PdfObject> values = operation.Operands;
            switch (operation.Operator)
            {
                case "q":
                    graphicsStack.Push(state.Clone());
                    break;
                case "Q":
                    if (graphicsStack.Count > 0)
                        state = graphicsStack.Pop();
                    break;
                case "cm" when TryNumbers(values, 6, out double[] matrix):
                    state.Ctm = new PdfMatrix(
                        matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5])
                        .Multiply(state.Ctm);
                    break;
                case "BT":
                    inText = true;
                    state.TextMatrix = PdfMatrix.Identity;
                    state.LineMatrix = PdfMatrix.Identity;
                    break;
                case "ET":
                    inText = false;
                    break;
                case "Tf" when values.Count >= 2 &&
                                    values[^2] is PdfName fontName &&
                                    Number(values[^1]) is { } fontSize:
                    state.Font = _fonts.GetValueOrDefault(fontName.Value, _fallbackFont);
                    state.FontSize = fontSize;
                    break;
                case "Tm" when TryNumbers(values, 6, out double[] textMatrix):
                    state.TextMatrix = new PdfMatrix(
                        textMatrix[0], textMatrix[1], textMatrix[2],
                        textMatrix[3], textMatrix[4], textMatrix[5]);
                    state.LineMatrix = state.TextMatrix;
                    break;
                case "Td" when TryLastPair(values, out double tdX, out double tdY):
                    MoveText(state, tdX, tdY);
                    break;
                case "TD" when TryLastPair(values, out double tdx, out double tdy):
                    state.Leading = -tdy;
                    MoveText(state, tdx, tdy);
                    break;
                case "T*":
                    MoveText(state, 0, -state.Leading);
                    break;
                case "Tc" when LastNumber(values) is { } characterSpacing:
                    state.CharacterSpacing = characterSpacing;
                    break;
                case "Tw" when LastNumber(values) is { } wordSpacing:
                    state.WordSpacing = wordSpacing;
                    break;
                case "Tz" when LastNumber(values) is { } horizontalScale:
                    state.HorizontalScale = horizontalScale / 100.0;
                    break;
                case "TL" when LastNumber(values) is { } leading:
                    state.Leading = leading;
                    break;
                case "Ts" when LastNumber(values) is { } rise:
                    state.Rise = rise;
                    break;
                case "Tj" when inText && values.LastOrDefault() is PdfString text:
                    Show(text, state, results);
                    break;
                case "TJ" when inText && values.LastOrDefault() is PdfArray array:
                    ShowArray(array, state, results);
                    break;
                case "'" when inText && values.LastOrDefault() is PdfString quoteText:
                    MoveText(state, 0, -state.Leading);
                    Show(quoteText, state, results);
                    break;
                case "\"" when inText && values.Count >= 3 &&
                                       Number(values[^3]) is { } quoteWordSpacing &&
                                       Number(values[^2]) is { } quoteCharacterSpacing &&
                                       values[^1] is PdfString doubleQuoteText:
                    state.WordSpacing = quoteWordSpacing;
                    state.CharacterSpacing = quoteCharacterSpacing;
                    MoveText(state, 0, -state.Leading);
                    Show(doubleQuoteText, state, results);
                    break;
            }
        }

        return layout == TextLayout.RawOrder
            ? results
            : results
                .OrderByDescending(box => Math.Round(box.BoundingBox.Top, 1))
                .ThenBy(box => box.BoundingBox.Left)
                .ToArray();
    }

    public static string Join(IReadOnlyList<TextBox> boxes, TextLayout layout)
    {
        if (boxes.Count == 0)
            return "";
        if (layout == TextLayout.RawOrder)
            return string.Concat(boxes.Select(box => box.Text + (box.HasSpaceAfter ? " " : ""))).Trim();

        var builder = new StringBuilder();
        double? baseline = null;
        PdfRectangle previous = default;
        double previousFontSize = 0;
        foreach (TextBox box in boxes)
        {
            double currentBaseline = box.BoundingBox.Bottom;
            bool newLine = baseline.HasValue &&
                           Math.Abs(currentBaseline - baseline.Value) >
                           Math.Max(2, Math.Max(previousFontSize, box.FontSize) * 0.6);
            if (newLine)
            {
                builder.AppendLine();
            }
            else if (builder.Length > 0 &&
                     builder[^1] is not '\n' and not ' ' &&
                     (box.BoundingBox.Left - previous.Right >
                      Math.Max(1, Math.Min(previousFontSize, box.FontSize) * 0.15) ||
                      box.HasSpaceAfter))
            {
                builder.Append(' ');
            }

            builder.Append(box.Text);
            if (box.HasSpaceAfter)
                builder.Append(' ');
            baseline = currentBaseline;
            previous = box.BoundingBox;
            previousFontSize = box.FontSize;
        }

        return builder.ToString().Trim();
    }

    private void ShowArray(PdfArray array, TextState state, List<TextBox> results)
    {
        foreach (PdfObject item in array)
        {
            if (item is PdfString text)
            {
                Show(text, state, results);
            }
            else if (item is PdfNumber adjustment)
            {
                double movement = -adjustment.Value / 1000.0 *
                                  state.FontSize *
                                  state.HorizontalScale;
                state.TextMatrix = state.TextMatrix.Translate(movement, 0);
            }
        }
    }

    private void Show(PdfString value, TextState state, List<TextBox> results)
    {
        ReadOnlySpan<byte> bytes = value.Bytes.Span;
        string text = state.Font.Decode(bytes);
        double advance =
            (state.Font.GetAdvance(bytes) / 1000.0 * state.FontSize +
             state.CharacterSpacing * bytes.Length +
             state.WordSpacing * bytes.Count((byte)' ')) *
            state.HorizontalScale;

        if (!string.IsNullOrEmpty(text))
        {
            PdfMatrix deviceMatrix = state.TextMatrix.Multiply(state.Ctm);
            (double startX, double startY) = deviceMatrix.Transform(0, state.Rise);
            (double endX, double endY) = deviceMatrix.Transform(advance, state.Rise);
            (double topX, double topY) = deviceMatrix.Transform(0, state.Rise + state.FontSize);
            double minX = Math.Min(startX, Math.Min(endX, topX));
            double maxX = Math.Max(startX, Math.Max(endX, topX));
            double minY = Math.Min(startY, Math.Min(endY, topY));
            double maxY = Math.Max(startY, Math.Max(endY, topY));
            int rotation = NormalizeRotation(
                (int)Math.Round(Math.Atan2(endY - startY, endX - startX) * 180 / Math.PI));
            results.Add(new TextBox(
                text.TrimEnd(),
                new PdfRectangle(minX, minY, maxX, maxY),
                rotation,
                text.Length > 0 && char.IsWhiteSpace(text[^1]),
                state.Font.Name,
                state.FontSize));
        }

        state.TextMatrix = state.TextMatrix.Translate(advance, 0);
    }

    private byte[] GetContentBytes()
    {
        PdfObject? contents = _page.Dictionary.GetValueOrNull("Contents");
        if (contents is null)
            return Array.Empty<byte>();
        PdfObject resolved = contents.Resolve(_document);
        if (resolved is PdfStream stream)
            return _document.Decode(stream);
        if (resolved is not PdfArray array)
            return Array.Empty<byte>();

        using var output = new MemoryStream();
        foreach (PdfObject item in array)
        {
            if (item.AsStream(_document) is not { } part)
                continue;
            byte[] decoded = _document.Decode(part);
            if (output.Length > 0)
                output.WriteByte((byte)'\n');
            if (output.Length + decoded.Length > _document.Options.MaximumDecodedStreamBytes)
                throw new PdfLimitException("Combined page content exceeds the decoded stream limit.");
            output.Write(decoded);
        }

        return output.ToArray();
    }

    private void ReadFonts()
    {
        PdfDictionary? resources = _page.Resources.AsDictionary(_document);
        PdfDictionary? fonts = resources?.GetValueOrNull("Font").AsDictionary(_document);
        if (fonts is null)
            return;
        foreach ((string resourceName, PdfObject fontObject) in fonts)
        {
            PdfDictionary? dictionary = fontObject.AsDictionary(_document);
            if (dictionary is not null)
                _fonts[resourceName] = new PdfFontDecoder(dictionary, _document);
        }
    }

    private static void MoveText(TextState state, double x, double y)
    {
        state.LineMatrix = state.LineMatrix.Translate(x, y);
        state.TextMatrix = state.LineMatrix;
    }

    private static bool TryNumbers(
        IReadOnlyList<PdfObject> values,
        int count,
        out double[] numbers)
    {
        numbers = Array.Empty<double>();
        if (values.Count < count)
            return false;
        numbers = new double[count];
        int start = values.Count - count;
        for (int index = 0; index < count; index++)
        {
            double? number = Number(values[start + index]);
            if (!number.HasValue)
                return false;
            numbers[index] = number.Value;
        }

        return true;
    }

    private static bool TryLastPair(
        IReadOnlyList<PdfObject> values,
        out double first,
        out double second)
    {
        first = 0;
        second = 0;
        if (values.Count < 2 ||
            Number(values[^2]) is not { } firstNumber ||
            Number(values[^1]) is not { } secondNumber)
        {
            return false;
        }

        first = firstNumber;
        second = secondNumber;
        return true;
    }

    private static double? LastNumber(IReadOnlyList<PdfObject> values) =>
        values.Count > 0 ? Number(values[^1]) : null;

    private static double? Number(PdfObject value) => (value as PdfNumber)?.Value;

    private static int NormalizeRotation(int rotation)
    {
        rotation %= 360;
        return rotation < 0 ? rotation + 360 : rotation;
    }

    private sealed class TextState
    {
        public PdfMatrix Ctm { get; set; } = PdfMatrix.Identity;
        public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;
        public PdfMatrix LineMatrix { get; set; } = PdfMatrix.Identity;
        public PdfFontDecoder Font { get; set; } = null!;
        public double FontSize { get; set; } = 12;
        public double CharacterSpacing { get; set; }
        public double WordSpacing { get; set; }
        public double HorizontalScale { get; set; } = 1;
        public double Leading { get; set; }
        public double Rise { get; set; }

        public TextState Clone() => (TextState)MemberwiseClone();
    }
}

internal static class SpanByteExtensions
{
    public static int Count(this ReadOnlySpan<byte> span, byte value)
    {
        int count = 0;
        foreach (byte item in span)
        {
            if (item == value)
                count++;
        }

        return count;
    }
}
