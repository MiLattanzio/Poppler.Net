using Poppler.Core;

namespace Poppler.DocumentModel;

internal static class PdfPageContentReader
{
    public static byte[] Read(
        PdfDocumentCore document,
        PdfObject? contents)
    {
        if (contents is null)
            return Array.Empty<byte>();

        PdfObject resolved = contents.Resolve(document);
        if (resolved is PdfStream stream)
            return document.Decode(stream);
        if (resolved is not PdfArray array)
            return Array.Empty<byte>();
        if (array.Count > document.Options.MaximumContentStreamsPerPage)
        {
            throw new PdfLimitException(
                "Page content-stream count exceeds the configured limit.");
        }

        using var output = new MemoryStream();
        PdfException? firstFailure = null;
        int decodedStreams = 0;
        int skippedStreams = 0;
        foreach (PdfObject item in array)
        {
            try
            {
                PdfStream? part = item.AsStream(document);
                if (part is null)
                {
                    firstFailure ??= new PdfFormatException(
                        "Page /Contents array contains a non-stream entry.");
                    if (!document.Options.AttemptContentStreamRepair)
                        throw firstFailure;
                    skippedStreams++;
                    continue;
                }

                byte[] decoded = document.Decode(part);
                if (output.Length > 0)
                    output.WriteByte((byte)'\n');
                if (output.Length + decoded.Length >
                    document.Options.MaximumDecodedStreamBytes)
                {
                    throw new PdfLimitException(
                        "Combined page content exceeds the decoded stream limit.");
                }

                output.Write(decoded);
                decodedStreams++;
            }
            catch (PdfLimitException)
            {
                throw;
            }
            catch (PdfException exception)
                when (document.Options.AttemptContentStreamRepair)
            {
                firstFailure ??= exception;
                skippedStreams++;
            }
        }

        if (decodedStreams == 0 && firstFailure is not null)
            throw firstFailure;
        if (skippedStreams > 0)
        {
            document.AddDiagnosticOnce(
                PdfDiagnosticSeverity.Warning,
                "content.repaired",
                $"Skipped {skippedStreams} invalid page content-stream " +
                $"{(skippedStreams == 1 ? "entry" : "entries")}.");
        }

        return output.ToArray();
    }
}
