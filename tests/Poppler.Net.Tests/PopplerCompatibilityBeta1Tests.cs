using System.Security.Cryptography;
using System.Text.Json;

namespace Poppler.Net.Tests;

public sealed class PopplerCompatibilityBeta1Tests
{
    [Test]
    public void ManifestPinsEverySourcePageAndItsClassification()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;
        Assert.That(
            root.GetProperty("semantic_reference").GetString(),
            Is.EqualTo("Poppler 26.07.0"));

        JsonElement[] corpora = root.GetProperty("corpora")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            corpora.Select(corpus => corpus.GetProperty("id").GetString()),
            Is.EqualTo(new[]
            {
                "geometry",
                "transparency",
                "shading",
                "cross-feature"
            }));

        foreach (JsonElement corpus in corpora)
        {
            string file = corpus.GetProperty("file").GetString()!;
            string expectedHash = corpus.GetProperty("sha256").GetString()!;
            string actualHash = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(FixtureDirectory(), file))))
                .ToLowerInvariant();
            Assert.That(actualHash, Is.EqualTo(expectedHash), file);

            string pageManifest = corpus.GetProperty("page_manifest").GetString()!;
            using JsonDocument source = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(FixtureDirectory(), pageManifest)));
            string[] sourcePages = source.RootElement.GetProperty("pages")
                .EnumerateArray()
                .Select(page => page.GetString()!)
                .ToArray();
            JsonElement[] trackedPages = corpus.GetProperty("pages")
                .EnumerateArray()
                .ToArray();

            Assert.That(trackedPages, Has.Length.EqualTo(sourcePages.Length), file);
            for (int index = 0; index < sourcePages.Length; index++)
            {
                JsonElement tracked = trackedPages[index];
                Assert.Multiple((Action)(() =>
                {
                    Assert.That(
                        tracked.GetProperty("number").GetInt32(),
                        Is.EqualTo(index + 1),
                        $"{file} page number");
                    Assert.That(
                        tracked.GetProperty("name").GetString(),
                        Is.EqualTo(sourcePages[index]),
                        $"{file} page name");
                    Assert.That(
                        tracked.GetProperty("classification").GetString(),
                        Is.Not.Empty,
                        $"{file} classification");
                }));
            }
        }
    }

    [Test]
    public void EveryMeasuredDifferenceFitsItsApprovedBudget()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement[] pages = manifest.RootElement.GetProperty("corpora")
            .EnumerateArray()
            .SelectMany(corpus => corpus.GetProperty("pages").EnumerateArray())
            .ToArray();

        Assert.That(pages, Has.Length.EqualTo(22));
        Assert.Multiple((Action)(() =>
        {
            foreach (JsonElement page in pages)
            {
                double baseline = page.GetProperty("baseline_mae").GetDouble();
                double maximum = page.GetProperty("maximum_mae").GetDouble();
                double changed = page.GetProperty("changed_rgb_pixels_over_8")
                    .GetDouble();
                string name = page.GetProperty("name").GetString()!;

                Assert.That(baseline, Is.GreaterThanOrEqualTo(0), name);
                Assert.That(baseline, Is.LessThanOrEqualTo(maximum), name);
                Assert.That(maximum, Is.LessThan(0.06), name);
                Assert.That(changed, Is.InRange(0, 1), name);
            }
        }));
    }

    private static JsonDocument LoadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            FixtureDirectory(),
            "poppler-beta1-compatibility.json")));

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
