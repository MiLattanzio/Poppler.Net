# Verification record

Verification performed on 2026-07-30 for `0.9.0-beta.2`, based on GitHub
`master` commit `9a07c2319439cf599be71b4a3cec8eab55236da6`:

- .NET SDK 8.0.423 compiled all four solution projects in Release with
  warnings treated as errors.
- NUnitLite executed 200 tests: 200 passed, 0 failed, 0 warnings, 0 skipped.
- The managed-only verifier accepted production source and every asset in the
  restored NuGet graph, including the three managed runtime codecs.
- `Poppler.Net.0.9.0-beta.2.nupkg` contains the Release net8.0 DLL/XML, README,
  license and notice. Its metadata names `Mi Lattanzio` as author and
  `https://github.com/MiLattanzio/Poppler.Net` as project and git repository.
- The package names only the pinned CoreJ2K, JBig2Decoder.NETStandard and
  StbImageSharp managed dependencies.
- The Linux, Windows and macOS CI workflow parsed as YAML, and `build.sh`
  passed shell syntax validation.
- Generated ASCII-heavy PDF fixtures are fixed to LF by `.gitattributes`,
  including the new `robustness-beta2.pdf` corpus.

## Release-surface, concurrency and performance gates

- A deterministic reflection fingerprint covers every public type, member and
  constant value. The frozen SHA-256 is
  `db65f8de415117a759a689e184bf41955224b67cf466154b31c40caa4166db45`.
- `Document.PortVersion`, assembly informational version, CLI and NuGet
  package version all report `0.9.0-beta.2`.
- Twenty-four workers concurrently read pages, text, fonts, graphics and
  raster output from one `Document`.
- Thirty-two workers concurrently materialize one lazy embedded file.
- Existing annotation, AcroForm and optional-content concurrency gates remain
  active.
- Twenty-four workers concurrently inspect advanced action chains and the
  damaged beta 2 corpus. Repair diagnostics remain deduplicated.
- Caller-owned font, CMap-directory and optional-content override collections
  are snapshotted at operation boundaries.
- The Release smoke workload completed in 104.1 ms and allocated 14.9 MiB,
  within the explicit 30-second and 512-MiB regression budgets.
- Twelve repeated reads of a 256-KiB decoded content stream allocated 78.1 KiB
  with the cache and 7,768.3 KiB with caching disabled.

## Robustness and stroke compatibility

The deterministic five-page beta 2 fixture has SHA-256
`451bcc89375c187328708e0485fddb6ae5a465bc0b35ec37469b90a033bfc0e2`.
Its generator reproduces both PDF and manifest byte for byte.

The corpus covers:

- one missing page-tree child and one circular `/Pages` branch;
- a stale root `/Count` while five valid pages remain recoverable;
- a `/Contents` array containing valid streams, an invalid Flate stream and a
  non-stream entry;
- a stream whose declared `/Length` is shorter than its actual bytes;
- butt, round and projecting-square caps;
- miter, round and bevel joins plus miter-limit fallback;
- dash phase continuity and PDF repetition of odd-length dash arrays.

Managed output at 72 DPI with 2x antialiasing was inspected at original
resolution and frozen as:

| Page | Purpose | PNG SHA-256 |
| --- | --- | --- |
| 1 | Line caps and dotted round strokes | `0ef1ba1189d2ee5556594b9e74b2ac087a0ef663de5d7d1e3c18aab12c233ad1` |
| 2 | Continuous and odd dash patterns | `b9c85729a4a0cd3f242765221a8f2a8f036a2fb2bfa1012d570f05970125818d` |
| 3 | Partially damaged content array | `68ea4e8840c19625bbce95a1e7f31b2b072dcb875206af7f85b6186041cae22f` |
| 4 | Recovered stream length | `a9a5984fa72cf5733efc2186eed9ca840571de33482a6b0df49739686e5fdeaf` |
| 5 | Miter, round and bevel joins | `87a9a6bd772e671de8584659db2d8a86d91e8587dc501a2302f47ca873660e49` |

Poppler 26.05.0 opens the damaged fixture while reporting the expected missing
child, malformed contents and page-tree loop. Structural recovery, strict
repair switches, safety limits and concurrent reads are asserted separately.

## Historical compatibility

The 192 tests inherited from `0.9.0-beta.1` remain green. They cover:

- advanced annotations/actions, AcroForm and optional-content models;
- eight default/inverted optional-content raster states;
- annotation appearance mapping and managed fallbacks;
- six mesh/pattern/transparency/overprint pages;
- external CMaps, CFF1/CFF2, Type 1, Type 3, Base-14 metrics, targeted GSUB,
  text/graphics ordering and filtered inline images;
- encryption revisions 2-6, image/color codecs and damaged-xref recovery.

Raster hashes that contain managed fallback strokes were intentionally updated
for the corrected cap/join scanner. The four-page AcroForm and three-page
advanced-annotation generators reproduce those updated manifests exactly.

The three pages of `drylab.pdf` were rerendered and visually inspected at
96 DPI. The managed hashes are:

- page 1: `2dd5d63520e0eff4629fe72ab8034403077d9316688de728b3b85d993661c061`;
- page 2: `f369bc9fcb56e85f31c924b12ed3d1ae1c362d081d788111e9100c44b791698b`;
- page 3: `a901f43cf1e48d39d0fa73d04e5057851d9df11e4c51114544b6f430880abb2a`.

## CI and NuGet publishing

`.github/workflows/ci.yml` defines Release build, test, managed-only and
package jobs on Ubuntu, Windows and macOS. A published GitHub Release triggers
NuGet.org publication through OIDC Trusted Publishing in the protected
`nuget.org` environment. Ordinary pushes, pull requests and manual workflow
runs never publish.

## Distribution

The source archive contains 192 files, including 94 C# files across the four
projects and 29,274 lines of production-library C#. It excludes `.git`, `bin`,
`obj`, NuGet packages, test results, QA renders, generated bytecode,
executables and native artifacts.

The final ZIP is extracted into a fresh directory and compared byte for byte
with the selected source tree. From that copy the release is restored from the
five-package managed offline feed, rebuilt without warnings, tested, verified
as managed-only, repackaged and rerendered.

The environment's `dotnet` CLI intermittently cannot inspect its process
namespace. Running MSBuild single-node with node reuse disabled and the CLI in
the foreground avoids `System.Diagnostics.Process.GetStat`; this is an
execution-environment issue, not a project or package error. The standard user
entry point remains:

```bash
./build.sh Release
```

It restores, compiles with warnings as errors, inspects the complete NuGet
graph, runs NUnitLite and packs the library.
