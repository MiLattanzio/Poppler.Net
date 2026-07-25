using System.Globalization;
using System.Text;
using Poppler.Core.Filters;

namespace Poppler.Core;

internal sealed class PdfDocumentCore
{
    private readonly byte[] _data;
    private readonly PdfReadOptions _options;
    private readonly Dictionary<PdfReference, PdfObject> _objectCache = new();
    private readonly HashSet<PdfReference> _resolving = new();
    private readonly List<PdfDiagnostic> _diagnostics = new();
    private readonly PdfCrossReference _crossReference;

    public PdfDocumentCore(byte[] data, PdfReadOptions options)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        PdfVersion = ReadHeaderVersion(data);
        _crossReference = new PdfCrossReference(this, data, options);
        _crossReference.Load();
    }

    public string PdfVersion { get; }
    public PdfReadOptions Options => _options;
    public PdfDictionary Trailer => _crossReference.Trailer;
    public bool XrefWasRepaired => _crossReference.WasRepaired;
    public IReadOnlyList<PdfDiagnostic> Diagnostics => _diagnostics;
    public ReadOnlyMemory<byte> OriginalBytes => _data;

    public PdfObject Resolve(PdfReference reference)
    {
        if (_objectCache.TryGetValue(reference, out PdfObject? cached))
            return cached;
        if (!_resolving.Add(reference))
            throw new PdfFormatException($"Circular indirect object reference at {reference}.");

        try
        {
            if (!_crossReference.Entries.TryGetValue(reference.ObjectNumber, out PdfXrefEntry? entry) ||
                entry.Type == PdfXrefEntryType.Free)
            {
                throw new PdfFormatException($"Indirect object {reference} is missing.");
            }

            PdfObject value = entry.Type switch
            {
                PdfXrefEntryType.Uncompressed => ReadUncompressed(reference, entry),
                PdfXrefEntryType.Compressed => ReadCompressed(reference, entry),
                _ => throw new PdfFormatException($"Indirect object {reference} is free.")
            };
            _objectCache[reference] = value;
            return value;
        }
        finally
        {
            _resolving.Remove(reference);
        }
    }

    public byte[] Decode(PdfStream stream) =>
        PdfFilterPipeline.Decode(stream, this, _options);

    public PdfDictionary FindCatalog()
    {
        PdfDictionary? fromTrailer = Trailer.GetValueOrNull("Root").AsDictionary(this);
        if (fromTrailer is not null)
            return fromTrailer;

        foreach ((int objectNumber, PdfXrefEntry entry) in _crossReference.Entries)
        {
            if (entry.Type == PdfXrefEntryType.Free)
                continue;
            try
            {
                if (Resolve(new PdfReference(objectNumber, entry.Generation)) is PdfDictionary dictionary &&
                    dictionary.GetValueOrNull("Type").AsName(this) == "Catalog")
                {
                    AddDiagnostic(
                        PdfDiagnosticSeverity.Warning,
                        "catalog.recovered",
                        "The catalog was recovered without a trailer /Root reference.");
                    return dictionary;
                }
            }
            catch (PdfException)
            {
                // Repair mode may discover false-positive object headers.
            }
        }

        throw new PdfFormatException("The PDF catalog could not be found.");
    }

    public bool IsLinearized()
    {
        int headerEnd = Math.Min(_data.Length, 4096);
        int objIndex = _data.AsSpan(0, headerEnd).IndexOf(" obj"u8);
        if (objIndex < 0)
            return false;
        int lineStart = objIndex;
        while (lineStart > 0 && _data[lineStart - 1] is not (byte)'\r' and not (byte)'\n')
            lineStart--;
        try
        {
            var reader = new PdfSyntaxReader(_data, lineStart, _data.Length - lineStart, _options);
            PdfIndirectObject first = reader.ReadIndirectObject();
            PdfDictionary? dictionary = first.Value switch
            {
                PdfDictionary direct => direct,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            return dictionary?.ContainsKey("Linearized") == true;
        }
        catch (PdfException)
        {
            return false;
        }
    }

    public void AddDiagnostic(
        PdfDiagnosticSeverity severity,
        string code,
        string message,
        long? offset = null) =>
        _diagnostics.Add(new PdfDiagnostic(severity, code, message, offset));

    private PdfObject ReadUncompressed(PdfReference requested, PdfXrefEntry entry)
    {
        if (entry.Field1 < 0 || entry.Field1 >= _data.Length)
            throw new PdfFormatException($"Object {requested} has an invalid xref offset.");
        int offset = checked((int)entry.Field1);
        var reader = new PdfSyntaxReader(_data, offset, _data.Length - offset, _options);
        PdfIndirectObject indirect = reader.ReadIndirectObject(ResolveLength);
        if (indirect.ObjectNumber != requested.ObjectNumber)
        {
            throw new PdfFormatException(
                $"Xref for {requested} points to object {indirect.ObjectNumber} {indirect.Generation}.");
        }

        return indirect.Value;
    }

    private int? ResolveLength(PdfObject value)
    {
        PdfObject resolved = value is PdfReference reference ? Resolve(reference) : value;
        return resolved is PdfNumber number && number.IsInteger &&
               number.Value is >= 0 and <= int.MaxValue
            ? (int)number.Value
            : null;
    }

    private PdfObject ReadCompressed(PdfReference requested, PdfXrefEntry entry)
    {
        if (entry.Field1 is < 0 or > int.MaxValue)
            throw new PdfFormatException($"Object {requested} has an invalid object-stream reference.");
        int streamObjectNumber = (int)entry.Field1;
        PdfObject containerObject = Resolve(new PdfReference(streamObjectNumber, 0));
        if (containerObject is not PdfStream container ||
            container.Dictionary.GetValueOrNull("Type").AsName(this) is not "ObjStm")
        {
            throw new PdfFormatException($"Object stream {streamObjectNumber} is invalid.");
        }

        int count = container.Dictionary.GetValueOrNull("N").AsInteger(this) ??
                    throw new PdfFormatException("Object stream has no /N.");
        int first = container.Dictionary.GetValueOrNull("First").AsInteger(this) ??
                    throw new PdfFormatException("Object stream has no /First.");
        if (count < 0 || count > _options.MaximumObjects || first < 0)
            throw new PdfFormatException("Invalid object stream header.");

        byte[] decoded = Decode(container);
        if (first > decoded.Length)
            throw new PdfFormatException("Object stream /First is outside the stream.");

        var headerReader = new PdfSyntaxReader(decoded, 0, first, _options);
        var items = new (int ObjectNumber, int Offset)[count];
        for (int index = 0; index < count; index++)
        {
            string objectToken = headerReader.ReadRawToken();
            string offsetToken = headerReader.ReadRawToken();
            if (!int.TryParse(objectToken, NumberStyles.None, CultureInfo.InvariantCulture, out int objectNumber) ||
                !int.TryParse(offsetToken, NumberStyles.None, CultureInfo.InvariantCulture, out int relativeOffset) ||
                objectNumber < 0 ||
                relativeOffset < 0)
            {
                throw new PdfFormatException("Invalid object stream index.");
            }

            items[index] = (objectNumber, relativeOffset);
        }

        for (int index = 0; index < items.Length; index++)
        {
            int start = checked(first + items[index].Offset);
            int end = index + 1 < items.Length
                ? checked(first + items[index + 1].Offset)
                : decoded.Length;
            if (start < first || end < start || end > decoded.Length)
                throw new PdfFormatException("Invalid object stream object range.");
            var objectReader = new PdfSyntaxReader(decoded, start, end - start, _options);
            PdfObject value = objectReader.ReadObject();
            _objectCache[new PdfReference(items[index].ObjectNumber, 0)] = value;
        }

        if (_objectCache.TryGetValue(new PdfReference(requested.ObjectNumber, 0), out PdfObject? result))
            return result;
        throw new PdfFormatException($"Object {requested} was not found in object stream {streamObjectNumber}.");
    }

    private static string ReadHeaderVersion(byte[] data)
    {
        int limit = Math.Min(data.Length, 1024);
        int marker = data.AsSpan(0, limit).IndexOf("%PDF-"u8);
        if (marker < 0 || marker + 8 > data.Length)
            throw new PdfFormatException("Missing PDF header.");
        int start = marker + 5;
        int end = start;
        while (end < limit && data[end] is >= (byte)'0' and <= (byte)'9' or (byte)'.')
            end++;
        string version = Encoding.ASCII.GetString(data, start, end - start);
        if (version.Length < 3 || !version.Contains('.', StringComparison.Ordinal))
            throw new PdfFormatException("Invalid PDF header version.");
        return version;
    }
}
