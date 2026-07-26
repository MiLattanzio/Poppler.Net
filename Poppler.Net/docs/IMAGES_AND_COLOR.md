# Managed images and color

Release `0.6.0-alpha.1` ports the first Image XObject and color-management
slice of Poppler 26.07.0. Decoding is performed in managed .NET code and never
loads Poppler or a native image library.

## Public API

`Page.Images` exposes each successfully decoded Image XObject as an immutable
`PdfImage`. `Data` contains top-to-bottom, tightly packed pixels. `BytesPerRow`
is exact and has no hidden alignment padding:

| `PdfPixelFormat` | Layout | `BytesPerRow` |
| --- | --- | ---: |
| `Gray8` | one gray byte | `Width` |
| `Rgb24` | red, green, blue | `Width * 3` |
| `Rgba32` | red, green, blue, straight alpha | `Width * 4` |

`ToPngBytes` and `SavePng` use the included managed PNG encoder. The CLI
command `images <input.pdf> <output-dir> [--page N]` exports all decoded page
images. `SvgPageRenderer` embeds the same PNG bytes in transformed SVG image
elements.

## Compression and samples

- unfiltered, Flate, LZW and RunLength image samples, after the existing
  predictor pipeline;
- DCT/JPEG through StbImageSharp 2.30.15;
- JPX/JPEG 2000 Part 1 through CoreJ2K 2.3.3.91;
- JBIG2 PDF streams and `JBIG2Globals` through
  JBig2Decoder.NETStandard 1.5.2;
- CCITT Modified Huffman, Group 3 and Group 4 through the internal
  `CcittFaxDecoder`;
- packed 1, 2, 4, 8 and 16-bit components and image `/Decode` arrays.

All package dependencies are C#/.NET assemblies. `eng/managed-packages.json`
records the approved versions and the build verifier rejects native,
mixed-mode, ReadyToRun, WebAssembly, executable and object-code assets in the
entire restored graph.

## Color

The managed pipeline supports DeviceGray, DeviceRGB, DeviceCMYK, CalGray,
CalRGB, Lab, Indexed, Separation and DeviceN. PDF function types 0 sampled,
2 exponential and 3 stitching drive tint transforms and gradients.

ICCBased supports common one- and three-component matrix/shaper profiles with
curve or parametric tone reproduction and an explicit `/Alternate` fallback.
Conversion targets sRGB using managed XYZ adaptation and transfer functions.

Image masks, explicit masks, color-key `/Mask` arrays and luminosity `/SMask`
streams become straight alpha. Different mask dimensions use deterministic
nearest-neighbor sampling.

## Explicit limitations

- ICC LUT/device-link profiles, proofing, black-point compensation, rendering
  intents, spot-color calibration and overprint simulation are not present.
- JPEG arithmetic coding, unusual JPEG color transforms and JPEG 2000 Part 2
  are not guaranteed by the selected managed codecs.
- Inline images (`BI`/`ID`/`EI`) are not decoded.
- Transparency groups, transfer functions and soft masks attached to graphics
  state remain part of the page rasterizer phase.
- The release extracts/embeds image pixels but does not rasterize an entire
  PDF page. Vector output remains SVG.

## Safety limits

`PdfReadOptions` bounds decoded image pixels, component count, ICC profile
bytes and sampled-function table size. Existing decoded-stream, XObject depth,
object recursion and collection limits also apply.
