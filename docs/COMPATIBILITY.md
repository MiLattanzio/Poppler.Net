# Compatibility matrix

## Works in 0.8.0-beta.2

- PDF 1.x and 2.0 header discovery.
- Classic xref tables and trailers.
- Xref streams and compressed object streams.
- Incremental updates through `/Prev`; hybrid `/XRefStm` lookup.
- Header-relative offsets when up to 1,023 leading bytes precede `%PDF-`.
- Conservative damaged-xref reconstruction, including xref streams and
  compressed object streams.
- Catalog and page-tree traversal with inherited boxes, resources and rotation;
  reversed boxes are normalized, page boxes are clipped to `MediaBox`, and a
  damaged tree without `MediaBox` uses Poppler's 612×792-point fallback.
- Flate, LZW, ASCIIHex, ASCII85 and RunLength filters.
- TIFF predictor 2 and PNG predictors 10–15.
- Document information, XMP metadata bytes, viewer mode/layout and PDF IDs.
- Number-tree page labels.
- Embedded-file name trees and extraction.
- Unencrypted content streams.
- Text operators `BT`, `ET`, `Tf`, `Tm`, `Td`, `TD`, `T*`, `Tj`, `TJ`, `'`,
  `"`, `Tc`, `Tw`, `Tz`, `TL`, `Ts`.
- Simple one-byte Type 1, TrueType and Type 3 text metrics.
- WinAnsi, MacRoman, Standard and common Symbol/Zapf encodings, encoding
  Differences and Adobe-style Unicode glyph names.
- Clear-text encoding discovery in embedded Type 1 programs.
- Bounded `ToUnicode` codespace, bfchar and bfrange CMaps.
- External encoding and `ToUnicode` CMaps from explicit directories and
  common system `poppler-data` locations, including `/UseCMap` and
  PostScript `usecmap` inheritance.
- Type 0 Encoding CMaps with codespaces, cidchar/cidrange and separate
  source-code-to-CID/source-code-to-Unicode handling.
- CIDFontType0/CIDFontType2 `DW`/`W`, `DW2`/`W2`, CIDToGIDMap and
  Identity-H/Identity-V.
- Embedded Type 1, CFF1, CFF2, TrueType and OpenType identification.
- Managed sfnt cmap format 4/12 Unicode fallback for embedded TrueType and
  OpenType fonts without `ToUnicode`, plus format 0 source-character-code
  mapping for byte-encoded TrueType subsets.
- Vertical text advances, run direction, right-to-left physical ordering and
  conservative two-column reading order.
- Public per-page font information and CLI font listing.
- Graphics-state operators `q`, `Q`, `cm`, `w`, `J`, `j`, `M`, `d` and
  common `ExtGState` line/alpha/blend entries.
- Path operators `m`, `l`, `c`, `v`, `y`, `h`, `re`, all common
  fill/stroke combinations and nonzero/even-odd clipping.
- DeviceGray, DeviceRGB and DeviceCMYK fill/stroke colors plus pattern color
  selection.
- Recursive Form XObjects with matrices, BBox clipping, local/inherited
  resources and bounded recursion.
- Image XObject metadata plus decoded `Gray8`, `Rgb24` or straight-alpha
  `Rgba32` pixels with exact tightly packed row strides.
- Packed 1, 2, 4, 8 and 16-bit image samples, `/Decode` arrays, Flate/LZW/
  RunLength/predictor input and unfiltered data.
- DCT/JPEG, JPX/JPEG 2000 Part 1, JBIG2 with `JBIG2Globals`, CCITT Modified
  Huffman, Group 3 and Group 4 Image XObjects through managed decoders.
- Image masks, explicit masks, color-key masks and luminosity soft masks.
- CalGray, CalRGB, Lab, common matrix/shaper ICCBased, Indexed, Separation and
  DeviceN conversion to managed sRGB.
- PDF function types 0 sampled, 2 exponential, 3 stitching and bounded type 4
  calculator functions for tint transforms, gradients and soft masks.
- Public `Page.Images`, managed PNG encoding, CLI image extraction and SVG
  image embedding.
- Colored and uncolored tiling patterns plus shading patterns.
- Type 2 axial and type 3 radial shadings in device color spaces using
  exponential and stitching functions.
- Type 4 free-form and type 5 lattice Gouraud meshes plus type 6 Coons and
  type 7 tensor-product patch meshes, exposed through bounded triangle lists.
- Public backend-neutral `Page.Graphics` display lists.
- Public `PdfTextElement` entries in exact page/Form content-stream order,
  carrying font, size, glyph count, graphics state and all eight `Tr` modes.
- Managed text fill/stroke/invisible/clip painting, including accumulated text
  clips and graphics-state alpha, blend, soft-mask and clipping interaction.
- Embedded TrueType `glyf`, CFF1/CFF2 Type 2 and PFA/PFB Type 1 outlines
  through bounded managed readers; common CFF CID subroutines, CFF2
  FDSelect format 4/default-instance blend data and Type 1 eexec/`lenIV`
  programs are supported.
- OpenType GSUB single substitutions for `vert`/`vrt2` and exact
  `liga`/`rlig` ligatures for embedded or substituted managed fonts.
- Type 3 CharProc execution with font matrices, resources and inherited text
  paint state.
- Optional managed font-file substitution for Base-14/non-embedded fonts,
  with explicit search roots, Narrow/Condensed and Expanded/Extended family
  scoring, ranked glyph fallback and no native font API or rasterizer.
- Canonical Poppler widths for all fourteen standard fonts when `/Widths` is
  omitted; horizontally substituted outlines are fitted to the PDF advance.
- Raw and commonly filtered inline images, standard abbreviated dictionary
  keys/names, filter-aware ASCIIHex/ASCII85/RunLength/DCT boundaries and
  content-stream interleaving.
- Managed SVG vector output for paths, clipping, Form content, tiling patterns,
  axial/radial gradients, decoded Image XObjects and extracted text.
- Managed full-page RGBA raster output and PNG encoding at configurable DPI,
  Crop/Media/Bleed/Trim/Art page box and PDF page rotation.
- Supersampled path fill/stroke and clip coverage at 1×, 2×, 4× or 8×,
  adaptive cubic Bézier flattening, image sampling, gradients and colored
  tiling patterns.
- Straight-alpha compositing for the 16 standard separable/nonseparable PDF
  blend modes.
- Preserved Form transparency groups, isolated/non-isolated and knockout
  intermediate surfaces,
  graphics-state Alpha/Luminosity soft masks, luminosity backdrop color and
  sampled/exponential/stitching/calculator soft-mask transfer functions.
- `/OP`, `/op` and `/OPM` state plus process-CMYK overprint-mode-1 preview for
  solid DeviceCMYK and DeviceGray paint.
- Embedded TrueType `glyf` simple/composite outlines, common component
  transforms, quadratic contour conversion and managed antialiased glyph
  painting.
- Source PDF character codes and CIDs retained through text extraction for
  direct subset glyph selection, including multi-scalar ligatures; explicit
  DeviceGray, DeviceRGB and DeviceCMYK text fill colors.
- Public `PdfBitmap`, `Page.Render`, `RenderToPng`, `SavePng`,
  `RasterRenderOptions` and CLI `render`.
- Missing `%%EOF` and leading-prefix diagnostics.
- Standard Security Handler `V=1/R=2`, `V=2/R=3`, `V=4/R=4` and
  `V=5/R=5–6`.
- User- and owner-password authentication, locked documents and password retry.
- RC4-40, variable-length RC4, AES-128-CBC and AES-256-CBC object decryption.
- Revision 6 hardened password hashing and validation of AES-256 `/Perms`.
- Independent `StrF`, `StmF` and `EFF` crypt filters using `Identity`, `V2`,
  `AESV2` or `AESV3`.
- Explicit stream `/Crypt` filters and `EncryptMetadata false`.
- Permission mapping for print, modify, copy, annotations, forms,
  accessibility, assembly and high-resolution print.

## Explicit limitations

- Public-key security handlers and custom third-party security handlers are not
  implemented.
- Full SASLprep processing for revision 5/6 Unicode passwords is not yet
  implemented; Latin-1-compatible strings and UTF-8 fallback are supported.
- Encryption is read-only: saving preserves the original encrypted bytes and
  cannot change passwords, permissions or crypt filters.
- Digital signatures are neither created nor validated.
- Inline images whose first filter has no deterministic boundary handled by
  this alpha (including ambiguous Flate/LZW/CCITT/JBIG2/JPX cases), unusual
  filter chains or unsupported color spaces may still require more complete
  recovery.
- Non-isolated groups with non-Normal boundary blend modes and nested knockout
  shape/opacity interactions remain approximations. SVG remains a preview
  backend and does not paint mesh shadings.
- Shading type 1 is not painted. Patch meshes use a fixed bounded tessellation
  rather than Poppler's adaptive device-space subdivision.
- ICC LUT/device-link profiles, rendering intents, black-point compensation,
  proofing, spot-color calibration and spot-color overprint are not
  implemented. Process overprint is an sRGB managed preview rather than a
  color-managed proof. ICCBased falls back to `/Alternate` outside common
  matrix/shaper profiles.
- Complex-script shaping, contextual GSUB, GPOS and the full Unicode
  Bidirectional Algorithm are not implemented. GSUB coverage is deliberately
  limited to non-contextual `vert`/`vrt2` and exact `liga`/`rlig` lookups.
- CFF2 uses the default variation instance; full variation-region
  interpolation, rare Type 1/CFF operators, Type 1 `seac`, advanced Type 3
  behavior and font hinting are not implemented. Font substitution is
  file-based and its choice can vary with installed fonts unless explicit
  roots are supplied.
- External named CMaps require local CMap data and support the codespace,
  bfchar/bfrange, cidchar/cidrange and inheritance syntax used by common
  `poppler-data` packs; unsupported PostScript procedures are ignored.
- Pattern and special-color-space text paint has the same limitations as its
  corresponding vector brush implementation.
- Vertical writing supports metrics and non-contextual `vert`/`vrt2`
  alternates; contextual vertical shaping remains unsupported.
- Stroke cap/join/miter geometry and dash continuity use the first managed
  approximation and are not yet pixel-equivalent to Splash in every case.
- JPEG 2000 Part 2, unusual JPEG color transforms and malformed/extension
  streams outside the managed codec coverage remain unsupported.
- Annotations, forms, optional content, actions, movie/sound and JavaScript are
  not executed. Presence detection is metadata only.
- Saving produces a byte-for-byte copy; object mutation is not implemented.
- Damaged-xref repair remains conservative. It does not yet reproduce all of
  Poppler's stream-end heuristics or repair every malformed incremental chain.

## Safety limits

Default limits are 256 MiB input, 256 MiB decoded per stream, 16 MiB per
external CMap, 16 inherited CMaps, 1,000,000
indirect objects, 1,000,000 direct collection items, 250,000 CMap mappings,
1,000,000 graphics operations, 250,000 display-list elements, 1,000,000 path
segments, 100,000,000 decoded pixels per image, 32 image components, 16 MiB
per ICC profile, 1,000,000 sampled-function samples, graphics stack depth 256,
XObject depth 32, transparency-group depth 32, 100,000,000 rendered pixels,
33 shading stops, 65,536 mesh triangles, 10,000 pages, tree depth 128 and
object recursion 64. Use
`PdfReadOptions` to lower limits for server workloads.
