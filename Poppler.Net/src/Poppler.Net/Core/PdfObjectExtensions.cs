namespace Poppler.Core;

internal static class PdfObjectExtensions
{
    public static PdfObject Resolve(this PdfObject value, PdfDocumentCore document) =>
        value is PdfReference reference ? document.Resolve(reference) : value;

    public static PdfDictionary? AsDictionary(this PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) switch
        {
            PdfDictionary dictionary => dictionary,
            PdfStream stream => stream.Dictionary,
            _ => null
        };

    public static PdfArray? AsArray(this PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) as PdfArray;

    public static PdfStream? AsStream(this PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) as PdfStream;

    public static string? AsName(this PdfObject? value, PdfDocumentCore document) =>
        (value?.Resolve(document) as PdfName)?.Value;

    public static int? AsInteger(this PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) is PdfNumber number && number.IsInteger
            ? checked((int)number.Value)
            : null;

    public static double? AsNumber(this PdfObject? value, PdfDocumentCore document) =>
        (value?.Resolve(document) as PdfNumber)?.Value;

    public static PdfObject? GetValueOrNull(this PdfDictionary dictionary, string key) =>
        dictionary.TryGetValue(key, out PdfObject? value) ? value : null;

    public static PdfRectangle? AsRectangle(this PdfObject? value, PdfDocumentCore document)
    {
        PdfArray? array = value.AsArray(document);
        if (array is null || array.Count < 4)
            return null;

        double? x1 = array[0].AsNumber(document);
        double? y1 = array[1].AsNumber(document);
        double? x2 = array[2].AsNumber(document);
        double? y2 = array[3].AsNumber(document);
        return x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue
            ? new PdfRectangle(x1.Value, y1.Value, x2.Value, y2.Value)
            : null;
    }
}
