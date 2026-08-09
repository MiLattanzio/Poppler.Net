# Verification record

Verification performed on 2026-08-09 for `0.12.0-alpha.3`. This source builds
on the verified `0.12.0-alpha.2` transparency compositor and retains the
`0.12.0-alpha.1` stroke-outline work. The separately planned `0.11` shaping
slice is not present.

- .NET SDK 8.0.423 restored and compiled all four solution projects in Release
  with warnings treated as errors.
- NUnitLite executed 262 tests: 262 passed, 0 failed, 0 warnings and 0 skipped.
- The managed-only verifier accepted production source and every asset in the
  restored NuGet graph.
- `Poppler.Net.0.12.0-alpha.3.nupkg` is 421,443 bytes with SHA-256
  `17563cee912669a38a900b185683c9768397d3afed7040e631caa1acaf0492ff`.
- The package contains only the Release net8.0 DLL/XML, README, release notes,
  license, notice and NuGet metadata. No unexpected binary/native entry is
  present.
- NuGet metadata reports version `0.12.0-alpha.3`, author Mi Lattanzio,
  GPL-2.0-or-later and repository/project URL
  `https://github.com/MiLattanzio/Poppler.Net`.
- The runtime graph remains CoreJ2K 2.3.3.91,
  JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15.
- `build.sh` passes shell syntax validation.

## Public API and version

The complete public-surface SHA-256 is
`31d77bb8f4659f9d4d32c1f5a1675e1f832d0c0e5ccf67e58241414a266236fa`.
The fingerprint that normalizes only `Document.PortVersion` is
`cd82599822b9d301c9236b56a22cacb45a284d40498ce4b42a0c72e75a07af46`.

Relative to alpha 2, the callable surface adds only:

- `PdfShadingKind.FunctionBased`;
- `PdfFunctionShadingBrush` with `Kind`, `Domain`, `BoundingBox` and `Matrix`;
- `PdfFunctionShadingElement`.

Parametric mesh patches, shared edge objects and the adaptive tessellator are
internal. `PdfMeshShadingBrush.Triangles` remains the deterministic public
inspection representation. Parser, text-extraction and shaping APIs are
unchanged. `Document.PortVersion`, library/CLI informational versions and the
NuGet version all report `0.12.0-alpha.3`.

## Shading and mesh corpus

The deterministic five-page `shading-alpha3.pdf` corpus is 6,153 bytes with
SHA-256
`82634dc1ccc914125c0a26ae67144d6d95471edda4e77c1fd70e78c256135321`.
Its manifest is 2,957 bytes with SHA-256
`a9595b1b4d25f8449427453b4761e26943ce86929337fd78dac53f51831592aa`.
The generator reproduces both files byte for byte.

The pages cover:

- valid two-input type 1 sampled/calculator functions and component arrays;
- `/Domain`, `/Matrix`, `/BBox`, clipping and a singular matrix;
- type 2 exponential and type 3 stitching functions in valid one-input axial
  shadings;
- transformed/clipped thin and degenerate type 4/5 Gouraud meshes;
- adjacent high-curvature Coons patches and a tensor-product patch;
- meshes inside an isolated transparency group and a luminosity soft mask.

The manifest freezes all five opaque pages at 72, 96, 144 and 300 DPI with
antialiasing 4. PNG golden hashes cover the header and uncompressed filtered
pixel rows; SVG golden hashes replace embedded PNG compression with that same
content hash before hashing the document. Transparent output for the patch
page and SVG fallback output are frozen separately. Eight concurrent
raster/SVG renders of one page from one `Document` are byte-identical.

The historical `rendering-beta2.pdf` and `transparency-alpha2.pdf` generators
also reproduce their PDFs and manifests byte for byte. The alpha 2 manifest
attributes the current bounded output to `alpha3-bounded-fallback` and uses
the same compression-independent PNG/SVG content hashing. Its mixed-content
page 4 explicitly records the baseline plus the Windows and macOS CI variants;
all other alpha 2 and alpha 3 outputs retain a single approved content hash.

## Independent Poppler comparison

Poppler 26.05.0 `pdfinfo` and `pdftoppm` independently open and render all five
pages. The source-port semantic reference remains Poppler 26.07.0. At 72 DPI,
the normalized RGB mean absolute errors are:

| Page | Error |
|---:|---:|
| 1 | 0.002847324 |
| 2 | 0.004553343 |
| 3 | 0.003421773 |
| 4 | 0.004763651 |
| 5 | 0.003388957 |

Managed and Poppler output show the same gradients, clips, patch boundaries,
group paint and luminosity mask. The visible page-three difference is confined
to antialiasing of the intentionally thin/degenerate Gouraud construction.

## Safety, determinism and performance

Adaptive tessellation measures geometric error in device space and color
error separately. Connected patches share edge refinement decisions. The
triangle limit is checked before output growth, and the raster spatial index
prevents an every-triangle-per-pixel scan.

SVG fallback bounds conservatively include paths/strokes, text, images, clips,
function domains/BBoxes, mesh vertices/control hulls and nested groups. Bounds
are aligned to the full-page raster grid before pixel and working-set limits
are checked. Nonzero page rotation keeps the complete CropBox conservatively.

The Release smoke workload completed in 44.6 ms and allocated 10.1 MiB. The
repeated decoded-stream test allocated 78.1 KiB with caching and 8,175.6 KiB
with caching disabled. All ownership, option/diagnostic snapshot, culture and
historical manifest gates remain active.

## Distribution

The source archive is selected explicitly from 222 files beneath one
`Poppler.Net/` root. Repository metadata, IDE state, `bin`, `obj`, NuGet/build
artifacts, test results, temporary renders, generated bytecode, executables and
native assets are excluded.

The archive is extracted into a new temporary directory and compared byte for
byte with the selected source set. That extracted copy is restored from the
approved managed package cache, rebuilt without warnings, tested, verified as
managed-only, repackaged and exercised through the CLI. No push, tag, GitHub
release or NuGet publication is part of this verification.

## Beta 1 compatibility-closure baseline

The first `0.12.0-beta.1` slice adds a reproducible Poppler differential gate
over the existing geometry, transparency and shading corpora. Nineteen pages
are compared at 72 DPI using normalized RGB mean absolute error. Every page has
an explicit budget and a written classification in
`tests/fixtures/poppler-beta1-compatibility.json`; the manifest is checked by
the managed test suite and the optional
`tests/fixtures/verify_poppler_beta1_compatibility.py` runner reproduces the
native comparison without adding a runtime dependency.

All recorded measurements fit their approved budgets. The gate distinguishes
expected antialiasing, high-precision compositing and adaptive-tessellation
differences from the intentional odd-dash negative-phase semantic difference.
The Poppler 26.07.0 source remains the semantic reference, while the local
measurement renderer is Poppler 26.05.0.
