using Poppler.Core;

namespace Poppler.DocumentModel;

internal static class EmbeddedFileReader
{
    public static IReadOnlyList<EmbeddedFile> Read(PdfDocumentCore document, PdfDictionary catalog)
    {
        PdfDictionary? names = catalog.GetValueOrNull("Names").AsDictionary(document);
        PdfObject? root = names?.GetValueOrNull("EmbeddedFiles");
        if (root is null)
            return Array.Empty<EmbeddedFile>();

        var results = new List<EmbeddedFile>();
        ReadNameTree(root, document, results, 0);
        return results;
    }

    private static void ReadNameTree(
        PdfObject nodeObject,
        PdfDocumentCore document,
        List<EmbeddedFile> results,
        int depth)
    {
        if (depth > document.Options.MaximumTreeDepth)
            throw new PdfLimitException("Embedded-file name tree is too deep.");
        PdfDictionary? node = nodeObject.AsDictionary(document);
        if (node is null)
            return;

        PdfArray? entries = node.GetValueOrNull("Names").AsArray(document);
        if (entries is not null)
        {
            for (int index = 0; index + 1 < entries.Count; index += 2)
            {
                string treeName = (entries[index].Resolve(document) as PdfString)?.Text ?? "";
                PdfDictionary? specification = entries[index + 1].AsDictionary(document);
                EmbeddedFile? file = specification is null
                    ? null
                    : Create(treeName, specification, document);
                if (file is not null)
                    results.Add(file);
            }
        }

        PdfArray? kids = node.GetValueOrNull("Kids").AsArray(document);
        if (kids is not null)
        {
            foreach (PdfObject kid in kids)
                ReadNameTree(kid, document, results, depth + 1);
        }
    }

    internal static EmbeddedFile? Create(
        string treeName,
        PdfDictionary specification,
        PdfDocumentCore document)
    {
        string fileName =
            (specification.GetValueOrNull("UF")?.Resolve(document) as PdfString)?.Text ??
            (specification.GetValueOrNull("F")?.Resolve(document) as PdfString)?.Text ??
            treeName;
        string description =
            (specification.GetValueOrNull("Desc")?.Resolve(document) as PdfString)?.Text ?? "";
        PdfDictionary? embedded = specification.GetValueOrNull("EF").AsDictionary(document);
        PdfStream? stream =
            embedded?.GetValueOrNull("UF").AsStream(document) ??
            embedded?.GetValueOrNull("F").AsStream(document);
        if (stream is null)
            return null;

        PdfDictionary? parameters = stream.Dictionary.GetValueOrNull("Params").AsDictionary(document);
        int? declaredSize = parameters?.GetValueOrNull("Size").AsInteger(document);
        DateTimeOffset? creationDate = PdfDateParser.Parse(
            (parameters?.GetValueOrNull("CreationDate")?.Resolve(document) as PdfString)?.Text);
        DateTimeOffset? modificationDate = PdfDateParser.Parse(
            (parameters?.GetValueOrNull("ModDate")?.Resolve(document) as PdfString)?.Text);
        byte[] checksum =
            (parameters?.GetValueOrNull("CheckSum")?.Resolve(document) as PdfString)?.Bytes.ToArray() ??
            Array.Empty<byte>();
        string mimeType = stream.Dictionary.GetValueOrNull("Subtype").AsName(document) ?? "";

        return new EmbeddedFile(
            fileName,
            description,
            mimeType.Replace('#', '/'),
            declaredSize,
            creationDate,
            modificationDate,
            checksum,
            () => document.Decode(stream));
    }
}
