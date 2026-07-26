# Upstream source record

The port was prepared from the user-supplied release archive:

| Field | Value |
| --- | --- |
| Filename | `poppler-26.07.0.tar.xz` |
| SHA-256 | `304832f48f8a47fdca90c6b6d1f684e68f37c10c9a0726f345f4ca9df4ca01e2` |
| Files | 894 |
| C/C++ header and implementation lines | 228,947 |
| Poppler core files (`poppler/`) | 224 |
| Stable C++ frontend files (`cpp/`) | 48 |
| Splash files (`splash/`) | 36 |
| Font parser files (`fofi/`) | 12 |
| Utility files (`utils/`) | 54 |

## Audit baseline

For each future porting slice:

1. Keep this archive hash as the semantic baseline.
2. Identify the upstream classes and tests covered by the slice.
3. Add managed unit tests plus differential fixtures against Poppler 26.07.0.
4. Confirm that the managed project contains no native interop or process
   fallback.
5. Update `COMPATIBILITY.md` only after the new behavior passes malformed-input
   and resource-limit tests.

The upstream archive itself is not duplicated inside this repository or its
ZIP; it is an input and is already independently available to the recipient.
