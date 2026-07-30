using System.Globalization;
using System.Text;
using Poppler.Core.Filters;
using Poppler.Security;
using Poppler.Text;

namespace Poppler.Core;

internal sealed class PdfDocumentCore : IDisposable
{
    private readonly byte[] _data;
    private readonly PdfReadOptions _options;
    private readonly Dictionary<PdfReference, PdfObject> _objectCache = new();
    private readonly HashSet<PdfReference> _resolving = new();
    private readonly Dictionary<PdfReference, Lazy<byte[]>> _decodedStreamCache = new();
    private readonly List<PdfDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _reportedDiagnostics = new(StringComparer.Ordinal);
    private readonly object _resolutionSync = new();
    private readonly object _decodedStreamSync = new();
    private readonly object _diagnosticSync = new();
    private readonly PdfCrossReference _crossReference;
    private readonly Lazy<PdfCMapResolver> _cMapResolver;
    private PdfStandardSecurityHandler? _securityHandler;
    private PdfReference? _encryptionReference;
    private long _cachedDecodedBytes;
    private const int MaximumDecodedStreamCacheEntries = 4096;

    public PdfDocumentCore(
        byte[] data,
        PdfReadOptions options,
        string ownerPassword = "",
        string userPassword = "")
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cMapResolver = new Lazy<PdfCMapResolver>(() => new PdfCMapResolver(this));
        ArgumentNullException.ThrowIfNull(ownerPassword);
        ArgumentNullException.ThrowIfNull(userPassword);
        (PdfVersion, HeaderOffset) = ReadHeader(data);
        if (HeaderOffset > 0)
        {
            AddDiagnostic(
                PdfDiagnosticSeverity.Warning,
                "header.prefix",
                $"Skipped {HeaderOffset} byte(s) before the PDF header.",
                HeaderOffset);
        }
        if (!HasEndOfFileMarker(data))
        {
            AddDiagnostic(
                PdfDiagnosticSeverity.Warning,
                "eof.missing",
                "The final PDF end-of-file marker is missing.");
        }
        _crossReference = new PdfCrossReference(this, data, options);
        _crossReference.Load();
        InitializeSecurity(ownerPassword, userPassword);
    }

    public string PdfVersion { get; }
    public int HeaderOffset { get; }
    public PdfReadOptions Options => _options;
    public PdfDictionary Trailer => _crossReference.Trailer;
    public bool XrefWasRepaired => _crossReference.WasRepaired;
    public IReadOnlyList<PdfDiagnostic> Diagnostics
    {
        get
        {
            lock (_diagnosticSync)
                return _diagnostics.ToArray();
        }
    }
    public ReadOnlyMemory<byte> OriginalBytes => _data;
    public bool IsEncrypted => _securityHandler is not null;
    public bool IsLocked => _securityHandler?.IsLocked == true;
    public Permission Permissions => _securityHandler?.Permissions ?? Permission.All;
    public PdfPasswordKind PasswordKind =>
        _securityHandler?.PasswordKind ?? PdfPasswordKind.None;
    public PdfEncryptionInfo? EncryptionInfo => _securityHandler?.EncryptionInfo;
    internal PdfCMapResolver CMapResolver => _cMapResolver.Value;

    public PdfObject Resolve(PdfReference reference)
    {
        lock (_resolutionSync)
        {
            if (_objectCache.TryGetValue(reference, out PdfObject? cached))
                return cached;
            if (!_resolving.Add(reference))
                throw new PdfFormatException($"Circular indirect object reference at {reference}.");

            try
            {
                if (!_crossReference.Entries.TryGetValue(
                        reference.ObjectNumber,
                        out PdfXrefEntry? entry) ||
                    entry.Type == PdfXrefEntryType.Free)
                {
                    throw new PdfFormatException($"Indirect object {reference} is missing.");
                }
                int expectedGeneration =
                    entry.Type == PdfXrefEntryType.Compressed ? 0 : entry.Generation;
                if (reference.Generation != expectedGeneration)
                {
                    throw new PdfFormatException(
                        $"Indirect object {reference.ObjectNumber} has generation " +
                        $"{expectedGeneration}, not {reference.Generation}.");
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
    }

    public byte[] Decode(PdfStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PdfReference? reference = stream.SourceReference;
        if (reference is null || _options.MaximumCachedDecodedBytes == 0)
            return PdfFilterPipeline.Decode(stream, this, _options);

        Lazy<byte[]>? cached = null;
        bool cacheCapacityReached = false;
        lock (_decodedStreamSync)
        {
            if (!_decodedStreamCache.TryGetValue(reference, out cached))
            {
                if (_decodedStreamCache.Count >= MaximumDecodedStreamCacheEntries)
                {
                    cacheCapacityReached = true;
                }
                else
                {
                    cached = new Lazy<byte[]>(
                        () => DecodeAndAccount(stream, reference),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    _decodedStreamCache.Add(reference, cached);
                }
            }
        }
        if (cacheCapacityReached)
            return PdfFilterPipeline.Decode(stream, this, _options);

        try
        {
            return cached!.Value;
        }
        catch
        {
            lock (_decodedStreamSync)
            {
                if (_decodedStreamCache.TryGetValue(reference, out Lazy<byte[]>? current) &&
                    ReferenceEquals(current, cached))
                {
                    _decodedStreamCache.Remove(reference);
                }
            }

            throw;
        }
    }

    public byte[] DecryptExplicitStream(
        PdfStream stream,
        ReadOnlySpan<byte> input,
        string cryptFilterName)
    {
        if (cryptFilterName == "Identity")
            return input.ToArray();
        return _securityHandler?.DecryptExplicitStream(stream, input, cryptFilterName) ??
               throw new PdfFormatException(
                   $"Stream uses crypt filter /{cryptFilterName} without an encryption dictionary.");
    }

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
        long? offset = null)
    {
        lock (_diagnosticSync)
            _diagnostics.Add(new PdfDiagnostic(severity, code, message, offset));
    }

    public void AddDiagnosticOnce(
        PdfDiagnosticSeverity severity,
        string code,
        string message,
        long? offset = null)
    {
        lock (_diagnosticSync)
        {
            if (_reportedDiagnostics.Add(code))
                _diagnostics.Add(new PdfDiagnostic(severity, code, message, offset));
        }
    }

    public int ToPhysicalOffset(long logicalOffset)
    {
        long physicalOffset;
        try
        {
            physicalOffset = checked(logicalOffset + HeaderOffset);
        }
        catch (OverflowException exception)
        {
            throw new PdfFormatException("PDF offset overflow.", exception);
        }

        if (physicalOffset < 0 || physicalOffset > int.MaxValue)
            throw new PdfFormatException($"PDF offset {logicalOffset} is outside the supported range.");
        return (int)physicalOffset;
    }

    public long ToLogicalOffset(int physicalOffset) => (long)physicalOffset - HeaderOffset;

    public void ResetResolutionCache()
    {
        lock (_resolutionSync)
        {
            _objectCache.Clear();
            _resolving.Clear();
        }
        lock (_decodedStreamSync)
        {
            _decodedStreamCache.Clear();
            _cachedDecodedBytes = 0;
        }
    }

    public void Dispose()
    {
        _securityHandler?.Dispose();
        lock (_decodedStreamSync)
        {
            _decodedStreamCache.Clear();
            _cachedDecodedBytes = 0;
        }
    }

    private byte[] DecodeAndAccount(PdfStream stream, PdfReference reference)
    {
        byte[] decoded = PdfFilterPipeline.Decode(stream, this, _options);
        lock (_decodedStreamSync)
        {
            long remaining = _options.MaximumCachedDecodedBytes - _cachedDecodedBytes;
            if (decoded.Length <= remaining)
            {
                _cachedDecodedBytes += decoded.Length;
            }
            else
            {
                _decodedStreamCache.Remove(reference);
            }
        }

        return decoded;
    }

    private PdfObject ReadUncompressed(PdfReference requested, PdfXrefEntry entry)
    {
        int offset = ToPhysicalOffset(entry.Field1);
        if (offset < HeaderOffset || offset >= _data.Length)
            throw new PdfFormatException($"Object {requested} has an invalid xref offset.");
        var reader = new PdfSyntaxReader(_data, offset, _data.Length - offset, _options);
        PdfIndirectObject indirect = reader.ReadIndirectObject(ResolveLength);
        if (indirect.ObjectNumber != requested.ObjectNumber ||
            indirect.Generation != requested.Generation)
        {
            throw new PdfFormatException(
                $"Xref for {requested} points to object {indirect.ObjectNumber} {indirect.Generation}.");
        }

        if (_securityHandler is not null &&
            !_securityHandler.IsLocked &&
            !requested.Equals(_encryptionReference))
        {
            return _securityHandler.DecryptObject(indirect.Value, requested);
        }

        return indirect.Value is PdfStream stream
            ? new PdfStream(stream.Dictionary, stream.EncodedBytes.Span, requested)
            : indirect.Value;
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
        if (count < 0 ||
            count > _options.MaximumObjects ||
            first < 0 ||
            entry.Field2 < 0 ||
            entry.Field2 >= count)
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

        if (items[entry.Field2].ObjectNumber != requested.ObjectNumber)
        {
            throw new PdfFormatException(
                $"Object stream index {entry.Field2} does not point to {requested.ObjectNumber}.");
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

    private static (string Version, int Offset) ReadHeader(byte[] data)
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
        return (version, marker);
    }

    private static bool HasEndOfFileMarker(byte[] data)
    {
        int searchStart = Math.Max(0, data.Length - 4096);
        return data.AsSpan(searchStart).LastIndexOf("%%EOF"u8) >= 0;
    }

    private void InitializeSecurity(string ownerPassword, string userPassword)
    {
        PdfObject? encryption = Trailer.GetValueOrNull("Encrypt");
        if (encryption is null)
            return;

        _encryptionReference = encryption as PdfReference;
        PdfDictionary dictionary = encryption.AsDictionary(this) ??
            throw new PdfFormatException("Trailer /Encrypt is not a dictionary.");
        _securityHandler = new PdfStandardSecurityHandler(this, dictionary);
        bool authenticated = _securityHandler.Authenticate(ownerPassword, userPassword);
        ResetResolutionCache();
        if (!authenticated)
        {
            AddDiagnostic(
                PdfDiagnosticSeverity.Warning,
                "security.locked",
                "The PDF is encrypted and the supplied passwords did not unlock it.");
        }
    }
}
