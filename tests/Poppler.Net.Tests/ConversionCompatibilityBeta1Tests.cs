using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class ConversionCompatibilityBeta1Tests
{
    [Test]
    public void ManifestPinsEveryConversionCorpusAndAcceptedDifference()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;
        Assert.That(root.GetProperty("release").GetString(), Is.EqualTo("0.13.0-beta.1"));
        Assert.That(
            root.GetProperty("reference").GetProperty("upstream").GetString(),
            Is.EqualTo("Poppler 26.07.0"));
        Assert.That(
            root.GetProperty("reference").GetProperty("tools")
                .EnumerateArray().Select(item => item.GetString()),
            Is.EquivalentTo(new[] { "pdftohtml", "pdfseparate", "pdftotext", "pdfimages" }));

        JsonElement[] cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.That(cases, Has.Length.EqualTo(7));
        Assert.That(
            cases.Select(item => item.GetProperty("classification").GetString()),
            Is.EquivalentTo(new[]
            {
                "font-mapping",
                "annotation",
                "form",
                "page-geometry",
                "image-filter",
                "encryption",
                "malformed-and-large-graph"
            }));

        foreach (JsonElement item in cases)
        {
            string fixture = item.GetProperty("fixture").GetString()!;
            string expected = item.GetProperty("sha256").GetString()!;
            string actual = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Fixture(fixture))))
                .ToLowerInvariant();
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.EqualTo(expected), fixture);
                Assert.That(item.GetProperty("popplerTools").GetArrayLength(), Is.GreaterThan(0), fixture);
                Assert.That(item.GetProperty("acceptedDifference").GetString(), Is.Not.Empty, fixture);
            });
        }

        Assert.That(
            root.GetProperty("acceptedProductBehaviors").GetArrayLength(),
            Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void HtmlKeepsTextAvailableAcrossFontAndGraphicsFallbacks()
    {
        using Document subset = Load("truetype-format0-subset.pdf");
        string subsetHtml = subset.CreatePage(0).RenderToHtml(new HtmlRenderOptions
        {
            EmbedFonts = false,
            IncludeVectorGraphics = false,
            IncludeImages = false,
            FallbackMode = SvgFallbackMode.Omit
        });
        using Document encrypted = Load("r4-aes-128.pdf", "user-03");
        string encryptedHtml = encrypted.CreatePage(0).RenderToHtml(new HtmlRenderOptions
        {
            EmbedFonts = false,
            IncludeVectorGraphics = false,
            IncludeImages = false,
            FallbackMode = SvgFallbackMode.Omit
        });

        Assert.Multiple(() =>
        {
            Assert.That(subsetHtml, Does.Contain("data-source-text=\"ABC\""));
            Assert.That(subsetHtml, Does.Contain("class=\"pdf-glyph-visual\""));
            Assert.That(subsetHtml, Does.Not.Contain("ABCDEF+DejaVuSans"));
            Assert.That(encryptedHtml, Does.Contain("Encrypted managed PDF R2-R6"));
            Assert.That(encryptedHtml, Does.Contain("class=\"pdf-glyph "));
        });
    }

    [TestCase("annotations-alpha1.pdf", 0, null)]
    [TestCase("acroform-alpha2.pdf", 0, null)]
    [TestCase("compatibility-beta1.pdf", 2, null)]
    [TestCase("images-and-color.pdf", 0, null)]
    [TestCase("r4-aes-128.pdf", 0, "user-03")]
    public void ExtractedPagesReopenAndRenderLikeTheirSource(
        string fixture,
        int pageIndex,
        string? userPassword)
    {
        using Document source = Load(fixture, userPassword);
        Page sourcePage = source.CreatePage(pageIndex);
        byte[] extractedBytes = source.ExtractPage(pageIndex);
        using Document extracted = Document.LoadFromData(extractedBytes);
        Page extractedPage = extracted.CreatePage(0);
        var renderOptions = new RasterRenderOptions
        {
            Dpi = 36,
            Antialiasing = 1,
            UseFontSubstitution = false
        };
        PdfBitmap expected = sourcePage.Render(renderOptions);
        PdfBitmap actual = extractedPage.Render(renderOptions);

        Assert.Multiple(() =>
        {
            Assert.That(extracted.Pages, Is.EqualTo(1));
            Assert.That(extracted.IsEncrypted, Is.False);
            Assert.That(extractedPage.Rotation, Is.EqualTo(sourcePage.Rotation));
            Assert.That(extractedPage.PageRect(PageBox.MediaBox),
                Is.EqualTo(sourcePage.PageRect(PageBox.MediaBox)));
            Assert.That(extractedPage.PageRect(PageBox.CropBox),
                Is.EqualTo(sourcePage.PageRect(PageBox.CropBox)));
            Assert.That(extractedPage.Text(), Is.EqualTo(sourcePage.Text()));
            Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
            Assert.That(actual.Data.ToArray(), Is.EqualTo(expected.Data.ToArray()));
        });
    }

    [Test]
    public void PageRangesStayAlignedAcrossHtmlStructuredDataAndSeparation()
    {
        using Document document = Load("compatibility-beta1.pdf");
        string html = document.RenderToHtml(new HtmlExportOptions
        {
            FirstPageIndex = 1,
            PageCount = 2
        });
        using JsonDocument structured = JsonDocument.Parse(document.ExportToJson(
            new StructuredExportOptions
            {
                FirstPageIndex = 1,
                PageCount = 2,
                IncludeImages = false
            }));
        JsonElement[] pages = structured.RootElement.GetProperty("pages")
            .EnumerateArray().ToArray();
        IReadOnlyList<PdfExtractedPage> separated = document.ExtractPages(1, 2);
        using JsonDocument pageExport = JsonDocument.Parse(
            document.CreatePage(1).ExportToJson(new StructuredExportOptions
            {
                IncludeImages = false
            }));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("id=\"page-1\""));
            Assert.That(html, Does.Contain("id=\"page-2\""));
            Assert.That(html, Does.Contain("id=\"page-3\""));
            Assert.That(pages.Select(page => page.GetProperty("index").GetInt32()),
                Is.EqualTo(new[] { 1, 2 }));
            Assert.That(separated.Select(page => page.SourcePageNumber),
                Is.EqualTo(new[] { 2, 3 }));
            Assert.That(
                JsonElement.DeepEquals(
                    pageExport.RootElement.GetProperty("page"),
                    pages[0]),
                Is.True);
        });
    }

    [Test]
    public void OptionalMetadataDamageAndCyclicResourcesStayBounded()
    {
        using Document missingInformation = Document.LoadFromData(
            PdfFixtures.CreateWithMissingInformationObject());
        string html = missingInformation.RenderToHtml();
        string json = missingInformation.ExportToJson();
        using Document extracted = Document.LoadFromData(
            missingInformation.ExtractPage(0));
        using Document cyclic = Document.LoadFromData(
            PdfFixtures.CreateWithCyclicResourceGraph());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Readable without metadata"));
            Assert.That(json, Does.Contain("Readable without metadata"));
            Assert.That(extracted.CreatePage(0).Text(), Does.Contain("Readable without metadata"));
            Assert.That(
                missingInformation.Diagnostics.Any(item => item.Code == "info.invalid"),
                Is.True);
            Assert.That(cyclic.RenderToHtml(), Does.StartWith("<!doctype html>"));
            Assert.That(cyclic.ExportToJson(), Does.Contain("\"schemaVersion\": \"1.0\""));
            Assert.That(() => cyclic.ExtractPage(0), Throws.Nothing);
        });

        using Document invalidStream = Document.LoadFromData(
            PdfFixtures.CreateWithInvalidFlateContent());
        Assert.Multiple(() =>
        {
            Assert.That(
                () => invalidStream.RenderToHtml(),
                Throws.TypeOf<PdfFormatException>());
            Assert.That(
                () => invalidStream.ExportToJson(),
                Throws.TypeOf<PdfFormatException>());
        });
    }

    private static JsonDocument LoadManifest() => JsonDocument.Parse(
        File.ReadAllBytes(Fixture("conversion-beta1-compatibility.json")));

    private static Document Load(string fileName, string? userPassword = null) =>
        Document.LoadFromFile(Fixture(fileName), userPassword: userPassword ?? "");

    private static string Fixture(string fileName) => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        fileName);
}
