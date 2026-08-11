using System.Collections.ObjectModel;

namespace Poppler;

/// <summary>An immutable file produced by a multi-file HTML export.</summary>
public sealed class HtmlExportFile
{
    private readonly byte[] _data;

    internal HtmlExportFile(string relativePath, string mediaType, byte[] data)
    {
        RelativePath = relativePath;
        MediaType = mediaType;
        _data = data.ToArray();
    }

    public string RelativePath { get; }
    public string MediaType { get; }
    public ReadOnlyMemory<byte> Data => _data;
}

/// <summary>
/// Self-contained description of a directory HTML export. The entry point is
/// always <c>index.html</c> and every path is relative to the destination root.
/// </summary>
public sealed class HtmlExportBundle
{
    private readonly IReadOnlyList<HtmlExportFile> _files;

    internal HtmlExportBundle(IEnumerable<HtmlExportFile> files)
    {
        HtmlExportFile[] materialized = files.ToArray();
        if (materialized.Length == 0 ||
            materialized.All(file => file.RelativePath != EntryPoint))
        {
            throw new ArgumentException("The HTML bundle must contain index.html.", nameof(files));
        }

        _files = new ReadOnlyCollection<HtmlExportFile>(materialized);
    }

    public string EntryPoint => "index.html";
    public IReadOnlyList<HtmlExportFile> Files => _files;

    /// <summary>Writes the complete bundle below <paramref name="directory"/>.</summary>
    public void SaveToDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        string rootPrefix = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (HtmlExportFile file in _files)
        {
            string relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(rootPrefix, comparison))
            {
                throw new InvalidDataException(
                    $"HTML bundle path '{file.RelativePath}' escapes the destination root.");
            }

            string? parent = Path.GetDirectoryName(destination);
            if (parent is not null)
                Directory.CreateDirectory(parent);
            File.WriteAllBytes(destination, file.Data.ToArray());
        }
    }
}
