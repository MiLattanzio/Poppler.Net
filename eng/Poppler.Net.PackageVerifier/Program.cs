using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
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

        if (Path.GetFileName(path).StartsWith("Poppler.Net.Cli.", StringComparison.Ordinal))
        {
            VerifyTool(path, expectedVersion);
            return;
        }

        VerifyLibrary(path, expectedVersion);
    }

    private static void VerifyLibrary(string path, string expectedVersion)
    {
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
        VerifyEmbeddedSourceLink(entries["lib/net8.0/Poppler.Net.dll"], commit);
        VerifyEmbeddedSourceLink(entries["lib/net10.0/Poppler.Net.dll"], commit);

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

    private static void VerifyTool(string path, string expectedVersion)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<string, ZipArchiveEntry> entries = archive.Entries
            .ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        string[] requiredEntries =
        [
            "_rels/.rels",
            "Poppler.Net.Cli.nuspec",
            "LICENSE",
            "NOTICE.md",
            "README.md",
            "RELEASE_NOTES.md",
            "tools/net8.0/any/CoreJ2K.dll",
            "tools/net8.0/any/DotnetToolSettings.xml",
            "tools/net8.0/any/JBig2Decoder.NETStandard.dll",
            "tools/net8.0/any/Poppler.Net.dll",
            "tools/net8.0/any/Poppler.Net.xml",
            "tools/net8.0/any/StbImageSharp.dll",
            "tools/net8.0/any/poppler-net.deps.json",
            "tools/net8.0/any/poppler-net.dll",
            "tools/net8.0/any/poppler-net.runtimeconfig.json",
            "[Content_Types].xml"
        ];
        foreach (string required in requiredEntries)
        {
            if (!entries.ContainsKey(required))
                throw new InvalidDataException($"Required tool package entry '{required}' is missing.");
        }

        ZipArchiveEntry[] coreProperties = archive.Entries
            .Where(entry => entry.FullName.StartsWith(
                "package/services/metadata/core-properties/",
                StringComparison.Ordinal))
            .ToArray();
        if (coreProperties.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected one tool package core-properties entry, found {coreProperties.Length}.");
        }
        var allowed = new HashSet<string>(requiredEntries, StringComparer.Ordinal)
        {
            coreProperties[0].FullName
        };
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!allowed.Contains(entry.FullName))
                throw new InvalidDataException($"Unexpected tool package entry '{entry.FullName}'.");
            if (entry.FullName.Contains("..", StringComparison.Ordinal) ||
                entry.FullName.StartsWith("/", StringComparison.Ordinal) ||
                entry.FullName.Contains('\\'))
            {
                throw new InvalidDataException($"Unsafe tool package entry '{entry.FullName}'.");
            }
        }

        XDocument nuspec = ReadXml(entries["Poppler.Net.Cli.nuspec"]);
        XNamespace ns = nuspec.Root?.Name.Namespace ??
            throw new InvalidDataException("Tool NuSpec root is missing.");
        XElement metadata = nuspec.Root?.Element(ns + "metadata") ??
            throw new InvalidDataException("Tool NuSpec metadata is missing.");
        RequireValue(metadata, ns, "id", "Poppler.Net.Cli");
        RequireValue(metadata, ns, "version", expectedVersion);
        RequireValue(metadata, ns, "authors", "Mi Lattanzio");
        RequireValue(metadata, ns, "readme", "README.md");
        RequireValue(metadata, ns, "projectUrl", RepositoryUrl);
        XElement packageType = metadata
            .Element(ns + "packageTypes")?
            .Element(ns + "packageType") ??
            throw new InvalidDataException("DotnetTool package type is missing.");
        if ((string?)packageType.Attribute("name") != "DotnetTool")
            throw new InvalidDataException("Tool package type is not DotnetTool.");
        XElement license = metadata.Element(ns + "license") ??
            throw new InvalidDataException("Tool NuSpec license is missing.");
        if ((string?)license.Attribute("type") != "expression" ||
            license.Value != "GPL-2.0-or-later")
        {
            throw new InvalidDataException("Tool NuSpec license metadata is not GPL-2.0-or-later.");
        }
        XElement repository = metadata.Element(ns + "repository") ??
            throw new InvalidDataException("Tool NuSpec repository metadata is missing.");
        string commit = (string?)repository.Attribute("commit") ?? "";
        if ((string?)repository.Attribute("type") != "git" ||
            (string?)repository.Attribute("url") != RepositoryUrl ||
            !GitCommit().IsMatch(commit))
        {
            throw new InvalidDataException("Tool NuSpec repository metadata is incomplete.");
        }
        VerifyEmbeddedSourceLink(
            entries["tools/net8.0/any/Poppler.Net.dll"],
            commit);
        VerifyEmbeddedSourceLink(
            entries["tools/net8.0/any/poppler-net.dll"],
            commit);

        XDocument settings = ReadXml(entries["tools/net8.0/any/DotnetToolSettings.xml"]);
        XElement command = settings.Root?
            .Element("Commands")?
            .Element("Command") ??
            throw new InvalidDataException("DotnetToolSettings command is missing.");
        if ((string?)command.Attribute("Name") != "poppler-net" ||
            (string?)command.Attribute("EntryPoint") != "poppler-net.dll" ||
            (string?)command.Attribute("Runner") != "dotnet")
        {
            throw new InvalidDataException("DotnetToolSettings command metadata changed.");
        }
        RequireText(entries["tools/net8.0/any/poppler-net.runtimeconfig.json"], "\"rollForward\": \"Major\"");
        RequireText(entries["README.md"], "dotnet tool install");
        RequireText(entries["RELEASE_NOTES.md"], expectedVersion);
        RequireText(entries["LICENSE"], "GNU GENERAL PUBLIC LICENSE");
        RequireText(entries["NOTICE.md"], "poppler-26.07.0.tar.xz");

        Console.WriteLine(
            $"Tool package {Path.GetFileName(path)} passed content, command, " +
            "license and metadata verification.");
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

    private static void VerifyEmbeddedSourceLink(
        ZipArchiveEntry assemblyEntry,
        string expectedCommit)
    {
        using Stream stream = assemblyEntry.Open();
        using var image = new MemoryStream();
        stream.CopyTo(image);
        image.Position = 0;
        using var peReader = new PEReader(image, PEStreamOptions.LeaveOpen);
        DebugDirectoryEntry[] embeddedEntries = peReader
            .ReadDebugDirectory()
            .Where(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            .ToArray();
        if (embeddedEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' must contain one embedded portable PDB.");
        }

        using MetadataReaderProvider provider =
            peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedEntries[0]);
        MetadataReader reader = provider.GetMetadataReader();
        var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
        CustomDebugInformationHandle sourceLink = reader
            .GetCustomDebugInformation(
                MetadataTokens.EntityHandle(TableIndex.Module, 1))
            .FirstOrDefault(handle =>
                reader.GetGuid(reader.GetCustomDebugInformation(handle).Kind) ==
                sourceLinkKind);
        if (sourceLink.IsNil)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' does not contain Source Link data.");
        }

        byte[] value = reader.GetBlobBytes(
            reader.GetCustomDebugInformation(sourceLink).Value);
        string json = Encoding.UTF8.GetString(value);
        if (!json.Contains(expectedCommit, StringComparison.OrdinalIgnoreCase) ||
            !json.Contains("MiLattanzio/Poppler.Net", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' Source Link data does not target " +
                "the NuSpec repository commit.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitCommit();

    [GeneratedRegex(
        @"\.(dll|so|dylib|a|lib|exe|winmd)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeExtension();
}
