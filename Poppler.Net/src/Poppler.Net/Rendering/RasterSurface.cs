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

internal sealed class RasterSurface
{
    private readonly byte[] _pixels;

    public RasterSurface(int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new byte[checked(width * height * 4)];
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels => _pixels;

    public void Clear(RasterColor color)
    {
        byte red = ToByte(color.Red);
        byte green = ToByte(color.Green);
        byte blue = ToByte(color.Blue);
        byte alpha = ToByte(color.Alpha);
        for (int offset = 0; offset < _pixels.Length; offset += 4)
        {
            _pixels[offset] = red;
            _pixels[offset + 1] = green;
            _pixels[offset + 2] = blue;
            _pixels[offset + 3] = alpha;
        }
    }

    public RasterColor GetPixel(int x, int y)
    {
        int offset = checked((y * Width + x) * 4);
        return new RasterColor(
            _pixels[offset] / 255.0,
            _pixels[offset + 1] / 255.0,
            _pixels[offset + 2] / 255.0,
            _pixels[offset + 3] / 255.0);
    }

    public void CompositePixel(
        int x,
        int y,
        RasterColor source,
        string blendMode)
    {
        if ((uint)x >= (uint)Width ||
            (uint)y >= (uint)Height ||
            source.Alpha <= 0)
        {
            return;
        }

        int offset = checked((y * Width + x) * 4);
        var backdrop = new RasterColor(
            _pixels[offset] / 255.0,
            _pixels[offset + 1] / 255.0,
            _pixels[offset + 2] / 255.0,
            _pixels[offset + 3] / 255.0);
        RasterColor result = PdfBlend.Composite(backdrop, source, blendMode);
        _pixels[offset] = ToByte(result.Red);
        _pixels[offset + 1] = ToByte(result.Green);
        _pixels[offset + 2] = ToByte(result.Blue);
        _pixels[offset + 3] = ToByte(result.Alpha);
    }

    public void CompositeSurface(
        RasterSurface source,
        string blendMode,
        double opacity,
        Func<int, int, double>? mask = null)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("Surface dimensions must match.", nameof(source));
        opacity = RasterColor.Clamp(opacity);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                RasterColor color = source.GetPixel(x, y);
                double effective = color.Alpha * opacity * (mask?.Invoke(x, y) ?? 1);
                if (effective > 0)
                    CompositePixel(x, y, color.WithAlpha(effective), blendMode);
            }
        }
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(RasterColor.Clamp(value) * 255), 0, 255);
}
