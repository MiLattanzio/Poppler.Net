# Poppler.Net 0.13.0-beta.2

Release date: 2026-08-16

`0.13.0-beta.2` hardens the managed HTML, standalone-page PDF, structured-data
and image-export features introduced in the 0.13 line. It adds cumulative
budgets, adversarial coverage, concurrent-conversion qualification and
package/source checks without adding a native runtime or a general PDF
mutation surface.

## Bounded export pipelines

- HTML exports now bound selected pages, generated DOM/SVG nodes, bundle
  files, embedded font bytes and cumulative UTF-8 output before growth.
- JSON, XML and XHTML now apply output and semantic-node limits to standalone
  results as well as bundles; structured bundles enforce one cumulative file
  and byte budget, including their manifest.
- Multi-page PDF extraction now limits selected pages and the cumulative bytes
  retained by the complete operation in addition to per-page writer limits.
- CLI options expose every new export limit. Output file names normalize both
  directory separators, control/invalid characters and Windows reserved names.
- Limit failures use stable `PdfLimitException` diagnostics and are covered by
  hostile image-resource names, undersized budgets and pre-allocation tests.

## Concurrency, performance and playground

- Twelve concurrent conversion families from one read-only document produce
  isolated, byte-identical results in the beta.2 regression corpus.
- Release baselines cover conversion time and managed allocation; the new
  two-page export baseline remains below 3 seconds and 32 MiB locally.
- The WebAssembly playground applies tighter browser-specific input, page,
  node, file and output budgets, reports them in the UI, and bounds ZIP growth.
- Preview and download object URLs are tracked and revoked after use or page
  teardown, avoiding retained large blobs during repeated multi-file exports.

## Packaging and qualification

- Clean NuGet consumers on `net8.0` and `net10.0` now exercise the public
  export limits as well as successful conversions.
- Package verification checks embedded portable PDBs and Source Link mappings
  for both library target frameworks and the packaged dotnet tool, including
  deterministic Source Link generation from the extracted source archive.
- Packaged CLI smoke tests exercise deterministic limit failures on Ubuntu,
  Windows and macOS in addition to existing conversion commands.
- The public API fingerprints are intentionally updated for the new limit
  options. Structured schema `1.0` and all generated manifest formats remain
  unchanged.

OCR, semantic reconstruction, office conversion, page merging, general PDF
editing, action execution, XFA and encryption mutation remain out of scope.
