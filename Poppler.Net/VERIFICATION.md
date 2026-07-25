# Verification record

Verification performed on 2026-07-26 for `0.2.0-alpha.1`:

- 34 C# files (5,030 lines at the time of the check) parsed with the current
  tree-sitter C# grammar: no syntax-error nodes.
- four solution projects resolved to existing project files.
- all six `.csproj`/property files parsed as XML; `global.json` and the managed
  package manifest parsed as JSON; the CI workflow parsed as YAML.
- local Markdown links resolved.
- production source contained no `DllImport`, `LibraryImport`,
  `NativeLibrary`, unmanaged exports or external-process fallback.
- production projects contained no `PackageReference`; the only direct
  package is test-only `xunit.v3` 3.2.2 and is centrally pinned.
- the source tree contained no ELF, Mach-O, WebAssembly, native-library or
  object-file asset.
- the shell build entry point passed `bash -n`.
- classic, leading-prefix and incremental-update fixture shapes were accepted
  by the locally available Poppler command-line tools.

The creation environment does not contain `dotnet`, `csc` or MSBuild.
Consequently, NuGet restore, C# compilation, the 18-test xUnit executable and
the post-restore package-binary verifier could not be executed here. The
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
