# Managed graphics engine

Version `0.5.0-alpha.1` ports the first backend-neutral slice of Poppler
26.07.0 `Gfx`, `GfxState`, `Function`, pattern and XObject behavior. It parses
page content into immutable managed objects; it does not call Poppler, Cairo,
FreeType or another native renderer.

## Execution model

`Page.Graphics` lazily interprets the page content stream and returns a display
list of:

- `PdfPathElement` for filled and/or stroked paths;
- `PdfImageElement` for Image XObject metadata;
- `PdfShadingElement` for directly painted gradients.

Every element retains a `PdfGraphicsState`, its active clipping paths and its
source Form resource. Form XObjects are flattened into the page display list
while preserving their resource path.

The managed SVG backend consumes the same list. The text extractor remains a
separate pass until the font-outline and raster slices are available.

## Operators

| Area | Operators or keys |
| --- | --- |
| Save/restore and CTM | `q`, `Q`, `cm` |
| Stroke parameters | `w`, `J`, `j`, `M`, `d` |
| Paths | `m`, `l`, `c`, `v`, `y`, `h`, `re` |
| Painting | `S`, `s`, `f`, `F`, `f*`, `B`, `B*`, `b`, `b*`, `n` |
| Clipping | `W`, `W*` |
| Device color | `G`, `g`, `RG`, `rg`, `K`, `k`, `CS`, `cs`, `SC`, `sc`, `SCN`, `scn` |
| Resources | `gs`, `Do`, `sh` |
| `ExtGState` | `LW`, `LC`, `LJ`, `ML`, `D`, `CA`, `ca`, `BM` |

Clipping follows PDF delayed semantics: `W`/`W*` records the rule and the
current path becomes part of the clip only when a path-ending operator is
processed. Clips are copied by `q` and restored by `Q`.

## XObjects

Form XObjects support:

- `/Matrix` concatenation;
- `/BBox` clipping;
- local `/Resources` with inherited fallback;
- nested Form XObjects;
- decoded Form content streams;
- recursion detection and a configurable depth limit.

Image XObjects record resource name, width, height, bits per component,
color-space name, mask flag and CTM. Pixel decoding belongs to release `0.6`;
SVG can draw the transformed unit-square boundary for diagnostics.

## Patterns and shading

Colored tiling patterns (`PatternType 1`, `PaintType 1`) interpret their own
content stream and resources into a nested display list. The pattern BBox,
steps and matrix are retained.

Shading patterns and direct `sh` painting support:

- axial shading type 2;
- radial shading type 3;
- DeviceGray, DeviceRGB and DeviceCMYK;
- function type 2 exponential interpolation;
- function type 3 stitching, including function arrays;
- `/Extend` flags and bounded generated stops.

Uncolored tiling patterns, sampled/calculator functions and function/triangle/
patch mesh shadings remain explicit limitations.

## Resource limits

`PdfReadOptions` adds:

| Property | Default |
| --- | ---: |
| `MaximumGraphicsOperations` | 1,000,000 |
| `MaximumGraphicsElements` | 250,000 |
| `MaximumPathSegments` | 1,000,000 |
| `MaximumGraphicsStateDepth` | 256 |
| `MaximumXObjectDepth` | 32 |
| `MaximumShadingStops` | 33 |

Limit failures throw `PdfLimitException` and are covered by NUnit tests.

## Upstream mapping

| Poppler 26.07.0 | Managed type |
| --- | --- |
| `Gfx` operator dispatch | `PdfGraphicsInterpreter` |
| `GfxState` CTM/paint/line state | `PdfGraphicsState` |
| `GfxPath`/`GfxSubpath` | `PdfGraphicsPath` and path segments |
| clipping state | `PdfClipPath` |
| `GfxTilingPattern` | `PdfTilingPatternBrush` |
| `GfxAxialShading`/`GfxRadialShading` | `PdfGradientBrush` |
| exponential/stitching `Function` | `PdfShadingReader` |
| `OutputDev` boundary | `PdfGraphicsElement` display list |
| initial vector output backend | `SvgPageRenderer` |

The mapping is behavioral rather than a line-for-line transliteration:
pointer-owned mutable Poppler objects become bounded managed values and
read-only public records.
