# Outlines and bookmarks

Version `0.10.0-alpha.1` adds a read-only managed model for the document
outline rooted at catalog `/Outlines`.

## Public model

`Document.OutlineItems` returns the top-level bookmarks in PDF sibling order.
Each immutable `PdfOutlineItem` exposes:

- `Title` and nested `Children`;
- resolved `Destination` and inspection-only `Action`;
- `IsOpen`, `IsBold` and `IsItalic`;
- optional DeviceRGB `Color`.

Direct destination arrays and named destinations from catalog `/Dests` or the
`/Names/Dests` name tree use the existing `PdfDestination` resolver. GoTo,
URI, Named, remote, script, form, layer and multimedia actions use the existing
`PdfAnnotationAction` model. The library only decodes these values: it never
navigates, launches a target, runs JavaScript or dispatches an action.

## Traversal and malformed input

Sibling order follows `/First` and `/Next`; child lists follow each item's
`/First`. `/Last`, `/Prev` and `/Parent` are checked against the discovered
hierarchy. Inconsistent optional back-links produce stable diagnostics without
changing the forward order.

Indirect references are tracked globally while the outline is materialized.
A node reached twice, including a `/Next` cycle or a child reused by another
parent, is skipped with `outline.node.repeated`. Circular action `/Next` chains
are truncated independently with `outline.action.circular`. The public tree is
therefore immutable and acyclic even when the PDF graph is not.

Traversal and final tree construction are iterative. One thread initializes
the lazy model; concurrent callers receive the same deterministic snapshot.

## Safety limits

`PdfReadOptions` adds:

- `MaximumOutlineItems` (default 100,000);
- `MaximumOutlineDepth` (default 128, with top-level items at depth 1);
- `MaximumOutlineTitleBytes` (default 65,536 bytes before decoding).

All three limits fail explicitly with `PdfLimitException`. Existing action and
named-destination limits remain active inside outline items.

## CLI

```bash
poppler-net outline input.pdf
```

The command prints the hierarchy, open state, style, RGB color, action type and
resolved page target. Output uses invariant numeric formatting. Diagnostics are
written separately to standard error.

## Not implemented

Bookmark creation, deletion, reordering, editing, persisted open-state changes
and action execution remain outside this release. Rendering does not depend on
outline state and is unchanged from `0.9.0`.
