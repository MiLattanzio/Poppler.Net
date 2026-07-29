using Poppler;
using System.Security.Cryptography;
using System.Text.Json;

namespace Poppler.Net.Tests;

public sealed class EmbeddedFontTests
{
    [TestCase(
        "truetype-cmap-fallback.pdf",
        PdfFontType.CidType2,
        EmbeddedFontFormat.TrueType)]
    [TestCase(
        "opentype-cff-cmap-fallback.pdf",
        PdfFontType.CidType0,
        EmbeddedFontFormat.OpenType)]
    public void UsesEmbeddedSfntCmapWhenToUnicodeIsMissing(
        string fileName,
        PdfFontType expectedType,
        EmbeddedFontFormat expectedFormat)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        using Document document = Document.LoadFromFile(path);
        Page page = document.CreatePage(0);

        Assert.That(page.Text(), Is.EqualTo("ABC"));
        Assert.That(page.Fonts, Has.Count.EqualTo(1));
        FontInfo font = page.Fonts[0];
        Assert.That(font.Type, Is.EqualTo(expectedType));
        Assert.That(font.EmbeddedFormat, Is.EqualTo(expectedFormat));
        Assert.That(font.IsEmbedded, Is.True);
        Assert.That(font.EmbeddedLength, Is.GreaterThan(0));
        Assert.That(font.HasToUnicode, Is.False);
    }

    [Test]
    public void EmbeddedFontFixtureHashesMatchManifest()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixtureDirectory, "font-fixtures.json")));
        foreach (JsonElement fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string fileName = fixture.GetProperty("file").GetString()!;
            string expected = fixture.GetProperty("sha256").GetString()!;
            string actual = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureDirectory, fileName))))
                .ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(expected), fileName);
        }
    }
}
