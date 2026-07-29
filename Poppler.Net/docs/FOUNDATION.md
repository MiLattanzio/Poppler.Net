# 0.2 foundation gate

Version `0.2.0-alpha.1` hardens the initial parser before higher-level Poppler
subsystems are ported. It deliberately does not add encryption, a font engine
or page rendering.

## Required build gate

`build.sh` and `build.ps1` perform these steps in order:

1. restore the centrally pinned NuGet graph;
2. compile the complete solution with warnings as errors;
3. inspect production source for native interop and process fallbacks;
4. inspect every restored package asset and reject ELF, Mach-O, WebAssembly,
   native PE, mixed-mode PE and ReadyToRun binaries;
5. run the NUnit regression suite through the managed NUnitLite executable;
6. create the `Poppler.Net` NuGet package.

The library and CLI have no third-party runtime package references. Test-only
packages must be listed in `eng/managed-packages.json`, and every transitive
asset is inspected after restore. Adding a package to that manifest is a review
decision, not a way to bypass binary inspection.

## Parser and xref gate

The release adds regression coverage for:

- classic and stream-based xrefs;
- compressed objects;
- the newest revision in a `/Prev` chain;
- xref reconstruction after a broken `startxref`;
- compressed-object recovery during reconstruction;
- logical offsets relative to a displaced PDF header;
- mismatched indirect-object generations;
- input, decoded-stream and object-count limits;
- missing final `%%EOF` diagnostics.

## Remaining 0.2 work

The foundation phase is not complete enough for a stable release until a
versioned real-world corpus, differential checks against Poppler 26.07.0 and a
continuous fuzz target are added. Those gates precede the `0.3` encryption
slice.
