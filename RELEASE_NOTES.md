# Poppler.Net 0.12.0-beta.2

Release date: 2026-08-11

`0.12.0-beta.2` hardens the feature-complete `0.12` graphics line for hostile
inputs, concurrent read-only use, bounded resource growth and real package
consumption. It retains the beta.1 Poppler differential baseline and does not
add a native runtime or expand the declared rendering scope.

## Hostile-input hardening

Decoded ASCIIHex, ASCII85 and RunLength streams now check their byte budget
before every output write. Combined page content includes inserted separators
in the pre-growth calculation. Oversized but finite page boxes and raster
working arrays fail as `PdfLimitException` with stable diagnostics instead of
leaking arithmetic exceptions or attempting oversized allocations.

Three new cumulative controls complement the existing per-resource limits:

- `MaximumClipPaths` bounds clips retained by one graphics state;
- `MaximumPageImagePixels` bounds decoded image pixels across one display list;
- `MaximumPageMeshTriangles` bounds mesh triangles across one page.

Repeated non-mask Image XObjects with the same resource identity share one
immutable decoded image inside an interpretation pass. Stencil masks remain
color-dependent and are deliberately decoded per use.

The adversarial suite covers decoder growth, combined malformed content,
deep clip accumulation, reused and distinct images, multiple meshes, extreme
page boxes and working-surface array limits. Existing geometry, nested group,
soft-mask, shading-function and adaptive-refinement limits remain active.

## Concurrency and performance

A combined stress gate discovers fonts, images, text and display-list resources
while rendering all image/color corpus pages concurrently from one `Document`;
all summaries and raster hashes must match.

The six-page Release smoke gate now permits at most 5 seconds and 32 MiB of
managed allocations. The qualification measurement on Windows completed in
about 0.2 seconds with 10.1 MiB allocated, leaving runner variance without
making the gate too broad to catch a material regression.

## Distribution gates

CI now inspects the produced NuGet archive for an exact managed package shape,
GPL license metadata, repository commit metadata and the pinned three-package
runtime dependency set. A clean project restores the produced package and
renders PNG/SVG output as both `net8.0` and `net10.0` on Ubuntu, Windows and
macOS. The library package contains assemblies for both target frameworks;
the CLI, test, engineering and WebAssembly projects run on .NET 10.
Historical raster gates now hash canonical decompressed PNG content, avoiding
false failures when .NET 8 and .NET 10 emit different valid zlib streams for
identical pixels.

CI also creates a tracked-file source archive, extracts it into a clean
directory, restores, builds, runs the complete tests and managed-only verifier,
repacks the library and rechecks package metadata. NuGet publication waits for
all six operating-system/framework consumer jobs.

## Installation

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0-beta.2" />
```

## Deliberate limits

Advanced ICC LUT/device-link profiles, proofing, rendering intents, spot-color
overprint, native SVG mesh primitives and complex-script shaping remain outside
`0.12`. The project is read-only and does not write, edit or sign PDFs.
