# Poppler.Net 0.9.0

Release date: 2026-07-31

`0.9.0` is the stable release of the managed-only Poppler 26.07.0 port's
`0.9` line. It promotes `0.9.0-rc.1` without changing the callable public API,
parsing behavior or verified raster output.

## Changes since 0.9.0-rc.1

- Finalizes library, CLI, assembly and NuGet versions at `0.9.0`.
- Preserves the callable API fingerprint frozen in RC 1; only the expected
  public `Document.PortVersion` value changes.
- Adds a stable-version regression that rejects prerelease labels.
- Finalizes stable release notes and compatibility documentation.
- Introduces no new production feature or rendering change.

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
- Culture-independent output, owned input bytes, operation-scoped option
  snapshots and independent diagnostic snapshots.

## Compatibility and upgrading

There are no intentional source or binary breaking changes from
`0.9.0-beta.2` or `0.9.0-rc.1`. Update the package reference:

```xml
<PackageReference Include="Poppler.Net" Version="0.9.0" />
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
- 206 NUnit tests, including the frozen callable API and all historical raster
  and corpus-integrity regressions.
- Managed-only production and dependency graph verification.
- Deterministic fixture regeneration and visual inspection of representative
  PDF pages.
- NuGet metadata, dependency and payload inspection.
- Offline restore, rebuild, test, package and rendering checks from the
  extracted source ZIP.

Base revision: `036e5912ab17693e0d47632532f0b6c86917ff4e`.
