using System.Text;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class HtmlExportAlpha1Tests
{
    [Test]
    public void PageRendersDeterministicSelfContainedHtmlWithSelectableText()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        Page page = document.CreatePage(0);

        string first = page.RenderToHtml();
        string second = page.RenderToHtml();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.StartWith("<!doctype html>"));
            Assert.That(first, Does.Contain("<svg xmlns=\"http://www.w3.org/2000/svg\""));
            Assert.That(first, Does.Contain("class=\"pdf-text\""));
            Assert.That(first, Does.Contain("Hello managed PDF"));
            Assert.That(first, Does.Contain("data-font-name=\"Helvetica\""));
            Assert.That(first, Does.Contain("class=\"pdf-invisible-text\""));
        }));
    }

    [Test]
    public void DocumentHtmlSupportsZeroBasedPageRanges()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "rendering-beta2.pdf"));

        string html = document.RenderToHtml(new HtmlExportOptions
        {
            FirstPageIndex = 1,
            PageCount = 2,
            Title = "Selected pages"
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(html, Does.Contain("<title>Selected pages</title>"));
            Assert.That(html, Does.Contain("<section class=\"pdf-page\" id=\"page-2\""));
            Assert.That(html, Does.Contain("<section class=\"pdf-page\" id=\"page-3\""));
            Assert.That(html, Does.Not.Contain("<section class=\"pdf-page\" id=\"page-1\""));
            Assert.That(html, Does.Not.Contain("<section class=\"pdf-page\" id=\"page-4\""));
        }));
    }

    [Test]
    public void VisibleTextModeKeepsTextInTheDom()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));

        string html = document.CreatePage(0).RenderToHtml(new HtmlRenderOptions
        {
            TextLayerMode = HtmlTextLayerMode.Visible,
            Foreground = "#123456"
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(html, Does.Contain("class=\"pdf-visible-text\""));
            Assert.That(html, Does.Contain("Hello managed PDF"));
            Assert.That(html, Does.Contain("color:#123456"));
        }));
    }

    [Test]
    public void PageRotationTransformsBackgroundTextAndLinksTogether()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "compatibility-beta1.pdf"));

        string html = document.CreatePage(2).RenderToHtml();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(html, Does.Contain(
                "--pdf-page-width:170px;--pdf-page-height:230px"));
            Assert.That(html, Does.Contain(
                "class=\"pdf-page-content\" style=\"width:230px;height:170px;" +
                "transform:translate(170px,0) rotate(90deg)\""));
            Assert.That(html, Does.Contain("data-page-rotation=\"90\""));
        }));
    }

    [Test]
    public void LinkAnnotationsBecomeSafeHtmlAnchors()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "annotations-alpha1.pdf"));

        string html = document.CreatePage(0).RenderToHtml();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(html, Does.Contain("href=\"https://example.test/alpha1\""));
            Assert.That(html, Does.Contain("rel=\"noreferrer noopener\""));
            Assert.That(html, Does.Contain("class=\"pdf-link\""));
        }));
    }

    [Test]
    public void DirectoryBundleContainsStableEntryPointAssetsAndManifest()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));

        HtmlExportBundle first = document.CreateHtmlBundle();
        HtmlExportBundle second = document.CreateHtmlBundle();
        Dictionary<string, byte[]> firstFiles = Files(first);
        Dictionary<string, byte[]> secondFiles = Files(second);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(first.EntryPoint, Is.EqualTo("index.html"));
            Assert.That(firstFiles.Keys, Does.Contain("index.html"));
            Assert.That(firstFiles.Keys, Does.Contain("styles.css"));
            Assert.That(firstFiles.Keys, Does.Contain("pages/page-0001.svg"));
            Assert.That(firstFiles.Keys, Does.Contain("manifest.json"));
            Assert.That(secondFiles.Keys, Is.EquivalentTo(firstFiles.Keys));
            foreach ((string path, byte[] data) in firstFiles)
                Assert.That(secondFiles[path], Is.EqualTo(data), path);
            string index = Encoding.UTF8.GetString(firstFiles["index.html"]);
            Assert.That(index, Does.Contain("src=\"pages/page-0001.svg\""));
            string manifest = Encoding.UTF8.GetString(firstFiles["manifest.json"]);
            Assert.That(manifest, Does.Contain("\"format\": \"poppler-net-html-bundle\""));
            Assert.That(manifest, Does.Contain("\"entryPoint\": \"index.html\""));
        }));
    }

    [Test]
    public void EmbeddedSubsetFontUsesNormalizedNameAndIsBundled()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "truetype-format0-subset.pdf"));
        Page page = document.CreatePage(0);

        string html = page.RenderToHtml(new HtmlRenderOptions
        {
            TextLayerMode = HtmlTextLayerMode.Visible
        });
        HtmlExportBundle bundle = document.CreateHtmlBundle(new HtmlExportOptions
        {
            PageCount = 1,
            PageOptions = new HtmlRenderOptions
            {
                TextLayerMode = HtmlTextLayerMode.Visible
            }
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.TextList().Select(box => box.FontName),
                Has.Some.EqualTo("DejaVuSans"));
            Assert.That(html, Does.Contain("data-font-name=\"DejaVuSans\""));
            Assert.That(html, Does.Not.Contain("ABCDEF+DejaVuSans"));
            Assert.That(bundle.Files.Any(file =>
                file.RelativePath.StartsWith("fonts/", StringComparison.Ordinal) &&
                file.MediaType == "font/ttf"), Is.True);
        }));
    }

    [Test]
    public void BundleCanBeSavedWithoutEscapingItsDestination()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"poppler-net-html-{Guid.NewGuid():N}");
        try
        {
            document.SaveHtmlBundle(directory);

            Assert.That(File.Exists(Path.Combine(directory, "index.html")), Is.True);
            Assert.That(
                File.Exists(Path.Combine(directory, "pages", "page-0001.svg")),
                Is.True);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void InvalidDocumentRangeFailsBeforeRendering()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));

        Assert.That(
            () => document.RenderToHtml(new HtmlExportOptions
            {
                FirstPageIndex = 1,
                PageCount = 1
            }),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void HtmlOptionsRejectCssInjectionAndBoundOutputBytes()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        Page page = document.CreatePage(0);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                () => page.RenderToHtml(new HtmlRenderOptions
                {
                    Background = "white;}body{display:none"
                }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => page.RenderToHtml(new HtmlRenderOptions
                {
                    MaximumOutputBytes = 1024,
                    MaximumEmbeddedFontBytes = 0
                }),
                Throws.TypeOf<PdfLimitException>());
        }));
    }

    [Test]
    public void HtmlCorpusMatchesTheRecordedPopplerReferenceContract()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(FixtureDirectory(), "html-alpha1-fixture.json")));
        Assert.That(
            manifest.RootElement.GetProperty("reference").GetProperty("upstream").GetString(),
            Is.EqualTo("Poppler 26.07.0"));
        Assert.That(
            manifest.RootElement.GetProperty("hashMode").GetString(),
            Is.EqualTo(CanonicalRenderingHash.HtmlMode));

        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            string fixture = item.GetProperty("fixture").GetString()!;
            using Document document = Document.LoadFromFile(
                Path.Combine(FixtureDirectory(), fixture));
            var options = new HtmlExportOptions
            {
                FirstPageIndex = item.GetProperty("firstPageIndex").GetInt32(),
                PageCount = item.GetProperty("pageCount").GetInt32(),
                PageOptions = new HtmlRenderOptions
                {
                    Scale = item.GetProperty("scale").GetDouble(),
                    TextLayerMode = Enum.Parse<HtmlTextLayerMode>(
                        item.GetProperty("textLayerMode").GetString()!)
                }
            };

            string rendered = document.RenderToHtml(options);
            byte[] html = Encoding.UTF8.GetBytes(rendered);
            string hash = CanonicalRenderingHash.Html(rendered);
            Assert.Multiple((Action)(() =>
            {
                if (item.TryGetProperty("utf8Bytes", out JsonElement expectedBytes))
                    Assert.That(html, Has.Length.EqualTo(expectedBytes.GetInt32()), fixture);
                Assert.That(hash, Is.EqualTo(item.GetProperty("sha256").GetString()), fixture);
            }));
        }
    }

    private static Dictionary<string, byte[]> Files(HtmlExportBundle bundle) =>
        bundle.Files.ToDictionary(
            file => file.RelativePath,
            file => file.Data.ToArray(),
            StringComparer.Ordinal);

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
