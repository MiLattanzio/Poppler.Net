# Changelog

## 0.8.0-beta.2 — 2026-07-27

- Added bounded managed decoding and rasterization for shading types 4–7:
  free-form and lattice Gouraud triangles plus Coons and tensor-product patch
  meshes. Patch meshes use deterministic 12×12 tessellation and the public
  display list exposes `PdfMeshShadingElement`, `PdfMeshShadingBrush`,
  vertices and triangles.
- Added `PaintType 2` uncolored tiling patterns with per-use underlying colors.
  Reusing one pattern resource with different `scn`/`SCN` colors no longer
  leaks the first cached color into later paint operations.
- Added a bounded pure-managed calculator-function evaluator for function type
  4, including arithmetic, relational, boolean, stack and conditional
  operators. Calculator `/TR` functions now apply to Alpha and Luminosity soft
  masks.
- Added isolated/non-isolated and knockout group surface handling, preserved
  the same flags on soft-mask groups, and decoded soft-mask backdrop colors in
  the declared group blending color space.
- Added `/OP`, `/op` and `/OPM` graphics-state support plus a managed
  process-CMYK overprint-mode-1 simulation for solid DeviceCMYK/DeviceGray
  paint. This is an sRGB preview, not ICC proofing or spot-color simulation.
- Added `MaximumMeshTriangles` and a deterministic six-page graphics corpus.
  Eight regressions bring the suite to 142 passing NUnit cases.

## 0.8.0-beta.1 — 2026-07-27

- Added bounded external CMap resolution from explicit directories and common
  `poppler-data` locations, including named encoding CMaps, external
  `ToUnicode` maps, `/UseCMap` dictionaries and PostScript `usecmap`
  inheritance.
- Added a managed CFF2 outline path for raw and OpenType `CFF2` programs,
  including 32-bit INDEX data, FDArray/FDSelect format 4 routing, the default
  variation instance and the common escaped Type 2 arithmetic, stack,
  transient-array and logical operators.
- Added bounded OpenType GSUB processing for `vert`/`vrt2` single
  substitutions and exact `liga`/`rlig` ligatures. Vertical embedded glyphs
  and multi-scalar fallback glyphs can therefore select the intended outline
  without a native shaping engine.
- Improved non-embedded font selection with Narrow/Condensed and
  Expanded/Extended family traits. The resolver now retains several ranked
  candidates and continues when the preferred file lacks the requested
  glyph.
- Added CLI `--cmap-dir` and public `PdfReadOptions.CMapDirectories`,
  `UseSystemCMaps`, `MaximumExternalCMapBytes` and
  `MaximumCMapUseDepth`.
- Added a deterministic four-page beta text corpus covering CFF2 escaped and
  blend operators, inherited external vertical CMaps, vertical alternates,
  `fi` ligatures and narrow-font matching. Six regressions bring the suite to
  134 passing NUnit cases.

## 0.8.0-alpha.3 — 2026-07-27

- Made inline-image boundary recovery filter-aware for ASCIIHex, ASCII85,
  RunLength and DCT/JPEG streams. Encoded whitespace-delimited `EI` bytes no
  longer truncate data before the actual filter terminator.
- Applied sampled, exponential and stitching `/TR` transfer functions to
  Alpha and Luminosity soft masks through a bounded cached lookup table.
- Exposed `PdfSoftMask.HasTransferFunction` in the public display-list model.
- Normalized reversed page boxes, clipped Crop/Bleed/Trim/Art boxes to the
  `MediaBox` and adopted Poppler's 612×792-point fallback for damaged page
  trees without an inherited `MediaBox`.
- Added a deterministic four-page differential fixture for ambiguous inline
  images, soft-mask transfer, oversized page boxes and missing page geometry.
- Added five NUnit regressions, bringing the suite to 128 passing cases, and
  visually rechecked every page of `drylab.pdf` against Poppler at 96 DPI.

## 0.8.0-alpha.2 — 2026-07-27

- Replaced the synthetic constant-width fallback for Base-14 fonts without a
  `/Widths` array with the canonical Poppler 26.07 metrics for Courier,
  Helvetica, Times, Symbol and ZapfDingbats, including every bold, italic and
  oblique variant.
- Preserved explicit PDF widths as authoritative and used Base-14 metrics only
  when the character has no declared width.
- Reconciled horizontally substituted TrueType/CFF outlines with the PDF
  character advance, preventing wider local replacement glyphs from
  overlapping the following character.
- Expanded the deterministic substitute font to contain proportional `i`,
  `m` and `W` glyphs and added metric/raster regressions that detect the
  monospaced spacing failure shown by the reported document.
- Added 15 NUnit cases, bringing the suite to 123 passing cases, and visually
  verified a multi-style Base-14 report page after rendering.

## 0.8.0-alpha.1 — 2026-07-26

- Moved text-showing operators into `PdfGraphicsInterpreter` and added public
  `PdfTextElement` entries in exact content-stream order, including text in
  Form XObjects and transparency groups.
- Added all eight PDF `Tr` rendering modes, managed glyph fill/stroke,
  invisible text and accumulated text clipping.
- Added bounded managed CFF1/Type 2 and PFA/PFB Type 1 charstring
  interpreters, including common subroutine and flex behavior.
- Executed Type 3 CharProcs as nested graphics programs with their font
  matrix, resources and inherited text paint state.
- Added optional managed font-file substitution for Base-14 and other
  non-embedded fonts. Explicit `RasterRenderOptions.FontDirectories` take
  priority over standard operating-system font folders; no native font API or
  rasterizer is used.
- Decoded raw inline images and abbreviated PDF image dictionary names while
  retaining them at the correct display-list position.
- Added CLI `--font-dir`, `--no-font-substitution` and text-run counts in the
  `graphics` command.
- Added deterministic Base-14, inline-image, Type 3, Type 1 and text-clipping
  fixtures plus seven rendering regressions, bringing the suite to 108 NUnit
  cases.
- Visually verified CFF, Type 1, Type 3, inline-image and interleaving output
  against Poppler, and rechecked the three-page `drylab.pdf` sample.

## 0.7.0-alpha.2 — 2026-07-26

- Fixed missing raster text in TrueType subsets whose sfnt contains only a
  Macintosh format 0 `cmap`.
- Retained original PDF character codes and CIDs in internal text runs so
  rasterization selects subset glyph IDs directly instead of performing the
  lossy `PDF code → Unicode → glyph ID` round trip.
- Applied `CIDToGIDMap`/identity mappings directly during TrueType outline
  selection and preserved multi-scalar ligatures as one source glyph.
- Preserved explicit DeviceGray, DeviceRGB and DeviceCMYK text fill colors;
  `RasterRenderOptions.TextColor` remains the fallback.
- Added a reproducible format 0 TrueType subset fixture and a rendering
  regression, bringing the suite to 101 passing NUnit cases.
- Verified all three pages of `drylab.pdf` visually against Poppler at 96 DPI,
  including title, body text, ligatures, Polish characters, orange emphasis
  and images.

## 0.7.0-alpha.1 — 2026-07-26

- Added an internal pure-C# raster backend corresponding to the first
  `SplashOutputDev`/Splash compositing slice.
- Added public immutable `PdfBitmap` RGBA output plus `Page.Render`,
  `RenderToPng`, `SavePng` and `RasterRenderOptions`.
- Rasterized path fills/strokes, adaptive cubic Bézier flattening,
  nonzero/even-odd fills, clipping, decoded images, axial/radial shadings and
  colored tiling patterns.
- Added 1×, 2×, 4× and 8× supersampled coverage antialiasing, page-box/DPI
  sizing, page rotation, transparent or configurable solid backgrounds and
  bounded render surfaces.
- Added PDF straight-alpha compositing for Normal, Multiply, Screen, Overlay,
  Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference,
  Exclusion, Hue, Saturation, Color and Luminosity blend modes.
- Preserved Form transparency groups in the display list and added isolated
  group compositing, Alpha/Luminosity soft masks and backdrop handling.
- Added a bounded managed TrueType `glyf`/`loca`/`hmtx` reader with simple and
  composite glyph outlines, quadratic-to-cubic conversion and raster text
  painting for embedded TrueType fonts.
- Added the CLI `render` command with DPI, antialiasing, transparency and page
  selection options.
- Added a deterministic two-page rendering fixture and differential samples
  verified against Poppler 26.05.0, including blend, alpha, both soft-mask
  modes, isolated groups, clipping and page rotation.
- Added 17 NUnit cases, bringing the suite to 100 passing cases, without adding
  a runtime package or native asset.

## 0.6.0-alpha.1 — 2026-07-26

- Added `Page.Images` and immutable `PdfImage` objects with exact tightly
  packed `BytesPerRow`, `Gray8`, `Rgb24` and straight-alpha `Rgba32` pixels.
- Added managed decoding for raw/Flate/LZW/RunLength samples, DCT/JPEG through
  StbImageSharp, JPEG 2000 through CoreJ2K, JBIG2 through
  JBig2Decoder.NETStandard and an internal CCITT MH/Group 3/Group 4 decoder.
- Added 1/2/4/8/16-bit sample unpacking, `/Decode`, image masks, explicit
  masks, color-key masks, `/SMask` and `JBIG2Globals`.
- Added CalGray, CalRGB, Lab, ICCBased common matrix/shaper, Indexed,
  Separation and DeviceN conversion to managed sRGB.
- Added bounded PDF function types 0, 2 and 3 for sampled tint transforms and
  shadings.
- Added dependency-free managed PNG encoding, `PdfImage.SavePng`, CLI
  `images` extraction and PNG embedding in the SVG backend.
- Added image-pixel/component, ICC-profile and sampled-function resource
  limits.
- Added a deterministic 12-image fixture and 17 NUnit cases, bringing the
  suite to 83 passing cases.
- Verified Release compilation under .NET SDK 8.0.423 and the complete
  restored package graph with the managed-only verifier.

## 0.5.0-alpha.1 — 2026-07-26

- Added a managed `Gfx`/`GfxState` slice that produces a public,
  backend-neutral per-page graphics display list.
- Added affine CTM composition, `q`/`Q`, line state, dash arrays, path
  construction, Bézier curves, rectangles and all common fill/stroke/end-path
  operators.
- Added nonzero/even-odd clipping with graphics-state save/restore semantics.
- Added DeviceGray, DeviceRGB and DeviceCMYK paint plus common `ExtGState`
  line, alpha and blend-mode entries.
- Added recursive Form XObject interpretation with Form matrices, BBox clips,
  resource inheritance and recursion limits.
- Added Image XObject metadata display-list entries without decoding pixels.
- Added colored tiling patterns and axial/radial shading functions, including
  exponential and stitching functions.
- Replaced the text-only SVG diagnostic with a managed vector SVG backend for
  paths, clips, gradients, tiling patterns, Form content and optional image
  bounds.
- Added the CLI `graphics` command and public `Page.Graphics` API.
- Added graphics operation, element, path, stack, XObject and shading-stop
  safety limits.
- Moved both CA2014 `stackalloc` sites out of their loops as requested.
- Added a deterministic graphics fixture and nine NUnit cases, bringing the
  declared suite to 66 cases.

## 0.4.0-alpha.1 — 2026-07-26

- Split font parsing, character decoding, CID metrics and text layout into
  dedicated managed components corresponding to Poppler's `GfxFont`,
  `CMap`, `CharCodeToUnicode` and initial `TextOutputDev` responsibilities.
- Added bounded CMap parsing for codespace ranges, `bfchar`, `bfrange`,
  `cidchar`, `cidrange`, CMap names and horizontal/vertical writing modes.
- Added Type 0 composite-font decoding with separate character-code-to-CID
  and character-code-to-Unicode mappings.
- Added CID `DW`/`W` and vertical `DW2`/`W2` metric handling.
- Added simple Type 1, TrueType and Type 3 encoding/width handling, Adobe
  glyph-name algorithms, ligature names and Type 3 font matrices.
- Added embedded Type 1 encoding discovery plus TrueType/OpenType format 4
  and format 12 `cmap` fallbacks when `ToUnicode` is absent.
- Added public per-page `FontInfo`, embedded format/subset reporting and the
  CLI `fonts` command.
- Added vertical text advancement, right-to-left run ordering, column-aware
  reading order and font ascent/descent bounds.
- Added a configurable CMap mapping limit for untrusted input.
- Added 14 NUnit cases and two reproducible embedded-font PDF fixtures,
  bringing the declared suite to 57 cases.
- Preserved the explicit `(Action)(() => ...)` form for every NUnit exception
  assertion to avoid ambiguous `Assert.That` overload resolution.

## 0.3.0-alpha.2 — 2026-07-26

- Corrected the revision 6 SHA-2 selector switch expression by evaluating
  `selector % 3` before entering the switch.
- Migrated all 43 regression cases from xUnit v3 to NUnit 4.
- Replaced xUnit theory data with NUnit `TestCaseSource` cases and converted
  assertions to NUnit's constraint model.
- Updated the build scripts to execute the suite with the managed NUnitLite
  in-process runner.
- Centrally pinned NUnit and NUnitLite as test-only managed dependencies.

## 0.3.0-alpha.1 — 2026-07-26

- Ported Standard Security Handler revisions 2 through 6.
- Added legacy owner/user validation, RC4-40/128, AES-128 and AES-256 object
  decryption.
- Added revision 6 hardened SHA-2 password hashing and AES-256 `/Perms`
  validation diagnostics.
- Added independent `StrF`, `StmF` and `EFF` selection, explicit `/Crypt`
  filters and `EncryptMetadata false`.
- Added locked-document loading and Poppler-compatible password retry.
- Exposed encryption revision, algorithm, key length, password kind and PDF
  permission flags.
- Added CLI owner/user password options and locked-document information.
- Added nine encrypted compatibility fixtures, including R2–R6 text and
  embedded-file coverage.
- Retained a package-free production library and the managed-only build policy.

## 0.2.0-alpha.1 — 2026-07-26

- Pinned the build to .NET SDK 8.0.423 and added three-platform CI.
- Centralized NuGet versions and migrated regression tests to managed xUnit v3.
- Added a build-time verifier for forbidden interop and native/mixed-mode
  binaries in the complete restored NuGet graph.
- Added header-relative xref/object offsets for PDFs with a leading prefix.
- Added strict indirect-object generation and compressed-object index checks.
- Hardened xref range validation before allocating or indexing entries.
- Kept configured resource-limit failures authoritative instead of retrying
  them through xref repair.
- Improved damaged-xref repair so parsed streams are skipped safely and xref
  streams/object streams can reconstruct compressed entries.
- Added incremental-update, repair, leading-prefix, generation, resource-limit
  and EOF-diagnostic regression fixtures.
- Added direct collection limits and normalized invalid Flate data to
  `PdfFormatException`.

## 0.1.0-alpha.1 — 2026-07-26

- Established Poppler 26.07.0 as the fixed upstream baseline.
- Added a managed PDF object model and bounds-checked syntax parser.
- Added classic xref, xref stream, hybrid, incremental and compressed-object
  stream support.
- Added Flate, LZW, ASCIIHex, ASCII85, RunLength and predictor decoding.
- Added catalog, page tree, inherited page attributes and page labels.
- Added document information, XMP access, ID, layout/mode and feature presence.
- Added embedded-file discovery and extraction.
- Added basic text extraction, simple font encodings and `ToUnicode` CMaps.
- Added a text-position SVG diagnostic renderer.
- Added a dependency-free CLI and executable regression-test project.
- Added explicit managed-only, compatibility, provenance and security limits.
