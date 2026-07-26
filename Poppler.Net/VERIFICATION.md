# Verification record

Verification performed on 2026-07-26 for `0.6.0-alpha.1`:

- .NET SDK 8.0.423 restored and compiled all four solution projects in Release
  with warnings treated as errors.
- NUnitLite executed 83 tests: 83 passed, 0 failed, 0 warnings, 0 skipped.
- the managed-only verifier accepted production source and every asset in the
  complete restored NuGet graph, including the three runtime codecs.
- the user corrections remain intact: revision 6 selects SHA-2 through
  `int va = selector % 3`, and every NUnit exception assertion explicitly
  casts its lambda to `Action`.
- the two CA2014 sites previously identified allocate their reusable
  `stackalloc` spans before their loops; no `stackalloc` occurs inside a loop
  body.
- the deterministic image/color fixture contains 12 decoded Image XObjects:
  raw RGB, Indexed, Separation, DeviceN sampled tint, Lab, ICCBased,
  DCT/JPEG, JPX/JPEG 2000, CCITT Group 3 and Group 4, JBIG2 and an RGB image
  with soft mask.
- fixture assertions verify exact raw/indexed/tint pixels, calibrated/ICC
  conversion ranges, JPEG/JPX output, CCITT rows, JBIG2 dimensions/content,
  straight-alpha soft masks, PNG structure and SVG embedding.
- the same fixture exercises Separation and Lab colors on graphics paths so
  image and vector paint use the same color-space implementation.
- `pdfinfo` parsed the fixture as a one-page PDF 1.7 document measuring
  600×800 points, and `pdfimages -list` identified all expected image
  encodings and color spaces.
- regenerating the image/color fixture updates a SHA-256 manifest and produces
  deterministic PDF bytes for the installed generator versions.
- all nine encrypted fixture hashes, both embedded-font fixture hashes and the
  graphics fixture hash continue to match their manifests.
- project/XML/JSON/YAML structure, local Markdown links, shell syntax and
  forbidden-interoperability source scans pass.

The environment's normal `dotnet` CLI startup cannot reliably inspect its
process namespace. Verification therefore invoked the SDK's managed MSBuild,
NUnitLite and verifier assemblies through the .NET 8 host. This executes the
same compiler, projects and managed test assemblies without changing build
inputs. The standard user entry point remains:

```bash
./build.sh Release
```

It restores, compiles with warnings as errors, inspects the complete NuGet
graph, runs NUnitLite and packs the library.
