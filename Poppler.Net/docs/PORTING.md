# Porting Poppler 26.07.0 to managed .NET

## Definition

This project is a **source port**, not a binding. The build graph contains only
managed C# projects. It does not load `libpoppler`, use C++/CLI, declare
`DllImport`, execute Poppler command-line tools, or require Cairo, Splash,
Fontconfig, FreeType, LCMS, NSS, GPGME, OpenJPEG or libjpeg.

The supplied Poppler 26.07.0 tree contains 228,947 lines across C/C++ headers
and implementations. A faithful port therefore has to be delivered in audited
slices. Version `0.6.0-alpha.1` adds managed image decoding and the first
calibrated/special color pipeline on top of the hardened `0.2` foundation,
`0.3` security handler, `0.4` font/text layer and `0.5` graphics interpreter;
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
| `CMap`, `CharCodeToUnicode` | `PdfCMap` | Embedded/Identity code, CID and Unicode maps |
| `GfxFont`, FoFi inspection | `PdfFontDecoder`, `PdfOpenTypeCmap` | Text metrics and sfnt fallback; no outlines |
| `TextOutputDev` | `PdfTextExtractor`, `PdfTextLayoutEngine` | Horizontal/vertical runs and initial reading order |
| `Outline`, `Link` | — | Planned |
| `Decrypt`, `SecurityHandler` | `PdfStandardSecurityHandler`, `PdfCryptography` | R2–R6 implemented |
| `Annot`, `Form` | detection only | Planned |
| `Gfx`, `GfxState`, `Function` | `PdfGraphicsInterpreter`, graphics model, `PdfFunction`, `PdfShadingReader` | Vector slice plus sampled/exponential/stitching functions |
| `ImageStream`, `DCTStream`, `JPXStream`, `JBIG2Stream`, `CCITTFaxStream` | `PdfImageDecoder`, `CcittFaxDecoder` | Managed Image XObject decoding |
| `GfxColorSpace`, common ICC transforms | `PdfColorSpaceDefinition`, `PdfIccProfile` | Device, calibrated, indexed, spot and common matrix/shaper profiles |
| `SplashOutputDev`, Cairo | `SvgPageRenderer`, `PngEncoder` | Managed SVG backend and image PNG export; no page rasterizer |
| FreeType/font rasterization and shaping | — | Planned |
| JPEG/JPEG2000/JBIG2/CCITT | managed package codecs plus internal CCITT decoder | Image XObjects implemented; inline images deferred |
| color management/overprint | managed common color conversions | No LUT ICC, proofing or overprint |
| signatures/NSS/GPGME | — | Planned |
| PDF mutation and incremental save | byte-for-byte copy only | Planned |

## Next implementation slices

1. Complete corpus/differential/fuzz gates for the parser foundation.
2. Complete font engine: raw CFF charset/encoding parsing, complex shaping,
   predefined external CMaps, font substitution and glyph rasterization.
3. Extend the graphics interpreter with transparency groups, group soft masks,
   uncolored/mesh patterns, calculator functions and inline images.
4. Add LUT-based ICC profiles, proofing, rendering intents and overprint.
5. Annotations, AcroForm/XFA surface, actions, links and outlines.
6. Digital signature validation through managed cryptography.
7. Writer, advanced repair mode, fuzz corpus, PDF corpus differential tests
   and API parity.

Each slice should be compared against the same Poppler 26.07.0 fixture corpus;
differences must be classified as parser, layout, font, raster or color errors.
