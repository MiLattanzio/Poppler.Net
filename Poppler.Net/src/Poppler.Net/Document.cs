using System.Collections.ObjectModel;
using Poppler.Core;
using Poppler.DocumentModel;

namespace Poppler;

/// <summary>Read-only managed representation of a PDF document.</summary>
public sealed class Document : IDisposable
{
    public const string PortVersion = "0.2.0-alpha.1";
    public const string UpstreamVersion = "26.07.0";

    private readonly PdfDocumentCore _core;
    private readonly PdfDictionary _catalog;
    private readonly IReadOnlyList<PdfPageNode> _pageNodes;
    private readonly PageLabelTree _pageLabels;
    private readonly Lazy<IReadOnlyDictionary<string, string>> _information;
    private readonly Lazy<IReadOnlyList<EmbeddedFile>> _embeddedFiles;
    private bool _disposed;

    private Document(byte[] data, PdfReadOptions options)
    {
        _core = new PdfDocumentCore(data, options);
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
    public bool IsEncrypted => _core.Trailer.ContainsKey("Encrypt");
    public bool IsLocked => IsEncrypted;
    public bool IsLinearized => _core.IsLinearized();
    public bool XrefWasRepaired => _core.XrefWasRepaired;
    public IReadOnlyList<PdfDiagnostic> Diagnostics => _core.Diagnostics;
    public IReadOnlyDictionary<string, string> Information => _information.Value;
    public IEnumerable<string> InfoKeys => Information.Keys;
    public IReadOnlyList<EmbeddedFile> EmbeddedFiles => _embeddedFiles.Value;
    public bool HasEmbeddedFiles => EmbeddedFiles.Count > 0;

    public string Title => GetInfo("Title");
    public string Author => GetInfo("Author");
    public string Subject => GetInfo("Subject");
    public string Keywords => GetInfo("Keywords");
    public string Creator => GetInfo("Creator");
    public string Producer => GetInfo("Producer");
    public DateTimeOffset? CreationDate => PdfDateParser.Parse(GetInfo("CreationDate"));
    public DateTimeOffset? ModificationDate => PdfDateParser.Parse(GetInfo("ModDate"));

    public PageMode PageMode => _catalog.GetValueOrNull("PageMode").AsName(_core) switch
    {
        "UseOutlines" => PageMode.UseOutlines,
        "UseThumbs" => PageMode.UseThumbs,
        "FullScreen" => PageMode.FullScreen,
        "UseOC" => PageMode.UseOptionalContent,
        "UseAttachments" => PageMode.UseAttachments,
        _ => PageMode.UseNone
    };

    public PageLayout PageLayout => _catalog.GetValueOrNull("PageLayout").AsName(_core) switch
    {
        "SinglePage" => PageLayout.SinglePage,
        "OneColumn" => PageLayout.OneColumn,
        "TwoColumnLeft" => PageLayout.TwoColumnLeft,
        "TwoColumnRight" => PageLayout.TwoColumnRight,
        "TwoPageLeft" => PageLayout.TwoPageLeft,
        "TwoPageRight" => PageLayout.TwoPageRight,
        _ => PageLayout.NoLayout
    };

    public FormType FormType
    {
        get
        {
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

    public Permission Permissions => IsEncrypted ? Permission.None : Permission.All;

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

        return new Document(File.ReadAllBytes(fileName), effectiveOptions);
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

        return new Document(data.ToArray(), effectiveOptions);
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

        return new Document(output.ToArray(), effectiveOptions);
    }

    public Page CreatePage(int index)
    {
        EnsureNotDisposed();
        if ((uint)index >= (uint)_pageNodes.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new Page(this, _core, _pageNodes[index], index, _pageLabels.GetLabel(index));
    }

    public Page CreatePage(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        for (int index = 0; index < Pages; index++)
        {
            if (string.Equals(_pageLabels.GetLabel(index), label, StringComparison.Ordinal))
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

    public bool Unlock(string ownerPassword, string userPassword) => !IsEncrypted;

    public void Save(string fileName) => SaveACopy(fileName);

    public void SaveACopy(string fileName)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        File.WriteAllBytes(fileName, _core.OriginalBytes.ToArray());
    }

    public void Dispose() => _disposed = true;

    internal bool Encrypted => IsEncrypted;

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

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
