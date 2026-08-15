# Poppler.Net 0.13.0-beta.1

Release date: 2026-08-15

`0.13.0-beta.1` closes conversion compatibility across the managed HTML,
standalone-page PDF, structured-data and image-export features introduced by
the 0.13 alpha releases. It adds no native runtime and no general PDF mutation
surface.

## Conversion compatibility

- Added a pinned cross-feature matrix for subset and missing fonts,
  annotations, forms, rotations/page boxes, images, encrypted/unlocked input,
  malformed optional metadata and bounded hostile resource graphs.
- Classified every representative difference against Poppler 26.07
  `pdftohtml`, `pdfseparate`, `pdftotext` and `pdfimages` behavior.
- Added an optional development-only executable differential while retaining a
  fully managed runtime and CI graph.
- Qualified selected HTML/structured ranges against the same source-page
  identity and verified standalone pages by reopening and comparing managed
  raster pixels, boxes, rotation and extracted text with their source.

## API, CLI and playground alignment

- Kept structured-export schema `1.0` and the alpha.3 public API unchanged.
- Added page-level JSON, XML and XHTML downloads to the WebAssembly playground
  beside the existing current-page HTML and autonomous PDF downloads.
- Extended packaged CLI smoke tests on Ubuntu, Windows and macOS to cover page
  and range HTML/JSON, structured image bundles and normalized/raw font names.
- Extended clean NuGet consumers on `net8.0` and `net10.0` to export schema
  `1.0` ranges and reopen standalone pages in addition to PNG/SVG/HTML.

## Correctness and boundaries

- HTML source text remains available when fonts are absent or embedding,
  vectors, images and raster fallbacks are disabled.
- Unlocked encrypted input converts normally; extracted pages are intentionally
  autonomous and unencrypted.
- Optional metadata damage remains diagnostic-only, while malformed required
  streams and configured resource limits fail deterministically.
- No known P0/P1 correctness defect remains in the declared 0.13 conversion
  scope after local qualification. Three-OS CI is the final promotion gate.

OCR, semantic reconstruction, office conversion, page merging, general PDF
editing, action execution, XFA and encryption mutation remain out of scope.
