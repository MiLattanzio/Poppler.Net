using System.Security.Cryptography;
using System.Text;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class HardeningBeta2Tests
{
    [TestCase("ASCIIHexDecode", "123>", 1)]
    [TestCase("ASCII85Decode", "!!!!!", 3)]
    public void TextDecodersFailBeforeCrossingTheConfiguredLimit(
        string filter,
        string encoded,
        int maximumBytes)
    {
        AssertDecodedLimit(
            filter,
            Encoding.ASCII.GetBytes(encoded),
            maximumBytes);
    }

    [Test]
    public void RunLengthDecoderFailsBeforeGrowingPastTheConfiguredLimit()
    {
        AssertDecodedLimit(
            "RunLengthDecode",
            new byte[] { 129, (byte)'A', 128 },
            maximumBytes: 64);
    }

    [Test]
    public void CombinedContentLimitIncludesInsertedSeparators()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithContentParts("q", "Q"),
            options: new PdfReadOptions
            {
                MaximumDecodedStreamBytes = 2
            });

        Exception? exception = Assert.Catch(
            (Action)(() => _ = document.CreatePage(0).Graphics));

        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo(
                "Combined page content exceeds the decoded stream limit."));
    }

    [Test]
    public void ClipLimitFailsBeforeRetainingAnotherClip()
    {
        const string content =
            "0 0 10 10 re W n " +
            "1 1 8 8 re W n " +
            "2 2 6 6 re W n " +
            "0 0 10 10 re f";
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithContent(content),
            options: new PdfReadOptions
            {
                MaximumClipPaths = 2
            });

        Exception? exception = Assert.Catch(
            (Action)(() => _ = document.CreatePage(0).Graphics));

        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo("Clip path count exceeds the configured limit."));
    }

    [Test]
    public void ReusedImageIsDecodedOnceWithinTheCumulativePixelBudget()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithRepeatedImages(distinct: false),
            options: new PdfReadOptions
            {
                MaximumPageImagePixels = 1
            });

        PdfImageElement[] images = document.CreatePage(0).Graphics
            .OfType<PdfImageElement>()
            .ToArray();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(images, Has.Length.EqualTo(2));
            Assert.That(images[0].Image, Is.SameAs(images[1].Image));
        }));
    }

    [Test]
    public void DistinctImagesUseOneCumulativePixelBudget()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithRepeatedImages(distinct: true),
            options: new PdfReadOptions
            {
                MaximumPageImagePixels = 1
            });

        Exception? exception = Assert.Catch(
            (Action)(() => _ = document.CreatePage(0).Graphics));

        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo(
                "Page images exceed the configured cumulative pixel limit."));
    }

    [Test]
    public void MeshesUseOneCumulativePageTriangleBudget()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "rendering-beta2.pdf"),
            options: new PdfReadOptions
            {
                MaximumMeshTriangles = 4,
                MaximumPageMeshTriangles = 3
            });

        Exception? exception = Assert.Catch(
            (Action)(() => _ = document.CreatePage(0).Graphics));

        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo(
                "Page mesh triangles exceed the configured cumulative limit."));
    }

    [Test]
    public void HugeFinitePageBoxFailsWithABoundedDiagnostic()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithContent(
                "0 0 1 1 re f",
                mediaBox:
                    "0 0 10000000000000000000000000000000000000000 " +
                    "10000000000000000000000000000000000000000"));
        long before = GC.GetAllocatedBytesForCurrentThread();

        Exception? exception = Assert.Catch((Action)(() =>
            document.CreatePage(0).Render(new RasterRenderOptions
            {
                Dpi = 72,
                Antialiasing = 1,
                UseFontSubstitution = false
            })));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(exception, Is.TypeOf<PdfLimitException>()
                .And.Message.EqualTo(
                    "Rendered page dimensions exceed the supported pixel range."));
            Assert.That(allocated, Is.LessThan(1024 * 1024));
        }));
    }

    [Test]
    public void OversizedWorkingSurfaceFailsBeforeArrayAllocation()
    {
        var budget = new RenderWorkingSetBudget(long.MaxValue);

        Exception? exception = Assert.Catch(
            (Action)(() => _ = new RasterSurface(20_000, 20_000, budget)));

        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo(
                "Raster working surface exceeds the supported array length."));
    }

    [Test]
    public void ValidatesCumulativeReadLimits()
    {
        Assert.Multiple((Action)(() =>
        {
            AssertInvalid(new PdfReadOptions { MaximumClipPaths = 0 });
            AssertInvalid(new PdfReadOptions { MaximumPageImagePixels = 0 });
            AssertInvalid(new PdfReadOptions { MaximumPageMeshTriangles = 0 });
        }));
    }

    [Test]
    public async Task ConcurrentResourceDiscoveryAndRenderingAreDeterministic()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "images-and-color.pdf"));
        string expected = ResourceSummary(document);
        Task<string>[] operations = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => ResourceSummary(document)))
            .ToArray();

        string[] actual = await Task.WhenAll(operations);

        Assert.That(actual, Has.All.EqualTo(expected));
    }

    private static void AssertDecodedLimit(
        string filter,
        byte[] encoded,
        int maximumBytes)
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithFilteredContent(filter, encoded),
            options: new PdfReadOptions
            {
                MaximumDecodedStreamBytes = maximumBytes
            });

        Exception? exception = Assert.Catch(
            (Action)(() => _ = document.CreatePage(0).Graphics));

        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo(
                $"Decoded stream exceeds {maximumBytes} bytes."));
    }

    private static void AssertInvalid(PdfReadOptions options) =>
        Assert.That(
            (Action)(() => Document.LoadFromData(
                PdfFixtures.Create(compressContent: false),
                options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());

    private static string ResourceSummary(Document document)
    {
        var summary = new StringBuilder();
        for (int index = 0; index < document.Pages; index++)
        {
            Page page = document.CreatePage(index);
            summary.Append(index).Append(':');
            summary.AppendJoin(
                ',',
                page.Fonts.Select(font => $"{font.ResourceName}/{font.Name}"));
            summary.Append('|');
            summary.AppendJoin(
                ',',
                page.Images.Select(image =>
                    $"{image.ResourceName}/{image.Width}x{image.Height}/{image.Format}"));
            summary.Append('|').Append(page.Graphics.Count);
            summary.Append('|').Append(page.Text(layout: TextLayout.RawOrder));
            summary.Append('|').Append(Hash(page.RenderToPng(new RasterRenderOptions
            {
                Dpi = 24,
                Antialiasing = 1,
                UseFontSubstitution = false
            })));
            summary.AppendLine();
        }

        return summary.ToString();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
