# Fonts and text in 0.4

Version `0.4.0-alpha.1` ports the read-only font and text responsibilities
needed before the graphics interpreter. All decoding executes in managed C#.
The implementation does not use FreeType, Fontconfig, HarfBuzz or a native
font library.

## Character pipeline

Simple fonts consume one PDF character code at a time. Differences and a
clear-text embedded Type 1 encoding map glyph names through the Adobe naming
conventions, including `uniXXXX`, `uXXXXX`, underscore sequences and common
ligature names. WinAnsi, MacRoman, Standard, Symbol and a useful Zapf subset
have managed fallbacks. Explicit `ToUnicode` mappings always take precedence.

Type 0 fonts keep two mappings separate:

1. the Encoding CMap consumes one to four bytes and maps the source code to a
   CID;
2. the `ToUnicode` CMap maps the original source code to Unicode.

This is required for custom CMaps where the source value and CID differ.
Codespace, `cidchar`, `cidrange`, `bfchar` and `bfrange` entries are supported.
Identity-H and Identity-V are built in. CID ranges remain compressed to avoid
allocating one entry per CID.

When a PDF omits `ToUnicode`, an embedded TrueType or OpenType font can supply
a fallback through sfnt `cmap` format 4 or 12. `CIDToGIDMap` is applied before
the reverse Unicode lookup. Adobe-Identity/Adobe-UCS fonts finally fall back
to identity Unicode where a valid scalar exists.

## Metrics and placement

- simple-font `FirstChar`, `Widths` and descriptor `MissingWidth`;
- Type 3 `FontMatrix` width scaling;
- CID default and exceptional widths through `DW` and both `W` forms;
- vertical defaults and exceptions through `DW2` and both `W2` forms;
- font descriptor ascent/descent for run bounds;
- character spacing, word spacing, horizontal scale, rise and `TJ`
  adjustments;
- horizontal and vertical text-matrix advancement.

`TextBox.WritingMode` and `TextBox.IsRightToLeft` expose directional
information. `TextLayout.Physical` clusters baselines and respects dominant
right-to-left direction. `TextLayout.NonRawNonPhysical` adds a conservative
two-column reading-order heuristic. `TextLayout.RawOrder` preserves content
stream order.

## Font inspection API

`Page.Fonts` returns one `FontInfo` per page resource. It reports the resource
and base names, PDF font type, encoding, writing mode, subset marker,
`ToUnicode` presence and embedded Type 1, CFF, TrueType or OpenType container.
The same data is available through:

```bash
poppler-net fonts input.pdf
```

Embedded byte counts refer to the decoded font stream when its filters are
supported, otherwise to the retained encoded payload. Font programs remain
owned by the document and are not exposed as mutable buffers.

## Resource limits

`PdfReadOptions.MaximumCMapMappings` defaults to 250,000. It covers expanded
Unicode entries and explicit CID entries. CID ranges themselves use a bounded
range record. Existing input, decoded stream, object and collection limits
also apply to fonts and CMaps.

## Deliberate limits

- release `0.7` rasterizes embedded TrueType `glyf` simple/composite outlines,
  but not CFF/Type 1 outlines or font hinting;
- no complex-script shaping, OpenType GSUB/GPOS processing or full Unicode
  Bidirectional Algorithm;
- no system-font discovery or substitution;
- no raw CFF charset/encoding fallback when `ToUnicode` is absent;
- no encrypted/eexec Type 1 encoding recovery beyond the clear-text section;
- no external `poppler-data` CMap packs and no arbitrary named `usecmap`
  inheritance beyond Identity-H/Identity-V;
- Type 3 CharProcs are not rendered; their encodings and text advances are
  available for extraction.

Raster text is currently a post-display-list pass, so exact PDF text paint
mode, color, clipping, transparency, interleaving and Form-nested text remain
future work. Recognizing a non-TrueType embedded font format does not imply
that its glyph outlines are rendered.
