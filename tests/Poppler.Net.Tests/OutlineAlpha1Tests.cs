using System.Security.Cryptography;
using System.Text.Json;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class OutlineAlpha1Tests
{
    [Test]
    public void ReadsOrderedImmutableOutlineTreeAndStyles()
    {
        using Document document = Load();

        Assert.That(
            document.OutlineItems.Select(item => item.Title),
            Is.EqualTo(new[] { "Chapter One", "Chapter Two", "Appendix" }));
        PdfOutlineItem chapter = document.OutlineItems[0];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(chapter.IsOpen, Is.True);
            Assert.That(chapter.IsBold, Is.True);
            Assert.That(chapter.IsItalic, Is.True);
            Assert.That(chapter.Color, Is.EqualTo(PdfColor.Rgb(0.9, 0.1, 0.1)));
            Assert.That(chapter.Children, Has.Count.EqualTo(2));
            Assert.That(
                chapter.Children.Select(item => item.Title),
                Is.EqualTo(new[]
                {
                    "Section 1.1 - Overview",
                    "External reference"
                }));
            Assert.That(document.OutlineItems[1].IsOpen, Is.False);
            Assert.That(document.OutlineItems[1].Children, Has.Count.EqualTo(1));
            Assert.That(
                document.OutlineItems[1].Children[0].Children.Single().Title,
                Is.EqualTo("Deep target"));
            Assert.That(document.OutlineItems[2].Children, Is.Empty);
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Has.None.EqualTo("outline.parent.mismatch"));
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Has.None.EqualTo("outline.prev.mismatch"));
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Has.None.EqualTo("outline.last.mismatch"));
        }));
    }

    [Test]
    public void DocumentWithoutOutlineReturnsAnEmptySnapshot()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));

        Assert.That(document.OutlineItems, Is.Empty);
    }

    [Test]
    public void ResolvesDirectAndNamedOutlineDestinations()
    {
        using Document document = Load();
        PdfOutlineItem chapterOne = document.OutlineItems[0];
        PdfOutlineItem sectionOne = chapterOne.Children[0];
        PdfOutlineItem chapterTwo = document.OutlineItems[1];
        PdfOutlineItem appendix = document.OutlineItems[2];

        Assert.Multiple((Action)(() =>
        {
            Assert.That(chapterOne.Destination!.PageIndex, Is.Zero);
            Assert.That(chapterOne.Destination.Type, Is.EqualTo(PdfDestinationType.Xyz));
            Assert.That(chapterOne.Destination.Zoom, Is.EqualTo(1.25));
            Assert.That(sectionOne.Destination!.PageIndex, Is.EqualTo(1));
            Assert.That(sectionOne.Destination.NamedDestination, Is.EqualTo("chapter-two"));
            Assert.That(sectionOne.Action.Type, Is.EqualTo(PdfAnnotationActionType.GoTo));
            Assert.That(chapterTwo.Action.Type, Is.EqualTo(PdfAnnotationActionType.GoTo));
            Assert.That(chapterTwo.Destination!.PageIndex, Is.EqualTo(1));
            Assert.That(appendix.Destination!.PageIndex, Is.EqualTo(2));
            Assert.That(appendix.Destination.NamedDestination, Is.EqualTo("appendix"));
        }));
    }

    [Test]
    public void ExposesActionsWithoutExecutingThemAndTruncatesCycles()
    {
        using Document document = Load();
        PdfOutlineItem uri = document.OutlineItems[0].Children[1];
        PdfOutlineItem script = document.OutlineItems[1].Children[0];

        Assert.Multiple((Action)(() =>
        {
            Assert.That(uri.Action.Type, Is.EqualTo(PdfAnnotationActionType.Uri));
            Assert.That(
                uri.Action.Uri,
                Is.EqualTo("https://example.test/outline-alpha1"));
            Assert.That(script.Action.Type, Is.EqualTo(PdfAnnotationActionType.JavaScript));
            Assert.That(script.Action.Script, Is.EqualTo("app.alert(\"inspection only\");"));
            Assert.That(script.Action.NextActions, Has.Count.EqualTo(1));
            Assert.That(
                script.Action.NextActions[0].Type,
                Is.EqualTo(PdfAnnotationActionType.None));
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("outline.action.circular"));
            Assert.That(
                document.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("outline.node.repeated"));
        }));
    }

    [Test]
    public async Task ConcurrentOutlineReadsAreDeterministic()
    {
        using Document document = Load();
        string expected = Summary(document.OutlineItems);

        Task<string>[] operations = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => Summary(document.OutlineItems)))
            .ToArray();
        string[] results = await Task.WhenAll(operations);

        Assert.That(results, Has.All.EqualTo(expected));
        Assert.That(
            document.Diagnostics.Select(diagnostic => diagnostic.Code),
            Is.EqualTo(new[]
            {
                "outline.node.repeated",
                "outline.action.circular"
            }));
    }

    [TestCase(6, 128, 65_536)]
    [TestCase(100_000, 2, 65_536)]
    [TestCase(100_000, 128, 10)]
    public void EnforcesOutlineLimits(
        int maximumItems,
        int maximumDepth,
        int maximumTitleBytes)
    {
        using Document document = Load(new PdfReadOptions
        {
            MaximumOutlineItems = maximumItems,
            MaximumOutlineDepth = maximumDepth,
            MaximumOutlineTitleBytes = maximumTitleBytes
        });

        Assert.That(
            (Action)(() => _ = document.OutlineItems),
            Throws.TypeOf<PdfLimitException>());
    }

    [TestCase(nameof(PdfReadOptions.MaximumOutlineItems))]
    [TestCase(nameof(PdfReadOptions.MaximumOutlineDepth))]
    [TestCase(nameof(PdfReadOptions.MaximumOutlineTitleBytes))]
    public void ValidatesOutlineLimits(string option)
    {
        PdfReadOptions options = option switch
        {
            nameof(PdfReadOptions.MaximumOutlineItems) =>
                new PdfReadOptions { MaximumOutlineItems = 0 },
            nameof(PdfReadOptions.MaximumOutlineDepth) =>
                new PdfReadOptions { MaximumOutlineDepth = 0 },
            _ => new PdfReadOptions { MaximumOutlineTitleBytes = 0 }
        };

        Assert.That(
            (Action)(() => Document.LoadFromData(
                PdfFixtures.Create(compressContent: false),
                options: options)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void OutlineFixtureHashMatchesManifest()
    {
        string directory = FixtureDirectory();
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(directory, "outline-alpha1-fixture.json")));
        string file = manifest.RootElement.GetProperty("file").GetString()!;
        string expected = manifest.RootElement.GetProperty("sha256").GetString()!;
        string actual = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, file))))
            .ToLowerInvariant();

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static string Summary(IReadOnlyList<PdfOutlineItem> roots)
    {
        var result = new List<string>();
        var stack = new Stack<(PdfOutlineItem Item, int Depth)>();
        for (int index = roots.Count - 1; index >= 0; index--)
            stack.Push((roots[index], 0));
        while (stack.Count > 0)
        {
            (PdfOutlineItem item, int depth) = stack.Pop();
            result.Add(
                $"{depth}:{item.Title}:{item.IsOpen}:{item.IsBold}:" +
                $"{item.IsItalic}:{item.Action.Type}:" +
                $"{item.Destination?.PageIndex}:" +
                $"{item.Destination?.NamedDestination}");
            for (int index = item.Children.Count - 1; index >= 0; index--)
                stack.Push((item.Children[index], depth + 1));
        }
        return string.Join("|", result);
    }

    private static Document Load(PdfReadOptions? options = null) =>
        Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "outline-alpha1.pdf"),
            options: options);

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
