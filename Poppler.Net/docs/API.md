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
the byte length is the retained encoded payload. This is inspection metadata,
not a font-rasterization API.

Each `TextBox` also reports `WritingMode` and `IsRightToLeft`.

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
