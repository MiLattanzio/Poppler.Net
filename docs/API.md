# API quick reference

## Loading

```csharp
Document.LoadFromFile(path, options: options);
Document.LoadFromData(bytes, options: options);
Document.LoadFromStream(stream, options: options);
```

All three loaders accept `ownerPassword` and `userPassword`. A wrong or missing
password returns a locked document so callers can inspect `IsEncrypted`,
`IsLocked` and `EncryptionInfo`, then retry:

```csharp
using Document document = Document.LoadFromData(bytes);
if (document.IsLocked && document.Unlock("", promptedUserPassword))
    throw new InvalidOperationException("The password was not accepted.");
```

`Unlock` follows Poppler's C++ API and returns the new locked state:
`false` means success. Protected properties and page creation throw
`PdfEncryptedException` while the document remains locked.

All loaders copy the input into owned managed memory. Configure upper bounds:

```csharp
var options = new PdfReadOptions
{
    MaximumInputBytes = 64 * 1024 * 1024,
    MaximumDecodedStreamBytes = 32 * 1024 * 1024,
    MaximumCachedDecodedBytes = 16 * 1024 * 1024,
    MaximumContentStreamsPerPage = 2_000,
    MaximumContentOperands = 100_000,
    MaximumCMapMappings = 100_000,
    MaximumExternalCMapBytes = 4 * 1024 * 1024,
    MaximumCMapUseDepth = 8,
    CMapDirectories = new[] { "application-cmaps" },
    UseSystemCMaps = false,
    MaximumGraphicsElements = 100_000,
    MaximumPathSegments = 250_000,
    MaximumImagePixels = 25_000_000,
    MaximumImageComponents = 8,
    MaximumIccProfileBytes = 4 * 1024 * 1024,
    MaximumFunctionSamples = 250_000,
    MaximumXObjectDepth = 16,
    MaximumTransparencyGroupDepth = 16,
    MaximumMeshTriangles = 16_384,
    MaximumAnnotationsPerPage = 10_000,
    MaximumAnnotationPoints = 50_000,
    MaximumAnnotationAppearanceDepth = 8,
    MaximumActions = 1_000,
    MaximumActionDepth = 16,
    MaximumActionScriptBytes = 256 * 1024,
    MaximumFormFields = 10_000,
    MaximumFormWidgets = 10_000,
    MaximumFormOptions = 25_000,
    MaximumFormFieldDepth = 64,
    MaximumFormDefaultAppearanceBytes = 16_384,
    MaximumOptionalContentGroups = 10_000,
    MaximumOptionalContentDepth = 64,
    MaximumOptionalContentExpressionNodes = 50_000,
    MaximumRenderPixels = 25_000_000,
    MaximumPages = 2_000,
    AttemptXrefRepair = false,
    AttemptPageTreeRepair = false,
    AttemptContentStreamRepair = false
};
```

Explicit CMap directories are searched before common system `poppler-data`
locations. `UseSystemCMaps = false` disables the latter. External CMaps are
data files only: the managed parser reads their declarative mapping and
`usecmap` inheritance without executing PostScript.

## Document inspection

`Document` exposes PDF version, page count, encryption/linearization state,
document information, XMP, trailer IDs, viewer mode/layout, form type,
JavaScript presence, permissions, diagnostics and embedded files.

For encrypted documents, `EncryptionInfo` reports security-handler version and
revision, key size, `StrF`, `StmF`, `EFF` algorithms and `EncryptMetadata`.
`PasswordKind` distinguishes user and owner authentication. Owner access
returns all permissions; user access maps the PDF `/P` bits.

`Save` and `SaveACopy` currently preserve the original bytes. They do not
serialize mutations.

## Pages and text

```csharp
Page page = document.CreatePage(0);
PdfRectangle crop = page.PageRect();
string text = page.Text(layout: TextLayout.Physical);
IReadOnlyList<TextBox> runs = page.TextList();
IReadOnlyList<PdfRectangle> hits = page.Search("needle");
```

The managed API uses zero-based indices. A page may also be selected by its PDF
page label with `CreatePage(string)`.

`TextLayout.RawOrder` follows content-stream order, `Physical` groups runs by
their geometric baselines, and `NonRawNonPhysical` additionally applies the
conservative column reading-order pass.

## Fonts

```csharp
foreach (FontInfo font in page.Fonts)
{
    Console.WriteLine(
        $"{font.ResourceName}: {font.Name}, {font.Type}, " +
        $"{font.Encoding}, embedded={font.IsEmbedded}");
}
```

`FontInfo` reports Type 1, CFF, TrueType, OpenType, CID and Type 3 resources,
horizontal/vertical mode, subset state, embedded format/program byte length,
collection and `ToUnicode` availability. For an unsupported font-stream filter
the byte length is the retained encoded payload.

Each `TextBox` also reports `WritingMode` and `IsRightToLeft`.

## Annotations, links and destinations

```csharp
foreach (PdfAnnotation annotation in page.Annotations)
{
    Console.WriteLine(
        $"{annotation.Id} {annotation.Type} {annotation.Rectangle} " +
        $"appearance={annotation.HasAppearance}");
    if (!string.IsNullOrEmpty(annotation.ParentId))
        Console.WriteLine($"reply/popup parent: {annotation.ParentId}");
    if (annotation.Attachment is { } attachment)
        Console.WriteLine($"{attachment.Name}: {attachment.Size} bytes");
    if (annotation.Action.Uri is { } uri)
        Console.WriteLine(uri);
    if (annotation.Action.Destination is { } destination)
        Console.WriteLine($"page {destination.PageNumber}: {destination.Type}");
}

foreach ((string name, PdfDestination destination) in document.NamedDestinations)
    Console.WriteLine($"{name} -> page {destination.PageNumber}");
```

`Page.Annotations` preserves `/Annots` order and exposes immutable metadata,
geometry, border/color state, flags, review/popup relationships, attachment
data and resolved actions. URI, remote, script, form, layer and multimedia
actions, including `/Next` chains, are inspection data only and are never
executed. Direct destinations,
catalog `/Dests` and `/Names/Dests` name trees resolve to zero-based page
indices. See [ANNOTATIONS.md](ANNOTATIONS.md).

## Outlines and bookmarks

```csharp
foreach (PdfOutlineItem item in document.OutlineItems)
{
    Console.WriteLine(
        $"{item.Title}: page {item.Destination?.PageNumber}, " +
        $"open={item.IsOpen}, action={item.Action.Type}");
}
```

`Document.OutlineItems` preserves the linked sibling order and exposes
immutable children, title, RGB color, bold/italic flags, open state, resolved
destination and inspection-only action. Direct and named targets reuse the
same `PdfDestination` resolver used by annotations. `/First`, `/Last`,
`/Next`, `/Prev` and `/Parent` links are bounded and checked for repeated or
circular nodes. See [OUTLINES.md](OUTLINES.md).

## AcroForm fields and widgets

```csharp
foreach (PdfFormField field in document.FormFields)
{
    Console.WriteLine(
        $"{field.FullyQualifiedName}: {field.Type} = {field.Value}");
    foreach (PdfFormWidget widget in field.Widgets)
        Console.WriteLine($"page {widget.PageNumber}: {widget.Rectangle}");
}

foreach (PdfFormWidget widget in page.FormWidgets)
    Console.WriteLine($"{widget.FieldName}: {widget.AppearanceState}");
```

The field model is read-only and follows inherited AcroForm field values,
flags, choice options, names and widget relationships. `FormNeedsAppearances`
reports the catalog flag. Existing widget `/AP` streams use the common
annotation renderer; missing streams receive deterministic managed
text/button/choice/signature fallbacks. Signature presence is reported but is
not cryptographically validated. See [FORMS.md](FORMS.md).

## Optional content and layers

```csharp
foreach (PdfOptionalContentGroup group in document.OptionalContentGroups)
{
    Console.WriteLine(
        $"{group.Id}: {group.Name}, visible={group.IsVisible}, " +
        $"locked={group.IsLocked}");
}

PdfOptionalContentConfiguration? configuration =
    document.DefaultOptionalContentConfiguration;
```

`Document.HasOptionalContent` reports whether the catalog declares usable
Optional Content Groups. `DefaultOptionalContentConfiguration` exposes the
default configuration's name, creator, base state, intents and radio-button
groups. Group `Id` values are stable within the loaded document and are the
keys accepted by render options.

Default `Page.Text()`, `Page.TextList()`, `Page.Graphics`, annotations and
widgets follow the default configuration. Raster and SVG calls can override
individual groups without mutating the document:

```csharp
var visibility = new Dictionary<string, bool>
{
    [document.OptionalContentGroups[0].Id] = false
};

page.SavePng("without-layer.png", new RasterRenderOptions
{
    OptionalContentVisibility = visibility
});
page.SaveSvg("without-layer.svg", new SvgRenderOptions
{
    OptionalContentVisibility = visibility
});
```

Override dictionaries are snapshotted when rendering begins. Unknown or blank
group identifiers are rejected. See
[OPTIONAL_CONTENT.md](OPTIONAL_CONTENT.md).

## Graphics display list

```csharp
IReadOnlyList<PdfGraphicsElement> graphics = page.Graphics;
foreach (PdfGraphicsElement element in graphics)
{
    Console.WriteLine($"{element.GetType().Name}: {element.State.Transform}");
}
```

`PdfPathElement` exposes path segments, fill rule and paint mode.
`PdfTextElement` exposes decoded text, font resource/name/size, glyph count and
the selected `PdfTextRenderingMode`; source codes and glyph matrices remain
internal so subset-font selection cannot be corrupted by a Unicode round trip.
`PdfImageElement` exposes Image XObject metadata and its optional decoded
`PdfImage`.
`PdfShadingElement` exposes an axial or radial gradient.
`PdfMeshShadingElement` exposes free-form/lattice Gouraud or Coons/tensor
patch data through a bounded `PdfMeshShadingBrush` triangle list. Paint is
represented by `PdfSolidBrush`, `PdfTilingPatternBrush`, `PdfGradientBrush` or
`PdfMeshShadingBrush`; each element also retains the active clipping paths and
source Form resource. Uncolored tiling brushes expose their per-use
`UnderlyingColor`.
`PdfTransparencyGroupElement` retains isolated/knockout flags and a nested
display list. `PdfGraphicsState.SoftMask` exposes Alpha/Luminosity group masks;
the graphics state also reports fill/stroke overprint and overprint mode.

The display list is immutable from the caller's perspective and is evaluated
lazily once per `Page`. Page content comes first; visible annotation
appearances or managed fallbacks follow in `/Annots` order. Their
`SourceResource` starts with `Annotation[N]/Subtype`.

## Concurrency and lifetime

Independent read operations may use the same unlocked `Document` and its
`Page` instances concurrently. Object, CMap, diagnostic, page and attachment
caches synchronize initialization and return immutable or read-only results.
`PdfReadOptions.CMapDirectories` is copied when the document is loaded.
`RasterRenderOptions.FontDirectories` and raster/SVG optional-content
visibility maps are copied when a render begins, so later caller mutations
cannot change an operation already in progress.

Decoded indirect streams are shared safely between repeated page, text and
graphics reads until `MaximumCachedDecodedBytes` is reached. The budget is per
document; zero disables this cache without changing decoded-stream safety
limits. See [ROBUSTNESS.md](ROBUSTNESS.md).

Do not call `Unlock` or `Dispose` concurrently with another operation.
`Dispose` is idempotent, but the caller remains responsible for ending all
readers before disposing their owner document.

## Decoded images

```csharp
foreach (PdfImage image in page.Images)
{
    Console.WriteLine(
        $"{image.ResourceName}: {image.Width}x{image.Height}, " +
        $"{image.Format}, stride={image.BytesPerRow}");
    ReadOnlyMemory<byte> pixels = image.Data;
    image.SavePng(Path.Combine(outputDirectory, image.ResourceName + ".png"));
}
```

`Gray8`, `Rgb24` and straight-alpha `Rgba32` rows are top-to-bottom and
tightly packed. `BytesPerRow` is always exact. `Compression` reports the PDF
image source family; `ColorSpace` reports the source color-space description.

## Attachments

```csharp
foreach (EmbeddedFile file in document.EmbeddedFiles)
{
    Console.WriteLine($"{file.Name}: {file.MimeType}, {file.Size} bytes");
    file.SaveTo(Path.Combine(outputDirectory, Path.GetFileName(file.Name)));
}
```

Callers are responsible for sanitizing untrusted attachment names when choosing
paths. The bundled CLI does this automatically.

## Managed SVG vector preview

`Page.RenderToSvg` and `Page.SaveSvg` render the managed graphics display list
plus extracted text. `SvgRenderOptions` can independently disable vector
graphics, decoded images or text, draw extraction bounds and draw Image
XObject unit-square bounds.

The SVG backend covers paths, clipping, Form content, colored tiling patterns,
axial/radial gradients and decoded Image XObjects embedded as managed PNG.
It remains a preview backend rather than the visual-conformance target.

## Managed page raster

```csharp
using Poppler.Rendering;

PdfBitmap bitmap = page.Render(new RasterRenderOptions
{
    Dpi = 144,
    PageBox = PageBox.CropBox,
    Antialiasing = 4,
    Transparent = false,
    IncludeText = true,
    UseFontSubstitution = true,
    FontDirectories = new[] { "application-fonts" },
    OptionalContentVisibility = new Dictionary<string, bool>
    {
        ["17:0"] = false
    }
});

ReadOnlyMemory<byte> rgba = bitmap.Data;
page.SavePng("page.png", new RasterRenderOptions { Dpi = 144 });
```

`PdfBitmap` contains immutable, tightly packed, top-to-bottom straight-alpha
RGBA rows. `Page.RenderToPng` returns encoded PNG bytes. The raster backend
paints the graphics display list with clipping, images, gradients, patterns,
PDF blend modes and graphics-state soft masks. Text is painted at its exact
display-list position from embedded TrueType, CFF1/CFF2 Type 2 or Type 1 outlines,
Type 3 CharProcs, or optional managed font-file substitution. See
[RENDERING.md](RENDERING.md) for the current text, stroke, group and color
limits.
