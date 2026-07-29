#!/usr/bin/env python3
"""Generate deterministic annotation regressions for Poppler.Net 0.9.0-alpha.1."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent


def stream(dictionary: str, payload: bytes) -> bytes:
    return (
        dictionary.encode("ascii")
        + b"\nstream\n"
        + payload
        + b"\nendstream"
    )


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
                "/ID [<09000109000109000109000109000109> "
                "<09000109000109000109000109000109>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def form(objects: Objects, bbox: str, content: bytes, extra: str = "") -> int:
    return objects.add(
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox {bbox} "
                f"{extra}/Length {len(content)} >>"
            ),
            content,
        )
    )


def annotation(objects: Objects, dictionary: str) -> int:
    return objects.add(f"<< /Type /Annot {dictionary} >>".encode("ascii"))


def set_page(
    objects: Objects,
    reference: int,
    parent: int,
    content: bytes,
    annotations: list[int],
) -> None:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    annots = " ".join(f"{item} 0 R" for item in annotations)
    objects.set(
        reference,
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 320 220] "
            f"/Resources << >> /Contents {content_ref} 0 R "
            f"/Annots [{annots}] >>"
        ).encode("ascii"),
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs = [objects.reserve() for _ in range(4)]

    green_appearance = form(
        objects,
        "[0 0 120 40]",
        b"0 0.75 0 rg 0 0 120 40 re f",
        "/Resources << >> ",
    )
    magenta_appearance = form(
        objects,
        "[0 0 60 30]",
        b"1 0 1 rg 0 0 60 30 re f",
        "/Resources << >> ",
    )
    page_one_annotations = [
        annotation(
            objects,
            (
                "/Subtype /Link /Rect [20 150 140 190] /Contents (URI appearance) "
                f"/AP << /N {green_appearance} 0 R >> "
                "/A << /S /URI /URI (https://example.test/alpha1) >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Link /Rect [170 150 300 190] /C [0 0 1] "
                "/Border [0 0 2 [4 2]] "
                f"/Dest [{page_refs[1]} 0 R /XYZ 25 180 1.5]"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Link /Rect [20 95 140 130] /C [0.2 0.2 0.8] "
                "/Dest (chapter-three)"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Text /Rect [180 90 200 110] /Contents (NOTE ALPHA 1) "
                "/T (Mi Lattanzio) /Subj (Fallback icon) /Name /Comment "
                "/M (D:20260728190000+02'00') /C [1 0.82 0]"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Square /Rect [230 90 290 120] /F 34 /C [1 0 1] "
                f"/AP << /N {magenta_appearance} 0 R >>"
            ),
        ),
    ]
    set_page(
        objects,
        page_refs[0],
        pages,
        b"\n".join(
            [
                b"0.9 g 0 0 320 220 re f",
                b"0 0 1 rg 20 150 120 40 re f",
                b"0.8 g 170 150 130 40 re f",
                b"0.75 g 20 95 120 35 re f",
            ]
        ),
        page_one_annotations,
    )

    page_two_annotations = [
        annotation(
            objects,
            (
                "/Subtype /FreeText /Rect [15 150 145 205] "
                "/Contents (FALLBACK TEXT 42) /C [0 0 0] /Border [0 0 1]"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Highlight /Rect [15 105 145 130] "
                "/QuadPoints [15 130 145 130 15 105 145 105] "
                "/C [1 1 0] /CA 0.45"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Square /Rect [170 145 230 205] /C [1 0 0] "
                "/IC [1 0.8 0.8] /BS << /W 2 /S /S >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Circle /Rect [245 145 305 205] /C [0 0 1] "
                "/IC [0.8 0.8 1] /BS << /W 2 /S /S >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Line /Rect [165 95 305 135] /L [175 105 295 125] "
                "/C [0 0.6 0] /BS << /W 3 /S /S >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Polygon /Rect [165 20 240 85] "
                "/Vertices [170 25 235 25 205 80] /C [0.7 0 0.7] "
                "/IC [0.95 0.8 0.95] /BS << /W 2 /S /S >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Ink /Rect [245 20 305 85] "
                "/InkList [[250 30 270 75 295 35]] /C [0.1 0.2 0.8] "
                "/BS << /W 2 /S /D /D [3 2] >>"
            ),
        ),
    ]
    set_page(
        objects,
        page_refs[1],
        pages,
        b"\n".join(
            [
                b"1 g 0 0 320 220 re f",
                b"0.7 g 15 105 130 25 re f",
                b"0.85 g 165 20 140 65 re f",
            ]
        ),
        page_two_annotations,
    )

    rotated_appearance = form(
        objects,
        "[10 20 60 60]",
        b"\n".join(
            [
                b"1 0 0 rg 10 20 25 40 re f",
                b"0 0.6 0 rg 35 20 25 40 re f",
            ]
        ),
        "/Matrix [0 1 -1 0 80 0] /Resources << >> ",
    )
    state_appearance = form(
        objects,
        "[0 0 80 40]",
        b"0.95 0.65 0 rg 0 0 80 40 re f 0 0 0 RG 2 w 1 1 78 38 re S",
        "/Resources << >> ",
    )
    child_form = form(
        objects,
        "[0 0 50 50]",
        b"0.1 0.35 0.95 rg 0 0 50 50 re f",
        "/Resources << >> ",
    )
    nested_appearance = form(
        objects,
        "[0 0 50 50]",
        b"/Child Do",
        f"/Resources << /XObject << /Child {child_form} 0 R >> >> ",
    )
    recursive_appearance = objects.reserve()
    recursive_content = b"/Self Do"
    objects.set(
        recursive_appearance,
        stream(
            (
                "<< /Type /XObject /Subtype /Form /BBox [0 0 40 40] "
                f"/Resources << /XObject << /Self {recursive_appearance} 0 R >> >> "
                f"/Length {len(recursive_content)} >>"
            ),
            recursive_content,
        ),
    )
    page_three_annotations = [
        annotation(
            objects,
            (
                "/Subtype /Stamp /Rect [20 120 150 200] "
                f"/AP << /N {rotated_appearance} 0 R >> /Name /Approved"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Text /Rect [180 145 260 185] /AS /Comment "
                f"/AP << /N << /Comment {state_appearance} 0 R >> >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /FreeText /Rect [20 30 120 100] "
                f"/AP << /N {nested_appearance} 0 R >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Square /Rect [190 35 230 75] /C [1 0 0] "
                f"/AP << /N {recursive_appearance} 0 R >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Link /Rect [250 35 305 75] /C [0 0 1] "
                "/Dest /legacy"
            ),
        ),
    ]
    set_page(
        objects,
        page_refs[2],
        pages,
        b"0.92 g 0 0 320 220 re f",
        page_three_annotations,
    )

    set_page(
        objects,
        page_refs[3],
        pages,
        b"0.82 0.9 1 rg 0 0 320 220 re f",
        [],
    )

    destination_leaf = objects.add(
        (
            f"<< /Names [(chapter-three) [{page_refs[2]} 0 R /FitH 180] "
            f"(chapter-four) << /D [{page_refs[3]} 0 R /Fit] >> "
            f"(loop-a) /loop-b (loop-b) /loop-a] >>"
        ).encode("ascii")
    )
    destination_root = objects.add(
        f"<< /Kids [{destination_leaf} 0 R] >>".encode("ascii")
    )
    objects.set(
        catalog,
        (
            f"<< /Type /Catalog /Pages {pages} 0 R "
            f"/Dests << /legacy [{page_refs[3]} 0 R /Fit] >> "
            f"/Names << /Dests {destination_root} 0 R >> >>"
        ).encode("ascii"),
    )
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Kids "
            f"[{' '.join(f'{item} 0 R' for item in page_refs)}] "
            f"/Count {len(page_refs)} >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    data = corpus()
    pdf = ROOT / "annotations-alpha1.pdf"
    pdf.write_bytes(data)
    (ROOT / "annotations-alpha1-fixture.json").write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "links-destinations-and-visibility",
                    "managed-annotation-fallbacks",
                    "appearance-mapping-state-and-recursion",
                    "named-destination-target",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
