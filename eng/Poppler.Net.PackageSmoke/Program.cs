using System.Security.Cryptography;
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

            Console.WriteLine(
                $"Poppler.Net {Document.PortVersion} clean consumer rendered " +
                $"PNG {Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()} " +
                $"and {html.Length} HTML characters.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Package consumer smoke failed: {exception.Message}");
            return 1;
        }
    }
}
