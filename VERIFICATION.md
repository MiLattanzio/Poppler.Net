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
over the geometry, transparency, shading and cross-feature corpora. Twenty-two
pages are compared at 72 DPI using normalized RGB mean absolute error. Every
page has an explicit budget and a written classification in
`tests/fixtures/poppler-beta1-compatibility.json`; the manifest is checked by
the managed test suite and the optional
`tests/fixtures/verify_poppler_beta1_compatibility.py` runner reproduces the
native comparison without adding a runtime dependency.

All recorded measurements fit their approved budgets. The gate distinguishes
expected antialiasing, high-precision compositing and adaptive-tessellation
differences from the intentional odd-dash negative-phase semantic difference.
The Poppler 26.07.0 source remains the semantic reference, while the local
measurement renderer is Poppler 26.05.0.

The second slice adds the deterministic three-page `compatibility-beta1.pdf`
corpus. It combines transformed dashed strokes with reused isolated groups; a
Coons mesh with a type 1 luminosity mask, even-odd clip and dashed boundary;
and both groups under a rotated CropBox. Canonical hashes freeze 72 DPI
opaque/transparent output and 72 DPI SVG fallbacks. The non-rotated
complex page embeds only its `244x148` painted support, while the rotated page
conservatively retains its complete `230x170` CropBox.

## Beta 1 release qualification

Release qualification performed on 2026-08-10 for `0.12.0-beta.1` uses .NET
SDK 8.0.423 with warnings treated as errors. NUnitLite executes 266 tests,
including the two new cross-feature compatibility tests. The managed-only
verifier accepts production source and the restored dependency graph.

The library and CLI versions, `Document.PortVersion` and NuGet metadata all
report `0.12.0-beta.1`. Local packaging contains only the Release net8.0
DLL/XML, README, release notes, license, notice and NuGet metadata. The runtime
dependency set remains CoreJ2K 2.3.3.91, JBig2Decoder.NETStandard 1.5.2 and
StbImageSharp 2.30.15. The complete public-surface SHA-256 is
`be515260264b76a8c2dd59df8d0052d0b71ca1635aa6d854a24e1a6fc230f21a`;
the version-normalized callable fingerprint remains
`cd82599822b9d301c9236b56a22cacb45a284d40498ce4b42a0c72e75a07af46`.

The pull-request gate repeats build, tests, managed-only verification and
packaging on Ubuntu, Windows and macOS. Publishing a matching GitHub
prerelease tag repeats those gates before NuGet.org trusted publishing.

## Beta 2 hardening qualification

Local qualification performed on 2026-08-11 for implementation commit
`4aa7bfc0b35110225981682d400827e4ae987364` uses .NET SDK 10.0.302 with
warnings treated as errors. The library builds `net8.0` and `net10.0` assets;
the other five solution projects build as `net10.0`. The managed-only verifier
accepts production source plus every restored dependency. NUnitLite on CLR
10.0.10 executes 285 tests: 285 pass with no failure, warning or skip. The five
historical PNG gates migrated to canonical decompressed-content hashes also
pass when the test assembly and library run on CLR 8.0.29.

The adversarial additions cover pre-growth ASCIIHex, ASCII85 and RunLength
limits, combined content separators, cumulative clip/image/mesh budgets,
reused Image XObjects, extreme finite page boxes and oversized working arrays.
A 16-operation shared-document stress gate produces identical font, image,
text, display-list and raster summaries. The six-page Release smoke allocates
7.6 MiB and completes in 24–40 ms across repeated local runs, below the new
32 MiB and 5 second gates. The complete public-surface SHA-256 is
`334fb37308370eb72515706bdbb25727670a1bcf8498d530c355eea6eafba432`;
the version-normalized callable fingerprint is
`ce87b22579e9458c3c1dcdb1aa01790f15d13006ad177ad4815f1e974e63b527`.

`Poppler.Net.PackageVerifier` accepts the beta.2 NuGet content allowlist,
GPL-2.0-or-later metadata, repository commit and the pinned CoreJ2K,
JBig2Decoder.NETStandard and StbImageSharp dependency set for both `net8.0`
and `net10.0`. A project with no source reference restores that local package
from an isolated cache and renders PNG/SVG output on both targets. The PNG
file hashes are
`288113db35fd02ced30e623e8ac7f2a58055a4607c311850f26a43e23de7af03`
on CLR 8 and
`578873f57b3a0125e4a6b7da9baf37ebe685393e402a6aea0a7c078eba5aefc2`
on CLR 10; their difference is the valid zlib stream, not pixel content.

`git archive` produces a single `Poppler.Net/` root from tracked files. A new
directory extracted from that archive restores, builds all six projects, runs
all 285 tests, passes the managed-only verifier, repacks beta.2 with the source
revision and passes package verification again. GitHub Actions repeats the
package consumer for both `net8.0` and `net10.0` on Ubuntu, Windows and macOS;
pull-request [CI run 48](https://github.com/MiLattanzio/Poppler.Net/actions/runs/31508463279)
passes all three build/test and managed-only jobs, the package/extracted-source
job, and all six operating-system/framework consumer jobs.
