using System.Text;
using Poppler.Core;
using Poppler.DocumentModel;
using Poppler.OptionalContent;

namespace Poppler.Text;

internal sealed class PdfTextExtractor
{
    private readonly PdfDocumentCore _document;
    private readonly PdfPageNode _page;
    private readonly PdfOptionalContentEvaluator _optionalContent;
    private readonly Dictionary<string, PdfFontDecoder> _fonts = new(StringComparer.Ordinal);
    private readonly PdfFontDecoder _fallbackFont;

    public PdfTextExtractor(
        PdfDocumentCore document,
        PdfPageNode page,
        PdfOptionalContentEvaluator optionalContent)
    {
        _document = document;
        _page = page;
        _optionalContent = optionalContent;
        _fallbackFont = PdfFontDecoder.CreateFallback(document);
        foreach ((string name, PdfFontDecoder font) in PdfFontCollection.Read(document, page))
            _fonts[name] = font;
    }

    public IReadOnlyList<TextBox> Extract(TextLayout layout)
    {
        byte[] content = GetContentBytes();
        var results = new List<TextBox>();
        var state = new TextState { Font = _fallbackFont };
        var graphicsStack = new Stack<TextState>();
        var visibilityStack = new Stack<bool>();
        PdfDictionary? resources = _page.Resources.AsDictionary(_document);
        bool visible = true;
        bool inText = false;

        foreach (PdfContentOperation operation in PdfContentReader.Read(content, _document.Options))
        {
            IReadOnlyList<PdfObject> values = operation.Operands;
            if (operation.Operator is "BMC" or "BDC")
            {
                if (visibilityStack.Count >=
                    _document.Options.MaximumOptionalContentDepth)
                {
                    throw new PdfLimitException(
                        "Optional-content nesting exceeds the configured limit.");
                }
                visibilityStack.Push(visible);
                if (operation.Operator == "BDC" &&
                    values.LastOrDefault() is { } membership)
                {
                    visible = visible &&
                              _optionalContent.IsVisible(
                                  membership,
                                  resources);
                }
                continue;
            }
            if (operation.Operator == "EMC")
            {
                if (visibilityStack.Count > 0)
                    visible = visibilityStack.Pop();
                continue;
            }
            if (!visible)
                continue;

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

        return PdfTextLayoutEngine.Order(results, layout);
    }

    public static string Join(IReadOnlyList<TextBox> boxes, TextLayout layout)
    {
        return PdfTextLayoutEngine.Join(boxes, layout);
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
                double movement = -adjustment.Value / 1000.0 * state.FontSize;
                state.TextMatrix = state.Font.WritingMode == FontWritingMode.Vertical
                    ? state.TextMatrix.Translate(0, movement)
                    : state.TextMatrix.Translate(movement * state.HorizontalScale, 0);
            }
        }
    }

    private void Show(PdfString value, TextState state, List<TextBox> results)
    {
        ReadOnlySpan<byte> bytes = value.Bytes.Span;
        IReadOnlyList<PdfDecodedGlyph> glyphs = state.Font.DecodeGlyphs(bytes);
        string text = string.Concat(glyphs.Select(glyph => glyph.Text));
        double advanceX = 0;
        double advanceY = 0;
        foreach (PdfDecodedGlyph glyph in glyphs)
        {
            double spacing =
                state.CharacterSpacing +
                (glyph.IsWordSpace ? state.WordSpacing : 0);
            if (state.Font.WritingMode == FontWritingMode.Vertical)
            {
                advanceY += glyph.AdvanceY / 1000.0 * state.FontSize -
                            spacing;
            }
            else
            {
                advanceX +=
                    (glyph.AdvanceX / 1000.0 * state.FontSize + spacing) *
                    state.HorizontalScale;
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            PdfMatrix deviceMatrix = state.TextMatrix.Multiply(state.Ctm);
            (double lower, double upper, double crossStart, double crossEnd) =
                state.Font.WritingMode == FontWritingMode.Vertical
                    ? (
                        Math.Min(0, advanceY),
                        Math.Max(0, advanceY),
                        -state.FontSize * 0.5 * state.HorizontalScale,
                        state.FontSize * 0.5 * state.HorizontalScale)
                    : (
                        state.Font.Descent * state.FontSize + state.Rise,
                        state.Font.Ascent * state.FontSize + state.Rise,
                        Math.Min(0, advanceX),
                        Math.Max(0, advanceX));
            (double X, double Y)[] corners =
                state.Font.WritingMode == FontWritingMode.Vertical
                    ? new[]
                    {
                        deviceMatrix.Transform(crossStart, lower + state.Rise),
                        deviceMatrix.Transform(crossEnd, lower + state.Rise),
                        deviceMatrix.Transform(crossStart, upper + state.Rise),
                        deviceMatrix.Transform(crossEnd, upper + state.Rise)
                    }
                    : new[]
                    {
                        deviceMatrix.Transform(crossStart, lower),
                        deviceMatrix.Transform(crossEnd, lower),
                        deviceMatrix.Transform(crossStart, upper),
                        deviceMatrix.Transform(crossEnd, upper)
                    };
            double minX = corners.Min(point => point.X);
            double maxX = corners.Max(point => point.X);
            double minY = corners.Min(point => point.Y);
            double maxY = corners.Max(point => point.Y);
            (double startX, double startY) = deviceMatrix.Transform(0, 0);
            (double endX, double endY) = deviceMatrix.Transform(advanceX, advanceY);
            int rotation = NormalizeRotation(
                (int)Math.Round(Math.Atan2(endY - startY, endX - startX) * 180 / Math.PI));
            string visibleText = text.TrimEnd();
            if (visibleText.Length > 0)
            {
                results.Add(new TextBox(
                    visibleText,
                    new PdfRectangle(minX, minY, maxX, maxY),
                    rotation,
                    text.Length > 0 && char.IsWhiteSpace(text[^1]),
                    state.Font.Name,
                    state.FontSize)
                {
                    WritingMode = state.Font.WritingMode,
                    IsRightToLeft =
                        PdfTextLayoutEngine.ContainsStrongRightToLeft(visibleText)
                });
            }
        }

        state.TextMatrix = state.TextMatrix.Translate(advanceX, advanceY);
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
