# Poppler.Net 0.12.0-alpha.3

Release date: 2026-08-09

`0.12.0-alpha.3` completes the shading slice planned for the `0.12` line. It
adds function-based shading type 1, replaces fixed patch subdivision at render
time with deterministic adaptive tessellation, and restricts complex SVG
fallbacks to conservative painted bounds.

This release builds on the verified `0.12.0-alpha.2` transparency compositor.
It remains a managed-only, read-only port of Poppler 26.07.0 and does not
include the separately planned `0.11` shaping work.

## Function-based shading type 1

The public display list adds:

- `PdfShadingKind.FunctionBased`;
- `PdfFunctionShadingBrush`;
- `PdfFunctionShadingElement`.

Type 1 shadings retain `/Domain`, `/Matrix` and optional `/BBox`, accept either
one two-input multicomponent function or one two-input function per color
component, and preserve clipping and graphics-state transforms. Sampled type 0
and bounded calculator type 4 functions provide the valid two-input function
families used by type 1. Singular transforms are skipped deterministically.

Exponential type 2 and stitching type 3 are one-input function families by PDF
definition, so Poppler also rejects them as the two-input function of a type 1
shading. They remain supported for gradients, masks and tint transforms.

## Adaptive patch meshes

Type 6 Coons and type 7 tensor-product mesh readers now retain their original
4×4 parametric control grids, corner colors and shared edge objects. The
existing public `Triangles` view remains deterministic for inspection.

Raster rendering tessellates retained patches against the complete
user-to-device transform. Geometry error is measured in device space, color
error is evaluated independently, and connected patches share the same edge
subdivision decision. A hard internal refinement cap and
`PdfReadOptions.MaximumMeshTriangles` are enforced before output growth. A
bounded spatial index avoids scanning every adaptive triangle for every pixel.

## Bounded SVG fallback

Axial and radial shadings remain native SVG gradients. Type 1, type 4–7 meshes
and transparency semantics without a faithful SVG representation use the
managed raster backend.

Alpha 3 calculates a conservative page-space support from paths and stroke
widths, text outlines, images, clips, function domains/BBoxes, mesh vertices
or patch control hulls and nested groups. The support is intersected with the
CropBox and aligned to the same pixel grid as a full-page render. Only that
region is rasterized and embedded as a PNG data URI with matching SVG
coordinates. Nonzero page rotation conservatively retains the complete
CropBox to preserve historical output coordinates.

`MaximumSvgFallbackPixels`, `MaximumRenderPixels` and
`MaximumRenderWorkingBytes` are applied to the cropped allocation before its
surface is created. `SvgFallbackMode.Omit` is unchanged and no external
resource is emitted.

## Corpus and compatibility

The deterministic five-page `shading-alpha3.pdf` corpus covers:

- type 1 sampled/calculator functions and component arrays;
- `/Domain`, `/Matrix`, `/BBox`, clips and a singular matrix;
- valid type 2 exponential and type 3 stitching functions in axial shadings;
- transformed/clipped thin and degenerate Gouraud meshes;
- adjacent high-curvature Coons patches and a tensor patch;
- meshes inside an isolated group and a luminosity soft mask.

Managed PNG content hashes are frozen at 72, 96, 144 and 300 DPI. They include
the PNG header and uncompressed pixel rows, while SVG hashes canonicalize any
embedded PNG in the same way. This preserves exact rendered-content checks
without treating platform-specific zlib output as a rendering change.
Transparent patch output, bounded SVG data URIs and eight-way raster/SVG
concurrency are frozen separately. Poppler 26.05 independently opens and
renders every page. At 72 DPI the normalized RGB mean absolute errors are
`0.00285`, `0.00455`, `0.00342`, `0.00476` and `0.00339`; the most visible
difference is confined to antialiasing of the intentionally thin/degenerate
triangle.

All historical corpus tests remain active. The alpha 2 transparency corpus
now records the alpha 3 bounded-fallback output with the same canonical
render-content hashing used by the new shading corpus. Its legacy mixed-content
page has a small explicit set of approved runtime variants; any unrecognized
pixel output still fails the manifest gate.

## Installation

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0-alpha.3" />
```

The runtime dependency set is unchanged: CoreJ2K 2.3.3.91,
JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15. All are managed.

## Deliberate limits

Advanced ICC LUT/device-link profiles, proofing, rendering intents and
spot-color overprint remain outside `0.12`. Native SVG mesh primitives are not
emitted. Complex-script shaping, contextual GSUB, GPOS and font variations
remain assigned to the separate shaping line. The project is read-only and
does not write, edit or sign PDFs.
