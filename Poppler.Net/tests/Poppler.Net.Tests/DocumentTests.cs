using System.Text;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void ReadsClassicXrefCatalogAndMetadata()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        Assert.Equal("1.7", document.PdfVersion);
        Assert.Equal(1, document.Pages);
        Assert.Equal("Managed fixture", document.Title);
        Assert.Equal("Poppler.Net tests", document.Producer);
        Assert.False(document.IsEncrypted);
        Assert.False(document.XrefWasRepaired);
        Assert.Equal(PageMode.UseOutlines, document.PageMode);
        Assert.Equal(PageLayout.SinglePage, document.PageLayout);
        Assert.NotNull(document.PdfId);
    }

    [Fact]
    public void ReadsPageGeometryLabelsAndText()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        Page page = document.CreatePage(0);

        Assert.Equal("A-1", page.Label);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), page.PageRect(PageBox.MediaBox));
        Assert.Equal(PageOrientation.Portrait, page.Orientation);
        Assert.Contains("Hello managed PDF", page.Text());
        TextBox box = Assert.Single(page.TextList());
        Assert.Contains("Hello managed PDF", box.Text);
        Assert.Equal("Helvetica", box.FontName);
        Assert.Single(page.Search("managed"));
        Assert.Contains("<svg", page.RenderToSvg());
    }

    [Fact]
    public void DecodesFlateContent()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: true));

        Assert.Contains("Hello managed PDF", document.CreatePage(0).Text());
    }

    [Fact]
    public void ExtractsEmbeddedFile()
    {
        using Document document = Document.LoadFromData(PdfFixtures.Create(compressContent: false));

        EmbeddedFile file = Assert.Single(document.EmbeddedFiles);

        Assert.Equal("hello.txt", file.Name);
        Assert.Equal("text/plain", file.MimeType);
        Assert.Equal("attachment payload", Encoding.ASCII.GetString(file.Data.Span));
    }

    [Fact]
    public void SavesAByteForByteCopy()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        using Document document = Document.LoadFromData(source);
        string path = Path.Combine(Path.GetTempPath(), $"poppler-net-{Guid.NewGuid():N}.pdf");
        try
        {
            document.SaveACopy(path);
            Assert.Equal(source, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
