# 0.12.0 stable-release checklist

Use this checklist to promote the qualified `0.12.0-rc.1` candidate to
`0.12.0`. After rc.1, accept only fixes that resolve a documented release
blocker and rerun every affected gate plus the complete qualification suite.

## Candidate approval

- [x] Issue #22 is complete and the rc.1 milestone has no open release blocker.
- [x] The approved master commit and successful rc.1 release run are recorded.
- [x] The version-normalized callable API fingerprint remains
  `ce87b22579e9458c3c1dcdb1aa01790f15d13006ad177ad4815f1e974e63b527`,
  matching `docs/API_FREEZE.md` and the regression test.
- [x] No post-RC blocker fix was required; the stable promotion contains no
  scope or implementation change.

## Stable version and documentation

- [x] Set the library, CLI, package-smoke fallback and `Document.PortVersion`
  to exactly `0.12.0`; retain assembly/file version `26.7.0.0`.
- [x] Replace RC wording in README, compatibility documentation and release
  notes; prepend the stable changelog entry.
- [x] Update only the complete API hash for the expected `PortVersion` change;
  the version-normalized callable hash must not change.
- [x] Audit `docs/API.md`, `docs/COMPATIBILITY.md`, `LICENSE`, `NOTICE.md`,
  package license/repository metadata and the explicit package allowlist.
- [x] Confirm release notes contain no prerelease label and install with
  `<PackageReference Include="Poppler.Net" Version="0.12.0" />`.

## Qualification

- [x] Restore with the repository `NuGet.Config`, build Release with warnings
  as errors and run the complete NUnitLite suite.
- [x] Run the managed-only verifier and the optional Poppler differential
  review; record approved differences.
- [x] Pack with the approved source revision and run the package verifier.
- [x] Extract the tracked source archive into a clean directory, then restore,
  build, test, verify, repack and exercise the CLI from that copy.
- [x] Restore and render from the produced package as `net8.0` and `net10.0`.
- [ ] PR CI is green for Ubuntu, Windows and macOS, package/source verification
  and all six operating-system/framework consumers.
- [x] The tag guard accepts `v0.12.0` and rejects a mismatched tag.

## Publication

- [ ] Merge only the approved PR and confirm `master` equals the qualified
  commit with no unrelated release change.
- [ ] Create tag `v0.12.0` on that commit and a GitHub release marked stable,
  not prerelease; use the audited release notes.
- [ ] Follow the release workflow through the successful NuGet OIDC login and
  `dotnet nuget push`; retain the run URL and artifact hashes.
- [ ] Confirm the GitHub release tag, package version and repository commit all
  identify the same stable revision.

## Post-publication

- [ ] After NuGet indexing, restore `Poppler.Net` `0.12.0` from nuget.org in a
  clean cache and render PNG/SVG as both supported target frameworks.
- [ ] Verify the NuGet page, README badges and WebAssembly playground.
- [ ] Record package/source hashes and final consumer evidence in
  `VERIFICATION.md`.
- [ ] Close issue #23 and the stable milestone, then move non-blocking work to
  the next release line.
