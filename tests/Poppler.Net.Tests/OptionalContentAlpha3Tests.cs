using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class OptionalContentAlpha3Tests
{
    private static readonly IReadOnlyDictionary<string, bool> Inverted =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["7:0"] = false,
            ["8:0"] = true
        };

    [Test]
    public void ReadsDefaultConfigurationAndImmutableGroupMetadata()
    {
        using Document document = Load();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.PageMode, Is.EqualTo(PageMode.UseOptionalContent));
            Assert.That(document.HasOptionalContent, Is.True);
            Assert.That(document.OptionalContentGroups, Has.Count.EqualTo(3));
            Assert.That(
                document.OptionalContentGroups.Select(group => group.Id),
                Is.EqualTo(new[] { "7:0", "8:0", "9:0" }));
            Assert.That(
                document.OptionalContentGroups.Select(group => group.Name),
                Is.EqualTo(new[]
                {
                    "Red plans",
                    "Blue notes",
                    "Locked green"
                }));
            Assert.That(
                document.OptionalContentGroups.Select(group => group.IsVisible),
                Is.EqualTo(new[] { true, false, true }));
            Assert.That(
                document.OptionalContentGroups.Select(group => group.IsLocked),
                Is.EqualTo(new[] { false, false, true }));
            Assert.That(
                document.OptionalContentGroups[0].Intents,
                Is.EqualTo(new[] { "View", "Design" }));
            Assert.That(document.OptionalContentGroups[0].ViewState, Is.True);
            Assert.That(document.OptionalContentGroups[1].ViewState, Is.False);
        }));

        PdfOptionalContentConfiguration configuration =
            document.DefaultOptionalContentConfiguration!;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(configuration.Name, Is.EqualTo("Default View"));
            Assert.That(configuration.Creator, Is.EqualTo("Poppler.Net alpha.3"));
            Assert.That(
                configuration.BaseState,
                Is.EqualTo(PdfOptionalContentBaseState.On));
            Assert.That(configuration.Intents, Is.EqualTo(new[] { "View" }));
            Assert.That(
                configuration.RadioButtonGroups.Single(),
                Is.EqualTo(new[] { "7:0", "8:0" }));
        }));
    }

    [Test]
    public void DocumentsWithoutOptionalContentExposeAnEmptyModel()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.HasOptionalContent, Is.False);
            Assert.That(document.OptionalContentGroups, Is.Empty);
            Assert.That(document.DefaultOptionalContentConfiguration, Is.Null);
        }));
    }

    [Test]
    public void AppliesMarkedContentToGraphicsAndTextExtraction()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);
        string text = page.Text(layout: TextLayout.RawOrder);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.Graphics, Has.Count.EqualTo(9));
            Assert.That(text, Does.Contain("OPTIONAL CONTENT DEFAULT"));
            Assert.That(text, Does.Contain("VISIBLE RED TEXT"));
            Assert.That(text, Does.Not.Contain("HIDDEN BLUE TEXT"));
        }));
    }

    [Test]
    public void AppliesOptionalContentToFormsImagesAndNestedXObjects()
    {
        using Document document = Load();
        Page page = document.CreatePage(1);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.Graphics, Has.Count.EqualTo(5));
            Assert.That(page.Images, Has.Count.EqualTo(1));
            Assert.That(page.Images[0].ResourceName, Is.EqualTo("RedImage"));
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.Contains(
                        "BlueForm",
                        StringComparison.Ordinal) == true),
                Is.False);
        }));
    }

    [Test]
    public void ExposesHiddenAnnotationsAndWidgetsWithoutPaintingThem()
    {
        using Document document = Load();
        Page page = document.CreatePage(2);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.Annotations, Has.Count.EqualTo(4));
            Assert.That(
                page.Annotations.Select(annotation => annotation.IsVisible),
                Is.EqualTo(new[] { true, false, true, false }));
            Assert.That(page.FormWidgets, Has.Count.EqualTo(1));
            Assert.That(page.FormWidgets[0].FieldName, Is.EqualTo("layered-field"));
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.StartsWith(
                        "Annotation[2]",
                        StringComparison.Ordinal) == true),
                Is.False);
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.StartsWith(
                        "Annotation[4]",
                        StringComparison.Ordinal) == true),
                Is.False);
        }));
    }

    [Test]
    public void RasterAndSvgRenderingAcceptPerGroupOverrides()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);
        byte[] defaultPng = page.RenderToPng(RenderOptions());
        byte[] invertedPng = page.RenderToPng(RenderOptions(Inverted));
        string defaultSvg = page.RenderToSvg(new SvgRenderOptions());
        string invertedSvg = page.RenderToSvg(new SvgRenderOptions
        {
            OptionalContentVisibility = Inverted
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(invertedPng, Is.Not.EqualTo(defaultPng));
            Assert.That(invertedSvg, Is.Not.EqualTo(defaultSvg));
            Assert.That(defaultSvg, Does.Contain("rgb(255 51 51)"));
            Assert.That(defaultSvg, Does.Not.Contain("rgb(51 76 255)"));
            Assert.That(invertedSvg, Does.Contain("rgb(51 76 255)"));
            Assert.That(invertedSvg, Does.Not.Contain("rgb(255 51 51)"));
        }));
    }

    [Test]
    public void OcmdPoliciesAndVisibilityExpressionsMatchRasterManifest()
    {
        using JsonDocument manifest = Manifest();
        string[] expectedDefault = ReadHashes(manifest, "managed_png_sha256");
        string[] expectedInverted = ReadHashes(manifest, "inverted_png_sha256");
        using Document document = Load();

        string[] actualDefault = RenderHashes(document, RenderOptions());
        string[] actualInverted = RenderHashes(
            document,
            RenderOptions(Inverted));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(actualDefault, Is.EqualTo(expectedDefault));
            Assert.That(actualInverted, Is.EqualTo(expectedInverted));
        }));
    }

    [Test]
    public async Task ConcurrentDefaultAndOverriddenRenderingIsDeterministic()
    {
        using Document document = Load();
        string defaultHash = RenderHash(document.CreatePage(3), RenderOptions());
        string invertedHash = RenderHash(
            document.CreatePage(3),
            RenderOptions(Inverted));
        Task<string>[] operations = Enumerable.Range(0, 24)
            .Select(index => Task.Run(() =>
                RenderHash(
                    document.CreatePage(3),
                    index % 2 == 0
                        ? RenderOptions()
                        : RenderOptions(Inverted))))
            .ToArray();

        string[] actual = await Task.WhenAll(operations);

        Assert.That(
            actual,
            Is.EqualTo(Enumerable.Range(0, 24)
                .Select(index => index % 2 == 0 ? defaultHash : invertedHash)));
    }

    [Test]
    public void EnforcesOptionalContentResourceLimits()
    {
        using Document groupLimited = Load(new PdfReadOptions
        {
            MaximumOptionalContentGroups = 2
        });
        using Document depthLimited = Load(new PdfReadOptions
        {
            MaximumOptionalContentDepth = 1
        });
        using Document expressionLimited = Load(new PdfReadOptions
        {
            MaximumOptionalContentExpressionNodes = 1
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => _ = groupLimited.OptionalContentGroups),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => _ = depthLimited.CreatePage(0).Graphics),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => _ = expressionLimited.CreatePage(0).Graphics),
                Throws.TypeOf<PdfLimitException>());
        }));
    }

    [Test]
    public void ValidatesOptionalContentOptions()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);

        Assert.Multiple((Action)(() =>
        {
            AssertInvalid(source, new PdfReadOptions
            {
                MaximumOptionalContentGroups = 0
            });
            AssertInvalid(source, new PdfReadOptions
            {
                MaximumOptionalContentDepth = 0
            });
            AssertInvalid(source, new PdfReadOptions
            {
                MaximumOptionalContentExpressionNodes = 0
            });
        }));
    }

    [Test]
    public void RejectsUnknownRenderOverrideIdentifiers()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);
        var unknown = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["missing:0"] = true
        };

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => page.Render(new RasterRenderOptions
                {
                    OptionalContentVisibility = unknown
                })),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                (Action)(() => page.RenderToSvg(new SvgRenderOptions
                {
                    OptionalContentVisibility = unknown
                })),
                Throws.TypeOf<ArgumentException>());
        }));
    }

    [Test]
    public void OptionalContentFixtureHashMatchesManifest()
    {
        using JsonDocument manifest = Manifest();
        string file = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(FixtureDirectory(), file))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static string[] RenderHashes(
        Document document,
        RasterRenderOptions options) =>
        Enumerable.Range(0, document.Pages)
            .Select(index => RenderHash(document.CreatePage(index), options))
            .ToArray();

    private static string RenderHash(
        Page page,
        RasterRenderOptions options) =>
        Convert.ToHexString(SHA256.HashData(page.RenderToPng(options)))
            .ToLowerInvariant();

    private static string[] ReadHashes(JsonDocument manifest, string property) =>
        manifest.RootElement
            .GetProperty(property)
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

    private static RasterRenderOptions RenderOptions(
        IReadOnlyDictionary<string, bool>? overrides = null) =>
        new()
        {
            Dpi = 72,
            Antialiasing = 2,
            UseFontSubstitution = false,
            OptionalContentVisibility =
                overrides ??
                new Dictionary<string, bool>(StringComparer.Ordinal)
        };

    private static void AssertInvalid(byte[] source, PdfReadOptions options) =>
        Assert.That(
            (Action)(() => Document.LoadFromData(source, options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(
                FixtureDirectory(),
                "optional-content-alpha3.pdf"),
            options: options);

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(
                    FixtureDirectory(),
                    "optional-content-alpha3-fixture.json")));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
