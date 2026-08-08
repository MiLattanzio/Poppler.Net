using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class RasterGeometryAlpha1Tests
{
    private static readonly string[] RenderKeys =
    {
        "dpi96-aa1-opaque",
        "dpi96-aa1-transparent",
        "dpi96-aa4-opaque",
        "dpi96-aa4-transparent",
        "dpi300-aa1-opaque",
        "dpi300-aa1-transparent",
        "dpi300-aa4-opaque",
        "dpi300-aa4-transparent"
    };

    [Test]
    public void CorpusCoversStrokeTransformDashAndClipCases()
    {
        using Document document = Load();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.Pages, Is.EqualTo(8));
            Assert.That(document.CreatePage(0).Graphics
                .OfType<PdfPathElement>()
                .Select(path => path.State.LineCap)
                .Distinct(), Is.EquivalentTo(new[]
                {
                    PdfLineCap.Butt,
                    PdfLineCap.Round,
                    PdfLineCap.Square
                }));
            Assert.That(document.CreatePage(0).Graphics
                .OfType<PdfPathElement>()
                .Select(path => path.State.LineJoin)
                .Distinct(), Is.EquivalentTo(new[]
                {
                    PdfLineJoin.Miter,
                    PdfLineJoin.Round,
                    PdfLineJoin.Bevel
                }));
            Assert.That(document.CreatePage(1).Graphics
                .OfType<PdfPathElement>()
                .Any(path => path.State.LineWidth == 0), Is.True);
            Assert.That(document.CreatePage(2).Graphics
                .OfType<PdfPathElement>()
                .Any(path => path.State.Dash.Phase < 0), Is.True);
            Assert.That(document.CreatePage(2).Graphics
                .OfType<PdfPathElement>()
                .Any(path => path.State.Dash.Segments.Count == 3), Is.True);
            Assert.That(document.CreatePage(2).Graphics
                .OfType<PdfPathElement>()
                .Any(path => path.State.Dash.Segments.Contains(0)), Is.True);
            Assert.That(document.CreatePage(3).Graphics
                .OfType<PdfPathElement>()
                .Any(path => path.State.Transform.A < 0), Is.True);
            Assert.That(document.CreatePage(6).Graphics
                .Any(element => element.ClipPaths.Count >= 3), Is.True);
            Assert.That(document.CreatePage(7).Rotation, Is.EqualTo(90));
            Assert.That(document.CreatePage(7).Text(), Is.Empty);
        }));
    }

    [Test]
    public void FixtureAndRequiredRenderMatrixMatchManifest()
    {
        using JsonDocument manifest = Manifest();
        JsonElement root = manifest.RootElement;
        string fixture = Path.Combine(
            FixtureDirectory(),
            root.GetProperty("file").GetString()!);
        string fixtureHash = Hash(File.ReadAllBytes(fixture));
        JsonElement hashes = root.GetProperty("managed_png_sha256");
        bool overridesValid =
            !root.TryGetProperty(
                "managed_png_sha256_windows_overrides",
                out JsonElement windowsOverrides) ||
            windowsOverrides.EnumerateObject().All(item =>
                RenderKeys.Contains(item.Name, StringComparer.Ordinal) &&
                item.Value.GetArrayLength() == 8);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                fixtureHash,
                Is.EqualTo(root.GetProperty("sha256").GetString()));
            Assert.That(hashes.EnumerateObject().Select(item => item.Name),
                Is.EqualTo(RenderKeys));
            Assert.That(
                hashes.EnumerateObject().All(item =>
                    item.Value.GetArrayLength() == 8),
                Is.True);
            Assert.That(overridesValid, Is.True);
        }));
    }

    [Test]
    public void RepresentativeRequiredRenderMatrixHashesAreDeterministic()
    {
        using JsonDocument manifest = Manifest();
        JsonElement hashes = manifest.RootElement
            .GetProperty("managed_png_sha256");
        JsonElement windowsOverrides = default;
        bool hasWindowsOverrides = OperatingSystem.IsWindows() &&
            manifest.RootElement.TryGetProperty(
                "managed_png_sha256_windows_overrides",
                out windowsOverrides);
        using Document document = Load();

        for (int index = 0; index < RenderKeys.Length; index++)
        {
            string key = RenderKeys[index];
            RasterRenderOptions options = Options(key);
            JsonElement expectedHashes = hashes.GetProperty(key);
            if (hasWindowsOverrides &&
                windowsOverrides.TryGetProperty(key, out JsonElement overrides))
            {
                expectedHashes = overrides;
            }
            string expected = expectedHashes[index].GetString()!;
            string actual = Hash(
                document.CreatePage(index).RenderToPng(options));
            Assert.That(actual, Is.EqualTo(expected), key);
        }
    }

    [Test]
    public void GeometryBudgetIsCumulativeAndValidated()
    {
        Assert.That(
            (Action)(() => Document.LoadFromFile(
                FixturePath(),
                options: new PdfReadOptions
                {
                    MaximumRasterGeometrySegments = 0
                })),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        using Document limited = Load(new PdfReadOptions
        {
            // Every individual path on page 1 stays below this value; the
            // page fails only when all flatten/dash/outline work is summed.
            MaximumRasterGeometrySegments = 100
        });
        long before = GC.GetAllocatedBytesForCurrentThread();
        Exception? exception = Assert.Catch((Action)(() =>
            limited.CreatePage(0).Render(new RasterRenderOptions
            {
                Dpi = 96,
                Antialiasing = 1,
                UseFontSubstitution = false
            })));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(exception, Is.TypeOf<PdfLimitException>()
                .And.Message.Contains("Raster geometry"));
            Assert.That(allocated, Is.LessThan(2L * 1024 * 1024));
        }));
    }

    [Test]
    public async Task ConcurrentRenderingOfOneDocumentIsDeterministic()
    {
        using Document document = Load();
        Page page = document.CreatePage(4);
        var options = new RasterRenderOptions
        {
            Dpi = 72,
            Antialiasing = 1,
            Transparent = true,
            UseFontSubstitution = false
        };
        string expected = Hash(page.RenderToPng(options));
        Task<string>[] renders = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Hash(page.RenderToPng(options))))
            .ToArray();

        Assert.That(await Task.WhenAll(renders), Has.All.EqualTo(expected));
    }

    [Test]
    public void SingularTransformIsSkippedAndRotatedCropBoxIsBounded()
    {
        using Document document = Load();
        PdfBitmap bitmap = document.CreatePage(7).Render(new RasterRenderOptions
        {
            Dpi = 96,
            Antialiasing = 4,
            UseFontSubstitution = false
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(bitmap.Width, Is.EqualTo(187));
            Assert.That(bitmap.Height, Is.EqualTo(288));
            Assert.That(bitmap.Data.Length, Is.EqualTo(187 * 288 * 4));
        }));
    }

    private static RasterRenderOptions Options(string key)
    {
        string[] parts = key.Split('-');
        double dpi = double.Parse(parts[0][3..],
            System.Globalization.CultureInfo.InvariantCulture);
        int antialiasing = int.Parse(parts[1][2..],
            System.Globalization.CultureInfo.InvariantCulture);
        return new RasterRenderOptions
        {
            Dpi = dpi,
            Antialiasing = antialiasing,
            Transparent = parts[2] == "transparent",
            UseFontSubstitution = false
        };
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(FixturePath(), options: options);

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FixtureDirectory(),
            "raster-geometry-alpha1-fixture.json")));

    private static string FixturePath() => Path.Combine(
        FixtureDirectory(),
        "raster-geometry-alpha1.pdf");

    private static string FixtureDirectory() => Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "Fixtures");
}
