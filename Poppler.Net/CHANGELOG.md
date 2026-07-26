# Changelog

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
