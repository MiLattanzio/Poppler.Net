using System.Text;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class DocumentTests
{
    [Test]
    public void ReadsClassicXrefCatalogAndMetadata()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        Assert.That(document.PdfVersion, Is.EqualTo("1.7"));
        Assert.That(document.Pages, Is.EqualTo(1));
        Assert.That(document.Title, Is.EqualTo("Managed fixture"));
        Assert.That(document.Producer, Is.EqualTo("Poppler.Net tests"));
        Assert.That(document.IsEncrypted, Is.False);
        Assert.That(document.XrefWasRepaired, Is.False);
        Assert.That(document.PageMode, Is.EqualTo(PageMode.UseOutlines));
        Assert.That(document.PageLayout, Is.EqualTo(PageLayout.SinglePage));
        Assert.That(document.PdfId, Is.Not.Null);
    }

    [Test]
    public void ReadsPageGeometryLabelsAndText()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        Page page = document.CreatePage(0);

        Assert.That(page.Label, Is.EqualTo("A-1"));
        Assert.That(
            page.PageRect(PageBox.MediaBox),
            Is.EqualTo(new PdfRectangle(0, 0, 612, 792)));
        Assert.That(page.Orientation, Is.EqualTo(PageOrientation.Portrait));
        Assert.That(page.Text(), Does.Contain("Hello managed PDF"));
        IReadOnlyList<TextBox> boxes = page.TextList();
        Assert.That(boxes, Has.Count.EqualTo(1));
        TextBox box = boxes[0];
        Assert.That(box.Text, Does.Contain("Hello managed PDF"));
        Assert.That(box.FontName, Is.EqualTo("Helvetica"));
        Assert.That(page.Search("managed"), Has.Count.EqualTo(1));
        Assert.That(page.RenderToSvg(), Does.Contain("<svg"));
    }

    [Test]
    public void DecodesFlateContent()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: true));

        Assert.That(document.CreatePage(0).Text(), Does.Contain("Hello managed PDF"));
    }

    [Test]
    public void ExtractsEmbeddedFile()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        Assert.That(document.EmbeddedFiles, Has.Count.EqualTo(1));
        EmbeddedFile file = document.EmbeddedFiles[0];

        Assert.That(file.Name, Is.EqualTo("hello.txt"));
        Assert.That(file.MimeType, Is.EqualTo("text/plain"));
        Assert.That(
            Encoding.ASCII.GetString(file.Data.Span),
            Is.EqualTo("attachment payload"));
    }

    [Test]
    public void SavesAByteForByteCopy()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        using Document document = Document.LoadFromData(source);
        string path = Path.Combine(Path.GetTempPath(), $"poppler-net-{Guid.NewGuid():N}.pdf");
        try
        {
            document.SaveACopy(path);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(source));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
