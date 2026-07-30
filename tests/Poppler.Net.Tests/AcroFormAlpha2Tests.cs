using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class AcroFormAlpha2Tests
{
    [Test]
    public void ReadsHierarchicalFieldsAndInheritedValues()
    {
        using Document document = Load();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.FormType, Is.EqualTo(FormType.AcroForm));
            Assert.That(document.FormNeedsAppearances, Is.True);
            Assert.That(document.HasFormFields, Is.True);
            Assert.That(document.FormFields, Has.Count.EqualTo(13));
            Assert.That(
                document.FormFields.Select(field => field.FullyQualifiedName),
                Is.EqualTo(new[]
                {
                    "person.name",
                    "person.biography",
                    "person.password",
                    "person.code",
                    "accept",
                    "colour",
                    "fallback-check",
                    "submit",
                    "country",
                    "interests",
                    "custom",
                    "approval",
                    "settings"
                }));
        }));

        PdfFormField name = Field(document, "person.name");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(name.Type, Is.EqualTo(PdfFormFieldType.Text));
            Assert.That(name.PartialName, Is.EqualTo("name"));
            Assert.That(name.AlternateName, Is.EqualTo("Full name"));
            Assert.That(name.MappingName, Is.EqualTo("person_name"));
            Assert.That(name.Value, Is.EqualTo("Mi Lattanzio"));
            Assert.That(name.DefaultValue, Is.EqualTo("Default Name"));
            Assert.That(name.DefaultAppearance, Does.Contain("11 Tf"));
            Assert.That(name.Alignment, Is.EqualTo(PdfTextAlignment.Left));
        }));

        PdfFormField settings = Field(document, "settings");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                settings.Flags & PdfFormFieldFlags.ReadOnly,
                Is.EqualTo(PdfFormFieldFlags.ReadOnly));
            Assert.That(settings.Value, Is.EqualTo("INHERITED VALUE"));
            Assert.That(settings.Widgets, Has.Count.EqualTo(1));
            Assert.That(settings.Widgets[0].PageIndex, Is.EqualTo(3));
            Assert.That(settings.Widgets[0].FieldName, Is.EqualTo("settings"));
        }));
    }

    [Test]
    public void ReadsTextFlagsAndPageWidgets()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);

        Assert.That(page.FormWidgets, Has.Count.EqualTo(4));
        Assert.That(
            page.Annotations.Select(annotation => annotation.Type),
            Has.All.EqualTo(PdfAnnotationType.Widget));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                Field(document, "person.biography").Flags &
                PdfFormFieldFlags.Multiline,
                Is.EqualTo(PdfFormFieldFlags.Multiline));
            Assert.That(
                Field(document, "person.password").Flags &
                PdfFormFieldFlags.Password,
                Is.EqualTo(PdfFormFieldFlags.Password));
            Assert.That(
                Field(document, "person.code").Flags &
                PdfFormFieldFlags.Comb,
                Is.EqualTo(PdfFormFieldFlags.Comb));
            Assert.That(Field(document, "person.code").MaximumLength, Is.EqualTo(4));
            Assert.That(page.FormWidgets[0].HasAppearance, Is.True);
            Assert.That(page.FormWidgets.Skip(1), Has.All.Property(
                nameof(PdfFormWidget.HasAppearance)).False);
        }));
    }

    [Test]
    public void UsesCanonicalButtonValueForAppearanceState()
    {
        using Document document = Load();
        PdfFormField accept = Field(document, "accept");
        PdfFormField colour = Field(document, "colour");
        PdfFormField fallback = Field(document, "fallback-check");
        PdfFormField push = Field(document, "submit");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(accept.ButtonType, Is.EqualTo(PdfButtonType.CheckBox));
            Assert.That(accept.Widgets[0].AppearanceState, Is.EqualTo("Yes"));
            Assert.That(accept.Widgets[0].OnState, Is.EqualTo("Yes"));
            Assert.That(colour.ButtonType, Is.EqualTo(PdfButtonType.RadioButton));
            Assert.That(
                colour.Widgets.Select(widget => widget.AppearanceState),
                Is.EqualTo(new[] { "Off", "Blue" }));
            Assert.That(fallback.Widgets[0].HasAppearance, Is.False);
            Assert.That(fallback.Widgets[0].OnState, Is.EqualTo("On"));
            Assert.That(push.ButtonType, Is.EqualTo(PdfButtonType.PushButton));
            Assert.That(push.Widgets[0].Caption, Is.EqualTo("SUBMIT"));
        }));
    }

    [Test]
    public void ReadsChoiceOptionsWithIndexPrecedence()
    {
        using Document document = Load();
        PdfFormField country = Field(document, "country");
        PdfFormField interests = Field(document, "interests");
        PdfFormField custom = Field(document, "custom");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                country.Flags & PdfFormFieldFlags.Combo,
                Is.EqualTo(PdfFormFieldFlags.Combo));
            Assert.That(
                country.Options.Select(option => option.ExportValue),
                Is.EqualTo(new[] { "it", "fr", "de" }));
            Assert.That(
                country.Options.Single(option => option.IsSelected).DisplayValue,
                Is.EqualTo("Italy"));
            Assert.That(interests.TopIndex, Is.EqualTo(1));
            Assert.That(
                interests.Options
                    .Where(option => option.IsSelected)
                    .Select(option => option.DisplayValue),
                Is.EqualTo(new[] { "PDF", "Security" }));
            Assert.That(
                interests.Value,
                Is.EqualTo("Code"),
                "/I must control selected options without rewriting the canonical /V.");
            Assert.That(
                custom.Flags & PdfFormFieldFlags.Edit,
                Is.EqualTo(PdfFormFieldFlags.Edit));
            Assert.That(custom.Value, Is.EqualTo("Custom value"));
            Assert.That(custom.Alignment, Is.EqualTo(PdfTextAlignment.Right));
        }));
    }

    [Test]
    public void ExposesSignaturePresenceWithoutValidation()
    {
        using Document document = Load();
        PdfFormField signature = Field(document, "approval");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(signature.Type, Is.EqualTo(PdfFormFieldType.Signature));
            Assert.That(signature.HasValue, Is.True);
            Assert.That(signature.IsSigned, Is.True);
            Assert.That(signature.Values, Is.Empty);
            Assert.That(signature.Widgets[0].Caption, Is.EqualTo("APPROVED"));
        }));
    }

    [Test]
    public void PaintsExplicitAndGeneratedWidgetAppearances()
    {
        using Document document = Load();
        Page page = document.CreatePage(1);
        _ = page.Graphics;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.StartsWith(
                        "Annotation[1]/Widget",
                        StringComparison.Ordinal) == true),
                Is.True);
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.StartsWith(
                        "Annotation[4]/Widget",
                        StringComparison.Ordinal) == true),
                Is.True);
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.StartsWith(
                        "Annotation[5]/Widget",
                        StringComparison.Ordinal) == true),
                Is.True);
        }));

        PdfBitmap bitmap = Render(page);
        AssertPixel(bitmap, 40, 40, 0, 115, 0, tolerance: 5);
        AssertPixel(bitmap, 87, 90, 0, 51, 230, tolerance: 5);
        AssertPixel(bitmap, 40, 150, 0, 0, 0, tolerance: 70);
    }

    [Test]
    public void KeepsOrphanWidgetsOutsideTheCanonicalFieldModel()
    {
        using Document document = Load();
        Page page = document.CreatePage(3);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.FormWidgets, Has.Count.EqualTo(1));
            Assert.That(page.Annotations, Has.Count.EqualTo(2));
            Assert.That(page.Annotations[1].Type, Is.EqualTo(PdfAnnotationType.Widget));
            Assert.That(page.Annotations[1].HasAppearance, Is.True);
            Assert.That(
                page.Graphics.Any(element =>
                    element.SourceResource?.StartsWith(
                        "Annotation[2]/Widget",
                        StringComparison.Ordinal) == true),
                Is.True);
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("form.field.circular"));
        }));
    }

    [Test]
    public void ManagedAcroFormRendersMatchManifest()
    {
        using JsonDocument manifest = Manifest();
        string[] expected = manifest.RootElement
            .GetProperty("managed_png_sha256")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        using Document document = Load();

        string[] actual = Enumerable.Range(0, document.Pages)
            .Select(index => Convert.ToHexString(
                    SHA256.HashData(
                        document.CreatePage(index).RenderToPng(RenderOptions())))
                .ToLowerInvariant())
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task ConcurrentFieldReadsAndRenderingAreDeterministic()
    {
        using Document document = Load();
        string expected = Summary(document);
        Task<string>[] operations = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => Summary(document)))
            .ToArray();

        string[] actual = await Task.WhenAll(operations);

        Assert.That(actual, Has.All.EqualTo(expected));
    }

    [Test]
    public void EnforcesAcroFormResourceLimits()
    {
        Assert.Multiple((Action)(() =>
        {
            AssertFormLimit(new PdfReadOptions { MaximumFormFields = 1 });
            AssertFormLimit(new PdfReadOptions { MaximumFormWidgets = 1 });
            AssertFormLimit(new PdfReadOptions { MaximumFormOptions = 2 });
            AssertFormLimit(new PdfReadOptions { MaximumFormFieldDepth = 1 });
            AssertFormLimit(new PdfReadOptions
            {
                MaximumFormDefaultAppearanceBytes = 1
            });
        }));
    }

    [Test]
    public void ValidatesAcroFormOptions()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        Assert.Multiple((Action)(() =>
        {
            AssertInvalid(source, new PdfReadOptions { MaximumFormFields = 0 });
            AssertInvalid(source, new PdfReadOptions { MaximumFormWidgets = 0 });
            AssertInvalid(source, new PdfReadOptions { MaximumFormOptions = 0 });
            AssertInvalid(source, new PdfReadOptions { MaximumFormFieldDepth = 0 });
            AssertInvalid(source, new PdfReadOptions
            {
                MaximumFormDefaultAppearanceBytes = 0
            });
        }));
    }

    [Test]
    public void AcroFormFixtureHashMatchesManifest()
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

    [Test]
    public void PreservesNeedAppearancesWhenTheFieldArrayIsAbsent()
    {
        byte[] source = File.ReadAllBytes(
            Path.Combine(FixtureDirectory(), "acroform-alpha2.pdf"));
        ReplaceAscii(source, "/Fields [", "/NoFlds [");

        using Document document = Document.LoadFromData(source);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.FormType, Is.EqualTo(FormType.AcroForm));
            Assert.That(document.FormNeedsAppearances, Is.True);
            Assert.That(document.FormFields, Is.Empty);
            Assert.That(document.HasFormFields, Is.False);
        }));
    }

    private static string Summary(Document document)
    {
        string fields = string.Join(
            "|",
            document.FormFields.Select(field =>
                $"{field.FullyQualifiedName}:{field.Type}:{field.Value}:" +
                $"{field.Widgets.Count}"));
        byte[] png = document.CreatePage(2).RenderToPng(RenderOptions());
        return $"{fields}|{Convert.ToHexString(SHA256.HashData(png))}";
    }

    private static void AssertFormLimit(PdfReadOptions options)
    {
        using Document document = Load(options);
        Assert.That(
            (Action)(() => _ = document.FormFields),
            Throws.TypeOf<PdfLimitException>());
    }

    private static void AssertInvalid(byte[] source, PdfReadOptions options) =>
        Assert.That(
            (Action)(() => Document.LoadFromData(source, options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());

    private static PdfFormField Field(Document document, string name) =>
        document.FormFields.Single(field =>
            string.Equals(
                field.FullyQualifiedName,
                name,
                StringComparison.Ordinal));

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "acroform-alpha2.pdf"),
            options: options);

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(
                    FixtureDirectory(),
                    "acroform-alpha2-fixture.json")));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static void ReplaceAscii(
        byte[] source,
        string oldValue,
        string newValue)
    {
        byte[] oldBytes = Encoding.ASCII.GetBytes(oldValue);
        byte[] newBytes = Encoding.ASCII.GetBytes(newValue);
        Assert.That(newBytes, Has.Length.EqualTo(oldBytes.Length));

        int offset = source.AsSpan().IndexOf(oldBytes);
        Assert.That(offset, Is.GreaterThanOrEqualTo(0));
        newBytes.CopyTo(source, offset);
    }

    private static RasterRenderOptions RenderOptions() => new()
    {
        Dpi = 72,
        Antialiasing = 2,
        UseFontSubstitution = false
    };

    private static PdfBitmap Render(Page page) => page.Render(RenderOptions());

    private static void AssertPixel(
        PdfBitmap bitmap,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        int tolerance)
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
            Assert.That(actualAlpha, Is.EqualTo(255));
        }));
    }
}
