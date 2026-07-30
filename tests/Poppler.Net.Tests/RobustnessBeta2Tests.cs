using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class RobustnessBeta2Tests
{
    [Test]
    public void RepairsDamagedPageTreeAndPartialContentArrays()
    {
        using Document document = Load();

        Assert.That(document.Pages, Is.EqualTo(5));
        Assert.That(
            document.Diagnostics.Select(diagnostic => diagnostic.Code),
            Does.Contain("page-tree.repaired"));
        Assert.That(
            document.Diagnostics.Select(diagnostic => diagnostic.Code),
            Does.Contain("page-tree.count-mismatch"));

        PdfBitmap recovered = document.CreatePage(2).Render(RenderOptions());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                CountPixels(
                    recovered,
                    static (red, green, blue) =>
                        red > 180 && green < 80 && blue < 80),
                Is.GreaterThan(10_000));
            Assert.That(
                CountPixels(
                    recovered,
                    static (red, green, blue) =>
                        blue > 180 && red < 80 && green < 120),
                Is.GreaterThan(10_000));
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("content.repaired"));
        }));
    }

    [Test]
    public void StrictRepairOptionsRejectTheDamagedBranches()
    {
        Assert.That(
            (Action)(() => Load(new PdfReadOptions
            {
                AttemptPageTreeRepair = false
            })),
            Throws.TypeOf<PdfFormatException>());

        using Document strictContent = Load(new PdfReadOptions
        {
            AttemptContentStreamRepair = false
        });
        Assert.That(
            (Action)(() => _ = strictContent.CreatePage(2).Graphics),
            Throws.TypeOf<PdfFormatException>());
    }

    [Test]
    public void PreservesCapDashAndJoinGraphicsState()
    {
        using Document document = Load();

        PdfLineCap[] caps = document.CreatePage(0).Graphics
            .OfType<PdfPathElement>()
            .Select(element => element.State.LineCap)
            .Distinct()
            .Order()
            .ToArray();
        PdfDashPattern oddDash = document.CreatePage(1).Graphics
            .OfType<PdfPathElement>()
            .Select(element => element.State.Dash)
            .Single(dash => dash.Segments.Count == 3);
        PdfLineJoin[] joins = document.CreatePage(4).Graphics
            .OfType<PdfPathElement>()
            .Select(element => element.State.LineJoin)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                caps,
                Is.EqualTo(new[]
                {
                    PdfLineCap.Butt,
                    PdfLineCap.Round,
                    PdfLineCap.Square
                }));
            Assert.That(oddDash.Segments, Is.EqualTo(new[] { 9d, 4d, 2d }));
            Assert.That(oddDash.Phase, Is.EqualTo(5));
            Assert.That(
                joins,
                Is.EqualTo(new[]
                {
                    PdfLineJoin.Miter,
                    PdfLineJoin.Round,
                    PdfLineJoin.Bevel
                }));
        }));
    }

    [Test]
    public void EnforcesContentAndCacheLimits()
    {
        using Document streamLimited = Load(new PdfReadOptions
        {
            MaximumContentStreamsPerPage = 2
        });
        using Document operandLimited = Load(new PdfReadOptions
        {
            MaximumContentOperands = 2
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => _ = streamLimited.CreatePage(2).Graphics),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => _ = operandLimited.CreatePage(0).Graphics),
                Throws.TypeOf<PdfLimitException>());
            AssertInvalid(new PdfReadOptions { MaximumCachedDecodedBytes = -1 });
            AssertInvalid(new PdfReadOptions { MaximumContentStreamsPerPage = 0 });
            AssertInvalid(new PdfReadOptions { MaximumContentOperands = 0 });
        }));
    }

    [Test]
    [NonParallelizable]
    public void DecodedStreamCacheReducesRepeatedPageAllocations()
    {
        byte[] source = PdfFixtures.CreateWithLargeCompressedContent();
        long cached = MeasureRepeatedGraphicsAllocations(
            source,
            new PdfReadOptions
            {
                MaximumCachedDecodedBytes = 1024 * 1024
            });
        long uncached = MeasureRepeatedGraphicsAllocations(
            source,
            new PdfReadOptions
            {
                MaximumCachedDecodedBytes = 0
            });

        TestContext.Progress.WriteLine(
            $"Repeated page allocations: cached={cached / 1024.0:0.0} KiB, " +
            $"uncached={uncached / 1024.0:0.0} KiB.");
        Assert.That(
            uncached - cached,
            Is.GreaterThan(2L * 1024 * 1024));
    }

    [Test]
    public async Task ConcurrentDamagedDocumentReadsAreDeterministic()
    {
        using Document document = Load();
        string expected = Summary(document);
        Task<string>[] operations = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => Summary(document)))
            .ToArray();

        string[] actual = await Task.WhenAll(operations);

        Assert.That(actual, Has.All.EqualTo(expected));
        Assert.That(
            document.Diagnostics.Count(diagnostic =>
                diagnostic.Code == "content.repaired"),
            Is.EqualTo(1));
    }

    [Test]
    public void ManagedRobustnessRendersMatchManifest()
    {
        using JsonDocument manifest = Manifest();
        string[] expected = manifest.RootElement
            .GetProperty("managed_png_sha256")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        using Document document = Load();

        string[] actual = Enumerable.Range(0, document.Pages)
            .Select(index => RenderHash(document.CreatePage(index)))
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void RobustnessFixtureHashMatchesManifest()
    {
        using JsonDocument manifest = Manifest();
        string file = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(FixtureDirectory(), file))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static long MeasureRepeatedGraphicsAllocations(
        byte[] source,
        PdfReadOptions options)
    {
        using Document document = Document.LoadFromData(source, options: options);
        _ = document.CreatePage(0).Graphics.Count;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 12; iteration++)
            _ = document.CreatePage(0).Graphics.Count;
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Summary(Document document) =>
        string.Join(
            "|",
            Enumerable.Range(0, document.Pages)
                .Select(index => RenderHash(document.CreatePage(index))));

    private static int CountPixels(
        PdfBitmap bitmap,
        Func<byte, byte, byte, bool> predicate)
    {
        ReadOnlySpan<byte> data = bitmap.Data.Span;
        int count = 0;
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            if (predicate(data[offset], data[offset + 1], data[offset + 2]))
                count++;
        }

        return count;
    }

    private static string RenderHash(Page page) =>
        Convert.ToHexString(SHA256.HashData(page.RenderToPng(RenderOptions())))
            .ToLowerInvariant();

    private static RasterRenderOptions RenderOptions() => new()
    {
        Dpi = 72,
        Antialiasing = 2,
        UseFontSubstitution = false
    };

    private static void AssertInvalid(PdfReadOptions options) =>
        Assert.That(
            (Action)(() => Document.LoadFromData(
                PdfFixtures.Create(compressContent: false),
                options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "robustness-beta2.pdf"),
            options: options);

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    FixtureDirectory(),
                    "robustness-beta2-fixture.json")));

    private static string FixtureDirectory() =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
}
