# 0.12 public API freeze

The callable public API was frozen at RC 1 and is stable in `0.12.0`. The
freeze covers exported types and their declared public fields, constructors,
properties, events and methods, including generic arity, parameter modifiers,
optional defaults and public constant values.

`ReleaseCandidateTests.CallablePublicApiMatchesFrozenReleaseSurface` builds a
deterministic reflection surface and normalizes only the value of
`Document.PortVersion`. Its approved SHA-256 is:

`ce87b22579e9458c3c1dcdb1aa01790f15d13006ad177ad4815f1e974e63b527`

The complete stable surface, including `Document.PortVersion`, has SHA-256:

`c72005f9c1bd3418c1a7fd00c582c8ccc88ba7e3beedd65e815a45861b72655b`

The complete hash changed from the RC 1 value
`dae14d92c94ed709bf9012ebe977e24779aa317b2be5a1cdca317f5bbcc83711`
only because the public version constant changed to `0.12.0`. The
version-normalized callable hash remains unchanged.

Changes to the stable callable API require an explicit review of the surface
diff, a new approved fingerprint in this file and in `ReleaseCandidateTests`,
and a corresponding changelog entry. New feature work belongs to a later
release line.

The descriptive examples in [API.md](API.md) and the support boundaries in
[COMPATIBILITY.md](COMPATIBILITY.md) were audited against this frozen surface.
