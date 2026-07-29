#!/usr/bin/env python3
"""Verify fixture hashes and plaintext with the pinned independent generator."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from pypdf import PdfReader
from pypdf.generic import ArrayObject, NameObject


def main() -> None:
    directory = Path(__file__).resolve().parent
    manifest = json.loads(
        (directory / "encrypted-fixtures.json").read_text(encoding="utf-8")
    )
    expected_text = manifest["text"]
    expected_attachment = manifest["attachment"].encode("ascii")
    expected_xmp = manifest["xmpMarker"].encode("ascii")

    entries = [*manifest["fixtures"], *manifest["variants"]]
    for entry in entries:
        payload = (directory / entry["file"]).read_bytes()
        assert hashlib.sha256(payload).hexdigest() == entry["sha256"]

    for entry in manifest["fixtures"]:
        path = directory / entry["file"]
        for password in (manifest["userPassword"], manifest["ownerPassword"]):
            reader = PdfReader(path)
            assert reader.decrypt(password)
            assert reader.metadata.title == manifest["title"]
            assert expected_text in reader.pages[0].extract_text()
            assert next(reader.attachment_list).content == expected_attachment
            metadata = reader.root_object["/Metadata"].get_object().get_data()
            assert expected_xmp in metadata

    reader = PdfReader(directory / "r4-aes-128-string-identity.pdf")
    assert reader.decrypt(manifest["userPassword"])
    assert expected_text in reader.pages[0].extract_text()

    # pypdf decrypts the object but deliberately refuses a named /Crypt
    # content filter. Remove that already-applied filter to inspect the
    # remaining ASCII85/Flate pipeline.
    reader = PdfReader(directory / "r4-aes-128-explicit-crypt.pdf")
    assert reader.decrypt(manifest["userPassword"])
    content = reader.pages[0]["/Contents"].get_object()
    filters = list(content["/Filter"])
    parameters = list(content["/DecodeParms"])
    assert str(filters.pop(0)) == "/Crypt"
    parameters.pop(0)
    content[NameObject("/Filter")] = ArrayObject(filters)
    content[NameObject("/DecodeParms")] = ArrayObject(parameters)
    assert expected_text.encode("ascii") in content.get_data()

    # pypdf exposes EFF but does not select it for embedded-file streams.
    # Apply its independently generated EFF primitive explicitly.
    reader = PdfReader(directory / "r4-aes-128-embedded-file-only.pdf")
    assert reader.decrypt(manifest["userPassword"])
    attachment = next(reader.attachment_list)
    stream = attachment._embedded_file
    reference = stream.indirect_reference
    crypt = reader._encryption._make_crypt_filter(
        reference.idnum,
        reference.generation,
    )
    assert crypt.ef_crypt.decrypt(attachment.content) == expected_attachment

    # The metadata-exclusion fixture must contain plaintext XMP bytes.
    metadata_false = (
        directory / "r4-aes-128-unencrypted-metadata.pdf"
    ).read_bytes()
    assert expected_xmp in metadata_false

    print(
        f"verified {len(manifest['fixtures'])} standard fixtures and "
        f"{len(manifest['variants'])} variants"
    )


if __name__ == "__main__":
    main()
