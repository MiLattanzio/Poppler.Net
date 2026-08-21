# Upgrading from 0.12.0 to 0.13.0

The 0.13 line is additive for existing 0.12 consumers. It keeps the managed-only
runtime boundary, `net8.0`/`net10.0` library targets, assembly/file version
`26.7.0.0`, existing loader/render/text APIs and their defaults. RC.1 freezes
the expanded surface described in [API_FREEZE.md](API_FREEZE.md).

## Package references

During RC validation, opt in explicitly:

```xml
<PackageReference Include="Poppler.Net" Version="0.13.0-rc.1" />
```

The CLI is now also distributed as a .NET tool:

```bash
dotnet tool install --global Poppler.Net.Cli --version 0.13.0-rc.1
```

Stable promotion changes only these versions to `0.13.0`; it must not change
the frozen callable/default/schema/manifest/CLI contracts.

## Additive 0.13 capabilities

- Fixed-layout page, range and document HTML, including self-contained output
  and directory bundles.
- Autonomous single/range PDF page extraction with bounded graph rewriting.
- Versioned JSON/XML/XHTML document and page data plus deterministic bundles.
- Safe reusable image payloads with managed PNG fallback and explicit metadata.
- Matching commands in `Poppler.Net.Cli` and browser-local downloads in the
  WebAssembly playground.

Existing code does not need to adopt these APIs. API page indexes remain
zero-based; CLI page numbers remain one-based.

## Defaults and compatibility

Do not clone option objects by assuming only the 0.12 members. Construct them
normally or use `with` expressions so the bounded 0.13 defaults remain active.
RC.1 freezes the exact defaults for `PdfReadOptions`, `RasterRenderOptions`,
`SvgRenderOptions`, `HtmlRenderOptions`, `HtmlExportOptions`,
`StructuredExportOptions` and `PdfPageExtractionOptions`.

Structured exports use schema version `1.0`. HTML and structured bundle
manifests retain their documented version/discriminator fields. Font metadata
exposes both normalized names and raw PDF names; use the normalized name for
display and the raw name only when source identity matters.

See [COMPATIBILITY.md](COMPATIBILITY.md) and
[CONVERSION_COMPATIBILITY.md](CONVERSION_COMPATIBILITY.md) for supported
behavior and accepted Poppler differences. Semantic/reflow HTML, page merge,
OCR, office reconstruction and general PDF mutation are not implied by the
upgrade and remain outside 0.13.

## Validation recommendation

Before production promotion, exercise representative PDFs with the same
framework used by the application, verify any relied-on HTML/structured
fields, and keep configured limits at or below the defaults appropriate for
the workload. Consumers that expose untrusted browser uploads should retain
the playground's tighter input, page, node, file and artifact budgets.
