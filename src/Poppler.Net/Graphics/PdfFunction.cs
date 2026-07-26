using Poppler.Core;

namespace Poppler.Graphics;

/// <summary>
/// Bounded evaluator for the function types used by shadings and tint
/// transforms. It corresponds to Poppler's Function hierarchy.
/// </summary>
internal abstract class PdfFunction
{
    protected PdfFunction(double[] domain, double[]? range)
    {
        Domain = domain;
        Range = range;
    }

    protected double[] Domain { get; }
    protected double[]? Range { get; }
    public int InputCount => Domain.Length / 2;
    public int OutputCount => Range?.Length / 2 ?? GetNaturalOutputCount();

    public static PdfFunction? Create(
        PdfObject? value,
        PdfDocumentCore document,
        int expectedInputCount,
        int expectedOutputCount,
        int depth = 0)
    {
        if (value is null)
            return null;
        if (depth > document.Options.MaximumObjectDepth)
            throw new PdfLimitException("PDF function nesting exceeds the configured limit.");

        PdfObject resolved = value.Resolve(document);
        PdfDictionary? dictionary = resolved switch
        {
            PdfDictionary direct => direct,
            PdfStream stream => stream.Dictionary,
            _ => null
        };
        if (dictionary is null)
            return null;

        double[] domain = ReadNumbers(dictionary.GetValueOrNull("Domain"), document) ??
                          DefaultPairs(expectedInputCount, 0, 1);
        if (domain.Length < 2 || domain.Length % 2 != 0)
            return null;
        double[]? range = ReadNumbers(dictionary.GetValueOrNull("Range"), document);
        if (range is { Length: > 0 } && range.Length % 2 != 0)
            return null;

        return dictionary.GetValueOrNull("FunctionType").AsInteger(document) switch
        {
            0 when resolved is PdfStream stream =>
                SampledFunction.TryCreate(stream, dictionary, document, domain, range),
            2 => ExponentialFunction.TryCreate(
                dictionary,
                document,
                domain,
                range,
                expectedOutputCount),
            3 => StitchingFunction.TryCreate(
                dictionary,
                document,
                domain,
                range,
                expectedOutputCount,
                depth),
            _ => null
        };
    }

    public double[] Evaluate(ReadOnlySpan<double> input, int expectedOutputCount)
    {
        if (input.Length < InputCount)
            throw new ArgumentException("The function input has too few components.", nameof(input));
        var normalized = new double[InputCount];
        for (int index = 0; index < normalized.Length; index++)
        {
            double value = double.IsFinite(input[index]) ? input[index] : 0;
            normalized[index] = Clamp(value, Domain[index * 2], Domain[index * 2 + 1]);
        }

        double[] result = EvaluateCore(normalized);
        int count = expectedOutputCount > 0 ? expectedOutputCount : result.Length;
        if (result.Length != count)
        {
            var resized = new double[count];
            for (int index = 0; index < resized.Length; index++)
                resized[index] = result.Length == 0 ? 0 : result[Math.Min(index, result.Length - 1)];
            result = resized;
        }

        if (Range is not null)
        {
            int ranges = Math.Min(result.Length, Range.Length / 2);
            for (int index = 0; index < ranges; index++)
                result[index] = Clamp(result[index], Range[index * 2], Range[index * 2 + 1]);
        }

        for (int index = 0; index < result.Length; index++)
        {
            if (!double.IsFinite(result[index]))
                result[index] = 0;
        }

        return result;
    }

    protected abstract double[] EvaluateCore(ReadOnlySpan<double> input);
    protected abstract int GetNaturalOutputCount();

    protected static double Interpolate(
        double value,
        double sourceMin,
        double sourceMax,
        double targetMin,
        double targetMax)
    {
        if (sourceMax == sourceMin)
            return targetMin;
        double fraction = (value - sourceMin) / (sourceMax - sourceMin);
        return targetMin + fraction * (targetMax - targetMin);
    }

    protected static double Clamp(double value, double first, double second)
    {
        double minimum = Math.Min(first, second);
        double maximum = Math.Max(first, second);
        return Math.Clamp(value, minimum, maximum);
    }

    private static double[] DefaultPairs(int count, double minimum, double maximum) =>
        Enumerable.Range(0, Math.Max(1, count))
            .SelectMany(_ => new[] { minimum, maximum })
            .ToArray();

    internal static double[]? ReadNumbers(PdfObject? value, PdfDocumentCore document)
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

    private sealed class ExponentialFunction : PdfFunction
    {
        private readonly double[] _c0;
        private readonly double[] _c1;
        private readonly double _exponent;

        private ExponentialFunction(
            double[] domain,
            double[]? range,
            double[] c0,
            double[] c1,
            double exponent)
            : base(domain, range)
        {
            _c0 = c0;
            _c1 = c1;
            _exponent = exponent;
        }

        public static PdfFunction? TryCreate(
            PdfDictionary dictionary,
            PdfDocumentCore document,
            double[] domain,
            double[]? range,
            int expectedOutputCount)
        {
            if (domain.Length != 2)
                return null;
            double[] c0 = ReadNumbers(dictionary.GetValueOrNull("C0"), document) ??
                          Enumerable.Repeat(0d, Math.Max(1, expectedOutputCount)).ToArray();
            double[] c1 = ReadNumbers(dictionary.GetValueOrNull("C1"), document) ??
                          Enumerable.Repeat(1d, Math.Max(1, expectedOutputCount)).ToArray();
            int count = Math.Max(c0.Length, c1.Length);
            if (count > document.Options.MaximumImageComponents)
                throw new PdfLimitException("Function output component count exceeds the configured limit.");
            if (c0.Length != count)
                c0 = Resize(c0, count);
            if (c1.Length != count)
                c1 = Resize(c1, count);
            double exponent = dictionary.GetValueOrNull("N").AsNumber(document) ?? 1;
            return double.IsFinite(exponent)
                ? new ExponentialFunction(domain, range, c0, c1, exponent)
                : null;
        }

        protected override double[] EvaluateCore(ReadOnlySpan<double> input)
        {
            double powered = Math.Pow(Math.Max(0, input[0]), _exponent);
            var result = new double[_c0.Length];
            for (int index = 0; index < result.Length; index++)
                result[index] = _c0[index] + powered * (_c1[index] - _c0[index]);
            return result;
        }

        protected override int GetNaturalOutputCount() => _c0.Length;

        private static double[] Resize(double[] source, int count)
        {
            var result = new double[count];
            for (int index = 0; index < count; index++)
                result[index] = source[Math.Min(index, source.Length - 1)];
            return result;
        }
    }

    private sealed class StitchingFunction : PdfFunction
    {
        private readonly PdfFunction[] _functions;
        private readonly double[] _bounds;
        private readonly double[] _encode;
        private readonly int _outputCount;

        private StitchingFunction(
            double[] domain,
            double[]? range,
            PdfFunction[] functions,
            double[] bounds,
            double[] encode,
            int outputCount)
            : base(domain, range)
        {
            _functions = functions;
            _bounds = bounds;
            _encode = encode;
            _outputCount = outputCount;
        }

        public static PdfFunction? TryCreate(
            PdfDictionary dictionary,
            PdfDocumentCore document,
            double[] domain,
            double[]? range,
            int expectedOutputCount,
            int depth)
        {
            if (domain.Length != 2)
                return null;
            PdfArray? values = dictionary.GetValueOrNull("Functions").AsArray(document);
            if (values is null || values.Count == 0)
                return null;
            if (values.Count > document.Options.MaximumCollectionItems)
                throw new PdfLimitException("Stitching function count exceeds the configured limit.");

            var functions = new PdfFunction[values.Count];
            for (int index = 0; index < functions.Length; index++)
            {
                functions[index] = Create(
                    values[index],
                    document,
                    expectedInputCount: 1,
                    expectedOutputCount,
                    depth + 1) ?? throw new PdfUnsupportedFeatureException(
                    "unsupported child function in a stitching function");
            }

            double[] bounds = ReadNumbers(dictionary.GetValueOrNull("Bounds"), document) ??
                              Array.Empty<double>();
            if (bounds.Length != functions.Length - 1)
                return null;
            double[] encode = ReadNumbers(dictionary.GetValueOrNull("Encode"), document) ??
                              Array.Empty<double>();
            if (encode.Length != functions.Length * 2)
                return null;
            int outputCount = expectedOutputCount > 0
                ? expectedOutputCount
                : functions.Max(function => function.OutputCount);
            return new StitchingFunction(
                domain,
                range,
                functions,
                bounds,
                encode,
                outputCount);
        }

        protected override double[] EvaluateCore(ReadOnlySpan<double> input)
        {
            double value = input[0];
            int index = 0;
            while (index < _bounds.Length && value >= _bounds[index])
                index++;
            double start = index == 0 ? Domain[0] : _bounds[index - 1];
            double end = index == _bounds.Length ? Domain[1] : _bounds[index];
            double encoded = Interpolate(
                value,
                start,
                end,
                _encode[index * 2],
                _encode[index * 2 + 1]);
            Span<double> childInput = stackalloc double[1];
            childInput[0] = encoded;
            return _functions[index].Evaluate(childInput, _outputCount);
        }

        protected override int GetNaturalOutputCount() => _outputCount;
    }

    private sealed class SampledFunction : PdfFunction
    {
        private readonly int[] _sizes;
        private readonly double[] _encode;
        private readonly double[] _decode;
        private readonly double[] _samples;
        private readonly int _outputCount;

        private SampledFunction(
            double[] domain,
            double[]? range,
            int[] sizes,
            double[] encode,
            double[] decode,
            double[] samples,
            int outputCount)
            : base(domain, range)
        {
            _sizes = sizes;
            _encode = encode;
            _decode = decode;
            _samples = samples;
            _outputCount = outputCount;
        }

        public static PdfFunction? TryCreate(
            PdfStream stream,
            PdfDictionary dictionary,
            PdfDocumentCore document,
            double[] domain,
            double[]? range)
        {
            double[]? sizeValues = ReadNumbers(dictionary.GetValueOrNull("Size"), document);
            if (sizeValues is null || sizeValues.Length != domain.Length / 2)
                return null;
            var sizes = new int[sizeValues.Length];
            long samplePoints = 1;
            for (int index = 0; index < sizes.Length; index++)
            {
                double value = sizeValues[index];
                if (value < 1 || value > int.MaxValue || value != Math.Truncate(value))
                    return null;
                sizes[index] = (int)value;
                samplePoints = checked(samplePoints * sizes[index]);
                if (samplePoints > document.Options.MaximumFunctionSamples)
                    throw new PdfLimitException("Sampled function size exceeds the configured limit.");
            }

            int bits = dictionary.GetValueOrNull("BitsPerSample").AsInteger(document) ?? 0;
            if (bits is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
                return null;
            double[]? decode = ReadNumbers(dictionary.GetValueOrNull("Decode"), document) ?? range;
            if (decode is null || decode.Length == 0 || decode.Length % 2 != 0)
                return null;
            int outputs = decode.Length / 2;
            if (outputs > document.Options.MaximumImageComponents)
                throw new PdfLimitException("Function output component count exceeds the configured limit.");

            double[] encode = ReadNumbers(dictionary.GetValueOrNull("Encode"), document) ??
                              sizes.SelectMany(size => new[] { 0d, size - 1d }).ToArray();
            if (encode.Length != sizes.Length * 2)
                return null;

            long sampleCount = checked(samplePoints * outputs);
            if (sampleCount > document.Options.MaximumFunctionSamples)
                throw new PdfLimitException("Sampled function table exceeds the configured limit.");
            byte[] bytes = document.Decode(stream);
            var reader = new SampleBitReader(bytes);
            var samples = new double[sampleCount];
            double maximum = bits == 32 ? uint.MaxValue : (1UL << bits) - 1;
            for (int index = 0; index < samples.Length; index++)
                samples[index] = reader.Read(bits) / maximum;
            return new SampledFunction(
                domain,
                range,
                sizes,
                encode,
                decode,
                samples,
                outputs);
        }

        protected override double[] EvaluateCore(ReadOnlySpan<double> input)
        {
            int dimensions = _sizes.Length;
            var lower = new int[dimensions];
            var upper = new int[dimensions];
            var fractions = new double[dimensions];
            for (int dimension = 0; dimension < dimensions; dimension++)
            {
                double encoded = Interpolate(
                    input[dimension],
                    Domain[dimension * 2],
                    Domain[dimension * 2 + 1],
                    _encode[dimension * 2],
                    _encode[dimension * 2 + 1]);
                encoded = Math.Clamp(encoded, 0, _sizes[dimension] - 1);
                lower[dimension] = (int)Math.Floor(encoded);
                upper[dimension] = Math.Min(lower[dimension] + 1, _sizes[dimension] - 1);
                fractions[dimension] = encoded - lower[dimension];
            }

            var result = new double[_outputCount];
            if (dimensions > 16)
            {
                int index = SampleIndex(lower, _sizes, _outputCount);
                DecodeSample(index, result);
                return result;
            }

            int corners = 1 << dimensions;
            var coordinates = new int[dimensions];
            for (int corner = 0; corner < corners; corner++)
            {
                double weight = 1;
                for (int dimension = 0; dimension < dimensions; dimension++)
                {
                    bool high = (corner & (1 << dimension)) != 0;
                    coordinates[dimension] = high ? upper[dimension] : lower[dimension];
                    weight *= high ? fractions[dimension] : 1 - fractions[dimension];
                }

                if (weight == 0)
                    continue;
                int sampleIndex = SampleIndex(coordinates, _sizes, _outputCount);
                for (int output = 0; output < result.Length; output++)
                {
                    double decoded = Interpolate(
                        _samples[sampleIndex + output],
                        0,
                        1,
                        _decode[output * 2],
                        _decode[output * 2 + 1]);
                    result[output] += weight * decoded;
                }
            }

            return result;
        }

        protected override int GetNaturalOutputCount() => _outputCount;

        private void DecodeSample(int index, Span<double> destination)
        {
            for (int output = 0; output < destination.Length; output++)
            {
                destination[output] = Interpolate(
                    _samples[index + output],
                    0,
                    1,
                    _decode[output * 2],
                    _decode[output * 2 + 1]);
            }
        }

        private static int SampleIndex(
            IReadOnlyList<int> coordinates,
            IReadOnlyList<int> sizes,
            int outputs)
        {
            int index = 0;
            int stride = 1;
            for (int dimension = 0; dimension < coordinates.Count; dimension++)
            {
                index = checked(index + coordinates[dimension] * stride);
                stride = checked(stride * sizes[dimension]);
            }

            return checked(index * outputs);
        }
    }

    private ref struct SampleBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bit;

        public SampleBitReader(ReadOnlySpan<byte> data) => _data = data;

        public uint Read(int count)
        {
            uint result = 0;
            for (int index = 0; index < count; index++)
            {
                if (_bit >= _data.Length * 8)
                    throw new PdfFormatException("Truncated sampled function.");
                int value = (_data[_bit >> 3] >> (7 - (_bit & 7))) & 1;
                result = (result << 1) | (uint)value;
                _bit++;
            }

            return result;
        }
    }
}
