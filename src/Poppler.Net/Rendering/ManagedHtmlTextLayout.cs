using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Poppler.Text;

namespace Poppler.Rendering;

/// <summary>
/// Reconstructs PDF glyphs as fixed-layout HTML while retaining graphical text
/// whenever moving it above the SVG background would change PDF paint order.
/// </summary>
internal static class ManagedHtmlTextLayout
{
    private const int MaximumGeneratedFontGlyphs = 32_768;

    public static ManagedHtmlPageLayout Create(
        Page page,
        HtmlRenderOptions options,
        HtmlCssCatalog styles)
    {
        IReadOnlyList<PdfGraphicsElement> graphics =
            page.GraphicsFor(options.OptionalContentVisibility);
        var located = new List<LocatedText>();
        CollectText(graphics, insideTransparencyGroup: false, located);

        var background = new HashSet<PdfTextElement>(
            ReferenceEqualityComparer.Instance);
        foreach (LocatedText item in located)
        {
            if (item.InsideTransparencyGroup || NeedsGraphicalBackground(item.Element))
                background.Add(item.Element);
        }
        MarkCoveredText(graphics, page.CropBox, options, background);

        (IReadOnlyList<ManagedHtmlWebFont> fonts,
            IReadOnlyDictionary<PdfFontDecoder, ManagedFontBinding> bindings) =
            BuildFonts(located, background, options);

        var runs = new List<ManagedHtmlTextRun>(located.Count);
        PdfRectangle crop = page.CropBox;
        double cropLeft = Math.Min(crop.Left, crop.Right);
        double cropTop = Math.Max(crop.Bottom, crop.Top);
        foreach (LocatedText item in located)
        {
            PdfTextElement element = item.Element;
            bool visible = HasVisualPaint(element) && !background.Contains(element);
            string normalizedName = NormalizeFontName(element.FontName);
            bindings.TryGetValue(element.Font, out ManagedFontBinding? binding);
            string fallbackFontClass = styles.ClassFor(
                "f",
                $"font-family:'{CssString(normalizedName)}',{FallbackFamily(normalizedName)}");
            string? managedFontClass = binding is null
                ? null
                : styles.ClassFor("f", $"font-family:'{binding.Font.Alias}'");
            string? paintClass = visible
                ? styles.ClassFor("p", PaintDeclaration(element, options.Foreground))
                : null;
            var glyphs = new List<ManagedHtmlGlyph>(element.Glyphs.Count);
            foreach (PdfTextGlyphPlacement placement in element.Glyphs)
            {
                string actualText = SafeHtmlText(placement.Glyph.Text);
                if (actualText.Length == 0)
                    continue;

                PdfMatrix matrix = placement.Transform;
                PdfPoint origin = matrix.Transform(0, 0);
                if (!matrix.IsFinite ||
                    !double.IsFinite(origin.X) ||
                    !double.IsFinite(origin.Y))
                {
                    continue;
                }

                string matrixClass = styles.ClassFor(
                    "m",
                    "transform:matrix(" +
                    $"{Format(matrix.A * options.Scale)}," +
                    $"{Format(-matrix.B * options.Scale)}," +
                    $"{Format(-matrix.C * options.Scale)}," +
                    $"{Format(matrix.D * options.Scale)},0,0)");
                string? visibleText = null;
                string fontClass = fallbackFontClass;
                if (visible)
                {
                    PdfFontGlyphKey key = PdfFontGlyphKey.From(placement.Glyph);
                    if (binding is not null &&
                        binding.Mapping.TryGetValue(key, out Rune mapped))
                    {
                        visibleText = mapped.ToString();
                        fontClass = managedFontClass!;
                    }
                    else
                    {
                        visibleText = actualText;
                    }
                }

                glyphs.Add(new ManagedHtmlGlyph(
                    actualText,
                    visibleText,
                    normalizedName,
                    fontClass,
                    matrixClass,
                    paintClass,
                    (origin.X - cropLeft) * options.Scale,
                    (cropTop - origin.Y) * options.Scale));
            }

            if (glyphs.Count > 0)
            {
                runs.Add(new ManagedHtmlTextRun(
                    SafeHtmlText(element.Text),
                    normalizedName,
                    background.Contains(element),
                    glyphs));
            }
        }

        return new ManagedHtmlPageLayout(runs, background, fonts);
    }

    private static void CollectText(
        IEnumerable<PdfGraphicsElement> elements,
        bool insideTransparencyGroup,
        List<LocatedText> output)
    {
        foreach (PdfGraphicsElement element in elements)
        {
            switch (element)
            {
                case PdfTextElement text:
                    output.Add(new LocatedText(text, insideTransparencyGroup));
                    break;
                case PdfTransparencyGroupElement group:
                    CollectText(group.Elements, insideTransparencyGroup: true, output);
                    break;
            }
        }
    }

    private static bool NeedsGraphicalBackground(PdfTextElement element)
    {
        if (!HasVisualPaint(element))
            return false;
        if (element.Font.IsType3 ||
            element.ClipPaths.Count > 0 ||
            element.State.SoftMask is not null ||
            !string.Equals(element.State.BlendMode, "Normal", StringComparison.Ordinal))
        {
            return true;
        }

        bool fills = Fills(element.RenderingMode);
        bool strokes = Strokes(element.RenderingMode);
        return (fills && element.State.Fill is not PdfSolidBrush) ||
               (strokes && element.State.Stroke is not PdfSolidBrush);
    }

    private static void MarkCoveredText(
        IReadOnlyList<PdfGraphicsElement> graphics,
        PdfRectangle crop,
        HtmlRenderOptions options,
        HashSet<PdfTextElement> background)
    {
        var laterGraphicalBounds = new List<RasterBounds>();
        for (int index = graphics.Count - 1; index >= 0; index--)
        {
            PdfGraphicsElement element = graphics[index];
            if (element is PdfTextElement text)
            {
                RasterBounds bounds = BoundsOfText(text);
                if (!background.Contains(text) &&
                    HasVisualPaint(text) &&
                    laterGraphicalBounds.Any(value => Overlaps(bounds, value)))
                {
                    background.Add(text);
                }

                if (background.Contains(text) &&
                    HasVisualPaint(text) &&
                    !text.Font.IsType3 &&
                    !bounds.IsEmpty)
                {
                    laterGraphicalBounds.Add(bounds);
                }
                continue;
            }

            if (!ContributesToBackground(element, options))
                continue;
            RasterBounds graphicalBounds = BoundsOfElement(element, crop);
            if (!graphicalBounds.IsEmpty)
                laterGraphicalBounds.Add(graphicalBounds);
        }
    }

    private static bool ContributesToBackground(
        PdfGraphicsElement element,
        HtmlRenderOptions options) => element switch
    {
        PdfImageElement => options.IncludeVectorGraphics && options.IncludeImages,
        PdfPathElement path =>
            options.IncludeVectorGraphics && path.PaintMode != PdfPaintMode.None,
        PdfShadingElement or PdfFunctionShadingElement or PdfMeshShadingElement =>
            options.IncludeVectorGraphics,
        PdfTransparencyGroupElement group => group.Elements.Any(child =>
            ContributesToBackground(child, options) || child is PdfTextElement),
        _ => false
    };

    private static (
        IReadOnlyList<ManagedHtmlWebFont> Fonts,
        IReadOnlyDictionary<PdfFontDecoder, ManagedFontBinding> Bindings)
        BuildFonts(
            IReadOnlyList<LocatedText> located,
            HashSet<PdfTextElement> background,
            HtmlRenderOptions options)
    {
        var bindings = new Dictionary<PdfFontDecoder, ManagedFontBinding>(
            ReferenceEqualityComparer.Instance);
        var fonts = new Dictionary<string, ManagedHtmlWebFont>(StringComparer.Ordinal);
        if (!options.EmbedFonts)
            return (Array.Empty<ManagedHtmlWebFont>(), bindings);

        foreach (IGrouping<PdfFontDecoder, LocatedText> group in located
                     .Where(value =>
                         !background.Contains(value.Element) &&
                         HasVisualPaint(value.Element) &&
                         !value.Element.Font.IsType3)
                     .GroupBy<LocatedText, PdfFontDecoder>(
                         value => value.Element.Font,
                         ReferenceEqualityComparer.Instance))
        {
            var unique = new Dictionary<PdfFontGlyphKey, ManagedFontGlyph>();
            foreach (PdfTextGlyphPlacement placement in group
                         .SelectMany(value => value.Element.Glyphs))
            {
                if (unique.Count >= MaximumGeneratedFontGlyphs)
                    break;
                PdfFontGlyphKey key = PdfFontGlyphKey.From(placement.Glyph);
                if (unique.ContainsKey(key) ||
                    !group.Key.TryGetGlyphOutline(
                        placement.Glyph,
                        out PdfGraphicsPath outline,
                        out double advance,
                        out _,
                        out _) ||
                    outline.IsEmpty)
                {
                    continue;
                }
                unique.Add(key, new ManagedFontGlyph(key, outline, advance));
            }
            if (unique.Count == 0)
                continue;

            ManagedFontResult generated;
            try
            {
                generated = ManagedTrueTypeFontBuilder.Build(
                    NormalizeFontName(group.Key.Info.Name),
                    unique.Values.ToArray());
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    InvalidOperationException or
                    OverflowException)
            {
                continue;
            }

            string hash = Convert.ToHexString(SHA256.HashData(generated.Data))
                .ToLowerInvariant();
            var font = new ManagedHtmlWebFont(
                hash,
                $"pdf-managed-{hash[..16]}",
                $"fonts/{hash[..24]}.ttf",
                "font/ttf",
                generated.Data);
            fonts.TryAdd(hash, font);
            bindings[group.Key] = new ManagedFontBinding(font, generated.Mapping);
        }
        return (
            fonts.Values.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray(),
            bindings);
    }

    private static RasterBounds BoundsOfElement(
        PdfGraphicsElement element,
        PdfRectangle crop) => element switch
    {
        PdfPathElement path => Expand(
            BoundsOfPath(path.Path, path.State.Transform),
            Math.Max(0, path.State.LineWidth) * MatrixScale(path.State.Transform)),
        PdfImageElement image => BoundsOfUnitSquare(image.State.Transform),
        PdfTextElement text => BoundsOfText(text),
        PdfTransparencyGroupElement group => group.Elements.Aggregate(
            RasterBounds.Empty,
            (current, child) => Union(current, BoundsOfElement(child, crop))),
        PdfShadingElement or PdfFunctionShadingElement or PdfMeshShadingElement =>
            BoundsOfRectangle(crop, PdfMatrix.Identity),
        _ => RasterBounds.Empty
    };

    private static RasterBounds BoundsOfText(PdfTextElement element)
    {
        RasterBounds bounds = RasterBounds.Empty;
        foreach (PdfTextGlyphPlacement placement in element.Glyphs)
        {
            if (element.Font.TryGetGlyphOutline(
                    placement.Glyph,
                    out PdfGraphicsPath outline,
                    out _,
                    out _,
                    out _) &&
                !outline.IsEmpty)
            {
                bounds = Union(bounds, BoundsOfPath(outline, placement.Transform));
                continue;
            }

            double advance = element.Font.WritingMode == FontWritingMode.Vertical
                ? 1
                : Math.Max(0.01, placement.Glyph.AdvanceX / 1000.0);
            bounds = Union(bounds, BoundsOfRectangle(
                new PdfRectangle(0, element.Font.Descent, advance, element.Font.Ascent),
                placement.Transform));
        }
        return bounds;
    }

    private static RasterBounds BoundsOfPath(
        PdfGraphicsPath path,
        PdfMatrix transform)
    {
        if (!transform.IsFinite)
            return RasterBounds.Empty;
        RasterBounds bounds = RasterBounds.Empty;
        foreach (PdfPathSegment segment in path.Segments)
        {
            switch (segment)
            {
                case PdfMoveTo move:
                    bounds = Include(bounds, transform.Transform(move.Point.X, move.Point.Y));
                    break;
                case PdfLineTo line:
                    bounds = Include(bounds, transform.Transform(line.Point.X, line.Point.Y));
                    break;
                case PdfCubicBezierTo curve:
                    bounds = Include(bounds, transform.Transform(
                        curve.Control1.X, curve.Control1.Y));
                    bounds = Include(bounds, transform.Transform(
                        curve.Control2.X, curve.Control2.Y));
                    bounds = Include(bounds, transform.Transform(curve.End.X, curve.End.Y));
                    break;
            }
        }
        return bounds;
    }

    private static RasterBounds BoundsOfUnitSquare(PdfMatrix transform) =>
        BoundsOfRectangle(new PdfRectangle(0, 0, 1, 1), transform);

    private static RasterBounds BoundsOfRectangle(
        PdfRectangle rectangle,
        PdfMatrix transform)
    {
        RasterBounds bounds = RasterBounds.Empty;
        bounds = Include(bounds, transform.Transform(rectangle.Left, rectangle.Bottom));
        bounds = Include(bounds, transform.Transform(rectangle.Right, rectangle.Bottom));
        bounds = Include(bounds, transform.Transform(rectangle.Right, rectangle.Top));
        return Include(bounds, transform.Transform(rectangle.Left, rectangle.Top));
    }

    private static RasterBounds Include(RasterBounds bounds, PdfPoint point) =>
        !double.IsFinite(point.X) || !double.IsFinite(point.Y)
            ? bounds
            : bounds.Include(point);

    private static RasterBounds Union(RasterBounds first, RasterBounds second)
    {
        if (first.IsEmpty)
            return second;
        if (second.IsEmpty)
            return first;
        return new RasterBounds(
            Math.Min(first.Left, second.Left),
            Math.Min(first.Top, second.Top),
            Math.Max(first.Right, second.Right),
            Math.Max(first.Bottom, second.Bottom));
    }

    private static RasterBounds Expand(RasterBounds bounds, double amount) =>
        bounds.IsEmpty || !double.IsFinite(amount)
            ? bounds
            : new RasterBounds(
                bounds.Left - amount,
                bounds.Top - amount,
                bounds.Right + amount,
                bounds.Bottom + amount);

    private static bool Overlaps(RasterBounds first, RasterBounds second) =>
        !first.IsEmpty &&
        !second.IsEmpty &&
        first.Left < second.Right &&
        first.Right > second.Left &&
        first.Top < second.Bottom &&
        first.Bottom > second.Top;

    private static double MatrixScale(PdfMatrix matrix) => Math.Max(
        Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B),
        Math.Sqrt(matrix.C * matrix.C + matrix.D * matrix.D));

    private static bool HasVisualPaint(PdfTextElement element) =>
        element.RenderingMode is not (
            PdfTextRenderingMode.Invisible or PdfTextRenderingMode.Clip);

    private static bool Fills(PdfTextRenderingMode mode) => mode is
        PdfTextRenderingMode.Fill or
        PdfTextRenderingMode.FillAndStroke or
        PdfTextRenderingMode.FillAndClip or
        PdfTextRenderingMode.FillStrokeAndClip;

    private static bool Strokes(PdfTextRenderingMode mode) => mode is
        PdfTextRenderingMode.Stroke or
        PdfTextRenderingMode.FillAndStroke or
        PdfTextRenderingMode.StrokeAndClip or
        PdfTextRenderingMode.FillStrokeAndClip;

    private static string PaintDeclaration(
        PdfTextElement element,
        string fallbackColor)
    {
        var css = new StringBuilder();
        if (Fills(element.RenderingMode) && element.State.Fill is PdfSolidBrush fill)
        {
            css.Append("color:").Append(CssColor(fill.Color, element.State.FillAlpha))
                .Append(";-webkit-text-fill-color:currentColor");
        }
        else
        {
            css.Append("color:transparent;-webkit-text-fill-color:transparent");
        }
        if (Strokes(element.RenderingMode) && element.State.Stroke is PdfSolidBrush stroke)
        {
            double width = element.State.LineWidth /
                           Math.Max(0.001, Math.Abs(element.FontSize));
            css.Append(";-webkit-text-stroke:")
                .Append(Format(width)).Append("px ")
                .Append(CssColor(stroke.Color, element.State.StrokeAlpha));
        }
        if (css.Length == 0)
            css.Append("color:").Append(fallbackColor.Trim());
        return css.ToString();
    }

    private static string CssColor(PdfColor color, double alpha)
    {
        (double red, double green, double blue) = color.ToRgb();
        int r = (int)Math.Round(Math.Clamp(red, 0, 1) * 255);
        int g = (int)Math.Round(Math.Clamp(green, 0, 1) * 255);
        int b = (int)Math.Round(Math.Clamp(blue, 0, 1) * 255);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"rgba({r},{g},{b},{Math.Clamp(alpha, 0, 1):0.###})");
    }

    internal static string NormalizeFontName(string name)
    {
        string normalized = string.IsNullOrWhiteSpace(name) ? "sans-serif" : name.Trim();
        if (normalized.Length > 7 &&
            normalized[6] == '+' &&
            normalized.AsSpan(0, 6).ToString().All(character =>
                character is >= 'A' and <= 'Z'))
        {
            normalized = normalized[7..];
        }
        return normalized;
    }

    private static string FallbackFamily(string name)
    {
        string normalized = name.ToLowerInvariant();
        if (normalized.Contains("courier", StringComparison.Ordinal) ||
            normalized.Contains("mono", StringComparison.Ordinal))
            return "monospace";
        if (normalized.Contains("times", StringComparison.Ordinal) ||
            normalized.Contains("serif", StringComparison.Ordinal) ||
            normalized.Contains("garamond", StringComparison.Ordinal) ||
            normalized.Contains("minion", StringComparison.Ordinal))
            return "serif";
        return "sans-serif";
    }

    private static string CssString(string value) =>
        string.Concat(value
            .Where(character => !char.IsControl(character))
            .Select(character => character switch
            {
                '\\' => "\\\\",
                '\'' => "\\'",
                _ => character.ToString()
            }));

    private static string SafeHtmlText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var result = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                if (rune.Value is 9 or 10 or 13)
                    result.Append(' ');
                continue;
            }
            result.Append(rune.ToString());
        }
        return result.ToString();
    }

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private sealed record LocatedText(
        PdfTextElement Element,
        bool InsideTransparencyGroup);
}

internal sealed class HtmlCssCatalog
{
    private readonly Dictionary<string, string> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nextByPrefix = new(StringComparer.Ordinal);
    private readonly List<ManagedHtmlCssRule> _rules = [];

    public IReadOnlyList<ManagedHtmlCssRule> Rules => _rules;

    public string ClassFor(string prefix, string declarations)
    {
        string key = prefix + "\0" + declarations;
        if (_classes.TryGetValue(key, out string? name))
            return name;
        int next = _nextByPrefix.GetValueOrDefault(prefix);
        _nextByPrefix[prefix] = next + 1;
        name = prefix + next.ToString(CultureInfo.InvariantCulture);
        _classes.Add(key, name);
        _rules.Add(new ManagedHtmlCssRule(name, declarations));
        return name;
    }
}

internal sealed record ManagedHtmlPageLayout(
    IReadOnlyList<ManagedHtmlTextRun> Runs,
    IReadOnlySet<PdfTextElement> BackgroundText,
    IReadOnlyList<ManagedHtmlWebFont> Fonts);

internal sealed record ManagedHtmlTextRun(
    string Text,
    string FontName,
    bool UsesGraphicalBackground,
    IReadOnlyList<ManagedHtmlGlyph> Glyphs);

internal sealed record ManagedHtmlGlyph(
    string ActualText,
    string? VisibleText,
    string FontName,
    string FontClass,
    string MatrixClass,
    string? PaintClass,
    double Left,
    double Top);

internal sealed record ManagedHtmlWebFont(
    string Hash,
    string Alias,
    string Path,
    string MediaType,
    byte[] Data);

internal sealed record ManagedFontBinding(
    ManagedHtmlWebFont Font,
    IReadOnlyDictionary<PdfFontGlyphKey, Rune> Mapping);

internal sealed record ManagedHtmlCssRule(string ClassName, string Declarations);
