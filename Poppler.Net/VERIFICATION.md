# Verification record

Verification performed on 2026-07-26 for `0.4.0-alpha.1`:

- 48 C# files (8,326 lines at the time of the check) parsed with the current
  tree-sitter C# grammar: no syntax-error nodes.
- the user corrections were preserved: revision 6 selects SHA-2 through
  `int va = selector % 3`, and every NUnit exception assertion explicitly
  casts its lambda to `Action`.
- four solution projects resolved to existing project files.
- all six `.csproj`/property files parsed as XML; all four JSON manifests and
  the CI workflow parsed successfully.
- all local Markdown links resolved.
- production source contained no `DllImport`, `LibraryImport`,
  `NativeLibrary`, unmanaged exports or external-process fallback.
- production projects contained no `PackageReference`; the direct test-only
  packages remain centrally pinned NUnit 4.6.1 and NUnitLite 4.6.1.
- the source tree contained no ELF, Mach-O, WebAssembly, native-library,
  executable or object-file asset.
- the shell build entry point passed `bash -n`.
- all nine encrypted fixture hashes matched their manifest and the independent
  verifier recovered expected R2–R6 metadata, text and attachment bytes.
- the two deterministic font-fixture hashes matched
  `font-fixtures.json`.
- Poppler 26.05.0 identified the font fixtures as embedded subset CID
  TrueType and CID Type 0C OpenType resources without `ToUnicode`, and
  extracted `ABC` from both.
- pypdf 6.10.0 independently extracted `ABC` from both font fixtures.
- regenerating both font fixtures twice produced identical byte hashes.
- 57 NUnit cases are defined: the previous 43 foundation/security cases plus
  14 font, CMap, metric, direction, layout, limit and embedded-font cases.

The creation environment does not contain `dotnet`, `csc` or MSBuild.
Consequently, NuGet restore, C# compilation, the NUnitLite executable and the
post-restore package-binary verifier could not be executed here. The
three-platform CI definition and `build.sh`/`build.ps1` make all four checks
mandatory on a machine with .NET SDK 8.0.423.

This ZIP is therefore a source release candidate, not a verified binary
release. Run the following before publishing its NuGet package:

```bash
./build.sh Release
```

The command restores, compiles with warnings as errors, inspects the entire
restored NuGet graph for native or mixed-mode code, runs the NUnitLite suite
and packs the library. This limitation is recorded explicitly rather than
presenting syntax-only checks as a successful build.
