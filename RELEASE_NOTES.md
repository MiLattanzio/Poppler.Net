# Poppler.Net 0.12.0-beta.1

Release date: 2026-08-10

`0.12.0-beta.1` closes the graphics-compatibility pass planned for the `0.12`
line. It turns the stroke, transparency and shading work delivered in the three
alpha releases into one reproducible compatibility baseline against Poppler
26.07.0.

The release remains a managed-only, read-only port. It adds no native runtime,
does not execute an external renderer in production, and does not expand the
public API or the declared `0.12` feature boundary.

## Poppler differential baseline

The versioned compatibility manifest combines 22 representative pages from
four deterministic corpora:

- eight stroke-geometry and page-box pages;
- six transparency, blend-mode and soft-mask pages;
- five function and Gouraud/patch-mesh shading pages;
- three cross-feature pages introduced for beta.1.

Every page records its normalized RGB mean absolute error at 72 DPI, an
approved maximum budget and a classification of the known difference. The
optional verifier reproduces the managed and Poppler renderings without making
Poppler a build, runtime or CI dependency.

## Cross-feature regression corpus

The new three-page `compatibility-beta1.pdf` fixture combines code paths that
were previously tested in isolation:

- transformed odd dash patterns, including a negative phase, inside a reused
  isolated transparency group;
- a curved Coons mesh through a two-input type 1 luminosity soft mask, an
  even-odd clip and a dashed boundary;
- transformed masked-mesh and stroke groups inside a rotated CropBox.

Canonical 72 DPI opaque and transparent PNG content hashes are frozen for all
three pages. Canonical SVG hashes also freeze the bounded non-rotated raster
fallback and the conservative full-CropBox fallback required for rotated
pages. Focused display-list tests assert the group, mask, mesh, clip, dash and
page-box structure before rendering.

## Compatibility and packaging

The callable public surface is unchanged from `0.12.0-alpha.3` apart from the
expected `Document.PortVersion` value. The library and CLI package versions are
`0.12.0-beta.1`. The dependency graph remains CoreJ2K 2.3.3.91,
JBig2Decoder.NETStandard 1.5.2 and StbImageSharp 2.30.15, all managed.

GitHub Actions builds and tests the release on Ubuntu, Windows and macOS,
verifies the managed-only boundary, and packs the NuGet artifact before the
release workflow publishes it through NuGet.org trusted publishing.

## Installation

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0-beta.1" />
```

## Deliberate limits

Advanced ICC LUT/device-link profiles, proofing, rendering intents, spot-color
overprint, native SVG mesh primitives and complex-script shaping remain outside
`0.12`. The project is read-only and does not write, edit or sign PDFs.
