using Poppler.Core;
using Poppler.DocumentModel;

namespace Poppler.Text;

internal static class PdfFontCollection
{
    public static IReadOnlyDictionary<string, PdfFontDecoder> Read(
        PdfDocumentCore document,
        PdfPageNode page)
    {
        var result = new Dictionary<string, PdfFontDecoder>(StringComparer.Ordinal);
        PdfDictionary? resources = page.Resources.AsDictionary(document);
        PdfDictionary? fonts = resources?.GetValueOrNull("Font").AsDictionary(document);
        if (fonts is null)
            return result;

        foreach ((string resourceName, PdfObject fontObject) in fonts)
        {
            PdfDictionary? dictionary = fontObject.AsDictionary(document);
            if (dictionary is not null)
                result[resourceName] = new PdfFontDecoder(resourceName, dictionary, document);
        }

        return result;
    }
}
