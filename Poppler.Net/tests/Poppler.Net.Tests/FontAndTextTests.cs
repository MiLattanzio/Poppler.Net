using Poppler;

namespace Poppler.Net.Tests;

public sealed class FontAndTextTests
{
    [Test]
    public void DecodesDifferencesLigaturesAndUnicodeGlyphNames()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateSimpleFontFixture());
        Page page = document.CreatePage(0);

        Assert.That(page.Text(), Is.EqualTo("fi€😀"));
        Assert.That(page.Fonts, Has.Count.EqualTo(1));
        FontInfo font = page.Fonts[0];
        Assert.That(font.Type, Is.EqualTo(PdfFontType.Type1));
        Assert.That(font.Encoding, Is.EqualTo("WinAnsiEncoding"));
        Assert.That(font.IsSubset, Is.True);
        Assert.That(font.IsEmbedded, Is.False);
        Assert.That(font.HasToUnicode, Is.False);
    }

    [TestCase("Courier", "696D57", 1800)]
    [TestCase("Courier-Bold", "696D57", 1800)]
    [TestCase("Courier-BoldOblique", "696D57", 1800)]
    [TestCase("Courier-Oblique", "696D57", 1800)]
    [TestCase("Helvetica", "696D57", 1999)]
    [TestCase("Helvetica-Bold", "696D57", 2111)]
    [TestCase("Helvetica-BoldOblique", "696D57", 2111)]
    [TestCase("Helvetica-Oblique", "696D57", 1999)]
    [TestCase("Times-Bold", "696D57", 2111)]
    [TestCase("Times-BoldItalic", "696D57", 1945)]
    [TestCase("Times-Italic", "696D57", 1833)]
    [TestCase("Times-Roman", "696D57", 2000)]
    [TestCase("Symbol", "414243", 2111)]
    [TestCase("ZapfDingbats", "212223", 2909)]
    public void AppliesCanonicalBase14WidthsWhenWidthsAreOmitted(
        string baseFont,
        string characterCodes,
        int expectedUnits)
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.CreateBase14MetricsFixture(
                baseFont,
                characterCodes));

        TextBox box = document.CreatePage(0).TextList().Single();

        Assert.That(
            box.BoundingBox.Right - box.BoundingBox.Left,
            Is.EqualTo(expectedUnits * 48 / 1000.0).Within(0.01));
    }

    [TestCase(false, FontWritingMode.Horizontal)]
    [TestCase(true, FontWritingMode.Vertical)]
    public void DecodesIdentityCidFonts(
        bool vertical,
        FontWritingMode expectedWritingMode)
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateType0IdentityFixture(vertical));
        Page page = document.CreatePage(0);

        Assert.That(page.Text(), Is.EqualTo("Abc"));
        Assert.That(page.Fonts[0].Type, Is.EqualTo(PdfFontType.CidType2));
        Assert.That(page.Fonts[0].HasToUnicode, Is.True);
        Assert.That(page.Fonts[0].WritingMode, Is.EqualTo(expectedWritingMode));
        Assert.That(page.TextList()[0].WritingMode, Is.EqualTo(expectedWritingMode));
    }

    [Test]
    public void AppliesHorizontalCidWidths()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateType0IdentityFixture(vertical: false));

        TextBox box = document.CreatePage(0).TextList()[0];

        Assert.That(box.BoundingBox.Right - box.BoundingBox.Left, Is.EqualTo(36).Within(0.01));
        Assert.That(box.Rotation, Is.EqualTo(0));
    }

    [Test]
    public void AppliesVerticalCidMetrics()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateType0IdentityFixture(vertical: true));

        TextBox box = document.CreatePage(0).TextList()[0];

        Assert.That(box.BoundingBox.Top - box.BoundingBox.Bottom, Is.EqualTo(60).Within(0.01));
        Assert.That(box.Rotation, Is.EqualTo(270));
    }

    [Test]
    public void MapsCustomCodeSpaceToCidsAndUnicode()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateCustomCMapFixture());
        Page page = document.CreatePage(0);

        Assert.That(page.Text(), Is.EqualTo("ABC"));
        Assert.That(page.Fonts[0].Encoding, Is.EqualTo("Fixture-H"));
        Assert.That(
            page.TextList()[0].BoundingBox.Right - page.TextList()[0].BoundingBox.Left,
            Is.EqualTo(36).Within(0.01));
    }

    [Test]
    public void AppliesType3FontMatrixToWidths()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateType3Fixture());
        Page page = document.CreatePage(0);

        Assert.That(page.Text(), Is.EqualTo("AB"));
        Assert.That(page.Fonts[0].Type, Is.EqualTo(PdfFontType.Type3));
        Assert.That(
            page.TextList()[0].BoundingBox.Right - page.TextList()[0].BoundingBox.Left,
            Is.EqualTo(22).Within(0.01));
    }

    [Test]
    public void ReadsEncodingFromAnEmbeddedType1Program()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateEmbeddedType1EncodingFixture());
        Page page = document.CreatePage(0);

        Assert.That(page.Text(), Is.EqualTo("fi"));
        Assert.That(page.Fonts[0].EmbeddedFormat, Is.EqualTo(EmbeddedFontFormat.Type1));
        Assert.That(page.Fonts[0].IsEmbedded, Is.True);
    }

    [Test]
    public void OffersPhysicalRawAndColumnReadingOrder()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateColumnLayoutFixture());
        Page page = document.CreatePage(0);

        Assert.That(
            page.Text(layout: TextLayout.RawOrder),
            Is.EqualTo("Left oneRight oneLeft twoRight two"));
        Assert.That(
            NormalizeLines(page.Text(layout: TextLayout.Physical)),
            Is.EqualTo("Left one Right one|Left two Right two"));
        Assert.That(
            NormalizeLines(page.Text(layout: TextLayout.NonRawNonPhysical)),
            Is.EqualTo("Left one|Left two|Right one|Right two"));
    }

    [Test]
    public void OrdersRightToLeftRunsByTheirPhysicalDirection()
    {
        using Document document =
            Document.LoadFromData(PdfFixtures.CreateRightToLeftFixture());
        Page page = document.CreatePage(0);

        Assert.That(page.Text(layout: TextLayout.RawOrder), Is.EqualTo("בא"));
        Assert.That(page.Text(layout: TextLayout.Physical), Is.EqualTo("אב"));
        Assert.That(page.TextList().All(box => box.IsRightToLeft), Is.True);
    }

    [Test]
    public void EnforcesCMapMappingLimit()
    {
        var options = new PdfReadOptions { MaximumCMapMappings = 1 };
        using Document document =
            Document.LoadFromData(
                PdfFixtures.CreateType0IdentityFixture(vertical: false),
                options: options);
        Page page = document.CreatePage(0);

        Assert.That(
            (Action)(() => _ = page.Fonts),
            Throws.TypeOf<PdfLimitException>());
    }

    private static string NormalizeLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\n', '|');
}
