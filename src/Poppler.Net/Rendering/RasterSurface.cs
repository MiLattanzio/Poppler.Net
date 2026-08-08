namespace Poppler.Rendering;

internal readonly record struct RasterColor(
    double Red,
    double Green,
    double Blue,
    double Alpha)
{
    public static RasterColor Transparent { get; } = new(0, 0, 0, 0);

    public static RasterColor FromPdf(PdfColor color, double alpha = 1)
    {
        (double red, double green, double blue) = color.ToRgb();
        return new RasterColor(
            Clamp(red),
            Clamp(green),
            Clamp(blue),
            Clamp(alpha));
    }

    public RasterColor WithAlpha(double alpha) =>
        this with { Alpha = Clamp(alpha) };

    public static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

internal readonly record struct PremultipliedRasterColor(
    double Red,
    double Green,
    double Blue,
    double Alpha)
{
    public static PremultipliedRasterColor Transparent { get; } = new(0, 0, 0, 0);

    public RasterColor ToStraight()
    {
        double alpha = RasterColor.Clamp(Alpha);
        if (alpha <= 1e-12)
            return RasterColor.Transparent;
        return new RasterColor(
            RasterColor.Clamp(Red / alpha),
            RasterColor.Clamp(Green / alpha),
            RasterColor.Clamp(Blue / alpha),
            alpha);
    }
}

/// <summary>
/// High-precision compositing surface. Color is stored premultiplied while
/// pixel alpha, accumulated PDF shape and group contribution alpha remain
/// independent. Quantization happens only when the final RGBA bitmap is made.
/// </summary>
internal sealed class RasterSurface : IDisposable
{
    private const int ComponentsPerPixel = 6;
    private const int PremultipliedRed = 0;
    private const int PremultipliedGreen = 1;
    private const int PremultipliedBlue = 2;
    private const int CompositeAlpha = 3;
    private const int Shape = 4;
    private const int ContributionAlpha = 5;

    private readonly RenderWorkingSetBudget _budget;
    private readonly long _reservedBytes;
    private readonly bool _quantizeComposite;
    private float[]? _components;

    public RasterSurface(
        int width,
        int height,
        RenderWorkingSetBudget budget,
        bool quantizeComposite = false)
    {
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(budget);

        int componentCount = checked(width * height * ComponentsPerPixel);
        _reservedBytes = checked((long)componentCount * sizeof(float));
        _budget = budget;
        _quantizeComposite = quantizeComposite;
        _budget.Reserve(_reservedBytes);
        try
        {
            _components = new float[componentCount];
        }
        catch
        {
            _budget.Release(_reservedBytes);
            throw;
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public RasterSurface CloneActual(bool quantizeComposite = false)
    {
        var clone = new RasterSurface(
            Width,
            Height,
            _budget,
            quantizeComposite);
        float[] source = Components;
        float[] target = clone.Components;
        for (int offset = 0; offset < source.Length; offset += ComponentsPerPixel)
        {
            target[offset + PremultipliedRed] = source[offset + PremultipliedRed];
            target[offset + PremultipliedGreen] = source[offset + PremultipliedGreen];
            target[offset + PremultipliedBlue] = source[offset + PremultipliedBlue];
            target[offset + CompositeAlpha] = source[offset + CompositeAlpha];
        }
        return clone;
    }

    public void Clear(RasterColor color)
    {
        double straightRed = RasterColor.Clamp(color.Red);
        double straightGreen = RasterColor.Clamp(color.Green);
        double straightBlue = RasterColor.Clamp(color.Blue);
        double actualAlpha = RasterColor.Clamp(color.Alpha);
        if (_quantizeComposite)
        {
            straightRed = Quantize(straightRed);
            straightGreen = Quantize(straightGreen);
            straightBlue = Quantize(straightBlue);
            actualAlpha = Quantize(actualAlpha);
        }
        float alpha = (float)actualAlpha;
        float red = (float)(straightRed * actualAlpha);
        float green = (float)(straightGreen * actualAlpha);
        float blue = (float)(straightBlue * actualAlpha);
        float[] components = Components;
        for (int offset = 0; offset < components.Length; offset += ComponentsPerPixel)
        {
            components[offset + PremultipliedRed] = red;
            components[offset + PremultipliedGreen] = green;
            components[offset + PremultipliedBlue] = blue;
            components[offset + CompositeAlpha] = alpha;
            components[offset + Shape] = 0;
            components[offset + ContributionAlpha] = 0;
        }
    }

    public RasterColor GetPixel(int x, int y) =>
        GetStraightPixel(Offset(x, y));

    public PremultipliedRasterColor GetPremultipliedPixel(int x, int y)
    {
        int offset = Offset(x, y);
        float[] components = Components;
        return new PremultipliedRasterColor(
            components[offset + PremultipliedRed],
            components[offset + PremultipliedGreen],
            components[offset + PremultipliedBlue],
            components[offset + CompositeAlpha]);
    }

    public double GetShape(int x, int y) =>
        Components[Offset(x, y) + Shape];

    public double GetContributionAlpha(int x, int y) =>
        Components[Offset(x, y) + ContributionAlpha];

    public void CompositePixel(
        int x,
        int y,
        RasterColor source,
        string blendMode,
        double sourceShape = 1,
        PdfColor? sourcePdfColor = null,
        bool overprint = false,
        int overprintMode = 0)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;

        double alpha = RasterColor.Clamp(source.Alpha);
        double shape = RasterColor.Clamp(sourceShape);
        if (alpha <= 0 && shape <= 0)
            return;

        int offset = Offset(x, y);
        RasterColor backdrop = GetStraightPixel(offset);
        if (overprint && overprintMode == 1 && sourcePdfColor.HasValue)
        {
            source = ApplyProcessOverprint(
                backdrop,
                source,
                sourcePdfColor.Value);
        }

        PremultipliedRasterColor result = alpha <= 0
            ? GetPremultipliedPixel(offset)
            : PdfBlend.Composite(backdrop, source, blendMode);
        SetPremultipliedPixel(offset, result);

        float[] components = Components;
        double previousShape = components[offset + Shape];
        double previousContributionAlpha = components[offset + ContributionAlpha];
        components[offset + Shape] = (float)Union(shape, previousShape);
        components[offset + ContributionAlpha] =
            (float)Union(alpha, previousContributionAlpha);
    }

    public void SetPixel(int x, int y, RasterColor color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;
        double alpha = RasterColor.Clamp(color.Alpha);
        SetPremultipliedPixel(
            Offset(x, y),
            new PremultipliedRasterColor(
                RasterColor.Clamp(color.Red) * alpha,
                RasterColor.Clamp(color.Green) * alpha,
                RasterColor.Clamp(color.Blue) * alpha,
                alpha));
    }

    /// <summary>
    /// Merges one child into a knockout group. The child was painted against
    /// the group's initial backdrop; its shape removes earlier siblings while
    /// retaining the backdrop contribution beneath partially covered pixels.
    /// </summary>
    public void MergeKnockoutChild(
        RasterSurface initialBackdrop,
        RasterSurface child)
    {
        EnsureMatching(initialBackdrop, nameof(initialBackdrop));
        EnsureMatching(child, nameof(child));
        float[] destination = Components;
        float[] initial = initialBackdrop.Components;
        float[] source = child.Components;
        for (int offset = 0; offset < destination.Length; offset += ComponentsPerPixel)
        {
            double childShape = RasterColor.Clamp(source[offset + Shape]);
            if (childShape <= 0)
                continue;
            double keep = 1 - childShape;
            double alpha = source[offset + CompositeAlpha] +
                keep * (destination[offset + CompositeAlpha] -
                        initial[offset + CompositeAlpha]);
            alpha = RasterColor.Clamp(alpha);
            destination[offset + CompositeAlpha] = (float)alpha;
            destination[offset + PremultipliedRed] = (float)ClampPremultiplied(
                source[offset + PremultipliedRed] +
                keep * (destination[offset + PremultipliedRed] -
                        initial[offset + PremultipliedRed]),
                alpha);
            destination[offset + PremultipliedGreen] = (float)ClampPremultiplied(
                source[offset + PremultipliedGreen] +
                keep * (destination[offset + PremultipliedGreen] -
                        initial[offset + PremultipliedGreen]),
                alpha);
            destination[offset + PremultipliedBlue] = (float)ClampPremultiplied(
                source[offset + PremultipliedBlue] +
                keep * (destination[offset + PremultipliedBlue] -
                        initial[offset + PremultipliedBlue]),
                alpha);
            destination[offset + ContributionAlpha] = (float)RasterColor.Clamp(
                source[offset + ContributionAlpha] +
                keep * destination[offset + ContributionAlpha]);
            destination[offset + Shape] = (float)Union(
                childShape,
                destination[offset + Shape]);
        }
    }

    public RasterColor RecoverGroupSource(
        RasterSurface initialBackdrop,
        int x,
        int y)
    {
        EnsureMatching(initialBackdrop, nameof(initialBackdrop));
        int offset = Offset(x, y);
        double alpha = RasterColor.Clamp(
            Components[offset + ContributionAlpha]);
        if (alpha <= 1e-12)
            return RasterColor.Transparent;

        float[] initial = initialBackdrop.Components;
        double keep = 1 - alpha;
        return new RasterColor(
            RecoverChannel(
                Components[offset + PremultipliedRed],
                initial[offset + PremultipliedRed],
                keep,
                alpha),
            RecoverChannel(
                Components[offset + PremultipliedGreen],
                initial[offset + PremultipliedGreen],
                keep,
                alpha),
            RecoverChannel(
                Components[offset + PremultipliedBlue],
                initial[offset + PremultipliedBlue],
                keep,
                alpha),
            alpha);
    }

    public byte[] ToRgbaBytes()
    {
        var result = new byte[checked(Width * Height * 4)];
        float[] components = Components;
        for (int source = 0, target = 0;
             source < components.Length;
             source += ComponentsPerPixel, target += 4)
        {
            double alpha = RasterColor.Clamp(components[source + CompositeAlpha]);
            if (alpha <= 1e-12)
            {
                result[target] = 0;
                result[target + 1] = 0;
                result[target + 2] = 0;
                result[target + 3] = 0;
                continue;
            }
            result[target] = ToByte(components[source + PremultipliedRed] / alpha);
            result[target + 1] = ToByte(components[source + PremultipliedGreen] / alpha);
            result[target + 2] = ToByte(components[source + PremultipliedBlue] / alpha);
            result[target + 3] = ToByte(alpha);
        }
        return result;
    }

    public void Dispose()
    {
        if (_components is null)
            return;
        _components = null;
        _budget.Release(_reservedBytes);
    }

    private float[] Components =>
        _components ?? throw new ObjectDisposedException(nameof(RasterSurface));

    private int Offset(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        return checked((y * Width + x) * ComponentsPerPixel);
    }

    private PremultipliedRasterColor GetPremultipliedPixel(int offset)
    {
        float[] components = Components;
        return new PremultipliedRasterColor(
            components[offset + PremultipliedRed],
            components[offset + PremultipliedGreen],
            components[offset + PremultipliedBlue],
            components[offset + CompositeAlpha]);
    }

    private RasterColor GetStraightPixel(int offset)
    {
        float[] components = Components;
        double alpha = RasterColor.Clamp(components[offset + CompositeAlpha]);
        if (alpha <= 1e-12)
            return RasterColor.Transparent;
        double red = RasterColor.Clamp(
            components[offset + PremultipliedRed] / alpha);
        double green = RasterColor.Clamp(
            components[offset + PremultipliedGreen] / alpha);
        double blue = RasterColor.Clamp(
            components[offset + PremultipliedBlue] / alpha);
        if (_quantizeComposite)
        {
            red = Quantize(red);
            green = Quantize(green);
            blue = Quantize(blue);
            alpha = Quantize(alpha);
        }
        return new RasterColor(red, green, blue, alpha);
    }

    private void SetPremultipliedPixel(
        int offset,
        PremultipliedRasterColor color)
    {
        float[] components = Components;
        double alpha = RasterColor.Clamp(color.Alpha);
        if (_quantizeComposite)
        {
            if (alpha <= 1e-12)
            {
                color = PremultipliedRasterColor.Transparent;
            }
            else
            {
                double originalAlpha = alpha;
                double red = Quantize(color.Red / originalAlpha);
                double green = Quantize(color.Green / originalAlpha);
                double blue = Quantize(color.Blue / originalAlpha);
                alpha = Quantize(originalAlpha);
                color = new PremultipliedRasterColor(
                    red * alpha,
                    green * alpha,
                    blue * alpha,
                    alpha);
            }
        }
        components[offset + PremultipliedRed] =
            (float)ClampPremultiplied(color.Red, alpha);
        components[offset + PremultipliedGreen] =
            (float)ClampPremultiplied(color.Green, alpha);
        components[offset + PremultipliedBlue] =
            (float)ClampPremultiplied(color.Blue, alpha);
        components[offset + CompositeAlpha] = (float)alpha;
    }

    private void EnsureMatching(RasterSurface other, string parameterName)
    {
        if (other.Width != Width || other.Height != Height)
        {
            throw new ArgumentException(
                "Surface dimensions must match.",
                parameterName);
        }
    }

    private static double RecoverChannel(
        double composite,
        double backdrop,
        double backdropAmount,
        double alpha) =>
        RasterColor.Clamp((composite - backdrop * backdropAmount) / alpha);

    private static double Union(double source, double backdrop) =>
        RasterColor.Clamp(source + backdrop * (1 - source));

    private static double ClampPremultiplied(double value, double alpha) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, alpha) : 0;

    private static RasterColor ApplyProcessOverprint(
        RasterColor backdrop,
        RasterColor source,
        PdfColor sourcePdfColor)
    {
        (double cyan, double magenta, double yellow, double black) =
            RgbToCmyk(backdrop);
        switch (sourcePdfColor.Space)
        {
            case PdfColorSpace.DeviceCmyk:
                if (sourcePdfColor.Component1 > 1e-12)
                    cyan = sourcePdfColor.Component1;
                if (sourcePdfColor.Component2 > 1e-12)
                    magenta = sourcePdfColor.Component2;
                if (sourcePdfColor.Component3 > 1e-12)
                    yellow = sourcePdfColor.Component3;
                if (sourcePdfColor.Component4 > 1e-12)
                    black = sourcePdfColor.Component4;
                break;
            case PdfColorSpace.DeviceGray:
            {
                double sourceBlack = 1 - sourcePdfColor.Component1;
                if (sourceBlack > 1e-12)
                    black = sourceBlack;
                break;
            }
            default:
                return source;
        }

        return RasterColor.FromPdf(
            PdfColor.Cmyk(cyan, magenta, yellow, black),
            source.Alpha);
    }

    private static (double Cyan, double Magenta, double Yellow, double Black)
        RgbToCmyk(RasterColor color)
    {
        double black = 1 - Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        if (black >= 1 - 1e-12)
            return (0, 0, 0, 1);
        double scale = 1 - black;
        return (
            RasterColor.Clamp((1 - color.Red - black) / scale),
            RasterColor.Clamp((1 - color.Green - black) / scale),
            RasterColor.Clamp((1 - color.Blue - black) / scale),
            RasterColor.Clamp(black));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(
            (int)Math.Round(RasterColor.Clamp(value) * 255),
            0,
            255);

    private static double Quantize(double value) =>
        ToByte(value) / 255.0;
}
