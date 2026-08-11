using System.Globalization;
using System.Net;
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
                BuildStyles(
                    assets.Fonts,
                    assets.CssRules,
                    effective.PageOptions,
                    inline: false))
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
        var fontsByHash = new Dictionary<string, ManagedHtmlWebFont>(StringComparer.Ordinal);
        var pageAssets = new List<PageAsset>(pages.Count);
        var styles = new HtmlCssCatalog();
        long embeddedFontBytes = 0;
        foreach (Page page in pages)
        {
            ManagedHtmlPageLayout? textLayout =
                options.TextLayerMode == HtmlTextLayerMode.InvisibleOverlay
                    ? null
                    : ManagedHtmlTextLayout.Create(page, options, styles);
            if (textLayout is not null)
            {
                foreach (ManagedHtmlWebFont font in textLayout.Fonts)
                {
                    if (!fontsByHash.TryAdd(font.Hash, font))
                        continue;
                    embeddedFontBytes = checked(embeddedFontBytes + font.Data.LongLength);
                    if (embeddedFontBytes > options.MaximumEmbeddedFontBytes)
                    {
                        throw new PdfLimitException(
                            $"Generated HTML fonts exceed the configured " +
                            $"{options.MaximumEmbeddedFontBytes}-byte limit.");
                    }
                }
            }

            var svgOptions = new SvgRenderOptions
            {
                Scale = options.Scale,
                Background = options.Background,
                Foreground = options.Foreground,
                IncludeVectorGraphics = options.IncludeVectorGraphics,
                IncludeImages = options.IncludeImages,
                IncludeText = true,
                FallbackMode = options.FallbackMode,
                RasterFallbackDpi = options.RasterFallbackDpi,
                OptionalContentVisibility = options.OptionalContentVisibility
            };
            string svg = textLayout is null
                ? SvgPageRenderer.Render(page, svgOptions)
                : SvgPageRenderer.Render(
                    page,
                    svgOptions,
                    text => textLayout.BackgroundText.Contains(text));
            string backgroundPath = $"pages/page-{page.Number:0000}.svg";
            pageAssets.Add(new PageAsset(page, svg, backgroundPath, textLayout));
        }

        return new ExportAssets(
            pageAssets,
            fontsByHash.Values.OrderBy(font => font.Path, StringComparer.Ordinal).ToArray(),
            styles.Rules.ToArray());
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
            html.Append(BuildStyles(assets.Fonts, assets.CssRules, options, inline: true));
            html.AppendLine("  </style>");
        }
        else
        {
            html.AppendLine("  <link rel=\"stylesheet\" href=\"styles.css\">");
        }
        html.AppendLine("</head>");
        html.Append("<body class=\"")
            .Append(options.TextLayerMode == HtmlTextLayerMode.InvisibleOverlay
                ? "pdf-invisible-text"
                : "pdf-native-text")
            .AppendLine("\">");
        html.Append("  <main class=\"pdf-document\" aria-label=\"")
            .Append(Encode(title))
            .AppendLine("\">");
        foreach (PageAsset page in assets.Pages)
            WritePage(html, page, options, inline);
        html.AppendLine("  </main>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return CanonicalText(html);
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
            .Append("\" aria-label=\"Page ")
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
        if (asset.TextLayout is not null)
        {
            WriteNativeTextLayer(html, asset.TextLayout);
            html.AppendLine("        </div>");
            return;
        }

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
            string family = $"'{CssString(normalizedName)}',{FallbackFamily(normalizedName)}";

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

    private static void WriteNativeTextLayer(
        StringBuilder html,
        ManagedHtmlPageLayout layout)
    {
        foreach (ManagedHtmlTextRun run in layout.Runs)
        {
            html.Append("<span class=\"pdf-text-run")
                .Append(run.UsesGraphicalBackground ? " pdf-background-text" : "")
                .Append("\" data-font-name=\"")
                .Append(Encode(run.FontName))
                .Append("\" data-source-text=\"")
                .Append(Encode(run.Text))
                .Append("\">");
            foreach (ManagedHtmlGlyph glyph in run.Glyphs)
            {
                html.Append("<span class=\"pdf-glyph ")
                    .Append(glyph.MatrixClass).Append(' ')
                    .Append(glyph.FontClass);
                if (glyph.PaintClass is not null)
                    html.Append(' ').Append(glyph.PaintClass);
                html.Append("\" data-font-name=\"")
                    .Append(Encode(glyph.FontName))
                    .Append("\" data-source-text=\"")
                    .Append(Encode(glyph.ActualText))
                    .Append("\" style=\"left:")
                    .Append(Format(glyph.Left)).Append("px;top:")
                    .Append(Format(glyph.Top)).Append("px\">");
                if (glyph.VisibleText is not null)
                {
                    html.Append("<span class=\"pdf-glyph-visual\" aria-hidden=\"true\">")
                        .Append(Encode(glyph.VisibleText))
                        .Append("</span>");
                }
                html.Append("<span class=\"pdf-glyph-copy\">")
                    .Append(Encode(glyph.ActualText))
                    .Append("</span></span>");
            }
            html.AppendLine("</span>");
        }
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
        IReadOnlyList<ManagedHtmlWebFont> fonts,
        IReadOnlyList<ManagedHtmlCssRule> rules,
        HtmlRenderOptions options,
        bool inline)
    {
        var css = new StringBuilder();
        foreach (ManagedHtmlWebFont font in fonts)
        {
            string source = inline
                ? $"data:{font.MediaType};base64,{Convert.ToBase64String(font.Data)}"
                : font.Path;
            css.Append("@font-face{font-family:'").Append(font.Alias)
                .Append("';src:url('").Append(source)
                .Append("');font-display:block;}\n");
        }
        foreach (ManagedHtmlCssRule rule in rules)
        {
            css.Append('.').Append(rule.ClassName).Append('{')
                .Append(rule.Declarations).AppendLine("}");
        }
        css.AppendLine("*{box-sizing:border-box}");
        css.Append("html,body{margin:0;padding:0;min-height:0;background:")
            .Append(CssValue(options.Background)).Append(";color:")
            .Append(CssValue(options.Foreground)).AppendLine("}");
        css.AppendLine("body{width:max-content;font-family:system-ui,-apple-system,'Segoe UI',sans-serif}");
        css.AppendLine(".pdf-document{display:block;width:max-content;margin:0;padding:0}");
        css.AppendLine(".pdf-page{display:block;width:var(--pdf-page-width);height:var(--pdf-page-height);margin:0;overflow:hidden}");
        css.Append(".pdf-page-canvas{position:relative;width:var(--pdf-page-width);height:var(--pdf-page-height);overflow:hidden;background:")
            .Append(CssValue(options.Background)).AppendLine("}");
        css.AppendLine(".pdf-page-content{position:absolute;left:0;top:0;transform-origin:0 0}");
        css.AppendLine(".pdf-page-background,.pdf-text-layer,.pdf-link-layer{position:absolute;inset:0;width:100%;height:100%}");
        css.AppendLine(".pdf-page-background svg,.pdf-page-background img{display:block;width:100%;height:100%}");
        css.AppendLine(".pdf-text{position:absolute;display:block;margin:0;padding:0;white-space:pre;overflow:visible;transform-origin:0 100%;font-weight:400;font-style:normal;font-kerning:none}");
        css.AppendLine(".pdf-invisible-text .pdf-text{color:transparent;-webkit-text-fill-color:transparent}");
        css.AppendLine(".pdf-text-run{display:contents}");
        css.AppendLine(".pdf-glyph{position:absolute;display:block;width:0;height:0;transform-origin:0 0;font-size:1px;line-height:1px;white-space:pre;font-style:normal;font-weight:400;font-kerning:none;font-variant-ligatures:none}");
        css.AppendLine(".pdf-glyph-visual,.pdf-glyph-copy{position:absolute;display:block;left:0;top:-.8px;height:1px;line-height:1px;white-space:pre}");
        css.AppendLine(".pdf-glyph-visual{user-select:none;pointer-events:none}");
        css.AppendLine(".pdf-glyph-copy{z-index:1;color:transparent!important;-webkit-text-fill-color:transparent!important;-webkit-text-stroke:0 transparent!important;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;user-select:text}");
        css.AppendLine(".pdf-background-text .pdf-glyph-copy{z-index:0}");
        css.AppendLine(".pdf-link-layer{pointer-events:none}");
        css.AppendLine(".pdf-link{position:absolute;display:block;pointer-events:auto;border:0;text-decoration:none}");
        css.AppendLine(".pdf-link:focus-visible{outline:2px solid #0277bd;outline-offset:-2px;background:rgba(2,119,189,.12)}");
        css.AppendLine("@media print{.pdf-page{break-after:page}.pdf-page:last-child{break-after:auto}}\n");
        return CanonicalText(css);
    }

    private static string CanonicalText(StringBuilder value) =>
        value.ToString().ReplaceLineEndings("\n");

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
        IReadOnlyList<ManagedHtmlWebFont> Fonts,
        IReadOnlyList<ManagedHtmlCssRule> CssRules);

    private sealed record PageAsset(
        Page Page,
        string Svg,
        string BackgroundPath,
        ManagedHtmlPageLayout? TextLayout);
}
