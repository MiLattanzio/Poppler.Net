using Poppler.Core;
using Poppler.Color;
using Poppler.DocumentModel;
using Poppler.Images;
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
    private readonly Dictionary<PdfReference, PdfBrush> _patternCache = new();
    private readonly HashSet<PdfReference> _activePatterns = new();
    private readonly HashSet<PdfReference> _activeForms = new();
    private readonly HashSet<PdfReference> _activeSoftMasks = new();
    private readonly HashSet<PdfReference> _activeType3Glyphs = new();
    private readonly HashSet<string> _reportedDiagnostics = new(StringComparer.Ordinal);
    private int _operationCount;
    private int _elementCount;
    private int _inlineImageCount;

    public PdfGraphicsInterpreter(PdfDocumentCore document, PdfPageNode page)
    {
        _document = document;
        _page = page;
    }

    public IReadOnlyList<PdfGraphicsElement> Interpret()
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
        if (depth > _document.Options.MaximumXObjectDepth)
            throw new PdfLimitException("Form or pattern nesting exceeds the configured limit.");

        PdfDictionary? resources = resourcesObject.AsDictionary(_document);
        IReadOnlyDictionary<string, PdfFontDecoder> fonts =
            PdfFontCollection.Read(_document, resourcesObject);
        PdfFontDecoder fallbackFont = PdfFontDecoder.CreateFallback(_document);
        context.Text.Font ??= fallbackFont;
        var stack = new Stack<GraphicsContext>();
        var path = new PdfPathBuilder();
        PdfFillRule? pendingClip = null;

        foreach (PdfContentOperation operation in PdfContentReader.Read(content, _document.Options))
        {
            CountOperation();
            IReadOnlyList<PdfObject> values = operation.Operands;
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

    private void PaintShading(
        PdfDictionary? resources,
        string resourceName,
        GraphicsContext context,
        List<PdfGraphicsElement> output,
        string? sourceResource)
    {
        PdfObject? shading = LookupResource(resources, "Shading", resourceName);
        if (!PdfShadingReader.TryRead(
                shading,
                _document,
                PdfMatrix.Identity,
                out PdfGradientBrush? brush) ||
            brush is null)
        {
            ReportOnce(
                "graphics.shading.unsupported",
                "A shading other than axial/radial or an unsupported color space was skipped.");
            return;
        }

        Emit(
            output,
            new PdfShadingElement(
                resourceName,
                brush,
                context.Graphics,
                context.Clips.ToArray(),
                sourceResource));
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
            brush = ReadPattern(resources, patternName.Value, depth);
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
        int depth)
    {
        PdfObject? patternObject = LookupResource(resources, "Pattern", resourceName);
        if (patternObject is null)
            return null;
        PdfObject resolved = patternObject.Resolve(_document);
        PdfReference? reference = patternObject as PdfReference;
        if (reference is not null && _patternCache.TryGetValue(reference, out PdfBrush? cached))
            return cached;
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
            PdfShadingReader.TryRead(
                dictionary.GetValueOrNull("Shading"),
                _document,
                matrix,
                out PdfGradientBrush? shading))
        {
            result = shading;
        }
        else if (patternType == 1 && resolved is PdfStream patternStream)
        {
            int paintType = dictionary.GetValueOrNull("PaintType").AsInteger(_document) ?? 1;
            if (paintType != 1)
            {
                ReportOnce(
                    "graphics.pattern.uncolored",
                    "Uncolored tiling patterns are detected but not yet painted.");
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
                result = new PdfTilingPatternBrush(
                    resourceName,
                    boundingBox.Value,
                    xStep.Value,
                    yStep.Value,
                    matrix,
                    patternElements);
            }
            finally
            {
                if (reference is not null)
                    _activePatterns.Remove(reference);
            }
        }

        if (result is not null && reference is not null)
            _patternCache[reference] = result;
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
            PdfColor backdrop = ReadBackdrop(dictionary.GetValueOrNull("BC"));
            return new PdfSoftMask(mode, elements, backdrop);
        }
        finally
        {
            if (reference is not null)
                _activeSoftMasks.Remove(reference);
        }
    }

    private PdfColor ReadBackdrop(PdfObject? value)
    {
        PdfArray? array = value.AsArray(_document);
        if (array is null)
            return PdfColor.Black;
        double[] components = array
            .Select(item => item.AsNumber(_document))
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToArray();
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
