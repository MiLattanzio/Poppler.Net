using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class StructuredExportAlpha3Tests
{
    [Test]
    public void RetainsRawSubsetFontNameBesideNormalizedName()
    {
        using Document document = Load("truetype-format0-subset.pdf");
        Page page = document.CreatePage(0);
        FontInfo font = page.Fonts.Single();
        TextBox text = page.TextList().Single();

        Assert.Multiple(() =>
        {
            Assert.That(font.NormalizedName, Is.EqualTo("DejaVuSans"));
            Assert.That(font.RawName, Is.EqualTo("ABCDEF+DejaVuSans"));
            Assert.That(text.FontName, Is.EqualTo("DejaVuSans"));
            Assert.That(text.RawFontName, Is.EqualTo("ABCDEF+DejaVuSans"));
            Assert.That(text.FontResourceName, Is.EqualTo(font.ResourceName));
        });
    }

    [Test]
    public void JsonXmlAndXhtmlAreVersionedAndDeterministic()
    {
        using Document document = Load("truetype-format0-subset.pdf");

        string first = document.ExportToJson();
        string second = document.ExportToJson();
        using JsonDocument json = JsonDocument.Parse(first);
        JsonElement root = json.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(root.GetProperty("schemaVersion").GetString(), Is.EqualTo("1.0"));
            Assert.That(root.GetProperty("pages")[0].GetProperty("fonts")[0]
                .GetProperty("rawName").GetString(), Is.EqualTo("ABCDEF+DejaVuSans"));
            Assert.That(root.GetProperty("pages")[0].GetProperty("textBoxes")[0]
                .GetProperty("id").GetString(), Is.EqualTo("p0001-text-000001"));
            Assert.That(XDocument.Parse(document.ExportToXml()).Root?.Name.LocalName,
                Is.EqualTo("structured-export"));
            Assert.That(XDocument.Parse(document.ExportToXhtml()).Root?.Name.LocalName,
                Is.EqualTo("html"));
            Assert.That(JsonDocument.Parse(document.CreatePage(0).ExportToJson())
                .RootElement.GetProperty("schemaVersion").GetString(), Is.EqualTo("1.0"));
        });
    }

    [Test]
    public void PublishedJsonAndXmlSchemasAreVersionedAndXmlValidates()
    {
        string schemaDirectory = Path.Combine(AppContext.BaseDirectory, "Schemas");
        using JsonDocument jsonSchema = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(schemaDirectory, "structured-export-v1.schema.json")));
        Assert.That(
            jsonSchema.RootElement.GetProperty("$schema").GetString(),
            Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));

        var schemas = new XmlSchemaSet();
        schemas.Add(null, Path.Combine(schemaDirectory, "structured-export-v1.xsd"));
        using Document document = Load("truetype-format0-subset.pdf");
        XDocument xml = XDocument.Parse(document.ExportToXml());
        var errors = new List<string>();
        xml.Validate(schemas, (_, eventArgs) => errors.Add(eventArgs.Message));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void SelectedPageRangeAndResolvedLinksHaveStableIds()
    {
        using Document document = Load("annotations-alpha1.pdf");
        using JsonDocument range = JsonDocument.Parse(document.ExportToJson(
            new StructuredExportOptions { FirstPageIndex = 1, PageCount = 2 }));
        JsonElement[] pages = range.RootElement.GetProperty("pages")
            .EnumerateArray().ToArray();
        Assert.That(pages.Select(page => page.GetProperty("index").GetInt32()),
            Is.EqualTo(new[] { 1, 2 }));

        using JsonDocument firstPage = JsonDocument.Parse(
            document.CreatePage(0).ExportToJson());
        JsonElement[] links = firstPage.RootElement.GetProperty("page")
            .GetProperty("links").EnumerateArray().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(links, Has.Length.EqualTo(3));
            Assert.That(links[0].GetProperty("id").GetString(),
                Is.EqualTo("p0001-link-0001"));
            Assert.That(links[0].GetProperty("uri").GetString(),
                Is.EqualTo("https://example.test/alpha1"));
            Assert.That(links[1].GetProperty("destinationPageNumber").GetInt32(),
                Is.EqualTo(2));
        });
    }

    [Test]
    public void BundleUsesOriginalStandaloneImagesAndPngFallbacks()
    {
        using Document document = Load("images-and-color.pdf");
        Page page = document.CreatePage(0);

        PdfImage jpeg = page.Images.Single(image => image.ResourceName == "Jpeg");
        PdfImage raw = page.Images.Single(image => image.ResourceName == "Raw");
        PdfImage soft = page.Images.Single(image => image.ResourceName == "Soft");
        PdfImage jbig = page.Images.Single(image => image.ResourceName == "Jbig");
        Assert.Multiple(() =>
        {
            Assert.That(jpeg.Export().IsOriginal, Is.True);
            Assert.That(jpeg.Export().Extension, Is.EqualTo("jpg"));
            Assert.That(raw.Export().IsOriginal, Is.False);
            Assert.That(raw.Export().Extension, Is.EqualTo("png"));
            Assert.That(soft.Export().IsOriginal, Is.False);
            Assert.That(soft.Export().FallbackReason, Does.Contain("mask"));
            Assert.That(jbig.Export().IsOriginal, Is.True);
            Assert.That(jbig.Export().MediaType, Is.EqualTo("image/jbig2"));
        });

        StructuredExportBundle first = document.CreateStructuredBundle();
        StructuredExportBundle second = document.CreateStructuredBundle();
        Assert.That(
            second.Files.Select(file => (file.RelativePath, Data: file.Data.ToArray())),
            Is.EqualTo(first.Files.Select(file =>
                (file.RelativePath, Data: file.Data.ToArray()))));
        Assert.That(first.Files.Any(file => file.RelativePath.EndsWith("jpeg.jpg")), Is.True);
        Assert.That(first.Files.Any(file => file.RelativePath.EndsWith("raw.png")), Is.True);

        StructuredExportFile manifest = first.Files.Single(file => file.RelativePath == "manifest.json");
        using JsonDocument json = JsonDocument.Parse(manifest.Data);
        foreach (JsonElement entry in json.RootElement.GetProperty("files").EnumerateArray())
        {
            string path = entry.GetProperty("path").GetString()!;
            StructuredExportFile file = first.Files.Single(item => item.RelativePath == path);
            string actual = Convert.ToHexString(SHA256.HashData(file.Data.Span)).ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(entry.GetProperty("sha256").GetString()), path);
        }
    }

    [Test]
    public void BundleHonorsFileAndByteLimitsBeforeGrowth()
    {
        using Document document = Load("images-and-color.pdf");

        Assert.That(
            () => document.CreateStructuredBundle(new StructuredExportOptions
            {
                MaximumFiles = 4
            }),
            Throws.TypeOf<PdfLimitException>());
        Assert.That(
            () => document.CreateStructuredBundle(new StructuredExportOptions
            {
                MaximumOutputBytes = 32
            }),
            Throws.TypeOf<PdfLimitException>());
    }

    private static Document Load(string fileName) => Document.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
