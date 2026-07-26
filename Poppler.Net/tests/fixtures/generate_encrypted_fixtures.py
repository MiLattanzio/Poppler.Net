#!/usr/bin/env python3
"""Regenerate the encrypted compatibility fixtures.

Generation dependencies are intentionally test-only and are not part of the
.NET build graph:

    python -m pip install pypdf==6.10.0 reportlab==4.4.9
"""

from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path

from pypdf import PdfReader, PdfWriter
from pypdf.constants import UserAccessPermissions
from pypdf.generic import (
    ArrayObject,
    BooleanObject,
    DictionaryObject,
    NameObject,
    NullObject,
    StreamObject,
)
from reportlab.pdfgen import canvas


USER_PASSWORD = "user-03"
OWNER_PASSWORD = "owner-03"
TITLE = "Poppler.Net encrypted fixture"
TEXT = "Encrypted managed PDF R2-R6"
ATTACHMENT = b"encrypted attachment payload"
XMP = b"""<x:xmpmeta xmlns:x='adobe:ns:meta/'>
  <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>
    <rdf:Description xmlns:test='https://example.invalid/poppler-net/'
                     test:marker='Poppler.Net encrypted XMP'/>
  </rdf:RDF>
</x:xmpmeta>"""
ALGORITHMS = (
    ("r2-rc4-40.pdf", "RC4-40", 2, "Rc4"),
    ("r3-rc4-128.pdf", "RC4-128", 3, "Rc4"),
    ("r4-aes-128.pdf", "AES-128", 4, "Aes128"),
    ("r5-aes-256.pdf", "AES-256-R5", 5, "Aes256"),
    ("r6-aes-256.pdf", "AES-256", 6, "Aes256"),
)


def make_plain_pdf() -> bytes:
    output = io.BytesIO()
    pdf = canvas.Canvas(output, pagesize=(300, 300), invariant=1)
    pdf.setTitle(TITLE)
    pdf.setFont("Helvetica", 14)
    pdf.drawString(36, 240, TEXT)
    pdf.showPage()
    pdf.save()
    return output.getvalue()


def encrypt(
    plain: bytes,
    algorithm: str,
    *,
    string_identity: bool = False,
    encrypt_metadata: bool = True,
    explicit_crypt: bool = False,
    embedded_file_only: bool = False,
) -> bytes:
    source = PdfReader(io.BytesIO(plain))
    writer = PdfWriter()
    writer.append_pages_from_reader(source)
    writer.add_metadata({"/Title": TITLE, "/Producer": "Poppler.Net fixture generator"})
    writer.add_attachment("secret.txt", ATTACHMENT)
    writer.xmp_metadata = XMP
    metadata_stream = writer.root_object["/Metadata"].get_object()
    metadata_stream[NameObject("/Type")] = NameObject("/Metadata")
    metadata_stream[NameObject("/Subtype")] = NameObject("/XML")

    # Reserved permission bits remain set. The user may print, copy and use
    # accessibility extraction, but may not modify, annotate, fill or assemble.
    permissions = (
        0xFFFFF0C0
        | int(UserAccessPermissions.PRINT)
        | int(UserAccessPermissions.EXTRACT)
        | int(UserAccessPermissions.EXTRACT_TEXT_AND_GRAPHICS)
    )
    writer.encrypt(
        USER_PASSWORD,
        OWNER_PASSWORD,
        permissions_flag=permissions,
        algorithm=algorithm,
    )
    encryption = writer._encryption
    entry = writer._encrypt_entry
    assert encryption is not None
    assert entry is not None

    if string_identity:
        encryption.StrF = "/Identity"
        entry[NameObject("/StrF")] = NameObject("/Identity")

    if embedded_file_only:
        encryption.StrF = "/Identity"
        encryption.StmF = "/Identity"
        encryption.EFF = "/AESV2"
        entry[NameObject("/StrF")] = NameObject("/Identity")
        entry[NameObject("/StmF")] = NameObject("/Identity")
        entry[NameObject("/EFF")] = NameObject("/StdCF")
        for object_number, candidate in enumerate(writer._objects, start=1):
            if (
                isinstance(candidate, StreamObject)
                and candidate.get("/Type") == "/EmbeddedFile"
            ):
                crypt = encryption._make_crypt_filter(object_number, 0)
                candidate.set_data(crypt.ef_crypt.encrypt(candidate._data))

    if not encrypt_metadata:
        encryption.EncryptMetadata = False
        replacement = encryption.write_entry(USER_PASSWORD, OWNER_PASSWORD)
        reference = entry.indirect_reference
        assert reference is not None
        replacement[NameObject("/EncryptMetadata")] = BooleanObject(False)
        replacement.indirect_reference = reference
        writer._objects[reference.idnum - 1] = replacement
        writer._encrypt_entry = replacement
        original_encrypt_object = encryption.encrypt_object

        def encrypt_object(candidate, object_number, generation):
            if (
                isinstance(candidate, StreamObject)
                and candidate.get("/Type") == "/Metadata"
            ):
                return candidate
            return original_encrypt_object(candidate, object_number, generation)

        encryption.encrypt_object = encrypt_object

    if explicit_crypt:
        content = writer.pages[0]["/Contents"].get_object()
        current_filter = content.get("/Filter")
        current_filters = (
            list(current_filter)
            if isinstance(current_filter, ArrayObject)
            else ([current_filter] if current_filter is not None else [])
        )
        content[NameObject("/Filter")] = ArrayObject(
            [NameObject("/Crypt"), *current_filters]
        )
        crypt_parameters = DictionaryObject(
            {NameObject("/Name"): NameObject("/StdCF")}
        )
        content[NameObject("/DecodeParms")] = ArrayObject(
            [crypt_parameters, *(NullObject() for _ in current_filters)]
        )

    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def main() -> None:
    directory = Path(__file__).resolve().parent
    plain = make_plain_pdf()
    fixtures = []
    for filename, algorithm, revision, expected_primitive in ALGORITHMS:
        payload = encrypt(plain, algorithm)
        path = directory / filename
        path.write_bytes(payload)
        fixtures.append(
            {
                "file": filename,
                "algorithm": algorithm,
                "revision": revision,
                "primitive": expected_primitive,
                "sha256": hashlib.sha256(payload).hexdigest(),
            }
        )

    variants = []
    variant_specs = (
        (
            "r4-aes-128-string-identity.pdf",
            {"string_identity": True},
            "Identity strings with AES-128 streams",
        ),
        (
            "r4-aes-128-unencrypted-metadata.pdf",
            {"encrypt_metadata": False},
            "AES-128 with EncryptMetadata false",
        ),
        (
            "r4-aes-128-explicit-crypt.pdf",
            {"explicit_crypt": True},
            "AES-128 selected by an explicit stream Crypt filter",
        ),
        (
            "r4-aes-128-embedded-file-only.pdf",
            {"embedded_file_only": True},
            "Identity strings/streams with AES-128 EFF",
        ),
    )
    for filename, options, purpose in variant_specs:
        payload = encrypt(plain, "AES-128", **options)
        (directory / filename).write_bytes(payload)
        variants.append(
            {
                "file": filename,
                "purpose": purpose,
                "sha256": hashlib.sha256(payload).hexdigest(),
            }
        )

    manifest = {
        "generator": {"pypdf": "6.10.0", "reportlab": "4.4.9"},
        "userPassword": USER_PASSWORD,
        "ownerPassword": OWNER_PASSWORD,
        "title": TITLE,
        "text": TEXT,
        "attachment": ATTACHMENT.decode("ascii"),
        "xmpMarker": "Poppler.Net encrypted XMP",
        "fixtures": fixtures,
        "variants": variants,
    }
    (directory / "encrypted-fixtures.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
