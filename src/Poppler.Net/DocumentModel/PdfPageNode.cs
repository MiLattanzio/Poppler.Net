using Poppler.Core;

namespace Poppler.DocumentModel;

internal sealed record PdfPageNode(
    PdfReference? SourceReference,
    PdfDictionary Dictionary,
    PdfRectangle MediaBox,
    PdfRectangle CropBox,
    PdfRectangle? BleedBox,
    PdfRectangle? TrimBox,
    PdfRectangle? ArtBox,
    PdfObject? Resources,
    int Rotation);

internal static class PdfPageTreeReader
{
    private static readonly PdfRectangle DefaultMediaBox = new(0, 0, 612, 792);

    public static IReadOnlyList<PdfPageNode> Read(PdfDocumentCore document, PdfDictionary catalog)
    {
        PdfObject pagesRoot = catalog.GetValueOrNull("Pages") ??
                              throw new PdfFormatException("Catalog has no /Pages tree.");
        var pages = new List<PdfPageNode>();
        var activeReferences = new HashSet<PdfReference>();
        Visit(
            pagesRoot,
            document,
            pages,
            activeReferences,
            inheritedMedia: null,
            inheritedCrop: null,
            inheritedBleed: null,
            inheritedTrim: null,
            inheritedArt: null,
            inheritedResources: null,
            inheritedRotation: 0,
            depth: 0);
        return pages;
    }

    private static void Visit(
        PdfObject nodeObject,
        PdfDocumentCore document,
        List<PdfPageNode> pages,
        HashSet<PdfReference> activeReferences,
        PdfRectangle? inheritedMedia,
        PdfRectangle? inheritedCrop,
        PdfRectangle? inheritedBleed,
        PdfRectangle? inheritedTrim,
        PdfRectangle? inheritedArt,
        PdfObject? inheritedResources,
        int inheritedRotation,
        int depth)
    {
        if (depth > document.Options.MaximumTreeDepth)
            throw new PdfLimitException("Page tree is too deep.");
        if (pages.Count >= document.Options.MaximumPages)
            throw new PdfLimitException($"Page count exceeds {document.Options.MaximumPages}.");

        PdfReference? nodeReference = nodeObject as PdfReference;
        if (nodeReference is not null && !activeReferences.Add(nodeReference))
            throw new PdfFormatException("Circular page tree.");
        try
        {
            PdfDictionary? node = nodeObject.AsDictionary(document);
            if (node is null)
                throw new PdfFormatException("Page-tree node is not a dictionary.");

            PdfRectangle? media = node.GetValueOrNull("MediaBox").AsRectangle(document) ?? inheritedMedia;
            PdfRectangle? crop = node.GetValueOrNull("CropBox").AsRectangle(document) ?? inheritedCrop ?? media;
            PdfRectangle? bleed = node.GetValueOrNull("BleedBox").AsRectangle(document) ?? inheritedBleed;
            PdfRectangle? trim = node.GetValueOrNull("TrimBox").AsRectangle(document) ?? inheritedTrim;
            PdfRectangle? art = node.GetValueOrNull("ArtBox").AsRectangle(document) ?? inheritedArt;
            PdfObject? resources = node.GetValueOrNull("Resources") ?? inheritedResources;
            int rotation = node.GetValueOrNull("Rotate").AsInteger(document) ?? inheritedRotation;
            rotation = ((rotation % 360) + 360) % 360;

            PdfArray? kids = node.GetValueOrNull("Kids").AsArray(document);
            string? type = node.GetValueOrNull("Type").AsName(document);
            if (type == "Pages" || kids is not null)
            {
                if (kids is null)
                    throw new PdfFormatException("/Pages node has no /Kids.");
                foreach (PdfObject kid in kids)
                {
                    Visit(
                        kid,
                        document,
                        pages,
                        activeReferences,
                        media,
                        crop,
                        bleed,
                        trim,
                        art,
                        resources,
                        rotation,
                        depth + 1);
                }

                return;
            }

            PdfRectangle effectiveMedia = NormalizeBox(media, DefaultMediaBox);
            PdfRectangle effectiveCrop = ClipTo(
                NormalizeBox(crop, effectiveMedia),
                effectiveMedia);
            PdfRectangle? effectiveBleed = NormalizeAndClipOptional(bleed, effectiveMedia);
            PdfRectangle? effectiveTrim = NormalizeAndClipOptional(trim, effectiveMedia);
            PdfRectangle? effectiveArt = NormalizeAndClipOptional(art, effectiveMedia);
            pages.Add(new PdfPageNode(
                nodeReference,
                node,
                effectiveMedia,
                effectiveCrop,
                effectiveBleed,
                effectiveTrim,
                effectiveArt,
                resources,
                rotation));
        }
        finally
        {
            if (nodeReference is not null)
                activeReferences.Remove(nodeReference);
        }
    }

    private static PdfRectangle NormalizeBox(
        PdfRectangle? value,
        PdfRectangle fallback)
    {
        if (value is not { } box ||
            !double.IsFinite(box.Left) ||
            !double.IsFinite(box.Bottom) ||
            !double.IsFinite(box.Right) ||
            !double.IsFinite(box.Top) ||
            box == default)
        {
            return fallback;
        }

        return new PdfRectangle(
            Math.Min(box.Left, box.Right),
            Math.Min(box.Bottom, box.Top),
            Math.Max(box.Left, box.Right),
            Math.Max(box.Bottom, box.Top));
    }

    private static PdfRectangle? NormalizeAndClipOptional(
        PdfRectangle? value,
        PdfRectangle mediaBox)
    {
        if (value is null || value.Value == default)
            return null;
        return ClipTo(NormalizeBox(value, mediaBox), mediaBox);
    }

    private static PdfRectangle ClipTo(PdfRectangle value, PdfRectangle boundary) =>
        new(
            Math.Clamp(value.Left, boundary.Left, boundary.Right),
            Math.Clamp(value.Bottom, boundary.Bottom, boundary.Top),
            Math.Clamp(value.Right, boundary.Left, boundary.Right),
            Math.Clamp(value.Top, boundary.Bottom, boundary.Top));
}
