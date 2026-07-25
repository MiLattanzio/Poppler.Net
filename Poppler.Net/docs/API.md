# API quick reference

## Loading

```csharp
Document.LoadFromFile(path, options: options);
Document.LoadFromData(bytes, options: options);
Document.LoadFromStream(stream, options: options);
```

All loaders copy the input into owned managed memory. Configure upper bounds:

```csharp
var options = new PdfReadOptions
{
    MaximumInputBytes = 64 * 1024 * 1024,
    MaximumDecodedStreamBytes = 32 * 1024 * 1024,
    MaximumPages = 2_000,
    AttemptXrefRepair = false
};
```

## Document inspection

`Document` exposes PDF version, page count, encryption/linearization state,
document information, XMP, trailer IDs, viewer mode/layout, form type,
JavaScript presence, permissions, diagnostics and embedded files.

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

## Diagnostic SVG

`Page.RenderToSvg` and `Page.SaveSvg` create a managed text-position preview.
The output is useful for debugging extraction coordinates; it is deliberately
not advertised as a faithful page renderer.
