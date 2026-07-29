using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class RenderingBeta2Tests
{
    [Test]
    public void DecodesAllFourMeshShadingKinds()
    {
        using Document document = Load();

        PdfMeshShadingElement[] gouraud = document.CreatePage(0).Graphics
            .OfType<PdfMeshShadingElement>()
            .ToArray();
        Assert.That(gouraud, Has.Length.EqualTo(2));
        Assert.That(
            gouraud.Select(element => element.Shading.Kind),
            Is.EqualTo(new[]
            {
                PdfShadingKind.FreeFormGouraud,
                PdfShadingKind.LatticeGouraud
            }));
        Assert.That(
            gouraud.Select(element => element.Shading.Triangles.Count),
            Is.EqualTo(new[] { 2, 2 }));

        PdfMeshShadingElement[] patches = document.CreatePage(1).Graphics
            .OfType<PdfMeshShadingElement>()
            .ToArray();
        Assert.That(patches, Has.Length.EqualTo(2));
        Assert.That(
            patches.Select(element => element.Shading.Kind),
            Is.EqualTo(new[]
            {
                PdfShadingKind.CoonsPatch,
                PdfShadingKind.TensorProductPatch
            }));
        Assert.That(
            patches.Select(element => element.Shading.Triangles.Count),
            Is.EqualTo(new[] { 288, 288 }));
    }

    [Test]
    public void RendersPatchMeshesWithoutTessellationSeams()
    {
        using Document document = Load();
        PdfBitmap bitmap = Render(document.CreatePage(1));

        AssertPixel(bitmap, 60, 80, 123, 125, 220, 255, tolerance: 2);
        AssertPixel(bitmap, 180, 80, 94, 104, 218, 255, tolerance: 2);
    }

    [Test]
    public void ReusesUncoloredPatternWithIndependentUnderlyingColors()
    {
        using Document document = Load();
        Page page = document.CreatePage(2);
        PdfTilingPatternBrush[] patterns = page.Graphics
            .OfType<PdfPathElement>()
            .Select(element => element.State.Fill)
            .OfType<PdfTilingPatternBrush>()
            .ToArray();

        Assert.That(patterns, Has.Length.EqualTo(2));
        Assert.That(patterns, Has.All.Property(nameof(PdfTilingPatternBrush.IsColored)).False);
        Assert.That(patterns[0].UnderlyingColor, Is.EqualTo(PdfColor.Rgb(1, 0, 0)));
        Assert.That(patterns[1].UnderlyingColor, Is.EqualTo(PdfColor.Rgb(0, 0, 1)));

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 30, 80, 255, 0, 0, 255);
        AssertPixel(bitmap, 150, 80, 0, 0, 255, 255);
        AssertPixel(bitmap, 117, 80, 255, 255, 255, 255);
    }

    [Test]
    public void AppliesCalculatorTransferAndPreservesKnockoutMetadata()
    {
        using Document document = Load();
        Page page = document.CreatePage(3);
        PdfPathElement masked = page.Graphics
            .OfType<PdfPathElement>()
            .Single(element => element.State.SoftMask is not null);
        Assert.That(masked.State.SoftMask!.HasTransferFunction, Is.True);

        PdfTransparencyGroupElement group = page.Graphics
            .OfType<PdfTransparencyGroupElement>()
            .Single();
        Assert.That(group.Isolated, Is.True);
        Assert.That(group.Knockout, Is.True);

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 60, 80, 255, 190, 190, 255, tolerance: 2);
        AssertPixel(bitmap, 180, 80, 127, 127, 255, 255, tolerance: 2);
    }

    [Test]
    public void DistinguishesIsolatedAndNonIsolatedGroups()
    {
        using Document document = Load();
        Page page = document.CreatePage(4);
        PdfTransparencyGroupElement[] groups = page.Graphics
            .OfType<PdfTransparencyGroupElement>()
            .ToArray();

        Assert.That(groups, Has.Length.EqualTo(2));
        Assert.That(groups[0].Isolated, Is.False);
        Assert.That(groups[1].Isolated, Is.True);

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 60, 80, 128, 64, 0, 255, tolerance: 2);
        AssertPixel(bitmap, 180, 80, 127, 63, 64, 255, tolerance: 2);
    }

    [Test]
    public void SimulatesProcessOverprintModeOne()
    {
        using Document document = Load();
        Page page = document.CreatePage(5);
        PdfPathElement[] paths = page.Graphics.OfType<PdfPathElement>().ToArray();
        Assert.That(paths[2].State.FillOverprint, Is.True);
        Assert.That(paths[2].State.OverprintMode, Is.EqualTo(1));
        Assert.That(paths[3].State.FillOverprint, Is.False);
        Assert.That(paths[3].State.OverprintMode, Is.EqualTo(0));

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 60, 80, 0, 0, 255, 255);
        AssertPixel(bitmap, 180, 80, 255, 0, 255, 255);
    }

    [Test]
    public void EnforcesMeshTriangleLimit()
    {
        using Document document = Load(new PdfReadOptions
        {
            MaximumMeshTriangles = 1
        });

        Assert.That(
            (Action)(() => _ = document.CreatePage(0).Graphics),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void Beta2FixtureHashMatchesManifest()
    {
        string directory = FixtureDirectory();
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(directory, "rendering-beta2-fixture.json")));
        string file = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, file))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "rendering-beta2.pdf"),
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
            Assert.That(actualRed, Is.InRange(
                (byte)Math.Max(0, red - tolerance),
                (byte)Math.Min(255, red + tolerance)));
            Assert.That(actualGreen, Is.InRange(
                (byte)Math.Max(0, green - tolerance),
                (byte)Math.Min(255, green + tolerance)));
            Assert.That(actualBlue, Is.InRange(
                (byte)Math.Max(0, blue - tolerance),
                (byte)Math.Min(255, blue + tolerance)));
            Assert.That(actualAlpha, Is.InRange(
                (byte)Math.Max(0, alpha - tolerance),
                (byte)Math.Min(255, alpha + tolerance)));
        }));
    }
}
