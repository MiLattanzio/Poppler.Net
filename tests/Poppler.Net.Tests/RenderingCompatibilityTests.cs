using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class RenderingCompatibilityTests
{
    [Test]
    public void RetainsTextAndInlineImagesInExactDisplayListOrder()
    {
        using Document document = Load("rendering-compatibility.pdf");
        IReadOnlyList<PdfGraphicsElement> graphics =
            document.CreatePage(0).Graphics;

        int textIndex = IndexOf<PdfTextElement>(
            graphics,
            element => element.FontResourceName == "FBase");
        int overlayIndex = IndexOf<PdfPathElement>(
            graphics,
            element =>
                element.State.Fill is PdfSolidBrush solid &&
                solid.Color == PdfColor.Rgb(0, 0, 1));
        int imageIndex = IndexOf<PdfImageElement>(graphics, _ => true);

        Assert.That(textIndex, Is.GreaterThan(0));
        Assert.That(overlayIndex, Is.GreaterThan(textIndex));
        Assert.That(imageIndex, Is.GreaterThan(overlayIndex));

        PdfTextElement text = (PdfTextElement)graphics[textIndex];
        Assert.That(text.Text, Is.EqualTo("ABC"));
        Assert.That(text.FontName, Is.EqualTo("Helvetica"));
        Assert.That(text.RenderingMode, Is.EqualTo(PdfTextRenderingMode.Fill));
        Assert.That(text.GlyphCount, Is.EqualTo(3));

        PdfImageElement image = (PdfImageElement)graphics[imageIndex];
        Assert.That(image.ResourceName, Does.StartWith("InlineImage"));
        Assert.That(image.Width, Is.EqualTo(2));
        Assert.That(image.Height, Is.EqualTo(1));
        Assert.That(image.ColorSpace, Is.EqualTo("DeviceRGB"));
        Assert.That(image.Image, Is.Not.Null);
    }

    [Test]
    public void RendersBase14FontThroughManagedFileSubstitution()
    {
        using Document document = Load("rendering-compatibility.pdf");
        Page page = document.CreatePage(0);
        string fixtures = FixtureDirectory();
        PdfBitmap substituted = page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            FontDirectories = new[] { fixtures }
        });
        PdfBitmap withoutSubstitution = page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false
        });

        Assert.That(
            CountPixels(substituted, static (r, g, b, a) =>
                a > 200 && r < 30 && g < 30 && b < 30),
            Is.GreaterThan(100));
        Assert.That(
            CountPixels(withoutSubstitution, static (r, g, b, a) =>
                a > 200 && r < 30 && g < 30 && b < 30),
            Is.EqualTo(0));
    }

    [Test]
    public void DecodesAndPaintsRawInlineImageSamples()
    {
        using Document document = Load("rendering-compatibility.pdf");
        PdfBitmap bitmap = document.CreatePage(0).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 1,
            UseFontSubstitution = false
        });

        AssertPixel(bitmap, 235, 95, 255, 0, 0, 255);
        AssertPixel(bitmap, 275, 95, 0, 255, 0, 255);
    }

    [Test]
    public void ExecutesType3CharProcsInsideTextPosition()
    {
        using Document document = Load("rendering-compatibility.pdf");
        Page page = document.CreatePage(0);

        Assert.That(
            page.Graphics.OfType<PdfTextElement>()
                .Any(element => element.FontResourceName == "FType3"),
            Is.True);
        Assert.That(
            page.Graphics.OfType<PdfPathElement>()
                .Any(element => element.SourceResource == "Type3:A"),
            Is.True);

        PdfBitmap bitmap = page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false
        });
        Assert.That(
            CountPixels(
                bitmap,
                35,
                190,
                85,
                245,
                static (r, g, b, a) =>
                    a > 200 && r < 30 && g < 30 && b > 180),
            Is.GreaterThan(100));
        Assert.That(
            CountPixels(
                bitmap,
                35,
                190,
                85,
                245,
                static (r, g, b, a) =>
                    a > 200 && r < 30 && g > 100 && b < 30),
            Is.EqualTo(0));

        string svg = page.RenderToSvg();
        Assert.That(
            CountOccurrences(svg, "<text "),
            Is.EqualTo(3),
            "the Type 3 CharProc path must not be duplicated as platform text");
    }

    [Test]
    public void RasterizesEmbeddedOpenTypeCffOutlines()
    {
        using Document document = Load("opentype-cff-cmap-fallback.pdf");
        PdfBitmap bitmap = document.CreatePage(0).Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false
        });

        for (int left = 70; left < 112; left += 14)
        {
            Assert.That(
                CountPixels(
                    bitmap,
                    left,
                    70,
                    left + 16,
                    100,
                    static (r, g, b, a) =>
                        a > 200 && r < 100 && g < 100 && b < 100),
                Is.GreaterThan(15),
                $"missing CFF glyph near x={left}");
        }
    }

    [Test]
    public void RasterizesType1CharStringsAndAppliesTextClipping()
    {
        using Document document = Load("type1-rendering.pdf");
        Page page = document.CreatePage(0);
        PdfTextElement[] text = page.Graphics.OfType<PdfTextElement>().ToArray();

        Assert.That(text, Has.Length.EqualTo(2));
        Assert.That(text[0].RenderingMode, Is.EqualTo(PdfTextRenderingMode.Fill));
        Assert.That(text[1].RenderingMode, Is.EqualTo(PdfTextRenderingMode.Clip));
        PdfPathElement clipped = page.Graphics
            .OfType<PdfPathElement>()
            .Single(element => element.SourceResource is null);
        Assert.That(clipped.ClipPaths, Is.Not.Empty);

        PdfBitmap bitmap = page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false
        });
        int black = CountPixels(bitmap, static (r, g, b, a) =>
            a > 200 && r < 40 && g < 40 && b < 40);
        int blue = CountPixels(bitmap, static (r, g, b, a) =>
            a > 200 && r < 30 && g < 30 && b > 180);
        Assert.That(black, Is.GreaterThan(100));
        Assert.That(blue, Is.GreaterThan(100));
        Assert.That(blue, Is.LessThan(10_000));
        for (int left = 25; left < 145; left += 40)
        {
            Assert.That(
                CountPixels(
                    bitmap,
                    left,
                    30,
                    left + 50,
                    100,
                    static (r, g, b, a) =>
                        a > 200 && r < 40 && g < 40 && b < 40),
                Is.GreaterThan(100),
                $"missing Type 1 glyph near x={left}");
        }
    }

    [Test]
    public void CompatibilityFixtureHashesMatchManifest()
    {
        string directory = FixtureDirectory();
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(
                directory,
                "rendering-compatibility-fixtures.json")));
        foreach (JsonElement fixture in
                 manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string file = fixture.GetProperty("file").GetString()!;
            string expected = fixture.GetProperty("sha256").GetString()!;
            string actual = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(
                        Path.Combine(directory, file))))
                .ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(expected), file);
        }
    }

    private static int IndexOf<T>(
        IReadOnlyList<PdfGraphicsElement> elements,
        Func<T, bool> predicate)
        where T : PdfGraphicsElement
    {
        for (int index = 0; index < elements.Count; index++)
        {
            if (elements[index] is T candidate && predicate(candidate))
                return index;
        }
        return -1;
    }

    private static int CountPixels(
        PdfBitmap bitmap,
        Func<byte, byte, byte, byte, bool> predicate)
    {
        ReadOnlySpan<byte> pixels = bitmap.Data.Span;
        int count = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (predicate(
                    pixels[index],
                    pixels[index + 1],
                    pixels[index + 2],
                    pixels[index + 3]))
            {
                count++;
            }
        }
        return count;
    }

    private static int CountOccurrences(string value, string text)
    {
        int count = 0;
        int position = 0;
        while ((position = value.IndexOf(
                   text,
                   position,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += text.Length;
        }
        return count;
    }

    private static int CountPixels(
        PdfBitmap bitmap,
        int left,
        int top,
        int right,
        int bottom,
        Func<byte, byte, byte, byte, bool> predicate)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = bitmap.Data.Span;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int index = y * bitmap.BytesPerRow + x * 4;
                if (predicate(
                        pixels[index],
                        pixels[index + 1],
                        pixels[index + 2],
                        pixels[index + 3]))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static void AssertPixel(
        PdfBitmap bitmap,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = y * bitmap.BytesPerRow + x * 4;
        ReadOnlySpan<byte> pixels = bitmap.Data.Span;
        Assert.That(pixels[offset], Is.EqualTo(red), "red");
        Assert.That(pixels[offset + 1], Is.EqualTo(green), "green");
        Assert.That(pixels[offset + 2], Is.EqualTo(blue), "blue");
        Assert.That(pixels[offset + 3], Is.EqualTo(alpha), "alpha");
    }

    private static Document Load(string fileName) =>
        Document.LoadFromFile(Path.Combine(FixtureDirectory(), fileName));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
