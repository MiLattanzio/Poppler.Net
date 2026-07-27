using Poppler.Core;

namespace Poppler.Text;

/// <summary>
/// Resolves named Adobe CMap resources from explicitly configured data roots
/// and conventional system poppler-data locations. CMap files are data only;
/// parsing remains fully managed.
/// </summary>
internal sealed class PdfCMapResolver
{
    private const int MaximumIndexedFiles = 50_000;

    private readonly PdfDocumentCore _document;
    private readonly Dictionary<string, PdfCMap?> _cache =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly HashSet<PdfReference> _activeStreams = new();
    private readonly object _sync = new();
    private Dictionary<string, string>? _files;

    public PdfCMapResolver(PdfDocumentCore document)
        => _document = document;

    public PdfCMap? Resolve(string name)
    {
        lock (_sync)
            return Resolve(name, depth: 0);
    }

    public PdfCMap ParseStream(PdfStream stream)
    {
        lock (_sync)
            return ParseStream(stream, depth: 0);
    }

    private PdfCMap ParseStream(PdfStream stream, int depth)
    {
        if (depth >= _document.Options.MaximumCMapUseDepth)
            return PdfCMap.Empty(_document.Options.MaximumCMapMappings);
        PdfReference? reference = stream.SourceReference;
        if (reference is not null && !_activeStreams.Add(reference))
            return PdfCMap.Empty(_document.Options.MaximumCMapMappings);
        try
        {
            PdfCMap? dictionaryBase = ResolveUseCMap(
                stream.Dictionary.GetValueOrNull("UseCMap"),
                depth + 1);
            return PdfCMap.Parse(
                    _document.Decode(stream),
                    _document.Options.MaximumCMapMappings,
                    baseName => Resolve(baseName, depth + 1))
                .WithBase(dictionaryBase);
        }
        finally
        {
            if (reference is not null)
                _activeStreams.Remove(reference);
        }
    }

    private PdfCMap? Resolve(string name, int depth)
    {
        if (name is "Identity-H" or "Identity-V")
        {
            return PdfCMap.Identity(
                name.EndsWith("-V", StringComparison.Ordinal)
                    ? FontWritingMode.Vertical
                    : FontWritingMode.Horizontal,
                _document.Options.MaximumCMapMappings);
        }
        if (string.IsNullOrWhiteSpace(name) ||
            depth >= _document.Options.MaximumCMapUseDepth)
        {
            return null;
        }
        if (_cache.TryGetValue(name, out PdfCMap? cached))
            return cached;
        if (!_active.Add(name))
            return null;

        try
        {
            Dictionary<string, string> files = _files ??= IndexFiles();
            string key = Path.GetFileName(name);
            if (!files.TryGetValue(key, out string? path))
            {
                _cache[name] = null;
                return null;
            }

            var info = new FileInfo(path);
            if (!info.Exists ||
                info.Length is <= 0 ||
                info.Length > _document.Options.MaximumExternalCMapBytes)
            {
                _cache[name] = null;
                return null;
            }

            PdfCMap parsed = PdfCMap.Parse(
                File.ReadAllBytes(path),
                _document.Options.MaximumCMapMappings,
                baseName => Resolve(baseName, depth + 1));
            _cache[name] = parsed;
            return parsed;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            _cache[name] = null;
            return null;
        }
        finally
        {
            _active.Remove(name);
        }
    }

    private PdfCMap? ResolveUseCMap(PdfObject? value, int depth)
    {
        string? name = value.AsName(_document);
        if (name is not null)
            return Resolve(name, depth);
        return value.AsStream(_document) is { } stream
            ? ParseStream(stream, depth)
            : null;
    }

    private Dictionary<string, string> IndexFiles()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string root in Roots())
        {
            if (result.Count >= MaximumIndexedFiles)
                break;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string file in Directory.EnumerateFiles(
                                 root,
                                 "*",
                                 SearchOption.AllDirectories)
                             .OrderBy(path => path, StringComparer.Ordinal))
                {
                    result.TryAdd(Path.GetFileName(file), Path.GetFullPath(file));
                    if (result.Count >= MaximumIndexedFiles)
                        break;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                // Continue with the next user-controlled or system data root.
            }
        }
        return result;
    }

    private IEnumerable<string> Roots()
    {
        foreach (string directory in _document.Options.CMapDirectories)
        {
            if (!string.IsNullOrWhiteSpace(directory))
                yield return directory;
        }
        if (!_document.Options.UseSystemCMaps)
            yield break;
        if (OperatingSystem.IsWindows())
        {
            string common = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrEmpty(common))
                yield return Path.Combine(common, "poppler", "cMap");
            yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/share/poppler/cMap";
            yield return "/usr/local/share/poppler/cMap";
            yield break;
        }
        yield return "/usr/share/poppler/cMap";
        yield return "/usr/local/share/poppler/cMap";
    }
}
