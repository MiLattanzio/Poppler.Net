# Poppler.Net release roadmap

This roadmap records the completed stabilization path from
`v0.12.0-alpha.3` to `0.12.0` and defines the active `0.13.0` conversion and
export line. It is a release contract: work may be split into smaller issues,
but each version is complete only when its exit criteria are satisfied.

Dates are intentionally omitted until capacity is agreed. Release readiness,
not a calendar date, controls promotion to the next milestone.

## Baseline and release boundaries

The roadmap starts from `v0.12.0-alpha.3` (`08f71d8`), with Poppler 26.07 as
the behavior reference. The alpha releases established the planned 0.12
graphics slices:

- `alpha.1`: user-space stroke outlines and the shared fill/stroke/clip
  scanner;
- `alpha.2`: the transparency compositor, groups, masks, and bounded resource
  handling;
- `alpha.3`: type 1 shadings, adaptive type 6/7 meshes, and bounded SVG
  fallback.

The remaining 0.12 releases stabilize this feature set. They do not add a new
rendering subsystem or broaden Poppler.Net beyond its managed-only, read-only
scope.

## Release sequence

`0.12.0-alpha.3` -> `0.12.0-beta.1` -> `0.12.0-beta.2` -> `0.12.0-rc.1` -> `0.12.0`

| Version | Purpose | GitHub tracking | Promotion gate |
| --- | --- | --- | --- |
| `0.12.0-beta.1` | Graphics compatibility closure | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/1), [issue #20](https://github.com/MiLattanzio/Poppler.Net/issues/20) | No known high-impact graphics correctness defect in the declared 0.12 scope |
| `0.12.0-beta.2` | Robustness, performance, concurrency, and package hardening | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/2), [issue #21](https://github.com/MiLattanzio/Poppler.Net/issues/21) | Safety, performance, three-OS CI, and clean-consumer gates pass |
| `0.12.0-rc.1` | API freeze and release qualification | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/3), [issue #22](https://github.com/MiLattanzio/Poppler.Net/issues/22) | Feature-complete, publication-ready candidate with no known release blocker |
| `0.12.0` | Stable publication | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/4), [issue #23](https://github.com/MiLattanzio/Poppler.Net/issues/23) | Qualified artifacts are published and verified from a clean consumer |

## 0.12.0-beta.1: graphics compatibility closure

### Scope

- Expand the real-world and deterministic differential corpus for stroke
  geometry, transparency, function shadings, Gouraud and patch meshes, and
  bounded SVG fallback.
- Compare representative pages with Poppler 26.07 and classify differences as
  parser, display-list, raster, SVG, color, or expected antialiasing variance.
- Fix confirmed correctness defects inside the existing 0.12 rendering model.
- Preserve public API compatibility and the managed-only, read-only dependency
  boundary.
- Keep canonical PNG and SVG gates green on Ubuntu, Windows, and macOS.

### Exit criteria

- [x] No known P0/P1 graphics correctness defect remains in the declared 0.12
  scope.
- [x] Every new regression has a deterministic fixture or focused unit test.
- [x] Full CI passes on Ubuntu, Windows, and macOS.
- [x] Poppler comparisons and approved differences are recorded in the
  verification documentation.
- [x] Changelog, compatibility documentation, and release notes describe the
  beta.1 state.

## 0.12.0-beta.2: hardening

Beta.2 begins only after the beta.1 tracker is complete.

### Scope

- Add adversarial and fuzz-derived cases for geometry growth, nested groups and
  masks, shading functions, mesh refinement, clips, page boxes, and malformed
  streams within existing support.
- Validate cumulative pixel, triangle, segment, decoded-stream, and working-set
  limits before allocation or growth.
- Exercise concurrent read-only rendering and deterministic resource
  discovery.
- Refresh performance and allocation baselines and fix material regressions.
- Verify restore, build, and render from both the produced NuGet package and an
  extracted source archive.
- Review dependency, license, package-content, and cross-platform metadata
  gates.

### Exit criteria

- [x] No known P0/P1 robustness, resource-exhaustion, concurrency, or packaging
  defect remains.
- [x] Safety-limit and malformed-input cases fail deterministically with
  bounded diagnostics.
- [x] Performance and allocation gates pass without unexplained regression.
- [x] Full CI and managed-only verification pass on all supported runners.
- [x] NuGet consumer smoke and extracted-source gates pass.
- [x] Documentation reflects the final beta behavior and limits.

## 0.12.0-rc.1: release qualification

RC.1 begins only after the beta.2 tracker is complete. It freezes the public
callable API; new feature work is not accepted in this milestone.

### Scope

- Freeze public callable API and version metadata.
- Resolve all release blockers found during beta.2.
- Run the complete historical and 0.12 corpus, Poppler differential review,
  clean NuGet consumer smoke, and extracted-source gate.
- Audit release notes, changelog, compatibility matrix, API documentation,
  license and notice files, and package contents.
- Validate the release workflow and tag-version guard without publishing a
  stable package.

### Exit criteria

- [x] No known P0/P1 defect remains; lower-priority work is documented and
  assigned beyond 0.12 where appropriate.
- [x] The public API fingerprint is frozen and approved.
- [x] Three-OS CI, managed-only verification, package inspection, source
  archive, and consumer smoke pass.
- [x] RC package and release notes accurately identify prerelease status.
- [x] The stable-release checklist is ready.

## 0.12.0: stable release

Stable work begins only after the rc.1 tracker is complete. From rc.1 onward,
only release-blocking fixes may change the 0.12 release candidate.

### Scope

- Apply release-blocker fixes and rerun affected and full qualification gates.
- Set package, assembly, port, and CLI version metadata to `0.12.0`; finalize
  changelog and release notes.
- Confirm stable GitHub release metadata, verify that the tag matches the
  package version, and publish NuGet.
- Verify NuGet indexing, package download, install, and render from a clean
  consumer project; preserve final release assets and hashes.
- Close or move remaining issues to the next release line.

### Exit criteria

- [x] All rc.1 exit criteria remain green after final changes.
- [x] The stable tag targets the approved `master` commit.
- [x] The GitHub release is marked stable and its notes are final.
- [x] NuGet `0.12.0` is published and a clean consumer smoke test passes.
- [x] The milestone contains no unresolved release blocker.

## Explicitly outside 0.12

The following areas are not release blockers for 0.12 and belong to a later
roadmap unless they expose a regression in behavior already supported:

- advanced ICC LUT and device-link transforms, proofing, and rendering intents;
- spot-color overprint;
- native SVG mesh primitives;
- complex-script shaping, contextual GSUB/GPOS, and font variations;
- PDF mutation, writing, and signing.

## GitHub workflow

- The tracker issue in each milestone owns its release checklist.
- A milestone closes only when its tracker exit criteria are satisfied.
- Defects and supporting tasks are assigned to the earliest milestone whose
  exit criteria they block and link back to the tracker.
- Work that expands the explicit 0.12 boundary is moved to a later roadmap;
  it does not delay this release line.

---

## 0.13.0 roadmap: managed conversion and export

The 0.13 line starts from stable `v0.12.0` (`56db30e`) and keeps Poppler
26.07 as the behavioral reference. Its purpose is to turn the existing parser,
text, image, SVG, raster, link, destination, annotation, and font pipelines
into reusable conversion APIs without adding a native runtime dependency.

The first feature is HTML conversion for a complete document and for individual
pages. Page separation follows only after the HTML API is established, because
it introduces a new and higher-risk PDF-writing boundary. Structured data and
original-image exports complete the planned alpha feature set.

### Feasibility and architectural boundaries

- **HTML conversion is feasible now.** Poppler 26.07 provides the reference in
  `utils/pdftohtml.cc` and `utils/HtmlOutputDev.*`. Poppler.Net already exposes
  positioned text, normalized fonts, images, links, page geometry, SVG, and
  raster output. The initial contract is deterministic fixed-layout HTML with
  selectable text, not semantic reflow.
- **Full-document and per-page HTML share one renderer.** The API must support
  one page, a page range, or the whole document; self-contained data-URL output
  and a directory bundle are packaging modes over the same page model.
- **Text remains data, never only pixels.** If unsupported graphics require an
  SVG or raster background fallback, extracted text must still be emitted in
  the DOM so it remains selectable, searchable, and downloadable.
- **Page separation is feasible with a bounded writer.** Poppler's
  `utils/pdfseparate.cc` delegates to `PDFDoc::savePageAs`, which traverses the
  reachable page object graph and writes a new catalog, page tree, objects,
  xref, and trailer. Poppler.Net currently saves only the original bytes, so
  this work is a new standalone-page writer, not a small wrapper.
- **The writer does not broaden into arbitrary mutation.** Its public contract
  is limited to producing autonomous PDFs for selected pages. Incremental
  updates, signing, output encryption, page merging, and general editing remain
  outside 0.13.
- **Additional alpha conversions reuse existing primitives.** Versioned
  JSON/XML/XHTML exports expose page, text, font, link, and image data. Original
  encoded image streams are exported only when they are independently reusable;
  decoded PNG is the required fallback.
- **Runtime remains managed-only.** Poppler 26.07 is a development and
  differential-testing reference, never a shipped library or subprocess.

### Release sequence

`0.12.0` -> `0.13.0-alpha.1` -> `0.13.0-alpha.2` -> `0.13.0-alpha.3` -> `0.13.0-beta.1` -> `0.13.0-beta.2` -> `0.13.0-rc.1` -> `0.13.0`

| Version | Purpose | GitHub tracking | Promotion gate |
| --- | --- | --- | --- |
| `0.13.0-alpha.1` | Managed fixed-layout HTML export | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/5), [issue #28](https://github.com/MiLattanzio/Poppler.Net/issues/28) | Whole-document, page-range, and single-page HTML is deterministic, selectable, and usable from API, CLI, and playground |
| `0.13.0-alpha.2` | Standalone PDF page extraction | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/6), [issue #29](https://github.com/MiLattanzio/Poppler.Net/issues/29) | Extracted PDFs are self-contained, bounded, and reopen with Poppler.Net and Poppler 26.07 |
| `0.13.0-alpha.3` | Structured data and image exports | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/7), [issue #30](https://github.com/MiLattanzio/Poppler.Net/issues/30) | Versioned schemas, normalized/raw font names, and safe original-image export have deterministic fallbacks |
| `0.13.0-beta.1` | Conversion compatibility closure | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/8), [issue #31](https://github.com/MiLattanzio/Poppler.Net/issues/31) | No known P0/P1 correctness defect in the declared conversion scope |
| `0.13.0-beta.2` | Robustness, performance, concurrency, package, and playground hardening | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/9), [issue #32](https://github.com/MiLattanzio/Poppler.Net/issues/32) | Safety, performance, three-OS CI, WebAssembly, source, and clean-consumer gates pass |
| `0.13.0-rc.1` | API freeze and release qualification | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/10), [issue #33](https://github.com/MiLattanzio/Poppler.Net/issues/33) | APIs, schemas, manifests, and CLI contracts are frozen with no release blocker |
| `0.13.0` | Stable publication | [milestone](https://github.com/MiLattanzio/Poppler.Net/milestone/11), [issue #34](https://github.com/MiLattanzio/Poppler.Net/issues/34) | Qualified GitHub, NuGet, consumer, and deployed-playground artifacts are verified |

### 0.13.0-alpha.1: managed HTML export

Alpha.1 is the first implementation milestone and owns the API shape reused by
all later HTML modes.

#### Scope

- Define page and document APIs for fixed-layout HTML rendering and export.
- Support a single page, page ranges, and complete documents.
- Emit selectable positioned text, normalized font families with fallbacks,
  links/destinations, images, page boxes, rotation, and supported graphics.
- Reconstruct decodable glyph outlines as pure-managed TrueType web fonts with
  collision-free private-use mappings and a separate source-Unicode copy layer.
- Keep missing-font text visible through exact-origin CSS fallbacks and retain
  clip/mask/transparency/occlusion-sensitive text in the SVG background.
- Support a self-contained HTML document and a multi-file directory bundle
  with stable names and a manifest.
- Use SVG/raster backgrounds only as bounded fallbacks while retaining text in
  the DOM.
- Add equivalent CLI commands and full/per-page WebAssembly playground
  downloads.
- Build a focused and real-world differential corpus against Poppler 26.07
  `pdftohtml` and document intentional differences.

#### Exit criteria

- [x] Whole-document, page-range, and single-page output is deterministic.
- [x] Text remains selectable/searchable through every rendering fallback.
- [x] Links, images, fonts, rotation, page boxes, and supported graphics pass
  focused tests.
- [x] Self-contained and directory-bundle modes open in current Chromium.
- [x] API, CLI, and playground expose equivalent options and stable downloads.
- [x] Managed-only verification and Ubuntu, Windows, and macOS CI pass.
- [x] Public documentation records options, fallbacks, limits, and known
  differences from Poppler.

### 0.13.0-alpha.2: standalone PDF page extraction

Alpha.2 begins only after the alpha.1 tracker is complete. It adds the minimum
writer required for a managed `pdfseparate` equivalent.

#### Scope

- Export one page or a page range into separately named autonomous PDFs.
- Traverse and copy the reachable object graph required by each page.
- Emit a minimal catalog and page tree plus deterministic objects, streams,
  xref, trailer, and EOF.
- Preserve inherited resources, content, page boxes, rotation, supported
  annotations, and required form resources.
- Rewrite or omit cross-page destinations, outlines, and other document-level
  structures that cannot remain valid after isolation.
- Initially accept unlocked encrypted input and emit an unencrypted result;
  document this policy explicitly.
- Apply object-count, recursion, decoded-stream, output-size, and cycle limits.
- Add API, CLI, and playground output with stable page-numbered names.

#### Exit criteria

- [x] Every extracted PDF reopens with Poppler.Net and Poppler 26.07.
- [x] Content, resources, boxes, rotation, and supported annotations survive
  extraction.
- [x] Inheritance, compressed objects, cycles, encryption policy, and malformed
  graphs have bounded deterministic tests.
- [x] The writer remains internal and exposes no general mutation API.
- [x] API, CLI, playground, and three-OS CI gates pass.

### 0.13.0-alpha.3: structured data and image exports

Alpha.3 begins only after the page-extraction tracker is complete and closes
the planned 0.13 feature set.

#### Scope

- Define versioned deterministic JSON and XML/XHTML exports for documents and
  pages.
- Include page geometry, ordered text boxes, fonts, links/destinations, and
  image metadata with stable identifiers.
- Expose normalized subset font names while retaining the raw PDF font name
  separately.
- Export original JPEG/JP2/JBIG2/CCITT or other encoded streams only when the
  payload is independently reusable; otherwise provide decoded PNG plus
  metadata.
- Standardize manifests, media types, extensions, and collision-safe names.
- Add API, CLI, and playground downloads and compare representative results
  with Poppler 26.07 `pdftotext` and `pdfimages` behavior.

#### Exit criteria

- [ ] Schemas are documented, versioned, and deterministic.
- [ ] Text geometry, normalized/raw font names, links, and image metadata pass
  focused tests.
- [ ] Raw images are emitted only when reusable; every other case has a bounded
  decoded fallback or diagnostic.
- [ ] Names, extensions, media types, and manifests agree across API, CLI, and
  playground.
- [ ] Hostile filter chains and oversized images remain bounded on all CI
  platforms.

### 0.13.0-beta.1: conversion compatibility closure

Beta.1 begins only after all three alpha trackers are complete.

#### Scope and gate

- Expand the deterministic and real-world corpus across HTML, page extraction,
  structured data, fonts, and images.
- Compare against Poppler 26.07 `pdftohtml`, `pdfseparate`, `pdftotext`, and
  `pdfimages`; classify and record every material difference.
- Cover encrypted/unlocked documents, inherited resources, annotations, forms,
  rotations, unusual fonts, malformed input, and large resource graphs.
- Fix all known P0/P1 correctness defects without expanding 0.13 boundaries.
- Promote only when API, CLI, playground, documentation, and three-OS CI agree
  and no high-impact correctness defect remains.

### 0.13.0-beta.2: export hardening

Beta.2 begins only after the compatibility tracker is complete.

#### Scope and gate

- Add adversarial and fuzz-derived cases for HTML generation, writer graphs,
  filters, manifests, and file names.
- Enforce cumulative limits before allocation or growth: objects, recursion,
  decoded bytes, pixels, pages, DOM nodes, files, output size, and working set.
- Exercise concurrent conversions, deterministic output, performance, and
  allocation baselines.
- Validate WebAssembly memory/download lifetime and large multi-file exports.
- Verify NuGet and extracted-source consumers on .NET 8 and .NET 10 plus
  dependency, license, Source Link, package-content, and three-OS gates.
- Promote only when no P0/P1 safety, performance, concurrency, packaging, or
  playground defect remains.

### 0.13.0-rc.1: release qualification

RC.1 freezes callable APIs, option defaults, schemas, manifests, and CLI
contracts. Only release blockers may change the candidate.

#### Exit criteria

- [ ] No known P0/P1 defect remains and deferred work is explicitly tracked.
- [ ] APIs, schemas, manifests, defaults, CLI, and version metadata are frozen.
- [ ] Full conversion corpus, Poppler differential review, browser checks,
  package inspection, source archive, and clean-consumer tests pass.
- [ ] Documentation, changelog, compatibility matrix, licenses, and release
  notes are publication-ready.
- [ ] The stable-release checklist contains no unresolved blocker.

### 0.13.0: stable release

Stable work begins only after RC.1 is complete. The approved candidate is
published and then verified from its public artifacts.

#### Exit criteria

- [ ] All RC.1 gates remain green after final release-blocker fixes.
- [ ] The stable tag targets the approved `master` commit and release assets
  plus hashes are preserved.
- [ ] NuGet `0.13.0` is pushed and clean .NET 8/.NET 10 consumers pass HTML and
  page-extraction smoke tests.
- [ ] The deployed GitHub Pages playground downloads representative HTML,
  separated PDFs, structured data, and images correctly.
- [ ] The milestone contains no unresolved release blocker; deferred work is
  reassigned to the next line.

### Explicitly outside 0.13

- semantic/reflow HTML and office-format reconstruction;
- OCR and PDF JavaScript execution;
- arbitrary mutation, incremental update, signing, and output encryption;
- PDF page merge/`pdfunite` until the bounded extraction writer is proven and a
  separate roadmap approves the broader object-rewrite semantics;
- PostScript/EPS or Cairo-backed conversion that would require a native runtime
  dependency;
- lossless raw-image export when the source payload is not independently
  reusable;
- the advanced color, SVG mesh, and shaping items already deferred after 0.12.

### Candidate work after 0.13

The bounded writer and export manifests created in 0.13 enable later planning
for page merge, page-range recomposition, semantic HTML, richer accessibility
trees, and additional image encodings. These are candidates, not implied 0.13
commitments, and each requires its own feasibility gate and tracker.

### GitHub workflow for 0.13

- One tracker issue owns each release milestone and its promotion checklist.
- Feature work is assigned to the earliest milestone whose exit criteria it
  blocks and links back to the tracker.
- A milestone closes only after its tracker criteria pass; later work does not
  compensate for an unresolved earlier gate.
- Changes to the architectural boundaries require an explicit roadmap update,
  not silent expansion inside an implementation issue.
