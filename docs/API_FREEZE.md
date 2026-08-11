# 0.12 public API freeze

The callable public API is frozen at `0.12.0-rc.1`. The freeze covers exported
types and their declared public fields, constructors, properties, events and
methods, including generic arity, parameter modifiers, optional defaults and
public constant values.

`ReleaseCandidateTests.CallablePublicApiMatchesFrozenReleaseSurface` builds a
deterministic reflection surface and normalizes only the value of
`Document.PortVersion`. Its approved SHA-256 is:

`ce87b22579e9458c3c1dcdb1aa01790f15d13006ad177ad4815f1e974e63b527`

The complete rc.1 surface, including `Document.PortVersion`, has SHA-256:

`dae14d92c94ed709bf9012ebe977e24779aa317b2be5a1cdca317f5bbcc83711`

The complete hash is expected to change when rc.1 is promoted to stable because
the public version constant changes from `0.12.0-rc.1` to `0.12.0`. The
version-normalized callable hash must remain unchanged.

After rc.1, only a documented release blocker may change the callable API. Any
such exception requires an explicit review of the surface diff, a new approved
fingerprint in this file and in `ReleaseCandidateTests`, and a corresponding
changelog entry. New feature work belongs to a later release line.

The descriptive examples in [API.md](API.md) and the support boundaries in
[COMPATIBILITY.md](COMPATIBILITY.md) were audited against this frozen surface.
