using System.Text;

namespace Poppler.Rendering;

internal sealed class PdfRasterRenderer
{
    private readonly Page _page;
    private readonly RasterRenderOptions _options;
    private readonly PdfMatrix _deviceTransform;
    private readonly Dictionary<PdfClipPath, RasterPath> _clipCache = new();
    private readonly Dictionary<PdfSoftMask, RasterSurface> _softMaskCache = new();
    private readonly Dictionary<PdfSoftMask, double[]> _softMaskTransferCache = new();
    private readonly PdfFontSubstitutionResolver _fontSubstitution;
    private readonly int _samples;

    private PdfRasterRenderer(
        Page page,
        RasterRenderOptions options,
        PdfMatrix deviceTransform,
        int width,
        int height)
    {
        _page = page;
        _options = options;
        _deviceTransform = deviceTransform;
        Width = width;
        Height = height;
        _samples = options.Antialiasing;
        _fontSubstitution = new PdfFontSubstitutionResolver(options);
    }

    private int Width { get; }
    private int Height { get; }

    public static PdfBitmap Render(Page page, RasterRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        PdfRectangle source = page.PageRect(options.PageBox);
        double left = Math.Min(source.Left, source.Right);
        double right = Math.Max(source.Left, source.Right);
        double bottom = Math.Min(source.Bottom, source.Top);
        double top = Math.Max(source.Bottom, source.Top);
        double scale = options.Dpi / 72.0;
        int rotation = NormalizeRotation(page.Rotation);
        double sourceWidth = Math.Max(0, right - left);
        double sourceHeight = Math.Max(0, top - bottom);
        int width = checked((int)Math.Ceiling(
            (rotation is 90 or 270 ? sourceHeight : sourceWidth) * scale));
        int height = checked((int)Math.Ceiling(
            (rotation is 90 or 270 ? sourceWidth : sourceHeight) * scale));
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        long pixelCount = checked((long)width * height);
        if (pixelCount > page.ReadOptions.MaximumRenderPixels)
        {
            throw new PdfLimitException(
                $"Rendered page contains {pixelCount} pixels, exceeding the configured limit.");
        }

        PdfMatrix device = rotation switch
        {
            90 => new PdfMatrix(0, scale, scale, 0, -bottom * scale, -left * scale),
            180 => new PdfMatrix(-scale, 0, 0, scale, right * scale, -bottom * scale),
            270 => new PdfMatrix(0, -scale, -scale, 0, top * scale, right * scale),
            _ => new PdfMatrix(scale, 0, 0, -scale, -left * scale, top * scale)
        };
        var renderer = new PdfRasterRenderer(page, options, device, width, height);
        return renderer.RenderPage();
    }

    private PdfBitmap RenderPage()
    {
        var surface = new RasterSurface(Width, Height);
        surface.Clear(_options.Transparent
            ? RasterColor.Transparent
            : RasterColor.FromPdf(_options.Background));
        RenderElements(surface, _page.Graphics, depth: 0);
        return new PdfBitmap(Width, Height, surface.Pixels);
    }

    private void RenderText(RasterSurface surface, PdfTextElement element)
    {
        if (!_options.IncludeText ||
            element.RenderingMode is PdfTextRenderingMode.Invisible or
                PdfTextRenderingMode.Clip)
        {
            return;
        }

        bool fill = element.RenderingMode is
            PdfTextRenderingMode.Fill or
            PdfTextRenderingMode.FillAndStroke or
            PdfTextRenderingMode.FillAndClip or
            PdfTextRenderingMode.FillStrokeAndClip;
        bool stroke = element.RenderingMode is
            PdfTextRenderingMode.Stroke or
            PdfTextRenderingMode.FillAndStroke or
            PdfTextRenderingMode.StrokeAndClip or
            PdfTextRenderingMode.FillStrokeAndClip;
        foreach (Text.PdfTextGlyphPlacement placement in element.Glyphs)
        {
            bool hasOutline = element.Font.TryGetGlyphOutline(
                placement.Glyph,
                out PdfGraphicsPath outline,
                out _,
                out _,
                out _);
            double substituteAdvance = 0;
            bool substituted = false;
            if (!hasOutline && !element.Font.IsType3)
            {
                hasOutline =
                    !string.IsNullOrEmpty(placement.Glyph.Text) &&
                    _fontSubstitution.TryGetGlyph(
                        element.FontName,
                        placement.Glyph.Text,
                        element.Font.WritingMode,
                        out outline,
                        out substituteAdvance);
                substituted = hasOutline;
            }
            if (!hasOutline)
            {
                continue;
            }

            PdfMatrix glyphTransform = placement.Transform;
            if (substituted &&
                element.Font.WritingMode == FontWritingMode.Horizontal &&
                substituteAdvance > 0 &&
                placement.Glyph.AdvanceX > 0)
            {
                double targetAdvance = placement.Glyph.AdvanceX / 1000.0;
                glyphTransform =
                    new PdfMatrix(
                        targetAdvance / substituteAdvance,
                        0,
                        0,
                        1,
                        0,
                        0)
                    .Multiply(glyphTransform);
            }
            RasterPath geometry = RasterGeometry.Flatten(
                outline,
                glyphTransform.Multiply(_deviceTransform));
            if (fill)
            {
                Paint(
                    surface,
                    geometry.Bounds,
                    element.ClipPaths,
                    (x, y) => RasterGeometry.Contains(
                        geometry,
                        x,
                        y,
                        PdfFillRule.NonZero),
                    (x, y) => SampleBrush(element.State.Fill, element.State, x, y, 0),
                    element.State.FillAlpha,
                    element.State.BlendMode,
                    element.State.SoftMask,
                    BrushColor(element.State.Fill),
                    element.State.FillOverprint,
                    element.State.OverprintMode);
            }

            if (stroke)
            {
                PdfMatrix stateTransform =
                    element.State.Transform.Multiply(_deviceTransform);
                double scale = RasterGeometry.EffectiveScale(stateTransform);
                double width = Math.Max(
                    1.0 / _samples,
                    element.State.LineWidth * scale);
                PdfDashPattern dash = ScaleDash(element.State.Dash, scale);
                Paint(
                    surface,
                    geometry.Bounds.Expand(width / 2 + 1),
                    element.ClipPaths,
                    (x, y) => RasterGeometry.StrokeContains(
                        geometry,
                        x,
                        y,
                        width,
                        dash),
                    (x, y) => SampleBrush(element.State.Stroke, element.State, x, y, 0),
                    element.State.StrokeAlpha,
                    element.State.BlendMode,
                    element.State.SoftMask,
                    BrushColor(element.State.Stroke),
                    element.State.StrokeOverprint,
                    element.State.OverprintMode);
            }
        }
    }

    private void RenderElements(
        RasterSurface surface,
        IReadOnlyList<PdfGraphicsElement> elements,
        int depth)
    {
        if (depth > _page.ReadOptions.MaximumTransparencyGroupDepth)
            throw new PdfLimitException("Transparency-group nesting exceeds the configured limit.");
        foreach (PdfGraphicsElement element in elements)
        {
            switch (element)
            {
                case PdfPathElement path:
                    RenderPath(surface, path);
                    break;
                case PdfImageElement image:
                    RenderImage(surface, image);
                    break;
                case PdfTextElement text:
                    RenderText(surface, text);
                    break;
                case PdfShadingElement shading:
                    RenderShading(surface, shading);
                    break;
                case PdfMeshShadingElement mesh:
                    RenderMeshShading(surface, mesh);
                    break;
                case PdfTransparencyGroupElement group:
                    RenderGroup(surface, group, depth + 1);
                    break;
            }
        }
    }

    private void RenderPath(RasterSurface surface, PdfPathElement element)
    {
        PdfMatrix transform = element.State.Transform.Multiply(_deviceTransform);
        RasterPath path = RasterGeometry.Flatten(element.Path, transform);
        if ((element.PaintMode & PdfPaintMode.Fill) != 0)
        {
            Paint(
                surface,
                path.Bounds,
                element.ClipPaths,
                (x, y) => RasterGeometry.Contains(path, x, y, element.FillRule),
                (x, y) => SampleBrush(element.State.Fill, element.State, x, y, 0),
                element.State.FillAlpha,
                element.State.BlendMode,
                element.State.SoftMask,
                BrushColor(element.State.Fill),
                element.State.FillOverprint,
                element.State.OverprintMode);
        }

        if ((element.PaintMode & PdfPaintMode.Stroke) != 0)
        {
            double scale = RasterGeometry.EffectiveScale(transform);
            double width = Math.Max(1.0 / _samples, element.State.LineWidth * scale);
            PdfDashPattern dash = ScaleDash(element.State.Dash, scale);
            Paint(
                surface,
                path.Bounds.Expand(width / 2 + 1),
                element.ClipPaths,
                (x, y) => RasterGeometry.StrokeContains(path, x, y, width, dash),
                (x, y) => SampleBrush(element.State.Stroke, element.State, x, y, 0),
                element.State.StrokeAlpha,
                element.State.BlendMode,
                element.State.SoftMask,
                BrushColor(element.State.Stroke),
                element.State.StrokeOverprint,
                element.State.OverprintMode);
        }
    }

    private void RenderImage(RasterSurface surface, PdfImageElement element)
    {
        if (element.Image is null)
            return;
        PdfMatrix transform = element.State.Transform.Multiply(_deviceTransform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse))
            return;
        RasterBounds bounds = BoundsOfUnitSquare(transform);
        Paint(
            surface,
            bounds,
            element.ClipPaths,
            (x, y) =>
            {
                PdfPoint point = inverse.Transform(x, y);
                return point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1;
            },
            (x, y) =>
            {
                PdfPoint point = inverse.Transform(x, y);
                return SampleImage(element.Image, point.X, 1 - point.Y);
            },
            element.State.FillAlpha,
            element.State.BlendMode,
            element.State.SoftMask);
    }

    private void RenderShading(RasterSurface surface, PdfShadingElement element)
    {
        Paint(
            surface,
            new RasterBounds(0, 0, Width, Height),
            element.ClipPaths,
            static (_, _) => true,
            (x, y) => SampleGradient(element.Shading, element.State, x, y),
            element.State.FillAlpha,
            element.State.BlendMode,
            element.State.SoftMask);
    }

    private void RenderMeshShading(
        RasterSurface surface,
        PdfMeshShadingElement element)
    {
        PdfMatrix transform = element.Shading.Matrix
            .Multiply(element.State.Transform)
            .Multiply(_deviceTransform);
        var triangles = new List<RasterMeshTriangle>(
            element.Shading.Triangles.Count);
        foreach (PdfMeshTriangle triangle in element.Shading.Triangles)
        {
            PdfPoint first = transform.Transform(
                triangle.First.Point.X,
                triangle.First.Point.Y);
            PdfPoint second = transform.Transform(
                triangle.Second.Point.X,
                triangle.Second.Point.Y);
            PdfPoint third = transform.Transform(
                triangle.Third.Point.X,
                triangle.Third.Point.Y);
            RasterBounds bounds = new(
                Math.Min(first.X, Math.Min(second.X, third.X)),
                Math.Min(first.Y, Math.Min(second.Y, third.Y)),
                Math.Max(first.X, Math.Max(second.X, third.X)),
                Math.Max(first.Y, Math.Max(second.Y, third.Y)));
            triangles.Add(new RasterMeshTriangle(
                triangle,
                first,
                second,
                third,
                bounds));
        }
        if (triangles.Count == 0)
            return;
        RasterBounds combined = new(
            triangles.Min(triangle => triangle.Bounds.Left),
            triangles.Min(triangle => triangle.Bounds.Top),
            triangles.Max(triangle => triangle.Bounds.Right),
            triangles.Max(triangle => triangle.Bounds.Bottom));
        Paint(
            surface,
            combined,
            element.ClipPaths,
            (x, y) => TrySampleMesh(triangles, x, y, out _),
            (x, y) => TrySampleMesh(triangles, x, y, out RasterColor color)
                ? color
                : RasterColor.Transparent,
            element.State.FillAlpha,
            element.State.BlendMode,
            element.State.SoftMask);
    }

    private void RenderGroup(
        RasterSurface surface,
        PdfTransparencyGroupElement group,
        int depth)
    {
        RasterSurface backdrop = surface.Clone();
        RasterSurface layer = RenderTransparencySurface(
            backdrop,
            group.Elements,
            group.Isolated,
            group.Knockout,
            depth);
        if (group.Isolated)
        {
            surface.CompositeSurface(
                layer,
                group.State.BlendMode,
                group.State.FillAlpha,
                (x, y) => ClipCoverage(group.ClipPaths, x, y) *
                          SoftMaskValue(group.State.SoftMask, x, y));
            return;
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                RasterColor before = backdrop.GetPixel(x, y);
                RasterColor after = layer.GetPixel(x, y);
                if (before == after)
                    continue;
                double amount =
                    RasterColor.Clamp(group.State.FillAlpha) *
                    ClipCoverage(group.ClipPaths, x, y) *
                    SoftMaskValue(group.State.SoftMask, x, y);
                surface.SetPixel(x, y, Interpolate(before, after, amount));
            }
        }
    }

    private RasterSurface RenderTransparencySurface(
        RasterSurface backdrop,
        IReadOnlyList<PdfGraphicsElement> elements,
        bool isolated,
        bool knockout,
        int depth)
    {
        var empty = new RasterSurface(Width, Height);
        empty.Clear(RasterColor.Transparent);
        RasterSurface initial = isolated ? empty : backdrop;
        RasterSurface layer = initial.Clone();
        if (!knockout)
        {
            RenderElements(layer, elements, depth);
            return layer;
        }

        foreach (PdfGraphicsElement element in elements)
        {
            RasterSurface child = initial.Clone();
            RenderElements(child, new[] { element }, depth);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    RasterColor original = initial.GetPixel(x, y);
                    RasterColor painted = child.GetPixel(x, y);
                    if (painted != original)
                        layer.SetPixel(x, y, painted);
                }
            }
        }
        return layer;
    }

    private void Paint(
        RasterSurface surface,
        RasterBounds bounds,
        IReadOnlyList<PdfClipPath> clips,
        Func<double, double, bool> contains,
        Func<double, double, RasterColor> sample,
        double opacity,
        string blendMode,
        PdfSoftMask? softMask,
        PdfColor? sourcePdfColor = null,
        bool overprint = false,
        int overprintMode = 0)
    {
        int left = Math.Max(0, (int)Math.Floor(bounds.Left));
        int top = Math.Max(0, (int)Math.Floor(bounds.Top));
        int right = Math.Min(Width, (int)Math.Ceiling(bounds.Right));
        int bottom = Math.Min(Height, (int)Math.Ceiling(bounds.Bottom));
        if (right <= left || bottom <= top)
            return;
        int totalSamples = _samples * _samples;
        double step = 1.0 / _samples;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int covered = 0;
                for (int sampleY = 0; sampleY < _samples; sampleY++)
                {
                    double pointY = y + (sampleY + 0.5) * step;
                    for (int sampleX = 0; sampleX < _samples; sampleX++)
                    {
                        double pointX = x + (sampleX + 0.5) * step;
                        if (contains(pointX, pointY) &&
                            InsideClips(clips, pointX, pointY))
                        {
                            covered++;
                        }
                    }
                }

                if (covered == 0)
                    continue;
                RasterColor color = sample(x + 0.5, y + 0.5);
                double alpha = color.Alpha *
                               RasterColor.Clamp(opacity) *
                               covered / totalSamples *
                               SoftMaskValue(softMask, x, y);
                if (alpha > 0)
                {
                    surface.CompositePixel(
                        x,
                        y,
                        color.WithAlpha(alpha),
                        blendMode,
                        sourcePdfColor,
                        overprint,
                        overprintMode);
                }
            }
        }
    }

    private bool InsideClips(
        IReadOnlyList<PdfClipPath> clips,
        double x,
        double y)
    {
        foreach (PdfClipPath clip in clips)
        {
            if (!RasterGeometry.Contains(ClipPath(clip), x, y, clip.FillRule))
                return false;
        }

        return true;
    }

    private double ClipCoverage(
        IReadOnlyList<PdfClipPath> clips,
        int x,
        int y)
    {
        if (clips.Count == 0)
            return 1;
        int covered = 0;
        int total = _samples * _samples;
        double step = 1.0 / _samples;
        for (int sampleY = 0; sampleY < _samples; sampleY++)
        {
            double pointY = y + (sampleY + 0.5) * step;
            for (int sampleX = 0; sampleX < _samples; sampleX++)
            {
                double pointX = x + (sampleX + 0.5) * step;
                if (InsideClips(clips, pointX, pointY))
                    covered++;
            }
        }

        return covered / (double)total;
    }

    private RasterPath ClipPath(PdfClipPath clip)
    {
        if (_clipCache.TryGetValue(clip, out RasterPath? cached))
            return cached;
        RasterPath path = RasterGeometry.Flatten(
            clip.Path,
            clip.Transform.Multiply(_deviceTransform));
        _clipCache[clip] = path;
        return path;
    }

    private RasterColor SampleBrush(
        PdfBrush brush,
        PdfGraphicsState state,
        double x,
        double y,
        int depth)
    {
        if (depth > _page.ReadOptions.MaximumTransparencyGroupDepth)
            return RasterColor.Transparent;
        return brush switch
        {
            PdfSolidBrush solid => RasterColor.FromPdf(solid.Color),
            PdfGradientBrush gradient => SampleGradient(gradient, state, x, y),
            PdfMeshShadingBrush mesh => SampleMesh(mesh, state, x, y),
            PdfTilingPatternBrush pattern => SamplePattern(pattern, state, x, y, depth + 1),
            _ => RasterColor.Transparent
        };
    }

    private RasterColor SampleGradient(
        PdfGradientBrush gradient,
        PdfGraphicsState state,
        double x,
        double y)
    {
        PdfMatrix transform = gradient.Matrix
            .Multiply(state.Transform)
            .Multiply(_deviceTransform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse))
            return RasterColor.Transparent;
        PdfPoint point = inverse.Transform(x, y);
        double? parameter = gradient.Kind == PdfShadingKind.Axial
            ? AxialParameter(gradient.Coordinates, point)
            : RadialParameter(gradient.Coordinates, point);
        if (!parameter.HasValue)
            return RasterColor.Transparent;
        double amount = parameter.Value;
        if (amount < 0 && !gradient.ExtendStart ||
            amount > 1 && !gradient.ExtendEnd)
        {
            return RasterColor.Transparent;
        }

        amount = Math.Clamp(amount, 0, 1);
        IReadOnlyList<PdfGradientStop> stops = gradient.Stops;
        if (stops.Count == 0)
            return RasterColor.Transparent;
        PdfGradientStop first = stops[0];
        PdfGradientStop second = stops[^1];
        for (int index = 1; index < stops.Count; index++)
        {
            if (amount <= stops[index].Offset)
            {
                first = stops[index - 1];
                second = stops[index];
                break;
            }
        }

        double range = second.Offset - first.Offset;
        double local = range <= 1e-12 ? 0 : (amount - first.Offset) / range;
        (double firstRed, double firstGreen, double firstBlue) = first.Color.ToRgb();
        (double secondRed, double secondGreen, double secondBlue) = second.Color.ToRgb();
        return new RasterColor(
            Lerp(firstRed, secondRed, local),
            Lerp(firstGreen, secondGreen, local),
            Lerp(firstBlue, secondBlue, local),
            1);
    }

    private RasterColor SamplePattern(
        PdfTilingPatternBrush pattern,
        PdfGraphicsState state,
        double x,
        double y,
        int depth)
    {
        PdfMatrix transform = pattern.Matrix
            .Multiply(state.Transform)
            .Multiply(_deviceTransform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse) ||
            Math.Abs(pattern.XStep) <= 1e-12 ||
            Math.Abs(pattern.YStep) <= 1e-12)
        {
            return RasterColor.Transparent;
        }

        PdfPoint patternPoint = inverse.Transform(x, y);
        double tileX = pattern.BoundingBox.Left +
                       PositiveModulo(
                           patternPoint.X - pattern.BoundingBox.Left,
                           Math.Abs(pattern.XStep));
        double tileY = pattern.BoundingBox.Bottom +
                       PositiveModulo(
                           patternPoint.Y - pattern.BoundingBox.Bottom,
                           Math.Abs(pattern.YStep));
        for (int index = pattern.Elements.Count - 1; index >= 0; index--)
        {
            if (pattern.Elements[index] is not PdfPathElement path)
                continue;
            RasterPath geometry = RasterGeometry.Flatten(
                path.Path,
                path.State.Transform);
            if ((path.PaintMode & PdfPaintMode.Stroke) != 0 &&
                RasterGeometry.StrokeContains(
                    geometry,
                    tileX,
                    tileY,
                    Math.Max(path.State.LineWidth, 0.01),
                    path.State.Dash))
            {
                RasterColor stroke = !pattern.IsColored && pattern.UnderlyingColor.HasValue
                    ? RasterColor.FromPdf(pattern.UnderlyingColor.Value)
                    : SamplePatternBrush(
                        path.State.Stroke,
                        path.State,
                        tileX,
                        tileY,
                        depth);
                return stroke.WithAlpha(stroke.Alpha * path.State.StrokeAlpha);
            }

            if ((path.PaintMode & PdfPaintMode.Fill) != 0 &&
                RasterGeometry.Contains(geometry, tileX, tileY, path.FillRule))
            {
                RasterColor fill = !pattern.IsColored && pattern.UnderlyingColor.HasValue
                    ? RasterColor.FromPdf(pattern.UnderlyingColor.Value)
                    : SamplePatternBrush(
                        path.State.Fill,
                        path.State,
                        tileX,
                        tileY,
                        depth);
                return fill.WithAlpha(fill.Alpha * path.State.FillAlpha);
            }
        }

        return RasterColor.Transparent;
    }

    private RasterColor SamplePatternBrush(
        PdfBrush brush,
        PdfGraphicsState state,
        double x,
        double y,
        int depth)
    {
        if (brush is PdfSolidBrush solid)
            return RasterColor.FromPdf(solid.Color);
        if (brush is PdfGradientBrush gradient)
        {
            PdfGraphicsState local = state with { Transform = PdfMatrix.Identity };
            return SampleGradientInUserSpace(gradient, local, x, y);
        }
        if (brush is PdfMeshShadingBrush mesh)
        {
            PdfGraphicsState local = state with { Transform = PdfMatrix.Identity };
            return SampleMeshInUserSpace(mesh, local, x, y);
        }

        return depth > _page.ReadOptions.MaximumTransparencyGroupDepth
            ? RasterColor.Transparent
            : RasterColor.Transparent;
    }

    private RasterColor SampleMesh(
        PdfMeshShadingBrush mesh,
        PdfGraphicsState state,
        double x,
        double y)
    {
        PdfMatrix transform = mesh.Matrix
            .Multiply(state.Transform)
            .Multiply(_deviceTransform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse))
            return RasterColor.Transparent;
        PdfPoint point = inverse.Transform(x, y);
        return SampleMeshPoint(mesh, point.X, point.Y);
    }

    private static RasterColor SampleMeshInUserSpace(
        PdfMeshShadingBrush mesh,
        PdfGraphicsState state,
        double x,
        double y)
    {
        PdfMatrix transform = mesh.Matrix.Multiply(state.Transform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse))
            return RasterColor.Transparent;
        PdfPoint point = inverse.Transform(x, y);
        return SampleMeshPoint(mesh, point.X, point.Y);
    }

    private static RasterColor SampleMeshPoint(
        PdfMeshShadingBrush mesh,
        double x,
        double y)
    {
        foreach (PdfMeshTriangle triangle in mesh.Triangles)
        {
            if (!TryBarycentric(
                    triangle.First.Point,
                    triangle.Second.Point,
                    triangle.Third.Point,
                    x,
                    y,
                    out double first,
                    out double second,
                    out double third))
            {
                continue;
            }
            return InterpolateTriangleColors(triangle, first, second, third);
        }
        return RasterColor.Transparent;
    }

    private RasterColor SampleGradientInUserSpace(
        PdfGradientBrush gradient,
        PdfGraphicsState state,
        double x,
        double y)
    {
        PdfMatrix transform = gradient.Matrix.Multiply(state.Transform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse))
            return RasterColor.Transparent;
        PdfPoint point = inverse.Transform(x, y);
        double? parameter = gradient.Kind == PdfShadingKind.Axial
            ? AxialParameter(gradient.Coordinates, point)
            : RadialParameter(gradient.Coordinates, point);
        if (!parameter.HasValue)
            return RasterColor.Transparent;
        double amount = Math.Clamp(parameter.Value, 0, 1);
        IReadOnlyList<PdfGradientStop> stops = gradient.Stops;
        if (stops.Count == 0)
            return RasterColor.Transparent;
        PdfGradientStop before = stops[0];
        PdfGradientStop after = stops[^1];
        for (int index = 1; index < stops.Count; index++)
        {
            if (amount <= stops[index].Offset)
            {
                before = stops[index - 1];
                after = stops[index];
                break;
            }
        }

        double span = after.Offset - before.Offset;
        double local = span <= 1e-12 ? 0 : (amount - before.Offset) / span;
        (double r0, double g0, double b0) = before.Color.ToRgb();
        (double r1, double g1, double b1) = after.Color.ToRgb();
        return new RasterColor(
            Lerp(r0, r1, local),
            Lerp(g0, g1, local),
            Lerp(b0, b1, local),
            1);
    }

    private double SoftMaskValue(PdfSoftMask? mask, int x, int y)
    {
        if (mask is null)
            return 1;
        if (!_softMaskCache.TryGetValue(mask, out RasterSurface? surface))
        {
            var backdrop = new RasterSurface(Width, Height);
            backdrop.Clear(
                mask.Mode == PdfSoftMaskMode.Luminosity && !mask.Isolated
                    ? RasterColor.FromPdf(mask.Backdrop)
                    : RasterColor.Transparent);
            surface = RenderTransparencySurface(
                backdrop,
                mask.Elements,
                mask.Isolated,
                mask.Knockout,
                depth: 1);
            _softMaskCache[mask] = surface;
        }

        RasterColor pixel = surface.GetPixel(x, y);
        double value;
        if (mask.Mode == PdfSoftMaskMode.Alpha)
        {
            value = pixel.Alpha;
        }
        else
        {
            (double backdropRed, double backdropGreen, double backdropBlue) =
                mask.Backdrop.ToRgb();
            double red = pixel.Red * pixel.Alpha + backdropRed * (1 - pixel.Alpha);
            double green = pixel.Green * pixel.Alpha + backdropGreen * (1 - pixel.Alpha);
            double blue = pixel.Blue * pixel.Alpha + backdropBlue * (1 - pixel.Alpha);
            value = RasterColor.Clamp(0.3 * red + 0.59 * green + 0.11 * blue);
        }

        return ApplySoftMaskTransfer(mask, value);
    }

    private double ApplySoftMaskTransfer(PdfSoftMask mask, double value)
    {
        if (mask.TransferFunction is null)
            return value;
        if (!_softMaskTransferCache.TryGetValue(mask, out double[]? table))
        {
            const int sampleCount = 4097;
            table = new double[sampleCount];
            var input = new double[1];
            for (int index = 0; index < table.Length; index++)
            {
                input[0] = (double)index / (sampleCount - 1);
                double[] result = mask.TransferFunction.Evaluate(input, 1);
                table[index] = RasterColor.Clamp(result[0]);
            }
            _softMaskTransferCache[mask] = table;
        }

        double position = RasterColor.Clamp(value) * (table.Length - 1);
        int before = Math.Min(table.Length - 1, (int)Math.Floor(position));
        int after = Math.Min(table.Length - 1, before + 1);
        return RasterColor.Clamp(Lerp(table[before], table[after], position - before));
    }

    private static RasterColor SampleImage(PdfImage image, double u, double v)
    {
        u = Math.Clamp(u, 0, 1);
        v = Math.Clamp(v, 0, 1);
        if (!image.Interpolate || image.Width == 1 || image.Height == 1)
        {
            int x = Math.Min(image.Width - 1, (int)(u * image.Width));
            int y = Math.Min(image.Height - 1, (int)(v * image.Height));
            return ReadImagePixel(image, x, y);
        }

        double sourceX = u * image.Width - 0.5;
        double sourceY = v * image.Height - 0.5;
        int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, image.Width - 1);
        int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, image.Height - 1);
        int x1 = Math.Min(image.Width - 1, x0 + 1);
        int y1 = Math.Min(image.Height - 1, y0 + 1);
        double amountX = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1);
        double amountY = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);
        RasterColor top = Interpolate(
            ReadImagePixel(image, x0, y0),
            ReadImagePixel(image, x1, y0),
            amountX);
        RasterColor bottom = Interpolate(
            ReadImagePixel(image, x0, y1),
            ReadImagePixel(image, x1, y1),
            amountX);
        return Interpolate(top, bottom, amountY);
    }

    private static RasterColor ReadImagePixel(PdfImage image, int x, int y)
    {
        ReadOnlySpan<byte> pixels = image.Data.Span;
        int components = image.Format switch
        {
            PdfPixelFormat.Gray8 => 1,
            PdfPixelFormat.Rgb24 => 3,
            PdfPixelFormat.Rgba32 => 4,
            _ => 4
        };
        int offset = checked(y * image.BytesPerRow + x * components);
        return image.Format switch
        {
            PdfPixelFormat.Gray8 => new RasterColor(
                pixels[offset] / 255.0,
                pixels[offset] / 255.0,
                pixels[offset] / 255.0,
                1),
            PdfPixelFormat.Rgb24 => new RasterColor(
                pixels[offset] / 255.0,
                pixels[offset + 1] / 255.0,
                pixels[offset + 2] / 255.0,
                1),
            _ => new RasterColor(
                pixels[offset] / 255.0,
                pixels[offset + 1] / 255.0,
                pixels[offset + 2] / 255.0,
                pixels[offset + 3] / 255.0)
        };
    }

    private static RasterColor Interpolate(
        RasterColor first,
        RasterColor second,
        double amount) =>
        new(
            Lerp(first.Red, second.Red, amount),
            Lerp(first.Green, second.Green, amount),
            Lerp(first.Blue, second.Blue, amount),
            Lerp(first.Alpha, second.Alpha, amount));

    private static bool TrySampleMesh(
        IReadOnlyList<RasterMeshTriangle> triangles,
        double x,
        double y,
        out RasterColor color)
    {
        foreach (RasterMeshTriangle triangle in triangles)
        {
            if (x < triangle.Bounds.Left ||
                x > triangle.Bounds.Right ||
                y < triangle.Bounds.Top ||
                y > triangle.Bounds.Bottom ||
                !TryBarycentric(
                    triangle.First,
                    triangle.Second,
                    triangle.Third,
                    x,
                    y,
                    out double first,
                    out double second,
                    out double third))
            {
                continue;
            }
            color = InterpolateTriangleColors(
                triangle.Source,
                first,
                second,
                third);
            return true;
        }
        color = RasterColor.Transparent;
        return false;
    }

    private static RasterColor InterpolateTriangleColors(
        PdfMeshTriangle triangle,
        double first,
        double second,
        double third)
    {
        (double r0, double g0, double b0) = triangle.First.Color.ToRgb();
        (double r1, double g1, double b1) = triangle.Second.Color.ToRgb();
        (double r2, double g2, double b2) = triangle.Third.Color.ToRgb();
        return new RasterColor(
            RasterColor.Clamp(r0 * first + r1 * second + r2 * third),
            RasterColor.Clamp(g0 * first + g1 * second + g2 * third),
            RasterColor.Clamp(b0 * first + b1 * second + b2 * third),
            1);
    }

    private static bool TryBarycentric(
        PdfPoint first,
        PdfPoint second,
        PdfPoint third,
        double x,
        double y,
        out double firstWeight,
        out double secondWeight,
        out double thirdWeight)
    {
        double denominator =
            (second.Y - third.Y) * (first.X - third.X) +
            (third.X - second.X) * (first.Y - third.Y);
        if (Math.Abs(denominator) <= 1e-20)
        {
            firstWeight = secondWeight = thirdWeight = 0;
            return false;
        }
        firstWeight =
            ((second.Y - third.Y) * (x - third.X) +
             (third.X - second.X) * (y - third.Y)) / denominator;
        secondWeight =
            ((third.Y - first.Y) * (x - third.X) +
             (first.X - third.X) * (y - third.Y)) / denominator;
        thirdWeight = 1 - firstWeight - secondWeight;
        const double tolerance = -1e-9;
        return firstWeight >= tolerance &&
               secondWeight >= tolerance &&
               thirdWeight >= tolerance;
    }

    private static PdfColor? BrushColor(PdfBrush brush) => brush switch
    {
        PdfSolidBrush solid => solid.Color,
        PdfTilingPatternBrush { IsColored: false, UnderlyingColor: { } color } =>
            color,
        _ => null
    };

    private readonly record struct RasterMeshTriangle(
        PdfMeshTriangle Source,
        PdfPoint First,
        PdfPoint Second,
        PdfPoint Third,
        RasterBounds Bounds);

    private static double? AxialParameter(
        IReadOnlyList<double> coordinates,
        PdfPoint point)
    {
        if (coordinates.Count < 4)
            return null;
        double dx = coordinates[2] - coordinates[0];
        double dy = coordinates[3] - coordinates[1];
        double denominator = dx * dx + dy * dy;
        return denominator <= 1e-20
            ? 0
            : ((point.X - coordinates[0]) * dx +
               (point.Y - coordinates[1]) * dy) / denominator;
    }

    private static double? RadialParameter(
        IReadOnlyList<double> coordinates,
        PdfPoint point)
    {
        if (coordinates.Count < 6)
            return null;
        double x0 = coordinates[0];
        double y0 = coordinates[1];
        double r0 = coordinates[2];
        double dx = coordinates[3] - x0;
        double dy = coordinates[4] - y0;
        double dr = coordinates[5] - r0;
        double px = point.X - x0;
        double py = point.Y - y0;
        double a = dx * dx + dy * dy - dr * dr;
        double b = -2 * (px * dx + py * dy + r0 * dr);
        double c = px * px + py * py - r0 * r0;
        if (Math.Abs(a) <= 1e-15)
            return Math.Abs(b) <= 1e-15 ? 0 : -c / b;
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
            return null;
        double root = Math.Sqrt(discriminant);
        double first = (-b - root) / (2 * a);
        double second = (-b + root) / (2 * a);
        bool firstValid = r0 + first * dr >= 0;
        bool secondValid = r0 + second * dr >= 0;
        if (firstValid && secondValid)
        {
            bool firstInside = first is >= 0 and <= 1;
            bool secondInside = second is >= 0 and <= 1;
            if (firstInside != secondInside)
                return firstInside ? first : second;
            return Math.Abs(first - 0.5) <= Math.Abs(second - 0.5)
                ? first
                : second;
        }

        return firstValid ? first : secondValid ? second : null;
    }

    private static RasterBounds BoundsOfUnitSquare(PdfMatrix matrix)
    {
        PdfPoint[] points =
        {
            matrix.Transform(0, 0),
            matrix.Transform(1, 0),
            matrix.Transform(0, 1),
            matrix.Transform(1, 1)
        };
        return new RasterBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static PdfDashPattern ScaleDash(PdfDashPattern dash, double scale) =>
        dash.Segments.Count == 0
            ? PdfDashPattern.Solid
            : new PdfDashPattern(
                dash.Segments.Select(value => value * scale),
                dash.Phase * scale);

    private static double PositiveModulo(double value, double modulus)
    {
        double result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static double Lerp(double first, double second, double amount) =>
        first + (second - first) * Math.Clamp(amount, 0, 1);

    private static int NormalizeRotation(int rotation)
    {
        int normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

}
