# Verification record

Local stable-candidate qualification was performed on 2026-08-21 for
`0.13.0` with .NET SDK 10.0.302 on Windows 10.0.26200. The library compiles
for `net8.0` and `net10.0`; the CLI tool targets `net8.0` with major-version
roll-forward.

- Release solution build: 0 warnings, 0 errors, including the Blazor
  WebAssembly playground.
- NUnitLite: 339 passed, 0 failed, 0 warnings, 0 skipped.
- Managed-only source and restored NuGet graph verification: passed.
- Library and CLI NuGet content, dependency, license, metadata, embedded
  portable-PDB and Source Link verification: passed.
- Clean local-package consumers: passed on `net8.0` and `net10.0`, including
  HTML, schema `1.0`, standalone-page reopen and public limit failures.
- Packaged dotnet-tool install/version, HTML, selected-range HTML/JSON, page
  separation, normalized/raw font names and structured image bundle: passed.
- Packaged CLI negative limit smokes returned the expected exit code without
  leaving result artifacts.
- A candidate snapshot assembled outside `.git` restored, built, passed the
  managed-only verifier and all 339 tests, repacked both packages, passed both
  strict package verifiers, exercised `net8.0`/`net10.0` consumers and
  exercised the installed CLI tool.
- Release tag guard: accepted `v0.13.0` and rejected a mismatched tag.
- Browser smoke: the local WebAssembly app reported `0.13.0`, loaded the demo
  PDF, rendered selectable HTML by default, exposed HTML/PNG/SVG and document
  downloads, and produced no warning/error console entries.

The stable change is not committed or pushed by this local record. The exact
pushed revision must still pass GitHub Actions on Ubuntu, Windows and macOS.
PR merge, stable tag, GitHub release and NuGet publication are not claimed.

## RC approval evidence

The stable branch starts from approved `master` commit
`386a031f2840cdf0e3d04e11f17646849c26764f`, the merge result of PR #41.
The RC.1 `master` workflow run #83 and release workflow run #84 completed
successfully; issue #33 and its milestone are closed. No stable-candidate
change adds a feature or fixes a release blocker.

## Frozen release contract

Stable 0.13.0 adds no callable public member and changes no public option
default from RC.1. `ReleaseCandidateTests` enforces these SHA-256
fingerprints:

| Contract | SHA-256 |
| --- | --- |
| callable public API, version normalized | `082e5c6049186507381f039a20299754104b3f9c9cc5338a5619aab1e20ea52a` |
| complete public API with `0.13.0` | `62051a5542175bf1a0987d592745dc0a822e3e03e319180629095db343344f0e` |
| public option defaults | `bebb562cea90592ee86bf2114893a1264a030c48ec295e38a1c14dbddb1bd3e2` |
| JSON/XSD schema files | `5b7efeb4e294e2ce9aef1193245808629bf30652f6ec3bf42119f09c95561035` |
| HTML/structured manifest shapes | `f417311ed5fa87cb034f07b9f06eebc458d03dad780689da744d87be87ba4b3b` |
| CLI help contract | `b1371a275a4ad3add72bd1543643c8c1b3dbcd4d6b5b8c75fad70a7f9a16363b` |

Only the complete public API hash changed, as expected from
`Document.PortVersion`. The callable/default/schema/manifest/CLI hashes are
identical to RC.1. The three HTML corpus byte counts decreased by exactly five
bytes because the embedded generator label changed from `0.13.0-rc.1` to
`0.13.0`; canonical rendering hashes remained unchanged.

## Performance and concurrency

The representative two-page HTML/structured/separation gate completed in
approximately 2.6 ms with 0.8 MiB allocated, below its 3 second/32 MiB limits.
The six-page release smoke completed in approximately 33.3-33.8 ms with
7.6 MiB allocated, below its 5 second/32 MiB limits. These values are local
regression guardrails, not cross-platform benchmarks.

The release corpus also covers deterministic concurrent reads, attachment
materialization and twelve concurrent conversion families. Cached repeated
page reads retained roughly 77 KiB versus about 8 MiB without the cache in the
local guard test.

## Packages

The local working-tree pipeline produced:

- `Poppler.Net.0.13.0.nupkg` — SHA-256
  `acbc239627a5291fa72d39e0e0889e71f2c510a5ca776743ad94b11a6fdd6da3`;
- `Poppler.Net.Cli.0.13.0.nupkg` — SHA-256
  `7cfb13286076de561cb5605f91f5603567728aebc77ba8eb1e9ed395952cda12`.

These hashes identify local uncommitted qualification artifacts. They will
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
