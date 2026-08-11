# Poppler.Net 0.12 roadmap

This roadmap defines the stabilization path from `v0.12.0-alpha.3` to the
stable `0.12.0` release. It is a release contract: work may be split into
smaller issues, but each version is complete only when its exit criteria are
satisfied.

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
- [ ] Three-OS CI, managed-only verification, package inspection, source
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

- [ ] All rc.1 exit criteria remain green after final changes.
- [ ] The stable tag targets the approved `master` commit.
- [ ] The GitHub release is marked stable and its notes are final.
- [ ] NuGet `0.12.0` is published and a clean consumer smoke test passes.
- [ ] The milestone contains no unresolved release blocker.

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
