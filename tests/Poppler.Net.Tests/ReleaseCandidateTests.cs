using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Poppler;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class ReleaseCandidateTests
{
    private const string FrozenPublicApiSha256 =
        "a4bd8b15a968793f80398fc495c93f00e5a911fb7233500a6f1d9a5e70130048";

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
                Is.LessThan(TimeSpan.FromSeconds(30)),
                "the six-page release smoke corpus exceeded its time budget");
            Assert.That(
                allocated,
                Is.LessThan(512L * 1024 * 1024),
                "the six-page release smoke corpus exceeded its allocation budget");
        }));
    }

    [Test]
    public void PublicApiMatchesReleaseSurface()
    {
        string surface = PublicApiSurface();
        string actual = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(surface)))
            .ToLowerInvariant();

        Assert.That(
            actual,
            Is.EqualTo(FrozenPublicApiSha256),
            $"Public API changed. Actual SHA-256: {actual}");
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

    private static string PublicApiSurface()
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
                string value = field.IsLiteral
                    ? $" = {FormatDefault(field.GetRawConstantValue())}"
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
