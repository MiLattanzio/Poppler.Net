# Verification record

Verification performed on 2026-07-26 for `0.3.0-alpha.1`:

- 38 C# files (6,394 lines at the time of the check) parsed with the current
  tree-sitter C# grammar: no syntax-error nodes.
- four solution projects resolved to existing project files.
- all six `.csproj`/property files parsed as XML; `global.json`, the managed
  package manifest and encrypted-fixture manifest parsed as JSON; the CI
  workflow parsed as YAML.
- all local Markdown links resolved.
- production source contained no `DllImport`, `LibraryImport`,
  `NativeLibrary`, unmanaged exports or external-process fallback.
- production projects contained no `PackageReference`; the only direct
  package is test-only `xunit.v3` 3.2.2 and is centrally pinned.
- the source tree contained no ELF, Mach-O, WebAssembly, native-library,
  executable or object-file asset.
- the shell build entry point passed `bash -n`.
- all nine encrypted fixture hashes matched their manifest.
- pypdf 6.10.0 independently recovered the expected metadata, text and
  attachment bytes for R2–R6 with both user and owner passwords.
- the R4 variant checks independently validated split string/stream filters,
  an explicit `/Crypt` filter, an independent `EFF` and plaintext metadata
  under `EncryptMetadata false`.
- the locally available Poppler 26.05.0 tools opened the five standard R2–R6
  fixtures with both passwords and recovered their page, text and embedded
  file.
- 43 xUnit cases are defined: the previous 18 foundation cases plus 25
  security cases covering authentication, permissions, R2–R6 primitives,
  metadata, attachments, crypt-filter routing and tampered `/Perms`.

The creation environment does not contain `dotnet`, `csc` or MSBuild.
Consequently, NuGet restore, C# compilation, the xUnit executable and the
post-restore package-binary verifier could not be executed here. The
three-platform CI definition and `build.sh`/`build.ps1` make all four checks
mandatory on a machine with .NET SDK 8.0.423.

This ZIP is therefore a source release candidate, not a verified binary
release. Run the following before publishing its NuGet package:

```bash
./build.sh Release
```

The command restores, compiles with warnings as errors, inspects the entire
restored NuGet graph for native or mixed-mode code, runs the xUnit suite and
packs the library. This limitation is recorded explicitly rather than
presenting syntax-only checks as a successful build.
