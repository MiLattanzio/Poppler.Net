using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class ExportHardeningBeta2Tests
{
    [Test]
    public void HtmlBudgetsFailWithStableBoundedDiagnostics()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        Page page = document.CreatePage(0);

        AssertLimit(
            () => page.RenderToHtml(new HtmlRenderOptions
            {
                MaximumDomNodes = 1
            }),
            "HTML DOM node count exceeds the configured limit.");
        AssertLimit(
            () => page.RenderToHtml(new HtmlRenderOptions
            {
                EmbedFonts = false,
                MaximumOutputBytes = 64,
                MaximumEmbeddedFontBytes = 0
            }),
            "HTML output exceeds the configured byte limit.");
        AssertLimit(
            () => document.CreateHtmlBundle(new HtmlExportOptions
            {
                PageOptions = new HtmlRenderOptions
                {
                    MaximumFiles = 3
                }
            }),
            "HTML bundle file count exceeds the configured limit.");
    }

    [Test]
    public void DocumentExportPageBudgetsAreCheckedBeforePageArrays()
    {
        using Document document = CompatibilityDocument();

        AssertLimit(
            () => document.RenderToHtml(new HtmlExportOptions
            {
                FirstPageIndex = 0,
                PageCount = 2,
                MaximumPages = 1
            }),
            "HTML export page count exceeds the configured limit.");
        AssertLimit(
            () => document.ExportToJson(new StructuredExportOptions
            {
                FirstPageIndex = 0,
                PageCount = 2,
                MaximumPages = 1
            }),
            "Structured export page count exceeds the configured limit.");
        AssertLimit(
            () => document.ExtractPages(
                0,
                2,
                new PdfPageExtractionOptions { MaximumPages = 1 }),
            "Extracted page count exceeds the configured limit.");
    }

    [TestCase("json")]
    [TestCase("xml")]
    [TestCase("xhtml")]
    public void StandaloneStructuredFormatsHonorOutputBudget(string format)
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        var options = new StructuredExportOptions
        {
            MaximumOutputBytes = 64,
            IncludeImages = false
        };

        AssertLimit(
            () => _ = format switch
            {
                "json" => document.ExportToJson(options),
                "xml" => document.ExportToXml(options),
                "xhtml" => document.ExportToXhtml(options),
                _ => throw new InvalidOperationException()
            },
            "Structured export output exceeds the configured byte limit.");
    }

    [Test]
    public void StructuredNodeAndFileBudgetsAreCumulative()
    {
        using Document hostile = Document.LoadFromData(
            PdfFixtures.CreateWithHostileImageResourceName());
        var nodeOptions = new StructuredExportOptions
        {
            MaximumNodes = 2
        };

        string[] diagnostics = Enumerable.Range(0, 2)
            .Select(_ => AssertLimit(
                () => hostile.ExportToJson(nodeOptions),
                "Structured export node count exceeds the configured limit."))
            .ToArray();
        Assert.That(diagnostics[1], Is.EqualTo(diagnostics[0]));

        AssertLimit(
            () => hostile.CreateStructuredBundle(new StructuredExportOptions
            {
                MaximumFiles = 4
            }),
            "Structured export file count exceeds the configured limit.");
    }

    [Test]
    public void StructuredImageNamesArePortableAndBounded()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateWithHostileImageResourceName());

        StructuredExportBundle bundle = document.CreateStructuredBundle();
        string path = bundle.Files.Single(file =>
            file.MediaType.StartsWith("image/", StringComparison.Ordinal)).RelativePath;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(path, Does.StartWith("images/page-0001-0001-"));
            Assert.That(path, Does.Not.Contain(".."));
            Assert.That(path, Does.Not.Contain('\\'));
            Assert.That(path.Length, Is.LessThan(120));
            Assert.That(
                path.All(character =>
                    char.IsAsciiLetterOrDigit(character) ||
                    character is '/' or '-' or '_' or '.'),
                Is.True);
        }));
    }

    [Test]
    public void SeparatedPagesUseOneCumulativeOutputBudget()
    {
        using Document document = CompatibilityDocument();

        AssertLimit(
            () => document.ExtractPages(
                0,
                2,
                new PdfPageExtractionOptions
                {
                    MaximumOutputBytes = 1024 * 1024,
                    MaximumTotalOutputBytes = 64
                }),
            "Extracted pages exceed the configured cumulative output limit.");
    }

    [Test]
    public async Task ConcurrentConversionFamiliesAreIsolatedAndDeterministic()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "truetype-format0-subset.pdf"));
        string expected = ConversionSummary(document);

        Task<string>[] tasks = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => ConversionSummary(document)))
            .ToArray();
        string[] results = await Task.WhenAll(tasks);

        Assert.That(results, Has.All.EqualTo(expected));
    }

    [Test]
    [NonParallelizable]
    public void RepresentativeExportsStayWithinBeta2Baselines()
    {
        string path = Path.Combine(FixtureDirectory(), "compatibility-beta1.pdf");
        using (Document warmup = Document.LoadFromFile(path))
            _ = ConversionSummary(warmup);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        using (Document document = Document.LoadFromFile(path))
        {
            _ = document.RenderToHtml(new HtmlExportOptions
            {
                FirstPageIndex = 0,
                PageCount = Math.Min(2, document.Pages),
                PageOptions = new HtmlRenderOptions
                {
                    EmbedFonts = false,
                    IncludeImages = false,
                    FallbackMode = SvgFallbackMode.Omit
                }
            });
            _ = document.ExportToJson(new StructuredExportOptions
            {
                FirstPageIndex = 0,
                PageCount = Math.Min(2, document.Pages),
                IncludeImages = false
            });
            _ = document.ExtractPages(0, Math.Min(2, document.Pages));
        }
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        TestContext.Progress.WriteLine(
            $"Beta.2 export baseline: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms, " +
            $"{allocated / (1024d * 1024d):0.0} MiB allocated.");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
            Assert.That(allocated, Is.LessThan(32L * 1024 * 1024));
        }));
    }

    [Test]
    public void NewExportLimitsRejectInvalidValues()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        Page page = document.CreatePage(0);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                () => page.RenderToHtml(new HtmlRenderOptions { MaximumDomNodes = 0 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => page.RenderToHtml(new HtmlRenderOptions { MaximumFiles = 2 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => document.ExportToJson(new StructuredExportOptions { MaximumNodes = 0 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => document.ExportToJson(new StructuredExportOptions { MaximumPages = 0 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => document.ExtractPages(
                    options: new PdfPageExtractionOptions { MaximumTotalOutputBytes = 0 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    private static string ConversionSummary(Document document)
    {
        Page page = document.CreatePage(0);
        string html = page.RenderToHtml(new HtmlRenderOptions
        {
            EmbedFonts = false
        });
        string json = page.ExportToJson(new StructuredExportOptions
        {
            IncludeImages = false
        });
        byte[] extracted = page.ExtractPdf();
        return string.Join(
            ':',
            Hash(Encoding.UTF8.GetBytes(html)),
            Hash(Encoding.UTF8.GetBytes(json)),
            Hash(extracted));
    }

    private static string AssertLimit(Action action, string expectedMessage)
    {
        Exception? exception = Assert.Catch(action);
        Assert.That(exception, Is.TypeOf<PdfLimitException>()
            .And.Message.EqualTo(expectedMessage));
        Assert.That(exception!.Message.Length, Is.LessThan(160));
        return exception.Message;
    }

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));

    private static Document CompatibilityDocument() =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "compatibility-beta1.pdf"));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
