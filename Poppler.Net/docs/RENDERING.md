# Managed raster rendering in 0.8

Release `0.8.0-alpha.1` extends the pure-C# counterpart of Poppler's
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
4. Solid colors, axial/radial gradients and colored tiling patterns supply
   straight RGBA source samples.
5. Decoded Image XObjects use nearest-neighbor or bilinear sampling according
   to `/Interpolate`; existing image/mask alpha remains straight.
6. Source samples are composited with the active constant alpha, blend mode,
   clip and optional graphics-state soft mask.
7. Transparency Form XObjects render to intermediate RGBA surfaces before
   their result is composited into the parent.
8. Text-showing operations are consumed at their exact display-list position,
   including nested Forms and transparency groups.
9. Embedded TrueType, CFF1/Type 2 and Type 1 outlines are selected from
   retained PDF character codes/CIDs and antialiased by the same path scanner.
   Type 3 CharProcs execute as nested managed graphics programs.
10. All `Tr` fill/stroke/invisible/clip modes retain the active fill/stroke
    brushes, alpha, blend mode, soft mask and clip. Text clips accumulate at
    `ET` as required by PDF delayed text-clipping semantics.
11. Raw inline images are decoded into ordinary `PdfImageElement` entries at
    their content-stream position.

The blend implementation covers Normal, Multiply, Screen, Overlay, Darken,
Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
Hue, Saturation, Color and Luminosity. Its straight-alpha equation includes
both source-only and backdrop-only contributions, matching the PDF
transparency model rather than applying an RGB-only CSS approximation.

## Transparency

`PdfGraphicsInterpreter` retains transparency Form XObjects as
`PdfTransparencyGroupElement` instead of flattening them. `/I` and `/K` are
reported through `Isolated` and `Knockout`; isolated groups are composited on
an intermediate transparent surface.

Extended graphics-state `/SMask` dictionaries become `PdfSoftMask` values.
Both `/S /Alpha` and `/S /Luminosity` are rendered, including `/BC` backdrop
color for luminosity masks. `/SMask /None` clears the current mask.

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

Type 3 fonts execute their named CharProc stream through the graphics
interpreter using the font matrix and font resources. This permits vector
paths, images and Forms inside ordinary Type 3 glyphs.

When an embedded outline is unavailable, `UseFontSubstitution` can discover
`.ttf` and `.otf` files. Explicit `FontDirectories` are searched before
standard operating-system font folders. Candidate scoring uses Base-14 family
and style traits, after which the selected file is parsed by the same managed
TrueType/CFF readers. Disable substitution or provide controlled directories
when reproducible output is more important than local font availability.

## Safety

`PdfReadOptions.MaximumRenderPixels` defaults to 100,000,000 and is checked
before allocating the output surface. `MaximumTransparencyGroupDepth`
defaults to 32 and bounds both intermediate groups and soft masks. Existing
graphics-operation, path-segment, XObject, image and decoded-stream limits
remain active.

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

This remains an alpha rasterizer:

- knockout and non-isolated group backdrop interaction is not yet complete;
- transfer functions on soft masks are not applied;
- line caps, joins, miter clipping and dash continuity across subpaths are
  approximated by the first managed stroke scanner;
- CFF2, uncommon Type 1/CFF operators, Type 1 `seac`, advanced Type 3
  behavior, hinting, GSUB/GPOS, shaping and vertical glyph forms remain
  unsupported;
- file-based substitution is deliberately simpler than Fontconfig/FreeType
  fallback and depends on local fonts unless explicit directories are used;
- inline images with ambiguous `EI` data boundaries, unusual filters or
  unsupported color spaces may not decode;
- uncolored tiling patterns, mesh shadings, overprint and full ICC
  LUT/device-link behavior remain unsupported.

These limits are narrower than the `0.6` absence of page rasterization, but
the output is not yet a general visual-conformance replacement for Splash.
