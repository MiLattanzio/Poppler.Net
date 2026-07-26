#!/usr/bin/env python3
"""Generate the deterministic vector-graphics fixture used by release 0.5."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


def stream(dictionary: str, data: bytes) -> bytes:
    return (
        dictionary.encode("ascii")
        + b"\nstream\n"
        + data
        + b"\nendstream"
    )


def build(objects: list[bytes]) -> bytes:
    output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for number, value in enumerate(objects, 1):
        offsets.append(len(output))
        output.extend(f"{number} 0 obj\n".encode("ascii"))
        output.extend(value)
        output.extend(b"\nendobj\n")
    xref = len(output)
    output.extend(f"xref\n0 {len(objects) + 1}\n".encode("ascii"))
    output.extend(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        output.extend(f"{offset:010d} 00000 n \n".encode("ascii"))
    output.extend(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R "
            "/ID [<05050505050505050505050505050505> "
            "<05050505050505050505050505050505>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def main() -> None:
    content = b"\n".join(
        [
            b"q 1 0 0 1 10 20 cm 2 w 1 J 2 j [4 2] 1 d /GS1 gs",
            b"1 0 0 RG 0 1 0 rg 10 10 m 110 10 l 110 60 l h B Q",
            b"q 20 80 100 40 re W n 0 0 1 rg 0 70 200 70 re f Q",
            b"q 1 0 0 1 200 100 cm /Fm1 Do Q",
            b"q 50 0 0 40 300 100 cm /Im1 Do Q",
            b"/Pattern cs /P1 scn 50 200 150 80 re f",
            b"/Pattern cs /P2 scn 250 200 150 80 re f",
            b"q 0 300 400 80 re W n /Sh1 sh Q",
        ]
    )
    form = b"\n".join(
        [
            b"0 0 1 rg 0 0 50 50 re f",
            b"1 1 1 RG 2 w 5 5 m 10 45 40 45 45 5 c S",
        ]
    )
    tiling = b"\n".join(
        [
            b"1 1 0 rg 0 0 5 10 re f",
            b"1 0 1 RG 1 w 0 0 m 10 10 l S",
        ]
    )
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 400] "
            b"/Resources << "
            b"/ExtGState << /GS1 6 0 R >> "
            b"/XObject << /Fm1 7 0 R /Im1 8 0 R >> "
            b"/Pattern << /P1 9 0 R /P2 10 0 R >> "
            b"/Shading << /Sh1 11 0 R >> "
            b">> /Contents 4 0 R >>"
        ),
        stream(f"<< /Length {len(content)} >>", content),
        b"<< /Producer (Poppler.Net graphics fixture) >>",
        b"<< /Type /ExtGState /LW 3 /LC 1 /LJ 2 /ML 12 "
        b"/CA 0.75 /ca 0.5 /BM /Normal >>",
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox [0 0 50 50] "
                f"/Matrix [2 0 0 2 0 0] /Resources << >> "
                f"/Length {len(form)} >>"
            ),
            form,
        ),
        stream(
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 "
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>",
            b"\xff\x80\x00",
        ),
        stream(
            (
                f"<< /Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 "
                f"/BBox [0 0 10 10] /XStep 10 /YStep 10 "
                f"/Matrix [1 0 0 1 0 0] /Resources << >> "
                f"/Length {len(tiling)} >>"
            ),
            tiling,
        ),
        b"<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 250 200] "
        b"/Shading 12 0 R >>",
        b"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 300 400 300] "
        b"/Function 13 0 R /Extend [true true] >>",
        b"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [0 0 0 75 40 90] "
        b"/Function 14 0 R /Extend [true true] >>",
        b"<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>",
        b"<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>",
    ]
    data = build(objects)
    directory = Path(__file__).resolve().parent
    pdf = directory / "graphics-engine.pdf"
    manifest = directory / "graphics-fixture.json"
    pdf.write_bytes(data)
    manifest.write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "features": [
                    "paths",
                    "clipping",
                    "ExtGState",
                    "form-xobject",
                    "image-xobject-metadata",
                    "tiling-pattern",
                    "axial-shading",
                    "radial-shading-pattern",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
