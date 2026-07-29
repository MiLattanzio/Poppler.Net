# Optional content and PDF layers in 0.9 alpha 3

Version `0.9.0-alpha.3` adds read-only Optional Content Group and Optional
Content Membership Dictionary handling based on Poppler's
`OptionalContent` responsibilities. The implementation evaluates visibility
for viewing and rendering; it does not edit configurations or save changed
layer state.

## Public model

`Document.OptionalContentGroups` is an immutable list in catalog `/OCGs`
order. Each `PdfOptionalContentGroup` exposes:

- a stable document-local `Id`;
- the human-readable `/Name`;
- declared `/Intent` values;
- visibility under the default configuration;
- `/Locked` membership;
- optional `/Usage /View /ViewState`.

Indirect groups use an `object:generation` identifier such as `17:0`. Direct
group dictionaries receive a deterministic `direct:N` identifier.
`Document.HasOptionalContent` reports whether at least one valid group exists.

`Document.DefaultOptionalContentConfiguration` exposes the catalog `/D`
configuration's name, creator, `/BaseState`, intents and `/RBGroups`. Radio
groups and locked state are metadata: callers may still explicitly override a
member for rendering.

```csharp
foreach (PdfOptionalContentGroup group in document.OptionalContentGroups)
{
    Console.WriteLine(
        $"{group.Id}: {group.Name}; " +
        $"visible={group.IsVisible}; locked={group.IsLocked}");
}
```

## Default visibility

The managed evaluator starts from `/BaseState`, applies explicit `/ON` and
`/OFF` arrays, then applies View-event `/AS` entries using each selected
group's `/Usage /View /ViewState`.

Visibility is applied to:

- marked content delimited by `BMC`/`BDC` and `EMC`;
- text extraction and the page graphics display list;
- Form and Image XObjects with `/OC`;
- local `/Properties` dictionaries in nested Forms;
- annotations, widgets and their explicit or generated appearances;
- managed raster and SVG output.

Nested hidden scopes remain hidden even when an inner group is enabled.
Annotation and widget metadata remains inspectable while its default
`IsVisible` state controls painting.

## Membership dictionaries

An `/OCMD` may combine one or more groups through the standard membership
policies:

| Policy | Visible when |
| --- | --- |
| `AnyOn` | at least one member is on |
| `AllOn` | every member is on |
| `AnyOff` | at least one member is off |
| `AllOff` | every member is off |

The default policy is `AnyOn`. When `/VE` is present, bounded `And`, `Or` and
single-operand `Not` expressions take precedence over `/P`. Recursive indirect
membership cycles are stopped conservatively instead of recursing without a
bound.

## Render overrides

`RasterRenderOptions.OptionalContentVisibility` and
`SvgRenderOptions.OptionalContentVisibility` accept group IDs and boolean
states. The dictionaries are snapshotted at render start and do not mutate the
document's default model.

```csharp
string layerId = document.OptionalContentGroups[0].Id;
var overrides = new Dictionary<string, bool>
{
    [layerId] = false
};

page.SavePng("layer-off.png", new RasterRenderOptions
{
    OptionalContentVisibility = overrides
});
```

Blank or unknown group identifiers throw an argument exception. Default
`Page.Text()`, `Page.TextList()`, `Page.Images` and `Page.Graphics` use the
document's default configuration; alpha 3 exposes overrides only at the
raster/SVG render boundary.

The CLI lists the effective default state and accepts repeatable overrides:

```bash
poppler-net layers input.pdf
poppler-net render input.pdf output.png --layer 17:0=off --layer 18:0=on
poppler-net svg input.pdf output.svg --layer 17:0=off
```

## Limits

`PdfReadOptions` adds:

| Property | Default |
| --- | ---: |
| `MaximumOptionalContentGroups` | 100,000 |
| `MaximumOptionalContentDepth` | 128 |
| `MaximumOptionalContentExpressionNodes` | 250,000 |

Existing collection, object-recursion, content-stream, graphics-operation,
display-list, XObject, annotation and form limits continue to apply.

## Current limits

- Only the default `/D` configuration and the View usage event are applied.
  Alternate `/Configs`, Print/Export events and automatic zoom, language or
  user usage categories are not modeled.
- `/Order`, ListMode and other layer-panel presentation metadata is not
  exposed.
- Locked and radio-button groups are reported but do not constrain explicit
  programmatic overrides.
- Layer state cannot be changed in the PDF or serialized.
- Intents are exposed as metadata; alpha 3 does not provide an intent-selecting
  render mode.
- Unknown or malformed optional-content objects are treated conservatively as
  visible so unsupported metadata does not silently erase page content.
