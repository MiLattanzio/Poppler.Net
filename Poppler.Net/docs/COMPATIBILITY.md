# Compatibility matrix

## Works in 0.4.0-alpha.1

- PDF 1.x and 2.0 header discovery.
- Classic xref tables and trailers.
- Xref streams and compressed object streams.
- Incremental updates through `/Prev`; hybrid `/XRefStm` lookup.
- Header-relative offsets when up to 1,023 leading bytes precede `%PDF-`.
- Conservative damaged-xref reconstruction, including xref streams and
  compressed object streams.
- Catalog and page-tree traversal with inherited boxes, resources and rotation.
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
- Type 0 Encoding CMaps with codespaces, cidchar/cidrange and separate
  source-code-to-CID/source-code-to-Unicode handling.
- CIDFontType0/CIDFontType2 `DW`/`W`, `DW2`/`W2`, CIDToGIDMap and
  Identity-H/Identity-V.
- Embedded Type 1, CFF, TrueType and OpenType identification.
- Managed sfnt cmap format 4/12 Unicode fallback for embedded TrueType and
  OpenType fonts without `ToUnicode`.
- Vertical text advances, run direction, right-to-left physical ordering and
  conservative two-column reading order.
- Public per-page font information and CLI font listing.
- Diagnostic SVG output containing extracted text.
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
- Raster rendering is not implemented.
- SVG output does not reproduce paths, images, clipping, transparency or exact
  glyph outlines and must not be used for visual-conformance testing.
- Complex-script shaping, GSUB/GPOS, full bidi, raw CFF fallback, arbitrary
  external named CMaps and font substitution are not implemented.
- Type 3 encodings and advances are read, but CharProcs are not rendered.
- Vertical writing is supported for extraction and metrics; vertical glyph
  forms and outlines await the rasterizer.
- JPEG, JPEG2000, JBIG2 and CCITT payloads are not decoded by the public
  `GetDecodedBytes` path.
- Annotations, forms, optional content, actions, movie/sound and JavaScript are
  not executed. Presence detection is metadata only.
- Saving produces a byte-for-byte copy; object mutation is not implemented.
- Damaged-xref repair remains conservative. It does not yet reproduce all of
  Poppler's stream-end heuristics or repair every malformed incremental chain.

## Safety limits

Default limits are 256 MiB input, 256 MiB decoded per stream, 1,000,000
indirect objects, 1,000,000 direct collection items, 250,000 CMap mappings,
10,000 pages, nesting depth 128 and object recursion 64. Use `PdfReadOptions`
to lower limits for server workloads.
