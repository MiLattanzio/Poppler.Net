# Poppler.Net 0.13.0-alpha.2

Release date: 2026-08-13

`0.13.0-alpha.2` adds bounded managed standalone PDF page extraction. Poppler
26.07 `PDFDoc::savePageAs` and `pdfseparate` are development references; the
runtime remains managed-only and invokes no Poppler binary or subprocess.

## Page extraction API

- Added `Page.ExtractPdf`/`SavePdf` and
  `Document.ExtractPage`/`ExtractPages`/`SavePage`.
- Added immutable `PdfExtractedPage` values for zero-based source indexes,
  one-based source page numbers and complete autonomous PDF bytes.
- Added `PdfPageExtractionOptions` object-count, graph-depth, cumulative
  stream-byte and output-size limits plus annotation policy.
- Added an internal, deterministic writer for a minimal catalog, page tree,
  copied object graph, streams, classic xref table, trailer and EOF. No general
  mutation API was introduced.

## Preservation and safety policy

- Materialized inherited resources, media/crop/bleed/trim/art boxes and
  rotation into each output page.
- Preserved content streams, fonts, images, supported annotations, local page
  destinations and terminalized page widgets with inherited form values.
- Omitted outlines, name trees and document-level structures that cannot stay
  valid after isolation. Cross-page GoTo destinations and actions are removed.
- Normalized compressed objects into ordinary indirect objects and traversed
  cyclic reference graphs deterministically without recursive object emission.
- Locked inputs are rejected. Unlocked encrypted inputs are accepted and emit
  unencrypted output, including explicit stream crypt-filter normalization.

## CLI and playground

- Added `poppler-net separate <input.pdf> <output-dir>` with optional
  `--first-page` and `--last-page`, stable zero-padded page names and
  collision-safe paths.
- Added current-page autonomous PDF download and an all-pages PDF ZIP to the
  WebAssembly playground. Processing and ZIP creation stay inside the browser.

## Validation

- Added focused coverage for deterministic output, inherited resources and
  boxes, compressed objects, cycles, annotations, forms, encrypted input and
  every writer limit.
- Extracted representative and real-world outputs reopen with Poppler.Net and
  Poppler 26.07.

## Scope limits

Page merge/recomposition, arbitrary PDF mutation, incremental updates,
signing and output encryption remain out of scope. Alpha.2 always produces one
standalone PDF per selected source page. Structured JSON/XML/XHTML and original
image export are planned for `0.13.0-alpha.3`.
