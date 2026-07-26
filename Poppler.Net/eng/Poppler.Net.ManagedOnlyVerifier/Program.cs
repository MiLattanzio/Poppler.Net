using System.Buffers.Binary;
using System.Text.Json;
using System.Xml.Linq;

namespace Poppler.Net.ManagedOnlyVerifier;

internal static class Program
{
    private static readonly string[] ForbiddenSourceTokens =
    {
        "DllImport",
        "LibraryImport",
        "NativeLibrary",
        "UnmanagedCallersOnly",
        "System.Diagnostics.Process",
        "Process.Start"
    };

    private static readonly string[] NativeExtensions =
    {
        ".a", ".dylib", ".lib", ".node", ".o", ".obj", ".so", ".wasm"
    };

    public static int Main(string[] args)
    {
        string root = Path.GetFullPath(
            args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ??
            Directory.GetCurrentDirectory());
        bool sourceOnly = args.Contains("--source-only", StringComparer.Ordinal);
        var failures = new List<string>();

        VerifyProductionSource(root, failures);
        VerifyDirectPackages(root, failures);
        if (!sourceOnly)
            VerifyRestoredPackageGraph(root, failures);

        if (failures.Count == 0)
        {
            Console.WriteLine(
                sourceOnly
                    ? "Managed-only source and direct-package policy passed."
                    : "Managed-only source and restored NuGet graph passed.");
            return 0;
        }

        foreach (string failure in failures)
            Console.Error.WriteLine($"ERROR {failure}");
        return 1;
    }

    private static void VerifyProductionSource(string root, ICollection<string> failures)
    {
        string sourceRoot = Path.Combine(root, "src");
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            foreach (string token in ForbiddenSourceTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{Path.GetRelativePath(root, path)} contains forbidden token '{token}'.");
                }
            }
        }
    }

    private static void VerifyDirectPackages(string root, ICollection<string> failures)
    {
        string manifestPath = Path.Combine(root, "eng", "managed-packages.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var approved = manifest.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetProperty("version").GetString() ?? "",
            StringComparer.OrdinalIgnoreCase);
        var centralVersions = ReadCentralVersions(root);

        foreach (string projectPath in EnumerateBuildFiles(root))
        {
            if (IsGeneratedPath(projectPath))
                continue;
            XDocument project = XDocument.Load(projectPath);
            foreach (XElement reference in project.Descendants("PackageReference"))
            {
                string? id = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (!approved.TryGetValue(id, out string? approvedVersion))
                {
                    failures.Add(
                        $"{Path.GetRelativePath(root, projectPath)} references unreviewed package '{id}'.");
                    continue;
                }

                string? inlineVersion = reference.Attribute("Version")?.Value;
                string? actualVersion = inlineVersion;
                if (actualVersion is null)
                    centralVersions.TryGetValue(id, out actualVersion);
                if (!string.Equals(actualVersion, approvedVersion, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"Package '{id}' is approved at {approvedVersion}, but the project uses " +
                        $"'{actualVersion ?? "no pinned version"}'.");
                }
            }
        }
    }

    private static Dictionary<string, string> ReadCentralVersions(string root)
    {
        string path = Path.Combine(root, "Directory.Packages.props");
        XDocument document = XDocument.Load(path);
        return document.Descendants("PackageVersion")
            .Where(element => element.Attribute("Include") is not null)
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")?.Value ?? "",
                StringComparer.OrdinalIgnoreCase);
    }

    private static void VerifyRestoredPackageGraph(string root, ICollection<string> failures)
    {
        string[] projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .ToArray();
        var packageDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string project in projects)
        {
            string assetsPath = Path.Combine(
                Path.GetDirectoryName(project)!,
                "obj",
                "project.assets.json");
            if (!File.Exists(assetsPath))
            {
                failures.Add(
                    $"{Path.GetRelativePath(root, assetsPath)} is missing; run dotnet restore first.");
                continue;
            }

            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
            string[] packageFolders = assets.RootElement.GetProperty("packageFolders")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
            foreach (JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject())
            {
                JsonElement value = library.Value;
                if (value.GetProperty("type").GetString() != "package")
                    continue;
                string? relativePath = value.GetProperty("path").GetString();
                if (relativePath is null)
                    continue;
                string? packageDirectory = packageFolders
                    .Select(folder => Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                    .FirstOrDefault(Directory.Exists);
                if (packageDirectory is null)
                {
                    failures.Add($"Restored package directory for '{library.Name}' was not found.");
                }
                else
                {
                    packageDirectories.Add(packageDirectory);
                }
            }
        }

        foreach (string packageDirectory in packageDirectories)
        {
            foreach (string path in Directory.EnumerateFiles(
                         packageDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                string? reason = DetectNativeBinary(path);
                if (reason is not null)
                {
                    failures.Add(
                        $"NuGet asset '{path}' is not pure managed IL ({reason}).");
                }
            }
        }
    }

    private static string? DetectNativeBinary(string path)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path);
        if (NativeExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
            fileName.Contains(".so.", StringComparison.OrdinalIgnoreCase))
        {
            return $"native extension {extension}";
        }

        using FileStream stream = File.OpenRead(path);
        if (stream.Length < 4)
            return null;
        Span<byte> magic = stackalloc byte[4];
        stream.ReadExactly(magic);
        if (magic.SequenceEqual(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }))
            return "ELF header";
        uint magicValue = BinaryPrimitives.ReadUInt32BigEndian(magic);
        if (magicValue is
            0xFEEDFACE or 0xFEEDFACF or 0xCAFEBABE or 0xBEBAFECA or 0xCEFAEDFE or 0xCFFAEDFE)
            return "Mach-O header";

        if (magic[0] == 'M' && magic[1] == 'Z')
        {
            return IsPureManagedPortableExecutable(stream)
                ? null
                : "native or mixed-mode PE";
        }

        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "non-PE DLL or executable";
        }
        return null;
    }

    private static bool IsPureManagedPortableExecutable(FileStream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.Position = 0x3C;
        if (stream.Read(buffer[..4]) != 4)
            return false;
        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(buffer[..4]);
        if (peOffset < 0 || peOffset > stream.Length - 256)
            return false;

        stream.Position = peOffset;
        if (stream.Read(buffer[..4]) != 4 ||
            !buffer[..4].SequenceEqual(new byte[] { (byte)'P', (byte)'E', 0, 0 }))
        {
            return false;
        }

        stream.Position = peOffset + 20;
        if (stream.Read(buffer[..2]) != 2)
            return false;
        ushort optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]);
        long optionalHeader = peOffset + 24L;
        if (optionalHeader + optionalHeaderSize > stream.Length)
            return false;

        stream.Position = optionalHeader;
        stream.ReadExactly(buffer[..2]);
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]);
        int dataDirectoryOffset = magic switch
        {
            0x10B => 96,
            0x20B => 112,
            _ => -1
        };
        if (dataDirectoryOffset < 0 ||
            dataDirectoryOffset + (15 * 8) > optionalHeaderSize)
        {
            return false;
        }

        stream.Position = optionalHeader + dataDirectoryOffset + (14 * 8);
        stream.ReadExactly(buffer);
        uint cliRva = BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]);
        uint cliSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        if (cliRva == 0 || cliSize < 72)
            return false;

        long sectionTable = optionalHeader + optionalHeaderSize;
        stream.Position = peOffset + 6;
        stream.ReadExactly(buffer[..2]);
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]);
        long? cliOffset = null;
        for (int section = 0; section < sectionCount; section++)
        {
            stream.Position = sectionTable + (section * 40L) + 8;
            Span<byte> sectionData = stackalloc byte[16];
            stream.ReadExactly(sectionData);
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(sectionData[..4]);
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(sectionData[4..8]);
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(sectionData[8..12]);
            uint rawAddress = BinaryPrimitives.ReadUInt32LittleEndian(sectionData[12..]);
            uint mappedSize = Math.Max(virtualSize, rawSize);
            if (cliRva >= virtualAddress && cliRva - virtualAddress < mappedSize)
            {
                cliOffset = rawAddress + (cliRva - virtualAddress);
                break;
            }
        }

        if (cliOffset is null || cliOffset > stream.Length - 72)
            return false;
        stream.Position = cliOffset.Value + 16;
        stream.ReadExactly(buffer[..4]);
        uint corFlags = BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]);
        stream.Position = cliOffset.Value + 64;
        stream.ReadExactly(buffer);
        uint managedNativeHeaderRva = BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]);
        uint managedNativeHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        const uint ilOnly = 0x00000001;
        return (corFlags & ilOnly) != 0 &&
               managedNativeHeaderRva == 0 &&
               managedNativeHeaderSize == 0;
    }

    private static bool IsGeneratedPath(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "obj" or "bin");

    private static IEnumerable<string> EnumerateBuildFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path =>
                Path.GetExtension(path) is ".csproj" or ".props" or ".targets")
            .Where(path => !IsGeneratedPath(path));
}
