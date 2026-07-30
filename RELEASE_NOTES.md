# Poppler.Net 0.9.0-rc.1

Release date: 2026-07-30

`0.9.0-rc.1` is the first release candidate for the managed-only Poppler
26.07.0 port. The `0.9` feature set is now frozen: this release focuses on
compatibility, deterministic behavior and distribution quality rather than new
public functionality.

## Highlights

- Freezes the callable public API carried by `0.9.0-beta.2`; the only public
  constant change is the expected `Document.PortVersion` value.
- Adds a version-normalized API fingerprint so promotion from RC to stable
  cannot silently add, remove or alter a public type, member or default value.
- Makes CLI document-information ordering explicitly ordinal and therefore
  independent of the current operating-system culture.
- Adds release gates for owned input bytes, byte-for-byte save copies after
  caller-buffer mutation, culture-independent PDF/SVG/PNG output,
  per-operation layer-override snapshots and independent diagnostic snapshots.
- Aligns library, CLI, assembly and NuGet versions at `0.9.0-rc.1`.

## What the 0.9 line adds over 0.8

- Immutable annotations, destinations and inspection-only PDF actions,
  including advanced annotation subtypes, reply/popup relationships and lazy
  file attachments.
- Read-only AcroForm field/widget inspection with inherited values, appearance
  selection and deterministic managed fallbacks.
- Optional Content Group and OCMD evaluation, visibility expressions and
  per-render layer overrides for raster and SVG output.
- Conservative page-tree and content-stream recovery with stable diagnostics,
  strict-mode switches and bounded decoded-stream caching.
- More faithful raster line caps, joins, miter fallback and continuous/odd dash
  patterns.

## Compatibility and upgrading

There are no intentional source or binary breaking changes from
`0.9.0-beta.2`. Update the package reference:

```xml
<PackageReference Include="Poppler.Net" Version="0.9.0-rc.1" />
```

Applications should continue to treat documents, pages, annotations, fields
and optional-content models as read-only. Annotation actions and JavaScript are
inspection data only and are never executed.

## Known limitations

- Editing, incremental writing, form mutation, signature validation and
  JavaScript/action execution are not implemented.
- Complex-script shaping, full bidirectional layout, complete color proofing,
  ICC LUT/device-link profiles and every malformed-PDF recovery heuristic
  remain outside this release.
- SVG is a vector preview backend and does not paint mesh shadings.
- Font substitution is file-based and can vary with installed fonts unless
  explicit font directories are supplied.

See `docs/COMPATIBILITY.md`, `docs/ANNOTATIONS.md`, `docs/FORMS.md`,
`docs/OPTIONAL_CONTENT.md` and `docs/ROBUSTNESS.md` for the detailed support
matrix.

## Verification

- Release build of all four projects with warnings treated as errors.
- 205 NUnit tests, including historical raster and corpus-integrity
  regressions.
- Managed-only production and dependency graph verification.
- Deterministic fixture regeneration and visual inspection of representative
  PDF pages.
- NuGet metadata, dependency and payload inspection.
- Offline restore, rebuild, test, package and rendering checks from the
  extracted source ZIP.

Base revision: `7c47d57e14a8b1642aabf6ea8beb75edb99ae02f`.
