# Managed raster rendering in 0.9

Release `0.9.0-beta.2` extends the pure-C# counterpart of Poppler's
`SplashOutputDev`, path scanner, compositing and font-outline responsibilities.
It consumes the backend-neutral `Page.Graphics` display list and never loads
Splash, Cairo, Skia, FreeType, a platform drawing API or another native
component.

## API

```csharp
using Poppler;
using Poppler.Rendering;

using Document document = Document.LoadFromFile("input.pdf");
Page page = document.CreatePage(0);

PdfBitmap bitmap = page.Render(new RasterRenderOptions
{
    Dpi = 144,
    PageBox = PageBox.CropBox,
    Antialiasing = 4,
    Transparent = false,
    IncludeText = true,
    UseFontSubstitution = true,
    FontDirectories = new[] { "application-fonts" }
});

ReadOnlyMemory<byte> rgba = bitmap.Data;
bitmap.SavePng("page.png");
```

`PdfBitmap` is immutable, tightly packed `Rgba32`. Rows are top-to-bottom,
`BytesPerRow` is exactly `Width * 4`, and alpha is straight rather than
premultiplied. `Page.RenderToPng` and `Page.SavePng` are convenience paths.

The equivalent CLI command is:

```bash
poppler-net render input.pdf page.png \
  --page 1 --dpi 144 --antialias 4 --font-dir application-fonts
```

`--font-dir` may be repeated. Add `--no-font-substitution` for deterministic
embedded-font-only rendering, or `--transparent` to leave untouched page
pixels at alpha zero.

## Raster pipeline

1. The selected PDF page box and `/Rotate` value produce a user-to-device
   matrix at the requested DPI.
2. Cubic Bézier paths are flattened adaptively in device space.
3. Fill, stroke and clipping coverage are sampled on a configurable 1×, 2×,
   4× or 8× grid per pixel.
   Stroke coverage applies explicit butt/round/square caps,
   miter/round/bevel joins and continuous dash phase, including odd dash-array
   repetition.
4. Solid colors, axial/radial gradients, colored/uncolored tiling patterns and
   type 4–7 mesh shadings supply straight RGBA source samples.
5. Decoded Image XObjects use nearest-neighbor or bilinear sampling according
   to `/Interpolate`; existing image/mask alpha remains straight.
6. Source samples are composited with the active constant alpha, blend mode,
   clip and optional graphics-state soft mask. Sampled, exponential and
   stitching or calculator `/TR` functions transform the resulting mask value.
7. Transparency Form XObjects render to intermediate RGBA surfaces before
   their result is composited into the parent.
8. Text-showing operations are consumed at their exact display-list position,
   including nested Forms and transparency groups.
9. Embedded TrueType, CFF1/CFF2 Type 2 and Type 1 outlines are selected from
   retained PDF character codes/CIDs and antialiased by the same path scanner.
   Type 3 CharProcs execute as nested managed graphics programs.
10. All `Tr` fill/stroke/invisible/clip modes retain the active fill/stroke
    brushes, alpha, blend mode, soft mask and clip. Text clips accumulate at
    `ET` as required by PDF delayed text-clipping semantics.
11. Raw and commonly filtered inline images are decoded into ordinary
    `PdfImageElement` entries at their content-stream position. ASCIIHex,
    ASCII85, RunLength and DCT boundaries use their filter terminators instead
    of the first whitespace-delimited `EI` byte sequence.
12. Visible annotation `/AP/N` streams or deterministic managed fallbacks are
    painted after page content in `/Annots` order.

The blend implementation covers Normal, Multiply, Screen, Overlay, Darken,
Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
Hue, Saturation, Color and Luminosity. Its straight-alpha equation includes
both source-only and backdrop-only contributions, matching the PDF
transparency model rather than applying an RGB-only CSS approximation.

## Transparency

`PdfGraphicsInterpreter` retains transparency Form XObjects as
`PdfTransparencyGroupElement` instead of flattening them. `/I` and `/K` are
reported through `Isolated` and `Knockout`; isolated groups start with a
transparent backdrop, non-isolated groups retain the parent backdrop and
knockout groups evaluate children against their initial group backdrop.

Extended graphics-state `/SMask` dictionaries become `PdfSoftMask` values.
Both `/S /Alpha` and `/S /Luminosity` are rendered, including `/BC` backdrop
color in the declared blending color space for luminosity masks. Function
types 0, 2, 3 and 4 in `/TR` are applied through a bounded cached lookup table
and exposed through
`HasTransferFunction`. `/SMask /None` clears the current mask.

Solid DeviceCMYK and DeviceGray paint also honors `/OP`, `/op` and `/OPM`.
Mode 1 preserves zero-valued process colorants from the backdrop before the
result is converted to managed sRGB. This is a preview path, not
ICC/profile-aware print proofing or spot-color overprint simulation.

## Managed font outlines

The raster text slice reads `head`, `maxp`, `loca`, `glyf`, `hhea` and `hmtx`
from embedded TrueType sfnt programs. It handles repeated coordinate flags,
on/off-curve points, implied quadratic points and XY-positioned composite
glyphs with common scale transforms. Format 4/12 `cmap` tables support Unicode
fallbacks. Format 0 tables map original byte character codes directly to
subset glyph IDs, and CID fonts use their `CIDToGIDMap` or identity mapping.
This avoids reconstructing glyph IDs from extracted Unicode, which is invalid
for arbitrary subset codes and multi-scalar ligatures.

The CFF1 reader supports raw CFF and OpenType/CFF programs, Type 2
charstrings, local/global subroutines, CID `FDSelect`/`FDArray` routing and
common flex operators. The Type 1 reader accepts PFA/PFB containers, decrypts
eexec and charstrings, honors `lenIV`, and executes ordinary path/subroutine
operators. Neither reader invokes a native font engine.

The beta CFF2 path accepts raw and OpenType `CFF2` tables, 32-bit INDEX data,
FDArray/FDSelect format 4 routing and the default variation instance. Its Type
2 evaluator also covers common arithmetic, stack, transient-array and logical
escaped operators. OpenType GSUB processing applies non-contextual
`vert`/`vrt2` alternates and exact `liga`/`rlig` ligatures before the managed
outline is selected.

Type 3 fonts execute their named CharProc stream through the graphics
interpreter using the font matrix and font resources. This permits vector
paths, images and Forms inside ordinary Type 3 glyphs.

When an embedded outline is unavailable, `UseFontSubstitution` can discover
`.ttf` and `.otf` files. Explicit `FontDirectories` are searched before
standard operating-system font folders. Candidate scoring uses Base-14 family,
style and Narrow/Condensed or Expanded/Extended traits. Several ranked files
may be tried when the preferred candidate lacks the requested glyph; the
selected file is parsed by the same managed TrueType/CFF readers. If a
standard font omits `/Widths`, the canonical
Poppler 26.07 Base-14 metrics determine character positions. A horizontally
substituted outline is fitted to that PDF advance so a locally wider font does
not overlap the following glyph. Disable substitution or provide controlled
directories when reproducible output is more important than local font
availability.

## Safety

`PdfReadOptions.MaximumRenderPixels` defaults to 100,000,000 and is checked
before allocating the output surface. `MaximumTransparencyGroupDepth`
defaults to 32 and bounds both intermediate groups and soft masks.
`MaximumMeshTriangles` defaults to 65,536 and bounds decoded/tessellated mesh
data. Existing
graphics-operation, path-segment, XObject, image and decoded-stream limits
remain active.

Annotation rendering is additionally bounded by
`MaximumAnnotationsPerPage`, `MaximumAnnotationPoints` and
`MaximumAnnotationAppearanceDepth`. Existing XObject, stream and display-list
limits continue to apply inside an appearance.

`RasterRenderOptions.Dpi` is restricted to 1–2400. The antialiasing grid must
be 1, 2, 4 or 8.

## Verification and deliberate limits

The deterministic two-page rendering fixture covers Multiply, constant alpha,
Alpha and Luminosity soft masks, an isolated Form transparency group, clipping
and page rotation. At 72 DPI, selected RGBA pixels match Poppler 26.05.0
exactly or within one 8-bit unit. The complete fixture comparison has a
normalized mean absolute error around `0.00019`; the pre-existing graphics
fixture is around `0.00236`.

A separate deterministic TrueType fixture contains only a Macintosh format 0
`cmap`, arbitrary PDF character codes `01`–`03`, a `ToUnicode` map and an RGB
text color. The three-page `drylab.pdf` compatibility sample was also rendered
at 96 DPI and visually compared page-by-page with Poppler: title, body text,
ligatures, Polish characters, orange emphasis and images are present.

The `0.8` compatibility fixture additionally covers exact text/path/image
ordering, Base-14 substitution from a controlled font directory, a raw RGB
inline image and a color-inheriting Type 3 CharProc. An embedded Nimbus Sans
Type 1 fixture covers `ABC` plus `Tr 7` clipping; the existing OpenType/CFF
fixture verifies three distinct Type 2 glyph outlines. Managed PNGs were
visually checked against Poppler rather than validated only by aggregate dark
pixel counts.

Alpha 2 adds a proportional Base-14 fixture containing `i`, `m` and `W`.
Fourteen parameterized metric cases cover every standard font/style, while a
raster assertion ensures Helvetica's narrow `i` advances by 222 units rather
than the former synthetic 500-unit cell. A separate multi-style report page
was rendered and visually inspected for letter gaps, overlap and clipping.

Alpha 3 adds a deterministic four-page corpus. It places false
whitespace-delimited `EI` tokens inside ASCII85, RunLength and JPEG data,
applies a quadratic transfer function to an Alpha soft mask, clips oversized
Crop/Bleed boxes and omits `MediaBox` from a damaged page tree. Poppler and
managed output have identical dimensions on all pages; transfer, page-box and
fallback pages are pixel-identical at 72 DPI. The filtered-image page has
normalized mean absolute error `0.011755836`, confined to managed codec sample
differences. `drylab.pdf` was rechecked visually on all three pages at 96 DPI.

Beta 1 adds a deterministic four-page text corpus for CFF2 escaped/default
blend execution, inherited external vertical CMaps, `vert`/`vrt2`, exact
`liga` substitution and Narrow/Condensed fallback. Each feature is checked
through distinct glyph regions rather than aggregate non-white pixel counts.

Beta 2 adds a deterministic six-page graphics corpus for free-form/lattice
Gouraud meshes, Coons/tensor patch meshes, per-use uncolored patterns,
calculator soft-mask transfer, isolated/non-isolated/knockout groups and
process overprint. Raster pages were inspected side by side with Poppler at
72 DPI and 2x antialiasing; the patch tessellation is visually continuous.
Poppler comparison for overprint uses its explicit `-overprint` preview mode.
RGB values differ
because Poppler applies its CMYK color-management path while this renderer uses
the documented managed sRGB conversion.

The `0.9.0-alpha.3` corpus adds four OCG/OCMD pages covering nested marked
content, Form and Image XObjects, annotations, widgets, all four membership
policies and `/VE` expressions. Raster and SVG options accept snapshotted
group-ID overrides, while the cached `Page.Graphics` display list continues to
represent the document's default configuration. See
[OPTIONAL_CONTENT.md](OPTIONAL_CONTENT.md).

The `0.9.0-beta.2` corpus adds five managed pages for cap/join geometry,
continuous and odd dash arrays, a partially corrupt `/Contents` array and a
stream with a stale declared length. Its damaged page tree also includes a
missing child, a circular branch and an inconsistent page count. The five
managed pages were inspected at original resolution; structural, strict-mode,
concurrency and allocation behavior is covered independently. See
[ROBUSTNESS.md](ROBUSTNESS.md).

This remains a compatibility-focused rasterizer with explicit limits:

- nested knockout shape/opacity and non-isolated groups with non-Normal
  boundary blend modes remain approximations;
- unsupported calculator operators are rejected and reported rather than
  executed;
- degenerate zero-length joins, extreme anisotropic transforms and uncommon
  self-intersecting stroke geometry remain managed approximations;
- CFF2 variation-region interpolation, uncommon Type 1/CFF operators, Type 1
  `seac`, advanced Type 3 behavior and hinting remain unsupported;
- GSUB is limited to non-contextual `vert`/`vrt2` and exact `liga`/`rlig`;
  contextual GSUB, GPOS and complex-script shaping remain unsupported;
- file-based substitution is deliberately simpler than Fontconfig/FreeType
  fallback and depends on local fonts unless explicit directories are used;
- inline images whose first filter has no deterministic boundary in this
  release, unusual filter chains or unsupported color spaces may not
  decode;
- function-based shading type 1, adaptive patch subdivision, spot-color
  overprint and full ICC LUT/device-link behavior remain unsupported.

These limits are narrower than the `0.6` absence of page rasterization, but
the output is not yet a general visual-conformance replacement for Splash.
