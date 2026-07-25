# Poppler.Net 26.07

`Poppler.Net` is an **in-progress, source-level managed C# port** of Poppler
26.07.0. It contains no C++/CLI, P/Invoke, native shared library, external
process invocation, or native NuGet dependency.

> This `0.2.0-alpha.1` foundation release is not a complete replacement for
> libpoppler.
> It implements the PDF object/xref layer, document and page discovery,
> common stream filters, metadata, embedded files, basic text extraction and
> an SVG diagnostic renderer. See
> [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) before adopting it.

## Build

Requirements: .NET SDK 8.0.423. `global.json` pins the selected feature band.

```bash
dotnet build Poppler.Net.sln
dotnet run --project tests/Poppler.Net.Tests
```

`./build.sh` performs restore, Release build, executable regression tests and
NuGet packaging. It also rejects native or mixed-mode binaries anywhere in the
restored NuGet graph. The production library itself has no package dependency;
xUnit v3 is the first approved managed-only test dependency. See
`VERIFICATION.md` for the checks completed in the creation environment.

## CLI

```bash
dotnet run --project src/Poppler.Net.Cli -- info input.pdf
dotnet run --project src/Poppler.Net.Cli -- text input.pdf --page 1
dotnet run --project src/Poppler.Net.Cli -- attachments input.pdf output-dir
dotnet run --project src/Poppler.Net.Cli -- svg input.pdf page.svg --page 1
```

## API

```csharp
using Poppler;

using var document = Document.LoadFromFile("input.pdf");
Console.WriteLine($"{document.Pages} pages, PDF {document.PdfVersion}");

Page page = document.CreatePage(0);
Console.WriteLine(page.Text());
foreach (TextBox word in page.TextList())
    Console.WriteLine($"{word.Text} at {word.BoundingBox}");
```

All page indices in the API are zero-based. CLI page numbers are one-based.

## Design

- `Core/` is the managed counterpart of Poppler's `Object`, `Lexer`, `Parser`,
  `XRef`, `Stream` and filter layers.
- `DocumentModel/` corresponds to `PDFDoc`, `Catalog`, `Page`, `Outline`,
  `FileSpec` and page labels.
- `Text/` corresponds to the first managed slice of `TextOutputDev`, font
  encodings and `ToUnicode` CMaps.
- `Rendering/` is a diagnostic SVG backend. It is not yet the counterpart of
  Splash/Cairo.

The implementation uses bounded allocations, recursion limits and decoded
stream limits because PDFs are untrusted input. Defaults can be changed with
`PdfReadOptions`.

The `0.2` foundation additionally supports header-relative offsets after a
leading transport prefix, validates object generations and compressed-object
indices, and reconstructs xref/object streams during damaged-xref recovery.
See [docs/FOUNDATION.md](docs/FOUNDATION.md).

## License and provenance

Poppler is GPL, not LGPL. This port is distributed under
GPL-2.0-or-later and retains Poppler provenance. See `LICENSE`,
`NOTICE.md`, and `docs/PORTING.md`. Applications distributing a derivative of
this code must comply with the GPL.
