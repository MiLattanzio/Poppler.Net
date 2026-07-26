using System.Text;
using Poppler.Text;

namespace Poppler.Rendering;

/// <summary>
/// Deterministic managed font-file substitution. Discovery is platform aware,
/// but outline parsing and rasterization never call a native font service.
/// </summary>
internal sealed class PdfFontSubstitutionResolver
{
    private const int MaximumFontFiles = 20_000;
    private const int MaximumFontBytes = 32 * 1024 * 1024;

    private readonly RasterRenderOptions _options;
    private readonly Dictionary<string, SubstituteFont?> _cache =
        new(StringComparer.Ordinal);
    private string[]? _fontFiles;

    public PdfFontSubstitutionResolver(RasterRenderOptions options)
        => _options = options;

    public bool TryGetGlyph(
        string pdfFontName,
        Rune rune,
        out PdfGraphicsPath path,
        out double advance)
    {
        path = new PdfGraphicsPath(Array.Empty<PdfPathSegment>());
        advance = 0;
        if (!_options.UseFontSubstitution || Rune.IsWhiteSpace(rune))
            return false;
        string key = NormalizePdfFontName(pdfFontName);
        if (!_cache.TryGetValue(key, out SubstituteFont? font))
        {
            font = Resolve(key);
            _cache[key] = font;
        }
        if (font is null ||
            !font.Cmap.TryGetGlyph(rune.Value, out uint glyph))
        {
            return false;
        }

        if (font.TrueType?.TryGetGlyph(glyph, out path, out advance) == true)
            return true;
        return font.Cff?.TryGetGlyph(glyph, out path, out advance) == true;
    }

    private SubstituteFont? Resolve(string pdfFontName)
    {
        string[] files = _fontFiles ??= DiscoverFontFiles();
        foreach (string file in files
                     .Select(path => (
                         Path: path,
                         Score: Score(path, pdfFontName) +
                                (IsConfiguredFont(path) ? 1000 : 0)))
                     .OrderByDescending(candidate => candidate.Score)
                     .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
                     .Take(64)
                     .Select(candidate => candidate.Path))
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists ||
                    info.Length is <= 0 or > MaximumFontBytes)
                {
                    continue;
                }
                byte[] bytes = File.ReadAllBytes(file);
                PdfOpenTypeCmap? cmap =
                    PdfOpenTypeCmap.TryParse(bytes, maximumMappings: 1_000_000);
                if (cmap is null)
                    continue;
                PdfTrueTypeFont? trueType = PdfTrueTypeFont.TryParse(bytes);
                PdfCffFont? cff = trueType is null
                    ? PdfCffFont.TryParse(bytes)
                    : null;
                if (trueType is not null || cff is not null)
                    return new SubstituteFont(cmap, trueType, cff);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                // A broken or inaccessible candidate does not disable the
                // remaining managed substitution candidates.
            }
        }

        return null;
    }

    private bool IsConfiguredFont(string path)
    {
        foreach (string directory in _options.FontDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            try
            {
                string root = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (path.StartsWith(root, StringComparison.Ordinal))
                    return true;
            }
            catch (ArgumentException)
            {
                // Invalid optional roots are ignored by discovery as well.
            }
        }

        return false;
    }

    private string[] DiscoverFontFiles()
    {
        var roots = new List<string>();
        roots.AddRange(_options.FontDirectories.Where(
            directory => !string.IsNullOrWhiteSpace(directory)));
        roots.AddRange(DefaultFontDirectories());
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            if (files.Count >= MaximumFontFiles)
                break;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string file in Directory.EnumerateFiles(
                             root,
                             "*",
                             SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath))
                        files.Add(fullPath);
                    if (files.Count >= MaximumFontFiles)
                        break;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                // Continue with the next configured root.
            }
        }
        return files.ToArray();
    }

    private static IEnumerable<string> DefaultFontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            string windows = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows))
                yield return Path.Combine(windows, "Fonts");
            yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            string user = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(user))
                yield return Path.Combine(user, "Library", "Fonts");
            yield break;
        }

        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        string profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            yield return Path.Combine(profile, ".fonts");
            yield return Path.Combine(profile, ".local", "share", "fonts");
        }
    }

    private static int Score(string path, string requested)
    {
        string name = Path.GetFileNameWithoutExtension(path)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        bool bold = requested.Contains("bold", StringComparison.Ordinal);
        bool italic =
            requested.Contains("italic", StringComparison.Ordinal) ||
            requested.Contains("oblique", StringComparison.Ordinal);
        bool mono =
            requested.Contains("courier", StringComparison.Ordinal) ||
            requested.Contains("mono", StringComparison.Ordinal);
        bool serif =
            requested.Contains("times", StringComparison.Ordinal) ||
            requested.Contains("serif", StringComparison.Ordinal) ||
            requested.Contains("roman", StringComparison.Ordinal);
        int score = 0;
        if (mono)
        {
            if (name.Contains("mono", StringComparison.Ordinal) ||
                name.Contains("courier", StringComparison.Ordinal))
                score += 100;
        }
        else if (serif)
        {
            if (name.Contains("serif", StringComparison.Ordinal) ||
                name.Contains("times", StringComparison.Ordinal) ||
                name.Contains("roman", StringComparison.Ordinal))
                score += 100;
        }
        else if (name.Contains("sans", StringComparison.Ordinal) ||
                 name.Contains("arial", StringComparison.Ordinal) ||
                 name.Contains("helvetica", StringComparison.Ordinal))
        {
            score += 100;
        }

        bool candidateBold = name.Contains("bold", StringComparison.Ordinal);
        bool candidateItalic =
            name.Contains("italic", StringComparison.Ordinal) ||
            name.Contains("oblique", StringComparison.Ordinal);
        score += candidateBold == bold ? 20 : -10;
        score += candidateItalic == italic ? 20 : -10;
        if (name.Contains("dejavu", StringComparison.Ordinal) ||
            name.Contains("liberation", StringComparison.Ordinal) ||
            name.Contains("nimbus", StringComparison.Ordinal))
        {
            score += 5;
        }
        return score;
    }

    private static string NormalizePdfFontName(string name)
    {
        int subset = name.IndexOf('+');
        if (subset == 6)
            name = name[7..];
        return name
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private sealed record SubstituteFont(
        PdfOpenTypeCmap Cmap,
        PdfTrueTypeFont? TrueType,
        PdfCffFont? Cff);
}
