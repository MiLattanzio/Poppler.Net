# Verification record

Local verification performed on 2026-08-15 for `0.13.0-beta.1` with .NET SDK
10.0.302 on Windows. The library continues to compile for both `net8.0` and
`net10.0`; the CLI tool targets `net8.0` with major-version roll-forward.

- Release solution build: 0 warnings, 0 errors.
- NUnitLite: 324 passed, 0 failed, 0 warnings, 0 skipped.
- Managed-only source and restored NuGet graph: passed.
- Library and CLI NuGet content/license/dependency/metadata verification:
  passed.
- Clean package consumers: passed on `net8.0` and `net10.0`, including HTML,
  schema `1.0` range export and standalone-page reopen.
- Packaged dotnet-tool install/version, HTML, selected-range HTML/JSON, page
  separation, normalized/raw font names and structured image-bundle smoke
  tests: passed.
- Blazor WebAssembly Release solution build: passed.
- Optional Poppler 26.07 differential script: Python syntax validation passed;
  executable comparison remains a development-only reproducer because the four
  Poppler utilities are not installed on this machine.

Three-OS GitHub Actions qualification remains the promotion gate and is not
claimed by this local record.

## Packages

The local pipeline produced `Poppler.Net.0.13.0-beta.1.nupkg` and
`Poppler.Net.Cli.0.13.0-beta.1.nupkg`. Package verification accepts only the
expected managed DLL/XML files, README, release notes, license, notice and
NuGet metadata. Runtime dependencies remain CoreJ2K 2.3.3.91,
JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15.

## Public API

The reviewed beta.1 callable public-surface SHA-256, normalizing only
`Document.PortVersion`, is unchanged from alpha.3:

`52722a22ee246fe22dbe8ffa07397b0a4287809f9ca80bf61fccb411e22e1c5d`

The complete surface, including version `0.13.0-beta.1`, is:

`f7cd31bc955fbdd55f7f0c1402ddf7e5b51baeddc6ff932aac34117a57d57b5c`

## Conversion compatibility qualification

The pinned matrix covers subset/missing fonts, annotations, forms,
CropBox/rotation, shared resource graphs, images/filter semantics,
encrypted/unlocked input, malformed optional metadata and hostile graphs.
Five representative source pages were extracted, reopened and compared with
their source for text, MediaBox/CropBox, rotation and exact managed raster
pixels.

HTML text remained available with embedded fonts, vector graphics, images and
raster fallbacks disabled. Missing optional information remained diagnostic;
malformed required content failed with a bounded `PdfFormatException`.
Structured schema `1.0`, page indexes, raw/normalized font names and image
bundle naming remained stable.

The source-level behavior reference was reviewed in Poppler 26.07
`pdftohtml`/`HtmlOutputDev`, `pdfseparate`/`PDFDoc::savePageAs`,
`pdftotext`/`TextOutputDev` and `pdfimages`/`ImageOutputDev`. The classifications
and accepted differences are recorded in
`docs/CONVERSION_COMPATIBILITY.md` and
`tests/fixtures/conversion-beta1-compatibility.json`.

Three private real-world documents that previously exposed missing-font text,
mirrored SVG text and damaged optional metadata also completed HTML,
structured export, standalone-page extraction and extracted-page rendering.
They are intentionally not part of the repository corpus.
