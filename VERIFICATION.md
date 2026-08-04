# Verification record

Verification performed on 2026-08-04 for `0.12.0-alpha.1`. The source was
derived from the final `Poppler.Net-26.07.0-0.10.0-alpha.1.zip` artifact,
SHA-256 `6c0bda3766f825693678ac5aa0a91f19e83b553d1bc01e4c7acb7eb2c1842e43`.
No `0.11.0` implementation was present in the supplied workspace.

- .NET SDK 8.0.423 compiled all four solution projects in Release with
  warnings treated as errors.
- NUnitLite executed 224 tests: 224 passed, 0 failed, 0 warnings and 0
  skipped.
- The managed-only verifier accepted production source and every asset in the
  restored NuGet graph.
- `Poppler.Net.0.12.0-alpha.1.nupkg` contains the Release net8.0 DLL/XML,
  README, release notes, license and notice.
- NuGet metadata identifies Mi Lattanzio as author and the public repository as
  `https://github.com/MiLattanzio/Poppler.Net`.
- The runtime graph contains only CoreJ2K 2.3.3.91,
  JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15.
- The Linux, Windows and macOS workflow parses as YAML, and `build.sh` passes
  shell syntax validation.

## Public API and version

The complete public-surface SHA-256 is
`4c931f2f8458513f1fa7722fa934ecc98245c88c50d6269e806345c54a6aa5f1`.
The fingerprint that normalizes only `Document.PortVersion` is
`23300ba9dca5e9bb8557924343035a8ac801c9df3244f3de21e26138387c2ede`.

The sole added public member relative to `0.10.0-alpha.1` is
`PdfReadOptions.MaximumRasterGeometrySegments`. `Page.Graphics`, every public
display-list element and all parser/text APIs are unchanged.

`Document.PortVersion`, library/CLI informational versions and NuGet version
all report `0.12.0-alpha.1`.

## Geometry corpus and safety

The deterministic eight-page `raster-geometry-alpha1.pdf` corpus has SHA-256
`32fb3960c725637ea4de1a03c27f1f381d57f549a89f12398bab5fd19c6fdf66`.
Its approved manifest has SHA-256
`9d3be11ee432c2e0d080729ac39b9a3cb2157f834bf59e96b15a88d12c41e7dc`.
The generator reproduces both files byte for byte.

The pages cover:

- cap, join and miter-limit combinations at multiple widths;
- zero-length lines and zero-width hairlines;
- negative dash phase, odd patterns, zero-length elements and closed seams;
- anisotropic scale, shear and reflection;
- tight cubics, cusps and exact reversals;
- self-intersection and near-collinearity;
- nested nonzero/even-odd clips at page boundaries;
- CropBox-edge clipping combined with page rotation.

The manifest records 64 managed outputs: all eight pages at 96 and 300 DPI,
antialiasing 1 and 4, and opaque and transparent backgrounds. A repeated
eight-task render of the same `Document` is byte-identical.

The geometry limit is cumulative across flattening segments, dash fragments,
stroke-outline edges and temporary clip geometry. The regression forces a
limit failure only after several individually valid paths, verifies
`PdfLimitException`, and measures less than 2 MiB of allocation before the
exception. Cubic subdivision has an internal depth cap of 16; round geometry
has an internal 4,096-edge cap.

## Independent Poppler comparison

Poppler 26.05.0 `pdfinfo`, `pdftotext` and `pdftoppm` open, extract and render
all eight pages. Poppler is used only as an independent QA reference. The
following normalized mean absolute errors compare 96-DPI, antialiasing-4,
opaque renders with Poppler; page 8 uses its CropBox.

| Page | `0.10.0-alpha.1` | `0.12.0-alpha.1` | Result |
|---:|---:|---:|---|
| 1 | 0.00360004 | 0.00320515 | improved |
| 2 | 0.00321823 | 0.00320826 | improved |
| 3 | 0.01511370 | 0.01479450 | improved |
| 4 | 0.10105300 | 0.00320114 | improved |
| 5 | 0.00162062 | 0.00161236 | improved |
| 6 | 0.00478453 | 0.00251939 | improved |
| 7 | 0.00481667 | 0.00481667 | equivalent |
| 8 | 0.00222807 | 0.00221778 | improved |

The largest improvement is the anisotropic/shear/reflection page, where the
old average-width approximation is removed. Original-resolution contact sheets
were inspected for both implementations; no clipping, overlap or stray
geometry is present.

## Historical compatibility

Every historical managed raster hash remains unchanged except the three
intentional pages recorded in their versioned manifests:

- AcroForm alpha 2 page 2, whose widget-button stroke uses the new outline;
- robustness beta 2 page 1, which exercises line caps;
- robustness beta 2 page 5, which exercises joins and miter limits.

The AcroForm and robustness corpus generators preserve both the updated hashes
and their reasons while reproducing the PDF bytes exactly. No production file
under the parser, text extraction, images, color or forms subsystems changed.

## Concurrency and performance

All ownership, culture, option-snapshot, diagnostic-snapshot and
shared-document concurrency gates remain active. The Release smoke workload
completed in 93.6 ms and allocated 10.5 MiB, inside the 30-second/512-MiB
budgets. The repeated decoded-stream test allocated 78.1 KiB with caching and
7,768.3 KiB with caching disabled.

## Distribution

The final source archive contains 206 files selected explicitly beneath one
`Poppler.Net/` root. It excludes repository metadata, build outputs, NuGet
packages, test results, temporary renders, generated bytecode, executables and
native assets.

The final ZIP is extracted into a new directory and compared byte for byte
with the selected source set. From that copy the solution is restored from the
five approved local managed packages, rebuilt without warnings, tested,
verified as managed-only, repackaged and exercised through the CLI.

The environment's `dotnet` CLI can intermittently fail while inspecting its
process namespace. Running MSBuild single-node with node reuse disabled and
the NUnitLite executable directly avoids `System.Diagnostics.Process.GetStat`;
this is an execution-environment issue, not a project or package error. The
normal user entry point remains:

```bash
./build.sh Release
```
