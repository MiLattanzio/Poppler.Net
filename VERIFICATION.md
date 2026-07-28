# Verification record

Verification performed on 2026-07-28 for `0.9.0-alpha.1`, based on GitHub
`master` commit `a9ea7fc1e9c5f1309b50aa9df0aef873850fac47`:

- .NET SDK 8.0.423 compiled all four solution projects in Release with
  warnings treated as errors.
- NUnitLite executed 158 tests: 158 passed, 0 failed, 0 warnings, 0 skipped.
- The managed-only verifier accepted production source and every asset in the
  complete restored NuGet graph, including the three managed runtime codecs.
- `Poppler.Net.0.9.0-alpha.1.nupkg` contains the Release net8.0 DLL/XML, README,
  license and notice. Its NuGet metadata names `Mi Lattanzio` as author and
  `https://github.com/MiLattanzio/Poppler.Net` as project and git repository.
- The package names only the pinned CoreJ2K, JBig2Decoder.NETStandard and
  StbImageSharp managed dependencies.
- The user corrections remain intact: revision 6 selects SHA-2 through
  `int va = selector % 3`, NUnit exception assertions retain explicit
  `Action` casts, and reusable `stackalloc` buffers remain outside loops.

## Release-surface and concurrency gates

Six tests freeze and stress the public release surface:

- A deterministic reflection fingerprint covers every public type, member and
  signature. The frozen SHA-256 is
  `a4bd8b15a968793f80398fc495c93f00e5a911fb7233500a6f1d9a5e70130048`.
- `Document.PortVersion` must match the assembly/NuGet informational version.
- Twenty-four workers concurrently read pages, text, fonts, graphics and
  raster output from one `Document`.
- Thirty-two workers concurrently materialize one lazy embedded file.
- Sixteen workers concurrently resolve annotations, destinations and
  annotation raster output from one document.
- Caller-owned font and CMap directory collections are snapshotted at operation
  boundaries.
- The Release smoke workload completed in 119.5 ms and allocated 14.9 MiB,
  within the explicit 30-second and 512-MiB regression budgets.

Object resolution, external-CMap parsing, diagnostics, document lifetime and
lazy attachment data are synchronized for concurrent read-only use. External
CMap and substitute-font discovery are ordered deterministically, with
case-insensitive path comparison on Windows.

## Rendering compatibility

The deterministic six-page beta 2 fixture has SHA-256
`15597777ca00e97f67638c5a82b5c42df4bcca3e2b2297f95e4c4b79540b9433`.
It covers Gouraud and patch meshes, uncolored tiling patterns, a calculator
transfer function, transparency groups and process overprint.

The final managed render at 72 DPI with 2x antialiasing was inspected at
original resolution. Every PNG remains byte-identical to the verified beta 2
output:

| Page | Purpose | PNG SHA-256 |
| --- | --- | --- |
| 1 | Gouraud meshes | `a226180909d49b552a6fd0a77042207280bb3db642572d68e9bc31a2083b5974` |
| 2 | Patch meshes | `b4f3b0f473227e5b1c127f4923c4adb22cdad059e59f925b973397247b08174e` |
| 3 | Uncolored pattern reuse | `a0e041c8cfd3e65ef63cf953f263dd16d8a3ff383879ac37d82cade719ef4f93` |
| 4 | Calculator transfer and knockout | `ebd45bacf97b320cd8f5dd836009d82e675d8d749b7e6b7ac045c1f0d9a9648d` |
| 5 | Isolated/non-isolated groups | `e7aa40698ee3eee7b39254092452b9f80694c22016bbfc6b386d616d03135131` |
| 6 | Process overprint preview | `541537933cd41bec9ed2a182d112b440d3e390d8e902da23df09bcf08a390899` |

The new four-page annotation fixture has SHA-256
`2ab74289a74d1a3616773dd065b889db1fca316586d6e668f0d3e39db5eeada4`.
It covers URI/direct/named links, hidden annotations, deterministic fallbacks,
interior colors, normal appearance streams, `/AS`, rotated appearance
matrices, nested Forms, recursive-Form rejection and circular named-destination
aliases. Managed output at 72 DPI with 2x antialiasing was inspected at
original resolution against Poppler 26.05.0:

| Page | Purpose | Managed PNG SHA-256 |
| --- | --- | --- |
| 1 | Links, destinations, visibility and normal appearance | `2c4667d131f8f20237923096e4c95bf15c2da0094aa4592f4f78acc84199e225` |
| 2 | FreeText, markup and shape fallbacks | `08b36da2e4d15250d115f4737c0b599fd3649fa2079cc1afe8f20505482f0764` |
| 3 | Matrix/state/nested/recursive appearances | `b35233721aa32456c9ae96371bf4a90606cbd533e4cf9b8c4ef69f5a71fff2f9` |
| 4 | Named destination target | `56fc260d4b4aab6535fb76ed350197b27a03e7be8c6b63b7d547d7e6fb154c73` |

Explicit appearances match Poppler geometrically, including the rotated BBox
mapping and nested Form. Differences on page 2 are deliberate managed
fallbacks: the malformed FreeText without `/DA` is rendered as deterministic
vector text instead of Poppler's black rectangle, while note/shape defaults
remain conservative rather than producer-specific.

The three pages of the Prince `drylab.pdf` sample, SHA-256
`2c1a1a89a63bbaa842306f6bfb57f5712de7e48710b317ca5776585a2a7dd995`,
were rerendered at 96 DPI with 2x antialiasing and inspected together. Title
and body text,
ligatures, Polish characters, colors and images remain complete. These PNGs
also remain byte-identical to the beta 2 baseline:

| Page | PNG SHA-256 |
| --- | --- |
| 1 | `2dd5d63520e0eff4629fe72ab8034403077d9316688de728b3b85d993661c061` |
| 2 | `f369bc9fcb56e85f31c924b12ed3d1ae1c362d081d788111e9100c44b791698b` |
| 3 | `a901f43cf1e48d39d0fa73d04e5057851d9df11e4c51114544b6f430880abb2a` |

The beta 1 font corpus, alpha 3 filtered-inline-image and page-box corpus,
TrueType format 0, CFF1/CFF2, Type 1, Type 3, Base-14 metrics, text/graphics
interleaving, transparency, image/color, encryption and damaged-xref
regressions remain part of the 158-test suite.

## CI and NuGet publishing

`.github/workflows/ci.yml` defines:

- Release builds, tests, managed-only verification and packaging on Ubuntu,
  Windows and macOS for pushes and pull requests;
- an uploaded `.nupkg` artifact after the platform matrix succeeds;
- NuGet.org publication only for a published GitHub Release, using an OIDC
  token restricted to the protected `nuget.org` environment and exchanged by
  `NuGet/login` for a short-lived publishing credential;
- `--skip-duplicate`, read-only repository permissions and concurrency
  cancellation for superseded non-release runs.

The workflow was parsed as YAML and the shell entry point passed `bash -n`.
The remote workflow remains the authoritative platform and publishing gate;
no long-lived NuGet API key is stored in GitHub or the source archive.

## Distribution

The source archive contains 168 files, including 85 C# files and 26,082 lines
of production-library C#. It excludes `bin`, `obj`, NuGet packages, test
results, QA renders, generated bytecode, executables and native artifacts.
The final ZIP was extracted into a fresh directory, matched all selected
source files byte for byte, restored from the five-package managed offline
feed, rebuilt all four projects without warnings, passed 158/158 tests,
passed the managed-only verifier, reproduced annotation page 1 and beta page 1
byte for byte and produced a valid `Poppler.Net.0.9.0-alpha.1.nupkg`.

The environment's `dotnet` CLI intermittently cannot inspect its process
namespace. Running MSBuild single-node with server/node reuse disabled and the
CLI in the foreground avoids `System.Diagnostics.Process.GetStat`; this is an
execution-environment issue, not a project or package error. The standard user
entry point remains:

```bash
./build.sh Release
```

It restores, compiles with warnings as errors, inspects the complete NuGet
graph, runs NUnitLite and packs the library.
