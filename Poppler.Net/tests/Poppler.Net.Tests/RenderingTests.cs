using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class RenderingTests
{
    [Test]
    public void PreservesTransparencyGroupsAndSoftMasksInDisplayList()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfGraphicsElement> graphics = document.CreatePage(0).Graphics;

        PdfTransparencyGroupElement group =
            graphics.OfType<PdfTransparencyGroupElement>().Single();
        Assert.That(group.Isolated, Is.True);
        Assert.That(group.Knockout, Is.False);
        Assert.That(group.Elements, Has.Count.EqualTo(2));
        Assert.That(group.Elements, Has.All.InstanceOf<PdfPathElement>());

        PdfPathElement masked = graphics
            .OfType<PdfPathElement>()
            .Single(element =>
                element.State.SoftMask?.Mode == PdfSoftMaskMode.Luminosity);
        Assert.That(masked.State.SoftMask!.Mode, Is.EqualTo(PdfSoftMaskMode.Luminosity));
        Assert.That(masked.State.SoftMask.Elements, Has.Count.EqualTo(1));
        Assert.That(
            masked.State.SoftMask.Elements[0],
            Is.InstanceOf<PdfShadingElement>());
    }

    [Test]
    public void RendersExpectedDimensionsAndRgbaLayout()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = document.CreatePage(0).Render(new RasterRenderOptions
        {
            Dpi = 72
        });

        Assert.That(bitmap.Width, Is.EqualTo(320));
        Assert.That(bitmap.Height, Is.EqualTo(240));
        Assert.That(bitmap.Format, Is.EqualTo(PdfPixelFormat.Rgba32));
        Assert.That(bitmap.BytesPerRow, Is.EqualTo(1280));
        Assert.That(bitmap.Data.Length, Is.EqualTo(320 * 240 * 4));
    }

    [Test]
    public void MatchesPopplerBlendAndConstantAlphaSamples()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = Render(document);

        AssertPixel(bitmap, 30, 60, 255, 0, 0, 255);
        AssertPixel(bitmap, 80, 60, 64, 0, 0, 255);
        AssertPixel(bitmap, 150, 60, 64, 64, 255, 255);
    }

    [Test]
    public void MatchesPopplerLuminositySoftMaskSamples()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = Render(document);

        AssertPixel(bitmap, 170, 60, 228, 248, 228, 255);
        AssertPixel(bitmap, 210, 60, 126, 223, 126, 255);
        AssertPixel(bitmap, 250, 60, 24, 197, 24, 255);
    }

    [Test]
    public void MatchesPopplerIsolatedGroupSamples()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = Render(document);

        AssertPixel(bitmap, 30, 160, 255, 0, 0, 255);
        AssertPixel(bitmap, 70, 160, 128, 0, 128, 255, tolerance: 1);
        AssertPixel(bitmap, 120, 160, 127, 127, 255, 255);
    }

    [Test]
    public void MatchesPopplerAlphaSoftMaskSample()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = Render(document);

        AssertPixel(bitmap, 175, 160, 236, 191, 236, 255, tolerance: 1);
    }

    [Test]
    public void AppliesClippingToCompositedShapes()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = Render(document);

        AssertPixel(bitmap, 210, 120, 255, 255, 255, 255);
        AssertPixel(bitmap, 250, 160, 255, 191, 128, 255, tolerance: 1);
    }

    [Test]
    public void SupportsTransparentPageBackground()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = document.CreatePage(0).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            Transparent = true
        });

        AssertPixel(bitmap, 319, 239, 0, 0, 0, 0);
        AssertPixel(bitmap, 30, 60, 255, 0, 0, 255);
    }

    [Test]
    public void AppliesPageRotationToDimensionsAndCoordinates()
    {
        using Document document = LoadFixture();
        PdfBitmap bitmap = document.CreatePage(1).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 1
        });

        Assert.That(bitmap.Width, Is.EqualTo(50));
        Assert.That(bitmap.Height, Is.EqualTo(100));
        AssertPixel(bitmap, 10, 10, 255, 0, 0, 255);
        AssertPixel(bitmap, 30, 10, 255, 255, 255, 255);
    }

    [Test]
    public void EncodesRenderedPageAsPng()
    {
        using Document document = LoadFixture();
        byte[] png = document.CreatePage(0).RenderToPng(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 1
        });

        Assert.That(
            png.AsSpan(0, 8).ToArray(),
            Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.That(
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)),
            Is.EqualTo(320u));
        Assert.That(
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)),
            Is.EqualTo(240u));
    }

    [Test]
    public void RasterizesImagesAndVectorGraphicsOnOneSurface()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "graphics-engine.pdf");
        using Document document = Document.LoadFromFile(path);
        PdfBitmap bitmap = document.CreatePage(0).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2
        });

        Assert.That(bitmap.Width, Is.EqualTo(420));
        Assert.That(bitmap.Height, Is.EqualTo(400));
        AssertPixel(bitmap, 325, 280, 255, 128, 0, 255, tolerance: 1);
        AssertPixel(bitmap, 30, 30, 235, 0, 20, 255, tolerance: 3);
    }

    [Test]
    public void RasterizesEmbeddedTrueTypeGlyphOutlines()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "truetype-cmap-fallback.pdf");
        using Document document = Document.LoadFromFile(path);
        Page page = document.CreatePage(0);
        PdfBitmap withText = page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2
        });
        PdfBitmap withoutText = page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            IncludeText = false
        });

        Assert.That(CountDarkPixels(withText), Is.GreaterThan(100));
        Assert.That(CountDarkPixels(withoutText), Is.EqualTo(0));
    }

    [Test]
    public void RenderingFixtureHashMatchesManifest()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixtureDirectory, "rendering-fixture.json")));
        string fileName = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureDirectory, fileName))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void EnforcesMaximumRenderedPixels()
    {
        using Document document = LoadFixture(new PdfReadOptions
        {
            MaximumRenderPixels = 10
        });

        Assert.That(
            (Action)(() => document.CreatePage(0).Render()),
            Throws.TypeOf<PdfLimitException>());
    }

    [TestCase(0, 4)]
    [TestCase(72, 3)]
    public void ValidatesRasterOptions(double dpi, int antialiasing)
    {
        using Document document = LoadFixture();
        var options = new RasterRenderOptions
        {
            Dpi = dpi,
            Antialiasing = antialiasing
        };

        Assert.That(
            (Action)(() => document.CreatePage(0).Render(options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidatesRenderPixelReadLimit()
    {
        var options = new PdfReadOptions
        {
            MaximumRenderPixels = 0
        };

        Assert.That(
            (Action)(() => Document.LoadFromData(new byte[8], options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static PdfBitmap Render(Document document) =>
        document.CreatePage(0).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 4
        });

    private static int CountDarkPixels(PdfBitmap bitmap)
    {
        ReadOnlySpan<byte> data = bitmap.Data.Span;
        int count = 0;
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            if (data[offset] < 128 &&
                data[offset + 1] < 128 &&
                data[offset + 2] < 128 &&
                data[offset + 3] > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertPixel(
        PdfBitmap bitmap,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        int tolerance = 0)
    {
        int offset = y * bitmap.BytesPerRow + x * 4;
        ReadOnlySpan<byte> data = bitmap.Data.Span;
        Assert.That(data[offset], Is.InRange(
            (byte)Math.Max(0, red - tolerance),
            (byte)Math.Min(255, red + tolerance)));
        Assert.That(data[offset + 1], Is.InRange(
            (byte)Math.Max(0, green - tolerance),
            (byte)Math.Min(255, green + tolerance)));
        Assert.That(data[offset + 2], Is.InRange(
            (byte)Math.Max(0, blue - tolerance),
            (byte)Math.Min(255, blue + tolerance)));
        Assert.That(data[offset + 3], Is.InRange(
            (byte)Math.Max(0, alpha - tolerance),
            (byte)Math.Min(255, alpha + tolerance)));
    }

    private static Document LoadFixture(PdfReadOptions? options = null)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "rendering-transparency.pdf");
        return Document.LoadFromFile(path, options: options);
    }
}
