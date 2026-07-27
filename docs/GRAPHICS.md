# Managed graphics engine

Version `0.8.0-alpha.3` retains and extends the backend-neutral slice of Poppler
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
| `ExtGState` | `LW`, `LC`, `LJ`, `ML`, `D`, `CA`, `ca`, `BM`, `SMask` |
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

Colored tiling patterns (`PatternType 1`, `PaintType 1`) interpret their own
content stream and resources into a nested display list. The pattern BBox,
steps and matrix are retained.

Shading patterns and direct `sh` painting support:

- axial shading type 2;
- radial shading type 3;
- device, calibrated and special color spaces supported by the `0.6` color
  pipeline;
- function type 0 sampled and type 2 exponential interpolation;
- function type 3 stitching, including function arrays;
- `/Extend` flags and bounded generated stops.

Uncolored tiling patterns, calculator functions and function/triangle/patch
mesh shadings remain explicit limitations.

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
| `OutputDev` boundary | `PdfGraphicsElement` display list, including `PdfTextElement` |
| initial vector output backend | `SvgPageRenderer` |
| initial Splash output backend | `PdfRasterRenderer` |

The mapping is behavioral rather than a line-for-line transliteration:
pointer-owned mutable Poppler objects become bounded managed values and
read-only public records.
