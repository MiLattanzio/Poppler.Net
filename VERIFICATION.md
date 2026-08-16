# Verification record

Local verification performed on 2026-08-16 for `0.13.0-beta.2` with .NET SDK
10.0.302 on Windows. The library compiles for both `net8.0` and `net10.0`; the
CLI tool targets `net8.0` with major-version roll-forward.

- Release solution build: 0 warnings, 0 errors, including the Blazor
  WebAssembly playground.
- NUnitLite: 335 passed, 0 failed, 0 warnings, 0 skipped.
- Managed-only source and restored NuGet graph: passed.
- Library and CLI NuGet content, license, dependency, metadata, embedded
  portable-PDB and exact-commit Source Link verification: passed.
- Clean package consumers: passed on `net8.0` and `net10.0`, including HTML,
  schema `1.0` range export, standalone-page reopen and public limit failures.
- Packaged dotnet-tool install/version, HTML, selected-range HTML/JSON, page
  separation, normalized/raw font names and structured image-bundle smoke
  tests: passed.
- Packaged CLI negative smokes for HTML page, structured node and separated
  page limits returned exit code 1 without leaving result artifacts.

Three-OS GitHub Actions qualification remains the promotion gate and is not
claimed by this local record.

## Export-hardening qualification

The beta.2 corpus adds 11 focused cases spanning pre-allocation page checks,
standalone and cumulative output bounds, HTML DOM/file budgets, structured
node/file budgets, hostile resource names, deterministic diagnostics and 12
concurrent conversion families from one read-only document.

The two-page representative HTML/structured/separation workload completed in
approximately 1.8-2.1 ms with 0.8 MiB of managed allocations, below its 3
second and 32 MiB gates. The six-page release smoke completed in approximately
31-41 ms with 7.6 MiB allocated, below its 5 second and 32 MiB gates. These
numbers are guardrails for regression detection, not platform benchmarks.

The WebAssembly playground uses a 64 MiB input limit, 512-page selection
limit, 500,000-node limit, 2,048-file limit and 128 MiB artifact/ZIP limit.
Preview and download object URLs are released after use and on page teardown.

## Packages

The local pipeline produced `Poppler.Net.0.13.0-beta.2.nupkg` and
`Poppler.Net.Cli.0.13.0-beta.2.nupkg`. Package verification accepts only the
expected managed assemblies/XML files, README, release notes, license, notice
and NuGet metadata. Runtime dependencies remain CoreJ2K 2.3.3.91,
JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15.

The portable PDBs embedded in library `net8.0`/`net10.0` and tool assemblies
contain Source Link mappings to the repository and the exact package commit.
Extracted archives receive the same deterministic mapping through an explicit
`SourceArchiveBuild`/`SourceRevisionId` build contract and do not require
access to a `.git` directory.

## Public API

Beta.2 intentionally adds only reviewed limit members to the existing HTML,
structured-export and page-extraction option records. The callable public
surface SHA-256, normalizing only `Document.PortVersion`, is:

`082e5c6049186507381f039a20299754104b3f9c9cc5338a5619aab1e20ea52a`

The complete surface, including version `0.13.0-beta.2`, is:

`d898dc1482df82df570e0db71892eb19340ec651b491f74bec52062c92580948`

Structured schema `1.0`, manifest layouts and conversion method families are
unchanged from beta.1.

## Conversion compatibility

The beta.1 matrix remains authoritative for subset/missing fonts,
annotations, forms, CropBox/rotation, shared resource graphs, images/filter
semantics, encrypted/unlocked input and malformed optional metadata. Beta.2
extends it with bounded hostile names and resource/output pressure while
preserving the qualified output behavior.

The source-level behavior reference remains Poppler 26.07
`pdftohtml`/`HtmlOutputDev`, `pdfseparate`/`PDFDoc::savePageAs`,
`pdftotext`/`TextOutputDev` and `pdfimages`/`ImageOutputDev`. No Poppler
executable or native library is a runtime, package or CI dependency.
