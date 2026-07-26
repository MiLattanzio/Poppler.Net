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
- Standard Security Handler password and object decryption ported to managed
  C# and .NET cryptography APIs;
- font character maps, CID metrics, embedded sfnt inspection and directional
  text layout ported to managed C#;
- path construction, graphics state, clipping, Form/Image XObjects, tiling
  patterns and axial/radial shadings ported to a managed display list;
- Image XObject decoding, masks, common PDF image codecs and calibrated/special
  color conversion added in managed code;
- a managed RGBA page raster, straight-alpha PDF blend compositor,
  transparency groups, graphics-state soft masks and embedded TrueType outline
  reader added without a native rendering dependency.

Managed runtime dependencies introduced by release 0.6:

- CoreJ2K 2.3.3.91, BSD-3-Clause, including the upstream JJ2000 notice;
- JBig2Decoder.NETStandard 1.5.2, MIT;
- StbImageSharp 2.30.15, public domain.

These dependencies are fetched from NuGet and are not copied into the source
ZIP. Their package and upstream license notices apply when restored.

The complete upstream GPL notices remain authoritative. See `LICENSE`.
