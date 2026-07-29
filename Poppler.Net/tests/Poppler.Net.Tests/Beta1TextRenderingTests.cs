using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class Beta1TextRenderingTests
{
    [Test]
    public void ResolvesNamedExternalCMapsAndUseCMapInheritance()
    {
        using Document document = Load();
        Page page = document.CreatePage(1);

        Assert.That(page.Text(layout: TextLayout.RawOrder), Is.EqualTo("AB"));
        Assert.That(page.Fonts, Has.Count.EqualTo(1));
        Assert.That(page.Fonts[0].Encoding, Is.EqualTo("BetaBase-V"));
        Assert.That(
            page.Fonts[0].WritingMode,
            Is.EqualTo(FontWritingMode.Vertical));
        PdfTextElement text = page.Graphics
            .OfType<PdfTextElement>()
            .Single();
        Assert.That(text.Text, Is.EqualTo("AB"));
        Assert.That(text.GlyphCount, Is.EqualTo(2));
    }

    [Test]
    public void RasterizesCff2AndEscapedType2Arithmetic()
    {
        using Document document = Load();
        PdfBitmap bitmap = document.CreatePage(0).Render(NoSubstitution());

        Assert.That(
            CountBlack(bitmap, 20, 55, 60, 115),
            Is.GreaterThan(500),
            "the first CFF2 glyph must be present");
        Assert.That(
            CountBlack(bitmap, 60, 55, 100, 115),
            Is.GreaterThan(500),
            "the second glyph exercises the escaped Type 2 add operator");
    }

    [Test]
    public void AppliesVerticalGsubAlternateToEmbeddedCff2Glyph()
    {
        using Document document = Load();
        PdfBitmap bitmap = document.CreatePage(1).Render(NoSubstitution());

        Assert.That(
            CountBlack(bitmap, 100, 60, 115, 120),
            Is.GreaterThan(200),
            "A.vert must replace the horizontal A in vertical writing mode");
        Assert.That(
            CountBlack(bitmap, 72, 130, 88, 195),
            Is.GreaterThan(200),
            "the following non-substituted B must retain its own outline");
    }

    [Test]
    public void ShapesMultiRunePdfGlyphWithStandardLigature()
    {
        using Document document = Load();
        PdfBitmap bitmap = document.CreatePage(2).Render(Substitution());

        Assert.That(document.CreatePage(2).Text(), Is.EqualTo("fi"));
        Assert.That(
            CountBlack(bitmap, 55, 45, 70, 70),
            Is.GreaterThan(50),
            "the fi GSUB ligature extends beyond the standalone f outline");
    }

    [Test]
    public void PrefersNarrowSubstituteForNarrowPdfFont()
    {
        using Document document = Load();
        PdfBitmap bitmap = document.CreatePage(3).Render(Substitution());

        Assert.That(
            CountBlack(bitmap, 47, 45, 57, 105),
            Is.GreaterThan(200));
        Assert.That(
            CountBlack(bitmap, 33, 45, 44, 105),
            Is.EqualTo(0),
            "the wide Helvetica candidate must not win over HelveticaNarrow");
    }

    [Test]
    public void Beta1CorpusHashesMatchManifest()
    {
        string fixtures = FixtureDirectory();
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(
                fixtures,
                "rendering-beta1-fixture.json")));
        foreach (JsonElement entry in
                 manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string relative = entry.GetProperty("file").GetString()!;
            string expected = entry.GetProperty("sha256").GetString()!;
            string actual = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(
                        Path.Combine(fixtures, relative))))
                .ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(expected), relative);
        }
    }

    private static RasterRenderOptions NoSubstitution() => new()
    {
        Dpi = 72,
        Antialiasing = 2,
        UseFontSubstitution = false
    };

    private static RasterRenderOptions Substitution() => new()
    {
        Dpi = 72,
        Antialiasing = 2,
        FontDirectories = new[]
        {
            Path.Combine(FixtureDirectory(), "beta-fonts")
        }
    };

    private static int CountBlack(
        PdfBitmap bitmap,
        int left,
        int top,
        int right,
        int bottom)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = bitmap.Data.Span;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset = y * bitmap.BytesPerRow + x * 4;
                if (pixels[offset + 3] > 200 &&
                    pixels[offset] < 50 &&
                    pixels[offset + 1] < 50 &&
                    pixels[offset + 2] < 50)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static Document Load() =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "rendering-beta1.pdf"),
            options: new PdfReadOptions
            {
                UseSystemCMaps = false,
                CMapDirectories = new[]
                {
                    Path.Combine(FixtureDirectory(), "cmaps")
                }
            });

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
