using System.Globalization;
using Poppler.Core.Filters;

namespace Poppler.Core;

internal enum PdfXrefEntryType
{
    Free,
    Uncompressed,
    Compressed
}

internal sealed record PdfXrefEntry(
    PdfXrefEntryType Type,
    long Field1,
    int Field2,
    int Generation);

internal sealed class PdfCrossReference
{
    private readonly PdfDocumentCore _document;
    private readonly byte[] _data;
    private readonly PdfReadOptions _options;
    private readonly Dictionary<int, PdfXrefEntry> _entries = new();
    private readonly HashSet<long> _visitedSections = new();

    public PdfCrossReference(PdfDocumentCore document, byte[] data, PdfReadOptions options)
    {
        _document = document;
        _data = data;
        _options = options;
    }

    public PdfDictionary Trailer { get; private set; } =
        new(new Dictionary<string, PdfObject>(StringComparer.Ordinal));

    public bool WasRepaired { get; private set; }
    public IReadOnlyDictionary<int, PdfXrefEntry> Entries => _entries;

    public void Load()
    {
        try
        {
            int startXref = FindStartXref();
            ReadSection(startXref, isPrimary: true);
            if (!_entries.Values.Any(entry => entry.Type is not PdfXrefEntryType.Free))
                throw new PdfFormatException("The cross-reference contains no objects.");
        }
        catch (Exception exception) when (
            _options.AttemptXrefRepair &&
            exception is PdfFormatException)
        {
            _document.AddDiagnostic(
                PdfDiagnosticSeverity.Warning,
                "xref.repair",
                $"Cross-reference parsing failed; conservative repair was used: {exception.Message}");
            _entries.Clear();
            _visitedSections.Clear();
            _document.ResetResolutionCache();
            RepairByScanningObjects();
            WasRepaired = true;
        }
    }

    private void ReadSection(long offset, bool isPrimary)
    {
        int physicalOffset = _document.ToPhysicalOffset(offset);
        if (physicalOffset < _document.HeaderOffset || physicalOffset >= _data.Length)
            throw new PdfFormatException("Cross-reference offset is outside the file", offset);
        if (!_visitedSections.Add(offset))
            return;
        if (_visitedSections.Count > _options.MaximumTreeDepth)
            throw new PdfLimitException("The cross-reference chain is too deep.");

        var reader = new PdfSyntaxReader(
            _data,
            physicalOffset,
            _data.Length - physicalOffset,
            _options);
        reader.SkipTrivia();

        PdfDictionary sectionTrailer;
        if (reader.TryReadKeyword("xref"))
        {
            sectionTrailer = ReadClassicTable(reader);
        }
        else
        {
            PdfIndirectObject indirect = reader.ReadIndirectObject();
            if (indirect.Value is not PdfStream stream ||
                stream.Dictionary.GetValueOrNull("Type").AsName(_document) is not "XRef")
            {
                throw new PdfFormatException("startxref does not point to an xref table or stream", offset);
            }

            _entries.TryAdd(
                indirect.ObjectNumber,
                new PdfXrefEntry(
                    PdfXrefEntryType.Uncompressed,
                    _document.ToLogicalOffset(indirect.StartOffset),
                    0,
                    indirect.Generation));
            sectionTrailer = stream.Dictionary;
            ReadXrefStream(stream);
        }

        if (isPrimary)
            Trailer = sectionTrailer;

        int? hybridOffset = sectionTrailer.GetValueOrNull("XRefStm").AsInteger(_document);
        if (hybridOffset is >= 0)
            ReadSection(hybridOffset.Value, isPrimary: false);

        int? previousOffset = sectionTrailer.GetValueOrNull("Prev").AsInteger(_document);
        if (previousOffset is >= 0)
            ReadSection(previousOffset.Value, isPrimary: false);
    }

    private PdfDictionary ReadClassicTable(PdfSyntaxReader reader)
    {
        long parsedEntries = 0;
        while (true)
        {
            string first = reader.ReadRawToken();
            if (first == "trailer")
            {
                if (reader.ReadObject() is not PdfDictionary trailer)
                    throw new PdfFormatException("Classic xref trailer is not a dictionary", reader.Position);
                return trailer;
            }

            string countToken = reader.ReadRawToken();
            if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int firstObject) ||
                !int.TryParse(countToken, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ||
                firstObject < 0 ||
                count < 0)
            {
                throw new PdfFormatException("Invalid xref subsection header", reader.Position);
            }

            EnsureObjectRange(firstObject, count);
            parsedEntries += count;
            if (parsedEntries > _options.MaximumObjects)
                throw new PdfLimitException("Classic xref contains too many entries.");
            for (int index = 0; index < count; index++)
            {
                string offsetToken = reader.ReadRawToken();
                string generationToken = reader.ReadRawToken();
                string status = reader.ReadRawToken();
                if (!long.TryParse(offsetToken, NumberStyles.None, CultureInfo.InvariantCulture, out long offset) ||
                    !int.TryParse(generationToken, NumberStyles.None, CultureInfo.InvariantCulture, out int generation) ||
                    status is not ("n" or "f"))
                {
                    throw new PdfFormatException("Invalid classic xref entry", reader.Position);
                }
                if (offset < 0 || generation is < 0 or > 65535)
                    throw new PdfFormatException("Invalid classic xref entry range", reader.Position);

                int objectNumber = firstObject + index;
                _entries.TryAdd(
                    objectNumber,
                    status == "n"
                        ? new PdfXrefEntry(PdfXrefEntryType.Uncompressed, offset, 0, generation)
                        : new PdfXrefEntry(PdfXrefEntryType.Free, offset, 0, generation));
            }
        }
    }

    private void ReadXrefStream(PdfStream stream)
    {
        PdfArray? widths = stream.Dictionary.GetValueOrNull("W").AsArray(_document);
        if (widths is null || widths.Count != 3)
            throw new PdfFormatException("Xref stream /W must contain three integers.");

        var fieldWidths = new int[3];
        for (int index = 0; index < 3; index++)
        {
            fieldWidths[index] = widths[index].AsInteger(_document) ?? -1;
            if (fieldWidths[index] is < 0 or > 8)
                throw new PdfFormatException("Invalid xref stream field width.");
        }

        int size = stream.Dictionary.GetValueOrNull("Size").AsInteger(_document) ??
                   throw new PdfFormatException("Xref stream has no /Size.");
        if (size < 0 || size > _options.MaximumObjects)
            throw new PdfLimitException($"PDF object count exceeds {_options.MaximumObjects}.");
        PdfArray? indexArray = stream.Dictionary.GetValueOrNull("Index").AsArray(_document);
        var ranges = new List<(int First, int Count)>();
        long totalEntries = 0;
        if (indexArray is null)
        {
            ranges.Add((0, size));
            totalEntries = size;
        }
        else
        {
            if (indexArray.Count % 2 != 0)
                throw new PdfFormatException("Xref stream /Index has an odd item count.");
            for (int index = 0; index < indexArray.Count; index += 2)
            {
                int first = indexArray[index].AsInteger(_document) ?? -1;
                int count = indexArray[index + 1].AsInteger(_document) ?? -1;
                if (first < 0 || count < 0)
                    throw new PdfFormatException("Invalid xref stream /Index range.");
                EnsureObjectRange(first, count);
                ranges.Add((first, count));
                totalEntries += count;
                if (totalEntries > _options.MaximumObjects)
                    throw new PdfLimitException("Xref stream contains too many indexed entries.");
            }
        }

        byte[] decoded = PdfFilterPipeline.Decode(stream, _document, _options);
        int position = 0;
        foreach ((int first, int count) in ranges)
        {
            EnsureObjectRange(first, count);
            for (int item = 0; item < count; item++)
            {
                ulong typeValue = fieldWidths[0] == 0
                    ? 1
                    : ReadUnsigned(decoded, ref position, fieldWidths[0]);
                ulong field1 = ReadUnsigned(decoded, ref position, fieldWidths[1]);
                ulong field2 = ReadUnsigned(decoded, ref position, fieldWidths[2]);
                int objectNumber = checked(first + item);

                PdfXrefEntry entry = CreateXrefEntry(typeValue, field1, field2);
                _entries.TryAdd(objectNumber, entry);
            }
        }
    }

    private PdfXrefEntry CreateXrefEntry(ulong type, ulong field1, ulong field2)
    {
        if (type is 0 or 1)
        {
            if (field1 > (ulong)long.MaxValue || field2 > 65535)
                throw new PdfFormatException("Xref stream entry is outside the supported range.");
            return new PdfXrefEntry(
                type == 0 ? PdfXrefEntryType.Free : PdfXrefEntryType.Uncompressed,
                (long)field1,
                0,
                (int)field2);
        }

        if (type == 2)
        {
            if (field1 >= (ulong)_options.MaximumObjects || field2 > (ulong)int.MaxValue)
                throw new PdfFormatException("Compressed xref entry is outside the supported range.");
            return new PdfXrefEntry(
                PdfXrefEntryType.Compressed,
                (long)field1,
                (int)field2,
                0);
        }

        return new PdfXrefEntry(PdfXrefEntryType.Free, 0, 0, 0);
    }

    private void RepairByScanningObjects()
    {
        int position = _document.HeaderOffset;
        int lastTrailerOffset = -1;
        while (position < _data.Length)
        {
            if (IsTokenStart(position) &&
                Matches(position, "trailer") &&
                IsTokenEnd(position + "trailer".Length))
            {
                lastTrailerOffset = position + "trailer".Length;
            }

            if (!IsTokenStart(position) || !IsDigit(_data[position]))
            {
                position++;
                continue;
            }

            int headerStart = position;
            if (!TryReadUnsignedToken(ref position, out int objectNumber) ||
                !SkipRequiredWhiteSpace(ref position) ||
                !TryReadUnsignedToken(ref position, out int generation) ||
                !SkipRequiredWhiteSpace(ref position) ||
                !Matches(position, "obj") ||
                !IsTokenEnd(position + 3) ||
                objectNumber < 0 ||
                generation is < 0 or > 65535)
            {
                position = headerStart + 1;
                continue;
            }

            EnsureObjectRange(objectNumber, 1);
            _entries[objectNumber] = new PdfXrefEntry(
                PdfXrefEntryType.Uncompressed,
                _document.ToLogicalOffset(headerStart),
                0,
                generation);
            try
            {
                var reader = new PdfSyntaxReader(
                    _data,
                    headerStart,
                    _data.Length - headerStart,
                    _options);
                PdfIndirectObject indirect = reader.ReadIndirectObject();
                position = indirect.ObjectNumber == objectNumber &&
                           indirect.Generation == generation &&
                           indirect.EndOffset > headerStart
                    ? indirect.EndOffset
                    : headerStart + 1;
            }
            catch (PdfException)
            {
                position = headerStart + 1;
            }
        }

        if (lastTrailerOffset >= 0)
        {
            try
            {
                var reader = new PdfSyntaxReader(
                    _data,
                    lastTrailerOffset,
                    _data.Length - lastTrailerOffset,
                    _options);
                if (reader.ReadObject() is PdfDictionary trailer)
                    Trailer = trailer;
            }
            catch (PdfException)
            {
                // An object scan can still recover a catalog without a readable trailer.
            }
        }

        RecoverSpecialStreams(lastTrailerOffset);
        if (_entries.Count == 0)
            throw new PdfFormatException("No indirect objects were found during xref repair.");
    }

    private void RecoverSpecialStreams(int trailerPosition)
    {
        int newestTrailerPosition = trailerPosition;
        KeyValuePair<int, PdfXrefEntry>[] streamCandidates = _entries
            .Where(pair => pair.Value.Type == PdfXrefEntryType.Uncompressed)
            .OrderBy(pair => pair.Value.Field1)
            .ToArray();

        foreach ((int objectNumber, PdfXrefEntry entry) in streamCandidates)
        {
            try
            {
                PdfObject value = _document.Resolve(
                    new PdfReference(objectNumber, entry.Generation));
                if (value is not PdfStream stream)
                    continue;

                string? type = stream.Dictionary.GetValueOrNull("Type").AsName(_document);
                if (type == "XRef")
                {
                    ReadXrefStream(stream);
                    int physicalPosition = _document.ToPhysicalOffset(entry.Field1);
                    if (physicalPosition > newestTrailerPosition &&
                        stream.Dictionary.ContainsKey("Root"))
                    {
                        Trailer = stream.Dictionary;
                        newestTrailerPosition = physicalPosition;
                    }
                }
                else if (type == "ObjStm")
                {
                    RecoverObjectStreamEntries(stream, objectNumber);
                }
            }
            catch (PdfException)
            {
                // A repair scan deliberately tolerates false-positive object headers.
            }
        }
    }

    private void RecoverObjectStreamEntries(PdfStream stream, int streamObjectNumber)
    {
        int count = stream.Dictionary.GetValueOrNull("N").AsInteger(_document) ?? -1;
        int first = stream.Dictionary.GetValueOrNull("First").AsInteger(_document) ?? -1;
        if (count < 0 || count > _options.MaximumObjects || first < 0)
            return;

        byte[] decoded = PdfFilterPipeline.Decode(stream, _document, _options);
        if (first > decoded.Length)
            return;

        var reader = new PdfSyntaxReader(decoded, 0, first, _options);
        for (int index = 0; index < count; index++)
        {
            string objectToken = reader.ReadRawToken();
            string offsetToken = reader.ReadRawToken();
            if (!int.TryParse(
                    objectToken,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int objectNumber) ||
                !int.TryParse(
                    offsetToken,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int relativeOffset) ||
                objectNumber < 0 ||
                relativeOffset < 0)
            {
                return;
            }

            EnsureObjectRange(objectNumber, 1);
            _entries.TryAdd(
                objectNumber,
                new PdfXrefEntry(
                    PdfXrefEntryType.Compressed,
                    streamObjectNumber,
                    index,
                    0));
        }
    }

    private int FindStartXref()
    {
        ReadOnlySpan<byte> marker = "startxref"u8;
        int searchStart = Math.Max(0, _data.Length - 1024 * 1024);
        int relative = _data.AsSpan(searchStart).LastIndexOf(marker);
        if (relative < 0)
            throw new PdfFormatException("Missing startxref marker.");

        int position = searchStart + relative + marker.Length;
        while (position < _data.Length && IsWhiteSpace(_data[position]))
            position++;
        int start = position;
        while (position < _data.Length && IsDigit(_data[position]))
            position++;
        if (position == start ||
            !int.TryParse(
                System.Text.Encoding.ASCII.GetString(_data, start, position - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int offset))
        {
            throw new PdfFormatException("Invalid startxref offset.", position);
        }

        return offset;
    }

    private void EnsureObjectRange(int firstObject, int count)
    {
        if (firstObject < 0 ||
            count < 0 ||
            (long)firstObject + count > _options.MaximumObjects)
        {
            throw new PdfLimitException($"PDF object count exceeds {_options.MaximumObjects}.");
        }
    }

    private static ulong ReadUnsigned(byte[] data, ref int position, int width)
    {
        if (position > data.Length - width)
            throw new PdfFormatException("Truncated xref stream.");
        ulong value = 0;
        for (int index = 0; index < width; index++)
            value = (value << 8) | data[position++];
        return value;
    }

    private bool TryReadUnsignedToken(ref int position, out int value)
    {
        value = 0;
        int start = position;
        while (position < _data.Length && IsDigit(_data[position]))
            position++;
        return position > start &&
               int.TryParse(
                   System.Text.Encoding.ASCII.GetString(_data, start, position - start),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private bool SkipRequiredWhiteSpace(ref int position)
    {
        int start = position;
        while (position < _data.Length && IsWhiteSpace(_data[position]))
            position++;
        return position > start;
    }

    private bool Matches(int position, string value) =>
        position >= 0 &&
        position <= _data.Length - value.Length &&
        _data.AsSpan(position, value.Length)
            .SequenceEqual(System.Text.Encoding.ASCII.GetBytes(value));

    private bool IsTokenStart(int position) =>
        position <= _document.HeaderOffset ||
        IsWhiteSpace(_data[position - 1]) ||
        IsDelimiter(_data[position - 1]);

    private bool IsTokenEnd(int position) =>
        position >= _data.Length ||
        IsWhiteSpace(_data[position]) ||
        IsDelimiter(_data[position]);

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';

    private static bool IsDelimiter(byte value) =>
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
            (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or
            (byte)'/' or (byte)'%';
}
