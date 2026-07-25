# Changelog

## 0.2.0-alpha.1 — 2026-07-26

- Pinned the build to .NET SDK 8.0.423 and added three-platform CI.
- Centralized NuGet versions and migrated regression tests to managed xUnit v3.
- Added a build-time verifier for forbidden interop and native/mixed-mode
  binaries in the complete restored NuGet graph.
- Added header-relative xref/object offsets for PDFs with a leading prefix.
- Added strict indirect-object generation and compressed-object index checks.
- Hardened xref range validation before allocating or indexing entries.
- Kept configured resource-limit failures authoritative instead of retrying
  them through xref repair.
- Improved damaged-xref repair so parsed streams are skipped safely and xref
  streams/object streams can reconstruct compressed entries.
- Added incremental-update, repair, leading-prefix, generation, resource-limit
  and EOF-diagnostic regression fixtures.
- Added direct collection limits and normalized invalid Flate data to
  `PdfFormatException`.

## 0.1.0-alpha.1 — 2026-07-26

- Established Poppler 26.07.0 as the fixed upstream baseline.
- Added a managed PDF object model and bounds-checked syntax parser.
- Added classic xref, xref stream, hybrid, incremental and compressed-object
  stream support.
- Added Flate, LZW, ASCIIHex, ASCII85, RunLength and predictor decoding.
- Added catalog, page tree, inherited page attributes and page labels.
- Added document information, XMP access, ID, layout/mode and feature presence.
- Added embedded-file discovery and extraction.
- Added basic text extraction, simple font encodings and `ToUnicode` CMaps.
- Added a text-position SVG diagnostic renderer.
- Added a dependency-free CLI and executable regression-test project.
- Added explicit managed-only, compatibility, provenance and security limits.
