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

    public static string Json(Document document, StructuredExportOptions? options) =>
        Encoding.UTF8.GetString(JsonBytes(Model(document, options)));

    public static string Json(Page page, StructuredExportOptions? options) =>
        Encoding.UTF8.GetString(JsonBytes(PageExport(page, EffectivePage(options))));

    public static string Xml(Document document, StructuredExportOptions? options) =>
        XmlText(Model(document, options));

    public static string Xml(Page page, StructuredExportOptions? options) =>
        XmlText(PageExport(page, EffectivePage(options)));

    public static string Xhtml(Document document, StructuredExportOptions? options) =>
        XhtmlText(Model(document, options));

    public static string Xhtml(Page page, StructuredExportOptions? options) =>
        XhtmlText(PageExport(page, EffectivePage(options)));

    public static StructuredExportBundle Bundle(
        Document document,
        StructuredExportOptions? options)
    {
        StructuredExportOptions effective = Effective(options);
        StructuredDocumentModel model = Model(document, effective);
        var files = new List<StructuredExportFile>();
        long totalBytes = 0;
        Add(files, "document.json", "application/json", JsonBytes(model), effective, ref totalBytes);
        Add(files, "document.xml", "application/xml", Utf8(XmlText(model)), effective, ref totalBytes);
        Add(files, "document.xhtml", "application/xhtml+xml", Utf8(XhtmlText(model)), effective, ref totalBytes);

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
            JsonBytes(manifest),
            effective,
            ref totalBytes);
        return new StructuredExportBundle(files);
    }

    private static StructuredDocumentModel Model(
        Document document,
        StructuredExportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        StructuredExportOptions effective = Effective(options);
        int count = effective.PageCount ?? (document.Pages - effective.FirstPageIndex);
        if (effective.FirstPageIndex >= document.Pages ||
            count < 1 ||
            effective.FirstPageIndex > document.Pages - count)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Structured export page range is invalid.");
        }

        StructuredPageModel[] pages = Enumerable
            .Range(effective.FirstPageIndex, count)
            .Select(index => PageModel(document.CreatePage(index), effective))
            .ToArray();
        KeyValuePair<string, string>[] information = document.Information
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
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
        StructuredExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        PdfRectangle box = page.PageRect();
        StructuredFontModel[] fonts = page.Fonts
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
        StructuredTextModel[] text = page.TextList(options.TextLayout)
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
        StructuredLinkModel[] links = page.Annotations
            .Where(annotation => annotation.Type == PdfAnnotationType.Link)
            .Select((annotation, index) => new StructuredLinkModel(
                $"p{page.Number:D4}-link-{index + 1:D4}",
                Rectangle(annotation.Rectangle),
                annotation.Action.Type.ToString(),
                annotation.Action.Uri,
                annotation.Action.Destination?.PageNumber,
                annotation.Action.Destination?.NamedDestination ??
                    annotation.Action.NamedTarget))
            .ToArray();
        StructuredImageModel[] images = options.IncludeImages
            ? page.Images.Select((image, index) =>
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
        StructuredExportOptions options) =>
        new(StructuredExportBundle.SchemaVersion, PageModel(page, options));

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

    private static byte[] JsonBytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string XmlText(object value)
    {
        var output = new StringBuilder();
        using var stringWriter = new Utf8StringWriter(output);
        using XmlWriter writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false,
            Encoding = Encoding.UTF8,
            NewLineChars = "\n"
        });
        writer.WriteStartElement("poppler", "structured-export", XmlNamespace);
        using JsonDocument json = JsonDocument.Parse(JsonBytes(value));
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Structured export root must be an object.");
        foreach (JsonProperty property in json.RootElement.EnumerateObject())
            WriteJsonElement(writer, property.Value, property.Name);
        writer.WriteEndElement();
        writer.Flush();
        return output.ToString();
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

    private static string XhtmlText(object value)
    {
        using JsonDocument json = JsonDocument.Parse(JsonBytes(value));
        var content = new StringBuilder();
        WriteXhtmlValue(content, json.RootElement);
        return "<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\"><head>" +
            "<meta charset=\"utf-8\"/><title>Poppler.Net structured export</title>" +
            "<style>body{margin:0;padding:1rem;font:14px system-ui,sans-serif}" +
            "dl,ol{margin:.35rem 0 1rem 1.25rem}dt{font-weight:700}dd{margin:0 0 .35rem 1rem}" +
            "code{font-family:ui-monospace,monospace;white-space:pre-wrap}</style></head><body>" +
            "<main data-schema-version=\"" + StructuredExportBundle.SchemaVersion + "\">" +
            "<h1>Poppler.Net structured export</h1>" + content + "</main></body></html>";
    }

    private static void WriteXhtmlValue(StringBuilder output, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append("<dl>");
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    output.Append("<dt>").Append(Escape(property.Name)).Append("</dt><dd>");
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
                output.Append("<code>").Append(Escape(XmlSafe(element.ToString()))).Append("</code>");
                break;
        }
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

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
        return string.IsNullOrEmpty(result) ? "image" : result;
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

    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
