using System.Text;

namespace Poppler.Rendering;

internal sealed class PdfRasterRenderer
{
    private readonly Page _page;
    private readonly IReadOnlyList<PdfGraphicsElement> _elements;
    private readonly RasterRenderOptions _options;
    private readonly PdfMatrix _deviceTransform;
    private readonly Dictionary<PdfClipPath, RasterPath> _clipCache = new();
    private readonly Dictionary<PdfSoftMask, RasterSurface> _softMaskCache = new();
    private readonly Dictionary<PdfSoftMask, double[]> _softMaskTransferCache = new();
    private readonly Dictionary<PdfPathElement, RasterPath> _patternFillCache =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfPathElement, RasterPath> _patternStrokeCache =
        new(ReferenceEqualityComparer.Instance);
    private readonly PdfFontSubstitutionResolver _fontSubstitution;
    private readonly RasterGeometryBudget _geometryBudget;
    private readonly RenderWorkingSetBudget _workingSetBudget;
    private readonly int _samples;
    private PdfMeshShadingBrush? _sampleMeshCacheBrush;
    private PdfMatrix _sampleMeshCacheTransform;
    private IReadOnlyList<PdfMeshTriangle>? _sampleMeshCacheTriangles;
    private int _softMaskRenderDepth;

    private PdfRasterRenderer(
        Page page,
        IReadOnlyList<PdfGraphicsElement> elements,
        RasterRenderOptions options,
        PdfMatrix deviceTransform,
        int width,
        int height)
    {
        _page = page;
        _elements = elements;
        _options = options;
        _deviceTransform = deviceTransform;
        Width = width;
        Height = height;
        _samples = options.Antialiasing;
        _fontSubstitution = new PdfFontSubstitutionResolver(options);
        _geometryBudget = new RasterGeometryBudget(
            page.ReadOptions.MaximumRasterGeometrySegments);
        _workingSetBudget = new RenderWorkingSetBudget(
            page.ReadOptions.MaximumRenderWorkingBytes);
    }

    private int Width { get; }
    private int Height { get; }

    public static PdfBitmap Render(Page page, RasterRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        options = options.Snapshot();
        return RenderCore(
            page,
            page.GraphicsFor(options.OptionalContentVisibility),
            options);
    }

    internal static PdfBitmap RenderSubset(
        Page page,
        IReadOnlyList<PdfGraphicsElement> elements,
        RasterRenderOptions options,
        PdfRectangle source)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(options);
        return RenderCore(page, elements, options.Snapshot(), source);
    }

    private static PdfBitmap RenderCore(
        Page page,
        IReadOnlyList<PdfGraphicsElement> elements,
        RasterRenderOptions options,
        PdfRectangle? sourceOverride = null)
    {
        PdfRectangle source = sourceOverride ?? page.PageRect(options.PageBox);
        double left = Math.Min(source.Left, source.Right);
        double right = Math.Max(source.Left, source.Right);
        double bottom = Math.Min(source.Bottom, source.Top);
        double top = Math.Max(source.Bottom, source.Top);
        double scale = options.Dpi / 72.0;
        int rotation = NormalizeRotation(page.Rotation);
        double sourceWidth = Math.Max(0, right - left);
        double sourceHeight = Math.Max(0, top - bottom);
        int width = PixelDimension(
            (rotation is 90 or 270 ? sourceHeight : sourceWidth) * scale);
        int height = PixelDimension(
            (rotation is 90 or 270 ? sourceWidth : sourceHeight) * scale);
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
        var renderer = new PdfRasterRenderer(
            page,
            elements,
            options,
            device,
            width,
            height);
        return renderer.RenderPage();
    }

    private static int PixelDimension(double value)
    {
        double rounded = Math.Ceiling(value);
        if (!double.IsFinite(rounded) || rounded > int.MaxValue)
        {
            throw new PdfLimitException(
                "Rendered page dimensions exceed the supported pixel range.");
        }

        return Math.Max(1, (int)rounded);
    }

    private PdfBitmap RenderPage()
    {
        try
        {
            using var surface = CreateSurface(quantizeComposite: true);
            surface.Clear(_options.Transparent
                ? RasterColor.Transparent
                : RasterColor.FromPdf(_options.Background));
            RenderElements(
                surface,
                _elements,
                depth: 0);
            return new PdfBitmap(Width, Height, surface.ToRgbaBytes());
        }
        finally
        {
            foreach (RasterSurface mask in _softMaskCache.Values)
                mask.Dispose();
            _softMaskCache.Clear();
        }
    }

    private RasterSurface CreateSurface(bool quantizeComposite = false) =>
        new(Width, Height, _workingSetBudget, quantizeComposite);

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
            if (fill)
            {
                RasterPath geometry = RasterGeometry.Flatten(
                    outline,
                    glyphTransform.Multiply(_deviceTransform),
                    _geometryBudget);
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
                if (!RasterGeometry.TryInvert(
                        element.State.Transform,
                        out PdfMatrix inverseState))
                {
                    continue;
                }
                PdfMatrix glyphToUser = glyphTransform.Multiply(inverseState);
                RasterPath strokeOutline = RasterStrokeOutliner.Create(
                    outline,
                    glyphToUser,
                    stateTransform,
                    element.State.LineWidth,
                    element.State.Dash,
                    element.State.LineCap,
                    element.State.LineJoin,
                    element.State.MiterLimit,
                    _geometryBudget);
                Paint(
                    surface,
                    strokeOutline.Bounds,
                    element.ClipPaths,
                    (x, y) => RasterGeometry.Contains(
                        strokeOutline,
                        x,
                        y,
                        PdfFillRule.NonZero),
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
                case PdfFunctionShadingElement function:
                    RenderFunctionShading(surface, function);
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
        if ((element.PaintMode & PdfPaintMode.Fill) != 0)
        {
            RasterPath path = RasterGeometry.IsNearSingular(transform)
                ? RasterPath.Empty
                : RasterGeometry.Flatten(
                    element.Path,
                    transform,
                    _geometryBudget);
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
            RasterPath strokeOutline = RasterStrokeOutliner.Create(
                element.Path,
                transform,
                element.State.LineWidth,
                element.State.Dash,
                element.State.LineCap,
                element.State.LineJoin,
                element.State.MiterLimit,
                _geometryBudget);
            Paint(
                surface,
                strokeOutline.Bounds,
                element.ClipPaths,
                (x, y) => RasterGeometry.Contains(
                    strokeOutline,
                    x,
                    y,
                    PdfFillRule.NonZero),
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

    private void RenderFunctionShading(
        RasterSurface surface,
        PdfFunctionShadingElement element)
    {
        PdfFunctionShadingBrush shading = element.Shading;
        PdfMatrix functionTransform = shading.Matrix
            .Multiply(element.State.Transform)
            .Multiply(_deviceTransform);
        if (!RasterGeometry.TryInvert(functionTransform, out PdfMatrix functionInverse))
            return;

        RasterBounds bounds = BoundsOfRectangle(shading.Domain, functionTransform);
        PdfMatrix boundingBoxInverse = PdfMatrix.Identity;
        if (shading.BoundingBox is { } boundingBox)
        {
            PdfMatrix boundingBoxTransform = shading.BoundingBoxMatrix
                .Multiply(element.State.Transform)
                .Multiply(_deviceTransform);
            if (!RasterGeometry.TryInvert(boundingBoxTransform, out boundingBoxInverse))
                return;
            bounds = Intersect(bounds, BoundsOfRectangle(boundingBox, boundingBoxTransform));
        }
        if (bounds.IsEmpty)
            return;

        Paint(
            surface,
            bounds,
            element.ClipPaths,
            (x, y) => ContainsFunctionPoint(
                shading,
                functionInverse,
                boundingBoxInverse,
                x,
                y,
                out _),
            (x, y) => ContainsFunctionPoint(
                shading,
                functionInverse,
                boundingBoxInverse,
                x,
                y,
                out PdfPoint point)
                    ? RasterColor.FromPdf(shading.Evaluate(point.X, point.Y))
                    : RasterColor.Transparent,
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
        IReadOnlyList<PdfMeshTriangle> sourceTriangles =
            PdfMeshTessellator.Tessellate(
                element.Shading,
                transform,
                _page.ReadOptions.MaximumMeshTriangles);
        var triangles = new RasterMeshTriangle[sourceTriangles.Count];
        for (int index = 0; index < sourceTriangles.Count; index++)
        {
            PdfMeshTriangle triangle = sourceTriangles[index];
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
            triangles[index] = new RasterMeshTriangle(
                triangle,
                first,
                second,
                third,
                bounds);
        }
        if (triangles.Length == 0)
            return;
        var meshIndex = new RasterMeshIndex(triangles);
        Paint(
            surface,
            meshIndex.Bounds,
            element.ClipPaths,
            (x, y) => meshIndex.TrySample(x, y, out _),
            (x, y) => meshIndex.TrySample(x, y, out RasterColor color)
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
        using TransparencyResult result = RenderTransparencySurface(
            surface,
            group.Elements,
            group.Isolated,
            group.Knockout,
            depth);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double groupShape = result.Surface.GetShape(x, y);
                if (groupShape <= 0)
                    continue;
                double clip = ClipCoverage(group.ClipPaths, x, y);
                if (clip <= 0)
                    continue;
                double opacity = RasterColor.Clamp(group.State.FillAlpha) *
                    SoftMaskValue(group.State.SoftMask, x, y);
                RasterColor source = result.Surface.RecoverGroupSource(
                    result.InitialBackdrop,
                    x,
                    y);
                surface.CompositePixel(
                    x,
                    y,
                    source.WithAlpha(source.Alpha * opacity * clip),
                    group.State.BlendMode,
                    sourceShape: groupShape * clip);
            }
        }
    }

    private TransparencyResult RenderTransparencySurface(
        RasterSurface backdrop,
        IReadOnlyList<PdfGraphicsElement> elements,
        bool isolated,
        bool knockout,
        int depth)
    {
        RasterSurface? initial = null;
        RasterSurface? layer = null;
        try
        {
            if (isolated)
            {
                initial = CreateSurface();
                initial.Clear(RasterColor.Transparent);
            }
            else
            {
                initial = backdrop.CloneActual();
            }
            layer = initial.CloneActual();
            if (!knockout)
            {
                RenderElements(layer, elements, depth);
                return new TransparencyResult(initial, layer);
            }

            foreach (PdfGraphicsElement element in elements)
            {
                using RasterSurface child = initial.CloneActual();
                RenderElements(child, new[] { element }, depth);
                layer.MergeKnockoutChild(initial, child);
            }
            return new TransparencyResult(initial, layer);
        }
        catch
        {
            layer?.Dispose();
            initial?.Dispose();
            throw;
        }
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
        if (bounds.IsEmpty)
            return;
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
                double shape = covered / (double)totalSamples;
                if (alpha > 0 || shape > 0)
                {
                    surface.CompositePixel(
                        x,
                        y,
                        color.WithAlpha(alpha),
                        blendMode,
                        shape,
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
        PdfMatrix transform = clip.Transform.Multiply(_deviceTransform);
        RasterPath path = RasterGeometry.IsNearSingular(transform)
            ? RasterPath.Empty
            : RasterGeometry.Flatten(
                clip.Path,
                transform,
                _geometryBudget,
                temporaryClip: true);
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
            PdfFunctionShadingBrush function => SampleFunction(function, state, x, y),
            PdfMeshShadingBrush mesh => SampleMesh(mesh, state, x, y),
            PdfTilingPatternBrush pattern => SamplePattern(pattern, state, x, y, depth + 1),
            _ => RasterColor.Transparent
        };
    }

    private RasterColor SampleFunction(
        PdfFunctionShadingBrush shading,
        PdfGraphicsState state,
        double x,
        double y) =>
        SampleFunction(
            shading,
            state,
            x,
            y,
            _deviceTransform);

    private static RasterColor SampleFunction(
        PdfFunctionShadingBrush shading,
        PdfGraphicsState state,
        double x,
        double y,
        PdfMatrix deviceTransform)
    {
        PdfMatrix functionTransform = shading.Matrix
            .Multiply(state.Transform)
            .Multiply(deviceTransform);
        if (!RasterGeometry.TryInvert(functionTransform, out PdfMatrix functionInverse))
            return RasterColor.Transparent;

        PdfMatrix boundingBoxInverse = PdfMatrix.Identity;
        if (shading.BoundingBox is not null)
        {
            PdfMatrix boundingBoxTransform = shading.BoundingBoxMatrix
                .Multiply(state.Transform)
                .Multiply(deviceTransform);
            if (!RasterGeometry.TryInvert(boundingBoxTransform, out boundingBoxInverse))
                return RasterColor.Transparent;
        }

        return ContainsFunctionPoint(
            shading,
            functionInverse,
            boundingBoxInverse,
            x,
            y,
            out PdfPoint point)
            ? RasterColor.FromPdf(shading.Evaluate(point.X, point.Y))
            : RasterColor.Transparent;
    }

    private static bool ContainsFunctionPoint(
        PdfFunctionShadingBrush shading,
        PdfMatrix functionInverse,
        PdfMatrix boundingBoxInverse,
        double x,
        double y,
        out PdfPoint functionPoint)
    {
        functionPoint = functionInverse.Transform(x, y);
        if (!shading.Domain.Contains(functionPoint.X, functionPoint.Y))
            return false;
        if (shading.BoundingBox is not { } boundingBox)
            return true;
        PdfPoint boundingBoxPoint = boundingBoxInverse.Transform(x, y);
        return boundingBox.Contains(boundingBoxPoint.X, boundingBoxPoint.Y);
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
            if ((path.PaintMode & PdfPaintMode.Stroke) != 0 &&
                RasterGeometry.Contains(
                    PatternStroke(path),
                    tileX,
                    tileY,
                    PdfFillRule.NonZero))
            {
                RasterColor stroke = !pattern.IsColored && pattern.UnderlyingColor.HasValue
                    ? RasterColor.FromPdf(pattern.UnderlyingColor.Value)
                    : SamplePatternBrush(
                        path.State.Stroke,
                        path.State,
                        tileX,
                        tileY,
                        depth,
                        transform);
                return stroke.WithAlpha(stroke.Alpha * path.State.StrokeAlpha);
            }

            if ((path.PaintMode & PdfPaintMode.Fill) != 0 &&
                RasterGeometry.Contains(
                    PatternFill(path),
                    tileX,
                    tileY,
                    path.FillRule))
            {
                RasterColor fill = !pattern.IsColored && pattern.UnderlyingColor.HasValue
                    ? RasterColor.FromPdf(pattern.UnderlyingColor.Value)
                    : SamplePatternBrush(
                        path.State.Fill,
                        path.State,
                        tileX,
                        tileY,
                        depth,
                        transform);
                return fill.WithAlpha(fill.Alpha * path.State.FillAlpha);
            }
        }

        return RasterColor.Transparent;
    }

    private RasterPath PatternFill(PdfPathElement path)
    {
        if (_patternFillCache.TryGetValue(path, out RasterPath? geometry))
            return geometry;
        geometry = RasterGeometry.Flatten(
            path.Path,
            path.State.Transform,
            _geometryBudget);
        _patternFillCache[path] = geometry;
        return geometry;
    }

    private RasterPath PatternStroke(PdfPathElement path)
    {
        if (_patternStrokeCache.TryGetValue(path, out RasterPath? geometry))
            return geometry;
        geometry = RasterStrokeOutliner.Create(
            path.Path,
            path.State.Transform,
            path.State.LineWidth,
            path.State.Dash,
            path.State.LineCap,
            path.State.LineJoin,
            path.State.MiterLimit,
            _geometryBudget);
        _patternStrokeCache[path] = geometry;
        return geometry;
    }

    private RasterColor SamplePatternBrush(
        PdfBrush brush,
        PdfGraphicsState state,
        double x,
        double y,
        int depth,
        PdfMatrix patternDeviceTransform)
    {
        if (brush is PdfSolidBrush solid)
            return RasterColor.FromPdf(solid.Color);
        if (brush is PdfGradientBrush gradient)
        {
            PdfGraphicsState local = state with { Transform = PdfMatrix.Identity };
            return SampleGradientInUserSpace(gradient, local, x, y);
        }
        if (brush is PdfFunctionShadingBrush function)
        {
            PdfGraphicsState local = state with { Transform = PdfMatrix.Identity };
            return SampleFunction(
                function,
                local,
                x,
                y,
                PdfMatrix.Identity);
        }
        if (brush is PdfMeshShadingBrush mesh)
        {
            PdfGraphicsState local = state with { Transform = PdfMatrix.Identity };
            return SampleMeshInUserSpace(
                mesh,
                local,
                x,
                y,
                patternDeviceTransform);
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
        return SampleMeshPoint(
            MeshTriangles(mesh, transform),
            point.X,
            point.Y);
    }

    private RasterColor SampleMeshInUserSpace(
        PdfMeshShadingBrush mesh,
        PdfGraphicsState state,
        double x,
        double y,
        PdfMatrix patternDeviceTransform)
    {
        PdfMatrix transform = mesh.Matrix.Multiply(state.Transform);
        if (!RasterGeometry.TryInvert(transform, out PdfMatrix inverse))
            return RasterColor.Transparent;
        PdfPoint point = inverse.Transform(x, y);
        PdfMatrix sourceToDevice = transform.Multiply(patternDeviceTransform);
        return SampleMeshPoint(
            MeshTriangles(mesh, sourceToDevice),
            point.X,
            point.Y);
    }

    private static RasterColor SampleMeshPoint(
        IReadOnlyList<PdfMeshTriangle> triangles,
        double x,
        double y)
    {
        foreach (PdfMeshTriangle triangle in triangles)
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

    private IReadOnlyList<PdfMeshTriangle> MeshTriangles(
        PdfMeshShadingBrush mesh,
        PdfMatrix sourceToDevice)
    {
        if (ReferenceEquals(mesh, _sampleMeshCacheBrush) &&
            sourceToDevice == _sampleMeshCacheTransform &&
            _sampleMeshCacheTriangles is not null)
        {
            return _sampleMeshCacheTriangles;
        }

        IReadOnlyList<PdfMeshTriangle> triangles = PdfMeshTessellator.Tessellate(
            mesh,
            sourceToDevice,
            _page.ReadOptions.MaximumMeshTriangles);
        _sampleMeshCacheBrush = mesh;
        _sampleMeshCacheTransform = sourceToDevice;
        _sampleMeshCacheTriangles = triangles;
        return triangles;
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
            if (_softMaskRenderDepth >=
                _page.ReadOptions.MaximumTransparencyGroupDepth)
            {
                throw new PdfLimitException(
                    "Soft-mask nesting exceeds the configured limit.");
            }
            _softMaskRenderDepth++;
            try
            {
                using RasterSurface backdrop = CreateSurface();
                backdrop.Clear(mask.Mode == PdfSoftMaskMode.Luminosity
                    ? RasterColor.FromPdf(mask.Backdrop)
                    : RasterColor.Transparent);
                using TransparencyResult result = RenderTransparencySurface(
                    backdrop,
                    mask.Elements,
                    mask.Isolated,
                    mask.Knockout,
                    depth: _softMaskRenderDepth);
                surface = result.DetachSurface();
                _softMaskCache[mask] = surface;
            }
            finally
            {
                _softMaskRenderDepth--;
            }
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

    private sealed class TransparencyResult : IDisposable
    {
        private bool _ownsSurface = true;

        public TransparencyResult(
            RasterSurface initialBackdrop,
            RasterSurface surface)
        {
            InitialBackdrop = initialBackdrop;
            Surface = surface;
        }

        public RasterSurface InitialBackdrop { get; }
        public RasterSurface Surface { get; }

        public RasterSurface DetachSurface()
        {
            if (!_ownsSurface)
                throw new InvalidOperationException("Surface ownership was already transferred.");
            _ownsSurface = false;
            return Surface;
        }

        public void Dispose()
        {
            if (_ownsSurface)
                Surface.Dispose();
            InitialBackdrop.Dispose();
        }
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

    private sealed class RasterMeshIndex
    {
        private const int LeafSize = 8;
        private readonly RasterMeshTriangle[] _triangles;
        private readonly int[] _indices;
        private readonly RasterMeshNode[] _nodes;
        private int _nodeCount;

        public RasterMeshIndex(RasterMeshTriangle[] triangles)
        {
            ArgumentNullException.ThrowIfNull(triangles);
            if (triangles.Length == 0)
                throw new ArgumentException("A mesh index requires triangles.", nameof(triangles));
            _triangles = triangles;
            _indices = Enumerable.Range(0, triangles.Length).ToArray();
            _nodes = new RasterMeshNode[checked(triangles.Length * 2)];
            int root = Build(start: 0, triangles.Length);
            Bounds = _nodes[root].Bounds;
        }

        public RasterBounds Bounds { get; }

        public bool TrySample(double x, double y, out RasterColor color)
        {
            int bestIndex = int.MaxValue;
            double first = 0;
            double second = 0;
            double third = 0;
            Search(
                nodeIndex: 0,
                x,
                y,
                ref bestIndex,
                ref first,
                ref second,
                ref third);
            if (bestIndex == int.MaxValue)
            {
                color = RasterColor.Transparent;
                return false;
            }
            color = InterpolateTriangleColors(
                _triangles[bestIndex].Source,
                first,
                second,
                third);
            return true;
        }

        private int Build(int start, int count)
        {
            RasterBounds bounds = BoundsFor(start, count);
            int nodeIndex = _nodeCount++;
            if (count <= LeafSize)
            {
                _nodes[nodeIndex] = new RasterMeshNode(
                    bounds,
                    start,
                    count,
                    -1,
                    -1);
                return nodeIndex;
            }

            bool splitX = bounds.Right - bounds.Left >=
                          bounds.Bottom - bounds.Top;
            Array.Sort(
                _indices,
                start,
                count,
                new RasterMeshTriangleIndexComparer(_triangles, splitX));
            int leftCount = count / 2;
            int left = Build(start, leftCount);
            int right = Build(start + leftCount, count - leftCount);
            _nodes[nodeIndex] = new RasterMeshNode(
                bounds,
                start,
                count,
                left,
                right);
            return nodeIndex;
        }

        private RasterBounds BoundsFor(int start, int count)
        {
            RasterBounds first = _triangles[_indices[start]].Bounds;
            double left = first.Left;
            double top = first.Top;
            double right = first.Right;
            double bottom = first.Bottom;
            for (int offset = 1; offset < count; offset++)
            {
                RasterBounds bounds = _triangles[_indices[start + offset]].Bounds;
                left = Math.Min(left, bounds.Left);
                top = Math.Min(top, bounds.Top);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
            }
            return new RasterBounds(left, top, right, bottom);
        }

        private void Search(
            int nodeIndex,
            double x,
            double y,
            ref int bestIndex,
            ref double bestFirst,
            ref double bestSecond,
            ref double bestThird)
        {
            RasterMeshNode node = _nodes[nodeIndex];
            if (!Contains(node.Bounds, x, y))
                return;
            if (node.Left >= 0)
            {
                Search(
                    node.Left,
                    x,
                    y,
                    ref bestIndex,
                    ref bestFirst,
                    ref bestSecond,
                    ref bestThird);
                Search(
                    node.Right,
                    x,
                    y,
                    ref bestIndex,
                    ref bestFirst,
                    ref bestSecond,
                    ref bestThird);
                return;
            }

            for (int offset = 0; offset < node.Count; offset++)
            {
                int triangleIndex = _indices[node.Start + offset];
                if (triangleIndex >= bestIndex)
                    continue;
                RasterMeshTriangle triangle = _triangles[triangleIndex];
                if (!Contains(triangle.Bounds, x, y) ||
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
                bestIndex = triangleIndex;
                bestFirst = first;
                bestSecond = second;
                bestThird = third;
            }
        }

        private static bool Contains(RasterBounds bounds, double x, double y) =>
            x >= bounds.Left &&
            x <= bounds.Right &&
            y >= bounds.Top &&
            y <= bounds.Bottom;

        private readonly record struct RasterMeshNode(
            RasterBounds Bounds,
            int Start,
            int Count,
            int Left,
            int Right);

        private sealed class RasterMeshTriangleIndexComparer : IComparer<int>
        {
            private readonly RasterMeshTriangle[] _triangles;
            private readonly bool _xAxis;

            public RasterMeshTriangleIndexComparer(
                RasterMeshTriangle[] triangles,
                bool xAxis)
            {
                _triangles = triangles;
                _xAxis = xAxis;
            }

            public int Compare(int first, int second)
            {
                RasterBounds firstBounds = _triangles[first].Bounds;
                RasterBounds secondBounds = _triangles[second].Bounds;
                double firstCenter = _xAxis
                    ? firstBounds.Left + firstBounds.Right
                    : firstBounds.Top + firstBounds.Bottom;
                double secondCenter = _xAxis
                    ? secondBounds.Left + secondBounds.Right
                    : secondBounds.Top + secondBounds.Bottom;
                int comparison = firstCenter.CompareTo(secondCenter);
                return comparison != 0 ? comparison : first.CompareTo(second);
            }
        }
    }

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
        => BoundsOfRectangle(new PdfRectangle(0, 0, 1, 1), matrix);

    private static RasterBounds BoundsOfRectangle(
        PdfRectangle rectangle,
        PdfMatrix matrix)
    {
        PdfPoint[] points =
        {
            matrix.Transform(rectangle.Left, rectangle.Bottom),
            matrix.Transform(rectangle.Right, rectangle.Bottom),
            matrix.Transform(rectangle.Left, rectangle.Top),
            matrix.Transform(rectangle.Right, rectangle.Top)
        };
        return new RasterBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static RasterBounds Intersect(RasterBounds first, RasterBounds second) =>
        new(
            Math.Max(first.Left, second.Left),
            Math.Max(first.Top, second.Top),
            Math.Min(first.Right, second.Right),
            Math.Min(first.Bottom, second.Bottom));

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
