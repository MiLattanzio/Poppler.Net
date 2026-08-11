# Poppler.Net 0.13.0-alpha.1

Release date: 2026-08-11

`0.13.0-alpha.1` begins the managed conversion/export line with fixed-layout
HTML for individual pages, page ranges and complete documents. Poppler 26.07
`pdftohtml`/`HtmlOutputDev` behavior is a development reference; the runtime
remains managed-only and invokes no Poppler binary or subprocess.

## HTML API

- Added `Page.RenderToHtml`/`SaveHtml` for self-contained single-page output.
- Added `Document.RenderToHtml`/`SaveHtml` with zero-based range selection.
- Added `Document.CreateHtmlBundle`/`SaveHtmlBundle` and immutable
  `HtmlExportBundle` files for `index.html`, CSS, page SVGs, reusable fonts and
  `manifest.json`.
- Added `HtmlRenderOptions`, `HtmlExportOptions` and
  `HtmlTextLayerMode.InvisibleOverlay|Visible`.
- Emitted only the requested page or page range at the top-left origin, without
  viewer toolbar, labels, centering, inter-page gaps or decorative shadows.
- Preserved exact managed SVG rendering by default while always emitting a
  selectable/searchable Unicode DOM text layer.
- Converted visible URI/GoTo link annotations into safe HTML anchors without
  copying PDF JavaScript or other executable actions.
- Applied optional-content visibility to both page backgrounds and text.

## Fonts and packaging

- Normalized six-letter PDF subset prefixes in text-box font names, so values
  such as `ABCDEF+DejaVuSans` are exposed as `DejaVuSans`.
- Embedded reusable TrueType/OpenType programs in single-file output and
  deduplicated them into hash-named files in directory bundles.
- Retained CSS fallback families and selectable text when fonts are absent or
  unsupported by browsers.
- Added deterministic bundle paths and a versioned manifest.

## CLI and playground

- Added `poppler-net html` with page/range, bundle, text-mode, scale, embedded
  font, image/vector, fallback, title, layer and password options.
- Added current-page HTML, complete-document HTML and directory-bundle ZIP
  downloads to the WebAssembly playground. All processing remains local.
- Made the sandboxed HTML page renderer the default playground preview, with
  HTML, PNG and SVG selectable and downloadable from the same viewer controls.
- `Poppler.Net.Cli` is now a `net8.0` dotnet-tool package with major-version
  roll-forward and command name `poppler-net`:

  ```bash
  dotnet tool install --global Poppler.Net.Cli --version 0.13.0-alpha.1
  ```

## CI and publication

- The package job now produces and inspects both `Poppler.Net` and
  `Poppler.Net.Cli` with one synchronized version.
- Ubuntu, Windows and macOS install the locally produced CLI package, run its
  version command and convert a subset-font fixture to HTML.
- GitHub Release publication pushes both `.nupkg` files to NuGet.org through
  the existing trusted-publishing environment.
- Source-archive verification rebuilds, repacks and validates both packages.

## Scope limits

HTML is fixed-layout rather than semantic reflow. Page merge/split writing,
OCR, PDF JavaScript execution, arbitrary mutation, signing and output
encryption remain outside alpha.1. Standalone PDF page extraction is planned
for `0.13.0-alpha.2`.
