using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Poppler;
using Poppler.Graphics;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class ShadingAlpha3CorpusTests
{
    [Test]
    public void CorpusExposesTheApprovedFunctionAndMeshFamilies()
    {
        using Document document = Load();

        PdfFunctionShadingElement[] functions = document.CreatePage(0).Graphics
            .OfType<PdfFunctionShadingElement>()
            .ToArray();
        PdfGraphicsElement[] mixed = document.CreatePage(1).Graphics.ToArray();
        PdfMeshShadingElement[] triangles = document.CreatePage(2).Graphics
            .OfType<PdfMeshShadingElement>()
            .ToArray();
        PdfMeshShadingElement[] patches = document.CreatePage(3).Graphics
            .OfType<PdfMeshShadingElement>()
            .ToArray();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.PageCount, Is.EqualTo(5));
            Assert.That(functions, Has.Length.EqualTo(2));
            Assert.That(
                functions.Select(element => element.Shading.Kind),
                Has.All.EqualTo(PdfShadingKind.FunctionBased));
            Assert.That(mixed.OfType<PdfFunctionShadingElement>().Count(), Is.EqualTo(2));
            Assert.That(mixed.OfType<PdfShadingElement>().Count(), Is.EqualTo(2));
            Assert.That(
                triangles.Select(element => element.Shading.Kind),
                Is.EqualTo(new[]
                {
                    PdfShadingKind.FreeFormGouraud,
                    PdfShadingKind.LatticeGouraud
                }));
            Assert.That(
                triangles.Select(element => element.Shading.Triangles.Count),
                Is.EqualTo(new[] { 3, 2 }));
            Assert.That(
                patches.Select(element => element.Shading.Kind),
                Is.EqualTo(new[]
                {
                    PdfShadingKind.CoonsPatch,
                    PdfShadingKind.TensorProductPatch
                }));
            Assert.That(
                patches.Select(element => element.Shading.Patches.Count),
                Is.EqualTo(new[] { 2, 1 }));
            Assert.That(
                patches[1].Shading.Patches[0].GetControlPoint(1, 1),
                Is.Not.EqualTo(new PdfPoint(0, 0)));
        }));
    }

    [Test]
    public void AdjacentCoonsPatchesRetainOneSharedEdgeObject()
    {
        using Document document = Load();
        PdfMeshShadingBrush coons = document.CreatePage(3).Graphics
            .OfType<PdfMeshShadingElement>()
            .Single(element => element.Shading.Kind == PdfShadingKind.CoonsPatch)
            .Shading;

        PdfMeshPatch first = coons.Patches[0];
        PdfMeshPatch second = coons.Patches[1];

        Assert.That(
            second.GetEdge(PdfMeshPatchEdgeSide.Top),
            Is.SameAs(first.GetEdge(PdfMeshPatchEdgeSide.Right)));
    }

    [Test]
    public void CorpusPlacesMeshesInsideAGroupAndALuminosityMask()
    {
        using Document document = Load();
        Page page = document.CreatePage(4);
        PdfTransparencyGroupElement group = page.Graphics
            .OfType<PdfTransparencyGroupElement>()
            .Single();
        PdfPathElement masked = page.Graphics
            .OfType<PdfPathElement>()
            .Single(element => element.State.SoftMask is not null);

        PdfMeshShadingElement groupedMesh = group.Elements
            .OfType<PdfMeshShadingElement>()
            .Single();
        PdfMeshShadingElement maskMesh = masked.State.SoftMask!.Elements
            .OfType<PdfMeshShadingElement>()
            .Single();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(group.Isolated, Is.True);
            Assert.That(groupedMesh.Shading.Kind, Is.EqualTo(PdfShadingKind.CoonsPatch));
            Assert.That(masked.State.SoftMask.Mode, Is.EqualTo(PdfSoftMaskMode.Luminosity));
            Assert.That(maskMesh.Shading.Kind, Is.EqualTo(PdfShadingKind.LatticeGouraud));
        }));
    }

    [Test]
    public void RasterOutputsMatchTheFourDpiManifest()
    {
        using JsonDocument manifest = Manifest();
        using Document document = Load();
        Assert.That(
            manifest.RootElement.GetProperty("managed_png_hash_mode").GetString(),
            Is.EqualTo(CanonicalRenderingHash.PngMode));
        JsonElement configurations = manifest.RootElement
            .GetProperty("managed_png_sha256");
        var results = new List<(string Name, string[] Actual, string[] Expected)>();

        foreach (JsonProperty configuration in configurations.EnumerateObject())
        {
            double dpi = double.Parse(
                configuration.Name[3..configuration.Name.IndexOf('-', 3)],
                System.Globalization.CultureInfo.InvariantCulture);
            string[] expected = configuration.Value
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            Assert.That(expected, Has.Length.EqualTo(document.PageCount));
            var actual = new string[document.PageCount];
            for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                actual[pageIndex] = CanonicalRenderingHash.Png(
                    document.CreatePage(pageIndex).RenderToPng(
                    new RasterRenderOptions
                    {
                        Dpi = dpi,
                        Antialiasing = 4,
                        UseFontSubstitution = false
                    }));
            }
            results.Add((configuration.Name, actual, expected));
        }

        Assert.Multiple((Action)(() =>
        {
            foreach ((string name, string[] actual, string[] expected) in results)
                Assert.That(actual, Is.EqualTo(expected), name);
        }));
    }

    [Test]
    public void TransparentPatchOutputsAndSvgFallbacksMatchTheManifest()
    {
        using JsonDocument manifest = Manifest();
        using Document document = Load();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                manifest.RootElement.GetProperty("managed_png_hash_mode").GetString(),
                Is.EqualTo(CanonicalRenderingHash.PngMode));
            Assert.That(
                manifest.RootElement.GetProperty("managed_svg_hash_mode").GetString(),
                Is.EqualTo(CanonicalRenderingHash.SvgMode));
        }));
        Page transparentPage = document.CreatePage(3);
        var transparentResults = new List<(string Name, string Actual, string Expected)>();
        foreach (JsonProperty configuration in manifest.RootElement
                     .GetProperty("managed_transparent_page_sha256")
                     .EnumerateObject())
        {
            string dpiToken = configuration.Name.Split('-')[0];
            double dpi = double.Parse(
                dpiToken[3..],
                System.Globalization.CultureInfo.InvariantCulture);
            string actual = CanonicalRenderingHash.Png(transparentPage.RenderToPng(
                new RasterRenderOptions
                {
                    Dpi = dpi,
                    Antialiasing = 4,
                    Transparent = true,
                    UseFontSubstitution = false
                }));
            string expected = configuration.Value.GetString()!;
            transparentResults.Add((configuration.Name, actual, expected));
        }

        string[] expectedSvg = manifest.RootElement
            .GetProperty("managed_svg_sha256")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        var actualSvg = new string[document.PageCount];
        for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            string svg = document.CreatePage(pageIndex).RenderToSvg();
            Assert.That(svg, Does.Contain("data:image/png;base64,"));
            actualSvg[pageIndex] = CanonicalRenderingHash.Svg(svg);
        }

        Assert.Multiple((Action)(() =>
        {
            foreach ((string name, string actual, string expected) in transparentResults)
                Assert.That(actual, Is.EqualTo(expected), name);
            Assert.That(actualSvg, Is.EqualTo(expectedSvg));
        }));
    }

    [Test]
    public async Task SharedDocumentRasterAndSvgRenderingIsDeterministic()
    {
        using Document document = Load();
        Page page = document.CreatePage(3);
        var rasterOptions = new RasterRenderOptions
        {
            Dpi = 144,
            Antialiasing = 4,
            Transparent = true,
            UseFontSubstitution = false
        };
        string expected = Hash(page.RenderToPng(rasterOptions)) + "\n" +
                          Hash(Encoding.UTF8.GetBytes(page.RenderToSvg()));
        Task<string>[] renders = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
                Hash(page.RenderToPng(rasterOptions)) + "\n" +
                Hash(Encoding.UTF8.GetBytes(page.RenderToSvg()))))
            .ToArray();

        Assert.That(await Task.WhenAll(renders), Has.All.EqualTo(expected));
    }

    [Test]
    public void CorpusHashAndPageNamesMatchTheManifest()
    {
        using JsonDocument manifest = Manifest();
        JsonElement root = manifest.RootElement;
        string file = root.GetProperty("file").GetString()!;
        byte[] data = File.ReadAllBytes(Path.Combine(FixtureDirectory(), file));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(Hash(data), Is.EqualTo(root.GetProperty("sha256").GetString()));
            Assert.That(root.GetProperty("pages").GetArrayLength(), Is.EqualTo(5));
        }));
    }

    private static Document Load() =>
        Document.LoadFromFile(Path.Combine(FixtureDirectory(), "shading-alpha3.pdf"));

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            FixtureDirectory(),
            "shading-alpha3-fixture.json")));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
