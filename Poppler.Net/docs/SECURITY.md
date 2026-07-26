# Standard Security Handler port

Version `0.3.0-alpha.1` ports the document-opening portion of Poppler
26.07.0's `SecurityHandler` and `Decrypt` subsystems to C#.

## Implemented

| PDF mode | Password/key derivation | Object cipher |
| --- | --- | --- |
| `V=1/R=2` | 32-byte legacy padding, MD5, 40-bit file key | RC4 |
| `V=2/R=3` | strengthened owner/user validation, 40–128-bit key | RC4 |
| `V=4/R=4` | revision 4 metadata branch and crypt filters | RC4 or AES-128-CBC |
| `V=5/R=5` | SHA-256 validation plus `OE`, `UE` and `Perms` | AES-256-CBC |
| `V=5/R=6` | iterative SHA-256/384/512 algorithm 2.B | AES-256-CBC |

The object number and generation participate in RC4/AES-128 key derivation.
AES object values consume the leading random IV and validate PKCS#7 padding.
AES-256 uses the file key directly, as required by PDF 2.0.

`StrF`, `StmF` and `EFF` are selected independently. `Identity`, `V2`,
`AESV2`, `AESV3`, explicit stream `/Crypt` filters and metadata streams
excluded by `EncryptMetadata false` are supported. Xref streams remain
unencrypted as required by the PDF format.

## Public behavior

- A missing or incorrect password creates a locked `Document`.
- `EncryptionInfo` is available while locked; page/content APIs are not.
- `Unlock` reparses the original bytes with new credentials and returns the new
  locking state (`false` means unlocked), matching Poppler's C++ frontend.
- `PasswordKind` reports user or owner authentication.
- Owner authentication bypasses `/P`; user authentication maps the eight
  public `Permission` flags.
- File keys and intermediate password material are cleared when replaced or
  disposed. Password comparisons use fixed-time equality.

## Compatibility corpus

The repository contains nine generated encrypted PDFs. Five cover R2 through
R6 and are independently opened by Poppler tools. Four R4 variants cover
different string/stream filters, an independent `EFF`, an explicit `/Crypt` filter and
`EncryptMetadata false`. The variants also document limitations in the
reference readers: Poppler 26.07.0 deliberately accepts only equal `StrF` and
`StmF`, while pypdf 6.10.0 does not decode named `/Crypt` filters.

The fixture generator is pinned to pypdf 6.10.0 and ReportLab 4.4.9. Neither is
part of the .NET dependency graph.

## Deliberate limits

- Public-key security handlers and arbitrary plugin security handlers are out
  of scope.
- Revision 5/6 password bytes use Latin-1 when representable and UTF-8
  otherwise, truncated to 127 bytes. Full SASLprep normalization remains to be
  ported.
- The writer cannot create encryption or change passwords/permissions.
- RC4 and MD5 exist only for reading legacy PDFs; new systems must not use them
  for protecting data.
- Passwords passed to the CLI may be visible in the local process list. The
  library API is the intended integration surface for secret input.
