# AcroForm fields and widgets in 0.9 alpha 2

Version `0.9.0-alpha.2` adds a read-only managed AcroForm model based on
Poppler's `Form`, `FormField` and `FormWidget` responsibilities. It reads
existing field values and paints widgets, but it does not change a value,
execute field JavaScript or serialize a modified PDF.

Version `0.9.0-alpha.3` also applies widget `/OC` membership to both explicit
appearances and managed fallbacks. Hidden widgets remain available through the
immutable field/page models but are omitted from default rendering. See
[OPTIONAL_CONTENT.md](OPTIONAL_CONTENT.md).

## Public model

`Document.FormFields` is an immutable, lazily initialized flat list of
terminal fields in field-tree order. Container nodes contribute their partial
names to each terminal field's `FullyQualifiedName` but do not appear as
independent values.

Each `PdfFormField` exposes:

- Button, Text, Choice, Signature or Unknown type;
- checkbox, radio-button or push-button specialization;
- partial, fully qualified, alternate UI and mapping names;
- inherited `/Ff` flags;
- current `/V` and default `/DV` values;
- `/DA`, `/Q`, `/MaxLen` and choice `/TI`;
- choice export/display values and selected state;
- every associated immutable `PdfFormWidget`.

`HasValue` distinguishes an absent value from a value that cannot be
represented as text. In particular, `IsSigned` reports that a signature field
has a `/V` signature dictionary. It does not validate the signature or its
certificate.

```csharp
foreach (PdfFormField field in document.FormFields)
{
    Console.WriteLine(
        $"{field.FullyQualifiedName}: {field.Type} = {field.Value}");
    foreach (PdfFormWidget widget in field.Widgets)
    {
        Console.WriteLine(
            $"  page {widget.PageNumber}: {widget.Rectangle}, " +
            $"state={widget.AppearanceState}");
    }
}
```

`Page.FormWidgets` exposes only canonical field widgets mapped to that page.
An orphan `/Widget` annotation remains available through `Page.Annotations`
and can retain its explicit appearance, but it is not invented as an
AcroForm field.

## Field-tree semantics

The managed reader handles:

- composed field/widget dictionaries;
- logical fields with separate pure-widget `/Kids`;
- non-terminal name containers;
- inherited `/FT`, `/Ff`, `/V`, `/DV`, `/DA`, `/Q`, `/MaxLen`, `/Opt`,
  `/I` and `/TI`;
- fully qualified names built from the field hierarchy;
- cyclic or repeated field references with a diagnostic instead of
  unbounded recursion.

Choice `/I` indices determine `PdfFormFieldOption.IsSelected` when present,
matching common viewer behavior. The canonical `/V` bytes are still exposed
unchanged through `Values`. Export/display pairs in `/Opt` are kept separate.

## Widget appearance rendering

Widget annotations use the annotation pipeline introduced in alpha 1.
`/AP/N` streams are interpreted by the shared graphics engine, mapped from
their `/BBox` and `/Matrix` to the widget `/Rect`, clipped and painted in page
`/Annots` order.

For checkboxes and radio buttons, the canonical field `/V` selects the
matching normal-appearance state before a stale widget `/AS`. Non-selected
radio widgets use `/Off`. This prevents a valid form value and its visible
state from diverging.

When a widget has no usable appearance, deterministic managed fallbacks cover:

- single-line and multiline text;
- password masking;
- editable and non-editable choice values;
- checkbox checks and radio selection marks;
- push-button captions;
- signature-presence captions;
- `/MK` border/background colors, common border styles, `/DA` text color and
  font size, and `/Q` alignment.

Fallback text uses the built-in vector cell font, so output does not depend on
native APIs or installed fonts. Existing appearance streams remain
authoritative; the reader never writes a generated `/AP` back into the
document.

`Document.FormNeedsAppearances` exposes the AcroForm `/NeedAppearances` flag.
It is informational: missing appearances receive the managed fallback, while
existing appearances are preserved.

## Limits

`PdfReadOptions` adds:

| Property | Default |
| --- | ---: |
| `MaximumFormFields` | 100,000 |
| `MaximumFormWidgets` | 100,000 |
| `MaximumFormOptions` | 250,000 |
| `MaximumFormFieldDepth` | 128 |
| `MaximumFormDefaultAppearanceBytes` | 65,536 |

Annotation, decoded-stream, graphics-operation, display-list, path, object and
collection limits continue to apply to widget parsing and painting.

## Current limits

- Field values, selections and appearances cannot be edited or saved.
- XFA packets are only detected through `Document.FormType`; they are not
  interpreted or executed.
- Signature dictionaries are detected but cryptographic validation,
  certificate handling and signing are outside this release.
- Calculation, formatting, keystroke and validation actions are not executed.
- Rich text is exposed through flags, but fallback rendering uses the plain
  `/V` value.
- Default appearance parsing covers font size and DeviceGray/DeviceRGB/
  DeviceCMYK text colors. General appearance programs require an explicit
  `/AP` stream.
- Comb-cell geometry, list-box scrolling, captions/icons, border effects and
  widget rotation remain conservative in generated fallbacks.
- Orphan widgets are not attached to synthetic fields.
