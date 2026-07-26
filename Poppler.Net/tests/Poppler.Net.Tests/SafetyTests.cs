using System.Reflection;
using System.Runtime.InteropServices;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class SafetyTests
{
    [Fact]
    public void EnforcesInputLimitForStreams()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        using var input = new MemoryStream(source, writable: false);
        var options = new PdfReadOptions { MaximumInputBytes = source.Length - 1 };

        Assert.Throws<PdfLimitException>(() => Document.LoadFromStream(input, options: options));
    }

    [Fact]
    public void EnforcesDecodedStreamLimit()
    {
        var options = new PdfReadOptions { MaximumDecodedStreamBytes = 8 };
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: true),
            options: options);

        Assert.Throws<PdfLimitException>(() => document.CreatePage(0).Text());
    }

    [Fact]
    public void NormalizesInvalidFlateDataToPdfFormatException()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithInvalidFlateContent());

        Assert.Throws<PdfFormatException>(() => document.CreatePage(0).Text());
    }

    [Fact]
    public void EnforcesObjectLimitBeforeAllocatingXrefEntries()
    {
        var options = new PdfReadOptions
        {
            MaximumObjects = 5
        };

        Assert.Throws<PdfLimitException>(
            () => Document.LoadFromData(PdfFixtures.Create(compressContent: false), options: options));
    }

    [Fact]
    public void EnforcesDirectCollectionLimit()
    {
        var options = new PdfReadOptions
        {
            MaximumCollectionItems = 2
        };

        Assert.Throws<PdfLimitException>(
            () => Document.LoadFromData(PdfFixtures.Create(compressContent: false), options: options));
    }

    [Fact]
    public void ReportsMissingEndOfFileMarker()
    {
        using Document document = Document.LoadFromData(PdfFixtures.CreateWithoutEndOfFileMarker());

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "eof.missing");
    }

    [Fact]
    public void ProductionAssemblyHasNoNativeInterop()
    {
        Assembly assembly = typeof(Document).Assembly;
        MethodInfo[] methods = assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance))
            .ToArray();

        Assert.DoesNotContain(
            methods,
            method => method.GetCustomAttributes(typeof(DllImportAttribute), inherit: false).Length > 0);
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            name =>
                (name.Name ?? "").Contains("Cpp", StringComparison.OrdinalIgnoreCase) ||
                (name.Name ?? "").Contains("Native", StringComparison.OrdinalIgnoreCase));
    }
}
