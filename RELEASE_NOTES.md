# Poppler.Net 0.13.0

Release date: 2026-08-21

`0.13.0` is the stable managed conversion release built on Poppler 26.07. It
adds fixed-layout HTML, autonomous page PDFs, versioned structured data and
safe image export to the stable 0.12 parser, inspection and rendering surface.
The approved RC implementation is promoted unchanged apart from stable version
metadata.

## Install

```xml
<PackageReference Include="Poppler.Net" Version="0.13.0" />
```

The matching command-line package is available as a .NET tool:

```bash
dotnet tool install --global Poppler.Net.Cli --version 0.13.0
```

The library targets .NET 8 and .NET 10. The CLI targets .NET 8 with
major-version roll-forward and reports the same package version.

## Conversion capabilities

- Render one page, a selected range or a complete document as deterministic
  fixed-layout HTML with selectable/searchable text, safe links, managed web
  fonts and SVG/raster fallbacks.
- Export one or more pages as bounded autonomous PDF files while preserving
  reachable content, resources, geometry and supported annotations/forms.
- Export deterministic schema `1.0` JSON, XML and XHTML for documents/pages,
  including geometry, text boxes, normalized/raw font names, links and image
  metadata.
- Reuse independently valid JPEG, JPEG 2000 and JBIG2 payloads and emit a
  managed PNG fallback when masks, filters or PDF color semantics require it.
- Use the same conversion families through the managed API, `Poppler.Net.Cli`
  and the browser-local WebAssembly playground.

## Frozen contract and hardening

- The callable API, option defaults, schema files, HTML/structured manifests
  and CLI help are frozen by deterministic regression fingerprints.
- Export page, object, depth, node, file, byte, font, image and working-memory
  budgets are checked before unbounded allocation or growth.
- Concurrent read-only conversions are isolated and deterministic; hostile
  resource names and damaged optional metadata have bounded diagnostics.
- The library and tool packages contain managed assemblies only and include
  embedded portable PDBs with exact-commit Source Link metadata.

## Qualification

The historical and 0.13 corpora cover rendering, text/font mapping,
annotations, forms, optional content, encryption, damaged input, HTML,
standalone pages, structured data and image export. Release gates rebuild from
the tracked source archive and exercise clean .NET 8/.NET 10 package consumers
plus the installed CLI on Ubuntu, Windows and macOS.

Poppler 26.07 remains the pinned source-level behavioral reference. Poppler
executables and native libraries are not runtime, package or CI dependencies.

## Declared limits

Semantic/reflow HTML, page merge, OCR, office reconstruction, general PDF
mutation, signing, output encryption, action execution and XFA are outside the
0.13 contract. Post-0.13 candidates are tracked separately in issue #40.
