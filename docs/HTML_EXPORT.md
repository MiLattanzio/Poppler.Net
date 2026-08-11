# Fixed-layout HTML export

`0.13.0-alpha.1` adds managed HTML conversion for one page, a page range, or a
complete PDF. Poppler 26.07 `pdftohtml` and `HtmlOutputDev` are behavioral
references only; export runs through Poppler.Net's managed parser, display
list, SVG renderer and text extractor.

The glyph/state/font architecture was also compared behaviorally with
pdf2htmlEX. No pdf2htmlEX C/C++ implementation is copied or shipped: its
FontForge, FreeType, Cairo and native Poppler dependencies are replaced by the
existing Poppler.Net decoders plus the managed TrueType builder in this
release.

## Output model

Each selected page is represented by a fixed-size `<section>` containing:

1. a managed SVG background for vectors, images, text that requires PDF paint
   ordering, and bounded raster fallbacks;
2. glyph-level, absolutely positioned DOM text carrying source Unicode,
   normalized font names and the complete affine transform;
3. safe URI and internal-destination link rectangles.

The exported HTML is the rendering artifact itself, not a document viewer. It
contains no toolbar, page navigation, page labels, viewport padding, centering
or decorative shadow. A single-page export starts at the top-left origin and
contains only that page. Range and complete-document exports contain only the
selected pages, stacked in order at their exact PDF dimensions without gaps.

The default `HtmlTextLayerMode.Visible` reconstructs supported glyphs in HTML.
Poppler.Net decodes each embedded TrueType, OpenType, CFF or Type 1 outline and
builds a small deterministic TrueType web font entirely in managed code. Its
`cmap` uses supplementary private-use scalars, so PDF character codes and
ligatures cannot collide with browser Unicode shaping. A separate transparent,
selectable span retains the original Unicode for copy, search and accessibility.

When no usable outline is available, the source Unicode is still rendered at
the exact PDF glyph origin using a CSS serif, sans-serif or monospace fallback.
Type 3 glyphs, complex clips, soft masks, non-solid text brushes, transparency
groups and text covered by a later graphical operation remain in SVG, with the
Unicode copy layer retained above them. This conservative split preserves PDF
paint order without turning text into unavailable data.

`HtmlTextLayerMode.InvisibleOverlay` is the compatibility mode: every visual
text operation remains in SVG and word-level transparent DOM text provides
selection. Six-uppercase-letter PDF subset prefixes such as
`ABCDEF+DejaVuSans` are removed from every exposed font-family name.

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
both the SVG background and DOM text extraction. Native glyphs retain display-
list paint order; `TextLayout` controls the word-level compatibility overlay.

`MaximumOutputBytes` bounds the UTF-8 single-file result or the total bundle
payload (256 MiB by default). `MaximumEmbeddedFontBytes` independently bounds
deduplicated generated font programs (64 MiB by default). Each managed web
font is internally bounded to 32,768 glyphs. Use `EmbedFonts = false` to keep
exact glyph positioning while rendering every native glyph through a browser
fallback family.

## Packaging modes

`Page.RenderToHtml` and `Document.RenderToHtml` return a single HTML document.
CSS, page SVG and supported embedded fonts are inlined, so the output can be
opened or transferred without companion files. HTML and CSS always use LF line
endings so byte counts and hashes remain deterministic across operating systems.

`Document.CreateHtmlBundle` returns immutable files with stable relative paths:

- `index.html` entry point;
- `styles.css`;
- `pages/page-NNNN.svg` for each original PDF page number;
- deduplicated `fonts/<sha256-prefix>.ttf` managed web fonts when outlines are
  reusable;
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
poppler-net html input.pdf native.html --scale 1.25
poppler-net html input.pdf svg-text.html --svg-text
poppler-net html input.pdf layers.html --layer 17:0=off
```

CLI page numbers are one-based. `--bundle` interprets the output as a
directory. `--fallback rasterize|omit`, `--fallback-dpi`, `--no-embed-fonts`,
`--no-images`, `--no-vector`, `--title` and the common password/CMap options
are supported.

## Playground

The WebAssembly playground generates every artifact locally. The export panel
offers:

- a sandboxed live HTML preview of the current page, selected by default and
  downloadable through the same preview action used by PNG and SVG;
- current page HTML;
- complete-document self-contained HTML;
- a ZIP containing the directory bundle without flattening its paths.

No PDF, password, font or generated HTML is uploaded to a server.

## Poppler reference corpus

`tests/fixtures/html-alpha1-fixture.json` records the Poppler 26.07 source
components used as the semantic reference, the aligned behaviors and accepted
product differences. It freezes deterministic HTML byte counts and SHA-256
values for link annotations, an embedded TrueType subset in native-text mode,
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
- Browser glyph shapes may differ when the PDF has no decodable outline; each
  fallback glyph still retains its exact origin and source Unicode.
- Occlusion detection is intentionally conservative: overlapping later
  graphics can keep a complete PDF text-showing operation in SVG.
- Link rectangles are axis-aligned, matching the public annotation rectangle.
- Cross-page links can target a page omitted from a selected range; the href is
  retained but has no target element in that output.
- HTML currently uses the CropBox, matching the SVG renderer.
- Type 3 programs remain graphical. Managed TrueType web fonts are generated
  for decoded TrueType, OpenType, CFF and Type 1 outlines.
