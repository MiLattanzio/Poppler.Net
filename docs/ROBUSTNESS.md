# Recovery, limits and decoded-stream caching

Release `0.9.0-beta.2` keeps repair conservative: it recovers independent
content that can be proven usable, reports what was skipped and never turns a
configured safety-limit failure into a warning.

## Page-tree recovery

`AttemptPageTreeRepair` defaults to `true`. While walking a `/Pages` node,
Poppler.Net can skip an invalid child reference or a circular child branch and
continue with valid siblings. A document whose damaged tree yields no valid
page still fails instead of silently opening as empty.

The following diagnostics are stable:

- `page-tree.repaired` means at least one invalid branch was skipped;
- `page-tree.count-mismatch` means a `/Pages /Count` value did not match the
  pages actually discovered.

Set `AttemptPageTreeRepair = false` when validation workflows require the
first malformed page-tree branch to fail the load.

## Page-content recovery

`AttemptContentStreamRepair` also defaults to `true`. For a page `/Contents`
array, an invalid stream reference, unsupported/corrupt stream or non-stream
entry can be skipped when another stream in the same array decodes
successfully. The valid streams preserve their original order and are joined
with one PDF whitespace byte.

Recovery does not hide:

- a malformed single `/Contents` stream;
- an array in which every decodable stream fails;
- `PdfLimitException`, including aggregate decoded-size failures.

Successful partial recovery adds one deduplicated `content.repaired`
diagnostic. Set `AttemptContentStreamRepair = false` for strict processing.

## Decoded-stream cache

Repeated page creation previously decoded the same indirect Flate/LZW stream
again for text, display-list and rendering work. The document now shares a
thread-safe lazy result for indirect streams.

`MaximumCachedDecodedBytes` defaults to 64 MiB per document. A decoded stream
that would exceed the remaining budget is returned to the current operation
but not retained. The cache also has a fixed entry ceiling, is cleared during
security-model resets and disposal, and never changes
`MaximumDecodedStreamBytes`. Set the byte budget to zero to disable caching.

## New structural limits

- `MaximumContentStreamsPerPage` defaults to 10,000 and is checked before
  concatenating a page content array.
- `MaximumContentOperands` defaults to 250,000 and is checked while tokenizing
  operands before an operator is reached.

Both values must be positive. They complement the existing decoded-byte,
graphics-operation, display-list, path, recursion, object and collection
limits.

## Stroke compatibility

The beta 2 raster scanner carries dash state across every segment of a
subpath, repeats odd-length dash arrays according to the PDF rule and applies
phase before the first segment. Butt, round and projecting-square caps plus
miter, round and bevel joins are painted explicitly. A miter beyond `/M`
falls back to a bevel.

Degenerate zero-length geometry and extreme anisotropic transforms remain
managed approximations rather than pixel-identical Splash behavior.
