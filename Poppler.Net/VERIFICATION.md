# Verification record

Verification performed on 2026-07-29 for `0.9.0-alpha.3`, based on GitHub
`master` commit `79dbc41844689126a64bb8780e5f24b2e3287969`:

- .NET SDK 8.0.423 compiled all four solution projects in Release with
  warnings treated as errors.
- NUnitLite executed 183 tests: 183 passed, 0 failed, 0 warnings, 0 skipped.
- The managed-only verifier accepted production source and every asset in the
  complete restored NuGet graph, including the three managed runtime codecs.
- `Poppler.Net.0.9.0-alpha.3.nupkg` contains the Release net8.0 DLL/XML, README,
  license and notice. Its metadata names `Mi Lattanzio` as author and
  `https://github.com/MiLattanzio/Poppler.Net` as project and git repository.
- The package names only the pinned CoreJ2K, JBig2Decoder.NETStandard and
  StbImageSharp managed dependencies.
- The Linux, Windows and macOS CI workflow parsed as YAML, and `build.sh`
  passed shell syntax validation.
- The user corrections remain intact: revision 6 selects SHA-2 through
  `int va = selector % 3`, NUnit exception assertions retain explicit
  `Action` casts, reusable `stackalloc` buffers remain outside loops, and PDF
  fixtures are forced to LF by `.gitattributes`.

## Release-surface and concurrency gates

The release gates freeze and stress the public surface:

- A deterministic reflection fingerprint covers every public type, member and
  signature. The frozen SHA-256 is
  `f5c13f67ae973c0fa3c420d4176ee02102ef120bc761666a8686d6f1fb34d6e5`.
- `Document.PortVersion` matches the assembly/NuGet informational version.
- Twenty-four workers concurrently read pages, text, fonts, graphics and
  raster output from one `Document`.
- Thirty-two workers concurrently materialize one lazy embedded file.
- Sixteen workers concurrently resolve annotations, destinations and
  annotation raster output from one document.
- Sixteen workers concurrently enumerate AcroForm fields and widgets and
  reproduce the same page raster from one document.
- Twenty-four workers alternate default and overridden OCG/OCMD rendering from
  one document and reproduce the expected state-specific output.
- Caller-owned font, CMap-directory and optional-content override collections
  are snapshotted at operation boundaries.
- The Release smoke workload completed in 95.3 ms and allocated 14.9 MiB,
  within the explicit 30-second and 512-MiB regression budgets.

Object resolution, external-CMap parsing, diagnostics, document lifetime and
lazy attachment data are synchronized for concurrent read-only use. External
CMap and substitute-font discovery are ordered deterministically, with
case-insensitive path comparison on Windows.

The Optional Content model is lazily initialized once per document. Public
group/configuration collections are immutable; per-render evaluators retain
independent override snapshots and bound recursive membership expressions.

## Optional-content compatibility

The deterministic four-page alpha 3 fixture has SHA-256
`dc55042692e5fa1be5bc2810e54ea38e11fcb7eba25db17501403dd333bc7e03`.
It covers default View state, nested marked content, hidden text, Form and
Image XObjects, local Form properties, annotations, an AcroForm widget, all
four OCMD policies and recursive `/VE` expressions.

The generator reproduced both PDF and JSON manifest byte for byte. Managed
output at 72 DPI with 2x antialiasing was inspected at original resolution
against Poppler. Explicitly inverting group `7:0` off and group `8:0` on
changes exactly the intended content:

| Page | Purpose | Default PNG SHA-256 | Inverted PNG SHA-256 |
| --- | --- | --- | --- |
| 1 | Marked graphics and text | `ab0c74b1793fbe9ea06b48fd43cd56242a29c3fb48cff18d3de1736f3c44a2b2` | `e4d2fd3d2ce5f353671fd97a3c6c4142965b85cbdc6da2cd22c690b40d454a57` |
| 2 | Form and Image XObjects | `7b0a0ca09e5b2cee919aae248ec7d2211131e0add9a727b66bb9e69597f30a19` | `7d5a4600c32a222308ea1d52313518f31eef798eb1f675fd04255978b858051b` |
| 3 | Annotations and widget | `2b74fb3d00bcdadb67a159a93ce5e94dd6d8428f126f082011bd799328b8bb75` | `e327385c8339c78c92619483a4ef44f54312b6ecb80a616d704f35f56aa92982` |
| 4 | OCMD policies and `/VE` | `72012479807e591cbe913f265824bbe9b0998fdb9cc00f24a8965019c6b0fdd8` | `f6d223af7ce45f5ea8c42cf41cf32adbb0bc35eefcce63e95a7ece5dd0e3edc6` |

Default text extraction retains `OPTIONAL CONTENT DEFAULT` and
`VISIBLE RED TEXT` while omitting `HIDDEN BLUE TEXT`. Default and inverted SVG
outputs differ consistently with the PNG state. Blank or unknown override IDs,
group/depth/expression limits and documents without `/OCProperties` have
dedicated regressions.

## Historical rendering compatibility

The six beta 2 rendering pages, four annotation pages, four AcroForm pages and
three pages of `drylab.pdf` were rerendered. All 17 PNGs remain byte-identical
to the `0.9.0-alpha.2` baselines:

| Corpus | Page hashes |
| --- | --- |
| Beta 2 graphics | `a226180909d49b552a6fd0a77042207280bb3db642572d68e9bc31a2083b5974`, `b4f3b0f473227e5b1c127f4923c4adb22cdad059e59f925b973397247b08174e`, `a0e041c8cfd3e65ef63cf953f263dd16d8a3ff383879ac37d82cade719ef4f93`, `ebd45bacf97b320cd8f5dd836009d82e675d8d749b7e6b7ac045c1f0d9a9648d`, `e7aa40698ee3eee7b39254092452b9f80694c22016bbfc6b386d616d03135131`, `541537933cd41bec9ed2a182d112b440d3e390d8e902da23df09bcf08a390899` |
| Annotations | `2c4667d131f8f20237923096e4c95bf15c2da0094aa4592f4f78acc84199e225`, `08b36da2e4d15250d115f4737c0b599fd3649fa2079cc1afe8f20505482f0764`, `b35233721aa32456c9ae96371bf4a90606cbd533e4cf9b8c4ef69f5a71fff2f9`, `56fc260d4b4aab6535fb76ed350197b27a03e7be8c6b63b7d547d7e6fb154c73` |
| AcroForm | `b8459dc201c595b58ae81e1a53d3d6bf2dc24428b1289e00368cfe146a2380b8`, `3cfeda9506df006f2d7f857e7504989eb71afdc2b8dda6ed201c7985d862df2d`, `4f4c95dd0eaff7237945909a6dccf98b784f0da60379bf979e1bf2be05004d01`, `80bcdbb957481fc5409b56aaf67af6d93b61750ce25f65b020059afbee6c1017` |
| `drylab.pdf` | `2dd5d63520e0eff4629fe72ab8034403077d9316688de728b3b85d993661c061`, `f369bc9fcb56e85f31c924b12ed3d1ae1c362d081d788111e9100c44b791698b`, `a901f43cf1e48d39d0fa73d04e5057851d9df11e4c51114544b6f430880abb2a` |

The beta 1 font corpus, filtered-inline-image and page-box corpus, TrueType
format 0, CFF1/CFF2, Type 1, Type 3, Base-14 metrics, text/graphics
interleaving, transparency, image/color, encryption and damaged-xref
regressions remain part of the 183-test suite.

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

The remote workflow remains the authoritative platform and publishing gate;
no long-lived NuGet API key is stored in GitHub or the source archive.

## Distribution

The source archive contains 182 files, including 91 C# files and 28,289 lines
of production-library C#. It excludes `.git`, `bin`, `obj`, NuGet packages,
test results, QA renders, generated bytecode, executables and native artifacts.
The final ZIP was extracted into a fresh directory, matched all selected
source files byte for byte, restored from the five-package managed offline
feed, rebuilt all four projects without warnings, passed 183/183 tests,
passed the managed-only verifier, reproduced optional-content default and
inverted raster hashes plus historical beta 2 output, and produced a valid
`Poppler.Net.0.9.0-alpha.3.nupkg`.

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
