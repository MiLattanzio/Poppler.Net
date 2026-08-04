# Poppler.Net 0.12.0-alpha.1

Release date: 2026-08-04

`0.12.0-alpha.1` replaces the scalar raster-stroke approximation with a
bounded managed outline pipeline modeled on Poppler Splash. Stroke geometry is
now expanded before the current transformation matrix and transformed only
after cap, join and dash geometry is complete.

This source snapshot is derived from the verified `0.10.0-alpha.1` archive
because no `0.11.0` implementation exists in the supplied workspace. It does
not claim to include the separately planned `0.11` shaping work.

## Highlights

- Builds non-hairline stroke outlines in PDF user space before the CTM.
- Measures cubic flattening error after the full user-to-device transform.
- Preserves anisotropic scale, shear and reflection instead of reducing the
  CTM to one average width.
- Handles butt, round and projecting-square caps; miter, round and bevel joins;
  miter limits; zero-length lines; hairlines; cusps; exact reversals; duplicate
  points; near-collinear segments; and closed subpaths.
- Applies negative dash phase, odd-pattern repetition, zero-length dash
  elements and phase continuity across segments and closed-path seams.
- Uses the same nonzero/even-odd point scanner for fill, stroke outlines and
  clip geometry.
- Treats singular and scale-relative near-singular transforms explicitly and
  deterministically.
- Adds only `PdfReadOptions.MaximumRasterGeometrySegments` to the public API.
  The per-render cumulative budget covers flattening segments, dash fragments,
  stroke-outline edges and temporary raster clip geometry.
- Keeps `Page.Graphics` and the public display-list model unchanged.

## Compatibility and upgrading

The release is source compatible with `0.10.0-alpha.1`; the sole new public
member is an optional protection with a default of 4,000,000 segments.

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0-alpha.1" />
```

Raster output intentionally changes where old code used average CTM scale or
painted caps/joins directly from the transformed centerline. The manifest
documents all changed historical pages:

- AcroForm alpha 2 page 2: widget-button stroke outline;
- robustness beta 2 page 1: cap geometry;
- robustness beta 2 page 5: join and miter-limit geometry.

Every other historical managed raster hash remains unchanged.

## Corpus and verification

The deterministic eight-page `raster-geometry-alpha1.pdf` corpus covers cap
and join widths, zero-length lines, hairlines, continuous and discontinuous
dash patterns, negative phase, odd and zero-length dash entries, anisotropic
`18×0.22`/`0.22×18` transforms, shear, mirror, tight curves, cusp/reversal,
self-intersection, nested nonzero/even-odd clips, CropBox boundaries and page
rotation.

The approved manifest records every page at:

- 96 and 300 DPI;
- antialiasing 1 and 4;
- opaque and transparent backgrounds.

The regular suite checks all corpus semantics, manifest completeness,
representative values across the entire render matrix, cumulative limit
failure, singular-transform behavior, rotated CropBox dimensions and eight-way
concurrent rendering of one `Document`. Poppler 26.05.0 was used only as an
independent QA reference; it is not loaded or invoked by the library.

## Safety and implementation details

- Geometry budgets are local to one raster operation, so concurrent renders
  do not share mutable counters.
- The budget is charged before temporary geometry collections grow.
- Cubic subdivision has an internal hard depth of 16; round geometry has an
  internal 4,096-edge hard cap.
- A zero-width hairline resolves dash positions in user space and then expands
  to one device pixel, the only stroke case expanded after the CTM.
- A singular non-hairline paint is skipped; a singular clip becomes empty.
- Parser, text extraction, SVG serialization and the public display list are
  unchanged.

## Known limitations

- Pixel-edge antialiasing is deterministic but is not guaranteed byte-for-byte
  identical to Splash at every boundary sample.
- Transparency-group shape/alpha refinements, adaptive mesh shading and inline
  image boundary recovery remain assigned to later `0.12.0` prereleases.
- The separately planned `0.11` font/shaping work is not present in this
  source snapshot.

Base artifact: `Poppler.Net-26.07.0-0.10.0-alpha.1.zip`, SHA-256
`6c0bda3766f825693678ac5aa0a91f19e83b553d1bc01e4c7acb7eb2c1842e43`.
