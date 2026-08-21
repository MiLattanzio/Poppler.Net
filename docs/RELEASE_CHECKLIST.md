# 0.13.0 stable-release checklist

Use this checklist to promote the qualified `0.13.0-rc.1` candidate to
`0.13.0`. After RC.1, accept only fixes for documented release blockers and
rerun every affected gate plus the complete qualification suite.

## Candidate approval

- [ ] Issue #33 is complete and the RC.1 milestone has no open release blocker.
- [ ] The approved `master` commit and successful RC.1 release run are recorded.
- [x] The version-normalized callable API fingerprint is
  `082e5c6049186507381f039a20299754104b3f9c9cc5338a5619aab1e20ea52a`,
  matching `docs/API_FREEZE.md` and the regression test.
- [x] Public option defaults, schema files, manifest shapes and CLI help have
  frozen fingerprints and no feature work remains in the candidate.
- [x] Non-blocking post-0.13 work is separated into issue #40.

## Stable version and documentation

- [ ] Set library, CLI, package-smoke fallback and `Document.PortVersion` to
  exactly `0.13.0`; retain assembly/file version `26.7.0.0`.
- [ ] Replace RC wording in README, compatibility documentation and release
  notes; prepend the stable changelog entry.
- [ ] Update only the complete API hash for the expected `PortVersion` change;
  callable/default/schema/manifest/CLI hashes must not change.
- [x] Audit API/conversion/compatibility/limit documentation, `LICENSE`,
  `NOTICE.md`, package license/repository metadata and content allowlists.
- [ ] Confirm stable release notes contain no prerelease label and show
  `<PackageReference Include="Poppler.Net" Version="0.13.0" />` plus CLI
  tool installation at exactly `0.13.0`.

## Qualification

- [ ] Restore with repository `NuGet.Config`, build Release with warnings as
  errors and run the complete NUnitLite suite on the stable candidate.
- [ ] Run the managed-only verifier and review the pinned Poppler 26.07
  differential classifications; record any accepted differences.
- [ ] Pack the approved source revision and run the strict package verifier.
- [ ] Extract the tracked source archive into a clean directory, then restore,
  build, test, verify, repack and exercise the CLI from that copy.
- [ ] Restore and convert from produced packages as `net8.0` and `net10.0`;
  install and exercise the packaged dotnet tool.
- [ ] PR CI is green for Ubuntu, Windows and macOS, including package/source,
  browser playground and all OS/framework consumer jobs.
- [ ] The tag guard accepts `v0.13.0` and rejects a mismatched tag.

## Publication

- [ ] Merge only the approved PR and confirm `master` equals the qualified
  commit with no unrelated release change.
- [ ] Create tag `v0.13.0` on that commit and a GitHub release marked stable,
  not prerelease; use the audited stable release notes.
- [ ] Follow the release workflow through successful NuGet OIDC login and both
  library/tool `dotnet nuget push` operations; retain run URL and hashes.
- [ ] Confirm GitHub tag, library package, tool package and repository commit
  all identify the same stable revision.

## Post-publication

- [ ] After NuGet indexing, restore `Poppler.Net` `0.13.0` from nuget.org in a
  clean cache as .NET 8 and .NET 10 and exercise HTML, page extraction,
  structured data and image export.
- [ ] Install `Poppler.Net.Cli` `0.13.0` from nuget.org and repeat representative
  conversion commands.
- [ ] Verify NuGet pages, README badges and deployed WebAssembly playground.
- [ ] Record public package/source hashes and final consumer evidence in
  `VERIFICATION.md`.
- [ ] Close the stable tracker and milestone; retain issue #40 as post-0.13
  planning rather than reopening the release scope.
