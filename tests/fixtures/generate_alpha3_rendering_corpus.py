#!/usr/bin/env python3
"""Generate the deterministic rendering corpus introduced in 0.8.0-alpha.3."""

from __future__ import annotations

import base64
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
            "/ID [<08000308000308000308000308000308> "
            "<08000308000308000308000308000308>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def filtered_inline_content() -> bytes:
    # The first two encoded streams deliberately contain whitespace-delimited
    # "EI" bytes before their real filter terminators. The JPEG contains the
    # same token in a COM segment before its EOI marker.
    ascii85 = b" EI ~>"
    run_length = bytes(
        [
            8,
            255,
            0,
            0,
            32,
            69,
            73,
            32,
            0,
            255,
            128,
        ]
    )
    jpeg = base64.b64decode(
        "/9j//gAGIEVJIP/gABBKRklGAAEBAAABAAEAAP/bAEMAAwICAwICAwMDAwQDAwQFCAUFBAQFCgcH"
        "BggMCgwMCwoLCw0OEhANDhEOCwsQFhARExQVFRUMDxcYFhQYEhQVFP/bAEMBAwQEBQQFCQUFCRQN"
        "Cw0UFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFP/AABEI"
        "AAEAAQMBIgACEQEDEQH/xAAfAAABBQEBAQEBAQAAAAAAAAAAAQIDBAUGBwgJCgv/xAC1EAACAQMD"
        "AgQDBQUEBAAAAX0BAgMABBEFEiExQQYTUWEHInEUMoGRoQgjQrHBFVLR8CQzYnKCCQoWFxgZGiUm"
        "JygpKjQ1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoOEhYaHiImKkpOUlZaX"
        "mJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4eLj5OXm5+jp6vHy8/T19vf4"
        "+fr/xAAfAQADAQEBAQEBAQEBAAAAAAAAAQIDBAUGBwgJCgv/xAC1EQACAQIEBAMEBwUEBAABAncA"
        "AQIDEQQFITEGEkFRB2FxEyIygQgUQpGhscEJIzNS8BVictEKFiQ04SXxFxgZGiYnKCkqNTY3ODk6"
        "Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqCg4SFhoeIiYqSk5SVlpeYmZqio6Slpqeo"
        "qaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2dri4+Tl5ufo6ery8/T19vf4+fr/2gAMAwEAAhED"
        "EQA/APnSiiivww/1TP/Z"
    )
    return b"\n".join(
        [
            (
                b"q 80 0 0 80 10 10 cm "
                b"BI /W 1 /H 1 /BPC 8 /CS /G /F /A85 ID "
                + ascii85
                + b" EI Q"
            ),
            (
                b"q 80 0 0 80 100 10 cm "
                b"BI /W 3 /H 1 /BPC 8 /CS /RGB /F /RL ID "
                + run_length
                + b" EI Q"
            ),
            (
                b"q 80 0 0 80 190 10 cm "
                b"BI /W 1 /H 1 /BPC 8 /CS /RGB /F /DCT ID "
                + jpeg
                + b" EI Q"
            ),
        ]
    )


def corpus_pdf() -> bytes:
    filtered = filtered_inline_content()
    transfer_page = b"q /Mask gs 1 0 0 rg 0 0 100 100 re f Q"
    mask_group = b"q /Half gs 0 g 0 0 100 100 re f Q"
    clipped_box = b"0 0 1 rg -20 -10 160 130 re f"
    default_box = b"0 1 0 rg 0 0 100 100 re f"
    return build(
        [
            b"<< /Type /Catalog /Pages 2 0 R >>",
            b"<< /Type /Pages /Kids [3 0 R 5 0 R 10 0 R 12 0 R] /Count 4 >>",
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 280 100] "
                b"/Resources << >> /Contents 4 0 R >>"
            ),
            stream(f"<< /Length {len(filtered)} >>", filtered),
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] "
                b"/Resources << /ExtGState << /Mask 7 0 R >> >> "
                b"/Contents 6 0 R >>"
            ),
            stream(f"<< /Length {len(transfer_page)} >>", transfer_page),
            (
                b"<< /Type /ExtGState "
                b"/SMask << /S /Alpha /G 8 0 R /TR 9 0 R >> >>"
            ),
            stream(
                (
                    b"<< /Type /XObject /Subtype /Form /FormType 1 "
                    b"/BBox [0 0 100 100] "
                    b"/Group << /S /Transparency /I true >> "
                    b"/Resources << /ExtGState << /Half 11 0 R >> >> "
                    + f"/Length {len(mask_group)} >>".encode("ascii")
                ).decode("ascii"),
                mask_group,
            ),
            b"<< /FunctionType 2 /Domain [0 1] /Range [0 1] /C0 [0] /C1 [1] /N 2 >>",
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [100 80 0 0] "
                b"/CropBox [140 120 -20 -10] /BleedBox [120 100 -40 -30] "
                b"/Resources << >> /Contents 13 0 R >>"
            ),
            b"<< /Type /ExtGState /ca 0.5 /CA 0.5 >>",
            (
                b"<< /Type /Page /Parent 2 0 R "
                b"/Resources << >> /Contents 14 0 R >>"
            ),
            stream(f"<< /Length {len(clipped_box)} >>", clipped_box),
            stream(f"<< /Length {len(default_box)} >>", default_box),
        ]
    )


def main() -> None:
    path = ROOT / "rendering-alpha3.pdf"
    data = corpus_pdf()
    path.write_bytes(data)
    (ROOT / "rendering-alpha3-fixture.json").write_text(
        json.dumps(
            {
                "file": path.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "filter-aware-inline-image-boundaries",
                    "soft-mask-transfer-function",
                    "page-box-clipping",
                    "missing-media-box-fallback",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
