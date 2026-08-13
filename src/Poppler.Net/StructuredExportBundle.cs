using System.Collections.ObjectModel;

namespace Poppler;

/// <summary>An immutable file produced by a structured export bundle.</summary>
public sealed class StructuredExportFile
{
    private readonly byte[] _data;

    internal StructuredExportFile(string relativePath, string mediaType, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(data);
        RelativePath = relativePath;
        MediaType = mediaType;
        _data = data.ToArray();
    }

    public string RelativePath { get; }
    public string MediaType { get; }
    public ReadOnlyMemory<byte> Data => _data;
}

/// <summary>
/// Deterministic JSON, XML, XHTML, image, and manifest files for a PDF export.
/// </summary>
public sealed class StructuredExportBundle
{
    public const string SchemaVersion = "1.0";
    private readonly IReadOnlyList<StructuredExportFile> _files;

    internal StructuredExportBundle(IEnumerable<StructuredExportFile> files)
    {
        StructuredExportFile[] materialized = files.ToArray();
        string[] required = ["document.json", "document.xml", "document.xhtml", "manifest.json"];
        if (required.Any(path => materialized.All(file => file.RelativePath != path)))
        {
            throw new ArgumentException(
                "A structured bundle must contain JSON, XML, XHTML, and its manifest.",
                nameof(files));
        }
        if (materialized
            .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Structured bundle paths must be unique.", nameof(files));
        }

        _files = new ReadOnlyCollection<StructuredExportFile>(materialized);
    }

    public string ManifestPath => "manifest.json";
    public IReadOnlyList<StructuredExportFile> Files => _files;

    /// <summary>Writes every bundle file below the destination directory.</summary>
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

        foreach (StructuredExportFile file in _files)
        {
            string relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(rootPrefix, comparison))
            {
                throw new InvalidDataException(
                    $"Structured bundle path '{file.RelativePath}' escapes the destination root.");
            }

            string? parent = Path.GetDirectoryName(destination);
            if (parent is not null)
                Directory.CreateDirectory(parent);
            File.WriteAllBytes(destination, file.Data.ToArray());
        }
    }
}
