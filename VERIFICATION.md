# Verification record

Verification performed on 2026-08-09 for `0.12.0-alpha.2`. The source was
derived from the final `Poppler.Net-26.07.0-0.12.0-alpha.1.zip` artifact,
SHA-256 `4c32a590cb1c2c2a868326c5c0ecf62fb5e6f34480bbdfc1919469ad7f58b606`.
The separately planned `0.11` shaping slice is not present.

- .NET SDK 8.0.423 compiled all four solution projects in Release with
  warnings treated as errors.
- NUnitLite executed 239 tests: 239 passed, 0 failed, 0 warnings and 0
  skipped.
- The managed-only verifier accepted production source and every asset in the
  restored NuGet graph.
- `Poppler.Net.0.12.0-alpha.2.nupkg` contains the Release net8.0 DLL/XML,
  README, release notes, license and notice.
- NuGet metadata identifies Mi Lattanzio as author and the public repository as
  `https://github.com/MiLattanzio/Poppler.Net`.
- The runtime graph contains only CoreJ2K 2.3.3.91,
  JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15.
- The Linux, Windows and macOS workflow parses as YAML, and `build.sh` passes
  shell syntax validation.

## Public API and version

The complete public-surface SHA-256 is
`221999f6699cf3963b99f1ce5bfe0474d208a64d3d3e1ba62afe08c4c99ac6b5`.
The fingerprint that normalizes only `Document.PortVersion` is
`c46d950b23a5b590b4bf0609688e3979e581aaf00b2624f199469be046029b4f`.

Relative to alpha 1, the callable surface adds only:

- `SvgFallbackMode`;
- `SvgRenderOptions.FallbackMode`;
- `SvgRenderOptions.RasterFallbackDpi`;
- `PdfReadOptions.MaximumRenderWorkingBytes`;
- `PdfReadOptions.MaximumSvgFallbackPixels`.

`Page.Graphics`, every public display-list element and all parser/text APIs
are unchanged. `Document.PortVersion`, library/CLI informational versions and
NuGet version all report `0.12.0-alpha.2`.

## Transparency corpus and numerical gates

The deterministic six-page `transparency-alpha2.pdf` corpus has SHA-256
`4776510211fc97ec94fce458806952fe2ecd33e14ad62738f70acc5d1c700a7a`.
Its approved manifest has SHA-256
`11a5e3d18c4c362b9f263bf1d26fe4a44e8f19e2d3a051b29a9e0e1ac9677a1c`.
The generator reproduces both files byte for byte.

The pages cover:

- all four isolated/knockout combinations;
- three-level group nesting and non-`Normal` boundary blends;
- all separable and nonseparable standard blend modes;
- Alpha and Luminosity masks, `/BC`, calculator `/TR` and partial clips;
- groups inside masks and masks inside groups;
- text, image, pattern, shading and annotation appearance paint;
- direct `1x1` and `2x2` formula samples.

Numerical tests inspect premultiplied color, composite alpha, shape and source
contribution independently. They prove that zero-alpha paint and a painted
color equal to the backdrop still contribute shape; non-isolated recovery and
knockout merge are tested without relying on screenshots. The `2x2` output is
frozen at `(191,159,223)`, `(128,128,255)`, `(128,0,128)` and white.

The manifest freezes managed PNG and default SVG SHA-256 values for all six
pages. An eight-task raster/SVG render of the same `Document` is byte-identical.
SVG is parsed as XML, its default complex-page fallback is an embedded PNG data
URI, `Omit` contains no fallback URI, and the pixel limit is forced before
allocation.

## Safety and bounded memory

Each high-precision raster surface stores six 32-bit values per pixel:
premultiplied RGB, composite alpha, shape and contribution alpha. The
per-render working-set budget reserves bytes before allocating the final
target, saved backdrops, group/knockout buffers or soft-mask caches and releases
the reservation with the owning surface. Regressions force both a direct
pre-allocation failure and a nested-group failure after multiple live surfaces.

`MaximumTransparencyGroupDepth` remains active for groups and soft masks; a
depth-one render of the three-level corpus fails with `PdfLimitException`.
Existing output-pixel, geometry, XObject, image and stream limits remain
unchanged.

## Independent Poppler comparison

Poppler 26.05.0 `pdfinfo`, `pdftotext` and `pdftoppm` open, extract and render
all six pages. Poppler is used only as an independent QA reference. At 72 DPI,
antialiasing 4 and an opaque backdrop, the normalized RGB mean absolute errors
are:

| Page | Error |
|---:|---:|
| 1 | 0.000255061 |
| 2 | 0.007452158 |
| 3 | 0.001080719 |
| 4 | 0.000622433 |
| 5 | 0.002404463 |
| 6 | 0.000980392 |

Original-resolution managed and Poppler contact sheets were inspected. Group
boundaries, overlaps, masks, clips, patterns, image samples, annotation paint
and all blend-grid cells are present without clipping or stray geometry. Page
4 uses the controlled managed Helvetica fixture; text, image, pattern and
shading paint all remain inside the transparency group.

## Historical compatibility and performance

Every historical manifest regression remains active. The one existing
isolated-group channel that uses a byte-quantized Poppler intermediate is
accepted within one unit: retaining high-precision group color rounds it to
128 instead of 127. No parser, text-extraction or public display-list behavior
changes.

All input ownership, culture, option-snapshot, diagnostic-snapshot and
shared-document concurrency gates remain active. The Release smoke workload
completed in 102.3 ms and allocated 13.7 MiB, inside the 30-second/512-MiB
budgets. The repeated decoded-stream test allocated 78.1 KiB with caching and
7,773.4 KiB with caching disabled.

## Distribution

The final source archive contains 212 files selected explicitly beneath one
`Poppler.Net/` root. It excludes repository metadata, build outputs, NuGet
packages, test results, temporary renders, generated bytecode, executables and
native assets.

The final ZIP is extracted into a new directory and compared byte for byte
with the selected source set. From that copy the solution is restored from the
five approved local managed packages, rebuilt without warnings, tested,
verified as managed-only, repackaged and exercised through the CLI.

The environment's `dotnet` CLI can intermittently fail while inspecting its
process namespace. Retrying the command, and using single-node MSBuild with
node reuse disabled, avoids `System.Diagnostics.Process.GetStat`; this is an
execution-environment issue, not a project or package error. The normal user
entry point remains:

```bash
./build.sh Release
```
