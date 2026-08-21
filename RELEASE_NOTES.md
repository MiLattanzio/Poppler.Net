# Poppler.Net 0.13.0-rc.1

Release date: 2026-08-21

`0.13.0-rc.1` is the publication candidate for the managed 0.13 conversion
line. It freezes the callable API, public option defaults, structured schemas,
HTML and structured manifest layouts, CLI help contract and synchronized
library/tool version metadata. No new conversion feature is introduced after
beta.2.

## Frozen release contract

- The version-normalized callable API remains byte-for-byte compatible with
  beta.2. Reflection-based regression tests now enforce both the callable and
  complete RC surfaces.
- Public defaults for PDF reading, raster/SVG/HTML rendering, HTML and
  structured export, and standalone-page extraction have a dedicated frozen
  fingerprint.
- Structured JSON schema and XML schema remain at version `1.0`; representative
  HTML and structured manifest shapes and their discriminators are frozen.
- CLI commands, switches, one-based page convention and help text are covered
  by a release-contract fingerprint.
- `Poppler.Net` and the `Poppler.Net.Cli` dotnet tool are both versioned
  `0.13.0-rc.1`; the library still targets .NET 8 and .NET 10 and the tool
  targets .NET 8 with major-version roll-forward.

## Qualification scope

- The historical and 0.13 conversion corpora cover fixed-layout HTML,
  standalone pages, JSON/XML/XHTML, safe image export, raster/SVG/text,
  annotations, forms, optional content, encryption and damaged input.
- Release gates cover deterministic concurrent reads/conversions, allocation
  and time budgets, managed-only source inspection, package contents,
  portable PDB/Source Link, extracted source and clean package/tool consumers.
- The WebAssembly playground remains a browser-local consumer of the same
  managed API, with bounded input, page, node, file, artifact and ZIP sizes.
- Poppler 26.07 remains the pinned source-level behavioral reference. No
  Poppler executable, native library or external process is a runtime/package
  dependency.

## Compatibility and limits

The declared 0.13 behavior and known limits are documented in
`docs/COMPATIBILITY.md`, `docs/CONVERSION_COMPATIBILITY.md`,
`docs/HTML_EXPORT.md`, `docs/PAGE_EXTRACTION.md` and
`docs/STRUCTURED_EXPORT.md`. OCR, semantic/reflow reconstruction, office
conversion, page merge, general PDF mutation, action execution, XFA and
encryption mutation remain outside 0.13 and are tracked in issue #40.

This is a prerelease. Stable `0.13.0` promotion requires a green Ubuntu,
Windows and macOS matrix for the exact candidate commit and completion of
`docs/RELEASE_CHECKLIST.md`.
