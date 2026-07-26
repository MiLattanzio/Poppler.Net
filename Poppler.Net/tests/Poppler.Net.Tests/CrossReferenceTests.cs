using Poppler;

namespace Poppler.Net.Tests;

public sealed class CrossReferenceTests
{
    [Fact]
    public void ReadsXrefAndCompressedObjectStreams()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithXrefStream());

        Assert.Equal(1, document.Pages);
        Assert.Contains("Compressed font object", document.CreatePage(0).Text());
    }

    [Fact]
    public void AppliesLatestIncrementalRevision()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithIncrementalUpdate());

        Assert.Equal("Updated title", document.Title);
        Assert.Equal("Incremental update", document.Producer);
        Assert.False(document.XrefWasRepaired);
    }

    [Fact]
    public void RepairsBrokenStartXref()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithBrokenStartXref());

        Assert.True(document.XrefWasRepaired);
        Assert.Contains("Hello managed PDF", document.CreatePage(0).Text());
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "xref.repair");
    }

    [Fact]
    public void RepairRecoversXrefAndCompressedObjectStreams()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateXrefStreamWithBrokenStartXref());

        Assert.True(document.XrefWasRepaired);
        Assert.Contains("Compressed font object", document.CreatePage(0).Text());
    }

    [Fact]
    public void TreatsOffsetsAsRelativeToHeaderAfterLeadingGarbage()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithLeadingGarbage());

        Assert.False(document.XrefWasRepaired);
        Assert.Contains("Hello managed PDF", document.CreatePage(0).Text());
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "header.prefix");
    }

    [Fact]
    public void RejectsAReferenceWithTheWrongGeneration()
    {
        Assert.Throws<PdfFormatException>(
            () => Document.LoadFromData(PdfFixtures.CreateWithWrongPageGeneration()));
    }
}
