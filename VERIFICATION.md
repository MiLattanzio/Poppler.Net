# Verification record

Verification performed on 2026-07-27 for `0.8.0-alpha.3`:

- .NET SDK 8.0.423 restored and compiled all four solution projects in Release
  with warnings treated as errors.
- NUnitLite executed 128 tests: 128 passed, 0 failed, 0 warnings, 0 skipped.
- the managed-only verifier accepted production source and every asset in the
  complete restored NuGet graph, including the three runtime codecs.
- `Poppler.Net.0.8.0-alpha.3.nupkg` was assembled from the current Release
  DLL/XML and the previously generated NuGet pack layout; its manifest and
  package metadata identify alpha 3, contain the three pinned managed codec
  dependencies and contain no native/runtime asset.
- the user corrections remain intact: revision 6 selects SHA-2 through
  `int va = selector % 3`, and every NUnit exception assertion explicitly
  casts its lambda to `Action`.
- the two CA2014 sites previously identified allocate their reusable
  `stackalloc` spans before their loops; no `stackalloc` occurs inside a loop
  body.
- the deterministic image/color fixture contains 12 decoded Image XObjects:
  raw RGB, Indexed, Separation, DeviceN sampled tint, Lab, ICCBased,
  DCT/JPEG, JPX/JPEG 2000, CCITT Group 3 and Group 4, JBIG2 and an RGB image
  with soft mask.
- fixture assertions verify exact raw/indexed/tint pixels, calibrated/ICC
  conversion ranges, JPEG/JPX output, CCITT rows, JBIG2 dimensions/content,
  straight-alpha soft masks, PNG structure and SVG embedding.
- the same fixture exercises Separation and Lab colors on graphics paths so
  image and vector paint use the same color-space implementation.
- the deterministic rendering fixture covers Multiply, constant alpha, Alpha
  and Luminosity graphics-state soft masks, an isolated Form transparency
  group, clipping and `/Rotate 90`.
- managed and Poppler 26.05.0 output are both 320×240 at 72 DPI for rendering
  fixture page 1; blend, group, clip and soft-mask sample pixels match exactly
  or within one 8-bit channel unit.
- ImageMagick reports normalized mean absolute error `0.000186547` for the
  rendering fixture and `0.00235634` for the pre-existing vector fixture.
- embedded TrueType `ABC` is painted from managed `glyf` outlines; disabling
  `RasterRenderOptions.IncludeText` removes all dark pixels from the text-only
  fixture.
- a separate embedded TrueType fixture uses only `cmap` format 0, arbitrary
  PDF codes `01`–`03`, `ToUnicode` and an RGB fill color; managed rendering
  paints the expected orange `ABC` directly from the retained source codes.
- the `0.8` graphics fixture retains two text runs and one raw RGB inline image
  among seven display-list elements. Assertions verify that a blue vector
  overlay follows Base-14 `ABC`, that the image samples are exactly red/green
  and that the Type 3 CharProc inherits the expected blue paint.
- Base-14 substitution was exercised with an explicit fixture directory and
  no platform font API. Disabling substitution removes the non-embedded text.
- all fourteen standard font/style variants were checked against the canonical
  widths ported from Poppler 26.07. The proportional `imW` cases distinguish
  narrow, ordinary and wide advances and fail against the former constant
  500-unit fallback.
- the controlled substitute font contains `ABCimW`. A raster regression checks
  that Helvetica `m` begins after the canonical 222-unit `i` advance, and
  horizontal replacement outlines are fitted to the PDF advance.
- a synthetic report page mirroring the reported failure was rendered with
  Helvetica, Helvetica-Bold and Times-Roman. Visual inspection found no
  internal letter gaps, overlap, truncation or clipping. The reported source
  PDF itself was not available; only its rendered PNG was supplied.
- the alpha 3 differential corpus has four pages covering whitespace-delimited
  `EI` bytes inside ASCII85, RunLength and JPEG data, a quadratic `/TR`
  transfer function on an Alpha soft mask, Crop/Bleed boxes outside
  `MediaBox`, and a damaged page tree with no inherited `MediaBox`.
- managed output and Poppler 26.05.0 have the same dimensions on all four
  alpha 3 pages. The transfer-function, page-box and default-geometry pages
  are pixel-identical at 72 DPI. The filtered-image page has normalized mean
  absolute error `0.011755836`, confined to codec sample differences; all
  three inline images and the following graphics operators are retained.
- the OpenType/CFF fixture renders distinct `A`, `B` and `C` Type 2 outlines;
  the embedded PFB Type 1 fixture renders distinct `A`, `B` and `C` outlines
  and applies a `Tr 7` text clip to a blue path.
- managed/Poppler 26.05.0 normalized mean absolute errors at 72 DPI are
  `0.00322099` for the mixed compatibility fixture, `0.000470635` for Type 1
  and `0.0000252752` for OpenType/CFF. All three pairs were also inspected
  visually so an aggregate pixel count cannot hide a missing glyph.
- the three-page Prince `drylab.pdf` compatibility sample uses five embedded
  TrueType subsets with format 0 CMaps. Managed output at 96 DPI was visually
  checked page-by-page against Poppler output: title/body/footer text,
  multi-scalar ligatures, Polish characters, device colors and images are
  present on all pages.
- rendered PNG verification covers RGBA layout, straight alpha, dimensions,
  page rotation, configurable transparency, PNG structure and render-pixel
  limits.
- `pdfinfo` parsed the image fixture as a one-page PDF 1.7 document measuring
  600×800 points, and `pdfimages -list` identified all expected image
  encodings and color spaces.
- regenerating the image/color fixture updates a SHA-256 manifest and produces
  deterministic PDF bytes for the installed generator versions.
- all nine encrypted fixture hashes, all three embedded-font fixture hashes and the
  graphics fixture hash continue to match their manifests.
- project/XML/JSON/YAML structure, local Markdown links, shell syntax and
  forbidden-interoperability source scans pass.
- the source archive contains 141 files, including 75 C# sources and 21,249
  lines of production C#; it excludes `bin`, `obj`, NuGet packages, generated
  bytecode, executables and native artifacts.
- the final source ZIP was extracted into a fresh directory, matched all 141
  source files byte for byte, restored from the same five managed NuGet
  packages, rebuilt all four projects without warnings, passed 128/128 tests
  and produced a byte-identical alpha 3 transfer-function regression PNG.

The environment's `dotnet` CLI cannot reliably inspect its process namespace.
Serialized restore/build succeeded, but repeated direct `dotnet pack` startup
attempts failed inside `System.Diagnostics.Process.GetStat` before MSBuild
could read the project. The package layout was therefore validated separately
from the compiled Release DLL and XML. This is an execution-environment issue,
not a project or package error. The standard user entry point remains:

```bash
./build.sh Release
```

It restores, compiles with warnings as errors, inspects the complete NuGet
graph, runs NUnitLite and packs the library.
