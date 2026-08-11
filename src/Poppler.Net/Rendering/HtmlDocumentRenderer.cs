using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Poppler.Rendering;

internal static class HtmlDocumentRenderer
{
    private const string HtmlMediaType = "text/html;charset=utf-8";
    private const string CssMediaType = "text/css;charset=utf-8";

    public static string Render(Page page, HtmlRenderOptions? options)
    {
        ArgumentNullException.ThrowIfNull(page);
        HtmlRenderOptions effective = (options ?? new HtmlRenderOptions()).Snapshot();
        ExportAssets assets = CreateAssets([page], effective);
        string title = $"PDF page {page.Number}";
        return ValidateOutput(
            BuildHtml(title, assets, effective, inline: true),
            effective.MaximumOutputBytes);
    }

    public static string Render(Document document, HtmlExportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        (HtmlExportOptions effective, int count) =
            (options ?? new HtmlExportOptions()).Snapshot(document.Pages);
        Page[] pages = SelectedPages(document, effective.FirstPageIndex, count);
        ExportAssets assets = CreateAssets(pages, effective.PageOptions);
        return ValidateOutput(
            BuildHtml(TitleFor(document, effective), assets, effective.PageOptions, inline: true),
            effective.PageOptions.MaximumOutputBytes);
    }

    public static HtmlExportBundle CreateBundle(
        Document document,
        HtmlExportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        (HtmlExportOptions effective, int count) =
            (options ?? new HtmlExportOptions()).Snapshot(document.Pages);
        Page[] pages = SelectedPages(document, effective.FirstPageIndex, count);
        ExportAssets assets = CreateAssets(pages, effective.PageOptions);
        string title = TitleFor(document, effective);

        var files = new List<HtmlExportFile>
        {
            TextFile(
                "index.html",
                HtmlMediaType,
                BuildHtml(title, assets, effective.PageOptions, inline: false)),
            TextFile(
                "styles.css",
                CssMediaType,
                BuildStyles(assets.Fonts, effective.PageOptions, inline: false))
        };
        files.AddRange(assets.Pages.Select(page =>
            TextFile(page.BackgroundPath, "image/svg+xml", page.Svg)));
        files.AddRange(assets.Fonts.Select(font =>
            new HtmlExportFile(font.Path, font.MediaType, font.Data)));

        byte[] manifest = BuildManifest(title, assets, files);
        files.Add(new HtmlExportFile("manifest.json", "application/json", manifest));
        long totalBytes = files.Sum(file => (long)file.Data.Length);
        if (totalBytes > effective.PageOptions.MaximumOutputBytes)
        {
            throw new PdfLimitException(
                $"HTML bundle is {totalBytes} bytes; limit is " +
                $"{effective.PageOptions.MaximumOutputBytes} bytes.");
        }
        return new HtmlExportBundle(files);
    }

    private static Page[] SelectedPages(Document document, int firstPageIndex, int count) =>
        Enumerable.Range(firstPageIndex, count)
            .Select(document.CreatePage)
            .ToArray();

    private static string TitleFor(Document document, HtmlExportOptions options) =>
        !string.IsNullOrWhiteSpace(options.Title)
            ? options.Title.Trim()
            : !string.IsNullOrWhiteSpace(document.Title)
                ? document.Title
                : "PDF document";

    private static ExportAssets CreateAssets(
        IReadOnlyList<Page> pages,
        HtmlRenderOptions options)
    {
        var fontsByHash = new Dictionary<string, EmbeddedFont>(StringComparer.Ordinal);
        var pageAssets = new List<PageAsset>(pages.Count);
        long embeddedFontBytes = 0;
        foreach (Page page in pages)
        {
            var fontAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options.EmbedFonts)
            {
                foreach (FontInfo font in page.Fonts
                             .OrderBy(value => value.ResourceName, StringComparer.Ordinal))
                {
                    if (!TryEmbeddedFont(font, out byte[] data, out string extension, out string mediaType))
                        continue;

                    string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                    if (!fontsByHash.TryGetValue(hash, out EmbeddedFont? embedded))
                    {
                        embedded = new EmbeddedFont(
                            $"pdf-font-{hash[..16]}",
                            $"fonts/{hash[..24]}.{extension}",
                            mediaType,
                            data);
                        embeddedFontBytes = checked(embeddedFontBytes + data.LongLength);
                        if (embeddedFontBytes > options.MaximumEmbeddedFontBytes)
                        {
                            throw new PdfLimitException(
                                $"Embedded HTML fonts exceed the configured " +
                                $"{options.MaximumEmbeddedFontBytes}-byte limit.");
                        }
                        fontsByHash.Add(hash, embedded);
                    }
                    fontAliases.TryAdd(NormalizeFontName(font.Name), embedded.Alias);
                }
            }

            string svg = page.RenderToSvg(new SvgRenderOptions
            {
                Scale = options.Scale,
                Background = options.Background,
                Foreground = options.Foreground,
                IncludeVectorGraphics = options.IncludeVectorGraphics,
                IncludeImages = options.IncludeImages,
                IncludeText = options.TextLayerMode == HtmlTextLayerMode.InvisibleOverlay,
                FallbackMode = options.FallbackMode,
                RasterFallbackDpi = options.RasterFallbackDpi,
                OptionalContentVisibility = options.OptionalContentVisibility
            });
            string backgroundPath = $"pages/page-{page.Number:0000}.svg";
            pageAssets.Add(new PageAsset(page, svg, backgroundPath, fontAliases));
        }

        return new ExportAssets(
            pageAssets,
            fontsByHash.Values.OrderBy(font => font.Path, StringComparer.Ordinal).ToArray());
    }

    private static string BuildHtml(
        string title,
        ExportAssets assets,
        HtmlRenderOptions options,
        bool inline)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\">");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("  <meta name=\"generator\" content=\"Poppler.Net ")
            .Append(Encode(Document.PortVersion))
            .AppendLine("\">");
        html.Append("  <title>").Append(Encode(title)).AppendLine("</title>");
        if (inline)
        {
            html.AppendLine("  <style>");
            html.Append(BuildStyles(assets.Fonts, options, inline: true));
            html.AppendLine("  </style>");
        }
        else
        {
            html.AppendLine("  <link rel=\"stylesheet\" href=\"styles.css\">");
        }
        html.AppendLine("</head>");
        html.Append("<body class=\"")
            .Append(options.TextLayerMode == HtmlTextLayerMode.Visible
                ? "pdf-visible-text"
                : "pdf-invisible-text")
            .AppendLine("\">");
        html.AppendLine("  <header class=\"pdf-document-header\">");
        html.Append("    <h1>").Append(Encode(title)).AppendLine("</h1>");
        html.AppendLine("    <nav aria-label=\"PDF pages\">");
        foreach (PageAsset page in assets.Pages)
        {
            html.Append("      <a href=\"#page-")
                .Append(page.Page.Number.ToString(CultureInfo.InvariantCulture))
                .Append("\">")
                .Append(page.Page.Number.ToString(CultureInfo.InvariantCulture))
                .AppendLine("</a>");
        }
        html.AppendLine("    </nav>");
        html.AppendLine("  </header>");
        html.AppendLine("  <main class=\"pdf-document\">");
        foreach (PageAsset page in assets.Pages)
            WritePage(html, page, options, inline);
        html.AppendLine("  </main>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static void WritePage(
        StringBuilder html,
        PageAsset asset,
        HtmlRenderOptions options,
        bool inline)
    {
        Page page = asset.Page;
        PdfRectangle crop = page.PageRect(PageBox.CropBox);
        double sourceWidth = crop.Width * options.Scale;
        double sourceHeight = crop.Height * options.Scale;
        int rotation = NormalizeRotation(page.Rotation);
        bool swapDimensions = rotation is 90 or 270;
        double width = swapDimensions ? sourceHeight : sourceWidth;
        double height = swapDimensions ? sourceWidth : sourceHeight;
        html.Append("    <section class=\"pdf-page\" id=\"page-")
            .Append(page.Number.ToString(CultureInfo.InvariantCulture))
            .Append("\" data-page-number=\"")
            .Append(page.Number.ToString(CultureInfo.InvariantCulture))
            .Append("\" data-page-label=\"")
            .Append(Encode(page.Label))
            .Append("\" data-page-rotation=\"")
            .Append(rotation.ToString(CultureInfo.InvariantCulture))
            .Append("\" style=\"--pdf-page-width:")
            .Append(Format(width))
            .Append("px;--pdf-page-height:")
            .Append(Format(height))
            .AppendLine("px\">");
        html.Append("      <h2 class=\"pdf-page-label\">Page ")
            .Append(page.Number.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(page.Label) && page.Label != page.Number.ToString(CultureInfo.InvariantCulture))
            html.Append(" · ").Append(Encode(page.Label));
        html.AppendLine("</h2>");
        html.AppendLine("      <div class=\"pdf-page-canvas\">");
        html.Append("        <div class=\"pdf-page-content\" style=\"width:")
            .Append(Format(sourceWidth)).Append("px;height:")
            .Append(Format(sourceHeight)).Append("px");
        string? pageTransform = PageTransform(rotation, sourceWidth, sourceHeight);
        if (pageTransform is not null)
            html.Append(";transform:").Append(pageTransform);
        html.AppendLine("\">");
        html.AppendLine("        <div class=\"pdf-page-background\" aria-hidden=\"true\">");
        if (inline)
        {
            string svg = StripXmlDeclaration(asset.Svg);
            foreach (string line in svg.Split('\n'))
            {
                if (line.Length > 0)
                    html.Append("          ").AppendLine(line);
            }
        }
        else
        {
            html.Append("          <img src=\"")
                .Append(Encode(asset.BackgroundPath))
                .AppendLine("\" alt=\"\">");
        }
        html.AppendLine("        </div>");
        WriteTextLayer(html, asset, crop, options);
        WriteLinkLayer(html, page, crop, options.Scale);
        html.AppendLine("        </div>");
        html.AppendLine("      </div>");
        html.AppendLine("    </section>");
    }

    private static void WriteTextLayer(
        StringBuilder html,
        PageAsset asset,
        PdfRectangle crop,
        HtmlRenderOptions options)
    {
        html.Append("        <div class=\"pdf-text-layer\" role=\"group\" aria-label=\"Text layer for page ")
            .Append(asset.Page.Number.ToString(CultureInfo.InvariantCulture))
            .AppendLine("\">");
        IReadOnlyList<TextBox> boxes = asset.Page.TextFor(
            options.TextLayout,
            options.OptionalContentVisibility);
        foreach (TextBox box in boxes)
        {
            double left = (Math.Min(box.BoundingBox.Left, box.BoundingBox.Right) -
                           Math.Min(crop.Left, crop.Right)) * options.Scale;
            double top = (Math.Max(crop.Bottom, crop.Top) -
                          Math.Max(box.BoundingBox.Bottom, box.BoundingBox.Top)) * options.Scale;
            double width = Math.Max(0.1, box.BoundingBox.Width * options.Scale);
            double height = Math.Max(0.1, box.BoundingBox.Height * options.Scale);
            double fontSize = Math.Max(0.1, box.FontSize * options.Scale);
            string normalizedName = NormalizeFontName(box.FontName);
            string family = asset.FontAliases.TryGetValue(normalizedName, out string? alias)
                ? $"'{alias}',{FallbackFamily(normalizedName)}"
                : $"'{CssString(normalizedName)}',{FallbackFamily(normalizedName)}";

            html.Append("          <span class=\"pdf-text\" data-font-name=\"")
                .Append(Encode(normalizedName))
                .Append("\" data-rotation=\"")
                .Append(box.Rotation.ToString(CultureInfo.InvariantCulture))
                .Append("\" style=\"left:").Append(Format(left))
                .Append("px;top:").Append(Format(top))
                .Append("px;width:").Append(Format(width))
                .Append("px;height:").Append(Format(height))
                .Append("px;font-size:").Append(Format(fontSize))
                .Append("px;line-height:").Append(Format(height))
                .Append("px;font-family:").Append(family).Append(';');
            if (box.Rotation != 0)
            {
                html.Append("transform:rotate(")
                    .Append((-box.Rotation).ToString(CultureInfo.InvariantCulture))
                    .Append("deg);");
            }
            if (box.WritingMode == FontWritingMode.Vertical)
                html.Append("writing-mode:vertical-rl;");
            if (box.IsRightToLeft)
                html.Append("direction:rtl;");
            html.Append("\">")
                .Append(Encode(box.Text + (box.HasSpaceAfter ? " " : "")))
                .AppendLine("</span>");
        }
        html.AppendLine("        </div>");
    }

    private static void WriteLinkLayer(
        StringBuilder html,
        Page page,
        PdfRectangle crop,
        double scale)
    {
        html.AppendLine("        <div class=\"pdf-link-layer\" aria-label=\"PDF links\">");
        foreach (PdfAnnotation annotation in page.Annotations.Where(value =>
                     value.Type == PdfAnnotationType.Link && value.IsVisible))
        {
            if (!TryLink(annotation.Action, out string href, out bool external))
                continue;
            PdfRectangle rectangle = annotation.Rectangle;
            double left = (Math.Min(rectangle.Left, rectangle.Right) -
                           Math.Min(crop.Left, crop.Right)) * scale;
            double top = (Math.Max(crop.Bottom, crop.Top) -
                          Math.Max(rectangle.Bottom, rectangle.Top)) * scale;
            double width = Math.Max(0.1, rectangle.Width * scale);
            double height = Math.Max(0.1, rectangle.Height * scale);
            string label = !string.IsNullOrWhiteSpace(annotation.Contents)
                ? annotation.Contents
                : external ? href : "PDF destination";

            html.Append("          <a class=\"pdf-link\" href=\"")
                .Append(Encode(href))
                .Append("\" aria-label=\"")
                .Append(Encode(label))
                .Append("\" style=\"left:").Append(Format(left))
                .Append("px;top:").Append(Format(top))
                .Append("px;width:").Append(Format(width))
                .Append("px;height:").Append(Format(height)).Append("px\"");
            if (external)
                html.Append(" target=\"_blank\" rel=\"noreferrer noopener\"");
            html.AppendLine("></a>");
        }
        html.AppendLine("        </div>");
    }

    private static bool TryLink(
        PdfAnnotationAction action,
        out string href,
        out bool external)
    {
        external = false;
        if (action.Type == PdfAnnotationActionType.GoTo && action.Destination is { } destination)
        {
            href = $"#page-{destination.PageNumber}";
            return true;
        }
        if (action.Type != PdfAnnotationActionType.Uri ||
            string.IsNullOrWhiteSpace(action.Uri) ||
            !Uri.TryCreate(action.Uri, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https" or "mailto" or "tel"))
        {
            href = "";
            return false;
        }

        href = uri.AbsoluteUri;
        external = true;
        return true;
    }

    private static string BuildStyles(
        IReadOnlyList<EmbeddedFont> fonts,
        HtmlRenderOptions options,
        bool inline)
    {
        var css = new StringBuilder();
        foreach (EmbeddedFont font in fonts)
        {
            string source = inline
                ? $"data:{font.MediaType};base64,{Convert.ToBase64String(font.Data)}"
                : font.Path;
            css.Append("@font-face{font-family:'").Append(font.Alias)
                .Append("';src:url('").Append(source)
                .Append("');font-display:block;}\n");
        }
        css.AppendLine("*{box-sizing:border-box}");
        css.Append("html,body{margin:0;min-height:100%;background:#eceff1;color:")
            .Append(CssValue(options.Foreground)).AppendLine("}");
        css.AppendLine("body{font-family:system-ui,-apple-system,'Segoe UI',sans-serif}");
        css.AppendLine(".pdf-document-header{position:sticky;top:0;z-index:20;display:flex;align-items:center;gap:1rem;padding:.6rem 1rem;background:rgba(255,255,255,.96);border-bottom:1px solid #cfd8dc}");
        css.AppendLine(".pdf-document-header h1{margin:0;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:1rem}");
        css.AppendLine(".pdf-document-header nav{display:flex;gap:.35rem;overflow:auto;margin-left:auto}");
        css.AppendLine(".pdf-document-header a{display:inline-flex;min-width:1.8rem;height:1.8rem;align-items:center;justify-content:center;border:1px solid #b0bec5;border-radius:.25rem;color:#263238;text-decoration:none}");
        css.AppendLine(".pdf-document{display:grid;gap:2rem;justify-content:center;padding:2rem}");
        css.AppendLine(".pdf-page{margin:0;max-width:100%}");
        css.AppendLine(".pdf-page-label{margin:0 0 .35rem;color:#455a64;font-size:.8rem;font-weight:600}");
        css.Append(".pdf-page-canvas{position:relative;width:var(--pdf-page-width);height:var(--pdf-page-height);max-width:none;overflow:hidden;background:")
            .Append(CssValue(options.Background)).AppendLine(";box-shadow:0 .25rem 1rem rgba(0,0,0,.2)}");
        css.AppendLine(".pdf-page-content{position:absolute;left:0;top:0;transform-origin:0 0}");
        css.AppendLine(".pdf-page-background,.pdf-text-layer,.pdf-link-layer{position:absolute;inset:0;width:100%;height:100%}");
        css.AppendLine(".pdf-page-background svg,.pdf-page-background img{display:block;width:100%;height:100%}");
        css.AppendLine(".pdf-text{position:absolute;display:block;margin:0;padding:0;white-space:pre;overflow:visible;transform-origin:0 100%;font-weight:400;font-style:normal;font-kerning:none}");
        css.AppendLine(".pdf-invisible-text .pdf-text{color:transparent;-webkit-text-fill-color:transparent}");
        css.Append(".pdf-visible-text .pdf-text{color:").Append(CssValue(options.Foreground))
            .AppendLine(";-webkit-text-fill-color:currentColor}");
        css.AppendLine(".pdf-link-layer{pointer-events:none}");
        css.AppendLine(".pdf-link{position:absolute;display:block;pointer-events:auto;border:0;text-decoration:none}");
        css.AppendLine(".pdf-link:focus-visible{outline:2px solid #0277bd;outline-offset:-2px;background:rgba(2,119,189,.12)}");
        css.AppendLine("@media(max-width:720px){.pdf-document{justify-content:start;padding:1rem;overflow:auto}.pdf-document-header{position:relative}.pdf-document-header h1{display:none}}");
        css.AppendLine("@media print{html,body{background:#fff}.pdf-document-header,.pdf-page-label{display:none}.pdf-document{display:block;padding:0}.pdf-page{break-after:page}.pdf-page-canvas{box-shadow:none}}\n");
        return css.ToString();
    }

    private static byte[] BuildManifest(
        string title,
        ExportAssets assets,
        IReadOnlyList<HtmlExportFile> files)
    {
        using var output = new MemoryStream();
        using (var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("format", "poppler-net-html-bundle");
            json.WriteNumber("formatVersion", 1);
            json.WriteString("generator", $"Poppler.Net {Document.PortVersion}");
            json.WriteString("upstream", Document.UpstreamVersion);
            json.WriteString("title", title);
            json.WriteString("entryPoint", "index.html");
            json.WriteStartArray("pages");
            foreach (PageAsset asset in assets.Pages)
            {
                PdfRectangle crop = asset.Page.PageRect(PageBox.CropBox);
                json.WriteStartObject();
                json.WriteNumber("index", asset.Page.Index);
                json.WriteNumber("number", asset.Page.Number);
                json.WriteString("label", asset.Page.Label);
                json.WriteNumber("widthPoints", crop.Width);
                json.WriteNumber("heightPoints", crop.Height);
                json.WriteNumber("rotation", asset.Page.Rotation);
                json.WriteString("background", asset.BackgroundPath);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("files");
            foreach (HtmlExportFile file in files.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("path", file.RelativePath);
                json.WriteString("mediaType", file.MediaType);
                json.WriteNumber("bytes", file.Data.Length);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        output.WriteByte((byte)'\n');
        return output.ToArray();
    }

    private static bool TryEmbeddedFont(
        FontInfo font,
        out byte[] data,
        out string extension,
        out string mediaType)
    {
        ReadOnlyMemory<byte> program = font.GetEmbeddedData();
        if (program.IsEmpty ||
            font.EmbeddedFormat is not (EmbeddedFontFormat.TrueType or EmbeddedFontFormat.OpenType))
        {
            data = [];
            extension = "";
            mediaType = "";
            return false;
        }

        data = program.ToArray();
        bool openType = font.EmbeddedFormat == EmbeddedFontFormat.OpenType;
        extension = openType ? "otf" : "ttf";
        mediaType = openType ? "font/otf" : "font/ttf";
        return true;
    }

    private static HtmlExportFile TextFile(string path, string mediaType, string text) =>
        new(path, mediaType, Encoding.UTF8.GetBytes(text));

    private static string ValidateOutput(string html, long maximumBytes)
    {
        long bytes = Encoding.UTF8.GetByteCount(html);
        if (bytes > maximumBytes)
        {
            throw new PdfLimitException(
                $"HTML output is {bytes} bytes; limit is {maximumBytes} bytes.");
        }
        return html;
    }

    private static string StripXmlDeclaration(string svg)
    {
        if (!svg.StartsWith("<?xml", StringComparison.Ordinal))
            return svg;
        int end = svg.IndexOf("?>", StringComparison.Ordinal);
        return end < 0 ? svg : svg[(end + 2)..].TrimStart('\r', '\n');
    }

    private static string NormalizeFontName(string name)
    {
        string normalized = string.IsNullOrWhiteSpace(name) ? "sans-serif" : name.Trim();
        if (normalized.Length > 7 &&
            normalized[6] == '+' &&
            normalized.AsSpan(0, 6).ToString().All(character => character is >= 'A' and <= 'Z'))
        {
            normalized = normalized[7..];
        }
        return normalized;
    }

    private static string FallbackFamily(string name)
    {
        string normalized = name.ToLowerInvariant();
        if (normalized.Contains("courier", StringComparison.Ordinal) ||
            normalized.Contains("mono", StringComparison.Ordinal))
        {
            return "monospace";
        }
        if (normalized.Contains("times", StringComparison.Ordinal) ||
            normalized.Contains("serif", StringComparison.Ordinal) ||
            normalized.Contains("garamond", StringComparison.Ordinal) ||
            normalized.Contains("minion", StringComparison.Ordinal))
        {
            return "serif";
        }
        return "sans-serif";
    }

    private static string CssString(string value) =>
        new(value
            .Where(character => !char.IsControl(character))
            .SelectMany(character => character switch
            {
                '\\' => "\\\\",
                '\'' => "\\'",
                _ => character.ToString()
            })
            .ToArray());

    private static string CssValue(string value) => value.Trim();

    private static int NormalizeRotation(int rotation)
    {
        int normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static string? PageTransform(
        int rotation,
        double sourceWidth,
        double sourceHeight) => rotation switch
        {
            90 => $"translate({Format(sourceHeight)}px,0) rotate(90deg)",
            180 => $"translate({Format(sourceWidth)}px,{Format(sourceHeight)}px) rotate(180deg)",
            270 => $"translate(0,{Format(sourceWidth)}px) rotate(270deg)",
            _ => null
        };

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record ExportAssets(
        IReadOnlyList<PageAsset> Pages,
        IReadOnlyList<EmbeddedFont> Fonts);

    private sealed record PageAsset(
        Page Page,
        string Svg,
        string BackgroundPath,
        IReadOnlyDictionary<string, string> FontAliases);

    private sealed record EmbeddedFont(
        string Alias,
        string Path,
        string MediaType,
        byte[] Data);
}
