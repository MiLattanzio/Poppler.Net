using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class ImageAndColorTests
{
    [Test]
    public void DecodesAllImageCompressionFamilies()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfImage> images = document.CreatePage(0).Images;

        Assert.That(images, Has.Count.EqualTo(12));
        Assert.That(Image(images, "Raw").Compression, Is.EqualTo(PdfImageCompression.Raw));
        Assert.That(Image(images, "Jpeg").Compression, Is.EqualTo(PdfImageCompression.Jpeg));
        Assert.That(Image(images, "Jpx").Compression, Is.EqualTo(PdfImageCompression.Jpeg2000));
        Assert.That(Image(images, "Fax").Compression, Is.EqualTo(PdfImageCompression.CcittFax));
        Assert.That(Image(images, "Jbig").Compression, Is.EqualTo(PdfImageCompression.Jbig2));
    }

    [Test]
    public void DecodesRawAndIndexedSamples()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfImage> images = document.CreatePage(0).Images;
        byte[] expected =
        {
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            255, 255, 255
        };

        PdfImage raw = Image(images, "Raw");
        PdfImage indexed = Image(images, "Indexed");
        Assert.That(raw.Format, Is.EqualTo(PdfPixelFormat.Rgb24));
        Assert.That(raw.BytesPerRow, Is.EqualTo(6));
        Assert.That(raw.Data.ToArray(), Is.EqualTo(expected));
        Assert.That(indexed.ColorSpace, Is.EqualTo("Indexed/DeviceRGB"));
        Assert.That(indexed.Data.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void AppliesSeparationAndDeviceNTintFunctions()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfImage> images = document.CreatePage(0).Images;

        Assert.That(
            Image(images, "Spot").Data.ToArray(),
            Is.EqualTo(new byte[] { 255, 255, 255, 255, 0, 0 }));
        Assert.That(
            Image(images, "DeviceN").Data.ToArray(),
            Is.EqualTo(new byte[]
            {
                255, 255, 255,
                255, 0, 0,
                0, 0, 255,
                0, 0, 0
            }));
    }

    [Test]
    public void ConvertsLabAndMatrixShaperIccToSrgb()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfImage> images = document.CreatePage(0).Images;

        byte[] lab = Image(images, "Lab").Data.ToArray();
        Assert.That(lab[0], Is.InRange(115, 125));
        Assert.That(lab[1], Is.InRange(115, 125));
        Assert.That(lab[2], Is.InRange(115, 125));

        PdfImage icc = Image(images, "Icc");
        Assert.That(icc.ColorSpace, Does.StartWith("ICCBased"));
        Assert.That(icc.Data.Span[0], Is.EqualTo(255));
        Assert.That(icc.Data.Span[4], Is.EqualTo(255));
        Assert.That(icc.Data.Span[8], Is.EqualTo(255));
    }

    [Test]
    public void ConvertsSpecialColorSpacesForGraphicsPaths()
    {
        using Document document = LoadFixture();
        PdfPathElement[] paths = document.CreatePage(0).Graphics
            .OfType<PdfPathElement>()
            .ToArray();

        Assert.That(paths, Has.Length.EqualTo(2));
        var spot = (PdfSolidBrush)paths[0].State.Fill;
        var lab = (PdfSolidBrush)paths[1].State.Fill;
        Assert.That(spot.Color.ToRgb(), Is.EqualTo((1d, 0d, 0d)));
        (double red, double green, double blue) = lab.Color.ToRgb();
        Assert.That(red, Is.InRange(0.45, 0.49));
        Assert.That(green, Is.InRange(0.45, 0.49));
        Assert.That(blue, Is.InRange(0.45, 0.49));
    }

    [Test]
    public void DecodesJpegAndJpeg2000Pixels()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfImage> images = document.CreatePage(0).Images;

        PdfImage jpeg = Image(images, "Jpeg");
        Assert.That(jpeg.Width, Is.EqualTo(2));
        Assert.That(jpeg.Height, Is.EqualTo(2));
        Assert.That(jpeg.Data.Span[0], Is.GreaterThanOrEqualTo(250));
        Assert.That(jpeg.Data.Span[1], Is.LessThanOrEqualTo(5));

        PdfImage jpx = Image(images, "Jpx");
        Assert.That(
            jpx.Data.ToArray(),
            Is.EqualTo(Image(images, "Raw").Data.ToArray()));
    }

    [Test]
    public void DecodesCcittGroup3AndGroup4Rows()
    {
        using Document document = LoadFixture();
        IReadOnlyList<PdfImage> images = document.CreatePage(0).Images;
        PdfImage fax = Image(images, "Fax");
        PdfImage faxGroup3 = Image(images, "FaxG3");

        Assert.That(fax.Format, Is.EqualTo(PdfPixelFormat.Gray8));
        Assert.That(fax.Width, Is.EqualTo(16));
        Assert.That(fax.Height, Is.EqualTo(8));
        Assert.That(fax.Data.Span[0], Is.EqualTo(0));
        Assert.That(fax.Data.Span[1], Is.EqualTo(255));
        Assert.That(fax.Data.Span[15], Is.EqualTo(0));
        Assert.That(fax.Data.Span[16 + 1], Is.EqualTo(0));
        Assert.That(faxGroup3.Data.ToArray(), Is.EqualTo(fax.Data.ToArray()));
    }

    [Test]
    public void DecodesJbig2PdfStream()
    {
        using Document document = LoadFixture();
        PdfImage jbig = Image(document.CreatePage(0).Images, "Jbig");

        Assert.That(jbig.Width, Is.EqualTo(96));
        Assert.That(jbig.Height, Is.EqualTo(96));
        Assert.That(jbig.Data.Length, Is.EqualTo(96 * 96 * 3));
        Assert.That(jbig.Data.Span.Contains((byte)0), Is.True);
        Assert.That(jbig.Data.Span.Contains((byte)255), Is.True);
    }

    [Test]
    public void CombinesSoftMaskAsStraightAlpha()
    {
        using Document document = LoadFixture();
        PdfImage soft = Image(document.CreatePage(0).Images, "Soft");

        Assert.That(soft.Format, Is.EqualTo(PdfPixelFormat.Rgba32));
        Assert.That(soft.BytesPerRow, Is.EqualTo(8));
        Assert.That(
            new[]
            {
                soft.Data.Span[3],
                soft.Data.Span[7],
                soft.Data.Span[11],
                soft.Data.Span[15]
            },
            Is.EqualTo(new byte[] { 255, 170, 85, 0 }));
    }

    [Test]
    public void EncodesDecodedImageAsStandardsConformantPng()
    {
        using Document document = LoadFixture();
        byte[] png = Image(document.CreatePage(0).Images, "Raw").ToPngBytes();

        Assert.That(
            png.AsSpan(0, 8).ToArray(),
            Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)), Is.EqualTo(2u));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)), Is.EqualTo(2u));
        Assert.That(png.AsSpan().IndexOf("IEND"u8), Is.GreaterThan(0));
    }

    [Test]
    public void EmbedsDecodedPixelsInSvg()
    {
        using Document document = LoadFixture();
        string svg = document.CreatePage(0).RenderToSvg(new SvgRenderOptions
        {
            IncludeText = false
        });

        Assert.That(svg, Does.Contain("<image"));
        Assert.That(svg, Does.Contain("data:image/png;base64,"));
        Assert.That(svg, Does.Contain("matrix(1 0 0 -1 0 1)"));
    }

    [Test]
    public void ImageFixtureHashMatchesManifest()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixtureDirectory, "images-color-fixture.json")));
        string fileName = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureDirectory, fileName))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void EnforcesImagePixelLimit()
    {
        using Document document = LoadFixture(new PdfReadOptions
        {
            MaximumImagePixels = 1_000
        });

        Assert.That(
            (Action)(() => _ = document.CreatePage(0).Images),
            Throws.TypeOf<PdfLimitException>());
    }

    [TestCase(0L, nameof(PdfReadOptions.MaximumImagePixels))]
    [TestCase(0L, nameof(PdfReadOptions.MaximumImageComponents))]
    [TestCase(127L, nameof(PdfReadOptions.MaximumIccProfileBytes))]
    [TestCase(1L, nameof(PdfReadOptions.MaximumFunctionSamples))]
    public void ValidatesImageAndColorLimits(long value, string option)
    {
        PdfReadOptions options = option switch
        {
            nameof(PdfReadOptions.MaximumImagePixels) =>
                new PdfReadOptions { MaximumImagePixels = value },
            nameof(PdfReadOptions.MaximumImageComponents) =>
                new PdfReadOptions { MaximumImageComponents = checked((int)value) },
            nameof(PdfReadOptions.MaximumIccProfileBytes) =>
                new PdfReadOptions { MaximumIccProfileBytes = checked((int)value) },
            _ => new PdfReadOptions { MaximumFunctionSamples = checked((int)value) }
        };

        Assert.That(
            (Action)(() => Document.LoadFromData(new byte[8], options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static PdfImage Image(IEnumerable<PdfImage> images, string resourceName) =>
        images.Single(image => image.ResourceName == resourceName);

    private static Document LoadFixture(PdfReadOptions? options = null)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "images-and-color.pdf");
        return Document.LoadFromFile(path, options: options);
    }
}
