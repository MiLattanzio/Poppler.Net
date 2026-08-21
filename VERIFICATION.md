# Verification record

Local release qualification was performed on 2026-08-21 for
`0.13.0-rc.1` with .NET SDK 10.0.302 on Windows 10.0.26200. The library
compiles for `net8.0` and `net10.0`; the CLI tool targets `net8.0` with
major-version roll-forward.

- Release solution build: 0 warnings, 0 errors, including the Blazor
  WebAssembly playground.
- NUnitLite: 339 passed, 0 failed, 0 warnings, 0 skipped.
- Managed-only source and restored NuGet graph verification: passed.
- Library and CLI NuGet content, dependency, license, metadata, embedded
  portable-PDB and exact-revision Source Link verification: passed.
- Clean local-package consumers: passed on `net8.0` and `net10.0`, including
  HTML, schema `1.0`, standalone-page reopen and public limit failures.
- Packaged dotnet-tool install/version, HTML, selected-range HTML/JSON, page
  separation, normalized/raw font names and structured image bundle: passed.
- Packaged CLI negative limit smokes returned the expected exit code without
  leaving result artifacts.
- Source snapshot restore, build, managed-only verification, 339-test suite,
  repack and strict library/tool package verification: passed outside `.git`.
- Release tag guard: accepted `v0.13.0-rc.1` and rejected a mismatched tag.
- Browser smoke: the local WebAssembly app reported `0.13.0-rc.1`, loaded the
  demo PDF, rendered selectable HTML by default, exposed page/document
  downloads and produced no warning/error console entries.

The exact pushed commit must still pass GitHub Actions on Ubuntu, Windows and
macOS. That remote matrix, PR merge and prerelease publication are not claimed
by this local record.

## Frozen release contract

RC.1 adds no callable public member and changes no public option default from
beta.2. `ReleaseCandidateTests` enforces these SHA-256 fingerprints:

| Contract | SHA-256 |
| --- | --- |
| callable public API, version normalized | `082e5c6049186507381f039a20299754104b3f9c9cc5338a5619aab1e20ea52a` |
| complete public API with `0.13.0-rc.1` | `dd2c730d3d23353782d772880ace99e023fefa99008d5dfd296434e3c9cf180d` |
| public option defaults | `bebb562cea90592ee86bf2114893a1264a030c48ec295e38a1c14dbddb1bd3e2` |
| JSON/XSD schema files | `5b7efeb4e294e2ce9aef1193245808629bf30652f6ec3bf42119f09c95561035` |
| HTML/structured manifest shapes | `f417311ed5fa87cb034f07b9f06eebc458d03dad780689da744d87be87ba4b3b` |
| CLI help contract | `b1371a275a4ad3add72bd1543643c8c1b3dbcd4d6b5b8c75fad70a7f9a16363b` |

The callable hash is identical to beta.2. The three HTML corpus byte counts
changed by exactly two bytes because the embedded generator label changed
from `0.13.0-beta.2` to the two-character-shorter `0.13.0-rc.1`; canonical
rendering hashes remained unchanged.

## Performance and concurrency

The representative two-page HTML/structured/separation gate completed in
approximately 1.5-1.6 ms with 0.8 MiB allocated, below its 3 second/32 MiB
limits. The six-page release smoke completed in approximately 23-32 ms with
7.6 MiB allocated, below its 5 second/32 MiB limits. These values are local
regression guardrails, not cross-platform benchmarks.

The release corpus also covers deterministic concurrent reads, attachment
materialization and twelve concurrent conversion families. Cached repeated
page reads retained roughly 77 KiB versus about 8 MiB without the cache in the
local guard test.

## Packages

The local pipeline produced:

- `Poppler.Net.0.13.0-rc.1.nupkg` — SHA-256
  `627c390858d530ad3761dea26afd54c761fa18c4b7602dc73c31b349ff623a2a`;
- `Poppler.Net.Cli.0.13.0-rc.1.nupkg` — SHA-256
  `a3f5909709e572e2ff492dc592196e2a947af3f0c8e36e01a702cf94c795d740`.

These hashes identify local uncommitted qualification artifacts and will
change when repository/Source Link metadata records the final pushed commit.
Release artifacts must use and record hashes from the green CI run.

Runtime dependencies remain CoreJ2K 2.3.3.91,
JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15. Package inspection
accepts only the documented managed assemblies/XML files, embedded PDBs,
README, release notes, GPL license, provenance notice and exact NuGet metadata.

## Conversion and Poppler reference

The complete historical and 0.13 corpora cover fixed-layout HTML,
standalone-page PDFs, JSON/XML/XHTML, safe image exports, text/font mapping,
graphics, annotations, forms, optional content, encryption, damaged inputs and
resource limits.

The pinned Poppler 26.07 source reference was reviewed at
`C:\Users\mi\Documents\poppler-26.07.0`: `pdftohtml`/`HtmlOutputDev`,
`pdfseparate`/`PDFDoc::savePageAs`, `pdftotext`/`TextOutputDev` and
`pdfimages`/`ImageOutputDev` sources are present and match the classifications
in `docs/CONVERSION_COMPATIBILITY.md`. A live executable differential was not
run because no Poppler 26.07 binary installation is available; it remains an
optional development reproducer and is intentionally not a package or CI
dependency.

No known P0/P1 defect remains in the declared 0.13 scope. Semantic/reflow
HTML, page merge/recomposition, OCR, office reconstruction and additional
formats are explicitly deferred to GitHub issue #40.
