# Managed raster rendering in 0.7

Release `0.7.0-alpha.2` adds the first pure-C# counterpart of Poppler's
`SplashOutputDev`, path scanner and compositing responsibilities. It consumes
the backend-neutral `Page.Graphics` display list and never loads Splash,
Cairo, Skia, FreeType, a platform drawing API or another native component.

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
    IncludeText = true
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
  --page 1 --dpi 144 --antialias 4
```

Add `--transparent` to leave untouched page pixels at alpha zero.

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
8. Embedded TrueType `glyf` outlines are selected from retained PDF character
   codes/CIDs, converted from quadratic contours to the same cubic path
   representation and antialiased by the same scanner.
9. Explicit DeviceGray, DeviceRGB and DeviceCMYK text fill colors are retained;
   `RasterRenderOptions.TextColor` remains the fallback when no such operator
   precedes a text run.

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

## Managed TrueType outlines

The raster text slice reads `head`, `maxp`, `loca`, `glyf`, `hhea` and `hmtx`
from embedded TrueType sfnt programs. It handles repeated coordinate flags,
on/off-curve points, implied quadratic points and XY-positioned composite
glyphs with common scale transforms. Format 4/12 `cmap` tables support Unicode
fallbacks. Format 0 tables map original byte character codes directly to
subset glyph IDs, and CID fonts use their `CIDToGIDMap` or identity mapping.
This avoids reconstructing glyph IDs from extracted Unicode, which is invalid
for arbitrary subset codes and multi-scalar ligatures.

This deliberately does not consult a system font. A missing or unsupported
font program is skipped instead of making output depend on Fontconfig,
FreeType or OS-specific substitution.

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

This remains an alpha rasterizer:

- knockout and non-isolated group backdrop interaction is not yet complete;
- transfer functions on soft masks are not applied;
- line caps, joins, miter clipping and dash continuity across subpaths are
  approximated by the first managed stroke scanner;
- text is a separate pass after the graphics display list, so its exact
  interleaving, clipping, paint mode, special/pattern color and transparency
  state are not yet preserved;
- embedded CFF/Type 1 outlines, Type 3 CharProcs, hinting, GSUB/GPOS, shaping,
  vertical glyph forms and platform font substitution are not rendered;
- uncolored tiling patterns, inline images, mesh shadings, overprint and
  full ICC LUT/device-link behavior remain unsupported.

These limits are narrower than the `0.6` absence of page rasterization, but
the output is not yet a general visual-conformance replacement for Splash.
