# Annotations, destinations and widget integration in 0.9

Version `0.9.0-alpha.1` adds a read-only managed annotation slice modeled
after Poppler's `Annot`, `Link` and destination handling. It never executes an
action, opens a URI or changes a document.

Version `0.9.0-alpha.2` recognizes `/Widget` as a typed annotation and links
canonical AcroForm widgets to the field model described in
[FORMS.md](FORMS.md).

Version `0.9.0-alpha.3` evaluates annotation `/OC` entries together with the
standard visibility flags. See
[OPTIONAL_CONTENT.md](OPTIONAL_CONTENT.md).

Version `0.9.0-beta.1` adds advanced annotation subtypes, review threads,
file-attachment annotations and bounded inspection of advanced action chains.
It remains strictly read-only and never executes an action.

## Public model

`Page.Annotations` is an immutable, lazily initialized list in the page's
original `/Annots` order. Each `PdfAnnotation` exposes:

- subtype and normalized `/Rect`;
- contents, unique name, title, subject, icon name and modification date;
- standard annotation flags;
- border style, dash pattern, opacity, exterior and interior colors;
- quad points, vertices, line points and ink paths;
- stable indirect-object IDs, popup/parent/reply relationships and review
  state;
- intent, rich text, default style, callout geometry, line endings and
  rectangle differences;
- lazy attachment data for FileAttachment annotations;
- the selected normal-appearance state and whether it exists;
- default screen visibility after annotation flags and optional-content state;
- a resolved `PdfAnnotationAction`.

The typed annotation set covers Link, Text, FreeText, Highlight,
Underline, Squiggly, StrikeOut, Square, Circle, Line, Polygon, PolyLine, Ink,
Stamp, Widget, Caret, Popup, FileAttachment, Sound, Movie, Screen,
PrinterMark, TrapNet, Watermark, 3D and Redact. Unknown subtypes remain visible as
`PdfAnnotationType.Unknown` instead of being discarded.

## Links and destinations

Link actions are decoded without being followed:

- direct `/Dest` arrays;
- `/GoTo` actions;
- `/URI` actions;
- `/Named` viewer actions;
- name and string destinations from the catalog `/Dests` dictionary;
- `/Names/Dests` name trees, including indirect kids and destination
  dictionaries.

`PdfDestination` reports a zero-based page index, destination kind and the
applicable XYZ/Fit coordinates. `Document.NamedDestinations` exposes every
resolvable named destination in ordinal order, and
`Document.ResolveDestination(name)` resolves one name.

Beta 1 also decodes GoToR, Launch, JavaScript, SubmitForm, ResetForm,
ImportData, Hide, SetOCGState, Rendition, Trans and GoTo3DView payloads plus
bounded `/Next` chains. Circular chains are truncated with a diagnostic.
Unsupported actions remain visible as `PdfAnnotationActionType.Unsupported`.
Decoded scripts, files, form targets and multimedia actions are inspection
data only and are never executed.

## Appearance rendering

The selected `/AP/N` stream is interpreted by the same managed graphics engine
used for page content. An `/AS` name selects the matching normal-appearance
state; otherwise the first state is chosen deterministically. For canonical
checkbox and radio widgets, alpha 2 uses the field `/V` state before a stale
widget `/AS`, then falls back to `/Off`.

The appearance `/BBox` and optional `/Matrix` are mapped to the annotation
`/Rect`, clipped to both bounds and painted after the page content in `/Annots`
order. Local appearance resources, nested Form XObjects, paths, text, images,
patterns, shadings and transparency supported by the graphics engine therefore
remain available without a second renderer. Recursive Forms are detected and
bounded.

Invisible, Hidden, NoView and hidden optional-content annotations remain in
`Page.Annotations` but are not painted by the default screen renderer.
`PdfAnnotation.IsVisible` reports this default state. Explicit raster/SVG layer
overrides re-evaluate the annotation `/OC` entry for that render without
mutating its immutable metadata.

When no usable normal appearance exists, deterministic managed fallbacks cover
links, note icons, FreeText, text markup, squares, circles, lines, polygons,
polylines, ink, stamps, Caret, Popup, FileAttachment, Redact, Watermark and
common multimedia frames. FreeText fallback uses a built-in vector cell font
so output does not depend on installed system fonts. These fallbacks are
deliberately conservative and do not claim pixel identity with a producer's
missing appearance.

## Limits

`PdfReadOptions` adds:

| Property | Default |
| --- | ---: |
| `MaximumAnnotationsPerPage` | 100,000 |
| `MaximumAnnotationPoints` | 250,000 |
| `MaximumAnnotationAppearanceDepth` | 16 |
| `MaximumActions` | 10,000 |
| `MaximumActionDepth` | 32 |
| `MaximumActionScriptBytes` | 1 MiB |

Existing collection, decoded-stream, graphics-operation, display-list,
path-segment, tree and XObject limits continue to apply.

## Current limits

- AcroForm fields and deterministic widget fallbacks are implemented in alpha
  2, but mutation, saved regeneration and XFA remain outside the release.
- Rich text and default styles are exposed but only the plain `/Contents`
  value is used by managed text fallbacks.
- Sound, movie, screen and 3D metadata is recognized, but media playback and
  activation are outside the library.
- NoZoom and NoRotate flags are exposed but are not compensated in device
  space.
- Border effects, cloudy shapes, line-ending painting, FreeText callouts and
  rich default-appearance styling remain partial.
- Actions are inspection data only; the library does not provide an action
  dispatcher.
