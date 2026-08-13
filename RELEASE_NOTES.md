# Poppler.Net 0.13.0-alpha.3

Release date: 2026-08-13

`0.13.0-alpha.3` completes the planned 0.13 feature set with managed,
versioned structured data and safe image exports. Poppler 26.07
`TextOutputDev`/`pdftotext` and `ImageOutputDev`/`pdfimages` remain development
references only; the runtime invokes no native library, utility or subprocess.

## Structured API and schemas

- Added `Document` and `Page` JSON, XML and XHTML export methods.
- Added `StructuredExportOptions`, `StructuredExportBundle` and immutable
  bundle files with page ranges, text-order selection and bounded output.
- Published JSON Schema draft 2020-12 and XSD contracts for schema `1.0`.
- Exported page geometry, ordered text boxes, normalized/raw fonts, resolved
  links and image metadata with deterministic page-scoped identifiers.

## Font and image fidelity

- `FontInfo.RawName` and `TextBox.RawFontName` retain values such as
  `ABCDEF+DejaVuSans`; normalized names remain separately available.
- `PdfImage.Export` retains JPEG, JPEG 2000 and JBIG2 bytes only when the
  encoded payload is independently reusable.
- Image/soft masks, `/Decode`, external color interpretation, JBIG2 globals,
  bare CCITT and decoded sample streams receive a managed PNG fallback with a
  bounded diagnostic reason.
- Structured bundles use stable collision-safe paths and a manifest containing
  media type, byte count and SHA-256 for every data/image file.

## CLI and WebAssembly playground

- Added `poppler-net json`, `xml`, `xhtml` and `export` commands with page
  ranges, text order, image omission and decoded-image controls.
- Added JSON, XML, XHTML and structured data/image ZIP downloads to the
  playground.
- Individual and ZIP image downloads now choose a safe original representation
  when available and PNG otherwise; browser processing remains fully local.

## Validation and compatibility

- Added deterministic schema, font-name, geometry, link, image, manifest,
  page-range and hostile output-limit tests.
- Recorded intentional differences from Poppler 26.07: Poppler.Net adds stable
  identifiers/manifests, composites masks into PNG, and declines raw streams
  that require PDF-only side parameters.
- The library continues to target `net8.0` and `net10.0`; the dotnet tool
  continues to target `net8.0` with major-version roll-forward.

OCR, semantic document reconstruction, office conversion, raw export with
lossy semantics, page merging and general PDF editing remain out of scope.
