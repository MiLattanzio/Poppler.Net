using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Poppler.Net.PackageVerifier;

internal static partial class Program
{
    private const string RepositoryUrl =
        "https://github.com/MiLattanzio/Poppler.Net";

    private static readonly IReadOnlyDictionary<string, string> Dependencies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CoreJ2K"] = "2.3.3.91",
            ["JBig2Decoder.NETStandard"] = "1.5.2",
            ["StbImageSharp"] = "2.30.15"
        };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                throw new ArgumentException(
                    "Usage: Poppler.Net.PackageVerifier <package.nupkg> <expected-version>");
            }

            Verify(Path.GetFullPath(args[0]), args[1]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Package verification failed: {exception.Message}");
            return 1;
        }
    }

    private static void Verify(string path, string expectedVersion)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("NuGet package was not found.", path);
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new ArgumentException("Expected version cannot be empty.");

        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<string, ZipArchiveEntry> entries = archive.Entries
            .ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        string[] requiredEntries =
        [
            "_rels/.rels",
            "Poppler.Net.nuspec",
            "lib/net8.0/Poppler.Net.dll",
            "lib/net8.0/Poppler.Net.xml",
            "lib/net10.0/Poppler.Net.dll",
            "lib/net10.0/Poppler.Net.xml",
            "README.md",
            "RELEASE_NOTES.md",
            "LICENSE",
            "NOTICE.md",
            "[Content_Types].xml"
        ];
        foreach (string required in requiredEntries)
        {
            if (!entries.ContainsKey(required))
                throw new InvalidDataException($"Required package entry '{required}' is missing.");
        }
        int coreProperties = archive.Entries.Count(entry =>
            entry.FullName.StartsWith(
                "package/services/metadata/core-properties/",
                StringComparison.Ordinal));
        if (coreProperties != 1)
        {
            throw new InvalidDataException(
                $"Expected one package core-properties entry, found {coreProperties}.");
        }

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;
            if (name.Contains("..", StringComparison.Ordinal) ||
                name.StartsWith("/", StringComparison.Ordinal) ||
                name.Contains('\\'))
            {
                throw new InvalidDataException($"Unsafe package entry '{name}'.");
            }
            if (!requiredEntries.Contains(name, StringComparer.Ordinal) &&
                !name.StartsWith(
                    "package/services/metadata/core-properties/",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected package entry '{name}'.");
            }
            if (NativeExtension().IsMatch(name) &&
                name != "lib/net8.0/Poppler.Net.dll" &&
                name != "lib/net10.0/Poppler.Net.dll")
                throw new InvalidDataException($"Native package entry '{name}' is forbidden.");
        }

        XDocument nuspec = ReadXml(entries["Poppler.Net.nuspec"]);
        XNamespace ns = nuspec.Root?.Name.Namespace ??
            throw new InvalidDataException("NuSpec root is missing.");
        XElement metadata = nuspec.Root?.Element(ns + "metadata") ??
            throw new InvalidDataException("NuSpec metadata is missing.");
        RequireValue(metadata, ns, "id", "Poppler.Net");
        RequireValue(metadata, ns, "version", expectedVersion);
        RequireValue(metadata, ns, "authors", "Mi Lattanzio");
        RequireValue(metadata, ns, "readme", "README.md");
        RequireValue(metadata, ns, "projectUrl", RepositoryUrl);

        XElement license = metadata.Element(ns + "license") ??
            throw new InvalidDataException("NuSpec license is missing.");
        if ((string?)license.Attribute("type") != "expression" ||
            license.Value != "GPL-2.0-or-later")
        {
            throw new InvalidDataException("NuSpec license metadata is not GPL-2.0-or-later.");
        }

        XElement repository = metadata.Element(ns + "repository") ??
            throw new InvalidDataException("NuSpec repository metadata is missing.");
        string commit = (string?)repository.Attribute("commit") ?? "";
        if ((string?)repository.Attribute("type") != "git" ||
            (string?)repository.Attribute("url") != RepositoryUrl ||
            !GitCommit().IsMatch(commit))
        {
            throw new InvalidDataException("NuSpec repository metadata is incomplete.");
        }

        XElement[] groups = metadata
            .Element(ns + "dependencies")?
            .Elements(ns + "group")
            .ToArray() ?? [];
        string[] targetFrameworks = ["net8.0", "net10.0"];
        if (groups.Length != targetFrameworks.Length)
            throw new InvalidDataException("NuSpec dependency group count changed.");
        foreach (string targetFramework in targetFrameworks)
        {
            XElement group = groups.SingleOrDefault(candidate =>
                (string?)candidate.Attribute("targetFramework") == targetFramework) ??
                throw new InvalidDataException(
                    $"NuSpec dependency target '{targetFramework}' is missing or ambiguous.");
            Dictionary<string, string> actualDependencies = group
                .Elements(ns + "dependency")
                .ToDictionary(
                    element => (string?)element.Attribute("id") ?? "",
                    element => (string?)element.Attribute("version") ?? "",
                    StringComparer.Ordinal);
            if (actualDependencies.Count != Dependencies.Count ||
                Dependencies.Any(expected =>
                    !actualDependencies.TryGetValue(expected.Key, out string? version) ||
                    version != expected.Value))
            {
                throw new InvalidDataException(
                    $"NuSpec runtime dependency set changed for {targetFramework}.");
            }
            if (group.Elements(ns + "dependency").Any(element =>
                    (string?)element.Attribute("exclude") != "Build,Analyzers"))
            {
                throw new InvalidDataException(
                    "NuSpec dependencies must exclude build and analyzer assets.");
            }
        }

        RequireText(entries["README.md"], "Poppler.Net");
        RequireText(entries["RELEASE_NOTES.md"], expectedVersion);
        RequireText(entries["LICENSE"], "GNU GENERAL PUBLIC LICENSE");
        RequireText(entries["NOTICE.md"], "poppler-26.07.0.tar.xz");

        Console.WriteLine(
            $"Package {Path.GetFileName(path)} passed content, license, " +
            "dependency and metadata verification.");
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static void RequireValue(
        XElement metadata,
        XNamespace ns,
        string name,
        string expected)
    {
        string? actual = metadata.Element(ns + name)?.Value;
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"NuSpec {name} is '{actual}', expected '{expected}'.");
        }
    }

    private static void RequireText(ZipArchiveEntry entry, string expected)
    {
        using var reader = new StreamReader(entry.Open());
        string text = reader.ReadToEnd();
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package entry '{entry.FullName}' does not contain '{expected}'.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitCommit();

    [GeneratedRegex(
        @"\.(dll|so|dylib|a|lib|exe|winmd)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeExtension();
}
