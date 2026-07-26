namespace Poppler.Rendering;

internal static class PdfBlend
{
    public static RasterColor Composite(
        RasterColor backdrop,
        RasterColor source,
        string mode)
    {
        double sourceAlpha = RasterColor.Clamp(source.Alpha);
        double backdropAlpha = RasterColor.Clamp(backdrop.Alpha);
        double resultAlpha =
            sourceAlpha + backdropAlpha * (1 - sourceAlpha);
        if (resultAlpha <= 0)
            return RasterColor.Transparent;

        (double blendRed, double blendGreen, double blendBlue) =
            Blend(backdrop, source, mode);
        double red = CompositeChannel(
            backdrop.Red,
            source.Red,
            blendRed,
            backdropAlpha,
            sourceAlpha,
            resultAlpha);
        double green = CompositeChannel(
            backdrop.Green,
            source.Green,
            blendGreen,
            backdropAlpha,
            sourceAlpha,
            resultAlpha);
        double blue = CompositeChannel(
            backdrop.Blue,
            source.Blue,
            blendBlue,
            backdropAlpha,
            sourceAlpha,
            resultAlpha);
        return new RasterColor(red, green, blue, resultAlpha);
    }

    private static double CompositeChannel(
        double backdrop,
        double source,
        double blended,
        double backdropAlpha,
        double sourceAlpha,
        double resultAlpha)
    {
        double premultiplied =
            (1 - sourceAlpha) * backdrop * backdropAlpha +
            (1 - backdropAlpha) * source * sourceAlpha +
            sourceAlpha * backdropAlpha * blended;
        return RasterColor.Clamp(premultiplied / resultAlpha);
    }

    private static (double Red, double Green, double Blue) Blend(
        RasterColor backdrop,
        RasterColor source,
        string mode)
    {
        string normalized = mode.StartsWith("/", StringComparison.Ordinal)
            ? mode[1..]
            : mode;
        return normalized switch
        {
            "Multiply" => Channels(backdrop, source, Multiply),
            "Screen" => Channels(backdrop, source, Screen),
            "Overlay" => Channels(backdrop, source, Overlay),
            "Darken" => Channels(backdrop, source, Math.Min),
            "Lighten" => Channels(backdrop, source, Math.Max),
            "ColorDodge" => Channels(backdrop, source, ColorDodge),
            "ColorBurn" => Channels(backdrop, source, ColorBurn),
            "HardLight" => Channels(backdrop, source, HardLight),
            "SoftLight" => Channels(backdrop, source, SoftLight),
            "Difference" => Channels(backdrop, source, Difference),
            "Exclusion" => Channels(backdrop, source, Exclusion),
            "Hue" => SetLum(SetSat(
                (source.Red, source.Green, source.Blue),
                Saturation(backdrop)), Luminosity(backdrop)),
            "Saturation" => SetLum(SetSat(
                (backdrop.Red, backdrop.Green, backdrop.Blue),
                Saturation(source)), Luminosity(backdrop)),
            "Color" => SetLum(
                (source.Red, source.Green, source.Blue),
                Luminosity(backdrop)),
            "Luminosity" => SetLum(
                (backdrop.Red, backdrop.Green, backdrop.Blue),
                Luminosity(source)),
            _ => (source.Red, source.Green, source.Blue)
        };
    }

    private static (double, double, double) Channels(
        RasterColor backdrop,
        RasterColor source,
        Func<double, double, double> function) =>
        (
            function(backdrop.Red, source.Red),
            function(backdrop.Green, source.Green),
            function(backdrop.Blue, source.Blue));

    private static double Multiply(double backdrop, double source) =>
        backdrop * source;

    private static double Screen(double backdrop, double source) =>
        backdrop + source - backdrop * source;

    private static double Overlay(double backdrop, double source) =>
        HardLight(source, backdrop);

    private static double ColorDodge(double backdrop, double source) =>
        source >= 1 ? 1 : Math.Min(1, backdrop / (1 - source));

    private static double ColorBurn(double backdrop, double source) =>
        source <= 0 ? 0 : 1 - Math.Min(1, (1 - backdrop) / source);

    private static double HardLight(double backdrop, double source) =>
        source <= 0.5
            ? 2 * backdrop * source
            : Screen(backdrop, 2 * source - 1);

    private static double SoftLight(double backdrop, double source)
    {
        if (source <= 0.5)
            return backdrop - (1 - 2 * source) * backdrop * (1 - backdrop);
        double d = backdrop <= 0.25
            ? ((16 * backdrop - 12) * backdrop + 4) * backdrop
            : Math.Sqrt(backdrop);
        return backdrop + (2 * source - 1) * (d - backdrop);
    }

    private static double Difference(double backdrop, double source) =>
        Math.Abs(backdrop - source);

    private static double Exclusion(double backdrop, double source) =>
        backdrop + source - 2 * backdrop * source;

    private static double Luminosity(RasterColor color) =>
        Luminosity((color.Red, color.Green, color.Blue));

    private static double Luminosity((double Red, double Green, double Blue) color) =>
        0.3 * color.Red + 0.59 * color.Green + 0.11 * color.Blue;

    private static double Saturation(RasterColor color) =>
        Math.Max(color.Red, Math.Max(color.Green, color.Blue)) -
        Math.Min(color.Red, Math.Min(color.Green, color.Blue));

    private static (double Red, double Green, double Blue) SetLum(
        (double Red, double Green, double Blue) color,
        double luminosity)
    {
        double delta = luminosity - Luminosity(color);
        return ClipColor((
            color.Red + delta,
            color.Green + delta,
            color.Blue + delta));
    }

    private static (double Red, double Green, double Blue) ClipColor(
        (double Red, double Green, double Blue) color)
    {
        double luminosity = Luminosity(color);
        double minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        double maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        if (minimum < 0)
        {
            color = (
                luminosity + (color.Red - luminosity) * luminosity / (luminosity - minimum),
                luminosity + (color.Green - luminosity) * luminosity / (luminosity - minimum),
                luminosity + (color.Blue - luminosity) * luminosity / (luminosity - minimum));
        }

        if (maximum > 1)
        {
            color = (
                luminosity + (color.Red - luminosity) * (1 - luminosity) / (maximum - luminosity),
                luminosity + (color.Green - luminosity) * (1 - luminosity) / (maximum - luminosity),
                luminosity + (color.Blue - luminosity) * (1 - luminosity) / (maximum - luminosity));
        }

        return (
            RasterColor.Clamp(color.Red),
            RasterColor.Clamp(color.Green),
            RasterColor.Clamp(color.Blue));
    }

    private static (double Red, double Green, double Blue) SetSat(
        (double Red, double Green, double Blue) color,
        double saturation)
    {
        double[] channels = { color.Red, color.Green, color.Blue };
        int[] order = { 0, 1, 2 };
        Array.Sort(order, (left, right) => channels[left].CompareTo(channels[right]));
        double minimum = channels[order[0]];
        double maximum = channels[order[2]];
        channels[order[1]] = maximum > minimum
            ? (channels[order[1]] - minimum) * saturation / (maximum - minimum)
            : 0;
        channels[order[2]] = maximum > minimum ? saturation : 0;
        channels[order[0]] = 0;
        return (channels[0], channels[1], channels[2]);
    }
}
