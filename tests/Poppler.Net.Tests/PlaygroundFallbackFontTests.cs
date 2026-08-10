using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class PlaygroundFallbackFontTests
{
    private static readonly string[] ExpectedFiles =
    {
        "DejaVuSans.ttf",
        "DejaVuSans-Bold.ttf",
        "DejaVuSans-Oblique.ttf",
        "DejaVuSans-BoldOblique.ttf"
    };

    [TestCase("Helvetica")]
    [TestCase("Helvetica-Bold")]
    [TestCase("Helvetica-Oblique")]
    [TestCase("Helvetica-BoldOblique")]
    public void BundledFallbackRendersUnembeddedBase14Fonts(string pdfFontName)
    {
        string directory = FontDirectory();
        var resolver = new PdfFontSubstitutionResolver(new RasterRenderOptions
        {
            FontDirectories = new[] { directory }
        });

        bool found = resolver.TryGetGlyph(
            pdfFontName,
            "È",
            FontWritingMode.Horizontal,
            out PdfGraphicsPath path,
            out double advance);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(path.IsEmpty, Is.False);
            Assert.That(advance, Is.GreaterThan(0));
        }));
    }

    [Test]
    public void BundledFallbackIncludesEveryRuntimeAssetAndItsLicense()
    {
        string directory = FontDirectory();

        Assert.Multiple((Action)(() =>
        {
            foreach (string fileName in ExpectedFiles)
                Assert.That(new FileInfo(Path.Combine(directory, fileName)).Length, Is.GreaterThan(0), fileName);
            Assert.That(
                File.ReadAllText(Path.Combine(directory, "LICENSE-DejaVu.txt")),
                Does.Contain("Bitstream Vera Fonts Copyright"));
            Assert.That(
                File.ReadAllText(Path.Combine(directory, "NOTICE.md")),
                Does.Contain("7576310B219E04159D35FF61DD4A4EC4CDBA4F35C00E002A136F00E96A908B0A"));
        }));
    }

    private static string FontDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "PlaygroundFonts");
}
