using System.Reflection;
using System.Runtime.InteropServices;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class SafetyTests
{
    [Test]
    public void EnforcesInputLimitForStreams()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        using var input = new MemoryStream(source, writable: false);
        var options = new PdfReadOptions { MaximumInputBytes = source.Length - 1 };

        Assert.That(
            (Action)(() => Document.LoadFromStream(input, options: options)),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void EnforcesDecodedStreamLimit()
    {
        var options = new PdfReadOptions { MaximumDecodedStreamBytes = 8 };
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: true),
            options: options);

        Assert.That(
            (Action)(() => document.CreatePage(0).Text()),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void NormalizesInvalidFlateDataToPdfFormatException()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithInvalidFlateContent());

        Assert.That(
            (Action)(() => document.CreatePage(0).Text()),
            Throws.TypeOf<PdfFormatException>());
    }

    [Test]
    public void EnforcesObjectLimitBeforeAllocatingXrefEntries()
    {
        var options = new PdfReadOptions
        {
            MaximumObjects = 5
        };

        Assert.That(
            (Action)(() => Document.LoadFromData(
                PdfFixtures.Create(compressContent: false),
                options: options)),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void EnforcesDirectCollectionLimit()
    {
        var options = new PdfReadOptions
        {
            MaximumCollectionItems = 2
        };

        Assert.That(
            (Action)(() => Document.LoadFromData(
                PdfFixtures.Create(compressContent: false),
                options: options)),
            Throws.TypeOf<PdfLimitException>());
    }

    [Test]
    public void ReportsMissingEndOfFileMarker()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithoutEndOfFileMarker());

        Assert.That(
            document.Diagnostics.Any(diagnostic => diagnostic.Code == "eof.missing"),
            Is.True);
    }

    [Test]
    public void ProductionAssemblyHasNoNativeInterop()
    {
        Assembly assembly = typeof(Document).Assembly;
        MethodInfo[] methods = assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance))
            .ToArray();

        Assert.That(
            methods.Any(
                method =>
                    method.GetCustomAttributes(typeof(DllImportAttribute), inherit: false).Length > 0),
            Is.False);
        Assert.That(
            assembly.GetReferencedAssemblies().Any(
                name =>
                    (name.Name ?? "").Contains("Cpp", StringComparison.OrdinalIgnoreCase) ||
                    (name.Name ?? "").Contains("Native", StringComparison.OrdinalIgnoreCase)),
            Is.False);
    }
}
