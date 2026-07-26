using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class GraphicsTests
{
    [Test]
    public void InterpretsPathsGraphicsStateAndClipping()
    {
        using Document document = LoadGraphicsFixture();
        Page page = document.CreatePage(0);

        Assert.That(page.Graphics, Has.Count.EqualTo(8));
        PdfPathElement first = page.Graphics.OfType<PdfPathElement>().First();
        Assert.That(first.PaintMode, Is.EqualTo(PdfPaintMode.Fill | PdfPaintMode.Stroke));
        Assert.That(first.Path.Segments, Has.Count.EqualTo(4));
        Assert.That(first.State.Transform, Is.EqualTo(new PdfMatrix(1, 0, 0, 1, 10, 20)));
        Assert.That(first.State.LineWidth, Is.EqualTo(3));
        Assert.That(first.State.LineCap, Is.EqualTo(PdfLineCap.Round));
        Assert.That(first.State.LineJoin, Is.EqualTo(PdfLineJoin.Bevel));
        Assert.That(first.State.MiterLimit, Is.EqualTo(12));
        Assert.That(first.State.Dash.Segments, Is.EqualTo(new[] { 4d, 2d }));
        Assert.That(first.State.Dash.Phase, Is.EqualTo(1));
        Assert.That(first.State.FillAlpha, Is.EqualTo(0.5));
        Assert.That(first.State.StrokeAlpha, Is.EqualTo(0.75));

        PdfPathElement clipped = page.Graphics
            .OfType<PdfPathElement>()
            .First(element => element.ClipPaths.Count > 0 &&
                              element.SourceResource is null);
        Assert.That(clipped.ClipPaths, Has.Count.EqualTo(1));
        Assert.That(clipped.ClipPaths[0].FillRule, Is.EqualTo(PdfFillRule.NonZero));
        Assert.That(clipped.State.Transform, Is.EqualTo(PdfMatrix.Identity));
    }

    [Test]
    public void InterpretsFormAndImageXObjects()
    {
        using Document document = LoadGraphicsFixture();
        IReadOnlyList<PdfGraphicsElement> graphics = document.CreatePage(0).Graphics;

        PdfPathElement[] formPaths = graphics
            .OfType<PdfPathElement>()
            .Where(element => element.SourceResource == "Fm1")
            .ToArray();
        Assert.That(formPaths, Has.Length.EqualTo(2));
        Assert.That(
            formPaths.All(element =>
                element.State.Transform == new PdfMatrix(2, 0, 0, 2, 200, 100)),
            Is.True);
        Assert.That(formPaths.All(element => element.ClipPaths.Count == 1), Is.True);
        Assert.That(
            formPaths.SelectMany(element => element.Path.Segments)
                .OfType<PdfCubicBezierTo>()
                .Count(),
            Is.EqualTo(1));

        PdfImageElement image = graphics.OfType<PdfImageElement>().Single();
        Assert.That(image.ResourceName, Is.EqualTo("Im1"));
        Assert.That(image.Width, Is.EqualTo(1));
        Assert.That(image.Height, Is.EqualTo(1));
        Assert.That(image.BitsPerComponent, Is.EqualTo(8));
        Assert.That(image.ColorSpace, Is.EqualTo("DeviceRGB"));
        Assert.That(image.State.Transform, Is.EqualTo(new PdfMatrix(50, 0, 0, 40, 300, 100)));
    }

    [Test]
    public void InterpretsTilingAndShadingPatterns()
    {
        using Document document = LoadGraphicsFixture();
        IReadOnlyList<PdfGraphicsElement> graphics = document.CreatePage(0).Graphics;

        PdfPathElement tilingPath = graphics
            .OfType<PdfPathElement>()
            .Single(element => element.State.Fill is PdfTilingPatternBrush);
        var tiling = (PdfTilingPatternBrush)tilingPath.State.Fill;
        Assert.That(tiling.ResourceName, Is.EqualTo("P1"));
        Assert.That(tiling.XStep, Is.EqualTo(10));
        Assert.That(tiling.YStep, Is.EqualTo(10));
        Assert.That(tiling.Elements, Has.Count.EqualTo(2));

        PdfPathElement radialPath = graphics
            .OfType<PdfPathElement>()
            .Single(element => element.State.Fill is PdfGradientBrush);
        var radial = (PdfGradientBrush)radialPath.State.Fill;
        Assert.That(radial.Kind, Is.EqualTo(PdfShadingKind.Radial));
        Assert.That(radial.Coordinates, Has.Count.EqualTo(6));
        Assert.That(radial.Stops, Has.Count.EqualTo(33));

        PdfShadingElement axialElement = graphics.OfType<PdfShadingElement>().Single();
        Assert.That(axialElement.Shading.Kind, Is.EqualTo(PdfShadingKind.Axial));
        Assert.That(axialElement.Shading.ExtendStart, Is.True);
        Assert.That(axialElement.Shading.ExtendEnd, Is.True);
        Assert.That(axialElement.Shading.Stops[0].Color, Is.EqualTo(PdfColor.Rgb(1, 0, 0)));
        Assert.That(axialElement.Shading.Stops[^1].Color, Is.EqualTo(PdfColor.Rgb(0, 0, 1)));
    }

    [Test]
    public void EmitsVectorSvgDefinitionsAndImageDiagnostics()
    {
        using Document document = LoadGraphicsFixture();
        string svg = document.CreatePage(0).RenderToSvg(new SvgRenderOptions
        {
            IncludeText = false,
            DrawImageBounds = true
        });

        Assert.That(svg, Does.Contain("<path"));
        Assert.That(svg, Does.Contain("<clipPath"));
        Assert.That(svg, Does.Contain("<pattern"));
        Assert.That(svg, Does.Contain("<linearGradient"));
        Assert.That(svg, Does.Contain("<radialGradient"));
        Assert.That(svg, Does.Contain("fill-opacity=\"0.5\""));
        Assert.That(svg, Does.Contain("stroke-dasharray=\"4 2\""));
        Assert.That(svg, Does.Contain("Image /Im1: 1x1, DeviceRGB"));
    }

    [Test]
    public void GraphicsFixtureHashMatchesManifest()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixtureDirectory, "graphics-fixture.json")));
        string fileName = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureDirectory, fileName))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("operations")]
    [TestCase("elements")]
    [TestCase("segments")]
    public void EnforcesGraphicsResourceLimits(string limit)
    {
        PdfReadOptions options = limit switch
        {
            "operations" => new PdfReadOptions { MaximumGraphicsOperations = 5 },
            "elements" => new PdfReadOptions { MaximumGraphicsElements = 1 },
            _ => new PdfReadOptions { MaximumPathSegments = 3 }
        };
        using Document document = LoadGraphicsFixture(options);
        Page page = document.CreatePage(0);

        Assert.That(
            (Action)(() => _ = page.Graphics),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void MatrixCompositionUsesPdfOrder()
    {
        var scale = new PdfMatrix(2, 0, 0, 3, 0, 0);
        var translate = new PdfMatrix(1, 0, 0, 1, 10, 20);

        PdfPoint point = scale.Multiply(translate).Transform(1, 1);

        Assert.That(point, Is.EqualTo(new PdfPoint(12, 23)));
    }

    private static Document LoadGraphicsFixture(PdfReadOptions? options = null)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "graphics-engine.pdf");
        return Document.LoadFromFile(path, options: options);
    }
}
