using Poppler;

namespace Poppler.Net.Tests;

public sealed class CrossReferenceTests
{
    [Test]
    public void ReadsXrefAndCompressedObjectStreams()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithXrefStream());

        Assert.That(document.Pages, Is.EqualTo(1));
        Assert.That(document.CreatePage(0).Text(), Does.Contain("Compressed font object"));
    }

    [Test]
    public void AppliesLatestIncrementalRevision()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithIncrementalUpdate());

        Assert.That(document.Title, Is.EqualTo("Updated title"));
        Assert.That(document.Producer, Is.EqualTo("Incremental update"));
        Assert.That(document.XrefWasRepaired, Is.False);
    }

    [Test]
    public void RepairsBrokenStartXref()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithBrokenStartXref());

        Assert.That(document.XrefWasRepaired, Is.True);
        Assert.That(document.CreatePage(0).Text(), Does.Contain("Hello managed PDF"));
        Assert.That(
            document.Diagnostics.Any(diagnostic => diagnostic.Code == "xref.repair"),
            Is.True);
    }

    [Test]
    public void RepairRecoversXrefAndCompressedObjectStreams()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateXrefStreamWithBrokenStartXref());

        Assert.That(document.XrefWasRepaired, Is.True);
        Assert.That(document.CreatePage(0).Text(), Does.Contain("Compressed font object"));
    }

    [Test]
    public void TreatsOffsetsAsRelativeToHeaderAfterLeadingGarbage()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithLeadingGarbage());

        Assert.That(document.XrefWasRepaired, Is.False);
        Assert.That(document.CreatePage(0).Text(), Does.Contain("Hello managed PDF"));
        Assert.That(
            document.Diagnostics.Any(diagnostic => diagnostic.Code == "header.prefix"),
            Is.True);
    }

    [Test]
    public void RejectsAReferenceWithTheWrongGeneration()
    {
        Assert.That(
            (Action)(() => Document.LoadFromData(PdfFixtures.CreateWithWrongPageGeneration())),
            Throws.TypeOf<PdfFormatException>());
    }
}
