# Managed graphics engine

Version `0.8.0` retains and extends the backend-neutral slice of Poppler
26.07.0 `Gfx`, `GfxState`, `Function`, pattern and XObject behavior. It parses
page content into immutable managed objects; it does not call Poppler, Cairo,
FreeType or another native renderer.

## Execution model

`Page.Graphics` lazily interprets the page content stream and returns a display
list of:

- `PdfPathElement` for filled and/or stroked paths;
- `PdfImageElement` for Image XObject metadata and decoded pixels when the
  codec/color space is supported;
- `PdfTextElement` for a text-showing operation, its font, source glyph count,
  rendering mode and graphics state;
- `PdfShadingElement` for directly painted gradients;
- `PdfMeshShadingElement` for directly painted Gouraud or patch meshes;
- `PdfTransparencyGroupElement` for Form XObjects that declare a transparency
  group.

Every element retains a `PdfGraphicsState`, its active clipping paths and its
source Form resource. Ordinary Form XObjects are flattened while transparency
groups remain nested for intermediate-surface compositing.

The managed SVG and raster backends consume the same list. Text, paths, Forms,
images and shadings therefore preserve their exact content-stream ordering.

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
| `ExtGState` | `LW`, `LC`, `LJ`, `ML`, `D`, `CA`, `ca`, `BM`, `SMask`, `OP`, `op`, `OPM` |
| Text state | `BT`, `ET`, `Tf`, `Tm`, `Td`, `TD`, `T*`, `Tc`, `Tw`, `Tz`, `TL`, `Ts`, `Tr` |
| Text showing | `Tj`, `TJ`, `'`, `"` |
| Inline images | `BI`, `ID`, `EI` |

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
- transparency `/Group`, `/I` and `/K`;
- recursion detection and a configurable depth limit.

Image XObjects and inline images record resource name, width, height, bits per
component, color-space name, mask flag and CTM. Release `0.6` also attaches a
decoded `PdfImage` and embeds it as PNG in SVG. Alpha 3 uses ASCIIHex,
ASCII85, RunLength and DCT/JPEG filter terminators to prevent encoded `EI`
bytes from truncating inline data. Unsupported images remain visible as
metadata elements and diagnostics rather than aborting the full display list.

## Patterns and shading

Colored and uncolored tiling patterns (`PatternType 1`, `PaintType 1/2`)
interpret their own content stream and resources into a nested display list.
For `PaintType 2`, the underlying `scn`/`SCN` color is retained per use rather
than cached with the pattern resource. The pattern BBox, steps and matrix are
retained.

Shading patterns and direct `sh` painting support:

- axial shading type 2;
- radial shading type 3;
- device, calibrated and special color spaces supported by the `0.6` color
  pipeline;
- function type 0 sampled and type 2 exponential interpolation;
- function type 3 stitching, including function arrays;
- bounded function type 4 calculator programs;
- `/Extend` flags and bounded generated stops.
- type 4 free-form and type 5 lattice Gouraud triangle meshes;
- type 6 Coons and type 7 tensor-product patch meshes, converted to a bounded
  deterministic triangle list.

Function-based shading type 1 remains an explicit limitation. Mesh rendering
is currently raster-only; the SVG preview skips mesh elements.

## Resource limits

`PdfReadOptions` adds:

| Property | Default |
| --- | ---: |
| `MaximumGraphicsOperations` | 1,000,000 |
| `MaximumGraphicsElements` | 250,000 |
| `MaximumPathSegments` | 1,000,000 |
| `MaximumGraphicsStateDepth` | 256 |
| `MaximumXObjectDepth` | 32 |
| `MaximumTransparencyGroupDepth` | 32 |
| `MaximumShadingStops` | 33 |
| `MaximumMeshTriangles` | 65,536 |

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
| Gouraud/Coons/tensor mesh shadings | `PdfMeshShadingBrush` |
| sampled/exponential/stitching/calculator `Function` | `PdfFunction` |
| `OutputDev` boundary | `PdfGraphicsElement` display list, including `PdfTextElement` |
| initial vector output backend | `SvgPageRenderer` |
| initial Splash output backend | `PdfRasterRenderer` |

The mapping is behavioral rather than a line-for-line transliteration:
pointer-owned mutable Poppler objects become bounded managed values and
read-only public records.
