# Poppler.Net 0.12.0-rc.1

Release date: 2026-08-11

`0.12.0-rc.1` is the publication-ready release candidate for the managed-only,
read-only Poppler 26.07.0 port. The callable `0.12` API is frozen; this release
qualifies the beta.2 implementation and distribution without adding a new
feature family.

## API freeze

- The version-normalized callable API SHA-256 is
  `ce87b22579e9458c3c1dcdb1aa01790f15d13006ad177ad4815f1e974e63b527`.
- The complete rc.1 public-surface SHA-256 is
  `dae14d92c94ed709bf9012ebe977e24779aa317b2be5a1cdca317f5bbcc83711`.
- Promotion to `0.12.0` may change only `Document.PortVersion` and version
  metadata unless a documented release blocker requires an explicitly
  approved API re-baseline.

See `docs/API_FREEZE.md` for the fingerprint scope and change policy.

## Release qualification

- The complete historical and `0.12` corpus, managed-only verifier and
  bounded performance/concurrency gates remain active.
- The package contains library assets for both `net8.0` and `net10.0`; tools,
  tests, engineering utilities and the WebAssembly playground use .NET 10.
- NuGet content, GPL license metadata, repository revision and the pinned
  managed dependency graph are inspected before publication.
- The tracked source archive is restored, rebuilt, tested, repacked and
  verified from a clean extraction.
- Clean consumers restore the produced package and render PNG/SVG output as
  `net8.0` and `net10.0` on Ubuntu, Windows and macOS.
- CI exercises both the accepting and rejecting paths of the release
  tag-version guard before an RC package can be published.

## Compatibility with beta.2

There are no intentional source or binary breaking changes from
`0.12.0-beta.2`. Update the package reference to:

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0-rc.1" />
```

The candidate retains beta.2 hostile-input limits, shared-document
determinism, immutable decoded-image reuse, dual-target packaging and the
beta.1 Poppler differential baseline.

## Prerelease status and limits

This is a prerelease. Stable `0.12.0` is published only after the RC gates and
the checklist in `docs/RELEASE_CHECKLIST.md` remain green.

Advanced ICC LUT/device-link profiles, proofing, rendering intents, spot-color
overprint, native SVG mesh primitives and complex-script shaping remain
outside `0.12`. The project does not write, edit or sign PDFs.
