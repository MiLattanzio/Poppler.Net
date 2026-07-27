using System.Globalization;
using System.Text;
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
            4 when resolved is PdfStream stream =>
                CalculatorFunction.TryCreate(
                    stream,
                    document,
                    domain,
                    range,
                    expectedOutputCount),
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

    private sealed class CalculatorFunction : PdfFunction
    {
        private static readonly HashSet<string> Operators = new(
            StringComparer.Ordinal)
        {
            "abs", "add", "and", "atan", "ceiling", "copy", "cos", "cvi",
            "cvr", "div", "dup", "eq", "exch", "exp", "false", "floor",
            "ge", "gt", "idiv", "if", "ifelse", "index", "le", "ln", "log",
            "lt", "mod", "mul", "ne", "neg", "not", "or", "pop", "roll",
            "round", "sin", "sqrt", "sub", "true", "truncate", "xor"
        };

        private readonly CalculatorProcedure _procedure;
        private readonly int _outputCount;
        private readonly int _maximumOperations;

        private CalculatorFunction(
            double[] domain,
            double[]? range,
            CalculatorProcedure procedure,
            int outputCount,
            int maximumOperations)
            : base(domain, range)
        {
            _procedure = procedure;
            _outputCount = outputCount;
            _maximumOperations = maximumOperations;
        }

        public static PdfFunction? TryCreate(
            PdfStream stream,
            PdfDocumentCore document,
            double[] domain,
            double[]? range,
            int expectedOutputCount)
        {
            try
            {
                string source = Encoding.ASCII.GetString(document.Decode(stream));
                var parser = new CalculatorParser(source);
                CalculatorProcedure procedure = parser.Parse();
                if (!Validate(procedure, depth: 0))
                    return null;
                int outputCount = range?.Length / 2 ?? expectedOutputCount;
                if (outputCount < 1 ||
                    outputCount > document.Options.MaximumImageComponents)
                {
                    return null;
                }
                return new CalculatorFunction(
                    domain,
                    range,
                    procedure,
                    outputCount,
                    Math.Min(document.Options.MaximumGraphicsOperations, 100_000));
            }
            catch (PdfException)
            {
                return null;
            }
        }

        protected override double[] EvaluateCore(ReadOnlySpan<double> input)
        {
            var stack = new List<CalculatorValue>(input.Length + _outputCount + 8);
            for (int index = 0; index < input.Length; index++)
                stack.Add(CalculatorValue.Number(input[index]));
            var context = new CalculatorContext(stack, _maximumOperations);
            try
            {
                Execute(_procedure, context, depth: 0);
                if (stack.Count < _outputCount)
                    return new double[_outputCount];
                var result = new double[_outputCount];
                int start = stack.Count - result.Length;
                for (int index = 0; index < result.Length; index++)
                    result[index] = stack[start + index].AsNumber();
                return result;
            }
            catch (PdfException)
            {
                return new double[_outputCount];
            }
        }

        protected override int GetNaturalOutputCount() => _outputCount;

        private static bool Validate(CalculatorProcedure procedure, int depth)
        {
            if (depth > 32)
                return false;
            foreach (object token in procedure.Tokens)
            {
                if (token is string operation && !Operators.Contains(operation))
                    return false;
                if (token is CalculatorProcedure child && !Validate(child, depth + 1))
                    return false;
            }
            return true;
        }

        private static void Execute(
            CalculatorProcedure procedure,
            CalculatorContext context,
            int depth)
        {
            if (depth > 32)
                throw new PdfLimitException("Calculator function nesting exceeds its limit.");
            foreach (object token in procedure.Tokens)
            {
                context.Count();
                switch (token)
                {
                    case double number:
                        context.Push(CalculatorValue.Number(number));
                        break;
                    case bool boolean:
                        context.Push(CalculatorValue.Boolean(boolean));
                        break;
                    case CalculatorProcedure child:
                        context.Push(CalculatorValue.Procedure(child));
                        break;
                    case string operation:
                        ExecuteOperator(operation, context, depth);
                        break;
                }
            }
        }

        private static void ExecuteOperator(
            string operation,
            CalculatorContext context,
            int depth)
        {
            switch (operation)
            {
                case "true":
                    context.Push(CalculatorValue.Boolean(true));
                    return;
                case "false":
                    context.Push(CalculatorValue.Boolean(false));
                    return;
                case "dup":
                    context.Push(context.Peek());
                    return;
                case "exch":
                {
                    CalculatorValue second = context.Pop();
                    CalculatorValue first = context.Pop();
                    context.Push(second);
                    context.Push(first);
                    return;
                }
                case "pop":
                    context.Pop();
                    return;
                case "copy":
                {
                    int count = context.PopInteger();
                    if (count < 0 || count > context.Stack.Count)
                        throw new PdfFormatException("Invalid calculator copy.");
                    CalculatorValue[] values =
                        context.Stack.Skip(context.Stack.Count - count).ToArray();
                    foreach (CalculatorValue value in values)
                        context.Push(value);
                    return;
                }
                case "index":
                {
                    int index = context.PopInteger();
                    if (index < 0 || index >= context.Stack.Count)
                        throw new PdfFormatException("Invalid calculator index.");
                    context.Push(context.Stack[context.Stack.Count - 1 - index]);
                    return;
                }
                case "roll":
                {
                    int amount = context.PopInteger();
                    int count = context.PopInteger();
                    if (count < 0 || count > context.Stack.Count)
                        throw new PdfFormatException("Invalid calculator roll.");
                    if (count == 0)
                        return;
                    amount %= count;
                    if (amount < 0)
                        amount += count;
                    int start = context.Stack.Count - count;
                    CalculatorValue[] values = context.Stack.Skip(start).ToArray();
                    context.Stack.RemoveRange(start, count);
                    for (int index = 0; index < count; index++)
                        context.Push(values[(index - amount + count) % count]);
                    return;
                }
                case "if":
                {
                    CalculatorProcedure procedure = context.Pop().AsProcedure();
                    if (context.Pop().AsBoolean())
                        Execute(procedure, context, depth + 1);
                    return;
                }
                case "ifelse":
                {
                    CalculatorProcedure whenFalse = context.Pop().AsProcedure();
                    CalculatorProcedure whenTrue = context.Pop().AsProcedure();
                    Execute(
                        context.Pop().AsBoolean() ? whenTrue : whenFalse,
                        context,
                        depth + 1);
                    return;
                }
                case "abs":
                    Unary(context, Math.Abs);
                    return;
                case "neg":
                    Unary(context, value => -value);
                    return;
                case "ceiling":
                    Unary(context, Math.Ceiling);
                    return;
                case "floor":
                    Unary(context, Math.Floor);
                    return;
                case "round":
                    Unary(context, value => Math.Round(value, MidpointRounding.AwayFromZero));
                    return;
                case "truncate":
                case "cvi":
                    Unary(context, Math.Truncate);
                    return;
                case "cvr":
                    Unary(context, value => value);
                    return;
                case "sqrt":
                    Unary(context, value => Math.Sqrt(Math.Max(0, value)));
                    return;
                case "ln":
                    Unary(context, Math.Log);
                    return;
                case "log":
                    Unary(context, Math.Log10);
                    return;
                case "sin":
                    Unary(context, value => Math.Sin(value * Math.PI / 180));
                    return;
                case "cos":
                    Unary(context, value => Math.Cos(value * Math.PI / 180));
                    return;
                case "add":
                    Binary(context, (first, second) => first + second);
                    return;
                case "sub":
                    Binary(context, (first, second) => first - second);
                    return;
                case "mul":
                    Binary(context, (first, second) => first * second);
                    return;
                case "div":
                    Binary(context, (first, second) => first / second);
                    return;
                case "idiv":
                    Binary(context, (first, second) => Math.Truncate(first / second));
                    return;
                case "mod":
                    Binary(context, (first, second) => first % second);
                    return;
                case "exp":
                    Binary(context, Math.Pow);
                    return;
                case "atan":
                    Binary(
                        context,
                        (first, second) =>
                        {
                            double angle = Math.Atan2(first, second) * 180 / Math.PI;
                            return angle < 0 ? angle + 360 : angle;
                        });
                    return;
                case "eq":
                    Compare(context, static (first, second) => first == second);
                    return;
                case "ne":
                    Compare(context, static (first, second) => first != second);
                    return;
                case "gt":
                    Compare(context, static (first, second) => first > second);
                    return;
                case "ge":
                    Compare(context, static (first, second) => first >= second);
                    return;
                case "lt":
                    Compare(context, static (first, second) => first < second);
                    return;
                case "le":
                    Compare(context, static (first, second) => first <= second);
                    return;
                case "and":
                    BooleanOrBitwise(context, static (first, second) => first && second,
                        static (first, second) => first & second);
                    return;
                case "or":
                    BooleanOrBitwise(context, static (first, second) => first || second,
                        static (first, second) => first | second);
                    return;
                case "xor":
                    BooleanOrBitwise(context, static (first, second) => first ^ second,
                        static (first, second) => first ^ second);
                    return;
                case "not":
                {
                    CalculatorValue value = context.Pop();
                    context.Push(value.Kind == CalculatorValueKind.Boolean
                        ? CalculatorValue.Boolean(!value.AsBoolean())
                        : CalculatorValue.Number(~value.AsInteger()));
                    return;
                }
                default:
                    throw new PdfUnsupportedFeatureException(
                        $"calculator function operator {operation}");
            }
        }

        private static void Unary(
            CalculatorContext context,
            Func<double, double> function)
        {
            double value = context.Pop().AsNumber();
            context.Push(CalculatorValue.Number(function(value)));
        }

        private static void Binary(
            CalculatorContext context,
            Func<double, double, double> function)
        {
            double second = context.Pop().AsNumber();
            double first = context.Pop().AsNumber();
            context.Push(CalculatorValue.Number(function(first, second)));
        }

        private static void Compare(
            CalculatorContext context,
            Func<double, double, bool> function)
        {
            double second = context.Pop().AsNumber();
            double first = context.Pop().AsNumber();
            context.Push(CalculatorValue.Boolean(function(first, second)));
        }

        private static void BooleanOrBitwise(
            CalculatorContext context,
            Func<bool, bool, bool> booleanFunction,
            Func<int, int, int> integerFunction)
        {
            CalculatorValue second = context.Pop();
            CalculatorValue first = context.Pop();
            context.Push(first.Kind == CalculatorValueKind.Boolean &&
                         second.Kind == CalculatorValueKind.Boolean
                ? CalculatorValue.Boolean(
                    booleanFunction(first.AsBoolean(), second.AsBoolean()))
                : CalculatorValue.Number(
                    integerFunction(first.AsInteger(), second.AsInteger())));
        }

        private sealed record CalculatorProcedure(IReadOnlyList<object> Tokens);

        private enum CalculatorValueKind
        {
            Number,
            Boolean,
            Procedure
        }

        private readonly record struct CalculatorValue(
            CalculatorValueKind Kind,
            double NumberValue,
            bool BooleanValue,
            CalculatorProcedure? ProcedureValue)
        {
            public static CalculatorValue Number(double value) =>
                new(CalculatorValueKind.Number, value, false, null);

            public static CalculatorValue Boolean(bool value) =>
                new(CalculatorValueKind.Boolean, 0, value, null);

            public static CalculatorValue Procedure(CalculatorProcedure value) =>
                new(CalculatorValueKind.Procedure, 0, false, value);

            public double AsNumber() => Kind == CalculatorValueKind.Number
                ? NumberValue
                : throw new PdfFormatException("Calculator value is not numeric.");

            public int AsInteger()
            {
                double value = AsNumber();
                if (!double.IsFinite(value) ||
                    value < int.MinValue ||
                    value > int.MaxValue)
                {
                    throw new PdfFormatException("Calculator integer is out of range.");
                }
                return (int)Math.Truncate(value);
            }

            public bool AsBoolean() => Kind == CalculatorValueKind.Boolean
                ? BooleanValue
                : throw new PdfFormatException("Calculator value is not boolean.");

            public CalculatorProcedure AsProcedure() =>
                Kind == CalculatorValueKind.Procedure && ProcedureValue is not null
                    ? ProcedureValue
                    : throw new PdfFormatException("Calculator value is not a procedure.");
        }

        private sealed class CalculatorContext
        {
            private int _operations;
            private readonly int _maximumOperations;

            public CalculatorContext(
                List<CalculatorValue> stack,
                int maximumOperations)
            {
                Stack = stack;
                _maximumOperations = maximumOperations;
            }

            public List<CalculatorValue> Stack { get; }

            public void Count()
            {
                _operations++;
                if (_operations > _maximumOperations)
                    throw new PdfLimitException("Calculator function operation limit exceeded.");
            }

            public void Push(CalculatorValue value)
            {
                if (Stack.Count >= 1_024)
                    throw new PdfLimitException("Calculator function stack limit exceeded.");
                Stack.Add(value);
            }

            public CalculatorValue Pop()
            {
                if (Stack.Count == 0)
                    throw new PdfFormatException("Calculator function stack underflow.");
                CalculatorValue value = Stack[^1];
                Stack.RemoveAt(Stack.Count - 1);
                return value;
            }

            public CalculatorValue Peek()
            {
                if (Stack.Count == 0)
                    throw new PdfFormatException("Calculator function stack underflow.");
                return Stack[^1];
            }

            public int PopInteger() => Pop().AsInteger();
        }

        private sealed class CalculatorParser
        {
            private readonly string _source;
            private int _offset;
            private int _tokenCount;

            public CalculatorParser(string source) => _source = source;

            public CalculatorProcedure Parse()
            {
                SkipSpace();
                CalculatorProcedure result;
                if (Peek() == '{')
                {
                    _offset++;
                    result = ParseProcedure(expectClosingBrace: true, depth: 0);
                }
                else
                {
                    result = ParseProcedure(expectClosingBrace: false, depth: 0);
                }
                SkipSpace();
                if (_offset != _source.Length)
                    throw new PdfFormatException("Trailing calculator function data.");
                return result;
            }

            private CalculatorProcedure ParseProcedure(
                bool expectClosingBrace,
                int depth)
            {
                if (depth > 32)
                    throw new PdfLimitException("Calculator function nesting exceeds its limit.");
                var tokens = new List<object>();
                while (true)
                {
                    SkipSpace();
                    char current = Peek();
                    if (current == '\0')
                    {
                        if (expectClosingBrace)
                            throw new PdfFormatException("Unterminated calculator procedure.");
                        break;
                    }
                    if (current == '}')
                    {
                        if (!expectClosingBrace)
                            throw new PdfFormatException("Unexpected calculator procedure terminator.");
                        _offset++;
                        break;
                    }
                    if (current == '{')
                    {
                        _offset++;
                        tokens.Add(ParseProcedure(expectClosingBrace: true, depth + 1));
                    }
                    else
                    {
                        string token = ReadToken();
                        if (double.TryParse(
                                token,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double number))
                        {
                            tokens.Add(number);
                        }
                        else if (token == "true")
                        {
                            tokens.Add(true);
                        }
                        else if (token == "false")
                        {
                            tokens.Add(false);
                        }
                        else
                        {
                            tokens.Add(token);
                        }
                    }

                    _tokenCount++;
                    if (_tokenCount > 10_000)
                        throw new PdfLimitException("Calculator function token limit exceeded.");
                }
                return new CalculatorProcedure(tokens.AsReadOnly());
            }

            private string ReadToken()
            {
                int start = _offset;
                while (_offset < _source.Length)
                {
                    char value = _source[_offset];
                    if (char.IsWhiteSpace(value) || value is '{' or '}' or '%')
                        break;
                    _offset++;
                }
                if (_offset == start)
                    throw new PdfFormatException("Invalid calculator function token.");
                return _source[start.._offset];
            }

            private void SkipSpace()
            {
                while (_offset < _source.Length)
                {
                    if (char.IsWhiteSpace(_source[_offset]))
                    {
                        _offset++;
                        continue;
                    }
                    if (_source[_offset] != '%')
                        break;
                    while (_offset < _source.Length &&
                           _source[_offset] is not ('\r' or '\n'))
                    {
                        _offset++;
                    }
                }
            }

            private char Peek() =>
                _offset < _source.Length ? _source[_offset] : '\0';
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
