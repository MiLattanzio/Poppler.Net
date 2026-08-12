using System.Text;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class PageExtractionAlpha2Tests
{
    [Test]
    public void ExtractsADeterministicAutonomousPage()
    {
        using Document source = Document.LoadFromData(PdfFixtures.Create(compressContent: true));

        byte[] first = source.ExtractPage(0);
        byte[] second = source.CreatePage(0).ExtractPdf();
        using Document extracted = Document.LoadFromData(first);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(extracted.Pages, Is.EqualTo(1));
            Assert.That(extracted.IsEncrypted, Is.False);
            Assert.That(extracted.CreatePage(0).Text(), Does.Contain("Hello managed PDF"));
            Assert.That(extracted.CreatePage(0).Fonts, Has.Count.EqualTo(1));
            Assert.That(Encoding.ASCII.GetString(first), Does.Not.Contain("/Info"));
        }));
    }

    [Test]
    public void MaterializesInheritedBoxesResourcesAndRotation()
    {
        using Document source = Document.LoadFromData(
            PdfFixtures.CreateWithInheritedPageResources());
        using Document extracted = Document.LoadFromData(source.ExtractPage(0));
        Page page = extracted.CreatePage(0);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.Text(), Does.Contain("Inherited page resources"));
            Assert.That(page.Rotation, Is.EqualTo(90));
            Assert.That(page.PageRect(PageBox.MediaBox),
                Is.EqualTo(new PdfRectangle(0, 0, 320, 240)));
            Assert.That(page.PageRect(PageBox.CropBox),
                Is.EqualTo(new PdfRectangle(10, 20, 300, 220)));
        }));
    }

    [Test]
    public void ExtractsRangesWithSourcePageIdentity()
    {
        using Document source = Document.LoadFromData(PdfFixtures.CreateFunctionShadingFixture());

        IReadOnlyList<PdfExtractedPage> pages = source.ExtractPages(1, 3);

        Assert.That(pages.Select(page => page.SourcePageNumber), Is.EqualTo(new[] { 2, 3, 4 }));
        foreach (PdfExtractedPage page in pages)
        {
            using Document extracted = Document.LoadFromData(page.Data.ToArray());
            Assert.That(extracted.Pages, Is.EqualTo(1));
        }
    }

    [Test]
    public void NormalizesCompressedObjectsAndReferenceCycles()
    {
        using Document compressed = Document.LoadFromData(PdfFixtures.CreateWithXrefStream());
        using Document compressedPage = Document.LoadFromData(compressed.ExtractPage(0));
        using Document cyclic = Document.LoadFromData(PdfFixtures.CreateWithCyclicResourceGraph());

        byte[] first = cyclic.ExtractPage(0);
        byte[] second = cyclic.ExtractPage(0);
        using Document cyclicPage = Document.LoadFromData(first);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(compressedPage.CreatePage(0).Text(), Does.Contain("Compressed font object"));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(cyclicPage.Pages, Is.EqualTo(1));
        }));
    }

    [Test]
    public void OmitsCrossPageDestinationsButPreservesLocalAnnotations()
    {
        using Document source = Document.LoadFromFile(Fixture("annotations-alpha1.pdf"));
        using Document extracted = Document.LoadFromData(source.ExtractPage(0));
        using Document withoutAnnotations = Document.LoadFromData(source.ExtractPage(
            0,
            new PdfPageExtractionOptions { PreserveAnnotations = false }));

        IReadOnlyList<PdfAnnotation> annotations = extracted.CreatePage(0).Annotations;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extracted.Pages, Is.EqualTo(1));
            Assert.That(annotations, Has.Count.EqualTo(5));
            Assert.That(annotations[0].Action.Type, Is.EqualTo(PdfAnnotationActionType.Uri));
            Assert.That(annotations[1].Action.Type, Is.EqualTo(PdfAnnotationActionType.None));
            Assert.That(annotations[2].Action.Type, Is.EqualTo(PdfAnnotationActionType.None));
            Assert.That(annotations[3].Contents, Is.Not.Empty);
            Assert.That(withoutAnnotations.CreatePage(0).Annotations, Is.Empty);
        }));
    }

    [Test]
    public void MakesPageWidgetsTerminalAndSelfContained()
    {
        using Document source = Document.LoadFromFile(Fixture("acroform-alpha2.pdf"));
        using Document extracted = Document.LoadFromData(source.ExtractPage(0));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extracted.Pages, Is.EqualTo(1));
            Assert.That(extracted.CreatePage(0).FormWidgets, Has.Count.EqualTo(4));
            Assert.That(extracted.FormFields, Has.Count.EqualTo(4));
            Assert.That(extracted.FormNeedsAppearances, Is.True);
        }));
    }

    [TestCase("r4-aes-128.pdf")]
    [TestCase("r4-aes-128-explicit-crypt.pdf")]
    public void EmitsUnlockedEncryptedInputWithoutEncryption(string fileName)
    {
        using Document source = Document.LoadFromFile(
            Fixture(fileName),
            userPassword: "user-03");

        byte[] bytes = source.ExtractPage(0);
        using Document extracted = Document.LoadFromData(bytes);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extracted.IsEncrypted, Is.False);
            Assert.That(extracted.IsLocked, Is.False);
            Assert.That(
                extracted.CreatePage(0).Text(),
                Does.Contain("Encrypted managed PDF R2-R6"));
            Assert.That(Encoding.ASCII.GetString(bytes), Does.Not.Contain("/Encrypt"));
        }));
    }

    [Test]
    public void EnforcesWriterObjectStreamDepthAndOutputLimits()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => document.ExtractPage(
                    0,
                    new PdfPageExtractionOptions { MaximumObjects = 3 })),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => document.ExtractPage(
                    0,
                    new PdfPageExtractionOptions { MaximumStreamBytes = 8 })),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => document.ExtractPage(
                    0,
                    new PdfPageExtractionOptions { MaximumDepth = 1 })),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => document.ExtractPage(
                    0,
                    new PdfPageExtractionOptions { MaximumOutputBytes = 128 })),
                Throws.TypeOf<PdfLimitException>());
        }));
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
