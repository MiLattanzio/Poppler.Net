# Verification record

Verification performed on 2026-07-26:

- 30 C# files parsed with the current tree-sitter C# grammar: no syntax-error
  nodes.
- all `.csproj` and `Directory.Build.props` files parsed as XML.
- solution scan found no `DllImport`, `LibraryImport`, `NativeLibrary`,
  `Process.Start`, native package reference or external executable fallback in
  production code.
- generated classic-xref fixtures were checked structurally.
- the xref-stream fixture containing a compressed font object was accepted by
  Poppler `pdfinfo`, and Poppler `pdftotext` returned
  `Compressed font object`.
- shell build entry point passed `bash -n`.

The creation environment did not contain a `dotnet` SDK, so `dotnet build` and
the managed executable test suite could not be run here. Run `./build.sh` on a
machine with .NET 8 SDK or later before treating the alpha as a binary release.
This limitation is intentionally recorded rather than presenting a syntax-only
check as a successful compilation.
