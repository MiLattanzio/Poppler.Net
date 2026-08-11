# Poppler.Net 0.12.0

Release date: 2026-08-11

`0.12.0` is the stable release of the managed-only, read-only Poppler 26.07.0
port. It promotes the qualified RC 1 implementation without adding a new
feature family or changing the frozen callable `0.12` API.

## API freeze

- The version-normalized callable API SHA-256 is
  `ce87b22579e9458c3c1dcdb1aa01790f15d13006ad177ad4815f1e974e63b527`.
- The complete stable public-surface SHA-256 is
  `c72005f9c1bd3418c1a7fd00c582c8ccc88ba7e3beedd65e815a45861b72655b`.
- The only public-surface change from RC 1 is `Document.PortVersion`; no
  callable member was added, removed or changed.

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
  tag-version guard before the stable package can be published.

## Compatibility

There are no intentional source or binary breaking changes from beta.2 or RC
1. Install the stable package with:

```xml
<PackageReference Include="Poppler.Net" Version="0.12.0" />
```

The stable release retains beta.2 hostile-input limits, shared-document
determinism, immutable decoded-image reuse, dual-target packaging and the
beta.1 Poppler differential baseline.

## Scope limits

Advanced ICC LUT/device-link profiles, proofing, rendering intents, spot-color
overprint, native SVG mesh primitives and complex-script shaping remain
outside `0.12`. The project does not write, edit or sign PDFs.
