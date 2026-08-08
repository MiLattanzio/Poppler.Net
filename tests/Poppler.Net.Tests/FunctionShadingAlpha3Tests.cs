using Poppler;
using Poppler.Graphics;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class FunctionShadingAlpha3Tests
{
    [Test]
    public void ExposesFunctionShadingWithoutChangingGradientElements()
    {
        using Document document = Load();
        PdfFunctionShadingElement shading = document.CreatePage(0).Graphics
            .OfType<PdfFunctionShadingElement>()
            .Single();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(shading.ResourceName, Is.EqualTo("S"));
            Assert.That(shading.Shading.Kind, Is.EqualTo(PdfShadingKind.FunctionBased));
            Assert.That(
                shading.Shading.Domain,
                Is.EqualTo(new PdfRectangle(2, 10, 4, 20)));
            Assert.That(
                shading.Shading.BoundingBox,
                Is.EqualTo(new PdfRectangle(25, 0, 75, 100)));
            Assert.That(
                shading.Shading.Matrix,
                Is.EqualTo(new PdfMatrix(50, 0, 0, 10, -100, -100)));
            Assert.That(document.CreatePage(0).Graphics.OfType<PdfShadingElement>(), Is.Empty);
        }));
    }

    [Test]
    public void RendersSampledCalculatorAndComponentFunctionArrays()
    {
        using Document document = Load();

        PdfBitmap sampled = Render(document.CreatePage(0));
        AssertPixel(sampled, 50, 50, 129, 126, 0, tolerance: 2);
        AssertPixel(sampled, 10, 50, 255, 255, 255);
        AssertPixel(sampled, 90, 50, 255, 255, 255);

        PdfBitmap calculator = Render(document.CreatePage(1));
        AssertPixel(calculator, 25, 75, 65, 62, 0, tolerance: 2);
        AssertPixel(calculator, 75, 75, 255, 255, 255);

        PdfBitmap components = Render(document.CreatePage(2));
        AssertPixel(components, 50, 50, 129, 126, 128, tolerance: 2);
    }

    [Test]
    public void RendersFunctionShadingPatternsAndSkipsSingularTransforms()
    {
        using Document document = Load();

        PdfFunctionShadingElement singular = document.CreatePage(3).Graphics
            .OfType<PdfFunctionShadingElement>()
            .Single();
        Assert.That(singular.Shading.Matrix, Is.EqualTo(new PdfMatrix(0, 0, 0, 0, 50, 50)));
        PdfBitmap singularBitmap = Render(document.CreatePage(3));
        AssertPixel(singularBitmap, 50, 50, 255, 255, 255);

        PdfPathElement path = document.CreatePage(4).Graphics
            .OfType<PdfPathElement>()
            .Single();
        Assert.That(path.State.Fill, Is.InstanceOf<PdfFunctionShadingBrush>());
        PdfBitmap pattern = Render(document.CreatePage(4));
        AssertPixel(pattern, 50, 50, 129, 126, 0, tolerance: 2);
    }

    [Test]
    public void UsesDeterministicSvgFallbackAndPreservesOmitMode()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);
        var rasterize = new SvgRenderOptions
        {
            IncludeText = false,
            RasterFallbackDpi = 72
        };
        string first = page.RenderToSvg(rasterize);
        string second = page.RenderToSvg(rasterize);
        string omitted = page.RenderToSvg(rasterize with
        {
            FallbackMode = SvgFallbackMode.Omit
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Contain("data:image/png;base64,"));
            Assert.That(omitted, Does.Not.Contain("data:image/png;base64,"));
        }));
    }

    [Test]
    public async Task ConcurrentFunctionShadingRendersAreDeterministic()
    {
        using Document document = Load();
        Page page = document.CreatePage(2);
        byte[] expected = Render(page).Data.ToArray();
        Task<byte[]>[] renders = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Render(page).Data.ToArray()))
            .ToArray();

        byte[][] results = await Task.WhenAll(renders);

        Assert.That(results, Has.All.EqualTo(expected));
    }

    [Test]
    public void EnforcesSampleTableLimitBeforeRendering()
    {
        using Document document = Load(new PdfReadOptions
        {
            MaximumFunctionSamples = 2
        });

        Assert.That(
            (Action)(() => _ = document.CreatePage(0).Graphics),
            Throws.TypeOf<PdfLimitException>());
    }

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromData(PdfFixtures.CreateFunctionShadingFixture(), options: options);

    private static PdfBitmap Render(Page page) =>
        page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 1,
            UseFontSubstitution = false
        });

    private static void AssertPixel(
        PdfBitmap bitmap,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        int tolerance = 0)
    {
        ReadOnlySpan<byte> pixels = bitmap.Data.Span;
        int offset = y * bitmap.BytesPerRow + x * 4;
        byte actualRed = pixels[offset];
        byte actualGreen = pixels[offset + 1];
        byte actualBlue = pixels[offset + 2];
        byte actualAlpha = pixels[offset + 3];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(actualRed, Is.InRange(
                (byte)Math.Max(0, red - tolerance),
                (byte)Math.Min(255, red + tolerance)));
            Assert.That(actualGreen, Is.InRange(
                (byte)Math.Max(0, green - tolerance),
                (byte)Math.Min(255, green + tolerance)));
            Assert.That(actualBlue, Is.InRange(
                (byte)Math.Max(0, blue - tolerance),
                (byte)Math.Min(255, blue + tolerance)));
            Assert.That(actualAlpha, Is.EqualTo(255));
        }));
    }
}
