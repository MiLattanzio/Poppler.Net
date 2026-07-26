using System.Globalization;
using System.Security;
using System.Text;

namespace Poppler.Rendering;

internal static class SvgPageRenderer
{
    public static string Render(Page page, SvgRenderOptions options)
    {
        Validate(options);
        return new Writer(page, options).Render();
    }

    private static void Validate(SvgRenderOptions options)
    {
        if (!double.IsFinite(options.Scale) || options.Scale <= 0 || options.Scale > 100)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Scale must be greater than 0 and at most 100.");
    }

    private sealed class Writer
    {
        private readonly Page _page;
        private readonly SvgRenderOptions _options;
        private readonly PdfRectangle _crop;
        private readonly StringBuilder _svg = new();
        private readonly Dictionary<PdfClipPath, string> _clipIds =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<BrushKey, string> _brushIds = new();
        private readonly List<PdfClipPath> _clips = new();
        private readonly List<BrushKey> _brushes = new();
        private int _nextId;

        public Writer(Page page, SvgRenderOptions options)
        {
            _page = page;
            _options = options;
            _crop = page.CropBox;
        }

        public string Render()
        {
            IReadOnlyList<PdfGraphicsElement> graphics = _options.IncludeVectorGraphics
                ? _page.Graphics
                : Array.Empty<PdfGraphicsElement>();
            RegisterElements(graphics);

            _svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            _svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" ");
            _svg.Append("aria-label=\"PDF page ");
            _svg.Append(_page.Number.ToString(CultureInfo.InvariantCulture));
            _svg.Append("\" width=\"");
            _svg.Append(Format(_crop.Width * _options.Scale));
            _svg.Append("\" height=\"");
            _svg.Append(Format(_crop.Height * _options.Scale));
            _svg.Append("\" viewBox=\"0 0 ");
            _svg.Append(Format(_crop.Width));
            _svg.Append(' ');
            _svg.Append(Format(_crop.Height));
            _svg.AppendLine("\">");
            _svg.Append("  <rect width=\"100%\" height=\"100%\" fill=\"");
            _svg.Append(Escape(_options.Background));
            _svg.AppendLine("\"/>");
            WriteDefinitions();

            _svg.Append("  <g transform=\"matrix(1 0 0 -1 ");
            _svg.Append(Format(-Math.Min(_crop.Left, _crop.Right)));
            _svg.Append(' ');
            _svg.Append(Format(Math.Max(_crop.Bottom, _crop.Top)));
            _svg.AppendLine(")\">");
            WriteElements(graphics, indent: 2);
            _svg.AppendLine("  </g>");

            if (_options.IncludeText)
                WriteText();
            _svg.AppendLine("</svg>");
            return _svg.ToString();
        }

        private void RegisterElements(IEnumerable<PdfGraphicsElement> elements)
        {
            foreach (PdfGraphicsElement element in elements)
            {
                foreach (PdfClipPath clip in element.ClipPaths)
                    RegisterClip(clip);
                switch (element)
                {
                    case PdfPathElement path:
                        RegisterBrush(path.State.Fill, PdfMatrix.Identity);
                        RegisterBrush(path.State.Stroke, PdfMatrix.Identity);
                        break;
                    case PdfShadingElement shading:
                        RegisterBrush(shading.Shading, shading.State.Transform);
                        break;
                }
            }
        }

        private void RegisterClip(PdfClipPath clip)
        {
            if (_clipIds.ContainsKey(clip))
                return;
            string id = NextId("clip");
            _clipIds.Add(clip, id);
            _clips.Add(clip);
        }

        private void RegisterBrush(PdfBrush brush, PdfMatrix additionalTransform)
        {
            if (brush is PdfSolidBrush)
                return;
            var key = new BrushKey(brush, additionalTransform);
            if (_brushIds.ContainsKey(key))
                return;
            string id = NextId(brush is PdfTilingPatternBrush ? "pattern" : "gradient");
            _brushIds.Add(key, id);
            _brushes.Add(key);
            if (brush is PdfTilingPatternBrush pattern)
                RegisterElements(pattern.Elements);
        }

        private void WriteDefinitions()
        {
            if (_clips.Count == 0 && _brushes.Count == 0)
                return;
            _svg.AppendLine("  <defs>");
            foreach (PdfClipPath clip in _clips)
            {
                _svg.Append("    <clipPath id=\"");
                _svg.Append(_clipIds[clip]);
                _svg.AppendLine("\" clipPathUnits=\"userSpaceOnUse\">");
                _svg.Append("      <path d=\"");
                _svg.Append(PathData(clip.Path));
                _svg.Append("\" transform=\"");
                _svg.Append(Matrix(clip.Transform));
                _svg.Append("\" clip-rule=\"");
                _svg.Append(clip.FillRule == PdfFillRule.EvenOdd ? "evenodd" : "nonzero");
                _svg.AppendLine("\"/>");
                _svg.AppendLine("    </clipPath>");
            }

            foreach (BrushKey key in _brushes)
            {
                string id = _brushIds[key];
                switch (key.Brush)
                {
                    case PdfGradientBrush gradient:
                        WriteGradient(id, gradient, key.AdditionalTransform);
                        break;
                    case PdfTilingPatternBrush pattern:
                        WritePattern(id, pattern);
                        break;
                }
            }

            _svg.AppendLine("  </defs>");
        }

        private void WriteGradient(
            string id,
            PdfGradientBrush gradient,
            PdfMatrix additionalTransform)
        {
            IReadOnlyList<double> coordinates = gradient.Coordinates;
            string tag = gradient.Kind == PdfShadingKind.Axial
                ? "linearGradient"
                : "radialGradient";
            _svg.Append("    <");
            _svg.Append(tag);
            _svg.Append(" id=\"");
            _svg.Append(id);
            _svg.Append("\" gradientUnits=\"userSpaceOnUse\"");
            if (gradient.Kind == PdfShadingKind.Axial && coordinates.Count >= 4)
            {
                Attribute("x1", coordinates[0]);
                Attribute("y1", coordinates[1]);
                Attribute("x2", coordinates[2]);
                Attribute("y2", coordinates[3]);
            }
            else if (gradient.Kind == PdfShadingKind.Radial && coordinates.Count >= 6)
            {
                Attribute("fx", coordinates[0]);
                Attribute("fy", coordinates[1]);
                Attribute("fr", Math.Max(0, coordinates[2]));
                Attribute("cx", coordinates[3]);
                Attribute("cy", coordinates[4]);
                Attribute("r", Math.Max(0, coordinates[5]));
            }

            PdfMatrix transform = gradient.Matrix.Multiply(additionalTransform);
            if (transform != PdfMatrix.Identity)
            {
                _svg.Append(" gradientTransform=\"");
                _svg.Append(Matrix(transform));
                _svg.Append('"');
            }

            _svg.AppendLine(">");
            foreach (PdfGradientStop stop in gradient.Stops)
            {
                _svg.Append("      <stop offset=\"");
                _svg.Append(Format(Math.Clamp(stop.Offset, 0, 1) * 100));
                _svg.Append("%\" stop-color=\"");
                _svg.Append(Color(stop.Color));
                _svg.AppendLine("\"/>");
            }

            _svg.Append("    </");
            _svg.Append(tag);
            _svg.AppendLine(">");
        }

        private void WritePattern(string id, PdfTilingPatternBrush pattern)
        {
            _svg.Append("    <pattern id=\"");
            _svg.Append(id);
            _svg.Append("\" patternUnits=\"userSpaceOnUse\"");
            Attribute("x", Math.Min(pattern.BoundingBox.Left, pattern.BoundingBox.Right));
            Attribute("y", Math.Min(pattern.BoundingBox.Bottom, pattern.BoundingBox.Top));
            Attribute("width", Math.Abs(pattern.XStep));
            Attribute("height", Math.Abs(pattern.YStep));
            if (pattern.Matrix != PdfMatrix.Identity)
            {
                _svg.Append(" patternTransform=\"");
                _svg.Append(Matrix(pattern.Matrix));
                _svg.Append('"');
            }

            _svg.AppendLine(">");
            WriteElements(pattern.Elements, indent: 3);
            _svg.AppendLine("    </pattern>");
        }

        private void WriteElements(
            IEnumerable<PdfGraphicsElement> elements,
            int indent)
        {
            foreach (PdfGraphicsElement element in elements)
            {
                int openGroups = 0;
                foreach (PdfClipPath clip in element.ClipPaths)
                {
                    Indent(indent + openGroups);
                    _svg.Append("<g clip-path=\"url(#");
                    _svg.Append(_clipIds[clip]);
                    _svg.AppendLine(")\">");
                    openGroups++;
                }

                switch (element)
                {
                    case PdfPathElement path:
                        WritePath(path, indent + openGroups);
                        break;
                    case PdfImageElement image:
                        WriteImage(image, indent + openGroups);
                        break;
                    case PdfShadingElement shading:
                        WriteShading(shading, indent + openGroups);
                        break;
                }

                for (int index = openGroups - 1; index >= 0; index--)
                {
                    Indent(indent + index);
                    _svg.AppendLine("</g>");
                }
            }
        }

        private void WritePath(PdfPathElement element, int indent)
        {
            Indent(indent);
            _svg.Append("<path d=\"");
            _svg.Append(PathData(element.Path));
            _svg.Append("\" transform=\"");
            _svg.Append(Matrix(element.State.Transform));
            _svg.Append('"');
            WritePaint(element);
            WriteBlendMode(element.State.BlendMode);
            _svg.AppendLine("/>");
        }

        private void WritePaint(PdfPathElement element)
        {
            bool fill = (element.PaintMode & PdfPaintMode.Fill) != 0;
            bool stroke = (element.PaintMode & PdfPaintMode.Stroke) != 0;
            _svg.Append(" fill=\"");
            _svg.Append(fill ? Brush(element.State.Fill, PdfMatrix.Identity) : "none");
            _svg.Append("\" stroke=\"");
            _svg.Append(stroke ? Brush(element.State.Stroke, PdfMatrix.Identity) : "none");
            _svg.Append('"');
            if (fill)
            {
                _svg.Append(" fill-rule=\"");
                _svg.Append(element.FillRule == PdfFillRule.EvenOdd ? "evenodd" : "nonzero");
                _svg.Append("\" fill-opacity=\"");
                _svg.Append(Format(element.State.FillAlpha));
                _svg.Append('"');
            }

            if (!stroke)
                return;
            Attribute("stroke-width", element.State.LineWidth);
            _svg.Append(" stroke-linecap=\"");
            _svg.Append(element.State.LineCap switch
            {
                PdfLineCap.Round => "round",
                PdfLineCap.Square => "square",
                _ => "butt"
            });
            _svg.Append("\" stroke-linejoin=\"");
            _svg.Append(element.State.LineJoin switch
            {
                PdfLineJoin.Round => "round",
                PdfLineJoin.Bevel => "bevel",
                _ => "miter"
            });
            _svg.Append('"');
            Attribute("stroke-miterlimit", element.State.MiterLimit);
            Attribute("stroke-opacity", element.State.StrokeAlpha);
            if (element.State.Dash.Segments.Count > 0)
            {
                _svg.Append(" stroke-dasharray=\"");
                _svg.Append(string.Join(" ", element.State.Dash.Segments.Select(Format)));
                _svg.Append('"');
                Attribute("stroke-dashoffset", element.State.Dash.Phase);
            }
        }

        private void WriteImage(PdfImageElement image, int indent)
        {
            if (!_options.DrawImageBounds)
                return;
            Indent(indent);
            _svg.Append("<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\" fill=\"none\" ");
            _svg.Append("stroke=\"#7c3aed\" stroke-width=\"0.5\" transform=\"");
            _svg.Append(Matrix(image.State.Transform));
            _svg.Append("\"><title>");
            _svg.Append(Escape(
                $"Image /{image.ResourceName}: {image.Width}x{image.Height}, {image.ColorSpace}"));
            _svg.AppendLine("</title></rect>");
        }

        private void WriteShading(PdfShadingElement shading, int indent)
        {
            Indent(indent);
            _svg.Append("<rect x=\"");
            _svg.Append(Format(Math.Min(_crop.Left, _crop.Right)));
            _svg.Append("\" y=\"");
            _svg.Append(Format(Math.Min(_crop.Bottom, _crop.Top)));
            _svg.Append("\" width=\"");
            _svg.Append(Format(_crop.Width));
            _svg.Append("\" height=\"");
            _svg.Append(Format(_crop.Height));
            _svg.Append("\" fill=\"");
            _svg.Append(Brush(shading.Shading, shading.State.Transform));
            _svg.Append('"');
            Attribute("fill-opacity", shading.State.FillAlpha);
            WriteBlendMode(shading.State.BlendMode);
            _svg.AppendLine("/>");
        }

        private void WriteText()
        {
            foreach (TextBox box in _page.TextList())
            {
                if (string.IsNullOrEmpty(box.Text))
                    continue;
                double x = box.BoundingBox.Left - Math.Min(_crop.Left, _crop.Right);
                double baseline = Math.Max(_crop.Bottom, _crop.Top) - box.BoundingBox.Bottom;
                string fontFamily = NormalizeFontFamily(box.FontName);
                _svg.Append("  <text x=\"");
                _svg.Append(Format(x));
                _svg.Append("\" y=\"");
                _svg.Append(Format(baseline));
                _svg.Append("\" font-family=\"");
                _svg.Append(Escape(fontFamily));
                _svg.Append("\" font-size=\"");
                _svg.Append(Format(Math.Abs(box.FontSize)));
                _svg.Append("\" fill=\"");
                _svg.Append(Escape(_options.Foreground));
                _svg.Append('"');
                if (box.Rotation != 0)
                {
                    _svg.Append(" transform=\"rotate(");
                    _svg.Append(Format(-box.Rotation));
                    _svg.Append(' ');
                    _svg.Append(Format(x));
                    _svg.Append(' ');
                    _svg.Append(Format(baseline));
                    _svg.Append(")\"");
                }

                _svg.Append(" xml:space=\"preserve\">");
                _svg.Append(Escape(box.Text));
                _svg.AppendLine("</text>");

                if (_options.DrawTextBounds)
                {
                    double boxY = Math.Max(_crop.Bottom, _crop.Top) - box.BoundingBox.Top;
                    _svg.Append("  <rect x=\"");
                    _svg.Append(Format(x));
                    _svg.Append("\" y=\"");
                    _svg.Append(Format(boxY));
                    _svg.Append("\" width=\"");
                    _svg.Append(Format(box.BoundingBox.Width));
                    _svg.Append("\" height=\"");
                    _svg.Append(Format(box.BoundingBox.Height));
                    _svg.AppendLine("\" fill=\"none\" stroke=\"#e11d48\" stroke-width=\"0.5\"/>");
                }
            }
        }

        private string Brush(PdfBrush brush, PdfMatrix additionalTransform) =>
            brush switch
            {
                PdfSolidBrush solid => Color(solid.Color),
                _ => $"url(#{_brushIds[new BrushKey(brush, additionalTransform)]})"
            };

        private static string PathData(PdfGraphicsPath path)
        {
            var builder = new StringBuilder();
            foreach (PdfPathSegment segment in path.Segments)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                switch (segment)
                {
                    case PdfMoveTo move:
                        builder.Append("M ");
                        Point(builder, move.Point);
                        break;
                    case PdfLineTo line:
                        builder.Append("L ");
                        Point(builder, line.Point);
                        break;
                    case PdfCubicBezierTo curve:
                        builder.Append("C ");
                        Point(builder, curve.Control1);
                        builder.Append(' ');
                        Point(builder, curve.Control2);
                        builder.Append(' ');
                        Point(builder, curve.End);
                        break;
                    case PdfClosePath:
                        builder.Append('Z');
                        break;
                }
            }

            return builder.ToString();
        }

        private static void Point(StringBuilder builder, PdfPoint point)
        {
            builder.Append(Format(point.X));
            builder.Append(' ');
            builder.Append(Format(point.Y));
        }

        private static string Matrix(PdfMatrix matrix) =>
            $"matrix({Format(matrix.A)} {Format(matrix.B)} {Format(matrix.C)} " +
            $"{Format(matrix.D)} {Format(matrix.E)} {Format(matrix.F)})";

        private static string Color(PdfColor color)
        {
            (double red, double green, double blue) = color.ToRgb();
            int r = (int)Math.Round(Math.Clamp(red, 0, 1) * 255);
            int g = (int)Math.Round(Math.Clamp(green, 0, 1) * 255);
            int b = (int)Math.Round(Math.Clamp(blue, 0, 1) * 255);
            return $"rgb({r} {g} {b})";
        }

        private void Attribute(string name, double value)
        {
            _svg.Append(' ');
            _svg.Append(name);
            _svg.Append("=\"");
            _svg.Append(Format(value));
            _svg.Append('"');
        }

        private void WriteBlendMode(string blendMode)
        {
            string? css = blendMode switch
            {
                "Multiply" => "multiply",
                "Screen" => "screen",
                "Overlay" => "overlay",
                "Darken" => "darken",
                "Lighten" => "lighten",
                "ColorDodge" => "color-dodge",
                "ColorBurn" => "color-burn",
                "HardLight" => "hard-light",
                "SoftLight" => "soft-light",
                "Difference" => "difference",
                "Exclusion" => "exclusion",
                _ => null
            };
            if (css is not null)
            {
                _svg.Append(" style=\"mix-blend-mode:");
                _svg.Append(css);
                _svg.Append('"');
            }
        }

        private void Indent(int count) => _svg.Append(' ', count * 2);

        private string NextId(string prefix) =>
            $"{prefix}-{++_nextId}";

        private static string NormalizeFontFamily(string fontName)
        {
            int plus = fontName.IndexOf('+');
            string normalized = plus >= 0 && plus + 1 < fontName.Length
                ? fontName[(plus + 1)..]
                : fontName;
            return normalized.Replace(',', ' ').Replace('"', ' ').Trim();
        }

        private static string Escape(string value) =>
            SecurityElement.Escape(value) ?? "";

        private readonly record struct BrushKey(
            PdfBrush Brush,
            PdfMatrix AdditionalTransform);
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
