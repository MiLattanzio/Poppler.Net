# Provenance notice

This work is a C# port derived from the Poppler 26.07.0 source distribution,
which in turn is based on Xpdf. Copyright remains with the individual Poppler
and Xpdf contributors identified in the upstream source files.

The original source archive used for this port was `poppler-26.07.0.tar.xz`.
The port changes the implementation language and public surface, and is not
endorsed by the Poppler project.

Changed for the managed port in July 2026:

- C++ ownership and pointer semantics replaced with immutable C# values.
- native dependencies replaced with .NET Base Class Library implementations;
- defensive resource limits made explicit through `PdfReadOptions`;
- stable C++ API concepts mapped to idiomatic managed types;
- rendering scope reduced to an SVG diagnostic backend in the alpha release.

The complete upstream GPL notices remain authoritative. See `LICENSE`.
