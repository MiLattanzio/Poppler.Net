using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace Poppler.Exporting;

internal static class StructuredDocumentExporter
{
    private const string XmlNamespace = "urn:poppler-net:structured:v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Json(Document document, StructuredExportOptions? options)
    {
        StructuredExportOptions effective = Effective(options);
        var nodes = Nodes(effective);
        return Encoding.UTF8.GetString(
            JsonBytes(Model(document, effective, nodes), effective.MaximumOutputBytes));
    }

    public static string Json(Page page, StructuredExportOptions? options)
    {
        StructuredExportOptions effective = EffectivePage(options);
        var nodes = Nodes(effective);
        return Encoding.UTF8.GetString(
            JsonBytes(PageExport(page, effective, nodes), effective.MaximumOutputBytes));
    }

    public static string Xml(Document document, StructuredExportOptions? options)
    {
        StructuredExportOptions effective = Effective(options);
        var nodes = Nodes(effective);
        return XmlText(Model(document, effective, nodes), effective.MaximumOutputBytes);
    }

    public static string Xml(Page page, StructuredExportOptions? options)
    {
        StructuredExportOptions effective = EffectivePage(options);
        var nodes = Nodes(effective);
        return XmlText(PageExport(page, effective, nodes), effective.MaximumOutputBytes);
    }

    public static string Xhtml(Document document, StructuredExportOptions? options)
    {
        StructuredExportOptions effective = Effective(options);
        var nodes = Nodes(effective);
        return XhtmlText(Model(document, effective, nodes), effective.MaximumOutputBytes);
    }

    public static string Xhtml(Page page, StructuredExportOptions? options)
    {
        StructuredExportOptions effective = EffectivePage(options);
        var nodes = Nodes(effective);
        return XhtmlText(PageExport(page, effective, nodes), effective.MaximumOutputBytes);
    }

    public static StructuredExportBundle Bundle(
        Document document,
        StructuredExportOptions? options)
    {
        StructuredExportOptions effective = Effective(options);
        ExportNodeBudget nodes = Nodes(effective);
        StructuredDocumentModel model = Model(document, effective, nodes);
        var files = new List<StructuredExportFile>();
        long totalBytes = 0;
        Add(
            files,
            "document.json",
            "application/json",
            JsonBytes(model, Remaining(effective, totalBytes)),
            effective,
            ref totalBytes);
        Add(
            files,
            "document.xml",
            "application/xml",
            Utf8(XmlText(model, Remaining(effective, totalBytes))),
            effective,
            ref totalBytes);
        Add(
            files,
            "document.xhtml",
            "application/xhtml+xml",
            Utf8(XhtmlText(model, Remaining(effective, totalBytes))),
            effective,
            ref totalBytes);

        if (effective.IncludeImages)
        {
            foreach (StructuredPageModel page in model.Pages)
            {
                Page sourcePage = document.CreatePage(page.Index);
                for (int index = 0; index < sourcePage.Images.Count; index++)
                {
                    PdfImageExport export = sourcePage.Images[index]
                        .Export(effective.PreferOriginalImages);
                    string path = page.Images[index].File;
                    Add(
                        files,
                        path,
                        export.MediaType,
                        export.Data.ToArray(),
                        effective,
                        ref totalBytes);
                }
            }
        }

        nodes.Consume(files.Count + 1);
        StructuredManifestModel manifest = new(
            StructuredExportBundle.SchemaVersion,
            files
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new StructuredManifestEntry(
                    file.RelativePath,
                    file.MediaType,
                    file.Data.Length,
                    Convert.ToHexString(SHA256.HashData(file.Data.Span)).ToLowerInvariant()))
                .ToArray());
        Add(
            files,
            "manifest.json",
            "application/json",
            JsonBytes(manifest, Remaining(effective, totalBytes)),
            effective,
            ref totalBytes);
        return new StructuredExportBundle(files);
    }

    private static StructuredDocumentModel Model(
        Document document,
        StructuredExportOptions options,
        ExportNodeBudget nodes)
    {
        ArgumentNullException.ThrowIfNull(document);
        int count = options.PageCount ?? (document.Pages - options.FirstPageIndex);
        if (options.FirstPageIndex >= document.Pages ||
            count < 1 ||
            options.FirstPageIndex > document.Pages - count)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Structured export page range is invalid.");
        }
        if (count > options.MaximumPages)
        {
            throw new PdfLimitException(
                "Structured export page count exceeds the configured limit.");
        }

        nodes.Consume(checked(1 + document.Information.Count + count));
        KeyValuePair<string, string>[] information = document.Information
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();

        StructuredPageModel[] pages = Enumerable
            .Range(options.FirstPageIndex, count)
            .Select(index => PageModel(document.CreatePage(index), options, nodes))
            .ToArray();
        return new StructuredDocumentModel(
            StructuredExportBundle.SchemaVersion,
            document.PdfVersion,
            document.Pages,
            information,
            pages);
    }

    private static StructuredPageModel PageModel(
        Page page,
        StructuredExportOptions options,
        ExportNodeBudget nodes)
    {
        ArgumentNullException.ThrowIfNull(page);
        PdfRectangle box = page.PageRect();
        IReadOnlyList<FontInfo> sourceFonts = page.Fonts;
        nodes.Consume(sourceFonts.Count);
        StructuredFontModel[] fonts = sourceFonts
            .Select((font, index) => new StructuredFontModel(
                $"p{page.Number:D4}-font-{index + 1:D4}",
                font.ResourceName,
                font.NormalizedName,
                font.RawName,
                font.Type.ToString(),
                font.Encoding,
                font.WritingMode.ToString(),
                font.IsEmbedded,
                font.IsSubset,
                font.HasToUnicode))
            .ToArray();
        Dictionary<string, string> fontIds = fonts.ToDictionary(
            font => font.ResourceName,
            font => font.Id,
            StringComparer.Ordinal);
        IReadOnlyList<TextBox> sourceText = page.TextList(options.TextLayout);
        nodes.Consume(sourceText.Count);
        StructuredTextModel[] text = sourceText
            .Select((boxValue, index) => new StructuredTextModel(
                $"p{page.Number:D4}-text-{index + 1:D6}",
                boxValue.Text,
                Rectangle(boxValue.BoundingBox),
                boxValue.Rotation,
                boxValue.HasSpaceAfter,
                boxValue.FontResourceName,
                fontIds.GetValueOrDefault(boxValue.FontResourceName, ""),
                boxValue.FontName,
                boxValue.RawFontName,
                boxValue.FontSize,
                boxValue.WritingMode.ToString(),
                boxValue.IsRightToLeft))
            .ToArray();
        IReadOnlyList<PdfAnnotation> sourceAnnotations = page.Annotations;
        int linkCount = sourceAnnotations.Count(
            annotation => annotation.Type == PdfAnnotationType.Link);
        nodes.Consume(linkCount);
        PdfAnnotation[] sourceLinks = sourceAnnotations
            .Where(annotation => annotation.Type == PdfAnnotationType.Link)
            .ToArray();
        StructuredLinkModel[] links = sourceLinks
            .Select((annotation, index) => new StructuredLinkModel(
                $"p{page.Number:D4}-link-{index + 1:D4}",
                Rectangle(annotation.Rectangle),
                annotation.Action.Type.ToString(),
                annotation.Action.Uri,
                annotation.Action.Destination?.PageNumber,
                annotation.Action.Destination?.NamedDestination ??
                    annotation.Action.NamedTarget))
            .ToArray();
        IReadOnlyList<PdfImage> sourceImages = options.IncludeImages
            ? page.Images
            : [];
        nodes.Consume(sourceImages.Count);
        StructuredImageModel[] images = options.IncludeImages
            ? sourceImages.Select((image, index) =>
            {
                PdfImageExport export = image.Export(options.PreferOriginalImages);
                string id = $"p{page.Number:D4}-image-{index + 1:D4}";
                string resource = SafeStem(image.ResourceName);
                return new StructuredImageModel(
                    id,
                    image.ResourceName,
                    image.Width,
                    image.Height,
                    image.SourceBitsPerComponent,
                    image.ColorSpace,
                    image.Compression.ToString(),
                    image.CanExportOriginal,
                    export.IsOriginal,
                    export.FallbackReason,
                    $"images/page-{page.Number:D4}-{index + 1:D4}-{resource}.{export.Extension}",
                    export.MediaType,
                    export.Data.Length);
            }).ToArray()
            : [];

        return new StructuredPageModel(
            page.Index,
            page.Number,
            page.Label,
            new StructuredRectangle(box.Left, box.Bottom, box.Right, box.Top),
            page.Rotation,
            page.Text(layout: options.TextLayout),
            text,
            fonts,
            links,
            images);
    }

    private static StructuredPageExportModel PageExport(
        Page page,
        StructuredExportOptions options,
        ExportNodeBudget nodes)
    {
        nodes.Consume(2);
        return new(
            StructuredExportBundle.SchemaVersion,
            PageModel(page, options, nodes));
    }

    private static StructuredExportOptions Effective(StructuredExportOptions? options) =>
        (options ?? new StructuredExportOptions()).Snapshot();

    private static StructuredExportOptions EffectivePage(StructuredExportOptions? options)
    {
        StructuredExportOptions effective = Effective(options);
        if (effective.FirstPageIndex != 0 || effective.PageCount is not (null or 1))
        {
            throw new ArgumentException(
                "Page exports do not accept a document page range.",
                nameof(options));
        }
        return effective;
    }

    private static StructuredRectangle Rectangle(PdfRectangle value) =>
        new(value.Left, value.Bottom, value.Right, value.Top);

    private static byte[] JsonBytes<T>(T value, long maximumBytes)
    {
        using var output = new BoundedExportStream(
            maximumBytes,
            "Structured export output exceeds the configured byte limit.");
        JsonSerializer.Serialize(output, value, JsonOptions);
        return output.ToArray();
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string XmlText(object value, long maximumBytes)
    {
        using var output = new BoundedExportStream(
            maximumBytes,
            "Structured export output exceeds the configured byte limit.");
        using (XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
            NewLineChars = "\n"
        }))
        {
            writer.WriteStartElement("poppler", "structured-export", XmlNamespace);
            using JsonDocument json = JsonDocument.Parse(JsonBytes(value, maximumBytes));
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Structured export root must be an object.");
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
                WriteJsonElement(writer, property.Value, property.Name);
            writer.WriteEndElement();
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void WriteJsonElement(XmlWriter writer, JsonElement element, string name)
    {
        writer.WriteStartElement(name, XmlNamespace);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                    WriteJsonElement(writer, property.Value, property.Name);
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    WriteJsonElement(writer, item, "item");
                break;
            case JsonValueKind.Null:
                writer.WriteAttributeString("nil", "true");
                break;
            default:
                writer.WriteString(XmlSafe(element.ToString()));
                break;
        }
        writer.WriteEndElement();
    }

    private static string XhtmlText(object value, long maximumBytes)
    {
        using JsonDocument json = JsonDocument.Parse(JsonBytes(value, maximumBytes));
        var output = new BoundedUtf8Builder(
            maximumBytes,
            "Structured export output exceeds the configured byte limit.");
        output.Append("<!DOCTYPE html>\n")
            .Append("<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\"><head>")
            .Append("<meta charset=\"utf-8\"/><title>Poppler.Net structured export</title>")
            .Append("<style>body{margin:0;padding:1rem;font:14px system-ui,sans-serif}")
            .Append("dl,ol{margin:.35rem 0 1rem 1.25rem}dt{font-weight:700}dd{margin:0 0 .35rem 1rem}")
            .Append("code{font-family:ui-monospace,monospace;white-space:pre-wrap}</style></head><body>")
            .Append("<main data-schema-version=\"")
            .Append(StructuredExportBundle.SchemaVersion)
            .Append("\"><h1>Poppler.Net structured export</h1>");
        WriteXhtmlValue(output, json.RootElement);
        output.Append("</main></body></html>");
        return output.ToString();
    }

    private static void WriteXhtmlValue(BoundedUtf8Builder output, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append("<dl>");
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    output.Append("<dt>");
                    AppendEscaped(output, property.Name);
                    output.Append("</dt><dd>");
                    WriteXhtmlValue(output, property.Value);
                    output.Append("</dd>");
                }
                output.Append("</dl>");
                break;
            case JsonValueKind.Array:
                output.Append("<ol>");
                foreach (JsonElement item in element.EnumerateArray())
                {
                    output.Append("<li>");
                    WriteXhtmlValue(output, item);
                    output.Append("</li>");
                }
                output.Append("</ol>");
                break;
            case JsonValueKind.Null:
                output.Append("<span class=\"null\">null</span>");
                break;
            default:
                output.Append("<code>");
                AppendEscaped(output, XmlSafe(element.ToString()));
                output.Append("</code>");
                break;
        }
    }

    private static void AppendEscaped(BoundedUtf8Builder output, string value)
    {
        foreach (char character in value)
        {
            output.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => character.ToString()
            });
        }
    }

    private static string XmlSafe(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsHighSurrogate(character) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                result.Append(character).Append(value[++index]);
            }
            else if (!char.IsSurrogate(character) && XmlConvert.IsXmlChar(character))
            {
                result.Append(character);
            }
            else
            {
                result.Append('\uFFFD');
            }
        }
        return result.ToString();
    }

    private static string SafeStem(string value)
    {
        string result = new(value
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? char.ToLowerInvariant(character)
                : '-')
            .ToArray());
        result = result.Trim('-');
        if (string.IsNullOrEmpty(result))
            return "image";
        const int maximumLength = 64;
        return result.Length <= maximumLength
            ? result
            : result[..maximumLength].TrimEnd('-');
    }

    private static ExportNodeBudget Nodes(StructuredExportOptions options) => new(
        options.MaximumNodes,
        "Structured export node count exceeds the configured limit.");

    private static long Remaining(StructuredExportOptions options, long totalBytes)
    {
        long remaining = options.MaximumOutputBytes - totalBytes;
        if (remaining < 1)
        {
            throw new PdfLimitException(
                "Structured export output exceeds the configured byte limit.");
        }
        return remaining;
    }

    private static void Add(
        List<StructuredExportFile> files,
        string path,
        string mediaType,
        byte[] data,
        StructuredExportOptions options,
        ref long totalBytes)
    {
        if (files.Count >= options.MaximumFiles)
            throw new PdfLimitException("Structured export file count exceeds the configured limit.");
        if (totalBytes > options.MaximumOutputBytes - data.LongLength)
            throw new PdfLimitException("Structured export output exceeds the configured byte limit.");
        files.Add(new StructuredExportFile(path, mediaType, data));
        totalBytes = checked(totalBytes + data.LongLength);
    }

    private sealed record StructuredDocumentModel(
        string SchemaVersion,
        string PdfVersion,
        int PageCount,
        IReadOnlyList<KeyValuePair<string, string>> Information,
        IReadOnlyList<StructuredPageModel> Pages);
    private sealed record StructuredPageExportModel(
        string SchemaVersion,
        StructuredPageModel Page);

    private sealed record StructuredPageModel(
        int Index,
        int Number,
        string Label,
        StructuredRectangle CropBox,
        int Rotation,
        string Text,
        IReadOnlyList<StructuredTextModel> TextBoxes,
        IReadOnlyList<StructuredFontModel> Fonts,
        IReadOnlyList<StructuredLinkModel> Links,
        IReadOnlyList<StructuredImageModel> Images);

    private sealed record StructuredRectangle(double Left, double Bottom, double Right, double Top);
    private sealed record StructuredTextModel(
        string Id, string Text, StructuredRectangle BoundingBox, int Rotation,
        bool HasSpaceAfter, string FontResourceName, string FontId,
        string FontName, string RawFontName, double FontSize,
        string WritingMode, bool IsRightToLeft);
    private sealed record StructuredFontModel(
        string Id, string ResourceName, string Name, string RawName,
        string Type, string Encoding, string WritingMode,
        bool IsEmbedded, bool IsSubset, bool HasToUnicode);
    private sealed record StructuredLinkModel(
        string Id, StructuredRectangle Rectangle, string Action,
        string? Uri, int? DestinationPageNumber, string? NamedDestination);
    private sealed record StructuredImageModel(
        string Id, string ResourceName, int Width, int Height, int BitsPerComponent,
        string ColorSpace, string Compression, bool CanExportOriginal,
        bool IsOriginal, string? FallbackReason, string File, string MediaType, int Bytes);
    private sealed record StructuredManifestModel(
        string SchemaVersion,
        IReadOnlyList<StructuredManifestEntry> Files);
    private sealed record StructuredManifestEntry(
        string Path, string MediaType, int Bytes, string Sha256);

}
