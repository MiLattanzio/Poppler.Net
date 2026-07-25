# Porting Poppler 26.07.0 to managed .NET

## Definition

This project is a **source port**, not a binding. The build graph contains only
managed C# projects. It does not load `libpoppler`, use C++/CLI, declare
`DllImport`, execute Poppler command-line tools, or require Cairo, Splash,
Fontconfig, FreeType, LCMS, NSS, GPGME, OpenJPEG or libjpeg.

The supplied Poppler 26.07.0 tree contains 228,947 lines across C/C++ headers
and implementations. A faithful port therefore has to be delivered in audited
slices. Version `0.2.0-alpha.1` is a hardened foundation, not a claim that all
of Poppler has already been translated.

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
9. Add xUnit v3 regression fixtures and a package-graph verifier that rejects
   native or mixed-mode NuGet assets.
10. Harden header-relative offsets, incremental revisions, generation checks
    and damaged-xref/object-stream reconstruction.

## Upstream-to-managed map

| Poppler 26.07.0 | Managed counterpart | Alpha status |
| --- | --- | --- |
| `Object`, `Array`, `Dict`, `Ref` | `PdfObject` hierarchy | Implemented |
| `Lexer`, `Parser` | `PdfSyntaxReader` | Implemented |
| `XRef`, `Hints`, `Linearization` | `PdfCrossReference`, detection | Substantial; repair remains partial |
| `Stream`, `FlateStream` | `PdfFilterPipeline` | Common filters implemented |
| `PDFDoc`, `Catalog`, `Page` | `Document`, `Page` | Read-only core implemented |
| `PageLabelInfo` | `PageLabelTree` | Implemented |
| `FileSpec` | `EmbeddedFile` | Implemented |
| `TextOutputDev`, CMaps | `PdfTextExtractor` | Common Latin/Unicode path |
| `Outline`, `Link` | — | Planned |
| `Decrypt`, `SecurityHandler` | detection only | Planned |
| `Annot`, `Form` | detection only | Planned |
| `Gfx`, `GfxState`, `Function` | content operation reader | Partial |
| `SplashOutputDev`, Cairo | `SvgPageRenderer` | Diagnostic only |
| FoFi/FreeType/font rasterization | — | Planned |
| JPEG/JPEG2000/JBIG2/CCITT | pass-through stream data | Planned decode |
| color management/overprint | — | Planned |
| signatures/NSS/GPGME | — | Planned |
| PDF mutation and incremental save | byte-for-byte copy only | Planned |

## Next implementation slices

1. Complete corpus/differential/fuzz gates for the parser foundation.
2. Encryption revisions 2–6 and permission enforcement.
3. Complete font engine: Type 1/CFF/TrueType parsing, shaping, substitution,
   vertical writing and glyph rasterization.
4. Full graphics interpreter, transparency groups, shadings, patterns,
   clipping, blend modes and image masks.
5. Managed JPEG, JPEG2000, JBIG2 and CCITT decoders.
6. Annotations, AcroForm/XFA surface, actions, links and outlines.
7. Color spaces, ICC profiles, spot colors and overprint.
8. Digital signature validation through managed cryptography.
9. Writer, advanced repair mode, fuzz corpus, PDF corpus differential tests
   and API parity.

Each slice should be compared against the same Poppler 26.07.0 fixture corpus;
differences must be classified as parser, layout, font, raster or color errors.
