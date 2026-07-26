#!/usr/bin/env python3
"""Generate the deterministic transparency/raster fixture used by release 0.7."""

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
            "/ID [<07070707070707070707070707070707> "
            "<07070707070707070707070707070707>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def main() -> None:
    page_content = b"\n".join(
        [
            b"1 0 0 rg 20 140 100 70 re f",
            b"q /Multiply gs 0 0 1 rg 70 160 100 40 re f Q",
            b"q 1 0 0 1 160 140 cm /Masked gs 0 0.75 0 rg 0 0 100 80 re f Q",
            b"q 1 0 0 1 20 30 cm /Group Do Q",
            b"q 1 0 0 1 150 30 cm /MaskedAlpha gs 0.7 0 0.7 rg 0 0 50 80 re f Q",
            b"q 220 30 80 80 re W n /Half gs 1 0.5 0 rg 200 10 120 120 re f Q",
        ]
    )
    group_content = b"\n".join(
        [
            b"1 0 0 rg 0 0 80 80 re f",
            b"/Half gs 0 0 1 rg 40 0 80 80 re f",
        ]
    )
    mask_content = b"/MaskGradient sh"
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R 16 0 R] /Count 2 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 320 240] "
            b"/Resources << "
            b"/ExtGState << /Multiply 6 0 R /Half 7 0 R /Masked 8 0 R "
            b"/MaskedAlpha 13 0 R >> "
            b"/XObject << /Group 9 0 R >> "
            b">> /Contents 4 0 R >>"
        ),
        stream(f"<< /Length {len(page_content)} >>", page_content),
        b"<< /Producer (Poppler.Net rendering fixture) >>",
        b"<< /Type /ExtGState /BM /Multiply /ca 0.75 /CA 0.75 >>",
        b"<< /Type /ExtGState /ca 0.5 /CA 0.5 >>",
        b"<< /Type /ExtGState /SMask << /S /Luminosity /G 10 0 R /BC [0] >> >>",
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox [0 0 120 80] "
                f"/Group << /S /Transparency /I true /K false >> "
                f"/Resources << /ExtGState << /Half 7 0 R >> >> "
                f"/Length {len(group_content)} >>"
            ),
            group_content,
        ),
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox [0 0 100 80] "
                f"/Group << /S /Transparency /I true /K false >> "
                f"/Resources << /Shading << /MaskGradient 11 0 R >> >> "
                f"/Length {len(mask_content)} >>"
            ),
            mask_content,
        ),
        b"<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 100 0] "
        b"/Function 12 0 R /Extend [true true] >>",
        b"<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>",
        b"<< /Type /ExtGState /SMask << /S /Alpha /G 14 0 R >> >>",
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox [0 0 100 80] "
                f"/Group << /S /Transparency /I true /K false >> "
                f"/Resources << /ExtGState << /Quarter 15 0 R >> >> "
                f"/Length {len(b'/Quarter gs 0 g 0 0 100 80 re f')} >>"
            ),
            b"/Quarter gs 0 g 0 0 100 80 re f",
        ),
        b"<< /Type /ExtGState /ca 0.25 /CA 0.25 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 50] /Rotate 90 "
        b"/Resources << >> /Contents 17 0 R >>",
        stream(
            f"<< /Length {len(b'1 0 0 rg 0 0 40 20 re f')} >>",
            b"1 0 0 rg 0 0 40 20 re f",
        ),
    ]
    data = build(objects)
    directory = Path(__file__).resolve().parent
    pdf = directory / "rendering-transparency.pdf"
    manifest = directory / "rendering-fixture.json"
    pdf.write_bytes(data)
    manifest.write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "width_at_72_dpi": 320,
                "height_at_72_dpi": 240,
                "features": [
                    "multiply-blend",
                    "constant-alpha",
                    "luminosity-soft-mask",
                    "alpha-soft-mask",
                    "isolated-transparency-group",
                    "clipping",
                    "antialiasing",
                    "page-rotation",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
