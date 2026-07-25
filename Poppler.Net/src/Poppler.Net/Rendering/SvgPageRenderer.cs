using System.Globalization;
using System.Security;
using System.Text;

namespace Poppler.Rendering;

internal static class SvgPageRenderer
{
    public static string Render(Page page, SvgRenderOptions options)
    {
        if (!double.IsFinite(options.Scale) || options.Scale <= 0 || options.Scale > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "Scale must be greater than 0 and at most 100.");

        PdfRectangle crop = page.CropBox;
        double width = crop.Width;
        double height = crop.Height;
        string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        string Escape(string value) => SecurityElement.Escape(value) ?? "";

        var svg = new StringBuilder();
        svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" ");
        svg.Append("aria-label=\"PDF page ");
        svg.Append(page.Number.ToString(CultureInfo.InvariantCulture));
        svg.Append("\" width=\"");
        svg.Append(Format(width * options.Scale));
        svg.Append("\" height=\"");
        svg.Append(Format(height * options.Scale));
        svg.Append("\" viewBox=\"0 0 ");
        svg.Append(Format(width));
        svg.Append(' ');
        svg.Append(Format(height));
        svg.AppendLine("\">");
        svg.Append("  <rect width=\"100%\" height=\"100%\" fill=\"");
        svg.Append(Escape(options.Background));
        svg.AppendLine("\"/>");
        svg.AppendLine("  <!-- Diagnostic managed renderer: text positions only; not visual-conformance output. -->");

        foreach (TextBox box in page.TextList())
        {
            if (string.IsNullOrEmpty(box.Text))
                continue;
            double x = box.BoundingBox.Left - Math.Min(crop.Left, crop.Right);
            double baseline = Math.Max(crop.Bottom, crop.Top) - box.BoundingBox.Bottom;
            string fontFamily = NormalizeFontFamily(box.FontName);
            svg.Append("  <text x=\"");
            svg.Append(Format(x));
            svg.Append("\" y=\"");
            svg.Append(Format(baseline));
            svg.Append("\" font-family=\"");
            svg.Append(Escape(fontFamily));
            svg.Append("\" font-size=\"");
            svg.Append(Format(Math.Abs(box.FontSize)));
            svg.Append("\" fill=\"");
            svg.Append(Escape(options.Foreground));
            svg.Append('"');
            if (box.Rotation != 0)
            {
                svg.Append(" transform=\"rotate(");
                svg.Append(Format(-box.Rotation));
                svg.Append(' ');
                svg.Append(Format(x));
                svg.Append(' ');
                svg.Append(Format(baseline));
                svg.Append(")\"");
            }

            svg.Append(" xml:space=\"preserve\">");
            svg.Append(Escape(box.Text));
            svg.AppendLine("</text>");

            if (options.DrawTextBounds)
            {
                double boxY = Math.Max(crop.Bottom, crop.Top) - box.BoundingBox.Top;
                svg.Append("  <rect x=\"");
                svg.Append(Format(x));
                svg.Append("\" y=\"");
                svg.Append(Format(boxY));
                svg.Append("\" width=\"");
                svg.Append(Format(box.BoundingBox.Width));
                svg.Append("\" height=\"");
                svg.Append(Format(box.BoundingBox.Height));
                svg.AppendLine("\" fill=\"none\" stroke=\"#e11d48\" stroke-width=\"0.5\"/>");
            }
        }

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static string NormalizeFontFamily(string fontName)
    {
        int plus = fontName.IndexOf('+');
        string normalized = plus >= 0 && plus + 1 < fontName.Length
            ? fontName[(plus + 1)..]
            : fontName;
        return normalized.Replace(',', ' ').Replace('"', ' ').Trim();
    }
}
