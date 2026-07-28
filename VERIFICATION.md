# Verification record

Verification performed on 2026-07-28 for `0.8.0` from GitHub `master` commit
`eabcd3f1ba32950406acbb1c4b8278b33693fbdd`:

- .NET SDK 8.0.423 compiled all four solution projects in Release with
  warnings treated as errors.
- NUnitLite executed 148 tests: 148 passed, 0 failed, 0 warnings, 0 skipped.
- The managed-only verifier accepted production source and every asset in the
  complete restored NuGet graph, including the three managed runtime codecs.
- `Poppler.Net.0.8.0.nupkg` contains the Release net8.0 DLL/XML, README,
  license and notice. Its NuGet metadata names `Mi Lattanzio` as author and
  `https://github.com/MiLattanzio/Poppler.Net` as project and git repository.
- The package names only the pinned CoreJ2K, JBig2Decoder.NETStandard and
  StbImageSharp managed dependencies.
- The user corrections remain intact: revision 6 selects SHA-2 through
  `int va = selector % 3`, NUnit exception assertions retain explicit
  `Action` casts, and reusable `stackalloc` buffers remain outside loops.

## Stable-release gates

Six tests freeze and stress the public release surface:

- A deterministic reflection fingerprint covers every public type, member and
  signature. The frozen SHA-256 is
  `085ed34a3fe24c3b698f3556ae868a76d4997d595b3fcd2ff031e552fdf7fc5b`.
- `Document.PortVersion` must match the assembly/NuGet informational version.
- Twenty-four workers concurrently read pages, text, fonts, graphics and
  raster output from one `Document`.
- Thirty-two workers concurrently materialize one lazy embedded file.
- Caller-owned font and CMap directory collections are snapshotted at operation
  boundaries.
- The Release smoke workload completed in 153.9 ms and allocated 14.9 MiB,
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
regressions remain part of the 148-test suite.

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

The source archive contains 160 files, including 81 C# files and 24,378 lines
of production-library C#. It excludes `bin`, `obj`, NuGet packages, test
results, QA renders, generated bytecode, executables and native artifacts.
The final ZIP was extracted into a fresh directory, matched all selected
source files byte for byte, restored from the five-package managed offline
feed, rebuilt all four projects without warnings, passed 148/148 tests,
passed the managed-only verifier, reproduced beta page 1 byte for byte and
produced a valid `Poppler.Net.0.8.0.nupkg`.

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
