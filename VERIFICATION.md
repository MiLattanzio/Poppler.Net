# Verification record

Local verification performed on 2026-08-13 for `0.13.0-alpha.3` with .NET SDK
10.0.302 on Windows. The library continues to compile for both `net8.0` and
`net10.0`; the CLI tool targets `net8.0` with major-version roll-forward.

- Release solution build: 0 warnings, 0 errors.
- NUnitLite: 315 passed, 0 failed, 0 warnings, 0 skipped.
- Managed-only source and restored NuGet graph: passed.
- Library and CLI NuGet content/license/dependency/metadata verification:
  passed.
- Clean package consumers: passed on `net8.0` and `net10.0`.
- Packaged dotnet-tool install/version, HTML, page separation, structured JSON
  and structured bundle smoke tests: passed.
- Blazor WebAssembly Release build: passed.

Three-OS GitHub Actions qualification remains the promotion gate and is not
claimed by this local record.

## Packages

The local pipeline produced `Poppler.Net.0.13.0-alpha.3.nupkg` and
`Poppler.Net.Cli.0.13.0-alpha.3.nupkg`. Package verification accepts only the
expected managed DLL/XML files,
README, release notes, license, notice and NuGet metadata. Runtime dependencies
remain CoreJ2K 2.3.3.91, JBig2Decoder.NETStandard 1.5.2 and StbImageSharp
2.30.15.

## Public API

The reviewed alpha.3 callable public-surface SHA-256, normalizing only
`Document.PortVersion`, is:

`52722a22ee246fe22dbe8ffa07397b0a4287809f9ca80bf61fccb411e22e1c5d`

The complete surface, including version `0.13.0-alpha.3`, is:

`827845945d37bd10f6d90735857c8d47cc2eec643830e6ea23ff2918e105532c`

## Structured export qualification

Focused tests cover schema parsing/XML validation, deterministic JSON/XML/XHTML,
page ranges, stable identifiers, text geometry, resolved URI/internal links,
raw and normalized subset font names, original JPEG/JP2/JBIG2 selection,
mask/CCITT/sample PNG fallbacks, manifest hashes, safe paths and pre-growth
file/output limits.

The source-level behavior reference was reviewed in Poppler 26.07
`TextOutputDev`, `pdftotext`, `ImageOutputDev` and `pdfimages`. Intentional
differences and the schema `1.0` contract are recorded in
`docs/STRUCTURED_EXPORT.md`.

The HTML corpus uses canonical mode `v2`, which normalizes only the generator
version in addition to embedded PNG encoding. This prevents a release-number
change from invalidating otherwise byte-identical visual output.
