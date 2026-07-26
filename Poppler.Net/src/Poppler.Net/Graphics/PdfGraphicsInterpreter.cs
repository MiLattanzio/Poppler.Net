using Poppler.Core;
using Poppler.DocumentModel;
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
    private readonly HashSet<string> _reportedDiagnostics = new(StringComparer.Ordinal);
    private int _operationCount;
    private int _elementCount;

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
                    ApplyExtendedState(resources, stateName.Value, context);
                    break;
                case "CS" when values.LastOrDefault() is PdfName strokeSpace:
                    context.StrokeColorSpace = ResolveColorSpaceName(
                        resources,
                        strokeSpace.Value);
                    break;
                case "cs" when values.LastOrDefault() is PdfName fillSpace:
                    context.FillColorSpace = ResolveColorSpaceName(
                        resources,
                        fillSpace.Value);
                    break;
                case "G" when LastNumber(values) is { } strokeGray:
                    context.StrokeColorSpace = "DeviceGray";
                    context.Graphics = context.Graphics with
                    {
                        Stroke = new PdfSolidBrush(PdfColor.Gray(strokeGray))
                    };
                    break;
                case "g" when LastNumber(values) is { } fillGray:
                    context.FillColorSpace = "DeviceGray";
                    context.Graphics = context.Graphics with
                    {
                        Fill = new PdfSolidBrush(PdfColor.Gray(fillGray))
                    };
                    break;
                case "RG" when TryNumbers(values, 3, out double[] strokeRgb):
                    context.StrokeColorSpace = "DeviceRGB";
                    context.Graphics = context.Graphics with
                    {
                        Stroke = new PdfSolidBrush(PdfColor.Rgb(
                            strokeRgb[0],
                            strokeRgb[1],
                            strokeRgb[2]))
                    };
                    break;
                case "rg" when TryNumbers(values, 3, out double[] fillRgb):
                    context.FillColorSpace = "DeviceRGB";
                    context.Graphics = context.Graphics with
                    {
                        Fill = new PdfSolidBrush(PdfColor.Rgb(
                            fillRgb[0],
                            fillRgb[1],
                            fillRgb[2]))
                    };
                    break;
                case "K" when TryNumbers(values, 4, out double[] strokeCmyk):
                    context.StrokeColorSpace = "DeviceCMYK";
                    context.Graphics = context.Graphics with
                    {
                        Stroke = new PdfSolidBrush(PdfColor.Cmyk(
                            strokeCmyk[0],
                            strokeCmyk[1],
                            strokeCmyk[2],
                            strokeCmyk[3]))
                    };
                    break;
                case "k" when TryNumbers(values, 4, out double[] fillCmyk):
                    context.FillColorSpace = "DeviceCMYK";
                    context.Graphics = context.Graphics with
                    {
                        Fill = new PdfSolidBrush(PdfColor.Cmyk(
                            fillCmyk[0],
                            fillCmyk[1],
                            fillCmyk[2],
                            fillCmyk[3]))
                    };
                    break;
                case "SC":
                    SetGenericColor(values, context, resources, stroke: true, pattern: false, depth);
                    break;
                case "sc":
                    SetGenericColor(values, context, resources, stroke: false, pattern: false, depth);
                    break;
                case "SCN":
                    SetGenericColor(values, context, resources, stroke: true, pattern: true, depth);
                    break;
                case "scn":
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
            }
        }
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
                stream.Dictionary.GetValueOrNull("ColorSpace"));
            bool imageMask =
                stream.Dictionary.GetValueOrNull("ImageMask")?.Resolve(_document)
                    is PdfBoolean { Value: true };
            Emit(
                output,
                new PdfImageElement(
                    resourceName,
                    Math.Max(0, width),
                    Math.Max(0, height),
                    Math.Max(0, bits),
                    colorSpace,
                    imageMask,
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
            Execute(
                _document.Decode(stream),
                childResources,
                child,
                output,
                depth + 1,
                source);
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
        string colorSpace = stroke ? context.StrokeColorSpace : context.FillColorSpace;
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
                    "Uncolored tiling patterns are detected but not painted in 0.5.");
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
        string colorSpace)
    {
        double[] numbers = values
            .OfType<PdfNumber>()
            .Select(number => number.Value)
            .Where(double.IsFinite)
            .ToArray();
        return colorSpace switch
        {
            "DeviceGray" when numbers.Length >= 1 =>
                new PdfSolidBrush(PdfColor.Gray(numbers[^1])),
            "DeviceRGB" when numbers.Length >= 3 =>
                new PdfSolidBrush(PdfColor.Rgb(
                    numbers[^3],
                    numbers[^2],
                    numbers[^1])),
            "DeviceCMYK" when numbers.Length >= 4 =>
                new PdfSolidBrush(PdfColor.Cmyk(
                    numbers[^4],
                    numbers[^3],
                    numbers[^2],
                    numbers[^1])),
            _ => null
        };
    }

    private void ApplyExtendedState(
        PdfDictionary? resources,
        string resourceName,
        GraphicsContext context)
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
        if (dictionary.GetValueOrNull("D").AsArray(_document) is { Count: >= 2 } dash)
            state = state with { Dash = ReadDash(dash[0], dash[1]) };
        context.Graphics = state;
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

    private string ResolveColorSpaceName(PdfDictionary? resources, string name)
    {
        if (name is "DeviceGray" or "G" or "DeviceRGB" or "RGB" or
            "DeviceCMYK" or "CMYK" or "Pattern")
        {
            return NormalizeColorSpaceName(name);
        }

        PdfObject? value = LookupResource(resources, "ColorSpace", name);
        PdfObject? resolved = value?.Resolve(_document);
        string? resolvedName = resolved switch
        {
            PdfName direct => direct.Value,
            PdfArray { Count: > 0 } array => array[0].AsName(_document),
            _ => null
        };
        return NormalizeColorSpaceName(resolvedName ?? name);
    }

    private static string NormalizeColorSpaceName(string name) => name switch
    {
        "G" => "DeviceGray",
        "RGB" => "DeviceRGB",
        "CMYK" => "DeviceCMYK",
        _ => name
    };

    private string DescribeColorSpace(PdfObject? value)
    {
        PdfObject? resolved = value?.Resolve(_document);
        return resolved switch
        {
            PdfName name => name.Value,
            PdfArray { Count: > 0 } array =>
                array[0].AsName(_document) ?? "Unknown",
            _ => "Unknown"
        };
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
        public string FillColorSpace { get; set; } = "DeviceGray";
        public string StrokeColorSpace { get; set; } = "DeviceGray";
        public List<PdfClipPath> Clips { get; } = new();

        public static GraphicsContext Create() => new();

        public GraphicsContext Clone()
        {
            var clone = new GraphicsContext
            {
                Graphics = Graphics,
                FillColorSpace = FillColorSpace,
                StrokeColorSpace = StrokeColorSpace
            };
            clone.Clips.AddRange(Clips);
            return clone;
        }
    }
}
