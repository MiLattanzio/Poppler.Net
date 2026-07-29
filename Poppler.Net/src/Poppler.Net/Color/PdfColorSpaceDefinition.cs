using Poppler.Core;
using Poppler.Graphics;

namespace Poppler.Color;

internal sealed class PdfColorSpaceDefinition
{
    private readonly double[]? _whitePoint;
    private readonly double[]? _blackPoint;
    private readonly double[]? _gamma;
    private readonly double[]? _matrix;
    private readonly double[]? _labRange;
    private readonly PdfColorSpaceDefinition? _alternate;
    private readonly byte[]? _lookup;
    private readonly int _highValue;
    private readonly PdfFunction? _tintTransform;
    private readonly PdfIccProfile? _iccProfile;

    private PdfColorSpaceDefinition(
        PdfColorSpace kind,
        string name,
        int components,
        double[]? whitePoint = null,
        double[]? blackPoint = null,
        double[]? gamma = null,
        double[]? matrix = null,
        double[]? labRange = null,
        PdfColorSpaceDefinition? alternate = null,
        byte[]? lookup = null,
        int highValue = 0,
        PdfFunction? tintTransform = null,
        PdfIccProfile? iccProfile = null)
    {
        Kind = kind;
        Name = name;
        Components = components;
        _whitePoint = whitePoint;
        _blackPoint = blackPoint;
        _gamma = gamma;
        _matrix = matrix;
        _labRange = labRange;
        _alternate = alternate;
        _lookup = lookup;
        _highValue = highValue;
        _tintTransform = tintTransform;
        _iccProfile = iccProfile;
    }

    public PdfColorSpace Kind { get; }
    public string Name { get; }
    public int Components { get; }

    public static PdfColorSpaceDefinition DeviceGray { get; } =
        new(PdfColorSpace.DeviceGray, "DeviceGray", 1);

    public static PdfColorSpaceDefinition DeviceRgb { get; } =
        new(PdfColorSpace.DeviceRgb, "DeviceRGB", 3);

    public static PdfColorSpaceDefinition DeviceCmyk { get; } =
        new(PdfColorSpace.DeviceCmyk, "DeviceCMYK", 4);

    public static PdfColorSpaceDefinition? Parse(
        PdfObject? value,
        PdfDictionary? resources,
        PdfDocumentCore document,
        int depth = 0)
    {
        if (value is null)
            return null;
        if (depth > document.Options.MaximumObjectDepth)
            throw new PdfLimitException("Color-space nesting exceeds the configured limit.");

        PdfObject resolved = value.Resolve(document);
        if (resolved is PdfName name)
        {
            PdfColorSpaceDefinition? device = ParseDeviceName(name.Value);
            if (device is not null)
                return device;
            PdfDictionary? colorSpaces = resources?
                .GetValueOrNull("ColorSpace")
                .AsDictionary(document);
            PdfObject? named = colorSpaces?.GetValueOrNull(name.Value);
            return named is null || ReferenceEquals(named, value)
                ? null
                : Parse(named, resources, document, depth + 1);
        }

        if (resolved is not PdfArray { Count: > 0 } array)
            return null;
        string? family = array[0].AsName(document);
        return family switch
        {
            "CalGray" when array.Count >= 2 =>
                ParseCalGray(array[1].AsDictionary(document), document),
            "CalRGB" when array.Count >= 2 =>
                ParseCalRgb(array[1].AsDictionary(document), document),
            "Lab" when array.Count >= 2 =>
                ParseLab(array[1].AsDictionary(document), document),
            "ICCBased" when array.Count >= 2 =>
                ParseIcc(array[1].AsStream(document), resources, document, depth),
            "Indexed" or "I" when array.Count >= 4 =>
                ParseIndexed(array, resources, document, depth),
            "Separation" when array.Count >= 4 =>
                ParseSpecial(array, resources, document, depth, separation: true),
            "DeviceN" when array.Count >= 4 =>
                ParseSpecial(array, resources, document, depth, separation: false),
            "Pattern" when array.Count >= 2 =>
                Parse(array[1], resources, document, depth + 1),
            _ => ParseDeviceName(family)
        };
    }

    public double[] DefaultDecode()
    {
        var result = new double[Components * 2];
        for (int component = 0; component < Components; component++)
        {
            (double minimum, double maximum) = Kind switch
            {
                PdfColorSpace.Lab when component == 0 => (0, 100),
                PdfColorSpace.Lab when _labRange is { Length: >= 4 } =>
                    (_labRange[(component - 1) * 2], _labRange[(component - 1) * 2 + 1]),
                PdfColorSpace.Indexed => (0, _highValue),
                _ => (0, 1)
            };
            result[component * 2] = minimum;
            result[component * 2 + 1] = maximum;
        }

        return result;
    }

    public PdfColor Convert(ReadOnlySpan<double> components)
    {
        if (components.Length < Components)
            throw new ArgumentException("Color has too few components.", nameof(components));
        return Kind switch
        {
            PdfColorSpace.DeviceGray => PdfColor.Gray(components[0]),
            PdfColorSpace.DeviceRgb => PdfColor.Rgb(
                components[0],
                components[1],
                components[2]),
            PdfColorSpace.DeviceCmyk => PdfColor.Cmyk(
                components[0],
                components[1],
                components[2],
                components[3]),
            PdfColorSpace.CalGray => ConvertCalGray(components[0]),
            PdfColorSpace.CalRgb => ConvertCalRgb(components),
            PdfColorSpace.Lab => ConvertLab(components),
            PdfColorSpace.IccBased => ConvertIcc(components),
            PdfColorSpace.Indexed => ConvertIndexed(components[0]),
            PdfColorSpace.Separation or PdfColorSpace.DeviceN => ConvertTint(components),
            _ => PdfColor.Black
        };
    }

    private static PdfColorSpaceDefinition? ParseDeviceName(string? name) => name switch
    {
        "DeviceGray" or "G" => DeviceGray,
        "DeviceRGB" or "RGB" => DeviceRgb,
        "DeviceCMYK" or "CMYK" => DeviceCmyk,
        _ => null
    };

    private static PdfColorSpaceDefinition? ParseCalGray(
        PdfDictionary? dictionary,
        PdfDocumentCore document)
    {
        if (dictionary is null ||
            ReadVector(dictionary.GetValueOrNull("WhitePoint"), document, 3) is not { } white)
        {
            return null;
        }

        return new PdfColorSpaceDefinition(
            PdfColorSpace.CalGray,
            "CalGray",
            1,
            white,
            ReadVector(dictionary.GetValueOrNull("BlackPoint"), document, 3) ??
            new[] { 0d, 0d, 0d },
            new[] { dictionary.GetValueOrNull("Gamma").AsNumber(document) ?? 1 });
    }

    private static PdfColorSpaceDefinition? ParseCalRgb(
        PdfDictionary? dictionary,
        PdfDocumentCore document)
    {
        if (dictionary is null ||
            ReadVector(dictionary.GetValueOrNull("WhitePoint"), document, 3) is not { } white)
        {
            return null;
        }

        return new PdfColorSpaceDefinition(
            PdfColorSpace.CalRgb,
            "CalRGB",
            3,
            white,
            ReadVector(dictionary.GetValueOrNull("BlackPoint"), document, 3) ??
            new[] { 0d, 0d, 0d },
            ReadVector(dictionary.GetValueOrNull("Gamma"), document, 3) ??
            new[] { 1d, 1d, 1d },
            ReadVector(dictionary.GetValueOrNull("Matrix"), document, 9) ??
            new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d });
    }

    private static PdfColorSpaceDefinition? ParseLab(
        PdfDictionary? dictionary,
        PdfDocumentCore document)
    {
        if (dictionary is null ||
            ReadVector(dictionary.GetValueOrNull("WhitePoint"), document, 3) is not { } white)
        {
            return null;
        }

        return new PdfColorSpaceDefinition(
            PdfColorSpace.Lab,
            "Lab",
            3,
            white,
            ReadVector(dictionary.GetValueOrNull("BlackPoint"), document, 3) ??
            new[] { 0d, 0d, 0d },
            labRange: ReadVector(dictionary.GetValueOrNull("Range"), document, 4) ??
                      new[] { -100d, 100d, -100d, 100d });
    }

    private static PdfColorSpaceDefinition? ParseIcc(
        PdfStream? stream,
        PdfDictionary? resources,
        PdfDocumentCore document,
        int depth)
    {
        if (stream is null)
            return null;
        int components = stream.Dictionary.GetValueOrNull("N").AsInteger(document) ?? 0;
        if (components is < 1 or > 4)
            return null;
        PdfColorSpaceDefinition alternate =
            Parse(
                stream.Dictionary.GetValueOrNull("Alternate"),
                resources,
                document,
                depth + 1) ??
            components switch
            {
                1 => DeviceGray,
                3 => DeviceRgb,
                4 => DeviceCmyk,
                _ => DeviceRgb
            };
        byte[] profile = document.Decode(stream);
        if (profile.Length > document.Options.MaximumIccProfileBytes)
            throw new PdfLimitException("ICC profile exceeds the configured limit.");
        PdfIccProfile? parsed = PdfIccProfile.TryParse(profile, components);
        return new PdfColorSpaceDefinition(
            PdfColorSpace.IccBased,
            parsed?.Description is { Length: > 0 } description
                ? $"ICCBased ({description})"
                : "ICCBased",
            components,
            alternate: alternate,
            iccProfile: parsed);
    }

    private static PdfColorSpaceDefinition? ParseIndexed(
        PdfArray array,
        PdfDictionary? resources,
        PdfDocumentCore document,
        int depth)
    {
        PdfColorSpaceDefinition? basis = Parse(array[1], resources, document, depth + 1);
        int high = array[2].AsInteger(document) ?? -1;
        if (basis is null || high is < 0 or > 255)
            return null;
        byte[]? lookup = array[3].Resolve(document) switch
        {
            PdfString text => text.Bytes.ToArray(),
            PdfStream stream => document.Decode(stream),
            _ => null
        };
        if (lookup is null)
            return null;
        int expected = checked((high + 1) * basis.Components);
        if (lookup.Length < expected)
            return null;
        if (lookup.Length != expected)
            lookup = lookup.AsSpan(0, expected).ToArray();
        return new PdfColorSpaceDefinition(
            PdfColorSpace.Indexed,
            $"Indexed/{basis.Name}",
            1,
            alternate: basis,
            lookup: lookup,
            highValue: high);
    }

    private static PdfColorSpaceDefinition? ParseSpecial(
        PdfArray array,
        PdfDictionary? resources,
        PdfDocumentCore document,
        int depth,
        bool separation)
    {
        int components;
        string name;
        if (separation)
        {
            components = 1;
            name = array[1].AsName(document) ?? "Unknown";
        }
        else
        {
            PdfArray? names = array[1].AsArray(document);
            if (names is null ||
                names.Count == 0 ||
                names.Count > document.Options.MaximumImageComponents)
            {
                return null;
            }

            components = names.Count;
            name = string.Join(
                ",",
                names.Select(item => item.AsName(document) ?? "Unknown"));
        }

        PdfColorSpaceDefinition? alternate = Parse(
            array[2],
            resources,
            document,
            depth + 1);
        if (alternate is null)
            return null;
        PdfFunction? function = PdfFunction.Create(
            array[3],
            document,
            components,
            alternate.Components,
            depth + 1);
        return function is null
            ? null
            : new PdfColorSpaceDefinition(
                separation ? PdfColorSpace.Separation : PdfColorSpace.DeviceN,
                $"{(separation ? "Separation" : "DeviceN")} ({name})",
                components,
                alternate: alternate,
                tintTransform: function);
    }

    private PdfColor ConvertCalGray(double value)
    {
        double gamma = _gamma?[0] ?? 1;
        double adjusted = Math.Pow(ClampUnit(value), gamma);
        double[] white = _whitePoint ?? PdfColorMath.D65;
        return PdfColorMath.XyzToColor(
            adjusted * white[0],
            adjusted * white[1],
            adjusted * white[2],
            white);
    }

    private PdfColor ConvertCalRgb(ReadOnlySpan<double> components)
    {
        double a = Math.Pow(ClampUnit(components[0]), _gamma?[0] ?? 1);
        double b = Math.Pow(ClampUnit(components[1]), _gamma?[1] ?? 1);
        double c = Math.Pow(ClampUnit(components[2]), _gamma?[2] ?? 1);
        double[] matrix = _matrix!;
        double x = matrix[0] * a + matrix[3] * b + matrix[6] * c;
        double y = matrix[1] * a + matrix[4] * b + matrix[7] * c;
        double z = matrix[2] * a + matrix[5] * b + matrix[8] * c;
        return PdfColorMath.XyzToColor(x, y, z, _whitePoint!);
    }

    private PdfColor ConvertLab(ReadOnlySpan<double> components)
    {
        double l = Math.Clamp(components[0], 0, 100);
        double a = Math.Clamp(components[1], _labRange![0], _labRange[1]);
        double b = Math.Clamp(components[2], _labRange[2], _labRange[3]);
        double fy = (l + 16) / 116;
        double fx = fy + a / 500;
        double fz = fy - b / 200;
        double[] white = _whitePoint!;
        double x = white[0] * LabInverse(fx);
        double y = white[1] * LabInverse(fy);
        double z = white[2] * LabInverse(fz);
        return PdfColorMath.XyzToColor(x, y, z, white);
    }

    private PdfColor ConvertIcc(ReadOnlySpan<double> components) =>
        _iccProfile?.Convert(components) ?? _alternate!.Convert(components);

    private PdfColor ConvertIndexed(double component)
    {
        int index = Math.Clamp((int)Math.Round(component), 0, _highValue);
        var values = new double[_alternate!.Components];
        int start = checked(index * values.Length);
        for (int item = 0; item < values.Length; item++)
            values[item] = _lookup![start + item] / 255d;
        return _alternate.Convert(values);
    }

    private PdfColor ConvertTint(ReadOnlySpan<double> components)
    {
        double[] transformed = _tintTransform!.Evaluate(components, _alternate!.Components);
        return _alternate.Convert(transformed);
    }

    private static double LabInverse(double value)
    {
        const double delta = 6d / 29;
        return value > delta
            ? value * value * value
            : 3 * delta * delta * (value - 4d / 29);
    }

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static double[]? ReadVector(
        PdfObject? value,
        PdfDocumentCore document,
        int count)
    {
        double[]? numbers = PdfFunction.ReadNumbers(value, document);
        return numbers is { Length: var length } && length == count
            ? numbers
            : null;
    }
}

internal static class PdfColorMath
{
    public static double[] D50 { get; } = { 0.9642, 1, 0.8249 };
    public static double[] D65 { get; } = { 0.95047, 1, 1.08883 };

    public static PdfColor XyzToColor(
        double x,
        double y,
        double z,
        IReadOnlyList<double> sourceWhite)
    {
        (x, y, z) = AdaptBradford(x, y, z, sourceWhite, D65);
        double red = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double green = -0.969266 * x + 1.8760108 * y + 0.041556 * z;
        double blue = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;
        return PdfColor.Rgb(EncodeSrgb(red), EncodeSrgb(green), EncodeSrgb(blue));
    }

    private static (double X, double Y, double Z) AdaptBradford(
        double x,
        double y,
        double z,
        IReadOnlyList<double> source,
        IReadOnlyList<double> target)
    {
        if (source.Count < 3 || target.Count < 3 || source[1] == 0)
            return (x, y, z);

        (double sl, double sm, double ss) = ToCone(source[0], source[1], source[2]);
        (double tl, double tm, double ts) = ToCone(target[0], target[1], target[2]);
        (double l, double m, double s) = ToCone(x, y, z);
        l *= sl == 0 ? 1 : tl / sl;
        m *= sm == 0 ? 1 : tm / sm;
        s *= ss == 0 ? 1 : ts / ss;
        return (
            0.9869929 * l - 0.1470543 * m + 0.1599627 * s,
            0.4323053 * l + 0.5183603 * m + 0.0492912 * s,
            -0.0085287 * l + 0.0400428 * m + 0.9684867 * s);
    }

    private static (double L, double M, double S) ToCone(double x, double y, double z) =>
        (
            0.8951 * x + 0.2664 * y - 0.1614 * z,
            -0.7502 * x + 1.7135 * y + 0.0367 * z,
            0.0389 * x - 0.0685 * y + 1.0296 * z);

    private static double EncodeSrgb(double value) =>
        value <= 0.0031308
            ? Math.Clamp(12.92 * value, 0, 1)
            : Math.Clamp(1.055 * Math.Pow(Math.Max(0, value), 1 / 2.4) - 0.055, 0, 1);
}
