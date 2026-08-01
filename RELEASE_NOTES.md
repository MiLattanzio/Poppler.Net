# Poppler.Net 0.10.0-alpha.1

Release date: 2026-08-01

`0.10.0-alpha.1` begins the document-reader completion line of the
managed-only Poppler 26.07.0 port. It adds immutable outlines/bookmarks and
navigation metadata without changing page parsing or rendering behavior.

## Highlights

- Adds `Document.OutlineItems` and immutable `PdfOutlineItem` trees.
- Exposes title, children, direct or named destination, inspection-only action,
  open state, bold/italic flags and optional RGB color.
- Preserves `/First` and `/Next` ordering while checking `/Last`, `/Prev` and
  `/Parent` consistency.
- Reuses the existing destination resolver and `PdfAnnotationAction` model;
  URI, JavaScript, Launch and every other action remain data only and are never
  executed.
- Truncates circular/repeated outline nodes and circular action chains with
  stable diagnostics instead of recursing indefinitely.
- Adds the `outline` CLI command with invariant, hierarchical output.
- Adds `MaximumOutlineItems`, `MaximumOutlineDepth` and
  `MaximumOutlineTitleBytes`.
- Adds a deterministic three-page corpus with seven bookmarks, three hierarchy
  levels, direct/named destinations, styles, actions and cycles.

## Compatibility and upgrading

The release adds public read-only members but does not remove or alter the
`0.9.0` callable surface. Existing code remains source compatible. Update the
package reference:

```xml
<PackageReference Include="Poppler.Net" Version="0.10.0-alpha.1" />
```

Applications should treat `Document`, pages, outlines, annotations, fields and
optional-content models as immutable inspection objects. Navigation and action
dispatch remain the responsibility of the host application.

## Safety and behavior

- The outline is initialized lazily and published as one immutable snapshot.
- Traversal and final tree construction are iterative.
- Top-level items have outline depth 1; exceeding any configured outline limit
  throws `PdfLimitException`.
- Direct and named destinations resolve to the existing zero-based
  `PdfDestination.PageIndex` model.
- Repeated nodes are skipped globally, so one malformed PDF object cannot be
  represented under multiple parents or form a cycle in the public tree.
- Outline state does not participate in page rendering; historical raster
  output is expected to remain byte-identical.

## Known limitations

- Bookmark creation, deletion, reordering, mutation and persisted open-state
  changes are not implemented.
- Actions and JavaScript are not executed.
- Tagged PDF structure, alternate optional-content configurations, complex
  shaping, complete color proofing, signature validation and PDF writing remain
  planned for later releases.
- SVG remains a preview backend and does not paint mesh shadings.

See `docs/OUTLINES.md` and `docs/COMPATIBILITY.md` for the detailed contract.

## Verification

- Release build of all four projects with warnings treated as errors.
- 218/218 NUnit tests, with every historical regression retained.
- Outline order, metadata, destinations, actions, cycles, concurrency, limits
  and deterministic fixture integrity covered by NUnit.
- Corpus opened, text-extracted and rendered independently with Poppler tools;
  all three pages were visually inspected.
- Historical parser, rendering, security, form, annotation and optional-content
  regressions retained.
- Managed-only dependency verification, NuGet inspection and offline rebuild
  from the extracted source ZIP are release gates.

Base revision: `b28458c306c9f4f32107379aa35119ff1a67c52d`.
