using Poppler.Core;
using Poppler.Color;

namespace Poppler.Graphics;

internal static class PdfShadingReader
{
    public static bool TryReadBrush(
        PdfObject? value,
        PdfDocumentCore document,
        PdfMatrix matrix,
        out PdfBrush? brush)
    {
        if (TryReadFunction(value, document, matrix, out PdfFunctionShadingBrush? function))
        {
            brush = function;
            return true;
        }
        if (TryRead(value, document, matrix, out PdfGradientBrush? gradient))
        {
            brush = gradient;
            return true;
        }
        if (PdfMeshShadingReader.TryRead(
                value,
                document,
                matrix,
                out PdfMeshShadingBrush? mesh))
        {
            brush = mesh;
            return true;
        }

        brush = null;
        return false;
    }

    private static bool TryReadFunction(
        PdfObject? value,
        PdfDocumentCore document,
        PdfMatrix matrix,
        out PdfFunctionShadingBrush? brush)
    {
        brush = null;
        PdfDictionary? dictionary = value.AsDictionary(document);
        if (dictionary is null ||
            dictionary.GetValueOrNull("ShadingType").AsInteger(document) != 1)
        {
            return false;
        }

        PdfColorSpaceDefinition? colorSpace = PdfColorSpaceDefinition.Parse(
            dictionary.GetValueOrNull("ColorSpace"),
            resources: null,
            document);
        if (colorSpace is null)
            return false;

        if (!TryReadOptionalExactNumbers(
                dictionary.GetValueOrNull("Domain"),
                document,
                4,
                out double[]? domainNumbers))
        {
            return false;
        }
        double[] domainValues = domainNumbers ?? new[] { 0d, 1d, 0d, 1d };
        var domain = new PdfRectangle(
            domainValues[0],
            domainValues[2],
            domainValues[1],
            domainValues[3]);

        double[]? boxValues = ReadExactNumbers(
            dictionary.GetValueOrNull("BBox"),
            document,
            4);
        PdfRectangle? boundingBox = boxValues is null
            ? null
            : new PdfRectangle(boxValues[0], boxValues[1], boxValues[2], boxValues[3]);

        if (!TryReadFunctionMatrix(
                dictionary.GetValueOrNull("Matrix"),
                document,
                out PdfMatrix localMatrix))
        {
            return false;
        }
        PdfFunction[]? functions = ReadFunctionSet(
            dictionary.GetValueOrNull("Function"),
            colorSpace.Components,
            document);
        if (functions is null)
            return false;

        brush = new PdfFunctionShadingBrush(
            domain,
            boundingBox,
            localMatrix.Multiply(matrix),
            matrix,
            functions,
            colorSpace);
        return true;
    }

    private static PdfFunction[]? ReadFunctionSet(
        PdfObject? value,
        int componentCount,
        PdfDocumentCore document)
    {
        if (value is null)
            return null;
        PdfObject resolved = value.Resolve(document);
        if (resolved is PdfArray array)
        {
            if (array.Count != componentCount)
                return null;
            if (array.Count > document.Options.MaximumImageComponents ||
                array.Count > document.Options.MaximumCollectionItems)
            {
                throw new PdfLimitException(
                    "Function shading component count exceeds the configured limit.");
            }

            var functions = new PdfFunction[array.Count];
            for (int index = 0; index < functions.Length; index++)
            {
                PdfFunction? function = PdfFunction.Create(
                    array[index],
                    document,
                    expectedInputCount: 2,
                    expectedOutputCount: 1);
                if (function is null ||
                    function.InputCount != 2 ||
                    function.OutputCount != 1)
                {
                    return null;
                }
                functions[index] = function;
            }
            return functions;
        }

        PdfFunction? combined = PdfFunction.Create(
            resolved,
            document,
            expectedInputCount: 2,
            expectedOutputCount: componentCount);
        return combined is not null &&
               combined.InputCount == 2 &&
               combined.OutputCount == componentCount
            ? new[] { combined }
            : null;
    }

    private static bool TryReadFunctionMatrix(
        PdfObject? value,
        PdfDocumentCore document,
        out PdfMatrix matrix)
    {
        matrix = PdfMatrix.Identity;
        PdfArray? array = value.AsArray(document);
        if (array is null || array.Count != 6)
            return true;
        double[]? numbers = ReadNumbers(value, document, 6);
        if (numbers is null)
            return false;
        matrix = new PdfMatrix(
            numbers[0],
            numbers[1],
            numbers[2],
            numbers[3],
            numbers[4],
            numbers[5]);
        return matrix.IsFinite;
    }

    public static bool TryRead(
        PdfObject? value,
        PdfDocumentCore document,
        PdfMatrix matrix,
        out PdfGradientBrush? brush)
    {
        brush = null;
        PdfDictionary? dictionary = value.AsDictionary(document);
        if (dictionary is null)
            return false;

        int type = dictionary.GetValueOrNull("ShadingType").AsInteger(document) ?? 0;
        PdfShadingKind kind;
        int coordinateCount;
        switch (type)
        {
            case 2:
                kind = PdfShadingKind.Axial;
                coordinateCount = 4;
                break;
            case 3:
                kind = PdfShadingKind.Radial;
                coordinateCount = 6;
                break;
            default:
                return false;
        }

        double[]? coordinates = ReadNumbers(
            dictionary.GetValueOrNull("Coords"),
            document,
            coordinateCount);
        if (coordinates is null)
            return false;

        PdfColorSpaceDefinition? colorSpace = PdfColorSpaceDefinition.Parse(
            dictionary.GetValueOrNull("ColorSpace"),
            resources: null,
            document);
        if (colorSpace is null)
            return false;
        int componentCount = colorSpace.Components;

        (bool extendStart, bool extendEnd) = ReadExtend(
            dictionary.GetValueOrNull("Extend"),
            document);
        PdfObject? function = dictionary.GetValueOrNull("Function");
        int stopCount = Math.Min(document.Options.MaximumShadingStops, 33);
        var stops = new List<PdfGradientStop>(stopCount);
        for (int index = 0; index < stopCount; index++)
        {
            double offset = index / (double)(stopCount - 1);
            double[] components = Evaluate(function, offset, componentCount, document, 0);
            stops.Add(new PdfGradientStop(offset, colorSpace.Convert(components)));
        }

        brush = new PdfGradientBrush(
            kind,
            coordinates,
            stops,
            extendStart,
            extendEnd,
            matrix);
        return true;
    }

    private static double[] Evaluate(
        PdfObject? functionObject,
        double input,
        int componentCount,
        PdfDocumentCore document,
        int depth)
    {
        if (depth > document.Options.MaximumObjectDepth)
            throw new PdfLimitException("Shading function nesting exceeds the configured limit.");
        if (functionObject is null)
            return Enumerable.Repeat(input, componentCount).ToArray();

        PdfObject resolved = functionObject.Resolve(document);
        if (resolved is PdfArray functions)
        {
            var combined = new List<double>();
            foreach (PdfObject function in functions)
            {
                combined.AddRange(Evaluate(function, input, 1, document, depth + 1));
                if (combined.Count >= componentCount)
                    break;
            }

            while (combined.Count < componentCount)
                combined.Add(combined.Count == 0 ? input : combined[^1]);
            return combined.Take(componentCount).ToArray();
        }

        PdfFunction? parsed = PdfFunction.Create(
            resolved,
            document,
            expectedInputCount: 1,
            expectedOutputCount: componentCount,
            depth);
        if (parsed is not null)
        {
            Span<double> functionInput = stackalloc double[1];
            functionInput[0] = input;
            return parsed.Evaluate(functionInput, componentCount);
        }

        PdfDictionary? dictionary = resolved switch
        {
            PdfDictionary direct => direct,
            PdfStream stream => stream.Dictionary,
            _ => null
        };
        if (dictionary is null)
            return Enumerable.Repeat(input, componentCount).ToArray();

        return dictionary.GetValueOrNull("FunctionType").AsInteger(document) switch
        {
            2 => EvaluateExponential(dictionary, input, componentCount, document),
            3 => EvaluateStitching(dictionary, input, componentCount, document, depth),
            _ => Enumerable.Repeat(input, componentCount).ToArray()
        };
    }

    private static double[] EvaluateExponential(
        PdfDictionary dictionary,
        double input,
        int componentCount,
        PdfDocumentCore document)
    {
        double[] domain = ReadNumbers(dictionary.GetValueOrNull("Domain"), document, 2) ??
                          new[] { 0d, 1d };
        double value = Math.Clamp(input, 0, 1);
        double x = domain[0] + value * (domain[1] - domain[0]);
        double exponent = dictionary.GetValueOrNull("N").AsNumber(document) ?? 1;
        double factor = exponent == 1 ? x : Math.Pow(Math.Max(0, x), exponent);
        double[] c0 = ReadVariableNumbers(dictionary.GetValueOrNull("C0"), document) ??
                      new[] { 0d };
        double[] c1 = ReadVariableNumbers(dictionary.GetValueOrNull("C1"), document) ??
                      new[] { 1d };
        var result = new double[componentCount];
        for (int index = 0; index < result.Length; index++)
        {
            double start = c0[Math.Min(index, c0.Length - 1)];
            double end = c1[Math.Min(index, c1.Length - 1)];
            result[index] = Clamp(start + factor * (end - start));
        }

        return result;
    }

    private static double[] EvaluateStitching(
        PdfDictionary dictionary,
        double input,
        int componentCount,
        PdfDocumentCore document,
        int depth)
    {
        PdfArray? functions = dictionary.GetValueOrNull("Functions").AsArray(document);
        if (functions is null || functions.Count == 0)
            return Enumerable.Repeat(input, componentCount).ToArray();

        double[] domain = ReadNumbers(dictionary.GetValueOrNull("Domain"), document, 2) ??
                          new[] { 0d, 1d };
        double[] bounds = ReadVariableNumbers(dictionary.GetValueOrNull("Bounds"), document) ??
                          Array.Empty<double>();
        double[] encode = ReadVariableNumbers(dictionary.GetValueOrNull("Encode"), document) ??
                          Enumerable.Range(0, functions.Count)
                              .SelectMany(_ => new[] { 0d, 1d })
                              .ToArray();

        double x = domain[0] + Math.Clamp(input, 0, 1) * (domain[1] - domain[0]);
        int functionIndex = 0;
        while (functionIndex < bounds.Length && x >= bounds[functionIndex])
            functionIndex++;
        functionIndex = Math.Min(functionIndex, functions.Count - 1);

        double intervalStart = functionIndex == 0
            ? domain[0]
            : bounds[Math.Min(functionIndex - 1, bounds.Length - 1)];
        double intervalEnd = functionIndex < bounds.Length
            ? bounds[functionIndex]
            : domain[1];
        double relative = intervalEnd == intervalStart
            ? 0
            : Math.Clamp((x - intervalStart) / (intervalEnd - intervalStart), 0, 1);
        int encodeIndex = functionIndex * 2;
        double encodedStart = encodeIndex < encode.Length ? encode[encodeIndex] : 0;
        double encodedEnd = encodeIndex + 1 < encode.Length ? encode[encodeIndex + 1] : 1;
        double encoded = encodedStart + relative * (encodedEnd - encodedStart);

        return Evaluate(
            functions[functionIndex],
            encoded,
            componentCount,
            document,
            depth + 1);
    }

    private static (bool Start, bool End) ReadExtend(
        PdfObject? value,
        PdfDocumentCore document)
    {
        PdfArray? array = value.AsArray(document);
        return array is { Count: >= 2 }
            ? (
                array[0].Resolve(document) is PdfBoolean { Value: true },
                array[1].Resolve(document) is PdfBoolean { Value: true })
            : (false, false);
    }

    internal static PdfMatrix ReadMatrix(
        PdfObject? value,
        PdfDocumentCore document,
        PdfMatrix fallback)
    {
        double[]? numbers = ReadNumbers(value, document, 6);
        if (numbers is null)
            return fallback;
        var matrix = new PdfMatrix(
            numbers[0],
            numbers[1],
            numbers[2],
            numbers[3],
            numbers[4],
            numbers[5]);
        return matrix.IsFinite ? matrix : fallback;
    }

    private static double[]? ReadNumbers(
        PdfObject? value,
        PdfDocumentCore document,
        int count)
    {
        PdfArray? array = value.AsArray(document);
        if (array is null || array.Count < count)
            return null;
        var result = new double[count];
        for (int index = 0; index < count; index++)
        {
            double? number = array[index].AsNumber(document);
            if (!number.HasValue || !double.IsFinite(number.Value))
                return null;
            result[index] = number.Value;
        }

        return result;
    }

    private static double[]? ReadExactNumbers(
        PdfObject? value,
        PdfDocumentCore document,
        int count)
    {
        PdfArray? array = value.AsArray(document);
        return array is { Count: var actual } && actual == count
            ? ReadNumbers(value, document, count)
            : null;
    }

    private static bool TryReadOptionalExactNumbers(
        PdfObject? value,
        PdfDocumentCore document,
        int count,
        out double[]? numbers)
    {
        numbers = null;
        PdfArray? array = value.AsArray(document);
        if (array is null || array.Count != count)
            return true;
        numbers = ReadNumbers(value, document, count);
        return numbers is not null;
    }

    private static double[]? ReadVariableNumbers(
        PdfObject? value,
        PdfDocumentCore document)
    {
        PdfArray? array = value.AsArray(document);
        if (array is null || array.Count == 0)
            return null;
        var result = new double[array.Count];
        for (int index = 0; index < result.Length; index++)
        {
            double? number = array[index].AsNumber(document);
            if (!number.HasValue || !double.IsFinite(number.Value))
                return null;
            result[index] = number.Value;
        }

        return result;
    }

    private static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
