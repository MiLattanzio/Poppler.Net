namespace Poppler.Text;

internal readonly record struct PdfMatrix(double A, double B, double C, double D, double E, double F)
{
    public static PdfMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public (double X, double Y) Transform(double x, double y) =>
        (A * x + C * y + E, B * x + D * y + F);

    public PdfMatrix Multiply(PdfMatrix other) => new(
        A * other.A + B * other.C,
        A * other.B + B * other.D,
        C * other.A + D * other.C,
        C * other.B + D * other.D,
        E * other.A + F * other.C + other.E,
        E * other.B + F * other.D + other.F);

    public PdfMatrix Translate(double x, double y) =>
        new PdfMatrix(1, 0, 0, 1, x, y).Multiply(this);
}
