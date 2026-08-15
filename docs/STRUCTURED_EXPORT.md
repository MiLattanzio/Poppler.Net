# Structured data and image export

Poppler.Net `0.13.0-alpha.3` introduced structured-export schema `1.0`; beta.1
retains that contract unchanged. The
runtime is managed-only. JSON, XML, XHTML, images and manifests are generated
without calling Poppler utilities or native libraries.

## API and formats

`Document.ExportToJson`, `ExportToXml` and `ExportToXhtml` export a document or
a zero-based page range. The corresponding `Page` methods export one page.
For page methods, range members must retain their default values (or
`PageCount = 1`); text and image options remain applicable.
`Document.CreateStructuredBundle` adds selected image files and a deterministic
`manifest.json`; `SaveStructuredBundle` writes it with traversal protection.

JSON follows [structured-export-v1.schema.json](schemas/structured-export-v1.schema.json).
XML uses namespace `urn:poppler-net:structured:v1` and the companion
[structured-export-v1.xsd](schemas/structured-export-v1.xsd). XHTML uses the
standard XHTML namespace and represents the same ordered object/array data as
semantic description lists and ordered lists. Every format declares
`schemaVersion` `1.0`.

Document output includes the PDF version, original total page count, sorted
information entries and selected pages. Each page includes crop geometry,
rotation, joined text, ordered text boxes, fonts, links and images. IDs use
stable page-scoped forms such as `p0001-text-000001`,
`p0001-font-0001`, `p0001-link-0001` and `p0001-image-0001`.

Font `name`/`fontName` is normalized for display and matching. `rawName` and
`rawFontName` preserve the exact PDF value, including subset prefixes such as
`ABCDEF+DejaVuSans`. Text boxes also identify their PDF font resource and the
corresponding stable font ID.

## Images and manifest

`PdfImage.Export()` returns a `PdfImageExport` with extension, media type,
bytes, original/fallback state and a diagnostic reason. An original JPEG or
JPEG 2000 or self-contained JBIG2 payload is emitted only when it is independently reusable: no PDF
image/soft mask, `/Decode` transform or PDF-only color-space interpretation
may be required. Image masks, JBIG2 global segments, bare CCITT data, decoded sample
streams and unsafe color cases receive a managed PNG fallback.

Bundle image paths are collision-safe and deterministic:
`images/page-0001-0001-resource.ext`. `manifest.json` records path, media type,
byte count and lowercase SHA-256 for every other bundle file. The manifest
does not hash itself. `MaximumFiles` and `MaximumOutputBytes` are checked
before adding each file; decoder and pixel limits continue to come from
`PdfReadOptions`.

## CLI and playground

The CLI exposes `json`, `xml`, `xhtml` and `export` commands. All accept
`--page` or a `--first-page`/`--last-page` range, text ordering flags,
`--no-images`, and `--decoded-images`. `export` writes the complete bundle.

The WebAssembly playground downloads document JSON, XML, XHTML and a ZIP
bundle. Its image buttons use original JPEG/JP2 when safe and PNG otherwise;
all work and ZIP creation remain inside the browser.

## Poppler 26.07 comparison and intentional differences

The behavior reference is the source in `poppler/TextOutputDev.{cc,h}` and
`utils/pdftotext.cc` for raw, physical and reading-order text, plus
`utils/ImageOutputDev.{cc,h}` and `utils/pdfimages.cc` for image enumeration
and encoded formats.

- Poppler's `TextWordList` selects content-stream, physical or reading order.
  Poppler.Net maps these to `TextLayout.RawOrder`, `Physical` and
  `NonRawNonPhysical` and additionally emits stable geometry/font IDs.
- `pdfimages` can deliberately dump raw JBIG2 and CCITT payloads with side
  parameters. Poppler.Net emits JBIG2 only when no global segment dependency
  exists and falls back to PNG for parameter-dependent JBIG2 and bare CCITT.
- Poppler lists masks as separate image rows. Poppler.Net composites masks into
  the decoded image and never labels the uncombined source as reusable.
- Poppler's tools use sequential command-line roots. Poppler.Net uses
  page/resource-derived safe names and a hashed manifest shared by API, CLI
  and playground.

These differences are intentional parts of schema `1.0`, not attempts to
reproduce the textual console format of either utility.
