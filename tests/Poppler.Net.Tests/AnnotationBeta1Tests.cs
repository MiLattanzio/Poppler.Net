using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class AnnotationBeta1Tests
{
    [Test]
    public void ReadsAdvancedSubtypesReviewRelationshipsAndGeometry()
    {
        using Document document = Load();
        Page page = document.CreatePage(0);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(page.Annotations, Has.Count.EqualTo(9));
            Assert.That(
                page.Annotations.Select(annotation => annotation.Type),
                Is.EqualTo(new[]
                {
                    PdfAnnotationType.Text,
                    PdfAnnotationType.Popup,
                    PdfAnnotationType.Text,
                    PdfAnnotationType.Caret,
                    PdfAnnotationType.FileAttachment,
                    PdfAnnotationType.FreeText,
                    PdfAnnotationType.Line,
                    PdfAnnotationType.Redact,
                    PdfAnnotationType.Watermark
                }));
            Assert.That(page.Annotations[0].Id, Does.Match(@"^\d+:0$"));
            Assert.That(page.Annotations[0].PopupId, Is.EqualTo(page.Annotations[1].Id));
            Assert.That(page.Annotations[1].ParentId, Is.EqualTo(page.Annotations[0].Id));
            Assert.That(page.Annotations[2].ParentId, Is.EqualTo(page.Annotations[0].Id));
            Assert.That(page.Annotations[2].ReplyType, Is.EqualTo("R"));
            Assert.That(page.Annotations[2].State, Is.EqualTo("Completed"));
            Assert.That(page.Annotations[2].StateModel, Is.EqualTo("Marked"));
            Assert.That(page.Annotations[1].IsOpen, Is.True);
            Assert.That(page.Annotations[1].IsVisible, Is.True);
            Assert.That(page.Annotations[5].Intent, Is.EqualTo("FreeTextCallout"));
            Assert.That(page.Annotations[5].CalloutLine, Has.Count.EqualTo(3));
            Assert.That(page.Annotations[5].RichText, Does.Contain("Rich text"));
            Assert.That(page.Annotations[5].DefaultStyle, Does.Contain("10pt"));
            Assert.That(page.Annotations[6].LineEndingStyles,
                Is.EqualTo(new[] { "ClosedArrow", "Circle" }));
            Assert.That(page.Annotations[7].RectangleDifferences,
                Is.EqualTo(new[] { 3d, 3d, 3d, 3d }));
        }));
    }

    [Test]
    public void ReadsFileAttachmentDataLazily()
    {
        using Document document = Load();
        EmbeddedFile attachment =
            document.CreatePage(0).Annotations[4].Attachment!;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(attachment, Is.Not.Null);
            Assert.That(attachment.Name, Is.EqualTo("beta1-note.txt"));
            Assert.That(attachment.Description,
                Is.EqualTo("Advanced annotation attachment"));
            Assert.That(attachment.MimeType, Is.EqualTo("text/plain"));
            Assert.That(attachment.DeclaredSize, Is.EqualTo(30));
            Assert.That(
                Encoding.ASCII.GetString(attachment.Data.Span),
                Is.EqualTo("Poppler.Net beta 1 attachment\n"));
        }));
    }

    [Test]
    public void DecodesAdvancedActionsWithoutExecutingThem()
    {
        using Document document = Load();
        IReadOnlyList<PdfAnnotation> annotations =
            document.CreatePage(1).Annotations;

        PdfAnnotationAction remote = annotations[0].Action;
        PdfAnnotationAction script = remote.NextActions.Single();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(remote.Type, Is.EqualTo(PdfAnnotationActionType.GoToRemote));
            Assert.That(remote.FileName, Is.EqualTo("remote.pdf"));
            Assert.That(remote.NamedTarget, Is.EqualTo("chapter-2"));
            Assert.That(remote.NewWindow, Is.True);
            Assert.That(script.Type, Is.EqualTo(PdfAnnotationActionType.JavaScript));
            Assert.That(script.Script, Does.Contain("inspection only"));
            Assert.That(script.NextActions.Single().Type,
                Is.EqualTo(PdfAnnotationActionType.None));
            Assert.That(annotations[1].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.Launch));
            Assert.That(annotations[1].Action.FileName, Is.EqualTo("manual.pdf"));
            Assert.That(annotations[1].Action.NewWindow, Is.False);
            Assert.That(annotations[2].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.SubmitForm));
            Assert.That(annotations[2].Action.Fields,
                Is.EqualTo(new[] { "person.name", "person.email" }));
            Assert.That(annotations[2].Action.Flags, Is.EqualTo(4));
            Assert.That(annotations[3].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.ResetForm));
            Assert.That(annotations[4].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.ImportData));
            Assert.That(annotations[5].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.Hide));
            Assert.That(annotations[5].Action.IsHidden, Is.True);
            Assert.That(annotations[6].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.SetOptionalContentState));
            Assert.That(annotations[6].Action.StateChanges,
                Is.EqualTo(new[]
                {
                    "ON", "20:0", "OFF", "21:0", "Toggle", "22:0"
                }));
            Assert.That(annotations[7].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.Rendition));
            Assert.That(annotations[8].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.Transition));
            Assert.That(annotations[9].Action.Type,
                Is.EqualTo(PdfAnnotationActionType.GoToThreeDView));
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("annotation.action.circular"));
        }));
    }

    [Test]
    public void RecognizesMultimediaAndProductionSubtypes()
    {
        using Document document = Load();

        Assert.That(
            document.CreatePage(2).Annotations.Select(annotation => annotation.Type),
            Is.EqualTo(new[]
            {
                PdfAnnotationType.Sound,
                PdfAnnotationType.Movie,
                PdfAnnotationType.Screen,
                PdfAnnotationType.ThreeD,
                PdfAnnotationType.PrinterMark,
                PdfAnnotationType.TrapNet
            }));
    }

    [Test]
    public void RendersAdvancedFallbacksDeterministically()
    {
        using JsonDocument manifest = Manifest();
        string[] expected = manifest.RootElement
            .GetProperty("managed_png_sha256")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        using Document document = Load();

        string[] actual = Enumerable.Range(0, document.Pages)
            .Select(index => RenderHash(document.CreatePage(index)))
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task ConcurrentAdvancedAnnotationReadsAreDeterministic()
    {
        using Document document = Load();
        string expected = Summary(document);
        Task<string>[] operations = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => Summary(document)))
            .ToArray();

        string[] actual = await Task.WhenAll(operations);

        Assert.That(actual, Has.All.EqualTo(expected));
    }

    [Test]
    public void EnforcesActionLimits()
    {
        using Document countLimited = Load(new PdfReadOptions
        {
            MaximumActions = 1
        });
        using Document depthLimited = Load(new PdfReadOptions
        {
            MaximumActionDepth = 2
        });
        using Document scriptLimited = Load(new PdfReadOptions
        {
            MaximumActionScriptBytes = 4
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                (Action)(() => _ = countLimited.CreatePage(1).Annotations),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => _ = depthLimited.CreatePage(1).Annotations),
                Throws.TypeOf<PdfLimitException>());
            Assert.That(
                (Action)(() => _ = scriptLimited.CreatePage(1).Annotations),
                Throws.TypeOf<PdfLimitException>());
        }));
    }

    [Test]
    public void ValidatesActionOptions()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);

        Assert.Multiple((Action)(() =>
        {
            AssertInvalid(source, new PdfReadOptions { MaximumActions = 0 });
            AssertInvalid(source, new PdfReadOptions { MaximumActionDepth = 0 });
            AssertInvalid(source, new PdfReadOptions { MaximumActionScriptBytes = 0 });
        }));
    }

    [Test]
    public void AdvancedAnnotationFixtureHashMatchesManifest()
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

    private static string Summary(Document document)
    {
        string metadata = string.Join(
            "|",
            document.CreatePage(1).Annotations.Select(annotation =>
                $"{annotation.Type}:{annotation.Action.Type}:" +
                $"{annotation.Action.NextActions.Count}"));
        return $"{metadata}|{RenderHash(document.CreatePage(0))}";
    }

    private static string RenderHash(Page page) =>
        Convert.ToHexString(SHA256.HashData(page.RenderToPng(RenderOptions())))
            .ToLowerInvariant();

    private static RasterRenderOptions RenderOptions() => new()
    {
        Dpi = 72,
        Antialiasing = 2,
        UseFontSubstitution = false
    };

    private static void AssertInvalid(byte[] source, PdfReadOptions options) =>
        Assert.That(
            (Action)(() => Document.LoadFromData(source, options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "annotations-beta1.pdf"),
            options: options);

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(
                    FixtureDirectory(),
                    "annotations-beta1-fixture.json")));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
