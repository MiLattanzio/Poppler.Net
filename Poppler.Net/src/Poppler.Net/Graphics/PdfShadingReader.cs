using Poppler.Core;

namespace Poppler.Graphics;

internal static class PdfShadingReader
{
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

        PdfColorSpace colorSpace = ReadColorSpace(
            dictionary.GetValueOrNull("ColorSpace"),
            document);
        int componentCount = ComponentCount(colorSpace);
        if (componentCount == 0)
            return false;

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
            stops.Add(new PdfGradientStop(offset, CreateColor(colorSpace, components)));
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

    private static PdfColorSpace ReadColorSpace(PdfObject? value, PdfDocumentCore document)
    {
        PdfObject? resolved = value?.Resolve(document);
        string? name = resolved switch
        {
            PdfName direct => direct.Value,
            PdfArray { Count: > 0 } array => array[0].AsName(document),
            _ => null
        };
        return name switch
        {
            "DeviceGray" or "G" => PdfColorSpace.DeviceGray,
            "DeviceRGB" or "RGB" => PdfColorSpace.DeviceRgb,
            "DeviceCMYK" or "CMYK" => PdfColorSpace.DeviceCmyk,
            _ => PdfColorSpace.Unknown
        };
    }

    private static int ComponentCount(PdfColorSpace colorSpace) => colorSpace switch
    {
        PdfColorSpace.DeviceGray => 1,
        PdfColorSpace.DeviceRgb => 3,
        PdfColorSpace.DeviceCmyk => 4,
        _ => 0
    };

    private static PdfColor CreateColor(PdfColorSpace colorSpace, IReadOnlyList<double> values) =>
        colorSpace switch
        {
            PdfColorSpace.DeviceGray => PdfColor.Gray(values[0]),
            PdfColorSpace.DeviceRgb => PdfColor.Rgb(values[0], values[1], values[2]),
            PdfColorSpace.DeviceCmyk => PdfColor.Cmyk(
                values[0],
                values[1],
                values[2],
                values[3]),
            _ => PdfColor.Black
        };

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
