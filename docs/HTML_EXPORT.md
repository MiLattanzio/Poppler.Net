# Fixed-layout HTML export

`0.13.0-alpha.1` adds managed HTML conversion for one page, a page range, or a
complete PDF. Poppler 26.07 `pdftohtml` and `HtmlOutputDev` are behavioral
references only; export runs through Poppler.Net's managed parser, display
list, SVG renderer and text extractor.

## Output model

Each selected page is represented by a fixed-size `<section>` containing:

1. a managed SVG background for supported text, vectors, images, annotations
   and bounded raster fallbacks;
2. absolutely positioned DOM text spans carrying Unicode, font name,
   direction, writing mode and rotation;
3. safe URI and internal-destination link rectangles.

The default `HtmlTextLayerMode.InvisibleOverlay` retains SVG text for visual
fidelity and makes the DOM text transparent but selectable and searchable.
This is also the safest mode for missing or unsupported browser fonts: the
visible rendering does not depend on a platform font, while the extracted text
remains in the document.

`HtmlTextLayerMode.Visible` removes text from the SVG background and uses the
browser text layer visually. Reusable embedded TrueType/OpenType programs are
loaded with `@font-face`; missing, Type 1 and standalone CFF programs fall back
to CSS serif, sans-serif or monospace families. Six-uppercase-letter PDF
subset prefixes such as `ABCDEF+DejaVuSans` are removed from the exposed font
family name.

This release does not attempt semantic reflow. Paragraph reconstruction,
office-format conversion and browser-perfect line breaking are outside the
alpha.1 contract.

## API

```csharp
using Poppler;
using Poppler.Rendering;

using Document document = Document.LoadFromFile("input.pdf");

// Complete, self-contained HTML document.
string html = document.RenderToHtml();
document.SaveHtml("document.html");

// One page. API indexes are zero-based.
Page page = document.CreatePage(2);
page.SaveHtml("page-3.html", new HtmlRenderOptions
{
    TextLayerMode = HtmlTextLayerMode.Visible,
    EmbedFonts = true,
    RasterFallbackDpi = 144
});

// Page range.
document.SaveHtml("pages-2-5.html", new HtmlExportOptions
{
    FirstPageIndex = 1,
    PageCount = 4,
    Title = "Selected pages"
});

// Multi-file bundle.
HtmlExportBundle bundle = document.CreateHtmlBundle();
bundle.SaveToDirectory("html-bundle");
```

`HtmlExportOptions.PageOptions` applies the same `HtmlRenderOptions` snapshot
to every selected page. Optional-content visibility overrides are applied to
both the SVG background and DOM text extraction.

`MaximumOutputBytes` bounds the UTF-8 single-file result or the total bundle
payload (256 MiB by default). `MaximumEmbeddedFontBytes` independently bounds
deduplicated reusable font programs (64 MiB by default); use
`EmbedFonts = false` when font assets must not be included.

## Packaging modes

`Page.RenderToHtml` and `Document.RenderToHtml` return a single HTML document.
CSS, page SVG and supported embedded fonts are inlined, so the output can be
opened or transferred without companion files.

`Document.CreateHtmlBundle` returns immutable files with stable relative paths:

- `index.html` entry point;
- `styles.css`;
- `pages/page-NNNN.svg` for each original PDF page number;
- deduplicated `fonts/<sha256-prefix>.ttf|otf` assets when reusable;
- `manifest.json` with format version, generator/upstream versions, page
  geometry, rotation, background mapping and file sizes.

`HtmlExportBundle.SaveToDirectory` resolves every destination below the
requested root and rejects any escaping path.

## CLI

Install the NuGet dotnet tool:

```bash
dotnet tool install --global Poppler.Net.Cli --version 0.13.0-alpha.1
```

Examples:

```bash
poppler-net html input.pdf document.html
poppler-net html input.pdf page-4.html --page 4
poppler-net html input.pdf selected.html --first-page 2 --last-page 5
poppler-net html input.pdf output-directory --bundle
poppler-net html input.pdf visible.html --visible-text --scale 1.25
poppler-net html input.pdf layers.html --layer 17:0=off
```

CLI page numbers are one-based. `--bundle` interprets the output as a
directory. `--fallback rasterize|omit`, `--fallback-dpi`, `--no-embed-fonts`,
`--no-images`, `--no-vector`, `--title` and the common password/CMap options
are supported.

## Playground

The WebAssembly playground generates every artifact locally. The export panel
offers:

- current page HTML;
- complete-document self-contained HTML;
- a ZIP containing the directory bundle without flattening its paths.

No PDF, password, font or generated HTML is uploaded to a server.

## Poppler reference corpus

`tests/fixtures/html-alpha1-fixture.json` records the Poppler 26.07 source
components used as the semantic reference, the aligned behaviors and accepted
product differences. It freezes deterministic HTML byte counts and SHA-256
values for link annotations, an embedded TrueType subset in visible-text mode,
and a scaled two-page graphics range. The corpus is run by
`HtmlExportAlpha1Tests`; Poppler is not a runtime or CI dependency.

## Links and active content

Resolved internal GoTo annotations become `#page-N` links. External links are
emitted only for absolute `http`, `https`, `mailto` and `tel` URIs and use
`noreferrer noopener`. PDF JavaScript, launch, form-submit, multimedia and
other actions are never executed or copied into executable HTML.

Document metadata and text are HTML-encoded. Rendering colors reject CSS
statement/block delimiters. The generated document contains no script.

## Known alpha.1 limits

- Fixed layout preserves page geometry; it is not responsive semantic reflow.
- Browser text metrics may differ in visible-text mode when an embedded font
  cannot be reused. Invisible-overlay mode preserves the SVG visual result.
- Link rectangles are axis-aligned, matching the public annotation rectangle.
- Cross-page links can target a page omitted from a selected range; the href is
  retained but has no target element in that output.
- HTML currently uses the CropBox, matching the SVG renderer.
- Embedded Type 1/raw CFF programs are downloadable through existing APIs and
  playground tools but are not registered as browser fonts.
