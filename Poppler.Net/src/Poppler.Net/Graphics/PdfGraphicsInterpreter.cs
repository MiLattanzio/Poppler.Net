using Poppler.Annotations;
using Poppler.Core;
using Poppler.Color;
using Poppler.DocumentModel;
using Poppler.Forms;
using Poppler.Images;
using Poppler.OptionalContent;
using Poppler.Text;
using PdfContentOperation = Poppler.Text.PdfContentOperation;
using PdfContentReader = Poppler.Text.PdfContentReader;

namespace Poppler.Graphics;

/// <summary>
/// Managed counterpart of the first vector slice of Poppler's Gfx/GfxState
/// interpreter. It produces a backend-neutral display list.
/// </summary>
internal sealed class PdfGraphicsInterpreter
{
    private readonly PdfDocumentCore _document;
    private readonly PdfPageNode _page;
    private readonly PdfOptionalContentEvaluator _optionalContent;
    private readonly Dictionary<PdfReference, PdfBrush> _patternCache = new();
    private readonly HashSet<PdfReference> _activePatterns = new();
    private readonly HashSet<PdfReference> _activeForms = new();
    private readonly HashSet<PdfReference> _activeSoftMasks = new();
    private readonly HashSet<PdfReference> _activeType3Glyphs = new();
    private readonly HashSet<string> _reportedDiagnostics = new(StringComparer.Ordinal);
    private int _operationCount;
    private int _elementCount;
    private int _inlineImageCount;
    private static readonly IReadOnlyDictionary<char, byte[]> AnnotationGlyphs =
        CreateAnnotationGlyphs();

    public PdfGraphicsInterpreter(
        PdfDocumentCore document,
        PdfPageNode page,
        PdfOptionalContentEvaluator optionalContent)
    {
        _document = document;
        _page = page;
        _optionalContent = optionalContent;
    }

    public IReadOnlyList<PdfGraphicsElement> Interpret() =>
        Interpret(Array.Empty<PdfAnnotationData>());

    public IReadOnlyList<PdfGraphicsElement> Interpret(
        IReadOnlyList<PdfAnnotationData> annotations)
    {
        var output = new List<PdfGraphicsElement>();
        byte[] content = ReadContent(_page.Dictionary.GetValueOrNull("Contents"));
        Execute(
            content,
            _page.Resources,
            GraphicsContext.Create(),
            output,
            depth: 0,
            sourceResource: null);
        foreach (PdfAnnotationData annotation in annotations)
            PaintAnnotation(annotation, output);
        return output;
    }

    private void Execute(
        byte[] content,
        PdfObject? resourcesObject,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        int depth,
        string? sourceResource)
    {
        int maximumDepth =
            sourceResource?.StartsWith("Annotation[", StringComparison.Ordinal) == true
                ? Math.Min(
                    _document.Options.MaximumXObjectDepth,
                    _document.Options.MaximumAnnotationAppearanceDepth)
                : _document.Options.MaximumXObjectDepth;
        if (depth > maximumDepth)
            throw new PdfLimitException("Form or pattern nesting exceeds the configured limit.");

        PdfDictionary? resources = resourcesObject.AsDictionary(_document);
        IReadOnlyDictionary<string, PdfFontDecoder> fonts =
            PdfFontCollection.Read(_document, resourcesObject);
        PdfFontDecoder fallbackFont = PdfFontDecoder.CreateFallback(_document);
        context.Text.Font ??= fallbackFont;
        var stack = new Stack<GraphicsContext>();
        var visibilityStack = new Stack<bool>();
        var path = new PdfPathBuilder();
        PdfFillRule? pendingClip = null;
        bool visible = true;

        foreach (PdfContentOperation operation in PdfContentReader.Read(content, _document.Options))
        {
            CountOperation();
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
                    if (stack.Count >= _document.Options.MaximumGraphicsStateDepth)
                        throw new PdfLimitException("Graphics-state nesting exceeds the configured limit.");
                    stack.Push(context.Clone());
                    break;
                case "Q":
                    if (stack.Count > 0)
                        context = stack.Pop();
                    break;
                case "cm" when TryNumbers(values, 6, out double[] matrixValues):
                {
                    var matrix = new PdfMatrix(
                        matrixValues[0],
                        matrixValues[1],
                        matrixValues[2],
                        matrixValues[3],
                        matrixValues[4],
                        matrixValues[5]);
                    if (matrix.IsFinite)
                    {
                        context.Graphics = context.Graphics with
                        {
                            Transform = matrix.Multiply(context.Graphics.Transform)
                        };
                    }

                    break;
                }
                case "w" when LastNumber(values) is { } lineWidth:
                    context.Graphics = context.Graphics with
                    {
                        LineWidth = Math.Max(0, lineWidth)
                    };
                    break;
                case "J" when LastInteger(values) is { } lineCap:
                    context.Graphics = context.Graphics with
                    {
                        LineCap = lineCap switch
                        {
                            1 => PdfLineCap.Round,
                            2 => PdfLineCap.Square,
                            _ => PdfLineCap.Butt
                        }
                    };
                    break;
                case "j" when LastInteger(values) is { } lineJoin:
                    context.Graphics = context.Graphics with
                    {
                        LineJoin = lineJoin switch
                        {
                            1 => PdfLineJoin.Round,
                            2 => PdfLineJoin.Bevel,
                            _ => PdfLineJoin.Miter
                        }
                    };
                    break;
                case "M" when LastNumber(values) is { } miterLimit:
                    context.Graphics = context.Graphics with
                    {
                        MiterLimit = Math.Max(1, miterLimit)
                    };
                    break;
                case "d":
                    SetDash(values, context);
                    break;
                case "gs" when values.LastOrDefault() is PdfName stateName:
                    ApplyExtendedState(resources, stateName.Value, context, depth);
                    break;
                case "CS" when !context.InType3Glyph &&
                                    values.LastOrDefault() is PdfName strokeSpace:
                    context.StrokeColorSpace = ResolveColorSpace(
                        resources,
                        strokeSpace.Value);
                    break;
                case "cs" when !context.InType3Glyph &&
                                    values.LastOrDefault() is PdfName fillSpace:
                    context.FillColorSpace = ResolveColorSpace(
                        resources,
                        fillSpace.Value);
                    break;
                case "G" when !context.InType3Glyph &&
                                   LastNumber(values) is { } strokeGray:
                    context.StrokeColorSpace = PdfColorSpaceDefinition.DeviceGray;
                    context.Graphics = context.Graphics with
                    {
                        Stroke = new PdfSolidBrush(PdfColor.Gray(strokeGray))
                    };
                    break;
                case "g" when !context.InType3Glyph &&
                                   LastNumber(values) is { } fillGray:
                    context.FillColorSpace = PdfColorSpaceDefinition.DeviceGray;
                    context.Graphics = context.Graphics with
                    {
                        Fill = new PdfSolidBrush(PdfColor.Gray(fillGray))
                    };
                    break;
                case "RG" when !context.InType3Glyph &&
                                    TryNumbers(values, 3, out double[] strokeRgb):
                    context.StrokeColorSpace = PdfColorSpaceDefinition.DeviceRgb;
                    context.Graphics = context.Graphics with
                    {
                        Stroke = new PdfSolidBrush(PdfColor.Rgb(
                            strokeRgb[0],
                            strokeRgb[1],
                            strokeRgb[2]))
                    };
                    break;
                case "rg" when !context.InType3Glyph &&
                                    TryNumbers(values, 3, out double[] fillRgb):
                    context.FillColorSpace = PdfColorSpaceDefinition.DeviceRgb;
                    context.Graphics = context.Graphics with
                    {
                        Fill = new PdfSolidBrush(PdfColor.Rgb(
                            fillRgb[0],
                            fillRgb[1],
                            fillRgb[2]))
                    };
                    break;
                case "K" when !context.InType3Glyph &&
                                   TryNumbers(values, 4, out double[] strokeCmyk):
                    context.StrokeColorSpace = PdfColorSpaceDefinition.DeviceCmyk;
                    context.Graphics = context.Graphics with
                    {
                        Stroke = new PdfSolidBrush(PdfColor.Cmyk(
                            strokeCmyk[0],
                            strokeCmyk[1],
                            strokeCmyk[2],
                            strokeCmyk[3]))
                    };
                    break;
                case "k" when !context.InType3Glyph &&
                                   TryNumbers(values, 4, out double[] fillCmyk):
                    context.FillColorSpace = PdfColorSpaceDefinition.DeviceCmyk;
                    context.Graphics = context.Graphics with
                    {
                        Fill = new PdfSolidBrush(PdfColor.Cmyk(
                            fillCmyk[0],
                            fillCmyk[1],
                            fillCmyk[2],
                            fillCmyk[3]))
                    };
                    break;
                case "SC" when !context.InType3Glyph:
                    SetGenericColor(values, context, resources, stroke: true, pattern: false, depth);
                    break;
                case "sc" when !context.InType3Glyph:
                    SetGenericColor(values, context, resources, stroke: false, pattern: false, depth);
                    break;
                case "SCN" when !context.InType3Glyph:
                    SetGenericColor(values, context, resources, stroke: true, pattern: true, depth);
                    break;
                case "scn" when !context.InType3Glyph:
                    SetGenericColor(values, context, resources, stroke: false, pattern: true, depth);
                    break;
                case "m" when TryNumbers(values, 2, out double[] move):
                    path.MoveTo(move[0], move[1]);
                    EnsurePathLimit(path);
                    break;
                case "l" when TryNumbers(values, 2, out double[] line):
                    path.LineTo(line[0], line[1]);
                    EnsurePathLimit(path);
                    break;
                case "c" when TryNumbers(values, 6, out double[] curve):
                    path.CurveTo(
                        curve[0], curve[1], curve[2],
                        curve[3], curve[4], curve[5]);
                    EnsurePathLimit(path);
                    break;
                case "v" when TryNumbers(values, 4, out double[] curveV):
                    path.CurveToV(curveV[0], curveV[1], curveV[2], curveV[3]);
                    EnsurePathLimit(path);
                    break;
                case "y" when TryNumbers(values, 4, out double[] curveY):
                    path.CurveToY(curveY[0], curveY[1], curveY[2], curveY[3]);
                    EnsurePathLimit(path);
                    break;
                case "h":
                    path.Close();
                    EnsurePathLimit(path);
                    break;
                case "re" when TryNumbers(values, 4, out double[] rectangle):
                    path.Rectangle(rectangle[0], rectangle[1], rectangle[2], rectangle[3]);
                    EnsurePathLimit(path);
                    break;
                case "W":
                    pendingClip = PdfFillRule.NonZero;
                    break;
                case "W*":
                    pendingClip = PdfFillRule.EvenOdd;
                    break;
                case "S":
                    FinishPath(
                        path,
                        PdfPaintMode.Stroke,
                        PdfFillRule.NonZero,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "s":
                    path.Close();
                    FinishPath(
                        path,
                        PdfPaintMode.Stroke,
                        PdfFillRule.NonZero,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "f":
                case "F":
                    FinishPath(
                        path,
                        PdfPaintMode.Fill,
                        PdfFillRule.NonZero,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "f*":
                    FinishPath(
                        path,
                        PdfPaintMode.Fill,
                        PdfFillRule.EvenOdd,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "B":
                    FinishPath(
                        path,
                        PdfPaintMode.Fill | PdfPaintMode.Stroke,
                        PdfFillRule.NonZero,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "B*":
                    FinishPath(
                        path,
                        PdfPaintMode.Fill | PdfPaintMode.Stroke,
                        PdfFillRule.EvenOdd,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "b":
                    path.Close();
                    FinishPath(
                        path,
                        PdfPaintMode.Fill | PdfPaintMode.Stroke,
                        PdfFillRule.NonZero,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "b*":
                    path.Close();
                    FinishPath(
                        path,
                        PdfPaintMode.Fill | PdfPaintMode.Stroke,
                        PdfFillRule.EvenOdd,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "n":
                    FinishPath(
                        path,
                        PdfPaintMode.None,
                        PdfFillRule.NonZero,
                        pendingClip,
                        context,
                        output,
                        sourceResource);
                    pendingClip = null;
                    break;
                case "Do" when values.LastOrDefault() is PdfName xObjectName:
                    InvokeXObject(
                        resources,
                        xObjectName.Value,
                        context,
                        output,
                        depth,
                        sourceResource);
                    break;
                case "sh" when values.LastOrDefault() is PdfName shadingName:
                    PaintShading(
                        resources,
                        shadingName.Value,
                        context,
                        output,
                        sourceResource);
                    break;
                case "BI" when operation.InlineImageDictionary is not null:
                    PaintInlineImage(
                        operation,
                        resources,
                        context,
                        output,
                        sourceResource);
                    break;
                case "BT":
                    context.Text.InTextObject = true;
                    context.Text.TextMatrix = PdfMatrix.Identity;
                    context.Text.LineMatrix = PdfMatrix.Identity;
                    context.Text.PendingClips.Clear();
                    break;
                case "ET":
                    context.Text.InTextObject = false;
                    context.Clips.AddRange(context.Text.PendingClips);
                    context.Text.PendingClips.Clear();
                    break;
                case "Tf" when values.Count >= 2 &&
                                    values[^2] is PdfName fontName &&
                                    LastNumber(values) is { } fontSize:
                    context.Text.Font =
                        fonts.GetValueOrDefault(fontName.Value, fallbackFont);
                    context.Text.FontResourceName = fontName.Value;
                    context.Text.FontSize = fontSize;
                    break;
                case "Tm" when TryNumbers(values, 6, out double[] textMatrix):
                    context.Text.TextMatrix = new PdfMatrix(
                        textMatrix[0], textMatrix[1], textMatrix[2],
                        textMatrix[3], textMatrix[4], textMatrix[5]);
                    context.Text.LineMatrix = context.Text.TextMatrix;
                    break;
                case "Td" when TryLastPair(values, out double tdX, out double tdY):
                    MoveText(context.Text, tdX, tdY);
                    break;
                case "TD" when TryLastPair(values, out double tdx, out double tdy):
                    context.Text.Leading = -tdy;
                    MoveText(context.Text, tdx, tdy);
                    break;
                case "T*":
                    MoveText(context.Text, 0, -context.Text.Leading);
                    break;
                case "Tc" when LastNumber(values) is { } characterSpacing:
                    context.Text.CharacterSpacing = characterSpacing;
                    break;
                case "Tw" when LastNumber(values) is { } wordSpacing:
                    context.Text.WordSpacing = wordSpacing;
                    break;
                case "Tz" when LastNumber(values) is { } horizontalScale:
                    context.Text.HorizontalScale = horizontalScale / 100.0;
                    break;
                case "TL" when LastNumber(values) is { } leading:
                    context.Text.Leading = leading;
                    break;
                case "Ts" when LastNumber(values) is { } rise:
                    context.Text.Rise = rise;
                    break;
                case "Tr" when LastInteger(values) is { } renderingMode &&
                                    renderingMode is >= 0 and <= 7:
                    context.Text.RenderingMode = (PdfTextRenderingMode)renderingMode;
                    break;
                case "Tj" when context.Text.InTextObject &&
                                    values.LastOrDefault() is PdfString shownText:
                    ShowText(
                        shownText,
                        context,
                        output,
                        resources,
                        depth,
                        sourceResource);
                    break;
                case "TJ" when context.Text.InTextObject &&
                                    values.LastOrDefault() is PdfArray textArray:
                    ShowTextArray(
                        textArray,
                        context,
                        output,
                        resources,
                        depth,
                        sourceResource);
                    break;
                case "'" when context.Text.InTextObject &&
                                   values.LastOrDefault() is PdfString quoteText:
                    MoveText(context.Text, 0, -context.Text.Leading);
                    ShowText(
                        quoteText,
                        context,
                        output,
                        resources,
                        depth,
                        sourceResource);
                    break;
                case "\"" when context.Text.InTextObject &&
                                      values.Count >= 3 &&
                                      values[^1] is PdfString doubleQuoteText &&
                                      values[^2] is PdfNumber quoteCharacterSpacing &&
                                      values[^3] is PdfNumber quoteWordSpacing:
                    context.Text.WordSpacing = quoteWordSpacing.Value;
                    context.Text.CharacterSpacing = quoteCharacterSpacing.Value;
                    MoveText(context.Text, 0, -context.Text.Leading);
                    ShowText(
                        doubleQuoteText,
                        context,
                        output,
                        resources,
                        depth,
                        sourceResource);
                    break;
            }
        }
    }

    private void ShowTextArray(
        PdfArray array,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        PdfDictionary? resources,
        int depth,
        string? sourceResource)
    {
        foreach (PdfObject item in array)
        {
            if (item is PdfString text)
            {
                ShowText(
                    text,
                    context,
                    output,
                    resources,
                    depth,
                    sourceResource);
            }
            else if (item is PdfNumber adjustment)
            {
                double movement =
                    -adjustment.Value / 1000.0 * context.Text.FontSize;
                context.Text.TextMatrix =
                    context.Text.Font?.WritingMode == FontWritingMode.Vertical
                        ? context.Text.TextMatrix.Translate(0, movement)
                        : context.Text.TextMatrix.Translate(
                            movement * context.Text.HorizontalScale,
                            0);
            }
        }
    }

    private void ShowText(
        PdfString value,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        PdfDictionary? resources,
        int depth,
        string? sourceResource)
    {
        PdfFontDecoder? font = context.Text.Font;
        if (font is null)
            return;
        IReadOnlyList<PdfDecodedGlyph> decoded =
            font.DecodeGlyphs(value.Bytes.Span);
        if (decoded.Count == 0)
            return;

        var placements = new List<PdfTextGlyphPlacement>(decoded.Count);
        var type3Programs = new List<(
            PdfTextGlyphPlacement Placement,
            byte[] Program,
            PdfMatrix FontMatrix,
            PdfObject? Resources,
            string GlyphName)>();
        foreach (PdfDecodedGlyph glyph in decoded)
        {
            PdfMatrix transform = CreateGlyphTransform(
                glyph,
                context.Text,
                context.Graphics.Transform,
                font.WritingMode);
            var placement = new PdfTextGlyphPlacement(glyph, transform);
            placements.Add(placement);
            if (font.TryGetType3GlyphProgram(
                    glyph,
                    out byte[] type3Program,
                    out PdfMatrix type3Matrix,
                    out PdfObject? type3Resources,
                    out string glyphName))
            {
                type3Programs.Add((
                    placement,
                    type3Program,
                    type3Matrix,
                    type3Resources,
                    glyphName));
            }
            if (ClipsText(context.Text.RenderingMode) &&
                font.TryGetGlyphOutline(
                    glyph,
                    out PdfGraphicsPath outline,
                    out _,
                    out _,
                    out _))
            {
                context.Text.PendingClips.Add(new PdfClipPath(
                    outline,
                    transform,
                    PdfFillRule.NonZero));
            }

            double spacing =
                context.Text.CharacterSpacing +
                (glyph.IsWordSpace ? context.Text.WordSpacing : 0);
            if (font.WritingMode == FontWritingMode.Vertical)
            {
                double advance =
                    glyph.AdvanceY / 1000.0 * context.Text.FontSize -
                    spacing;
                context.Text.TextMatrix =
                    context.Text.TextMatrix.Translate(0, advance);
            }
            else
            {
                double advance =
                    (glyph.AdvanceX / 1000.0 * context.Text.FontSize + spacing) *
                    context.Text.HorizontalScale;
                context.Text.TextMatrix =
                    context.Text.TextMatrix.Translate(advance, 0);
            }
        }

        Emit(
            output,
            new PdfTextElement(
                string.Concat(decoded.Select(glyph => glyph.Text)),
                context.Text.FontResourceName,
                font.Name,
                context.Text.FontSize,
                context.Text.RenderingMode,
                font,
                placements,
                context.Graphics,
                context.Clips.ToArray(),
                sourceResource));

        if (type3Programs.Count > 0 &&
            context.Text.RenderingMode is not (
                PdfTextRenderingMode.Invisible or
                PdfTextRenderingMode.Clip))
        {
            foreach (var glyph in type3Programs)
            {
                PaintType3Glyph(
                    glyph.Placement,
                    glyph.Program,
                    glyph.FontMatrix,
                    glyph.Resources ?? resources,
                    glyph.GlyphName,
                    context,
                    output,
                    depth,
                    sourceResource);
            }
        }
    }

    private void PaintType3Glyph(
        PdfTextGlyphPlacement placement,
        byte[] program,
        PdfMatrix fontMatrix,
        PdfObject? resources,
        string glyphName,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        int depth,
        string? parentSource)
    {
        if (depth >= _document.Options.MaximumXObjectDepth)
            throw new PdfLimitException("Type 3 glyph nesting exceeds the configured limit.");
        PdfReference? reference = resources as PdfReference;
        if (reference is not null && !_activeType3Glyphs.Add(reference))
        {
            ReportOnce(
                "graphics.type3.recursive",
                "A recursive Type 3 glyph was skipped.");
            return;
        }

        try
        {
            GraphicsContext child = context.Clone();
            child.Graphics = child.Graphics with
            {
                Transform = fontMatrix.Multiply(placement.Transform)
            };
            child.Text.InTextObject = false;
            child.Text.PendingClips.Clear();
            child.InType3Glyph = true;
            string source = parentSource is null
                ? $"Type3:{glyphName}"
                : $"{parentSource}/Type3:{glyphName}";
            Execute(
                program,
                resources,
                child,
                output,
                depth + 1,
                source);
        }
        finally
        {
            if (reference is not null)
                _activeType3Glyphs.Remove(reference);
        }
    }

    private static PdfMatrix CreateGlyphTransform(
        PdfDecodedGlyph glyph,
        TextContext text,
        PdfMatrix ctm,
        FontWritingMode writingMode)
    {
        double originX = 0;
        double originY = text.Rise;
        if (writingMode == FontWritingMode.Vertical)
        {
            originX -= glyph.OriginX / 1000.0 * text.FontSize;
            originY -= glyph.OriginY / 1000.0 * text.FontSize;
        }

        var fontScale = new PdfMatrix(
            text.FontSize * text.HorizontalScale,
            0,
            0,
            text.FontSize,
            originX,
            originY);
        return fontScale
            .Multiply(text.TextMatrix)
            .Multiply(ctm);
    }

    private static bool ClipsText(PdfTextRenderingMode mode) =>
        mode is PdfTextRenderingMode.FillAndClip or
            PdfTextRenderingMode.StrokeAndClip or
            PdfTextRenderingMode.FillStrokeAndClip or
            PdfTextRenderingMode.Clip;

    private static void MoveText(TextContext text, double x, double y)
    {
        text.LineMatrix = text.LineMatrix.Translate(x, y);
        text.TextMatrix = text.LineMatrix;
    }

    private void PaintInlineImage(
        PdfContentOperation operation,
        PdfDictionary? resources,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        string? parentSource)
    {
        PdfDictionary dictionary =
            ExpandInlineImageDictionary(operation.InlineImageDictionary!);
        string resourceName = $"InlineImage{++_inlineImageCount}";
        string source = parentSource is null
            ? resourceName
            : $"{parentSource}/{resourceName}";
        var stream = new PdfStream(dictionary, operation.InlineImageData.Span);
        int width = dictionary.GetValueOrNull("Width").AsInteger(_document) ?? 0;
        int height = dictionary.GetValueOrNull("Height").AsInteger(_document) ?? 0;
        bool imageMask =
            dictionary.GetValueOrNull("ImageMask") is PdfBoolean { Value: true };
        int bits = imageMask
            ? 1
            : dictionary.GetValueOrNull("BitsPerComponent").AsInteger(_document) ?? 0;
        string colorSpace = DescribeColorSpace(
            dictionary.GetValueOrNull("ColorSpace"),
            resources);
        PdfImage? image = null;
        try
        {
            PdfColor maskColor = context.Graphics.Fill is PdfSolidBrush solid
                ? solid.Color
                : PdfColor.Black;
            image = PdfImageDecoder.Decode(
                resourceName,
                stream,
                resources,
                _document,
                maskColor);
        }
        catch (PdfUnsupportedFeatureException exception)
        {
            ReportOnce(
                "graphics.inline-image.unsupported",
                $"An inline image was not decoded: {exception.Message}");
        }
        catch (PdfFormatException exception)
        {
            ReportOnce(
                "graphics.inline-image.invalid",
                $"An inline image is invalid: {exception.Message}");
        }

        Emit(
            output,
            new PdfImageElement(
                resourceName,
                Math.Max(0, width),
                Math.Max(0, height),
                Math.Max(0, bits),
                colorSpace,
                imageMask,
                image,
                context.Graphics,
                context.Clips.ToArray(),
                source));
    }

    private static PdfDictionary ExpandInlineImageDictionary(
        PdfDictionary source)
    {
        var values = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        foreach ((string key, PdfObject value) in source)
        {
            string expandedKey = key switch
            {
                "BPC" => "BitsPerComponent",
                "CS" => "ColorSpace",
                "D" => "Decode",
                "DP" => "DecodeParms",
                "F" => "Filter",
                "H" => "Height",
                "IM" => "ImageMask",
                "I" => "Interpolate",
                "W" => "Width",
                _ => key
            };
            values[expandedKey] = ExpandInlineImageValue(expandedKey, value);
        }

        return new PdfDictionary(values);
    }

    private static PdfObject ExpandInlineImageValue(string key, PdfObject value)
    {
        if (value is PdfArray array)
        {
            return new PdfArray(
                array.Select(item => ExpandInlineImageValue(key, item)));
        }
        if (value is not PdfName name)
            return value;
        string expanded = key switch
        {
            "ColorSpace" => name.Value switch
            {
                "G" => "DeviceGray",
                "RGB" => "DeviceRGB",
                "CMYK" => "DeviceCMYK",
                "I" => "Indexed",
                _ => name.Value
            },
            "Filter" => name.Value switch
            {
                "AHx" => "ASCIIHexDecode",
                "A85" => "ASCII85Decode",
                "LZW" => "LZWDecode",
                "Fl" => "FlateDecode",
                "RL" => "RunLengthDecode",
                "CCF" => "CCITTFaxDecode",
                "DCT" => "DCTDecode",
                _ => name.Value
            },
            _ => name.Value
        };
        return new PdfName(expanded);
    }

    private void FinishPath(
        PdfPathBuilder builder,
        PdfPaintMode paintMode,
        PdfFillRule fillRule,
        PdfFillRule? pendingClip,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        string? sourceResource)
    {
        if (!builder.IsEmpty)
        {
            PdfGraphicsPath path = builder.Snapshot();
            if (paintMode != PdfPaintMode.None)
            {
                Emit(
                    output,
                    new PdfPathElement(
                        path,
                        paintMode,
                        fillRule,
                        context.Graphics,
                        context.Clips.ToArray(),
                        sourceResource));
            }

            if (pendingClip is { } clipRule)
            {
                context.Clips.Add(new PdfClipPath(
                    path,
                    context.Graphics.Transform,
                    clipRule));
            }
        }

        builder.Clear();
    }

    private void InvokeXObject(
        PdfDictionary? resources,
        string resourceName,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        int depth,
        string? parentSource)
    {
        PdfObject? xObject = LookupResource(resources, "XObject", resourceName);
        PdfStream? stream = xObject.AsStream(_document);
        if (stream is null)
            return;
        if (!_optionalContent.IsVisible(
                stream.Dictionary.GetValueOrNull("OC"),
                resources))
        {
            return;
        }
        string? subtype = stream.Dictionary.GetValueOrNull("Subtype").AsName(_document);
        string source = parentSource is null
            ? resourceName
            : $"{parentSource}/{resourceName}";
        if (subtype == "Image")
        {
            int width = stream.Dictionary.GetValueOrNull("Width").AsInteger(_document) ?? 0;
            int height = stream.Dictionary.GetValueOrNull("Height").AsInteger(_document) ?? 0;
            int bits = stream.Dictionary.GetValueOrNull("BitsPerComponent").AsInteger(_document) ?? 1;
            string colorSpace = DescribeColorSpace(
                stream.Dictionary.GetValueOrNull("ColorSpace"),
                resources);
            bool imageMask =
                stream.Dictionary.GetValueOrNull("ImageMask")?.Resolve(_document)
                    is PdfBoolean { Value: true };
            PdfImage? image = null;
            try
            {
                PdfColor maskColor = context.Graphics.Fill is PdfSolidBrush solid
                    ? solid.Color
                    : PdfColor.Black;
                image = PdfImageDecoder.Decode(
                    resourceName,
                    stream,
                    resources,
                    _document,
                    maskColor);
            }
            catch (PdfUnsupportedFeatureException exception)
            {
                ReportOnce(
                    $"graphics.image.unsupported.{resourceName}",
                    $"Image /{resourceName} was not decoded: {exception.Message}");
            }
            catch (PdfFormatException exception)
            {
                ReportOnce(
                    $"graphics.image.invalid.{resourceName}",
                    $"Image /{resourceName} is invalid: {exception.Message}");
            }

            Emit(
                output,
                new PdfImageElement(
                    resourceName,
                    Math.Max(0, width),
                    Math.Max(0, height),
                    Math.Max(0, bits),
                    colorSpace,
                    imageMask,
                    image,
                    context.Graphics,
                    context.Clips.ToArray(),
                    source));
            return;
        }

        if (subtype != "Form")
            return;
        PdfReference? reference = stream.SourceReference;
        if (reference is not null && !_activeForms.Add(reference))
        {
            ReportOnce(
                "graphics.form.recursive",
                $"Recursive form XObject /{resourceName} was skipped.");
            return;
        }

        try
        {
            GraphicsContext child = context.Clone();
            PdfMatrix matrix = PdfShadingReader.ReadMatrix(
                stream.Dictionary.GetValueOrNull("Matrix"),
                _document,
                PdfMatrix.Identity);
            child.Graphics = child.Graphics with
            {
                Transform = matrix.Multiply(child.Graphics.Transform)
            };
            if (stream.Dictionary.GetValueOrNull("BBox").AsRectangle(_document) is { } box)
            {
                child.Clips.Add(new PdfClipPath(
                    RectanglePath(box),
                    child.Graphics.Transform,
                    PdfFillRule.NonZero));
            }

            PdfObject? childResources =
                stream.Dictionary.GetValueOrNull("Resources") ?? resources;
            PdfDictionary? group = stream.Dictionary
                .GetValueOrNull("Group")
                .AsDictionary(_document);
            bool transparencyGroup =
                group?.GetValueOrNull("S").AsName(_document) == "Transparency";
            if (transparencyGroup)
            {
                IReadOnlyList<PdfClipPath> parentClips = context.Clips.ToArray();
                child.Clips.Clear();
                if (stream.Dictionary.GetValueOrNull("BBox").AsRectangle(_document) is { } groupBox)
                {
                    child.Clips.Add(new PdfClipPath(
                        RectanglePath(groupBox),
                        child.Graphics.Transform,
                        PdfFillRule.NonZero));
                }

                PdfGraphicsState boundaryState = context.Graphics;
                child.Graphics = child.Graphics with
                {
                    FillAlpha = 1,
                    StrokeAlpha = 1,
                    BlendMode = "Normal",
                    SoftMask = null
                };
                var elements = new List<PdfGraphicsElement>();
                Execute(
                    _document.Decode(stream),
                    childResources,
                    child,
                    elements,
                    depth + 1,
                    source);
                PdfObject? isolatedValue = group?.GetValueOrNull("I");
                PdfObject? knockoutValue = group?.GetValueOrNull("K");
                bool isolated =
                    isolatedValue is not null &&
                    isolatedValue.Resolve(_document) is PdfBoolean { Value: true };
                bool knockout =
                    knockoutValue is not null &&
                    knockoutValue.Resolve(_document) is PdfBoolean { Value: true };
                Emit(
                    output,
                    new PdfTransparencyGroupElement(
                        elements,
                        isolated,
                        knockout,
                        boundaryState,
                        parentClips,
                        source));
            }
            else
            {
                Execute(
                    _document.Decode(stream),
                    childResources,
                    child,
                    output,
                    depth + 1,
                    source);
            }
        }
        finally
        {
            if (reference is not null)
                _activeForms.Remove(reference);
        }
    }

    private void PaintAnnotation(
        PdfAnnotationData data,
        List<PdfGraphicsElement> output)
    {
        PdfAnnotation annotation = data.Annotation;
        if (!_optionalContent.IsVisible(
                data.OptionalContent,
                _page.Resources.AsDictionary(_document)) ||
            (annotation.Flags &
             (PdfAnnotationFlags.Invisible |
              PdfAnnotationFlags.Hidden |
              PdfAnnotationFlags.NoView)) != 0 ||
            annotation.Rectangle.IsEmpty)
        {
            return;
        }

        string source = $"Annotation[{data.Index + 1}]/{annotation.Subtype}";
        if (data.NormalAppearance is { } appearance &&
            PaintAnnotationAppearance(
                annotation,
                appearance,
                source,
                output))
        {
            return;
        }

        if (data.FormWidget is { } formWidget)
        {
            PaintFormWidgetFallback(formWidget, annotation, source, output);
            return;
        }

        PaintAnnotationFallback(annotation, source, output);
    }

    private bool PaintAnnotationAppearance(
        PdfAnnotation annotation,
        PdfStream appearance,
        string source,
        List<PdfGraphicsElement> output)
    {
        if (appearance.Dictionary.GetValueOrNull("BBox").AsRectangle(_document)
                is not { } rawBox)
        {
            ReportOnce(
                "annotation.appearance.bbox.missing",
                "An annotation appearance without a usable /BBox used the managed fallback.");
            return false;
        }

        PdfRectangle box = NormalizeRectangle(rawBox);
        if (box.IsEmpty)
            return false;
        PdfMatrix appearanceMatrix = PdfShadingReader.ReadMatrix(
            appearance.Dictionary.GetValueOrNull("Matrix"),
            _document,
            PdfMatrix.Identity);
        if (!appearanceMatrix.IsFinite ||
            !TryMapAppearance(
                box,
                annotation.Rectangle,
                appearanceMatrix,
                out PdfMatrix transform))
        {
            ReportOnce(
                "annotation.appearance.matrix.invalid",
                "An annotation appearance with an invalid matrix used the managed fallback.");
            return false;
        }

        PdfReference? reference = appearance.SourceReference;
        if (reference is not null && !_activeForms.Add(reference))
        {
            ReportOnce(
                "annotation.appearance.recursive",
                "A recursive annotation appearance was skipped.");
            return false;
        }

        try
        {
            var context = GraphicsContext.Create();
            context.Graphics = context.Graphics with
            {
                Transform = transform
            };
            context.Clips.Add(new PdfClipPath(
                RectanglePath(box),
                transform,
                PdfFillRule.NonZero));
            context.Clips.Add(new PdfClipPath(
                RectanglePath(annotation.Rectangle),
                PdfMatrix.Identity,
                PdfFillRule.NonZero));
            PdfObject? resources =
                appearance.Dictionary.GetValueOrNull("Resources") ??
                _page.Resources;
            var elements = new List<PdfGraphicsElement>();
            Execute(
                _document.Decode(appearance),
                resources,
                context,
                elements,
                depth: 0,
                source);
            output.AddRange(elements);
            return true;
        }
        catch (PdfUnsupportedFeatureException exception)
        {
            ReportOnce(
                "annotation.appearance.unsupported",
                $"An annotation appearance used the managed fallback: {exception.Message}");
            return false;
        }
        catch (PdfFormatException exception)
        {
            ReportOnce(
                "annotation.appearance.invalid",
                $"An invalid annotation appearance used the managed fallback: {exception.Message}");
            return false;
        }
        finally
        {
            if (reference is not null)
                _activeForms.Remove(reference);
        }
    }

    private void PaintAnnotationFallback(
        PdfAnnotation annotation,
        string source,
        List<PdfGraphicsElement> output)
    {
        PdfColor color = annotation.Color ?? annotation.Type switch
        {
            PdfAnnotationType.Link => PdfColor.Rgb(0, 0, 1),
            PdfAnnotationType.Text or PdfAnnotationType.Highlight =>
                PdfColor.Rgb(1, 0.82, 0),
            _ => PdfColor.Black
        };
        double width = Math.Max(0, annotation.Border.Width);
        PdfDashPattern dash =
            annotation.Border.Style == PdfAnnotationBorderStyleKind.Dashed &&
            annotation.Border.DashPattern.Count > 0
                ? new PdfDashPattern(annotation.Border.DashPattern, 0)
                : PdfDashPattern.Solid;
        var state = new PdfGraphicsState
        {
            Fill = new PdfSolidBrush(annotation.InteriorColor ?? color),
            Stroke = new PdfSolidBrush(color),
            LineWidth = width,
            Dash = dash,
            FillAlpha = annotation.Opacity,
            StrokeAlpha = annotation.Opacity
        };

        switch (annotation.Type)
        {
            case PdfAnnotationType.Link when width > 0:
                EmitPath(
                    output,
                    RectanglePath(annotation.Rectangle),
                    PdfPaintMode.Stroke,
                    state,
                    source);
                break;
            case PdfAnnotationType.Text:
                PaintTextIcon(annotation.Rectangle, state, source, output);
                break;
            case PdfAnnotationType.FreeText:
                if (width > 0)
                {
                    EmitPath(
                        output,
                        RectanglePath(annotation.Rectangle),
                        PdfPaintMode.Stroke,
                        state,
                        source);
                }
                PaintFallbackText(annotation, source, output);
                break;
            case PdfAnnotationType.Highlight:
                PaintTextMarkup(
                    annotation,
                    PdfPaintMode.Fill,
                    state with
                    {
                        FillAlpha = Math.Min(annotation.Opacity, 0.45),
                        BlendMode = "Multiply"
                    },
                    source,
                    output);
                break;
            case PdfAnnotationType.Underline:
            case PdfAnnotationType.Squiggly:
            case PdfAnnotationType.StrikeOut:
                PaintTextMarkup(
                    annotation,
                    PdfPaintMode.Stroke,
                    state with
                    {
                        LineWidth = Math.Max(0.8, width)
                    },
                    source,
                    output);
                break;
            case PdfAnnotationType.Square:
                if (width > 0 || annotation.InteriorColor is not null)
                {
                    EmitPath(
                        output,
                        InsetRectangle(annotation.Rectangle, width / 2),
                        (width > 0 ? PdfPaintMode.Stroke : PdfPaintMode.None) |
                        (annotation.InteriorColor is not null
                            ? PdfPaintMode.Fill
                            : PdfPaintMode.None),
                        state,
                        source);
                }
                break;
            case PdfAnnotationType.Circle:
                if (width > 0 || annotation.InteriorColor is not null)
                {
                    EmitPath(
                        output,
                        EllipsePath(annotation.Rectangle, width / 2),
                        (width > 0 ? PdfPaintMode.Stroke : PdfPaintMode.None) |
                        (annotation.InteriorColor is not null
                            ? PdfPaintMode.Fill
                            : PdfPaintMode.None),
                        state,
                        source);
                }
                break;
            case PdfAnnotationType.Line:
                if (width > 0 && annotation.LinePoints.Count >= 2)
                {
                    EmitPath(
                        output,
                        PolylinePath(annotation.LinePoints.Take(2), close: false),
                        PdfPaintMode.Stroke,
                        state,
                        source);
                }
                break;
            case PdfAnnotationType.Polygon:
                if ((width > 0 || annotation.InteriorColor is not null) &&
                    annotation.Vertices.Count >= 2)
                {
                    EmitPath(
                        output,
                        PolylinePath(annotation.Vertices, close: true),
                        (width > 0
                            ? PdfPaintMode.Stroke
                            : PdfPaintMode.None) |
                        (annotation.InteriorColor is not null
                            ? PdfPaintMode.Fill
                            : PdfPaintMode.None),
                        state,
                        source);
                }
                break;
            case PdfAnnotationType.PolyLine:
                if (width > 0 && annotation.Vertices.Count >= 2)
                {
                    EmitPath(
                        output,
                        PolylinePath(annotation.Vertices, close: false),
                        PdfPaintMode.Stroke,
                        state,
                        source);
                }
                break;
            case PdfAnnotationType.Ink:
                if (width <= 0)
                    break;
                foreach (IReadOnlyList<PdfPoint> path in annotation.InkPaths)
                {
                    if (path.Count >= 2)
                    {
                        EmitPath(
                            output,
                            PolylinePath(path, close: false),
                            PdfPaintMode.Stroke,
                            state,
                            source);
                    }
                }
                break;
            case PdfAnnotationType.Stamp:
                EmitPath(
                    output,
                    InsetRectangle(annotation.Rectangle, Math.Max(1, width)),
                    PdfPaintMode.Stroke,
                    state with
                    {
                        LineWidth = Math.Max(2, width)
                    },
                    source);
                PaintFallbackText(annotation, source, output);
                break;
        }
    }

    private void PaintFormWidgetFallback(
        PdfFormWidgetData data,
        PdfAnnotation annotation,
        string source,
        List<PdfGraphicsElement> output)
    {
        PdfFormField field = data.Field;
        PdfFormWidget widget = data.Widget;
        PdfRectangle rectangle = widget.Rectangle;
        if (rectangle.IsEmpty)
            return;

        double width = Math.Max(0, annotation.Border.Width);
        PdfColor borderColor =
            widget.BorderColor ?? annotation.Color ?? PdfColor.Black;
        var state = new PdfGraphicsState
        {
            Fill = new PdfSolidBrush(widget.BackgroundColor ?? PdfColor.Gray(1)),
            Stroke = new PdfSolidBrush(borderColor),
            LineWidth = width,
            Dash =
                annotation.Border.Style == PdfAnnotationBorderStyleKind.Dashed &&
                annotation.Border.DashPattern.Count > 0
                    ? new PdfDashPattern(annotation.Border.DashPattern, 0)
                    : PdfDashPattern.Solid,
            FillAlpha = annotation.Opacity,
            StrokeAlpha = annotation.Opacity
        };

        PdfGraphicsPath boundary = field.ButtonType == PdfButtonType.RadioButton
            ? EllipsePath(rectangle, width / 2)
            : InsetRectangle(rectangle, width / 2);
        PdfPaintMode boundaryMode =
            (widget.BackgroundColor is not null
                ? PdfPaintMode.Fill
                : PdfPaintMode.None) |
            (width > 0 ? PdfPaintMode.Stroke : PdfPaintMode.None);
        if (boundaryMode != PdfPaintMode.None)
            EmitPath(output, boundary, boundaryMode, state, source);

        switch (field.Type)
        {
            case PdfFormFieldType.Button:
                PaintButtonWidget(data, state, source, output);
                break;
            case PdfFormFieldType.Text:
            {
                string text = field.Value;
                if ((field.Flags & PdfFormFieldFlags.Password) != 0)
                    text = new string('*', Math.Min(text.Length, 160));
                PaintWidgetText(
                    text,
                    data,
                    multiline:
                        (field.Flags & PdfFormFieldFlags.Multiline) != 0,
                    source,
                    output);
                break;
            }
            case PdfFormFieldType.Choice:
            {
                string[] selected = field.Options
                    .Where(option => option.IsSelected)
                    .Select(option => option.DisplayValue)
                    .Where(value => !string.IsNullOrEmpty(value))
                    .ToArray();
                string text = selected.Length > 0
                    ? string.Join(
                        (field.Flags & PdfFormFieldFlags.Combo) != 0
                            ? ""
                            : "\n",
                        selected)
                    : field.Value;
                PaintWidgetText(
                    text,
                    data,
                    multiline:
                        (field.Flags & PdfFormFieldFlags.Combo) == 0,
                    source,
                    output);
                break;
            }
            case PdfFormFieldType.Signature when field.IsSigned:
                PaintWidgetText(
                    string.IsNullOrWhiteSpace(widget.Caption)
                        ? "SIGNED"
                        : widget.Caption,
                    data,
                    multiline: false,
                    source,
                    output);
                break;
        }
    }

    private void PaintButtonWidget(
        PdfFormWidgetData data,
        PdfGraphicsState state,
        string source,
        List<PdfGraphicsElement> output)
    {
        PdfFormField field = data.Field;
        PdfFormWidget widget = data.Widget;
        PdfRectangle rectangle = widget.Rectangle;
        if (field.ButtonType == PdfButtonType.PushButton)
        {
            string caption = !string.IsNullOrWhiteSpace(widget.Caption)
                ? widget.Caption
                : !string.IsNullOrWhiteSpace(field.AlternateName)
                    ? field.AlternateName
                    : field.PartialName;
            PaintWidgetText(caption, data, multiline: false, source, output);
            return;
        }

        bool selected =
            !string.IsNullOrEmpty(widget.OnState) &&
            (string.Equals(
                 field.Value,
                 widget.OnState,
                 StringComparison.Ordinal) ||
             string.Equals(
                 widget.AppearanceState,
                 widget.OnState,
                 StringComparison.Ordinal));
        if (!selected)
            return;

        PdfColor markColor = data.TextColor;
        if (field.ButtonType == PdfButtonType.RadioButton)
        {
            EmitPath(
                output,
                EllipsePath(
                    rectangle,
                    Math.Min(rectangle.Width, rectangle.Height) * 0.3),
                PdfPaintMode.Fill,
                state with
                {
                    Fill = new PdfSolidBrush(markColor)
                },
                source);
            return;
        }

        double left = rectangle.Left + rectangle.Width * 0.2;
        double bottom = rectangle.Bottom + rectangle.Height * 0.5;
        double middleX = rectangle.Left + rectangle.Width * 0.43;
        double middleY = rectangle.Bottom + rectangle.Height * 0.25;
        double right = rectangle.Right - rectangle.Width * 0.15;
        double top = rectangle.Top - rectangle.Height * 0.2;
        var check = new PdfPathBuilder();
        check.MoveTo(left, bottom);
        check.LineTo(middleX, middleY);
        check.LineTo(right, top);
        EmitPath(
            output,
            check.Snapshot(),
            PdfPaintMode.Stroke,
            state with
            {
                Stroke = new PdfSolidBrush(markColor),
                LineWidth = Math.Max(1.5, Math.Min(
                    rectangle.Width,
                    rectangle.Height) * 0.12),
                LineCap = PdfLineCap.Round,
                LineJoin = PdfLineJoin.Round
            },
            source);
    }

    private void PaintWidgetText(
        string text,
        PdfFormWidgetData data,
        bool multiline,
        string source,
        List<PdfGraphicsElement> output)
    {
        PdfRectangle rectangle = data.Widget.Rectangle;
        double inset = Math.Min(2, Math.Min(
            rectangle.Width,
            rectangle.Height) * 0.1);
        var textRectangle = new PdfRectangle(
            rectangle.Left + inset,
            rectangle.Bottom + inset,
            rectangle.Right - inset,
            rectangle.Top - inset);
        PaintCellText(
            text,
            textRectangle,
            data.TextColor,
            data.FontSize,
            data.Field.Alignment,
            multiline,
            source,
            output);
    }

    private void PaintCellText(
        string text,
        PdfRectangle rectangle,
        PdfColor color,
        double requestedFontSize,
        PdfTextAlignment alignment,
        bool multiline,
        string source,
        List<PdfGraphicsElement> output)
    {
        if (string.IsNullOrWhiteSpace(text) || rectangle.IsEmpty)
            return;
        double fontSize = requestedFontSize > 0
            ? requestedFontSize
            : multiline
                ? Math.Clamp(rectangle.Height * 0.22, 6, 12)
                : Math.Clamp(rectangle.Height * 0.58, 6, 14);
        fontSize = Math.Clamp(fontSize, 4, Math.Max(4, rectangle.Height));
        double cell = fontSize / 7;
        double advance = cell * 6;
        int maximumCharacters = Math.Clamp(
            (int)Math.Floor(rectangle.Width / Math.Max(1, advance)),
            1,
            256);
        string[] lines = text
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n')
            .SelectMany(line => multiline
                ? WrapAnnotationText(line, maximumCharacters)
                : new[] { line })
            .Select(line => line.Length > maximumCharacters
                ? line[..maximumCharacters]
                : line)
            .Take(multiline
                ? Math.Max(1, (int)(rectangle.Height / (fontSize * 1.25)))
                : 1)
            .ToArray();
        if (lines.Length == 0)
            return;

        var path = new PdfPathBuilder();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            double lineWidth = line.Length * advance;
            double x = alignment switch
            {
                PdfTextAlignment.Center =>
                    rectangle.Left + Math.Max(0, (rectangle.Width - lineWidth) / 2),
                PdfTextAlignment.Right =>
                    rectangle.Right - Math.Min(rectangle.Width, lineWidth),
                _ => rectangle.Left
            };
            double top = multiline
                ? rectangle.Top - lineIndex * fontSize * 1.25
                : rectangle.Bottom + (rectangle.Height + fontSize) / 2;
            foreach (char sourceCharacter in line)
            {
                char character = char.ToUpperInvariant(sourceCharacter);
                if (!AnnotationGlyphs.TryGetValue(character, out byte[]? rows))
                    rows = AnnotationGlyphs['?'];
                for (int row = 0; row < rows.Length; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if ((rows[row] & (1 << (4 - column))) == 0)
                            continue;
                        path.Rectangle(
                            x + column * cell,
                            top - (row + 1) * cell,
                            cell * 0.92,
                            cell * 0.92);
                    }
                }
                x += advance;
            }
        }
        if (path.IsEmpty)
            return;
        Emit(
            output,
            new PdfPathElement(
                path.Snapshot(),
                PdfPaintMode.Fill,
                PdfFillRule.NonZero,
                new PdfGraphicsState
                {
                    Fill = new PdfSolidBrush(color)
                },
                new[]
                {
                    new PdfClipPath(
                        RectanglePath(rectangle),
                        PdfMatrix.Identity,
                        PdfFillRule.NonZero)
                },
                source));
    }

    private void PaintTextIcon(
        PdfRectangle rectangle,
        PdfGraphicsState state,
        string source,
        List<PdfGraphicsElement> output)
    {
        double side = Math.Min(18, Math.Min(rectangle.Width, rectangle.Height));
        var icon = new PdfRectangle(
            rectangle.Left,
            rectangle.Top - side,
            rectangle.Left + side,
            rectangle.Top);
        EmitPath(
            output,
            RectanglePath(icon),
            PdfPaintMode.Fill | PdfPaintMode.Stroke,
            state with
            {
                Stroke = new PdfSolidBrush(PdfColor.Black),
                LineWidth = Math.Max(0.8, state.LineWidth)
            },
            source);
        var fold = new PdfPathBuilder();
        fold.MoveTo(icon.Right - side * 0.35, icon.Top);
        fold.LineTo(icon.Right - side * 0.35, icon.Top - side * 0.35);
        fold.LineTo(icon.Right, icon.Top - side * 0.35);
        EmitPath(
            output,
            fold.Snapshot(),
            PdfPaintMode.Stroke,
            state with
            {
                Stroke = new PdfSolidBrush(PdfColor.Black),
                LineWidth = Math.Max(0.8, state.LineWidth)
            },
            source);
    }

    private void PaintTextMarkup(
        PdfAnnotation annotation,
        PdfPaintMode mode,
        PdfGraphicsState state,
        string source,
        List<PdfGraphicsElement> output)
    {
        if (annotation.QuadPoints.Count < 4)
        {
            if (annotation.Type == PdfAnnotationType.Highlight)
            {
                EmitPath(
                    output,
                    RectanglePath(annotation.Rectangle),
                    mode,
                    state,
                    source);
            }
            return;
        }

        for (int index = 0; index + 3 < annotation.QuadPoints.Count; index += 4)
        {
            PdfPoint[] quad = annotation.QuadPoints
                .Skip(index)
                .Take(4)
                .ToArray();
            double left = quad.Min(point => point.X);
            double right = quad.Max(point => point.X);
            double bottom = quad.Min(point => point.Y);
            double top = quad.Max(point => point.Y);
            var rectangle = new PdfRectangle(left, bottom, right, top);
            if (annotation.Type == PdfAnnotationType.Highlight)
            {
                EmitPath(
                    output,
                    RoundedRectanglePath(
                        rectangle,
                        Math.Min(rectangle.Width, rectangle.Height) / 2),
                    mode,
                    state,
                    source);
                continue;
            }

            double y = annotation.Type == PdfAnnotationType.StrikeOut
                ? (bottom + top) / 2
                : bottom + Math.Max(0.5, (top - bottom) * 0.08);
            PdfGraphicsPath line = annotation.Type == PdfAnnotationType.Squiggly
                ? SquigglyPath(left, right, y, Math.Max(1, (top - bottom) * 0.08))
                : LinePath(new PdfPoint(left, y), new PdfPoint(right, y));
            EmitPath(output, line, mode, state, source);
        }
    }

    private void PaintFallbackText(
        PdfAnnotation annotation,
        string source,
        List<PdfGraphicsElement> output)
    {
        if (string.IsNullOrWhiteSpace(annotation.Contents))
            return;
        PdfRectangle rectangle = annotation.Rectangle;
        double fontSize = Math.Clamp(rectangle.Height * 0.2, 7, 12);
        int maximumCharacters = Math.Clamp(
            (int)(rectangle.Width / Math.Max(1, fontSize * 0.7)),
            1,
            160);
        string[] lines = annotation.Contents
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n')
            .SelectMany(line => WrapAnnotationText(line, maximumCharacters))
            .Take(Math.Max(1, (int)(rectangle.Height / (fontSize * 1.25))))
            .ToArray();
        if (lines.Length == 0)
            return;

        double cell = fontSize / 7;
        double advance = cell * 6;
        var path = new PdfPathBuilder();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            double top = rectangle.Top - 2 - lineIndex * fontSize * 1.25;
            double x = rectangle.Left + 2;
            foreach (char sourceCharacter in lines[lineIndex])
            {
                char character = char.ToUpperInvariant(sourceCharacter);
                if (!AnnotationGlyphs.TryGetValue(character, out byte[]? rows))
                    rows = AnnotationGlyphs['?'];
                for (int row = 0; row < rows.Length; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if ((rows[row] & (1 << (4 - column))) == 0)
                            continue;
                        path.Rectangle(
                            x + column * cell,
                            top - (row + 1) * cell,
                            cell * 0.92,
                            cell * 0.92);
                    }
                }
                x += advance;
            }
        }
        Emit(
            output,
            new PdfPathElement(
                path.Snapshot(),
                PdfPaintMode.Fill,
                PdfFillRule.NonZero,
                new PdfGraphicsState
                {
                    Fill = new PdfSolidBrush(PdfColor.Black),
                    FillAlpha = annotation.Opacity
                },
                new[]
                {
                    new PdfClipPath(
                        RectanglePath(rectangle),
                        PdfMatrix.Identity,
                        PdfFillRule.NonZero)
                },
                source));
    }

    private void EmitPath(
        List<PdfGraphicsElement> output,
        PdfGraphicsPath path,
        PdfPaintMode mode,
        PdfGraphicsState state,
        string source)
    {
        if (path.IsEmpty)
            return;
        Emit(
            output,
            new PdfPathElement(
                path,
                mode,
                PdfFillRule.NonZero,
                state,
                Array.Empty<PdfClipPath>(),
                source));
    }

    private static bool TryMapAppearance(
        PdfRectangle box,
        PdfRectangle target,
        PdfMatrix appearanceMatrix,
        out PdfMatrix transform)
    {
        PdfPoint[] corners =
        {
            appearanceMatrix.Transform(box.Left, box.Bottom),
            appearanceMatrix.Transform(box.Right, box.Bottom),
            appearanceMatrix.Transform(box.Right, box.Top),
            appearanceMatrix.Transform(box.Left, box.Top)
        };
        if (corners.Any(point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            transform = default;
            return false;
        }

        double left = corners.Min(point => point.X);
        double right = corners.Max(point => point.X);
        double bottom = corners.Min(point => point.Y);
        double top = corners.Max(point => point.Y);
        if (right <= left || top <= bottom)
        {
            transform = default;
            return false;
        }

        double scaleX = target.Width / (right - left);
        double scaleY = target.Height / (top - bottom);
        var mapping = new PdfMatrix(
            scaleX,
            0,
            0,
            scaleY,
            target.Left - left * scaleX,
            target.Bottom - bottom * scaleY);
        transform = appearanceMatrix.Multiply(mapping);
        return transform.IsFinite;
    }

    private static PdfRectangle NormalizeRectangle(PdfRectangle rectangle) =>
        new(
            Math.Min(rectangle.Left, rectangle.Right),
            Math.Min(rectangle.Bottom, rectangle.Top),
            Math.Max(rectangle.Left, rectangle.Right),
            Math.Max(rectangle.Bottom, rectangle.Top));

    private static PdfGraphicsPath InsetRectangle(
        PdfRectangle rectangle,
        double inset)
    {
        double maximum = Math.Min(rectangle.Width, rectangle.Height) / 2;
        inset = Math.Clamp(inset, 0, maximum);
        return RectanglePath(new PdfRectangle(
            rectangle.Left + inset,
            rectangle.Bottom + inset,
            rectangle.Right - inset,
            rectangle.Top - inset));
    }

    private static PdfGraphicsPath EllipsePath(
        PdfRectangle rectangle,
        double inset)
    {
        double maximum = Math.Min(rectangle.Width, rectangle.Height) / 2;
        inset = Math.Clamp(inset, 0, maximum);
        double left = rectangle.Left + inset;
        double right = rectangle.Right - inset;
        double bottom = rectangle.Bottom + inset;
        double top = rectangle.Top - inset;
        double centerX = (left + right) / 2;
        double centerY = (bottom + top) / 2;
        double radiusX = (right - left) / 2;
        double radiusY = (top - bottom) / 2;
        const double kappa = 0.5522847498307936;
        var builder = new PdfPathBuilder();
        builder.MoveTo(centerX + radiusX, centerY);
        builder.CurveTo(
            centerX + radiusX,
            centerY + radiusY * kappa,
            centerX + radiusX * kappa,
            centerY + radiusY,
            centerX,
            centerY + radiusY);
        builder.CurveTo(
            centerX - radiusX * kappa,
            centerY + radiusY,
            centerX - radiusX,
            centerY + radiusY * kappa,
            centerX - radiusX,
            centerY);
        builder.CurveTo(
            centerX - radiusX,
            centerY - radiusY * kappa,
            centerX - radiusX * kappa,
            centerY - radiusY,
            centerX,
            centerY - radiusY);
        builder.CurveTo(
            centerX + radiusX * kappa,
            centerY - radiusY,
            centerX + radiusX,
            centerY - radiusY * kappa,
            centerX + radiusX,
            centerY);
        builder.Close();
        return builder.Snapshot();
    }

    private static PdfGraphicsPath RoundedRectanglePath(
        PdfRectangle rectangle,
        double radius)
    {
        radius = Math.Clamp(
            radius,
            0,
            Math.Min(rectangle.Width, rectangle.Height) / 2);
        if (radius <= 0)
            return RectanglePath(rectangle);
        const double kappa = 0.5522847498307936;
        double offset = radius * kappa;
        var builder = new PdfPathBuilder();
        builder.MoveTo(rectangle.Left + radius, rectangle.Bottom);
        builder.LineTo(rectangle.Right - radius, rectangle.Bottom);
        builder.CurveTo(
            rectangle.Right - radius + offset,
            rectangle.Bottom,
            rectangle.Right,
            rectangle.Bottom + radius - offset,
            rectangle.Right,
            rectangle.Bottom + radius);
        builder.LineTo(rectangle.Right, rectangle.Top - radius);
        builder.CurveTo(
            rectangle.Right,
            rectangle.Top - radius + offset,
            rectangle.Right - radius + offset,
            rectangle.Top,
            rectangle.Right - radius,
            rectangle.Top);
        builder.LineTo(rectangle.Left + radius, rectangle.Top);
        builder.CurveTo(
            rectangle.Left + radius - offset,
            rectangle.Top,
            rectangle.Left,
            rectangle.Top - radius + offset,
            rectangle.Left,
            rectangle.Top - radius);
        builder.LineTo(rectangle.Left, rectangle.Bottom + radius);
        builder.CurveTo(
            rectangle.Left,
            rectangle.Bottom + radius - offset,
            rectangle.Left + radius - offset,
            rectangle.Bottom,
            rectangle.Left + radius,
            rectangle.Bottom);
        builder.Close();
        return builder.Snapshot();
    }

    private static PdfGraphicsPath PolylinePath(
        IEnumerable<PdfPoint> points,
        bool close)
    {
        PdfPoint[] source = points.ToArray();
        var builder = new PdfPathBuilder();
        if (source.Length == 0)
            return builder.Snapshot();
        builder.MoveTo(source[0].X, source[0].Y);
        foreach (PdfPoint point in source.Skip(1))
            builder.LineTo(point.X, point.Y);
        if (close)
            builder.Close();
        return builder.Snapshot();
    }

    private static PdfGraphicsPath LinePath(PdfPoint start, PdfPoint end) =>
        PolylinePath(new[] { start, end }, close: false);

    private static PdfGraphicsPath SquigglyPath(
        double left,
        double right,
        double y,
        double amplitude)
    {
        var builder = new PdfPathBuilder();
        builder.MoveTo(left, y);
        double step = Math.Max(2, amplitude * 2);
        bool high = true;
        for (double x = left + step / 2; x < right; x += step / 2)
        {
            builder.LineTo(x, y + (high ? amplitude : -amplitude));
            high = !high;
        }
        builder.LineTo(right, y);
        return builder.Snapshot();
    }

    private static IEnumerable<string> WrapAnnotationText(
        string text,
        int maximumCharacters)
    {
        string remaining = text.Trim();
        while (remaining.Length > maximumCharacters)
        {
            int split = remaining.LastIndexOf(' ', maximumCharacters);
            if (split <= 0)
                split = maximumCharacters;
            yield return remaining[..split];
            remaining = remaining[split..].TrimStart();
        }
        if (remaining.Length > 0)
            yield return remaining;
    }

    private static IReadOnlyDictionary<char, byte[]> CreateAnnotationGlyphs()
    {
        return new Dictionary<char, byte[]>
        {
            [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
            ['A'] = new byte[] { 14, 17, 17, 31, 17, 17, 17 },
            ['B'] = new byte[] { 30, 17, 17, 30, 17, 17, 30 },
            ['C'] = new byte[] { 14, 17, 16, 16, 16, 17, 14 },
            ['D'] = new byte[] { 30, 17, 17, 17, 17, 17, 30 },
            ['E'] = new byte[] { 31, 16, 16, 30, 16, 16, 31 },
            ['F'] = new byte[] { 31, 16, 16, 30, 16, 16, 16 },
            ['G'] = new byte[] { 14, 17, 16, 23, 17, 17, 14 },
            ['H'] = new byte[] { 17, 17, 17, 31, 17, 17, 17 },
            ['I'] = new byte[] { 14, 4, 4, 4, 4, 4, 14 },
            ['J'] = new byte[] { 7, 2, 2, 2, 18, 18, 12 },
            ['K'] = new byte[] { 17, 18, 20, 24, 20, 18, 17 },
            ['L'] = new byte[] { 16, 16, 16, 16, 16, 16, 31 },
            ['M'] = new byte[] { 17, 27, 21, 21, 17, 17, 17 },
            ['N'] = new byte[] { 17, 25, 21, 19, 17, 17, 17 },
            ['O'] = new byte[] { 14, 17, 17, 17, 17, 17, 14 },
            ['P'] = new byte[] { 30, 17, 17, 30, 16, 16, 16 },
            ['Q'] = new byte[] { 14, 17, 17, 17, 21, 18, 13 },
            ['R'] = new byte[] { 30, 17, 17, 30, 20, 18, 17 },
            ['S'] = new byte[] { 15, 16, 16, 14, 1, 1, 30 },
            ['T'] = new byte[] { 31, 4, 4, 4, 4, 4, 4 },
            ['U'] = new byte[] { 17, 17, 17, 17, 17, 17, 14 },
            ['V'] = new byte[] { 17, 17, 17, 17, 17, 10, 4 },
            ['W'] = new byte[] { 17, 17, 17, 21, 21, 21, 10 },
            ['X'] = new byte[] { 17, 17, 10, 4, 10, 17, 17 },
            ['Y'] = new byte[] { 17, 17, 10, 4, 4, 4, 4 },
            ['Z'] = new byte[] { 31, 1, 2, 4, 8, 16, 31 },
            ['0'] = new byte[] { 14, 17, 19, 21, 25, 17, 14 },
            ['1'] = new byte[] { 4, 12, 4, 4, 4, 4, 14 },
            ['2'] = new byte[] { 14, 17, 1, 2, 4, 8, 31 },
            ['3'] = new byte[] { 30, 1, 1, 14, 1, 1, 30 },
            ['4'] = new byte[] { 2, 6, 10, 18, 31, 2, 2 },
            ['5'] = new byte[] { 31, 16, 16, 30, 1, 1, 30 },
            ['6'] = new byte[] { 14, 16, 16, 30, 17, 17, 14 },
            ['7'] = new byte[] { 31, 1, 2, 4, 8, 8, 8 },
            ['8'] = new byte[] { 14, 17, 17, 14, 17, 17, 14 },
            ['9'] = new byte[] { 14, 17, 17, 15, 1, 1, 14 },
            ['.'] = new byte[] { 0, 0, 0, 0, 0, 12, 12 },
            [','] = new byte[] { 0, 0, 0, 0, 0, 12, 8 },
            [':'] = new byte[] { 0, 12, 12, 0, 12, 12, 0 },
            [';'] = new byte[] { 0, 12, 12, 0, 12, 8, 0 },
            ['-'] = new byte[] { 0, 0, 0, 31, 0, 0, 0 },
            ['_'] = new byte[] { 0, 0, 0, 0, 0, 0, 31 },
            ['/'] = new byte[] { 1, 2, 2, 4, 8, 8, 16 },
            ['('] = new byte[] { 2, 4, 8, 8, 8, 4, 2 },
            [')'] = new byte[] { 8, 4, 2, 2, 2, 4, 8 },
            ['!'] = new byte[] { 4, 4, 4, 4, 4, 0, 4 },
            ['*'] = new byte[] { 0, 21, 14, 31, 14, 21, 0 },
            ['?'] = new byte[] { 14, 17, 1, 2, 4, 0, 4 }
        };
    }

    private void PaintShading(
        PdfDictionary? resources,
        string resourceName,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        string? sourceResource)
    {
        PdfObject? shading = LookupResource(resources, "Shading", resourceName);
        if (!PdfShadingReader.TryReadBrush(
                shading,
                _document,
                PdfMatrix.Identity,
                out PdfBrush? brush) ||
            brush is null)
        {
            ReportOnce(
                "graphics.shading.unsupported",
                "An unsupported shading or color space was skipped.");
            return;
        }

        if (brush is PdfGradientBrush gradient)
        {
            Emit(
                output,
                new PdfShadingElement(
                    resourceName,
                    gradient,
                    context.Graphics,
                    context.Clips.ToArray(),
                    sourceResource));
        }
        else if (brush is PdfMeshShadingBrush mesh)
        {
            Emit(
                output,
                new PdfMeshShadingElement(
                    resourceName,
                    mesh,
                    context.Graphics,
                    context.Clips.ToArray(),
                    sourceResource));
        }
    }

    private void SetGenericColor(
        IReadOnlyList<PdfObject> values,
        GraphicsContext context,
        PdfDictionary? resources,
        bool stroke,
        bool pattern,
        int depth)
    {
        PdfColorSpaceDefinition? colorSpace =
            stroke ? context.StrokeColorSpace : context.FillColorSpace;
        PdfBrush? brush = null;
        if (pattern && values.LastOrDefault() is PdfName patternName)
        {
            PdfBrush? underlying = ReadSolidColor(values, colorSpace);
            brush = ReadPattern(
                resources,
                patternName.Value,
                depth,
                underlying is PdfSolidBrush solid ? solid.Color : null);
        }

        brush ??= ReadSolidColor(values, colorSpace);
        if (brush is null)
            return;
        context.Graphics = stroke
            ? context.Graphics with { Stroke = brush }
            : context.Graphics with { Fill = brush };
    }

    private PdfBrush? ReadPattern(
        PdfDictionary? resources,
        string resourceName,
        int depth,
        PdfColor? underlyingColor = null)
    {
        PdfObject? patternObject = LookupResource(resources, "Pattern", resourceName);
        if (patternObject is null)
            return null;
        PdfObject resolved = patternObject.Resolve(_document);
        PdfReference? reference = patternObject as PdfReference;
        if (reference is not null && _patternCache.TryGetValue(reference, out PdfBrush? cached))
        {
            return cached is PdfTilingPatternBrush { IsColored: false } uncolored &&
                   underlyingColor.HasValue
                ? uncolored.WithUnderlyingColor(underlyingColor.Value)
                : cached;
        }
        PdfDictionary? dictionary = resolved switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary direct => direct,
            _ => null
        };
        if (dictionary is null)
            return null;

        int patternType = dictionary.GetValueOrNull("PatternType").AsInteger(_document) ?? 0;
        PdfMatrix matrix = PdfShadingReader.ReadMatrix(
            dictionary.GetValueOrNull("Matrix"),
            _document,
            PdfMatrix.Identity);
        PdfBrush? result = null;
        if (patternType == 2 &&
            PdfShadingReader.TryReadBrush(
                dictionary.GetValueOrNull("Shading"),
                _document,
                matrix,
                out PdfBrush? shading))
        {
            result = shading;
        }
        else if (patternType == 1 && resolved is PdfStream patternStream)
        {
            int paintType = dictionary.GetValueOrNull("PaintType").AsInteger(_document) ?? 1;
            if (paintType is not (1 or 2))
                return null;
            if (paintType == 2 && !underlyingColor.HasValue)
            {
                ReportOnce(
                    "graphics.pattern.uncolored",
                    "An uncolored tiling pattern did not provide its underlying color.");
                return null;
            }

            PdfRectangle? boundingBox = dictionary
                .GetValueOrNull("BBox")
                .AsRectangle(_document);
            double? xStep = dictionary.GetValueOrNull("XStep").AsNumber(_document);
            double? yStep = dictionary.GetValueOrNull("YStep").AsNumber(_document);
            if (boundingBox is null ||
                !xStep.HasValue ||
                !yStep.HasValue ||
                !double.IsFinite(xStep.Value) ||
                !double.IsFinite(yStep.Value) ||
                xStep.Value == 0 ||
                yStep.Value == 0)
            {
                return null;
            }

            if (reference is not null && !_activePatterns.Add(reference))
            {
                ReportOnce(
                    "graphics.pattern.recursive",
                    $"Recursive tiling pattern /{resourceName} was skipped.");
                return null;
            }

            try
            {
                var patternElements = new List<PdfGraphicsElement>();
                PdfObject? patternResources =
                    dictionary.GetValueOrNull("Resources") ?? resources;
                Execute(
                    _document.Decode(patternStream),
                    patternResources,
                    GraphicsContext.Create(),
                    patternElements,
                    depth + 1,
                    $"Pattern:{resourceName}");
                var tiling = new PdfTilingPatternBrush(
                    resourceName,
                    boundingBox.Value,
                    xStep.Value,
                    yStep.Value,
                    matrix,
                    patternElements,
                    isColored: paintType == 1);
                result = paintType == 2
                    ? tiling.WithUnderlyingColor(underlyingColor!.Value)
                    : tiling;
            }
            finally
            {
                if (reference is not null)
                    _activePatterns.Remove(reference);
            }
        }

        if (result is not null && reference is not null)
        {
            _patternCache[reference] =
                result is PdfTilingPatternBrush { IsColored: false } uncolored
                    ? new PdfTilingPatternBrush(
                        uncolored.ResourceName,
                        uncolored.BoundingBox,
                        uncolored.XStep,
                        uncolored.YStep,
                        uncolored.Matrix,
                        uncolored.Elements,
                        isColored: false)
                    : result;
        }
        return result;
    }

    private static PdfBrush? ReadSolidColor(
        IReadOnlyList<PdfObject> values,
        PdfColorSpaceDefinition? colorSpace)
    {
        if (colorSpace is null)
            return null;
        double[] numbers = values
            .OfType<PdfNumber>()
            .Select(number => number.Value)
            .Where(double.IsFinite)
            .ToArray();
        if (numbers.Length < colorSpace.Components)
            return null;
        double[] components = numbers[^colorSpace.Components..];
        return new PdfSolidBrush(colorSpace.Convert(components));
    }

    private void ApplyExtendedState(
        PdfDictionary? resources,
        string resourceName,
        GraphicsContext context,
        int depth)
    {
        PdfDictionary? dictionary = LookupResource(
                resources,
                "ExtGState",
                resourceName)
            .AsDictionary(_document);
        if (dictionary is null)
            return;

        PdfGraphicsState state = context.Graphics;
        if (dictionary.GetValueOrNull("LW").AsNumber(_document) is { } lineWidth)
            state = state with { LineWidth = Math.Max(0, lineWidth) };
        if (dictionary.GetValueOrNull("LC").AsInteger(_document) is { } lineCap)
        {
            state = state with
            {
                LineCap = lineCap switch
                {
                    1 => PdfLineCap.Round,
                    2 => PdfLineCap.Square,
                    _ => PdfLineCap.Butt
                }
            };
        }

        if (dictionary.GetValueOrNull("LJ").AsInteger(_document) is { } lineJoin)
        {
            state = state with
            {
                LineJoin = lineJoin switch
                {
                    1 => PdfLineJoin.Round,
                    2 => PdfLineJoin.Bevel,
                    _ => PdfLineJoin.Miter
                }
            };
        }

        if (dictionary.GetValueOrNull("ML").AsNumber(_document) is { } miter)
            state = state with { MiterLimit = Math.Max(1, miter) };
        if (dictionary.GetValueOrNull("CA").AsNumber(_document) is { } strokeAlpha)
            state = state with { StrokeAlpha = ClampUnit(strokeAlpha) };
        if (dictionary.GetValueOrNull("ca").AsNumber(_document) is { } fillAlpha)
            state = state with { FillAlpha = ClampUnit(fillAlpha) };
        if (dictionary.GetValueOrNull("OP")?.Resolve(_document)
            is PdfBoolean { Value: var strokeOverprint })
        {
            state = state with { StrokeOverprint = strokeOverprint };
        }
        if (dictionary.GetValueOrNull("op")?.Resolve(_document)
            is PdfBoolean { Value: var fillOverprint })
        {
            state = state with { FillOverprint = fillOverprint };
        }
        else if (dictionary.GetValueOrNull("OP")?.Resolve(_document)
                 is PdfBoolean { Value: var sharedOverprint })
        {
            state = state with { FillOverprint = sharedOverprint };
        }
        if (dictionary.GetValueOrNull("OPM").AsInteger(_document) is { } overprintMode)
            state = state with { OverprintMode = overprintMode == 1 ? 1 : 0 };
        string? blendMode = ReadBlendMode(dictionary.GetValueOrNull("BM"));
        if (blendMode is not null)
            state = state with { BlendMode = blendMode };
        PdfObject? softMaskObject = dictionary.GetValueOrNull("SMask");
        if (softMaskObject is not null)
        {
            state = state with
            {
                SoftMask = softMaskObject.AsName(_document) == "None"
                    ? null
                    : ReadSoftMask(softMaskObject, resources, context, depth)
            };
        }
        if (dictionary.GetValueOrNull("D").AsArray(_document) is { Count: >= 2 } dash)
            state = state with { Dash = ReadDash(dash[0], dash[1]) };
        context.Graphics = state;
    }

    private PdfSoftMask? ReadSoftMask(
        PdfObject value,
        PdfDictionary? resources,
        GraphicsContext context,
        int depth)
    {
        if (depth >= _document.Options.MaximumTransparencyGroupDepth)
            throw new PdfLimitException("Soft-mask nesting exceeds the configured limit.");
        PdfDictionary? dictionary = value.AsDictionary(_document);
        PdfObject? groupObject = dictionary?.GetValueOrNull("G");
        PdfStream? stream = groupObject.AsStream(_document);
        if (dictionary is null || stream is null)
            return null;

        PdfReference? reference = groupObject as PdfReference ?? stream.SourceReference;
        if (reference is not null && !_activeSoftMasks.Add(reference))
        {
            ReportOnce(
                "graphics.soft-mask.recursive",
                "A recursive soft mask was skipped.");
            return null;
        }

        try
        {
            GraphicsContext child = context.Clone();
            child.Clips.Clear();
            PdfMatrix matrix = PdfShadingReader.ReadMatrix(
                stream.Dictionary.GetValueOrNull("Matrix"),
                _document,
                PdfMatrix.Identity);
            child.Graphics = child.Graphics with
            {
                Transform = matrix.Multiply(child.Graphics.Transform),
                FillAlpha = 1,
                StrokeAlpha = 1,
                BlendMode = "Normal",
                SoftMask = null
            };
            if (stream.Dictionary.GetValueOrNull("BBox").AsRectangle(_document) is { } box)
            {
                child.Clips.Add(new PdfClipPath(
                    RectanglePath(box),
                    child.Graphics.Transform,
                    PdfFillRule.NonZero));
            }

            var elements = new List<PdfGraphicsElement>();
            PdfObject? childResources =
                stream.Dictionary.GetValueOrNull("Resources") ?? resources;
            PdfDictionary? group = stream.Dictionary
                .GetValueOrNull("Group")
                .AsDictionary(_document);
            Execute(
                _document.Decode(stream),
                childResources,
                child,
                elements,
                depth + 1,
                sourceResource: "SoftMask");
            PdfSoftMaskMode mode =
                dictionary.GetValueOrNull("S").AsName(_document) == "Luminosity"
                    ? PdfSoftMaskMode.Luminosity
                    : PdfSoftMaskMode.Alpha;
            PdfColorSpaceDefinition? blendingColorSpace =
                PdfColorSpaceDefinition.Parse(
                    group?.GetValueOrNull("CS"),
                    childResources.AsDictionary(_document),
                    _document);
            PdfColor backdrop = ReadBackdrop(
                dictionary.GetValueOrNull("BC"),
                blendingColorSpace);
            bool isolated =
                group?.GetValueOrNull("I")?.Resolve(_document)
                    is PdfBoolean { Value: true };
            bool knockout =
                group?.GetValueOrNull("K")?.Resolve(_document)
                    is PdfBoolean { Value: true };
            PdfObject? transferObject = dictionary.GetValueOrNull("TR");
            string? transferName = transferObject.AsName(_document);
            PdfFunction? transferFunction =
                transferObject is null || transferName == "Identity"
                    ? null
                    : PdfFunction.Create(
                        transferObject,
                        _document,
                        expectedInputCount: 1,
                        expectedOutputCount: 1);
            if (transferObject is not null &&
                transferName != "Identity" &&
                transferFunction is null)
            {
                ReportOnce(
                    "graphics.soft-mask.transfer.unsupported",
                    "An unsupported soft-mask transfer function was ignored.");
            }
            return new PdfSoftMask(
                mode,
                elements,
                backdrop,
                transferFunction,
                isolated,
                knockout);
        }
        finally
        {
            if (reference is not null)
                _activeSoftMasks.Remove(reference);
        }
    }

    private PdfColor ReadBackdrop(
        PdfObject? value,
        PdfColorSpaceDefinition? colorSpace = null)
    {
        PdfArray? array = value.AsArray(_document);
        if (array is null)
            return PdfColor.Black;
        double[] components = array
            .Select(item => item.AsNumber(_document))
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToArray();
        if (colorSpace is not null && components.Length >= colorSpace.Components)
            return colorSpace.Convert(components);
        return components.Length switch
        {
            1 => PdfColor.Gray(components[0]),
            3 => PdfColor.Rgb(components[0], components[1], components[2]),
            >= 4 => PdfColor.Cmyk(
                components[0],
                components[1],
                components[2],
                components[3]),
            _ => PdfColor.Black
        };
    }

    private void SetDash(IReadOnlyList<PdfObject> values, GraphicsContext context)
    {
        if (values.Count < 2)
            return;
        context.Graphics = context.Graphics with
        {
            Dash = ReadDash(values[^2], values[^1])
        };
    }

    private PdfDashPattern ReadDash(PdfObject arrayObject, PdfObject phaseObject)
    {
        PdfArray? array = arrayObject.AsArray(_document);
        double phase = phaseObject.AsNumber(_document) ?? 0;
        if (array is null)
            return PdfDashPattern.Solid;
        var segments = new List<double>(array.Count);
        foreach (PdfObject item in array)
        {
            double? value = item.AsNumber(_document);
            if (value.HasValue && double.IsFinite(value.Value))
                segments.Add(Math.Max(0, value.Value));
        }

        return segments.Count == 0 || segments.All(value => value == 0)
            ? PdfDashPattern.Solid
            : new PdfDashPattern(segments, phase);
    }

    private string? ReadBlendMode(PdfObject? value)
    {
        PdfObject? resolved = value?.Resolve(_document);
        return resolved switch
        {
            PdfName name => name.Value,
            PdfArray { Count: > 0 } array => array[0].AsName(_document),
            _ => null
        };
    }

    private PdfColorSpaceDefinition? ResolveColorSpace(
        PdfDictionary? resources,
        string name)
    {
        PdfObject value = name is "DeviceGray" or "G" or "DeviceRGB" or "RGB" or
            "DeviceCMYK" or "CMYK" or "Pattern"
            ? new PdfName(name)
            : LookupResource(resources, "ColorSpace", name) ?? new PdfName(name);
        return PdfColorSpaceDefinition.Parse(value, resources, _document);
    }

    private string DescribeColorSpace(PdfObject? value, PdfDictionary? resources)
    {
        PdfColorSpaceDefinition? definition =
            PdfColorSpaceDefinition.Parse(value, resources, _document);
        if (definition is not null)
            return definition.Name;
        PdfObject? resolved = value?.Resolve(_document);
        return resolved is PdfName name ? name.Value : "Unknown";
    }

    private PdfObject? LookupResource(
        PdfDictionary? resources,
        string category,
        string name)
    {
        PdfDictionary? categoryDictionary = resources?
            .GetValueOrNull(category)
            .AsDictionary(_document);
        return categoryDictionary?.GetValueOrNull(name);
    }

    private byte[] ReadContent(PdfObject? contents)
    {
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

    private void CountOperation()
    {
        _operationCount++;
        if (_operationCount > _document.Options.MaximumGraphicsOperations)
        {
            throw new PdfLimitException(
                "Graphics operation count exceeds the configured limit.");
        }
    }

    private void Emit(
        List<PdfGraphicsElement> output,
        PdfGraphicsElement element)
    {
        _elementCount++;
        if (_elementCount > _document.Options.MaximumGraphicsElements)
        {
            throw new PdfLimitException(
                "Graphics display-list size exceeds the configured limit.");
        }

        output.Add(element);
    }

    private void EnsurePathLimit(PdfPathBuilder path)
    {
        if (path.Count > _document.Options.MaximumPathSegments)
            throw new PdfLimitException("Path segment count exceeds the configured limit.");
    }

    private void ReportOnce(string code, string message)
    {
        if (_reportedDiagnostics.Add(code))
            _document.AddDiagnostic(PdfDiagnosticSeverity.Warning, code, message);
    }

    private static PdfGraphicsPath RectanglePath(PdfRectangle rectangle)
    {
        var builder = new PdfPathBuilder();
        builder.Rectangle(
            rectangle.Left,
            rectangle.Bottom,
            rectangle.Right - rectangle.Left,
            rectangle.Top - rectangle.Bottom);
        return builder.Snapshot();
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
            if (values[start + index] is not PdfNumber number ||
                !double.IsFinite(number.Value))
            {
                numbers = Array.Empty<double>();
                return false;
            }

            numbers[index] = number.Value;
        }

        return true;
    }

    private static double? LastNumber(IReadOnlyList<PdfObject> values) =>
        values.LastOrDefault() is PdfNumber number && double.IsFinite(number.Value)
            ? number.Value
            : null;

    private static bool TryLastPair(
        IReadOnlyList<PdfObject> values,
        out double first,
        out double second)
    {
        first = 0;
        second = 0;
        if (values.Count < 2 ||
            values[^2] is not PdfNumber firstNumber ||
            values[^1] is not PdfNumber secondNumber ||
            !double.IsFinite(firstNumber.Value) ||
            !double.IsFinite(secondNumber.Value))
        {
            return false;
        }

        first = firstNumber.Value;
        second = secondNumber.Value;
        return true;
    }

    private static int? LastInteger(IReadOnlyList<PdfObject> values) =>
        values.LastOrDefault() is PdfNumber { IsInteger: true } number &&
        number.Value is >= int.MinValue and <= int.MaxValue
            ? (int)number.Value
            : null;

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;

    private sealed class GraphicsContext
    {
        public PdfGraphicsState Graphics { get; set; } = new();
        public PdfColorSpaceDefinition? FillColorSpace { get; set; } =
            PdfColorSpaceDefinition.DeviceGray;
        public PdfColorSpaceDefinition? StrokeColorSpace { get; set; } =
            PdfColorSpaceDefinition.DeviceGray;
        public List<PdfClipPath> Clips { get; } = new();
        public TextContext Text { get; set; } = new();
        public bool InType3Glyph { get; set; }

        public static GraphicsContext Create() => new();

        public GraphicsContext Clone()
        {
            var clone = new GraphicsContext
            {
                Graphics = Graphics,
                FillColorSpace = FillColorSpace,
                StrokeColorSpace = StrokeColorSpace,
                Text = Text.Clone(),
                InType3Glyph = InType3Glyph
            };
            clone.Clips.AddRange(Clips);
            return clone;
        }
    }

    private sealed class TextContext
    {
        public bool InTextObject { get; set; }
        public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;
        public PdfMatrix LineMatrix { get; set; } = PdfMatrix.Identity;
        public PdfFontDecoder? Font { get; set; }
        public string FontResourceName { get; set; } = "Fallback";
        public double FontSize { get; set; } = 12;
        public double CharacterSpacing { get; set; }
        public double WordSpacing { get; set; }
        public double HorizontalScale { get; set; } = 1;
        public double Leading { get; set; }
        public double Rise { get; set; }
        public PdfTextRenderingMode RenderingMode { get; set; }
        public List<PdfClipPath> PendingClips { get; } = new();

        public TextContext Clone()
        {
            var clone = new TextContext
            {
                InTextObject = InTextObject,
                TextMatrix = TextMatrix,
                LineMatrix = LineMatrix,
                Font = Font,
                FontResourceName = FontResourceName,
                FontSize = FontSize,
                CharacterSpacing = CharacterSpacing,
                WordSpacing = WordSpacing,
                HorizontalScale = HorizontalScale,
                Leading = Leading,
                Rise = Rise,
                RenderingMode = RenderingMode
            };
            clone.PendingClips.AddRange(PendingClips);
            return clone;
        }
    }
}
