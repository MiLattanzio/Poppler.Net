# Porting Poppler 26.07.0 to managed .NET

## Definition

This project is a **source port**, not a binding. The build graph contains only
managed C# projects. It does not load `libpoppler`, use C++/CLI, declare
`DllImport`, execute Poppler command-line tools, or require Cairo, Splash,
Fontconfig, FreeType, LCMS, NSS, GPGME, OpenJPEG or libjpeg.

The supplied Poppler 26.07.0 tree contains 228,947 lines across C/C++ headers
and implementations. A faithful port therefore has to be delivered in audited
slices. Version `0.9.0-alpha.1` builds on the stable `0.8.0` text and graphics
interpreter,
adds managed CFF1/Type 2 and Type 1 outline readers, executes Type 3 CharProcs,
decodes inline images, ports the canonical Base-14 width tables and performs
managed font-file substitution with advance fitting. It also adds
filter-aware inline-image boundaries, soft-mask transfer functions,
Poppler-compatible damaged page-box recovery, bounded external CMap
inheritance, initial CFF2/default-instance execution, targeted OpenType
vertical/ligature substitution and improved font-family matching. Beta 2 adds
triangle/patch meshes, uncolored patterns, calculator functions, transparency
group refinements and process-overprint preview on top of the hardened `0.2`
foundation, `0.3` security handler, `0.4` font/text layer,
`0.5` graphics interpreter, `0.6` image/color pipeline and `0.7` raster;
it is not a claim that all of Poppler has already been translated.

## Implemented sequence

1. Freeze the upstream input at Poppler 26.07.0 and retain GPL provenance.
2. Map `Object`, `Array`, `Dict`, `Stream`, `Lexer` and `Parser` to immutable
   managed PDF value types and a bounds-checked syntax reader.
3. Port classic xref tables, xref streams, incremental `/Prev` chains and
   compressed object streams.
4. Port stream decoding for Flate, LZW, ASCIIHex, ASCII85 and RunLength,
   including TIFF/PNG predictors.
5. Port the read-only `PDFDoc`/`Catalog`/`Page` path: version, trailer,
   catalog, inherited page attributes, metadata and labels.
6. Port file specifications and embedded-file name trees.
7. Implement an initial text-content interpreter with `ToUnicode` CMaps,
   simple encodings and common text-state operators.
8. Expose a C# API patterned after Poppler's stable C++ API and a managed CLI.
9. Add NUnit regression fixtures and a package-graph verifier that rejects
   native or mixed-mode NuGet assets.
10. Harden header-relative offsets, incremental revisions, generation checks
    and damaged-xref/object-stream reconstruction.
11. Port Standard Security Handler revisions 2–6, crypt-filter routing,
    locked-document retry and permission flags.
12. Split `GfxFont`/CMap responsibilities into managed simple/composite font
    decoding, CID metrics, embedded sfnt fallback and directional text layout.
13. Port vector `Gfx`/`GfxState` responsibilities into an immutable public
    display list: CTMs, paths, clips, Form/Image XObjects, tiling patterns and
    axial/radial shading functions.
14. Port Image XObject sample decoding, masks, PNG export, calibrated/special
    color spaces, ICC matrix/shaper profiles and common compressed codecs.
15. Port the first Splash path scanner and straight-alpha compositor, Form
    transparency groups, graphics-state soft masks and embedded TrueType
    `glyf` outlines, retaining source character codes/CIDs for subset glyph
    selection.
16. Integrate text-showing operators with `Gfx` ordering, text paint/clip
    state and Form resources; add managed CFF1/Type 2, Type 1 and Type 3
    outline execution, raw inline images and font-file substitution.
17. Port Poppler's canonical Base-14 widths and reconcile horizontal
    replacement outlines with PDF advances.
18. Make common filtered inline-image boundaries deterministic, apply
    sampled/exponential/stitching soft-mask transfer functions and normalize
    page boxes with Poppler's missing-`MediaBox` fallback.
19. Resolve external encoding and `ToUnicode` CMaps with bounded inheritance;
    add initial CFF2 execution, targeted vertical/ligature GSUB and ranked
    Narrow/Condensed font-file fallback.
20. Decode shading types 4–7, paint uncolored tiling patterns, evaluate bounded
    calculator functions, preserve isolated/non-isolated/knockout group
    behavior and preview process-CMYK overprint mode 1.
21. Freeze the `0.8` public API, synchronize shared document/CMap caches,
    snapshot caller-owned option collections and make font/CMap discovery
    deterministic across repeated runs.
22. Read immutable page annotations, resolve direct and named destinations,
    inspect URI/GoTo/Named actions and map bounded normal appearance streams
    into the shared graphics display list with managed fallbacks.

## Upstream-to-managed map

| Poppler 26.07.0 | Managed counterpart | Alpha status |
| --- | --- | --- |
| `Object`, `Array`, `Dict`, `Ref` | `PdfObject` hierarchy | Implemented |
| `Lexer`, `Parser` | `PdfSyntaxReader` | Implemented |
| `XRef`, `Hints`, `Linearization` | `PdfCrossReference`, detection | Substantial; repair remains partial |
| `Stream`, `FlateStream`, image streams | `PdfFilterPipeline`, `PdfImageDecoder` | Common filters and image terminal codecs implemented |
| `PDFDoc`, `Catalog`, `Page` | `Document`, `Page` | Read-only core implemented |
| `PageLabelInfo` | `PageLabelTree` | Implemented |
| `FileSpec` | `EmbeddedFile` | Implemented |
| `CMap`, `CharCodeToUnicode` | `PdfCMap`, `PdfCMapResolver` | Embedded/Identity and bounded external code, CID and Unicode maps with inheritance |
| `GfxFont`, FoFi inspection | `PdfFontDecoder`, `PdfOpenTypeCmap`, `PdfOpenTypeLayout`, `PdfTrueTypeFont`, `PdfCffFont`, `PdfType1Font` | Text metrics, sfnt fallback, common TrueType/CFF1/Type 1 outlines, initial CFF2 and targeted GSUB |
| `TextOutputDev` | `PdfTextExtractor`, `PdfTextLayoutEngine` | Horizontal/vertical runs and initial reading order |
| `Outline`, `Link` | `PdfAnnotation`, `PdfAnnotationAction`, `PdfDestination` | Link/destination slice implemented; outlines planned |
| `Decrypt`, `SecurityHandler` | `PdfStandardSecurityHandler`, `PdfCryptography` | R2–R6 implemented |
| `Annot`, `Form` | `PdfAnnotationReader`, appearance reuse of `PdfGraphicsInterpreter` | Initial read-only annotation and normal-appearance slice |
| `Gfx`, `GfxState`, `Function` | `PdfGraphicsInterpreter`, graphics model, `PdfFunction`, `PdfShadingReader`, `PdfMeshShadingReader` | Vector slice plus sampled/exponential/stitching/calculator functions and shading types 2–7 |
| `ImageStream`, `DCTStream`, `JPXStream`, `JBIG2Stream`, `CCITTFaxStream` | `PdfImageDecoder`, `CcittFaxDecoder` | Managed Image XObject decoding |
| `GfxColorSpace`, common ICC transforms | `PdfColorSpaceDefinition`, `PdfIccProfile` | Device, calibrated, indexed, spot and common matrix/shaper profiles |
| `SplashOutputDev`, Splash path/composite | `PdfRasterRenderer`, `RasterGeometry`, `PdfBlend`, `RasterSurface` | Initial managed page raster, antialiasing and transparency |
| Cairo vector output | `SvgPageRenderer` | Managed SVG preview |
| FreeType/font rasterization and shaping | managed TrueType/CFF1/CFF2/Type 1 readers plus `PdfOpenTypeLayout`, `PdfFontSubstitutionResolver` and Base-14 metrics | Common outlines, CFF2 default instance, targeted GSUB, canonical advances and ranked file substitution; hinting/full shaping remain planned |
| JPEG/JPEG2000/JBIG2/CCITT | managed package codecs plus internal CCITT decoder | Image XObjects plus common inline-image data implemented |
| color management/overprint | managed common color conversions and process overprint preview | No LUT ICC, proofing or spot-color overprint |
| signatures/NSS/GPGME | — | Planned |
| PDF mutation and incremental save | byte-for-byte copy only | Planned |

## Next implementation slices

1. Complete corpus/differential/fuzz gates for the parser foundation.
2. Complete font engine: CFF2 variation-region interpolation, rare Type 1/CFF
   operators, contextual GSUB/GPOS and complex shaping, Type 1 `seac` and
   hinting.
3. Refine nested knockout/non-isolated group interaction, adaptive patch
   tessellation and stroke geometry; add remaining
   Flate/LZW/CCITT/JBIG2/JPX inline-image boundary cases.
4. Add LUT-based ICC profiles, proofing, rendering intents and spot-color
   overprint.
5. Complete advanced annotations, AcroForm/XFA field/widget behavior, optional
   content, outlines and additional inspection-only actions.
6. Digital signature validation through managed cryptography.
7. Writer, advanced repair mode, fuzz corpus, PDF corpus differential tests
   and API parity.

Each slice should be compared against the same Poppler 26.07.0 fixture corpus;
differences must be classified as parser, layout, font, raster or color errors.
