# Recovery, limits and decoded-stream caching

Release `0.9.0-beta.2` keeps repair conservative: it recovers independent
content that can be proven usable, reports what was skipped and never turns a
configured safety-limit failure into a warning.

## 0.13.0-beta.2 export hardening

All new export budgets are operation-local, immutable snapshots. Concurrent
read-only conversions from one `Document` do not share counters or output
buffers. HTML, structured and page-extraction operations check selected pages
before array allocation and charge semantic nodes, bundle files and UTF-8 or
binary bytes before their backing collection or stream grows.

The default public export budgets are:

- HTML: 10,000 pages, 1,000,000 DOM/SVG nodes, 10,000 files, 256 MiB output
  and 64 MiB generated web-font data;
- structured output: 10,000 pages, 1,000,000 semantic nodes, 10,000 files and
  256 MiB output;
- separated-page ranges: 10,000 results and 512 MiB cumulative output, in
  addition to the existing 256 MiB per-page output/stream limits.

The WebAssembly playground deliberately lowers these to a 64 MiB input, 512
pages, 500,000 nodes, 2,048 files and 128 MiB output. ZIP entries are counted
before insertion; preview/download blob URLs are revoked after use and during
page teardown. The UI reports these operational bounds rather than allowing a
browser out-of-memory failure to masquerade as an unexpected conversion error.

CLI-generated names normalize both directory separators, control/invalid
characters, excessive length and Windows reserved device names. Hostile
resource names remain deterministic local leaf names and cannot create an
unbounded path or escape the selected output directory.

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

## Cumulative display-list protection

Beta 2 adds limits around resources that are individually valid but hostile in
aggregate. `MaximumClipPaths` defaults to 4,096 per graphics state and is
checked before retaining another text, path, Form, annotation or soft-mask
clip. `MaximumPageImagePixels` defaults to 200,000,000 decoded pixels and
`MaximumPageMeshTriangles` defaults to 262,144 triangles across one page
interpretation. Their counters are charged before the next image decode or
mesh element is retained. Repeated non-mask Image XObjects share one immutable
decoded value when the object, resource dictionary and resource name match;
color-dependent image masks remain per-use.

ASCIIHex, ASCII85 and RunLength filters reserve their decoded byte budget
before each output write. Combined `/Contents` arrays include the separator
inserted between streams in the same calculation. Extreme finite page boxes
and working-surface dimensions fail with stable `PdfLimitException`
diagnostics before array allocation.

## Raster geometry protection

`MaximumRasterGeometrySegments` defaults to 4,000,000 per raster operation.
One cumulative counter covers device-error flattening segments, positive and
zero-length dash fragments, stroke-outline edges and temporary clip geometry.
The counter is checked before growing each corresponding collection. Separate
concurrent renders receive separate counters.

The `0.12.0-alpha.1` scanner repeats odd dash arrays, normalizes negative
phase, preserves state through every source segment and across a closed seam,
and retains zero-length on-elements for dotted round/square caps. Non-hairline
caps and joins are completed in user space before anisotropic, sheared or
reflected CTMs are applied. A miter beyond `/M` falls back to a bevel.

Cubic subdivision has an internal depth cap of 16 and round outlines have an
internal 4,096-edge cap. Singular/near-singular non-hairline paints are
skipped deterministically; singular clips are empty. These internal caps are
not public tuning parameters.

## Transparency working set and SVG fallback

`MaximumRenderWorkingBytes` defaults to 1 GiB per raster operation. It counts
every simultaneously live high-precision pixel surface: the final target,
group content, initial non-isolated/knockout backdrops, temporary knockout
children and cached soft masks. Each allocation reserves its full checked byte
size first and disposal releases it, so deeply nested groups fail with
`PdfLimitException` before unbounded surface growth. The existing
`MaximumTransparencyGroupDepth` of 32 remains authoritative for both group and
soft-mask recursion.

`MaximumSvgFallbackPixels` defaults to 25,000,000 and is checked against the
pixel-aligned conservative fallback bounds before a raster surface is
allocated. Paths, strokes, text, images, clips, function domains/BBoxes, mesh
vertices/control hulls and nested groups contribute to those bounds. Nonzero
page rotation retains the complete CropBox conservatively. Raster fallback
surfaces also remain subject to `MaximumRenderPixels`,
`MaximumRenderWorkingBytes` and all ordinary raster limits.
