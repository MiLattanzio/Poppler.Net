# Compatibility matrix

## Works in 0.1.0-alpha.1

- PDF 1.x and 2.0 header discovery.
- Classic xref tables and trailers.
- Xref streams and compressed object streams.
- Incremental updates through `/Prev`; hybrid `/XRefStm` lookup.
- Catalog and page-tree traversal with inherited boxes, resources and rotation.
- Flate, LZW, ASCIIHex, ASCII85 and RunLength filters.
- TIFF predictor 2 and PNG predictors 10–15.
- Document information, XMP metadata bytes, viewer mode/layout and PDF IDs.
- Number-tree page labels.
- Embedded-file name trees and extraction.
- Unencrypted content streams.
- Text operators `BT`, `ET`, `Tf`, `Tm`, `Td`, `TD`, `T*`, `Tj`, `TJ`, `'`,
  `"`, `Tc`, `Tw`, `Tz`, `TL`, `Ts`.
- Simple one-byte fonts and common `ToUnicode` bfchar/bfrange CMaps.
- Diagnostic SVG output containing extracted text.

## Explicit limitations

- Encrypted documents are detected and rejected; passwords are not yet used.
- Digital signatures are neither created nor validated.
- Raster rendering is not implemented.
- SVG output does not reproduce paths, images, clipping, transparency or exact
  glyph outlines and must not be used for visual-conformance testing.
- Complex scripts, shaping, ligatures without `ToUnicode`, Type 3 fonts,
  multibyte CMaps without a usable `ToUnicode`, and vertical writing are not
  complete.
- JPEG, JPEG2000, JBIG2 and CCITT payloads are not decoded by the public
  `GetDecodedBytes` path.
- Annotations, forms, optional content, actions, movie/sound and JavaScript are
  not executed. Presence detection is metadata only.
- Saving produces a byte-for-byte copy; object mutation is not implemented.
- Damaged-xref repair is a conservative object-header scan, not Poppler's full
  recovery behavior.

## Safety limits

Default limits are 256 MiB input, 256 MiB decoded per stream, 1,000,000
indirect objects, 10,000 pages, nesting depth 128 and object recursion 64.
Use `PdfReadOptions` to lower limits for server workloads.
