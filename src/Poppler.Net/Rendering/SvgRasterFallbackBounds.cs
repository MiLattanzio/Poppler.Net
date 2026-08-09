using Poppler.Graphics;

namespace Poppler.Rendering;

/// <summary>
/// Computes a conservative page-space support for the graphics flattened into
/// one SVG raster fallback. Curves use their control hulls, so the result may
/// include transparent pixels but never clips painted geometry.
/// </summary>
internal static class SvgRasterFallbackBounds
{
    public static PdfRectangle? Calculate(
        Page page,
        IReadOnlyList<PdfGraphicsElement> elements,
        RasterRenderOptions options)
    {
        PdfRectangle crop = Normalize(page.CropBox);
        if (crop.IsEmpty)
            return null;
        if (NormalizeRotation(page.Rotation) != 0)
            return crop;

        double scale = options.Dpi / 72.0;
        RasterBounds pageBounds = FromRectangle(crop);
        RasterBounds bounds = BoundsOfElements(elements, scale, pageBounds);
        bounds = Intersect(bounds, pageBounds);
        if (bounds.IsEmpty)
            return null;
        return AlignToPagePixels(bounds, crop, scale);
    }

    private static RasterBounds BoundsOfElements(
        IReadOnlyList<PdfGraphicsElement> elements,
        double scale,
        RasterBounds pageBounds)
    {
        RasterBounds bounds = RasterBounds.Empty;
        foreach (PdfGraphicsElement element in elements)
            bounds = Union(bounds, BoundsOfElement(element, scale, pageBounds));
        return bounds;
    }

    private static RasterBounds BoundsOfElement(
        PdfGraphicsElement element,
        double scale,
        RasterBounds pageBounds)
    {
        RasterBounds bounds = element switch
        {
            PdfPathElement path => BoundsOfPathElement(path, scale),
            PdfImageElement image => BoundsOfImage(image),
            PdfTextElement text => BoundsOfText(text, scale),
            PdfShadingElement shading => BoundsOfGradient(shading, pageBounds),
            PdfFunctionShadingElement function => BoundsOfFunction(function),
            PdfMeshShadingElement mesh => BoundsOfMesh(mesh),
            PdfTransparencyGroupElement group =>
                BoundsOfElements(group.Elements, scale, pageBounds),
            _ => RasterBounds.Empty
        };
        return ApplyClips(bounds, element.ClipPaths);
    }

    private static RasterBounds BoundsOfPathElement(
        PdfPathElement element,
        double scale)
    {
        RasterBounds pathBounds = PathBounds(
            element.Path,
            element.State.Transform);
        RasterBounds bounds = RasterBounds.Empty;
        if ((element.PaintMode & PdfPaintMode.Fill) != 0 &&
            BrushCanPaint(element.State.Fill, element.State))
        {
            bounds = pathBounds;
        }
        if ((element.PaintMode & PdfPaintMode.Stroke) != 0 &&
            BrushCanPaint(element.State.Stroke, element.State) &&
            !RasterGeometry.IsNearSingular(element.State.Transform))
        {
            bounds = Union(
                bounds,
                StrokeBounds(pathBounds, element.State, scale));
        }
        return bounds;
    }

    private static RasterBounds BoundsOfImage(PdfImageElement element)
    {
        if (element.Image is null ||
            RasterGeometry.IsNearSingular(element.State.Transform))
        {
            return RasterBounds.Empty;
        }
        return RectangleBounds(
            new PdfRectangle(0, 0, 1, 1),
            element.State.Transform);
    }

    private static RasterBounds BoundsOfText(
        PdfTextElement element,
        double scale)
    {
        if (element.RenderingMode is
            PdfTextRenderingMode.Invisible or PdfTextRenderingMode.Clip)
        {
            return RasterBounds.Empty;
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
        bool paintsFill = fill && BrushCanPaint(element.State.Fill, element.State);
        bool paintsStroke = stroke &&
            BrushCanPaint(element.State.Stroke, element.State) &&
            !RasterGeometry.IsNearSingular(element.State.Transform);
        if (!paintsFill && !paintsStroke)
            return RasterBounds.Empty;

        RasterBounds bounds = RasterBounds.Empty;
        foreach (Text.PdfTextGlyphPlacement placement in element.Glyphs)
        {
            RasterBounds glyphBounds;
            if (element.Font.TryGetGlyphOutline(
                    placement.Glyph,
                    out PdfGraphicsPath outline,
                    out _,
                    out _,
                    out _))
            {
                glyphBounds = PathBounds(outline, placement.Transform);
            }
            else
            {
                double advance = Math.Max(
                    Math.Abs(placement.Glyph.AdvanceX),
                    Math.Abs(placement.Glyph.AdvanceY)) / 1000.0;
                glyphBounds = RectangleBounds(
                    new PdfRectangle(-2, -2, Math.Max(3, advance + 2), 3),
                    placement.Transform);
            }

            if (paintsStroke)
                glyphBounds = StrokeBounds(glyphBounds, element.State, scale);
            bounds = Union(bounds, glyphBounds);
        }
        return bounds;
    }

    private static RasterBounds BoundsOfGradient(
        PdfShadingElement element,
        RasterBounds pageBounds)
    {
        PdfMatrix transform = element.Shading.Matrix.Multiply(element.State.Transform);
        return RasterGeometry.TryInvert(transform, out _)
            ? pageBounds
            : RasterBounds.Empty;
    }

    private static RasterBounds BoundsOfFunction(PdfFunctionShadingElement element)
    {
        PdfFunctionShadingBrush shading = element.Shading;
        PdfMatrix transform = shading.Matrix.Multiply(element.State.Transform);
        if (!RasterGeometry.TryInvert(transform, out _))
            return RasterBounds.Empty;
        RasterBounds bounds = RectangleBounds(shading.Domain, transform);
        if (shading.BoundingBox is { } boundingBox)
        {
            PdfMatrix boxTransform = shading.BoundingBoxMatrix
                .Multiply(element.State.Transform);
            if (!RasterGeometry.TryInvert(boxTransform, out _))
                return RasterBounds.Empty;
            bounds = Intersect(
                bounds,
                RectangleBounds(boundingBox, boxTransform));
        }
        return bounds;
    }

    private static RasterBounds BoundsOfMesh(PdfMeshShadingElement element)
    {
        PdfMeshShadingBrush shading = element.Shading;
        PdfMatrix transform = shading.Matrix.Multiply(element.State.Transform);
        if (!transform.IsFinite)
            return RasterBounds.Empty;
        RasterBounds bounds = RasterBounds.Empty;
        if (shading.Patches.Count > 0)
        {
            foreach (PdfMeshPatch patch in shading.Patches)
            {
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        PdfPoint point = patch.GetControlPoint(row, column);
                        bounds = Include(bounds, transform.Transform(point.X, point.Y));
                    }
                }
            }
            return bounds;
        }

        foreach (PdfMeshTriangle triangle in shading.Triangles)
        {
            bounds = Include(bounds, transform.Transform(
                triangle.First.Point.X,
                triangle.First.Point.Y));
            bounds = Include(bounds, transform.Transform(
                triangle.Second.Point.X,
                triangle.Second.Point.Y));
            bounds = Include(bounds, transform.Transform(
                triangle.Third.Point.X,
                triangle.Third.Point.Y));
        }
        return bounds;
    }

    private static bool BrushCanPaint(PdfBrush brush, PdfGraphicsState state) =>
        brush switch
        {
            PdfSolidBrush => true,
            PdfGradientBrush gradient => RasterGeometry.TryInvert(
                gradient.Matrix.Multiply(state.Transform),
                out _),
            PdfFunctionShadingBrush function => RasterGeometry.TryInvert(
                function.Matrix.Multiply(state.Transform),
                out _),
            PdfMeshShadingBrush mesh => RasterGeometry.TryInvert(
                mesh.Matrix.Multiply(state.Transform),
                out _),
            PdfTilingPatternBrush pattern =>
                Math.Abs(pattern.XStep) > 1e-12 &&
                Math.Abs(pattern.YStep) > 1e-12 &&
                pattern.Elements.Count > 0 &&
                RasterGeometry.TryInvert(
                    pattern.Matrix.Multiply(state.Transform),
                    out _),
            _ => false
        };

    private static RasterBounds ApplyClips(
        RasterBounds bounds,
        IReadOnlyList<PdfClipPath> clips)
    {
        foreach (PdfClipPath clip in clips)
        {
            if (RasterGeometry.IsNearSingular(clip.Transform))
                return RasterBounds.Empty;
            bounds = Intersect(bounds, PathBounds(clip.Path, clip.Transform));
            if (bounds.IsEmpty)
                return bounds;
        }
        return bounds;
    }

    private static RasterBounds PathBounds(PdfGraphicsPath path, PdfMatrix transform)
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
                        curve.Control1.X,
                        curve.Control1.Y));
                    bounds = Include(bounds, transform.Transform(
                        curve.Control2.X,
                        curve.Control2.Y));
                    bounds = Include(bounds, transform.Transform(curve.End.X, curve.End.Y));
                    break;
            }
        }
        return bounds;
    }

    private static RasterBounds StrokeBounds(
        RasterBounds bounds,
        PdfGraphicsState state,
        double scale)
    {
        if (!HasFiniteCoordinates(bounds))
            return RasterBounds.Empty;
        if (state.LineWidth <= 1e-12)
            return Expand(bounds, 0.5 / scale, 0.5 / scale);

        double factor = 1;
        if (state.LineJoin == PdfLineJoin.Miter)
            factor = Math.Max(factor, state.MiterLimit);
        if (state.LineCap == PdfLineCap.Square)
            factor = Math.Max(factor, Math.Sqrt(2));
        double radius = state.LineWidth * 0.5 * factor;
        PdfMatrix matrix = state.Transform;
        double expandX = radius * Math.Sqrt(matrix.A * matrix.A + matrix.C * matrix.C);
        double expandY = radius * Math.Sqrt(matrix.B * matrix.B + matrix.D * matrix.D);
        return double.IsFinite(expandX) && double.IsFinite(expandY)
            ? Expand(bounds, expandX, expandY)
            : RasterBounds.Empty;
    }

    private static RasterBounds RectangleBounds(
        PdfRectangle rectangle,
        PdfMatrix transform)
    {
        if (!transform.IsFinite)
            return RasterBounds.Empty;
        RasterBounds bounds = RasterBounds.Empty;
        bounds = Include(bounds, transform.Transform(rectangle.Left, rectangle.Bottom));
        bounds = Include(bounds, transform.Transform(rectangle.Right, rectangle.Bottom));
        bounds = Include(bounds, transform.Transform(rectangle.Left, rectangle.Top));
        return Include(bounds, transform.Transform(rectangle.Right, rectangle.Top));
    }

    private static PdfRectangle? AlignToPagePixels(
        RasterBounds bounds,
        PdfRectangle crop,
        double scale)
    {
        double left = crop.Left + Math.Floor((bounds.Left - crop.Left) * scale) / scale;
        double right = crop.Left + Math.Ceiling((bounds.Right - crop.Left) * scale) / scale;
        double top = crop.Top - Math.Floor((crop.Top - bounds.Bottom) * scale) / scale;
        double bottom = crop.Top - Math.Ceiling((crop.Top - bounds.Top) * scale) / scale;
        var aligned = new PdfRectangle(
            Math.Max(crop.Left, left),
            Math.Max(crop.Bottom, bottom),
            Math.Min(crop.Right, right),
            Math.Min(crop.Top, top));
        return aligned.IsEmpty ? null : aligned;
    }

    private static RasterBounds FromRectangle(PdfRectangle rectangle) =>
        new(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top);

    private static PdfRectangle Normalize(PdfRectangle rectangle) =>
        new(
            Math.Min(rectangle.Left, rectangle.Right),
            Math.Min(rectangle.Bottom, rectangle.Top),
            Math.Max(rectangle.Left, rectangle.Right),
            Math.Max(rectangle.Bottom, rectangle.Top));

    private static RasterBounds Include(RasterBounds bounds, PdfPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y)
            ? bounds.Include(point)
            : bounds;

    private static RasterBounds Expand(
        RasterBounds bounds,
        double horizontal,
        double vertical) =>
        new(
            bounds.Left - horizontal,
            bounds.Top - vertical,
            bounds.Right + horizontal,
            bounds.Bottom + vertical);

    private static bool HasFiniteCoordinates(RasterBounds bounds) =>
        double.IsFinite(bounds.Left) &&
        double.IsFinite(bounds.Top) &&
        double.IsFinite(bounds.Right) &&
        double.IsFinite(bounds.Bottom);

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

    private static RasterBounds Intersect(RasterBounds first, RasterBounds second) =>
        first.IsEmpty || second.IsEmpty
            ? RasterBounds.Empty
            : new RasterBounds(
                Math.Max(first.Left, second.Left),
                Math.Max(first.Top, second.Top),
                Math.Min(first.Right, second.Right),
                Math.Min(first.Bottom, second.Bottom));

    private static int NormalizeRotation(int rotation)
    {
        int normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
