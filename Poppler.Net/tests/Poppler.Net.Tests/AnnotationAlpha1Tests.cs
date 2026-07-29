using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class AnnotationAlpha1Tests
{
    [Test]
    public void ReadsImmutableAnnotationMetadataAndLinkActions()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);

        Assert.That(page.Annotations, Has.Count.EqualTo(5));
        PdfAnnotation uri = page.Annotations[0];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(uri.Type, Is.EqualTo(PdfAnnotationType.Link));
            Assert.That(uri.Rectangle, Is.EqualTo(new PdfRectangle(20, 150, 140, 190)));
            Assert.That(uri.Contents, Is.EqualTo("URI appearance"));
            Assert.That(uri.HasAppearance, Is.True);
            Assert.That(uri.Action.Type, Is.EqualTo(PdfAnnotationActionType.Uri));
            Assert.That(uri.Action.Uri, Is.EqualTo("https://example.test/alpha1"));
        }));

        PdfAnnotation direct = page.Annotations[1];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(direct.Border.Style, Is.EqualTo(PdfAnnotationBorderStyleKind.Dashed));
            Assert.That(direct.Border.DashPattern, Is.EqualTo(new[] { 4d, 2d }));
            Assert.That(direct.Action.Type, Is.EqualTo(PdfAnnotationActionType.GoTo));
            Assert.That(direct.Action.Destination!.PageIndex, Is.EqualTo(1));
            Assert.That(direct.Action.Destination.Type, Is.EqualTo(PdfDestinationType.Xyz));
            Assert.That(direct.Action.Destination.Left, Is.EqualTo(25));
            Assert.That(direct.Action.Destination.Top, Is.EqualTo(180));
            Assert.That(direct.Action.Destination.Zoom, Is.EqualTo(1.5));
        }));

        PdfAnnotation note = page.Annotations[3];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(note.Type, Is.EqualTo(PdfAnnotationType.Text));
            Assert.That(note.Title, Is.EqualTo("Mi Lattanzio"));
            Assert.That(note.Subject, Is.EqualTo("Fallback icon"));
            Assert.That(note.IconName, Is.EqualTo("Comment"));
            Assert.That(note.ModificationDate!.Value.Year, Is.EqualTo(2026));
        }));
        Assert.That(
            page.Annotations[4].Flags,
            Is.EqualTo(PdfAnnotationFlags.Hidden | PdfAnnotationFlags.NoView));
    }

    [Test]
    public void ResolvesLegacyAndNameTreeDestinations()
    {
        using Document document = Load();

        Assert.That(
            document.NamedDestinations.Keys,
            Is.EqualTo(new[] { "chapter-four", "chapter-three", "legacy" }));
        PdfDestination chapter = document.ResolveDestination("chapter-three")!;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(chapter.PageIndex, Is.EqualTo(2));
            Assert.That(chapter.Type, Is.EqualTo(PdfDestinationType.FitHorizontal));
            Assert.That(chapter.Top, Is.EqualTo(180));
            Assert.That(chapter.NamedDestination, Is.EqualTo("chapter-three"));
        }));

        PdfAnnotation namedLink = document.CreatePage(0).Annotations[2];
        Assert.That(namedLink.Action.NamedTarget, Is.EqualTo("chapter-three"));
        Assert.That(namedLink.Action.Destination!.PageIndex, Is.EqualTo(2));
        PdfAnnotation legacyLink = document.CreatePage(2).Annotations[4];
        Assert.That(legacyLink.Action.Destination!.PageIndex, Is.EqualTo(3));
        Assert.That(legacyLink.Action.Destination.NamedDestination, Is.EqualTo("legacy"));
        Assert.That(document.ResolveDestination("missing"), Is.Null);
        Assert.That(document.ResolveDestination("loop-a"), Is.Null);
        Assert.That(
            document.Diagnostics.Any(
                diagnostic => diagnostic.Code == "destination.named.circular"),
            Is.True);
    }

    [Test]
    public void PaintsAppearancesAfterPageContentAndSkipsHiddenAnnotations()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);
        int firstAnnotation = page.Graphics
            .Select((element, index) => (element, index))
            .First(item => item.element.SourceResource is not null)
            .index;

        Assert.That(
            page.Graphics.Take(firstAnnotation),
            Has.All.Property(nameof(PdfGraphicsElement.SourceResource)).Null);
        Assert.That(
            page.Graphics.Skip(firstAnnotation),
            Has.All.Property(nameof(PdfGraphicsElement.SourceResource)).Not.Null);
        Assert.That(
            page.Graphics.Any(element =>
                element.SourceResource?.StartsWith(
                    "Annotation[5]",
                    StringComparison.Ordinal) == true),
            Is.False);

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 80, 50, 0, 191, 0, 255);
        AssertPixel(bitmap, 235, 115, 230, 230, 230, 255);
    }

    [Test]
    public void RendersDeterministicManagedFallbacks()
    {
        using Document document = Load();
        Page page = document.CreatePage(1);
        PdfAnnotation square = page.Annotations[2];
        Assert.That(square.InteriorColor, Is.EqualTo(PdfColor.Rgb(1, 0.8, 0.8)));
        Assert.That(page.Graphics, Has.Count.EqualTo(11));

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 200, 45, 255, 204, 204, 255);
        AssertPixel(bitmap, 275, 45, 204, 204, 255, 255);
        AssertPixel(bitmap, 80, 102, 178, 178, 98, 255, tolerance: 2);
        AssertPixel(bitmap, 205, 165, 242, 204, 242, 255);
    }

    [Test]
    public void MapsAppearanceBoxesMatricesStatesAndNestedForms()
    {
        using Document document = Load();
        Page page = document.CreatePage(2);
        _ = page.Graphics;

        Assert.That(
            page.Graphics.Count(element =>
                element.SourceResource?.StartsWith(
                    "Annotation[1]/Stamp",
                    StringComparison.Ordinal) == true),
            Is.EqualTo(2));
        Assert.That(
            page.Graphics.Any(element =>
                element.SourceResource?.Contains(
                    "Annotation[3]/FreeText/Child",
                    StringComparison.Ordinal) == true),
            Is.True);
        Assert.That(
            document.Diagnostics.Select(diagnostic => diagnostic.Code),
            Does.Contain("graphics.form.recursive"));

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 80, 45, 0, 153, 0, 255);
        AssertPixel(bitmap, 80, 85, 255, 0, 0, 255);
        AssertPixel(bitmap, 220, 55, 242, 166, 0, 255, tolerance: 1);
        AssertPixel(bitmap, 70, 155, 26, 89, 242, 255, tolerance: 1);
    }

    [Test]
    public async Task ConcurrentAnnotationReadsAndRenderingAreDeterministic()
    {
        using Document document = Load();
        string expected = AnnotationSummary(document);

        Task<string>[] operations = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => AnnotationSummary(document)))
            .ToArray();
        string[] results = await Task.WhenAll(operations);

        Assert.That(results, Has.All.EqualTo(expected));
    }

    [Test]
    public void EnforcesAnnotationCountLimit()
    {
        using Document document = Load(new PdfReadOptions
        {
            MaximumAnnotationsPerPage = 4
        });

        Assert.That(
            (Action)(() => _ = document.CreatePage(0).Annotations),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void EnforcesAnnotationPointLimit()
    {
        using Document document = Load(new PdfReadOptions
        {
            MaximumAnnotationPoints = 3
        });

        Assert.That(
            (Action)(() => _ = document.CreatePage(1).Annotations),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void ValidatesAnnotationOptions()
    {
        Assert.That(
            (Action)(() => Document.LoadFromData(
                PdfFixtures.Create(compressContent: false),
                options: new PdfReadOptions
                {
                    MaximumAnnotationAppearanceDepth = 0
                })),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void AnnotationFixtureHashMatchesManifest()
    {
        string directory = FixtureDirectory();
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(directory, "annotations-alpha1-fixture.json")));
        string file = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, file))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static string AnnotationSummary(Document document)
    {
        Page page = document.CreatePage(0);
        string metadata = string.Join(
            "|",
            page.Annotations.Select(annotation =>
                $"{annotation.Type}:{annotation.Action.Type}:" +
                $"{annotation.Action.Destination?.PageIndex}"));
        byte[] png = page.RenderToPng(new RasterRenderOptions
        {
            Dpi = 36,
            Antialiasing = 1,
            UseFontSubstitution = false
        });
        return $"{metadata}|{Convert.ToHexString(SHA256.HashData(png))}";
    }

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "annotations-alpha1.pdf"),
            options: options);

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static PdfBitmap Render(Page page) =>
        page.Render(new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false
        });

    private static void AssertPixel(
        PdfBitmap bitmap,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        int tolerance = 0)
    {
        ReadOnlySpan<byte> pixels = bitmap.Data.Span;
        int offset = y * bitmap.BytesPerRow + x * 4;
        byte actualRed = pixels[offset];
        byte actualGreen = pixels[offset + 1];
        byte actualBlue = pixels[offset + 2];
        byte actualAlpha = pixels[offset + 3];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                actualRed,
                Is.InRange(
                    (byte)Math.Max(0, red - tolerance),
                    (byte)Math.Min(255, red + tolerance)));
            Assert.That(
                actualGreen,
                Is.InRange(
                    (byte)Math.Max(0, green - tolerance),
                    (byte)Math.Min(255, green + tolerance)));
            Assert.That(
                actualBlue,
                Is.InRange(
                    (byte)Math.Max(0, blue - tolerance),
                    (byte)Math.Min(255, blue + tolerance)));
            Assert.That(
                actualAlpha,
                Is.InRange(
                    (byte)Math.Max(0, alpha - tolerance),
                    (byte)Math.Min(255, alpha + tolerance)));
        }));
    }
}
