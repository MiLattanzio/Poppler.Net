using System.Security.Cryptography;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.PackageSmoke;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                throw new ArgumentException(
                    "Usage: Poppler.Net.PackageSmoke <fixture.pdf> <expected-version>");
            }
            if (Document.PortVersion != args[1])
            {
                throw new InvalidOperationException(
                    $"Loaded Poppler.Net {Document.PortVersion}, expected {args[1]}.");
            }

            using Document document = Document.LoadFromFile(args[0]);
            if (document.Pages != 6)
                throw new InvalidDataException($"Expected 6 pages, found {document.Pages}.");
            Page page = document.CreatePage(0);
            if (page.Graphics.Count == 0)
                throw new InvalidDataException("The package produced an empty display list.");

            byte[] png = page.RenderToPng(new RasterRenderOptions
            {
                Dpi = 36,
                Antialiasing = 1,
                UseFontSubstitution = false
            });
            if (!png.AsSpan().StartsWith(new byte[]
                { 137, (byte)'P', (byte)'N', (byte)'G' }))
            {
                throw new InvalidDataException("The package did not produce a PNG.");
            }
            string svg = page.RenderToSvg(new SvgRenderOptions
            {
                RasterFallbackDpi = 36
            });
            if (!svg.Contains("<svg", StringComparison.Ordinal))
                throw new InvalidDataException("The package did not produce SVG output.");
            string html = page.RenderToHtml(new HtmlRenderOptions
            {
                RasterFallbackDpi = 36
            });
            if (!html.Contains("class=\"pdf-text-layer\"", StringComparison.Ordinal) ||
                !html.Contains("<svg", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package did not produce a fixed-layout HTML text layer.");
            }

            string structured = document.ExportToJson(new StructuredExportOptions
            {
                FirstPageIndex = 1,
                PageCount = 2,
                IncludeImages = false
            });
            using JsonDocument json = JsonDocument.Parse(structured);
            JsonElement[] selectedPages = json.RootElement.GetProperty("pages")
                .EnumerateArray()
                .ToArray();
            if (json.RootElement.GetProperty("schemaVersion").GetString() != "1.0" ||
                selectedPages.Length != 2 ||
                selectedPages[0].GetProperty("index").GetInt32() != 1 ||
                selectedPages[1].GetProperty("index").GetInt32() != 2)
            {
                throw new InvalidDataException(
                    "The package did not preserve the selected structured-export range.");
            }

            byte[] extractedBytes = document.ExtractPage(2);
            using Document extracted = Document.LoadFromData(extractedBytes);
            if (extracted.Pages != 1 ||
                extracted.IsEncrypted ||
                extracted.CreatePage(0).Rotation != document.CreatePage(2).Rotation)
            {
                throw new InvalidDataException(
                    "The package did not produce an autonomous standalone page.");
            }

            ExpectLimit(
                () => document.RenderToHtml(new HtmlExportOptions
                {
                    PageCount = 2,
                    MaximumPages = 1
                }),
                "HTML export page count exceeds the configured limit.");
            ExpectLimit(
                () => document.ExportToJson(new StructuredExportOptions
                {
                    MaximumNodes = 1
                }),
                "Structured export node count exceeds the configured limit.");
            ExpectLimit(
                () => document.ExtractPages(
                    0,
                    2,
                    new PdfPageExtractionOptions { MaximumPages = 1 }),
                "Extracted page count exceeds the configured limit.");

            Console.WriteLine(
                $"Poppler.Net {Document.PortVersion} clean consumer rendered " +
                $"PNG {Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()} " +
                $"and {html.Length} HTML characters, exported schema 1.0, " +
                "and reopened a standalone page.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Package consumer smoke failed: {exception.Message}");
            return 1;
        }
    }

    private static void ExpectLimit(Action action, string message)
    {
        try
        {
            action();
        }
        catch (PdfLimitException exception) when (exception.Message == message)
        {
            return;
        }

        throw new InvalidDataException(
            $"The packaged API did not enforce '{message}' deterministically.");
    }
}
