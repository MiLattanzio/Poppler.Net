# Poppler.Net 26.07

`Poppler.Net` is an **in-progress, source-level managed C# port** of Poppler
26.07.0. It contains no C++/CLI, P/Invoke, native shared library, external
process invocation, or native NuGet dependency.

> This `0.7.0-alpha.1` raster release is not a complete replacement for
> libpoppler.
> It implements the PDF object/xref layer, document and page discovery,
> common stream filters, metadata, embedded files, structured font/text
> extraction, a backend-neutral vector display list and an SVG vector
> preview and a managed RGBA page rasterizer. It can open Standard Security Handler
> revisions 2–6 with user or owner passwords and decode common simple,
> composite, CID and vertical text paths. The graphics slice interprets paths,
> clipping, Form/Image XObjects, tiling patterns and axial/radial shadings.
> The image slice decodes common raw and compressed Image XObjects, converts
> calibrated and special color spaces to sRGB, exposes pixels, writes PNG and
> embeds decoded images in SVG. The raster slice paints vector paths, images,
> patterns, gradients and embedded TrueType glyph outlines with antialiasing,
> PDF blend modes, transparency groups and Alpha/Luminosity soft masks. See
> [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) before adopting it.

## Build

Requirements: .NET SDK 8.0.423. `global.json` pins the selected feature band.

```bash
dotnet build Poppler.Net.sln
dotnet run --project tests/Poppler.Net.Tests -- --noresult
```

`./build.sh` performs restore, Release build, NUnitLite regression tests and
NuGet packaging. It also rejects native or mixed-mode binaries anywhere in the
restored NuGet graph. The production library uses three audited managed
runtime packages for JPEG, JPEG 2000 and JBIG2. NUnit and its in-process
NUnitLite runner are approved test-only managed dependencies. See
`VERIFICATION.md` for the checks completed in the creation environment.

RC4, MD5, SHA-2 and AES are reached only through managed C# or the .NET
cryptography API. The solution does not ship OpenSSL, a platform crypto library
or any native cryptography asset.

## CLI

```bash
dotnet run --project src/Poppler.Net.Cli -- info input.pdf
dotnet run --project src/Poppler.Net.Cli -- text input.pdf --page 1
dotnet run --project src/Poppler.Net.Cli -- fonts input.pdf
dotnet run --project src/Poppler.Net.Cli -- graphics input.pdf --page 1
dotnet run --project src/Poppler.Net.Cli -- images input.pdf output-images
dotnet run --project src/Poppler.Net.Cli -- render input.pdf page.png --page 1 --dpi 144
dotnet run --project src/Poppler.Net.Cli -- attachments input.pdf output-dir
dotnet run --project src/Poppler.Net.Cli -- svg input.pdf page.svg --page 1
```

Encrypted input accepts `--user-password VALUE` or `--owner-password VALUE`.
Command-line values may be visible to other local processes; applications
should normally pass secrets through the `Document` API instead.

## API

```csharp
using Poppler;
using Poppler.Rendering;

using var document = Document.LoadFromFile("input.pdf");
Console.WriteLine($"{document.Pages} pages, PDF {document.PdfVersion}");

Page page = document.CreatePage(0);
Console.WriteLine(page.Text());
foreach (TextBox word in page.TextList())
    Console.WriteLine($"{word.Text} at {word.BoundingBox}");
foreach (FontInfo font in page.Fonts)
    Console.WriteLine($"{font.Name}: {font.Type}, {font.EmbeddedFormat}");
foreach (PdfGraphicsElement element in page.Graphics)
    Console.WriteLine($"{element.GetType().Name}: {element.State.Transform}");
foreach (PdfImage image in page.Images)
{
    Console.WriteLine($"{image.ResourceName}: {image.Width}x{image.Height}, stride {image.BytesPerRow}");
    image.SavePng($"{image.ResourceName}.png");
}
page.SavePng("page.png", new RasterRenderOptions
{
    Dpi = 144,
    Antialiasing = 4
});
```

All page indices in the API are zero-based. CLI page numbers are one-based.

Encrypted files can be opened directly:

```csharp
using var document = Document.LoadFromFile(
    "protected.pdf",
    userPassword: "secret");

Console.WriteLine(document.EncryptionInfo?.Revision);
Console.WriteLine(document.Permissions);
```

If no correct password is supplied, the returned document remains locked.
`Unlock(ownerPassword, userPassword)` retries from the original bytes and,
matching Poppler's C++ API, returns the document's new locking status
(`false` means unlocked).

## Design

- `Core/` is the managed counterpart of Poppler's `Object`, `Lexer`, `Parser`,
  `XRef`, `Stream` and filter layers.
- `DocumentModel/` corresponds to `PDFDoc`, `Catalog`, `Page`, `Outline`,
  `FileSpec` and page labels.
- `Text/` corresponds to the first managed slice of `TextOutputDev`, font
  encodings, `GfxFont`, CID metrics and `ToUnicode`/encoding CMaps.
- `Graphics/` is the first managed slice of `Gfx`, `GfxState`, `Function`,
  patterns and Form XObjects and produces a backend-neutral display list.
- `Color/` ports calibrated, ICC matrix/shaper and special color conversion.
- `Images/` decodes Image XObjects, masks and common PDF image codecs into
  tightly packed managed pixel buffers.
- `Rendering/` consumes that display list in managed SVG and RGBA/PNG
  backends; its raster core maps the initial Splash path, image, antialiasing,
  blend and transparency-group responsibilities.

The implementation uses bounded allocations, recursion limits and decoded
stream limits because PDFs are untrusted input. Defaults can be changed with
`PdfReadOptions`.

The `0.2` foundation additionally supports header-relative offsets after a
leading transport prefix, validates object generations and compressed-object
indices, and reconstructs xref/object streams during damaged-xref recovery.
See [docs/FOUNDATION.md](docs/FOUNDATION.md).

The `0.3` slice ports `SecurityHandler`/`Decrypt`: legacy password padding,
RC4 object keys, AES-128/256, revision 6 hardened hashing, crypt filters,
metadata exclusion and PDF permission flags. See
[docs/SECURITY.md](docs/SECURITY.md).

The `0.4` slice separates code-to-CID and code-to-Unicode maps, reads
horizontal and vertical CID metrics, recognizes embedded Type 1/CFF/TrueType/
OpenType programs and adds an sfnt `cmap` fallback, Type 3 metrics, vertical
advancement and improved reading order. See
[docs/FONTS_AND_TEXT.md](docs/FONTS_AND_TEXT.md).

The `0.5` slice interprets vector paths and painting state, clipping, device
colors, common `ExtGState` entries, Form/Image XObjects, colored tiling
patterns and type 2/3 shadings. See
[docs/GRAPHICS.md](docs/GRAPHICS.md).

The `0.6` slice decodes raw/predictor, JPEG, JPEG 2000, JBIG2 and CCITT images,
applies image masks and soft masks, evaluates sampled/exponential/stitching
functions, converts CalGray/CalRGB/Lab/ICCBased/Indexed/Separation/DeviceN,
and adds `Page.Images`, managed PNG export and SVG image embedding. See
[docs/IMAGES_AND_COLOR.md](docs/IMAGES_AND_COLOR.md).

The `0.7` slice adds managed page rasterization, supersampled coverage,
straight-alpha PDF compositing, common separable and nonseparable blend modes,
Form transparency groups, Alpha/Luminosity soft masks, page rotation and
embedded TrueType outline painting. See
[docs/RENDERING.md](docs/RENDERING.md).

## License and provenance

Poppler is GPL, not LGPL. This port is distributed under
GPL-2.0-or-later and retains Poppler provenance. See `LICENSE`,
`NOTICE.md`, and `docs/PORTING.md`. Applications distributing a derivative of
this code must comply with the GPL.
