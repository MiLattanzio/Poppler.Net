# Conversion compatibility qualification

`0.13.0-beta.1` closes compatibility gaps across the managed HTML, standalone
page PDF, structured-data and image-export features introduced by the three
0.13 alpha releases. It does not add a new conversion family or broaden the
public mutation surface.

## Reference and reproducibility

The semantic reference is the source tree for Poppler 26.07.0:

- `utils/pdftohtml.cc` and `utils/HtmlOutputDev.cc` for HTML page selection,
  text, links and image handling;
- `utils/pdfseparate.cc` and `poppler/PDFDoc.cc::savePageAs` for autonomous
  page output;
- `utils/pdftotext.cc` and `poppler/TextOutputDev.cc` for physical/raw text
  ordering and glyph-to-Unicode behavior;
- `utils/pdfimages.cc` and `utils/ImageOutputDev.cc` for image discovery and
  encoded/decoded output choices.

`tests/fixtures/conversion-beta1-compatibility.json` pins every input hash,
classification, applicable Poppler tool and accepted product difference.
`tests/fixtures/verify_poppler_013_beta1.py` optionally reproduces the
executable differential when Poppler 26.07.0 utilities are available:

```bash
python tests/fixtures/verify_poppler_013_beta1.py \
  --poppler-bin /path/to/poppler-26.07/bin
```

Poppler executables are development references only. They are never restored,
packaged, invoked by the library or required by consumers and CI.

## Qualified matrix

| Area | Deterministic corpus | Required managed behavior | Accepted Poppler.Net difference |
| --- | --- | --- | --- |
| Subset and missing fonts | `truetype-format0-subset.pdf`, generated Base-14/inherited-resource fixtures | Source text remains in HTML with embedded fonts disabled, vector/image output disabled and raster fallback omitted; normalized and raw names remain separate | Poppler commonly reports the raw subset name; schema `1.0` exposes both forms |
| Annotations and destinations | `annotations-alpha1.pdf` | Safe URI/local links are retained; isolated pages remove invalid cross-page destinations and reopen | Active actions remain inspection-only and are never executed |
| AcroForm widgets | `acroform-alpha2.pdf` | Widget appearance and value survive extraction; isolated widgets become self-contained terminal fields | Mutation, XFA and JavaScript calculation remain out of scope |
| Boxes, rotation and shared graphs | `compatibility-beta1.pdf` | Selected HTML/JSON ranges identify the same source pages; extracted pixels, boxes and rotation equal the source | HTML is a rendering artifact without Poppler frames or viewer chrome |
| Images and filters | `images-and-color.pdf` | Image count, names, media types and manifest paths are stable; reusable originals and PNG fallbacks follow the documented policy | Masks and PDF-dependent color/filter semantics are composited rather than emitted as unusable raw files |
| Encryption | `r4-aes-128.pdf` plus the R2-R6 security corpus | Conversion requires a successful unlock; extracted pages are autonomous and unencrypted | Encryption preservation and password mutation remain out of scope |
| Malformed and large graphs | `robustness-beta2.pdf`, missing-info, invalid-filter and cyclic-resource fixtures | Optional metadata damage is diagnostic-only; malformed required streams fail deterministically; limits apply before unbounded growth | Repair is conservative and may reject documents Poppler heuristically repairs |

The three documents that previously exposed missing-font text, mirrored SVG
text and damaged optional metadata were also exercised locally across HTML,
structured export and page separation. They are not committed because they
contain private real-world data; the behaviors are represented by the
synthetic fixtures above.

## Cross-surface contract

The shared defaults are physical text order, included images, reusable
original image payloads when safe, preserved annotations for extracted pages
and the complete document/page range selected by the caller.

- The API accepts zero-based page indexes and ranges.
- The CLI accepts one-based `--page`, `--first-page` and `--last-page` values
  and translates them to the same API options.
- The playground exposes current-page HTML/PDF/JSON/XML/XHTML downloads plus
  complete-document HTML/JSON/XML/XHTML and bundle downloads. All processing
  remains browser-local.
- Names may differ by container (filesystem directory versus ZIP entry), but
  page identity, normalized/raw font names, schema identifiers, media types
  and collision behavior are equivalent.

CI installs the packaged CLI on Ubuntu, Windows and macOS and exercises HTML,
page separation, page/range JSON and structured image bundles. Clean NuGet
consumers on `net8.0` and `net10.0` render HTML, export a selected schema `1.0`
range and reopen an extracted page.

## Promotion gate

Beta.1 may be promoted only when:

- every confirmed conversion regression is represented by a deterministic
  fixture or focused generator test;
- the manifest contains no unclassified material difference;
- no known P0/P1 correctness defect remains in the declared 0.13 conversion
  scope;
- the library, CLI tool, WebAssembly playground, package/source verification
  and all Ubuntu/Windows/macOS jobs pass on the final commit.
