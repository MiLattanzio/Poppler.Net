#!/usr/bin/env python3
"""Generate deterministic rendering and damaged-PDF regressions for 0.9.0-beta.2."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent


def stream(dictionary: str, payload: bytes) -> bytes:
    return dictionary.encode("ascii") + b"\nstream\n" + payload + b"\nendstream"


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
                "/ID [<09000002090000020900000209000002> "
                "<09000002090000020900000209000002>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def page(objects: Objects, reference: int, parent: int, contents: str) -> None:
    objects.set(
        reference,
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 360 240] "
            f"/Resources << >> /Contents {contents} >>"
        ).encode("ascii"),
    )


def add_content(objects: Objects, payload: bytes, declared_length: int | None = None) -> int:
    length = len(payload) if declared_length is None else declared_length
    return objects.add(stream(f"<< /Length {length} >>", payload))


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs = [objects.reserve() for _ in range(5)]
    cycle = objects.reserve()

    caps = add_content(
        objects,
        b"\n".join(
            [
                b"0.97 g 0 0 360 240 re f",
                b"0 G 12 w",
                b"0 J 35 185 m 150 185 l S",
                b"1 J 35 125 m 150 125 l S",
                b"2 J 35 65 m 150 65 l S",
                b"0.05 0.35 0.8 RG 10 w [1 18] 0 d 1 J 205 185 m 325 185 l S",
                b"0.75 0.15 0.1 RG [] 0 d 2 J 210 65 m 265 150 l 320 65 l S",
            ]
        ),
    )
    page(objects, page_refs[0], pages, f"{caps} 0 R")

    dashes = add_content(
        objects,
        b"\n".join(
            [
                b"0.98 g 0 0 360 240 re f",
                b"0.1 0.25 0.75 RG 8 w [28 12] 0 d 0 J",
                b"25 190 m 335 190 l S",
                b"0.75 0.15 0.1 RG [28 12] 0 d",
                b"25 130 m 165 130 l 165 45 l 335 45 l S",
                b"0.1 0.55 0.2 RG [9 4 2] 5 d 6 w",
                b"25 90 m 335 90 l S",
            ]
        ),
    )
    page(objects, page_refs[1], pages, f"{dashes} 0 R")

    first_fragment = add_content(
        objects,
        b"0.94 g 0 0 360 240 re f 0.85 0.1 0.1 rg 25 35 130 170 re f",
    )
    invalid_fragment = objects.add(
        stream(
            "<< /Length 18 /Filter /FlateDecode >>",
            b"not-a-flate-stream",
        )
    )
    second_fragment = add_content(
        objects,
        b"0.1 0.3 0.85 rg 205 35 130 170 re f "
        b"0.1 0.65 0.25 RG 8 w 30 120 m 330 120 l S",
    )
    page(
        objects,
        page_refs[2],
        pages,
        f"[{first_fragment} 0 R {invalid_fragment} 0 R 42 {second_fragment} 0 R]",
    )

    recovered_length = add_content(
        objects,
        b"0.96 g 0 0 360 240 re f "
        b"0.15 0.65 0.35 rg 35 40 120 160 re f "
        b"0.85 0.45 0.1 rg 205 40 120 160 re f",
        declared_length=5,
    )
    page(objects, page_refs[3], pages, f"{recovered_length} 0 R")

    joins = add_content(
        objects,
        b"\n".join(
            [
                b"0.97 g 0 0 360 240 re f",
                b"0 G 16 w 0 J",
                b"0 j 5 M 30 35 m 80 195 l 130 35 l S",
                b"0.1 0.35 0.8 RG 1 j 175 35 m 225 195 l 275 35 l S",
                b"0.75 0.15 0.1 RG 2 j 230 35 m 280 195 l 330 35 l S",
            ]
        ),
    )
    page(objects, page_refs[4], pages, f"{joins} 0 R")

    objects.set(catalog, f"<< /Type /Catalog /Pages {pages} 0 R >>".encode("ascii"))
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Kids [{page_refs[0]} 0 R 999 0 R "
            f"{page_refs[1]} 0 R {page_refs[2]} 0 R {cycle} 0 R "
            f"{page_refs[3]} 0 R {page_refs[4]} 0 R] /Count 7 >>"
        ).encode("ascii"),
    )
    objects.set(
        cycle,
        (
            f"<< /Type /Pages /Parent {pages} 0 R /Kids [{cycle} 0 R] "
            "/Count 1 >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    data = corpus()
    pdf = ROOT / "robustness-beta2.pdf"
    pdf.write_bytes(data)
    (ROOT / "robustness-beta2-fixture.json").write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "line-caps-and-dotted-strokes",
                    "continuous-and-odd-dash-patterns",
                    "partially-damaged-content-array",
                    "recovered-stream-length",
                    "miter-round-and-bevel-joins",
                ],
                "managed_png_sha256": [
                    "0ef1ba1189d2ee5556594b9e74b2ac087a0ef663de5d7d1e3c18aab12c233ad1",
                    "b9c85729a4a0cd3f242765221a8f2a8f036a2fb2bfa1012d570f05970125818d",
                    "68ea4e8840c19625bbce95a1e7f31b2b072dcb875206af7f85b6186041cae22f",
                    "a9a5984fa72cf5733efc2186eed9ca840571de33482a6b0df49739686e5fdeaf",
                    "87a9a6bd772e671de8584659db2d8a86d91e8587dc501a2302f47ca873660e49",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
