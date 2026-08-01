#!/usr/bin/env python3
"""Generate deterministic outline regressions for Poppler.Net 0.10.0-alpha.1."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent


def stream(dictionary: str, payload: bytes) -> bytes:
    return dictionary.encode("ascii") + b"\nstream\n" + payload + b"\nendstream"


def pdf_string_hex(value: str) -> str:
    return "<" + (b"\xfe\xff" + value.encode("utf-16-be")).hex().upper() + ">"


class Objects:
    def __init__(self) -> None:
        self.values: list[bytes | None] = []

    def reserve(self) -> int:
        self.values.append(None)
        return len(self.values)

    def add(self, value: bytes) -> int:
        reference = self.reserve()
        self.set(reference, value)
        return reference

    def set(self, reference: int, value: bytes) -> None:
        self.values[reference - 1] = value

    def build(self) -> bytes:
        if any(value is None for value in self.values):
            raise ValueError("unassigned PDF object")
        output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
        offsets = [0]
        for number, value in enumerate(self.values, 1):
            assert value is not None
            offsets.append(len(output))
            output.extend(f"{number} 0 obj\n".encode("ascii"))
            output.extend(value)
            output.extend(b"\nendobj\n")
        xref = len(output)
        output.extend(f"xref\n0 {len(self.values) + 1}\n".encode("ascii"))
        output.extend(b"0000000000 65535 f \n")
        for offset in offsets[1:]:
            output.extend(f"{offset:010d} 00000 n \n".encode("ascii"))
        output.extend(
            (
                f"trailer\n<< /Size {len(self.values) + 1} /Root 1 0 R "
                "/ID [<10000100100001001000010010000100> "
                "<10000100100001001000010010000100>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs = [objects.reserve() for _ in range(3)]
    outlines = objects.reserve()
    chapter_one = objects.reserve()
    section_one = objects.reserve()
    section_two = objects.reserve()
    chapter_two = objects.reserve()
    inspection = objects.reserve()
    deep_item = objects.reserve()
    appendix = objects.reserve()
    uri_action = objects.reserve()
    script_action = objects.reserve()
    goto_action = objects.reserve()
    name_tree = objects.reserve()

    font = objects.add(
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    )
    page_titles = [b"Outline Alpha 1", b"Named Destination", b"Appendix"]
    for index, page_ref in enumerate(page_refs):
        content = b"\n".join(
            [
                b"0.96 g 0 0 360 280 re f",
                f"{0.15 + index * 0.2:.2f} 0.35 0.72 rg 24 170 312 64 re f".encode(
                    "ascii"
                ),
                b"1 g",
                b"BT /F1 20 Tf 32 205 Td (" + page_titles[index] + b") Tj ET",
                b"0.2 g",
                f"BT /F1 12 Tf 32 140 Td (Page {index + 1}) Tj ET".encode("ascii"),
            ]
        )
        content_ref = objects.add(
            stream(f"<< /Length {len(content)} >>", content)
        )
        objects.set(
            page_ref,
            (
                f"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 360 280] "
                f"/Resources << /Font << /F1 {font} 0 R >> >> "
                f"/Contents {content_ref} 0 R >>"
            ).encode("ascii"),
        )

    objects.set(
        uri_action,
        b"<< /S /URI /URI (https://example.test/outline-alpha1) >>",
    )
    objects.set(
        script_action,
        (
            f"<< /S /JavaScript /JS (app.alert\\(\\042inspection only\\042\\);) "
            f"/Next {script_action} 0 R >>"
        ).encode("ascii"),
    )
    objects.set(
        goto_action,
        b"<< /S /GoTo /D (chapter-two) >>",
    )

    objects.set(
        outlines,
        (
            f"<< /Type /Outlines /First {chapter_one} 0 R "
            f"/Last {appendix} 0 R /Count 7 >>"
        ).encode("ascii"),
    )
    objects.set(
        chapter_one,
        (
            f"<< /Title (Chapter One) /Parent {outlines} 0 R "
            f"/First {section_one} 0 R /Last {section_two} 0 R /Count 2 "
            f"/Next {chapter_two} 0 R /Dest [{page_refs[0]} 0 R /XYZ 24 250 1.25] "
            "/C [0.9 0.1 0.1] /F 3 >>"
        ).encode("ascii"),
    )
    objects.set(
        section_one,
        (
            f"<< /Title {pdf_string_hex('Section 1.1 - Overview')} "
            f"/Parent {chapter_one} 0 R /Next {section_two} 0 R "
            "/Dest (chapter-two) >>"
        ).encode("ascii"),
    )
    objects.set(
        section_two,
        (
            f"<< /Title (External reference) /Parent {chapter_one} 0 R "
            f"/Prev {section_one} 0 R /A {uri_action} 0 R /F 1 >>"
        ).encode("ascii"),
    )
    objects.set(
        chapter_two,
        (
            f"<< /Title (Chapter Two) /Parent {outlines} 0 R "
            f"/Prev {chapter_one} 0 R /Next {appendix} 0 R "
            f"/First {inspection} 0 R /Last {inspection} 0 R /Count -1 "
            f"/A {goto_action} 0 R /C [0.1 0.25 0.85] >>"
        ).encode("ascii"),
    )
    objects.set(
        inspection,
        (
            f"<< /Title (Inspection only script) /Parent {chapter_two} 0 R "
            f"/First {deep_item} 0 R /Last {deep_item} 0 R /Count 1 "
            f"/A {script_action} 0 R >>"
        ).encode("ascii"),
    )
    objects.set(
        deep_item,
        (
            f"<< /Title (Deep target) /Parent {inspection} 0 R "
            f"/Dest [{page_refs[2]} 0 R /FitR 24 24 336 256] >>"
        ).encode("ascii"),
    )
    objects.set(
        appendix,
        (
            f"<< /Title (Appendix) /Parent {outlines} 0 R "
            f"/Prev {chapter_two} 0 R /Next {chapter_two} 0 R "
            f"/First {inspection} 0 R /Last {inspection} 0 R "
            "/Dest /appendix /F 2 >>"
        ).encode("ascii"),
    )

    objects.set(
        name_tree,
        (
            f"<< /Names [(appendix) [{page_refs[2]} 0 R /Fit] "
            f"(chapter-two) [{page_refs[1]} 0 R /FitH 250]] >>"
        ).encode("ascii"),
    )
    kids = " ".join(f"{reference} 0 R" for reference in page_refs)
    objects.set(
        pages,
        f"<< /Type /Pages /Count 3 /Kids [{kids}] >>".encode("ascii"),
    )
    objects.set(
        catalog,
        (
            f"<< /Type /Catalog /Pages {pages} 0 R /Outlines {outlines} 0 R "
            f"/PageMode /UseOutlines /Names << /Dests {name_tree} 0 R >> >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    pdf = corpus()
    pdf_path = ROOT / "outline-alpha1.pdf"
    pdf_path.write_bytes(pdf)
    manifest = {
        "file": pdf_path.name,
        "sha256": hashlib.sha256(pdf).hexdigest(),
        "pages": 3,
        "outline_items": 7,
        "coverage": [
            "multilevel-first-last-next-prev-parent",
            "direct-and-named-destinations",
            "style-color-and-open-state",
            "inspection-only-actions",
            "circular-action-and-repeated-outline-references",
        ],
    }
    (ROOT / "outline-alpha1-fixture.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


if __name__ == "__main__":
    main()
