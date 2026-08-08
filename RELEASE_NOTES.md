# Poppler.Net 0.12.0-alpha.2

Release date: 2026-08-09

`0.12.0-alpha.2` replaces transparency decisions based on comparing finished
pixels with an explicit managed PDF compositing model. Every live raster
surface retains premultiplied color, composite alpha, shape and contribution
alpha independently; non-isolated and knockout groups also retain their
initial backdrop and initial alpha.

This source snapshot is derived from the verified
`Poppler.Net-26.07.0-0.12.0-alpha.1.zip` artifact, SHA-256
`4c32a590cb1c2c2a868326c5c0ecf62fb5e6f34480bbdfc1919469ad7f58b606`.
It preserves the alpha 1 stroke-outline work and does not include the
separately planned `0.11` shaping work.

## Highlights

- Composites isolated and non-isolated transparency groups from independent
  premultiplied color, alpha and shape values.
- Recovers the source contribution of non-isolated groups from the saved
  backdrop before applying group alpha, soft mask and boundary blend mode.
- Evaluates simple and nested knockout children against the correct initial
  group backdrop, including partial overlaps and zero-alpha painted shape.
- Applies fill/stroke alpha, all 16 standard separable and nonseparable blend
  modes, group alpha, clips and Alpha/Luminosity soft masks at their proper
  boundaries.
- Retains luminosity-mask `/BC` backdrops and bounded sampled, exponential,
  stitching or calculator `/TR` transfer functions.
- Uses the common path for text, images, patterns, shadings, Type 3 programs,
  Form content and annotation appearances.
- Keeps color conversion sRGB managed. Advanced ICC LUT/device-link proofing
  and calibrated spot-color rendering remain outside this prerelease.

## SVG fallback policy

SVG output now has an explicit policy for constructs without equivalent SVG
semantics:

```csharp
page.SaveSvg("page.svg", new SvgRenderOptions
{
    FallbackMode = SvgFallbackMode.Rasterize,
    RasterFallbackDpi = 144
});
```

`Rasterize` is the default. It renders a complex page through the managed
raster backend and embeds the result as a PNG data URI; it never creates an
external image resource. `Omit` explicitly preserves the historical skip
behavior. Faithfully representable paths, clips, isolated groups, gradients,
patterns and decoded images remain vector. Alpha 2 uses a deterministic
full-page fallback when any unsupported group or mesh is present; a smaller
group-bounds fallback is deferred.

## Public API and limits

The callable surface adds one enum and four optional properties:

- `SvgFallbackMode` with `Rasterize` and `Omit`;
- `SvgRenderOptions.FallbackMode`, default `Rasterize`;
- `SvgRenderOptions.RasterFallbackDpi`, default `144`;
- `PdfReadOptions.MaximumRenderWorkingBytes`, default 1 GiB;
- `PdfReadOptions.MaximumSvgFallbackPixels`, default 25,000,000 pixels.

`MaximumRenderWorkingBytes` is charged before allocation for every
simultaneously live high-precision surface, including group content, saved
backdrops, knockout buffers and soft masks. Existing output-pixel,
transparency-depth, XObject, geometry and decoded-stream limits remain active.
`MaximumSvgFallbackPixels` is checked before allocating an SVG raster fallback.

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0-alpha.2" />
```

## Corpus and verification

The deterministic six-page `transparency-alpha2.pdf` corpus covers:

- all isolated/knockout combinations and three levels of nesting;
- Normal, separable and nonseparable boundary blend modes;
- Alpha and Luminosity masks with backdrop and transfer functions;
- partial clips inside and outside groups;
- groups inside masks and masks inside groups;
- text, image, colored pattern, axial shading and annotation appearance paint;
- direct `1x1` and `2x2` formula checks.

Tests assert numerical channels and internal shape independently, force
working-set and SVG fallback limits before allocation, check the existing
depth ceiling, validate embedded PNG-only SVG output, freeze PNG/SVG hashes
and render one document concurrently eight ways. All historical corpus
manifests remain active. Poppler 26.05.0 is used only as an independent visual
reference and is never loaded or invoked by the library.

## Compatibility and deliberate limits

The alpha is source compatible unless an application depended on SVG silently
omitting unsupported groups; select `SvgFallbackMode.Omit` to retain that
behavior. The public graphics display list is unchanged.

Final output remains straight RGBA. The only one-channel historical raster
sample affected by retaining intermediate precision rounds to 128 instead of
Poppler's 8-bit intermediate value 127; its regression allows the documented
one-unit tolerance. Raster and SVG output are otherwise deterministic.

Still outside scope are advanced ICC LUT/device-link profiles, proofing,
rendering intents, spot-color calibration/overprint, adaptive patch
tessellation and function-based shading type 1. Edge antialiasing may differ
from Splash at individual samples.
