using System.Collections.ObjectModel;
using Poppler.Core;
using Poppler.DocumentModel;

namespace Poppler;

/// <summary>Read-only managed representation of a PDF document.</summary>
public sealed class Document : IDisposable
{
    public const string PortVersion = "0.8.0-beta.2";
    public const string UpstreamVersion = "26.07.0";

    private readonly byte[] _data;
    private readonly PdfReadOptions _options;
    private PdfDocumentCore _core;
    private PdfDictionary _catalog = EmptyDictionary();
    private IReadOnlyList<PdfPageNode> _pageNodes = Array.Empty<PdfPageNode>();
    private PageLabelTree? _pageLabels;
    private Lazy<IReadOnlyDictionary<string, string>> _information =
        new(EmptyInformation);
    private Lazy<IReadOnlyList<EmbeddedFile>> _embeddedFiles =
        new(() => Array.Empty<EmbeddedFile>());
    private bool _disposed;

    private Document(
        byte[] data,
        PdfReadOptions options,
        string ownerPassword,
        string userPassword)
    {
        _data = data;
        _options = options;
        _core = new PdfDocumentCore(data, options, ownerPassword, userPassword);
        try
        {
            if (!_core.IsLocked)
                InitializeUnlockedModel();
        }
        catch
        {
            _core.Dispose();
            throw;
        }
    }

    private void InitializeUnlockedModel()
    {
        _catalog = _core.FindCatalog();
        _pageNodes = PdfPageTreeReader.Read(_core, _catalog);
        _pageLabels = new PageLabelTree(_catalog.GetValueOrNull("PageLabels"), _core);
        _information = new Lazy<IReadOnlyDictionary<string, string>>(ReadInformation);
        _embeddedFiles = new Lazy<IReadOnlyList<EmbeddedFile>>(
            () => EmbeddedFileReader.Read(_core, _catalog));
    }

    public string PdfVersion => _core.PdfVersion;
    public int Pages => _pageNodes.Count;
    public int PageCount => Pages;
    public bool IsEncrypted => _core.IsEncrypted;
    public bool IsLocked => _core.IsLocked;
    public bool IsLinearized => _core.IsLinearized();
    public bool XrefWasRepaired => _core.XrefWasRepaired;
    public IReadOnlyList<PdfDiagnostic> Diagnostics => _core.Diagnostics;
    public PdfPasswordKind PasswordKind => _core.PasswordKind;
    public PdfEncryptionInfo? EncryptionInfo => _core.EncryptionInfo;
    public IReadOnlyDictionary<string, string> Information
    {
        get
        {
            EnsureUnlocked();
            return _information.Value;
        }
    }
    public IEnumerable<string> InfoKeys => Information.Keys;
    public IReadOnlyList<EmbeddedFile> EmbeddedFiles
    {
        get
        {
            EnsureUnlocked();
            return _embeddedFiles.Value;
        }
    }
    public bool HasEmbeddedFiles => EmbeddedFiles.Count > 0;

    public string Title => GetInfo("Title");
    public string Author => GetInfo("Author");
    public string Subject => GetInfo("Subject");
    public string Keywords => GetInfo("Keywords");
    public string Creator => GetInfo("Creator");
    public string Producer => GetInfo("Producer");
    public DateTimeOffset? CreationDate => PdfDateParser.Parse(GetInfo("CreationDate"));
    public DateTimeOffset? ModificationDate => PdfDateParser.Parse(GetInfo("ModDate"));

    public PageMode PageMode
    {
        get
        {
            EnsureUnlocked();
            return _catalog.GetValueOrNull("PageMode").AsName(_core) switch
            {
                "UseOutlines" => PageMode.UseOutlines,
                "UseThumbs" => PageMode.UseThumbs,
                "FullScreen" => PageMode.FullScreen,
                "UseOC" => PageMode.UseOptionalContent,
                "UseAttachments" => PageMode.UseAttachments,
                _ => PageMode.UseNone
            };
        }
    }

    public PageLayout PageLayout
    {
        get
        {
            EnsureUnlocked();
            return _catalog.GetValueOrNull("PageLayout").AsName(_core) switch
            {
                "SinglePage" => PageLayout.SinglePage,
                "OneColumn" => PageLayout.OneColumn,
                "TwoColumnLeft" => PageLayout.TwoColumnLeft,
                "TwoColumnRight" => PageLayout.TwoColumnRight,
                "TwoPageLeft" => PageLayout.TwoPageLeft,
                "TwoPageRight" => PageLayout.TwoPageRight,
                _ => PageLayout.NoLayout
            };
        }
    }

    public FormType FormType
    {
        get
        {
            EnsureUnlocked();
            PdfDictionary? form = _catalog.GetValueOrNull("AcroForm").AsDictionary(_core);
            if (form is null)
                return FormType.None;
            return form.ContainsKey("XFA") ? FormType.Xfa : FormType.AcroForm;
        }
    }

    public bool HasJavaScript
    {
        get
        {
            EnsureUnlocked();
            PdfDictionary? names = _catalog.GetValueOrNull("Names").AsDictionary(_core);
            if (names?.ContainsKey("JavaScript") == true)
                return true;
            PdfDictionary? action = _catalog.GetValueOrNull("OpenAction").AsDictionary(_core);
            return action?.GetValueOrNull("S").AsName(_core) == "JavaScript";
        }
    }

    public string Metadata
    {
        get
        {
            EnsureNotDisposed();
            EnsureUnlocked();
            PdfStream? metadata = _catalog.GetValueOrNull("Metadata").AsStream(_core);
            return metadata is null
                ? ""
                : System.Text.Encoding.UTF8.GetString(_core.Decode(metadata));
        }
    }

    public (string PermanentId, string UpdateId)? PdfId
    {
        get
        {
            PdfArray? identifiers = _core.Trailer.GetValueOrNull("ID").AsArray(_core);
            if (identifiers is null || identifiers.Count < 2 ||
                identifiers[0].Resolve(_core) is not PdfString permanent ||
                identifiers[1].Resolve(_core) is not PdfString update)
            {
                return null;
            }

            return (
                Convert.ToHexString(permanent.Bytes.Span),
                Convert.ToHexString(update.Bytes.Span));
        }
    }

    public Permission Permissions => _core.Permissions;

    public static Document LoadFromFile(
        string fileName,
        string ownerPassword = "",
        string userPassword = "",
        PdfReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        PdfReadOptions effectiveOptions = options ?? PdfReadOptions.Default;
        effectiveOptions.Validate();
        var info = new FileInfo(fileName);
        if (!info.Exists)
            throw new FileNotFoundException("PDF file was not found.", fileName);
        if (info.Length > effectiveOptions.MaximumInputBytes)
        {
            throw new PdfLimitException(
                $"Input is {info.Length} bytes; limit is {effectiveOptions.MaximumInputBytes} bytes.");
        }

        return new Document(
            File.ReadAllBytes(fileName),
            effectiveOptions,
            ownerPassword,
            userPassword);
    }

    public static Document LoadFromData(
        ReadOnlyMemory<byte> data,
        string ownerPassword = "",
        string userPassword = "",
        PdfReadOptions? options = null)
    {
        PdfReadOptions effectiveOptions = options ?? PdfReadOptions.Default;
        effectiveOptions.Validate();
        if (data.Length > effectiveOptions.MaximumInputBytes)
        {
            throw new PdfLimitException(
                $"Input is {data.Length} bytes; limit is {effectiveOptions.MaximumInputBytes} bytes.");
        }

        return new Document(data.ToArray(), effectiveOptions, ownerPassword, userPassword);
    }

    public static Document LoadFromStream(
        Stream stream,
        string ownerPassword = "",
        string userPassword = "",
        PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PdfReadOptions effectiveOptions = options ?? PdfReadOptions.Default;
        effectiveOptions.Validate();
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > effectiveOptions.MaximumInputBytes)
                throw new PdfLimitException("Input stream exceeds the configured limit.");
            output.Write(buffer, 0, read);
        }

        return new Document(output.ToArray(), effectiveOptions, ownerPassword, userPassword);
    }

    public Page CreatePage(int index)
    {
        EnsureNotDisposed();
        EnsureUnlocked();
        if ((uint)index >= (uint)_pageNodes.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new Page(this, _core, _pageNodes[index], index, _pageLabels!.GetLabel(index));
    }

    public Page CreatePage(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        EnsureUnlocked();
        for (int index = 0; index < Pages; index++)
        {
            if (string.Equals(_pageLabels!.GetLabel(index), label, StringComparison.Ordinal))
                return CreatePage(index);
        }

        throw new ArgumentException($"No page has label '{label}'.", nameof(label));
    }

    public string InfoKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return GetInfo(key);
    }

    public DateTimeOffset? InfoDate(string key) => PdfDateParser.Parse(InfoKey(key));

    public bool HasPermission(Permission permission) => (Permissions & permission) == permission;

    /// <summary>
    /// Attempts to unlock the document and returns its new locking status:
    /// <see langword="false"/> means the document is unlocked.
    /// </summary>
    public bool Unlock(string ownerPassword, string userPassword)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(ownerPassword);
        ArgumentNullException.ThrowIfNull(userPassword);
        if (!IsLocked)
            return false;

        var candidate = new PdfDocumentCore(_data, _options, ownerPassword, userPassword);
        if (candidate.IsLocked)
        {
            candidate.Dispose();
            return true;
        }

        PdfDictionary candidateCatalog;
        IReadOnlyList<PdfPageNode> candidatePageNodes;
        PageLabelTree candidatePageLabels;
        try
        {
            candidateCatalog = candidate.FindCatalog();
            candidatePageNodes = PdfPageTreeReader.Read(candidate, candidateCatalog);
            candidatePageLabels = new PageLabelTree(
                candidateCatalog.GetValueOrNull("PageLabels"),
                candidate);
        }
        catch
        {
            candidate.Dispose();
            throw;
        }

        _core.Dispose();
        _core = candidate;
        _catalog = candidateCatalog;
        _pageNodes = candidatePageNodes;
        _pageLabels = candidatePageLabels;
        _information = new Lazy<IReadOnlyDictionary<string, string>>(ReadInformation);
        _embeddedFiles = new Lazy<IReadOnlyList<EmbeddedFile>>(
            () => EmbeddedFileReader.Read(_core, _catalog));
        return false;
    }

    public void Save(string fileName) => SaveACopy(fileName);

    public void SaveACopy(string fileName)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        File.WriteAllBytes(fileName, _core.OriginalBytes.ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _core.Dispose();
        _disposed = true;
    }

    internal bool Locked => IsLocked;

    private IReadOnlyDictionary<string, string> ReadInformation()
    {
        PdfDictionary? dictionary = _core.Trailer.GetValueOrNull("Info").AsDictionary(_core);
        if (dictionary is null)
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, PdfObject value) in dictionary)
        {
            PdfObject resolved = value.Resolve(_core);
            result[key] = resolved switch
            {
                PdfString text => text.Text,
                PdfName name => name.Value,
                PdfNumber number => number.ToString(),
                PdfBoolean boolean => boolean.ToString(),
                _ => resolved.ToString() ?? ""
            };
        }

        return new ReadOnlyDictionary<string, string>(result);
    }

    private string GetInfo(string key) =>
        Information.TryGetValue(key, out string? value) ? value : "";

    private void EnsureUnlocked()
    {
        EnsureNotDisposed();
        if (IsLocked)
            throw new PdfEncryptedException();
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static PdfDictionary EmptyDictionary() =>
        new(new Dictionary<string, PdfObject>(StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, string> EmptyInformation() =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}
