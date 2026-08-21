using System.Diagnostics;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class ReleaseCandidateTests
{
    private const string FrozenPublicApiSha256 =
        "62051a5542175bf1a0987d592745dc0a822e3e03e319180629095db343344f0e";
    private const string FrozenCallableApiSha256 =
        "082e5c6049186507381f039a20299754104b3f9c9cc5338a5619aab1e20ea52a";
    private const string FrozenOptionDefaultsSha256 =
        "bebb562cea90592ee86bf2114893a1264a030c48ec295e38a1c14dbddb1bd3e2";
    private const string FrozenSchemasSha256 =
        "5b7efeb4e294e2ce9aef1193245808629bf30652f6ec3bf42119f09c95561035";
    private const string FrozenManifestContractsSha256 =
        "f417311ed5fa87cb034f07b9f06eebc458d03dad780689da744d87be87ba4b3b";
    private const string FrozenCliHelpSha256 =
        "b1371a275a4ad3add72bd1543643c8c1b3dbcd4d6b5b8c75fad70a7f9a16363b";

    [Test]
    public async Task ConcurrentReadsFromOneDocumentAreDeterministic()
    {
        byte[] source = PdfFixtures.Create(compressContent: true);
        using Document document = Document.LoadFromData(source);
        string expected = ReadAndRender(document);

        Task<string>[] operations = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => ReadAndRender(document)))
            .ToArray();
        string[] results = await Task.WhenAll(operations);

        Assert.That(results, Has.All.EqualTo(expected));
        Assert.That(
            document.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray(),
            Is.Empty);
    }

    [Test]
    public async Task EmbeddedFileDataInitializesOnceUnderConcurrentReads()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        EmbeddedFile file = document.EmbeddedFiles.Single();

        Task<string>[] operations = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(
                () => Convert.ToHexString(SHA256.HashData(file.Data.Span))))
            .ToArray();
        string[] hashes = await Task.WhenAll(operations);

        Assert.That(hashes, Has.All.EqualTo(hashes[0]));
        Assert.That(file.Size, Is.EqualTo("attachment payload".Length));
    }

    [Test]
    public void ReadOptionsAreSnapshottedAtDocumentLoad()
    {
        string fixtures = FixtureDirectory();
        var directories = new List<string>
        {
            Path.Combine(fixtures, "cmaps")
        };
        var options = new PdfReadOptions
        {
            UseSystemCMaps = false,
            CMapDirectories = directories
        };
        using Document document = Document.LoadFromFile(
            Path.Combine(fixtures, "rendering-beta1.pdf"),
            options: options);

        directories.Clear();
        directories.Add(Path.Combine(fixtures, "missing"));

        Assert.That(
            document.CreatePage(1).Text(layout: TextLayout.RawOrder),
            Is.EqualTo("AB"));
    }

    [Test]
    public void LoadFromDataOwnsItsInputBytes()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        byte[] expected = source.ToArray();
        using Document document = Document.LoadFromData(source);

        Array.Fill(source, (byte)0);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(document.Title, Is.EqualTo("Managed fixture"));
            Assert.That(
                document.CreatePage(0).Text(layout: TextLayout.RawOrder),
                Does.Contain("Hello managed PDF"));
        }));

        string path = Path.Combine(
            Path.GetTempPath(),
            $"poppler-net-owned-input-{Guid.NewGuid():N}.pdf");
        try
        {
            document.SaveACopy(path);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    [NonParallelizable]
    public void StructuredOutputIsIndependentOfCurrentCulture()
    {
        byte[] source = PdfFixtures.Create(compressContent: false);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            string[] snapshots =
            [
                SnapshotForCulture(source, "en-US"),
                SnapshotForCulture(source, "it-IT"),
                SnapshotForCulture(source, "tr-TR"),
                SnapshotForCulture(source, "ar-SA")
            ];

            Assert.That(snapshots, Has.All.EqualTo(snapshots[0]));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public void RenderOverridesAreSnapshottedPerOperation()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "optional-content-alpha3.pdf"));
        Page page = document.CreatePage(0);
        string groupId = document.OptionalContentGroups[0].Id;
        var overrides = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [groupId] = false
        };
        var options = new RasterRenderOptions
        {
            Dpi = 36,
            Antialiasing = 1,
            UseFontSubstitution = false,
            OptionalContentVisibility = overrides
        };

        string hidden = Hash(page.RenderToPng(options));
        overrides[groupId] = true;
        string visible = Hash(page.RenderToPng(options));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(hidden, Is.Not.EqualTo(visible));
            Assert.That(
                hidden,
                Is.EqualTo(Hash(page.RenderToPng(options with
                {
                    OptionalContentVisibility =
                        new Dictionary<string, bool>(StringComparer.Ordinal)
                        {
                            [groupId] = false
                        }
                }))));
            Assert.That(
                visible,
                Is.EqualTo(Hash(page.RenderToPng(options with
                {
                    OptionalContentVisibility =
                        new Dictionary<string, bool>(StringComparer.Ordinal)
                        {
                            [groupId] = true
                        }
                }))));
        }));
    }

    [Test]
    public void DiagnosticReadsReturnIndependentSnapshots()
    {
        using Document document = Document.LoadFromFile(
            Path.Combine(FixtureDirectory(), "robustness-beta2.pdf"));
        IReadOnlyList<PdfDiagnostic> first = document.Diagnostics;
        IReadOnlyList<PdfDiagnostic> second = document.Diagnostics;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(ReferenceEquals(first, second), Is.False);
        }));

        if (first is PdfDiagnostic[] mutable)
        {
            mutable[0] = null!;
            Assert.That(document.Diagnostics[0], Is.Not.Null);
        }
    }

    [Test]
    [NonParallelizable]
    public void ReleaseSmokeFitsTimeAndAllocationBudgets()
    {
        string path = Path.Combine(FixtureDirectory(), "rendering-beta2.pdf");
        var renderOptions = new RasterRenderOptions
        {
            Dpi = 36,
            Antialiasing = 1,
            UseFontSubstitution = false
        };

        using (Document warmup = Document.LoadFromFile(path))
            _ = warmup.CreatePage(0).Render(renderOptions);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        using (Document document = Document.LoadFromFile(path))
        {
            for (int index = 0; index < document.Pages; index++)
            {
                Page page = document.CreatePage(index);
                _ = page.Graphics.Count;
                _ = page.Render(renderOptions);
            }
        }
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.Progress.WriteLine(
            $"Release smoke: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms, " +
            $"{allocated / (1024.0 * 1024.0):0.0} MiB allocated.");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(5)),
                "the six-page release smoke corpus exceeded its time budget");
            Assert.That(
                allocated,
                Is.LessThan(32L * 1024 * 1024),
                "the six-page release smoke corpus exceeded its allocation budget");
        }));
    }

    [Test]
    public void PublicApiMatchesReleaseSurface()
    {
        string surface = PublicApiSurface(normalizePortVersion: false);
        string actual = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(surface)))
            .ToLowerInvariant();

        Assert.That(
            actual,
            Is.EqualTo(FrozenPublicApiSha256),
            $"Public API changed. Actual SHA-256: {actual}");
    }

    [Test]
    public void CallablePublicApiMatchesFrozenReleaseSurface()
    {
        string surface = PublicApiSurface(normalizePortVersion: true);
        string actual = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(surface)))
            .ToLowerInvariant();

        Assert.That(
            actual,
            Is.EqualTo(FrozenCallableApiSha256),
            $"Callable public API changed. Actual SHA-256: {actual}");
    }

    [Test]
    public void PublicOptionDefaultsMatchFrozenReleaseContract()
    {
        string actual = Sha256(OptionDefaultsSurface());

        Assert.That(
            actual,
            Is.EqualTo(FrozenOptionDefaultsSha256),
            $"Public option defaults changed. Actual SHA-256: {actual}");
    }

    [Test]
    public void StructuredSchemasMatchFrozenReleaseContract()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Schemas");
        string surface = string.Join(
            '\n',
            Directory.GetFiles(directory)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(path =>
                    $"{Path.GetFileName(path)}\n{NormalizeNewlines(File.ReadAllText(path))}")) +
            "\n";
        string actual = Sha256(surface);

        Assert.That(
            actual,
            Is.EqualTo(FrozenSchemasSha256),
            $"Structured schemas changed. Actual SHA-256: {actual}");
    }

    [Test]
    public void ExportManifestsMatchFrozenReleaseContract()
    {
        using Document document = Document.LoadFromData(
            PdfFixtures.Create(compressContent: false));
        HtmlExportBundle html = document.CreateHtmlBundle();
        StructuredExportBundle structured = document.CreateStructuredBundle();
        using JsonDocument htmlManifest = JsonDocument.Parse(
            html.Files.Single(file => file.RelativePath == "manifest.json").Data);
        using JsonDocument structuredManifest = JsonDocument.Parse(
            structured.Files.Single(file => file.RelativePath == "manifest.json").Data);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                htmlManifest.RootElement.GetProperty("format").GetString(),
                Is.EqualTo("poppler-net-html-bundle"));
            Assert.That(
                htmlManifest.RootElement.GetProperty("formatVersion").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                htmlManifest.RootElement.GetProperty("entryPoint").GetString(),
                Is.EqualTo("index.html"));
            Assert.That(
                structuredManifest.RootElement.GetProperty("schemaVersion").GetString(),
                Is.EqualTo(StructuredExportBundle.SchemaVersion));
        }));

        string surface =
            $"html:{JsonShape(htmlManifest.RootElement)}\n" +
            $"structured:{JsonShape(structuredManifest.RootElement)}\n";
        string actual = Sha256(surface);
        Assert.That(
            actual,
            Is.EqualTo(FrozenManifestContractsSha256),
            $"Export manifest contracts changed. Actual SHA-256: {actual}");
    }

    [Test]
    public void CliHelpMatchesFrozenReleaseContract()
    {
        string source = NormalizeNewlines(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "ContractSources",
            "CliProgram.cs")));
        const string startMarker = "            poppler-net — managed-only Poppler 26.07 port\n";
        const string endMarker = "            Page numbers accepted by the CLI are one-based.\n";
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        string help = source[start..(end + endMarker.Length)];
        string actual = Sha256(help);

        Assert.That(
            actual,
            Is.EqualTo(FrozenCliHelpSha256),
            $"CLI help contract changed. Actual SHA-256: {actual}");
    }

    [Test]
    public void PortVersionMatchesAssemblyInformationalVersion()
    {
        string informationalVersion =
            typeof(Document).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;
        string packageVersion = informationalVersion.Split('+', 2)[0];

        Assert.That(Document.PortVersion, Is.EqualTo(packageVersion));
    }

    [Test]
    public void VersionMatchesStableRelease()
    {
        string informationalVersion =
            typeof(Document).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;
        string packageVersion = informationalVersion.Split('+', 2)[0];

        Assert.Multiple((Action)(() =>
        {
            Assert.That(Document.PortVersion, Is.EqualTo("0.13.0"));
            Assert.That(packageVersion, Is.EqualTo("0.13.0"));
            Assert.That(packageVersion, Does.Not.Contain("-"));
        }));
    }

    private static string OptionDefaultsSurface()
    {
        object[] defaults =
        [
            new PdfReadOptions(),
            new PdfPageExtractionOptions(),
            new StructuredExportOptions(),
            new RasterRenderOptions(),
            new SvgRenderOptions(),
            new HtmlRenderOptions(),
            new HtmlExportOptions()
        ];
        return string.Join(
            '\n',
            defaults.Select(value => ContractValue(value))) + "\n";
    }

    private static string ContractValue(object? value)
    {
        if (value is null)
            return "null";
        Type type = value.GetType();
        if (value is string text)
            return JsonSerializer.Serialize(text);
        if (value is bool boolean)
            return boolean ? "true" : "false";
        if (type.IsEnum)
            return $"{FriendlyName(type)}.{value}";
        if (value is IFormattable formattable &&
            (type.IsPrimitive || value is decimal))
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }
        if (value is IEnumerable items)
        {
            return $"[{string.Join(",", items.Cast<object?>().Select(ContractValue))}]";
        }
        if (type.Assembly == typeof(Document).Assembly)
        {
            string properties = string.Join(
                ",",
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => property.GetMethod is not null)
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property =>
                        $"{property.Name}={ContractValue(property.GetValue(value))}"));
            return $"{FriendlyName(type)}{{{properties}}}";
        }
        return value.ToString() ?? "";
    }

    private static string JsonShape(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object =>
            "{" + string.Join(
                ",",
                element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => $"{property.Name}:{JsonShape(property.Value)}")) + "}",
        JsonValueKind.Array =>
            "[" + string.Join(
                "|",
                element.EnumerateArray()
                    .Select(JsonShape)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)) + "]",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => element.ValueKind.ToString().ToLowerInvariant()
    };

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string SnapshotForCulture(byte[] source, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        using Document document = Document.LoadFromData(source);
        Page page = document.CreatePage(0);
        long creationTicks =
            document.CreationDate?.ToUniversalTime().Ticks ?? 0;
        string svg = page.RenderToSvg(new SvgRenderOptions
        {
            IncludeImages = false
        });
        string png = Hash(page.RenderToPng(new RasterRenderOptions
        {
            Dpi = 24,
            Antialiasing = 1,
            UseFontSubstitution = false
        }));
        return string.Join(
            "\n",
            document.PdfVersion,
            creationTicks.ToString(CultureInfo.InvariantCulture),
            page.PageRect().ToString(),
            page.Text(layout: TextLayout.RawOrder),
            svg,
            png);
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));

    private static string ReadAndRender(Document document)
    {
        Page page = document.CreatePage(0);
        string text = page.Text(layout: TextLayout.RawOrder);
        string fonts = string.Join(
            ",",
            page.Fonts.Select(font => $"{font.ResourceName}:{font.Name}"));
        int graphics = page.Graphics.Count;
        byte[] png = page.RenderToPng(new RasterRenderOptions
        {
            Dpi = 24,
            Antialiasing = 1,
            UseFontSubstitution = false
        });
        string hash = Convert.ToHexString(SHA256.HashData(png));
        return $"{text}|{fonts}|{graphics}|{hash}";
    }

    private static string PublicApiSurface(bool normalizePortVersion)
    {
        Assembly assembly = typeof(Document).Assembly;
        var lines = new List<string>();
        foreach (Type type in assembly.GetExportedTypes().OrderBy(FriendlyName, StringComparer.Ordinal))
        {
            lines.Add($"type {TypeKind(type)} {FriendlyName(type)}");
            const BindingFlags flags =
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                string modifier = field.IsLiteral
                    ? "const "
                    : field.IsStatic
                        ? field.IsInitOnly ? "static readonly " : "static "
                        : field.IsInitOnly ? "readonly " : "";
                object? rawValue =
                    field.IsLiteral ? field.GetRawConstantValue() : null;
                if (normalizePortVersion &&
                    type == typeof(Document) &&
                    field.Name == nameof(Document.PortVersion))
                {
                    rawValue = "<version>";
                }
                string value = field.IsLiteral
                    ? $" = {FormatDefault(rawValue)}"
                    : "";
                lines.Add(
                    $"  field {modifier}{FriendlyName(field.FieldType)} {field.Name}{value}");
            }
            foreach (ConstructorInfo constructor in type.GetConstructors(flags))
            {
                lines.Add(
                    $"  ctor {FriendlyName(type)}({FormatParameters(constructor.GetParameters())})");
            }
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                MethodInfo? accessor = property.GetMethod ?? property.SetMethod;
                string modifier = accessor?.IsStatic == true ? "static " : "";
                string accessors =
                    $"{(property.GetMethod is not null ? "get;" : "")}" +
                    $"{(property.SetMethod is not null ? "set;" : "")}";
                lines.Add(
                    $"  property {modifier}{FriendlyName(property.PropertyType)} " +
                    $"{property.Name} {{ {accessors} }}");
            }
            foreach (EventInfo @event in type.GetEvents(flags))
            {
                MethodInfo? accessor = @event.AddMethod ?? @event.RemoveMethod;
                string modifier = accessor?.IsStatic == true ? "static " : "";
                lines.Add(
                    $"  event {modifier}{FriendlyName(@event.EventHandlerType!)} {@event.Name}");
            }
            foreach (MethodInfo method in type.GetMethods(flags)
                         .Where(method =>
                             !method.Name.StartsWith("get_", StringComparison.Ordinal) &&
                             !method.Name.StartsWith("set_", StringComparison.Ordinal) &&
                             !method.Name.StartsWith("add_", StringComparison.Ordinal) &&
                             !method.Name.StartsWith("remove_", StringComparison.Ordinal)))
            {
                string modifier = method.IsStatic ? "static " : "";
                string generic = method.IsGenericMethodDefinition
                    ? $"<{string.Join(",", method.GetGenericArguments().Select(argument => argument.Name))}>"
                    : "";
                lines.Add(
                    $"  method {modifier}{FriendlyName(method.ReturnType)} " +
                    $"{method.Name}{generic}({FormatParameters(method.GetParameters())})");
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines) + "\n";
    }

    private static string FormatParameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            ", ",
            parameters.Select(parameter =>
            {
                Type type = parameter.ParameterType;
                string modifier = type.IsByRef
                    ? parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref "
                    : "";
                if (type.IsByRef)
                    type = type.GetElementType()!;
                string optional = parameter.HasDefaultValue
                    ? $" = {FormatDefault(parameter.DefaultValue)}"
                    : "";
                return $"{modifier}{FriendlyName(type)} {parameter.Name}{optional}";
            }));

    private static string FormatDefault(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        char character => $"'{character}'",
        bool boolean => boolean ? "true" : "false",
        Enum enumeration => Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static string FriendlyName(Type type)
    {
        if (type.IsArray)
            return $"{FriendlyName(type.GetElementType()!)}[]";
        if (type.IsPointer)
            return $"{FriendlyName(type.GetElementType()!)}*";
        if (type.IsGenericParameter)
            return type.Name;
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        string name = type.GetGenericTypeDefinition().FullName ??
                      type.GetGenericTypeDefinition().Name;
        int marker = name.IndexOf('`');
        if (marker >= 0)
            name = name[..marker];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(FriendlyName))}>";
    }

    private static string TypeKind(Type type) =>
        type.IsEnum
            ? "enum"
            : type.IsValueType
                ? "struct"
                : type.IsInterface
                    ? "interface"
                    : type.BaseType?.FullName?.StartsWith(
                        "System.MulticastDelegate",
                        StringComparison.Ordinal) == true
                        ? "delegate"
                        : "class";

    private static string FixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
