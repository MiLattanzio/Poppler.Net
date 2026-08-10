using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Graphics;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class GraphicsCompatibilityBeta1Tests
{
    [Test]
    public void CorpusCombinesGeometryTransparencyShadingAndPageBoxes()
    {
        using Document document = Load();
        PdfTransparencyGroupElement[] strokeGroups = document.CreatePage(0).Graphics
            .OfType<PdfTransparencyGroupElement>()
            .ToArray();
        Page maskedPage = document.CreatePage(1);
        PdfTransparencyGroupElement meshGroup = maskedPage.Graphics
            .OfType<PdfTransparencyGroupElement>()
            .Single();
        PdfPathElement dashedBoundary = maskedPage.Graphics
            .OfType<PdfPathElement>()
            .Single();
        Page rotated = document.CreatePage(2);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.Pages, Is.EqualTo(3));
            Assert.That(strokeGroups, Has.Length.EqualTo(2));
            Assert.That(strokeGroups, Has.All.Property("Isolated").True);
            Assert.That(
                strokeGroups.Select(group => group.State.Transform).Distinct().Count(),
                Is.EqualTo(2));
            Assert.That(
                strokeGroups.All(group =>
                    group.Elements.OfType<PdfPathElement>().Count() == 2),
                Is.True);

            Assert.That(meshGroup.State.SoftMask, Is.Not.Null);
            Assert.That(
                meshGroup.State.SoftMask!.Mode,
                Is.EqualTo(PdfSoftMaskMode.Luminosity));
            Assert.That(
                meshGroup.State.SoftMask.Elements
                    .OfType<PdfFunctionShadingElement>().Count(),
                Is.EqualTo(1));
            Assert.That(
                meshGroup.Elements.OfType<PdfMeshShadingElement>().Count(),
                Is.EqualTo(1));
            Assert.That(
                meshGroup.Elements.OfType<PdfMeshShadingElement>()
                    .Single().ClipPaths
                    .Any(clip => clip.FillRule == PdfFillRule.EvenOdd),
                Is.True);
            Assert.That(dashedBoundary.State.Dash.Segments, Has.Count.EqualTo(3));
            Assert.That(dashedBoundary.State.Dash.Phase, Is.EqualTo(-7));

            Assert.That(rotated.Rotation, Is.EqualTo(90));
            Assert.That(
                rotated.PageRect(PageBox.CropBox),
                Is.EqualTo(new PdfRectangle(25, 15, 255, 185)));
            Assert.That(
                rotated.Graphics.OfType<PdfTransparencyGroupElement>().Count(),
                Is.EqualTo(2));
        }));
    }

    [Test]
    public void RasterAndSvgOutputsMatchTheVersionedManifest()
    {
        using JsonDocument manifest = Manifest();
        JsonElement root = manifest.RootElement;
        string file = root.GetProperty("file").GetString()!;
        string fixtureHash = Hash(File.ReadAllBytes(
            Path.Combine(FixtureDirectory(), file)));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(fixtureHash, Is.EqualTo(root.GetProperty("sha256").GetString()));
            Assert.That(
                root.GetProperty("managed_png_hash_mode").GetString(),
                Is.EqualTo(CanonicalRenderingHash.PngMode));
            Assert.That(
                root.GetProperty("managed_svg_hash_mode").GetString(),
                Is.EqualTo(CanonicalRenderingHash.SvgMode));
        }));

        using Document document = Load();
        JsonElement png = root.GetProperty("managed_png_sha256");
        string[] actual72 = RenderHashes(document, 72, transparent: false);
        string[] actualTransparent = RenderHashes(document, 72, transparent: true);
        string[] expected72 = Strings(png.GetProperty("dpi72-aa4-opaque"));
        string[] expectedTransparent = Strings(
            root.GetProperty("managed_transparent_sha256"));

        var svg = new string[document.Pages];
        var svgContent = new string[document.Pages];
        for (int index = 0; index < document.Pages; index++)
        {
            svgContent[index] = document.CreatePage(index).RenderToSvg(
                new SvgRenderOptions { RasterFallbackDpi = 72 });
            svg[index] = CanonicalRenderingHash.Svg(svgContent[index]);
        }

        Assert.Multiple((Action)(() =>
        {
            Assert.That(actual72, Is.EqualTo(expected72), "72 DPI opaque");
            Assert.That(
                actualTransparent,
                Is.EqualTo(expectedTransparent),
                "72 DPI transparent");
            Assert.That(
                svg,
                Is.EqualTo(Strings(root.GetProperty("managed_svg_sha256"))),
                "SVG content");
            Assert.That(
                svgContent[1],
                Does.Contain("<image x=\"8\" y=\"24\" width=\"244\" height=\"148\""),
                "bounded non-rotated fallback");
            Assert.That(
                svgContent[2],
                Does.Contain("<image x=\"0\" y=\"0\" width=\"230\" height=\"170\""),
                "conservative rotated CropBox fallback");
        }));
    }

    private static string[] RenderHashes(
        Document document,
        double dpi,
        bool transparent)
    {
        var result = new string[document.Pages];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = CanonicalRenderingHash.Png(
                document.CreatePage(index).RenderToPng(new RasterRenderOptions
                {
                    Dpi = dpi,
                    Antialiasing = 4,
                    Transparent = transparent,
                    UseFontSubstitution = false
                }));
        }
        return result;
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static Document Load() =>
        Document.LoadFromFile(Path.Combine(
            FixtureDirectory(),
            "compatibility-beta1.pdf"));

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            FixtureDirectory(),
            "compatibility-beta1-fixture.json")));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
