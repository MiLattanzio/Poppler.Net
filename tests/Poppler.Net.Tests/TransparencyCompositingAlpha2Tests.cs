using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class TransparencyCompositingAlpha2Tests
{
    [Test]
    public void CorpusCoversGroupsMasksBlendsAndNestedGraphics()
    {
        using Document document = LoadCorpus();
        Assert.That(document.Pages, Is.EqualTo(6));

        PdfTransparencyGroupElement[] matrix = document.CreatePage(0).Graphics
            .OfType<PdfTransparencyGroupElement>()
            .ToArray();
        Assert.That(
            matrix.Select(group => (group.Isolated, group.Knockout)),
            Is.EqualTo(new[]
            {
                (false, false),
                (true, false),
                (false, true),
                (true, true)
            }));

        PdfTransparencyGroupElement nested = document.CreatePage(1).Graphics
            .OfType<PdfTransparencyGroupElement>()
            .First();
        Assert.That(GroupDepth(nested), Is.EqualTo(3));
        Assert.That(
            document.CreatePage(1).Graphics
                .OfType<PdfTransparencyGroupElement>()
                .Select(group => group.State.BlendMode),
            Is.EqualTo(new[] { "Multiply", "Hue" }));

        PdfSoftMask[] masks = Descendants(document.CreatePage(2).Graphics)
            .Select(element => element.State.SoftMask)
            .Where(mask => mask is not null)
            .Cast<PdfSoftMask>()
            .Distinct()
            .ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(masks.Select(mask => mask.Mode).Distinct(),
                Is.EquivalentTo(new[]
                {
                    PdfSoftMaskMode.Alpha,
                    PdfSoftMaskMode.Luminosity
                }));
            Assert.That(masks.Single(mask =>
                mask.Mode == PdfSoftMaskMode.Luminosity).HasTransferFunction,
                Is.True);
            Assert.That(masks.Single(mask =>
                mask.Mode == PdfSoftMaskMode.Luminosity).Backdrop,
                Is.EqualTo(PdfColor.Rgb(0.2, 0.4, 0.6)));
            Assert.That(document.CreatePage(2).Graphics
                .Any(element => element.ClipPaths.Count > 0), Is.True);
        }));

        PdfGraphicsElement[] mixed = Descendants(
            document.CreatePage(3).Graphics).ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(mixed, Has.Some.InstanceOf<PdfTextElement>());
            Assert.That(mixed, Has.Some.InstanceOf<PdfImageElement>());
            Assert.That(mixed, Has.Some.InstanceOf<PdfShadingElement>());
            Assert.That(mixed.OfType<PdfPathElement>()
                .Any(path => path.State.Fill is PdfTilingPatternBrush), Is.True);
            Assert.That(document.CreatePage(3).Annotations, Has.Count.EqualTo(1));
        }));

        string[] blendModes = document.CreatePage(4).Graphics
            .OfType<PdfTransparencyGroupElement>()
            .Select(group => group.State.BlendMode)
            .ToArray();
        Assert.That(blendModes, Has.Length.EqualTo(16));
        Assert.That(blendModes,
            Does.Contain("Multiply").And.Contain("Hue")
                .And.Contain("Color").And.Contain("Luminosity"));
    }

    [Test]
    public void TwoByTwoCorpusPageMatchesTheNumericFormulas()
    {
        using Document document = LoadCorpus();
        PdfBitmap bitmap = document.CreatePage(5).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 1,
            UseFontSubstitution = false
        });

        Assert.Multiple((Action)(() =>
        {
            AssertPixel(bitmap, 0, 0, 191, 159, 223, 255);
            AssertPixel(bitmap, 1, 0, 128, 128, 255, 255);
            AssertPixel(bitmap, 0, 1, 128, 0, 128, 255);
            AssertPixel(bitmap, 1, 1, 255, 255, 255, 255);
        }));
    }

    [Test]
    public void CorpusRasterAndSvgOutputsMatchTheVersionedManifest()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(FixtureDirectory(), "transparency-alpha2-fixture.json")));
        JsonElement root = manifest.RootElement;
        Assert.That(
            Hash(File.ReadAllBytes(Path.Combine(
                FixtureDirectory(),
                root.GetProperty("file").GetString()!))),
            Is.EqualTo(root.GetProperty("sha256").GetString()));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                root.GetProperty("managed_png_hash_mode").GetString(),
                Is.EqualTo(CanonicalRenderingHash.PngMode));
            Assert.That(
                root.GetProperty("managed_svg_hash_mode").GetString(),
                Is.EqualTo(CanonicalRenderingHash.SvgMode));
        }));

        const string renderKey = "dpi72-aa4-opaque-fixed-fonts";
        JsonElement expectedPngHashes = root.GetProperty("managed_png_sha256")
            .GetProperty(renderKey);
        string[] expectedPng = expectedPngHashes
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        JsonElement svgHashes = root.GetProperty("managed_svg_sha256");
        string[] expectedSvg = svgHashes
            .GetProperty("alpha3-bounded-fallback")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        using Document document = LoadCorpus();
        string fontDirectory = Path.Combine(FixtureDirectory(), "beta-fonts");
        var actualPng = new string[document.Pages];
        var actualSvg = new string[document.Pages];
        for (int index = 0; index < document.Pages; index++)
        {
            Page page = document.CreatePage(index);
            actualPng[index] = CanonicalRenderingHash.Png(page.RenderToPng(
                new RasterRenderOptions
            {
                Dpi = 72,
                Antialiasing = 4,
                FontDirectories = new[] { fontDirectory }
            }));
            actualSvg[index] = CanonicalRenderingHash.Svg(page.RenderToSvg());
        }

        Assert.Multiple((Action)(() =>
        {
            Assert.That(actualPng, Is.EqualTo(expectedPng), "PNG content");
            Assert.That(actualSvg, Is.EqualTo(expectedSvg), "SVG content");
        }));
    }

    [Test]
    public async Task RasterAndSvgRenderingOfOneDocumentAreConcurrentAndDeterministic()
    {
        using Document document = LoadCorpus();
        Page page = document.CreatePage(1);
        string expectedRaster = Hash(page.RenderToPng(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false
        }));
        string expectedSvg = Hash(System.Text.Encoding.UTF8.GetBytes(
            page.RenderToSvg(new SvgRenderOptions { RasterFallbackDpi = 72 })));
        Task<(string Raster, string Svg)>[] tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                string raster = Hash(page.RenderToPng(new RasterRenderOptions
                {
                    Dpi = 72,
                    Antialiasing = 2,
                    UseFontSubstitution = false
                }));
                string svg = Hash(System.Text.Encoding.UTF8.GetBytes(
                    page.RenderToSvg(new SvgRenderOptions
                    {
                        RasterFallbackDpi = 72
                    })));
                return (raster, svg);
            }))
            .ToArray();

        (string Raster, string Svg)[] results = await Task.WhenAll(tasks);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(results.Select(result => result.Raster),
                Has.All.EqualTo(expectedRaster));
            Assert.That(results.Select(result => result.Svg),
                Has.All.EqualTo(expectedSvg));
        }));
    }

    [Test]
    public void ExistingTransparencyDepthLimitAppliesDuringRendering()
    {
        using Document document = LoadCorpus(new PdfReadOptions
        {
            MaximumTransparencyGroupDepth = 1
        });

        Assert.That(
            (Action)(() => document.CreatePage(1).Render(new RasterRenderOptions
            {
                Dpi = 72,
                Antialiasing = 1,
                UseFontSubstitution = false
            })),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void PremultipliedSourceOverMatchesThePdfFormula()
    {
        var budget = new RenderWorkingSetBudget(1024);
        using var surface = new RasterSurface(1, 1, budget);
        surface.Clear(new RasterColor(0.2, 0.4, 0.6, 0.5));

        surface.CompositePixel(
            0,
            0,
            new RasterColor(0.8, 0.1, 0.3, 0.25),
            "Normal",
            sourceShape: 0.75);

        PremultipliedRasterColor actual = surface.GetPremultipliedPixel(0, 0);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(actual.Red, Is.EqualTo(0.275).Within(1e-6));
            Assert.That(actual.Green, Is.EqualTo(0.175).Within(1e-6));
            Assert.That(actual.Blue, Is.EqualTo(0.3).Within(1e-6));
            Assert.That(actual.Alpha, Is.EqualTo(0.625).Within(1e-6));
            Assert.That(surface.GetShape(0, 0), Is.EqualTo(0.75).Within(1e-6));
            Assert.That(
                surface.GetContributionAlpha(0, 0),
                Is.EqualTo(0.25).Within(1e-6));
        }));
    }

    [Test]
    public void ShapeIsIndependentFromOpacityAndColorEquality()
    {
        var budget = new RenderWorkingSetBudget(4096);
        using var surface = new RasterSurface(2, 2, budget);
        surface.Clear(new RasterColor(1, 1, 1, 1));

        surface.CompositePixel(
            0,
            0,
            new RasterColor(1, 1, 1, 0),
            "Normal",
            sourceShape: 0.5);
        surface.CompositePixel(
            0,
            0,
            new RasterColor(1, 1, 1, 0),
            "Normal",
            sourceShape: 0.5);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(surface.GetPixel(0, 0),
                Is.EqualTo(new RasterColor(1, 1, 1, 1)));
            Assert.That(surface.GetShape(0, 0), Is.EqualTo(0.75).Within(1e-6));
            Assert.That(surface.GetContributionAlpha(0, 0), Is.Zero);
            Assert.That(surface.GetShape(1, 1), Is.Zero);
        }));
    }

    [Test]
    public void NonIsolatedGroupRecoversSourceFromInitialBackdrop()
    {
        var budget = new RenderWorkingSetBudget(4096);
        using var backdrop = new RasterSurface(1, 1, budget);
        backdrop.Clear(new RasterColor(0, 0, 1, 1));
        using RasterSurface layer = backdrop.CloneActual();

        layer.CompositePixel(
            0,
            0,
            new RasterColor(1, 0, 0, 0.5),
            "Normal",
            sourceShape: 1);
        RasterColor recovered = layer.RecoverGroupSource(backdrop, 0, 0);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(recovered.Red, Is.EqualTo(1).Within(1e-6));
            Assert.That(recovered.Green, Is.Zero.Within(1e-6));
            Assert.That(recovered.Blue, Is.Zero.Within(1e-6));
            Assert.That(recovered.Alpha, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(layer.GetShape(0, 0), Is.EqualTo(1).Within(1e-6));
        }));
    }

    [Test]
    public void KnockoutUsesShapeEvenWhenChildMatchesBackdrop()
    {
        var budget = new RenderWorkingSetBudget(8192);
        using var initial = new RasterSurface(1, 1, budget);
        initial.Clear(new RasterColor(1, 1, 1, 1));
        using RasterSurface layer = initial.CloneActual();
        using RasterSurface first = initial.CloneActual();
        first.CompositePixel(
            0,
            0,
            new RasterColor(1, 0, 0, 1),
            "Normal",
            sourceShape: 1);
        layer.MergeKnockoutChild(initial, first);

        using RasterSurface second = initial.CloneActual();
        second.CompositePixel(
            0,
            0,
            new RasterColor(1, 1, 1, 1),
            "Normal",
            sourceShape: 1);
        layer.MergeKnockoutChild(initial, second);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(layer.GetPixel(0, 0),
                Is.EqualTo(new RasterColor(1, 1, 1, 1)));
            Assert.That(layer.GetShape(0, 0), Is.EqualTo(1).Within(1e-6));
            Assert.That(
                layer.GetContributionAlpha(0, 0),
                Is.EqualTo(1).Within(1e-6));
        }));
    }

    [Test]
    public void WorkingSurfaceBudgetIsCheckedBeforeAllocation()
    {
        var budget = new RenderWorkingSetBudget(95);

        Assert.That(
            (Action)(() => _ = new RasterSurface(2, 2, budget)),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void DefaultSvgFallbackEmbedsDeterministicManagedPng()
    {
        using Document document = LoadBeta2();
        Page page = document.CreatePage(3);

        string first = page.RenderToSvg(new SvgRenderOptions
        {
            RasterFallbackDpi = 72
        });
        string second = page.RenderToSvg(new SvgRenderOptions
        {
            RasterFallbackDpi = 72
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(first, Does.Contain("data:image/png;base64,"));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(Hash(first), Is.EqualTo(Hash(second)));
        }));
    }

    [Test]
    public void SvgOmitModeNeverSilentlySubstitutesAComplexGroup()
    {
        using Document document = LoadBeta2();
        string svg = document.CreatePage(3).RenderToSvg(new SvgRenderOptions
        {
            FallbackMode = SvgFallbackMode.Omit,
            RasterFallbackDpi = 72
        });

        Assert.That(svg, Does.Not.Contain("data:image/png;base64,"));
    }

    [Test]
    public void SvgFallbackPixelLimitIsCheckedBeforeRasterAllocation()
    {
        using Document document = LoadBeta2(new PdfReadOptions
        {
            MaximumSvgFallbackPixels = 1
        });

        Assert.That(
            (Action)(() => document.CreatePage(3).RenderToSvg()),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void RenderWorkingLimitIncludesTransparencySurfaces()
    {
        using Document document = LoadBeta2(new PdfReadOptions
        {
            MaximumRenderWorkingBytes = 240L * 160 * 24 * 2
        });

        Assert.That(
            (Action)(() => document.CreatePage(3).Render(new RasterRenderOptions
            {
                Dpi = 72,
                Antialiasing = 1,
                UseFontSubstitution = false
            })),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void ValidatesNewReadAndSvgOptions()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => Document.LoadFromData(
                    new byte[8],
                    options: new PdfReadOptions { MaximumRenderWorkingBytes = 0 })),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                (Action)(() => Document.LoadFromData(
                    new byte[8],
                    options: new PdfReadOptions { MaximumSvgFallbackPixels = 0 })),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }));

        using Document document = LoadBeta2();
        Page page = document.CreatePage(3);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => page.RenderToSvg(new SvgRenderOptions
                {
                    RasterFallbackDpi = 0
                })),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                (Action)(() => page.RenderToSvg(new SvgRenderOptions
                {
                    FallbackMode = (SvgFallbackMode)999
                })),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    private static Document LoadBeta2(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Fixtures",
                "rendering-beta2.pdf"),
            options: options);

    private static Document LoadCorpus(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "transparency-alpha2.pdf"),
            options: options);

    private static string FixtureDirectory() =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    private static IEnumerable<PdfGraphicsElement> Descendants(
        IEnumerable<PdfGraphicsElement> elements)
    {
        foreach (PdfGraphicsElement element in elements)
        {
            yield return element;
            if (element is PdfTransparencyGroupElement group)
            {
                foreach (PdfGraphicsElement child in Descendants(group.Elements))
                    yield return child;
            }
        }
    }

    private static int GroupDepth(PdfTransparencyGroupElement group) =>
        1 + group.Elements
            .OfType<PdfTransparencyGroupElement>()
            .Select(GroupDepth)
            .DefaultIfEmpty(0)
            .Max();

    private static void AssertPixel(
        PdfBitmap bitmap,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = y * bitmap.BytesPerRow + x * 4;
        ReadOnlySpan<byte> data = bitmap.Data.Span;
        Assert.That(
            new[]
            {
                data[offset],
                data[offset + 1],
                data[offset + 2],
                data[offset + 3]
            },
            Is.EqualTo(new[] { red, green, blue, alpha }));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
