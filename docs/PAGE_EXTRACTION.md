# Standalone PDF page extraction

Poppler.Net `0.13.0-alpha.2` can copy one page, or a selected page range, into
separate autonomous PDFs. The implementation is managed-only and deliberately
limited to page isolation; it is not a general-purpose PDF mutation API.

## API and naming

`Page.ExtractPdf` and `Document.ExtractPage` return one complete PDF as bytes.
`Page.SavePdf` and `Document.SavePage` write it to disk.
`Document.ExtractPages` returns one `PdfExtractedPage` per selected source page
and retains the zero-based source index and one-based source page number.

API page indexes are zero-based. CLI page numbers are one-based:

```bash
poppler-net separate input.pdf output-pages
poppler-net separate input.pdf output-pages --first-page 2 --last-page 5
```

The CLI uses the source stem and a zero-padded source page number, for example
`input-page-0002.pdf`. Existing files are never overwritten: a numeric suffix
is added to choose a collision-free path. The WebAssembly playground uses the
same stable source-page numbering for individual PDFs and ZIP downloads.

## Preserved page state

Each result has a new minimal catalog and one-page page tree. The writer copies
the reachable object graph needed by that page, including:

- inherited media/crop/bleed/trim/art boxes, resources, rotation and user unit;
- content streams, fonts, images, graphics state, color and optional-content
  resources;
- supported annotations whose semantics remain valid on the isolated page;
- terminalized page widgets, inherited field values and required AcroForm
  defaults/resources;
- selected catalog state required for rendering, such as output intents,
  optional-content properties, language and viewer preferences.

References to another source page are not copied. Local explicit destinations
are rewritten to the generated page. Cross-page destinations are omitted or
replaced by `null` when encountered in otherwise preserved resource graphs.
Document outlines, source page trees, document information and the original
xref/trailer are not retained.

Indirect and compressed objects are materialized into deterministic,
sequential indirect objects. Streams retain their encoded representation
unless an explicit PDF `/Crypt` filter must be removed from unlocked input.
The result uses a classic xref table and is deterministic for identical input
and options.

## Encryption policy

Locked documents cannot be extracted. A document opened with the correct user
or owner password may be extracted, but every result is intentionally
unencrypted and contains no source encryption dictionary. This avoids copying
document-level keys or permissions into an invalid one-page security context.
Applications that need encrypted output must apply encryption in a separate,
explicit post-processing step outside this read-only API.

## Safety limits

`PdfPageExtractionOptions` applies per-output bounds. Defaults are 100,000
indirect objects, traversal depth 64, 256 MiB cumulative stream bytes and
256 MiB output. `PreserveAnnotations` can be disabled. Invalid options and
inputs that exceed a bound fail deterministically with an argument or
`PdfLimitException`; indirect-reference cycles are tracked and never expanded
recursively without a bound.

Extraction writes each selected page independently. Limits therefore apply to
each generated PDF rather than to the complete range.
