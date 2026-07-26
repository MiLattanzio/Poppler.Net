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
    MaximumCMapMappings = 100_000,
    MaximumGraphicsElements = 100_000,
    MaximumPathSegments = 250_000,
    MaximumImagePixels = 25_000_000,
    MaximumImageComponents = 8,
    MaximumIccProfileBytes = 4 * 1024 * 1024,
    MaximumFunctionSamples = 250_000,
    MaximumXObjectDepth = 16,
    MaximumTransparencyGroupDepth = 16,
    MaximumRenderPixels = 25_000_000,
    MaximumPages = 2_000,
    AttemptXrefRepair = false
};
```

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
`PdfShadingElement` exposes an axial or radial gradient. Paint is represented
by `PdfSolidBrush`, `PdfTilingPatternBrush` or `PdfGradientBrush`; each element
also retains the active clipping paths and source Form resource.
`PdfTransparencyGroupElement` retains isolated/knockout flags and a nested
display list. `PdfGraphicsState.SoftMask` exposes Alpha/Luminosity group masks.

The display list is immutable from the caller's perspective and is evaluated
lazily once per `Page`.

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
    FontDirectories = new[] { "application-fonts" }
});

ReadOnlyMemory<byte> rgba = bitmap.Data;
page.SavePng("page.png", new RasterRenderOptions { Dpi = 144 });
```

`PdfBitmap` contains immutable, tightly packed, top-to-bottom straight-alpha
RGBA rows. `Page.RenderToPng` returns encoded PNG bytes. The raster backend
paints the graphics display list with clipping, images, gradients, patterns,
PDF blend modes and graphics-state soft masks. Text is painted at its exact
display-list position from embedded TrueType, CFF1/Type 2 or Type 1 outlines,
Type 3 CharProcs, or optional managed font-file substitution. See
[RENDERING.md](RENDERING.md) for the current text, stroke, group and color
limits.
