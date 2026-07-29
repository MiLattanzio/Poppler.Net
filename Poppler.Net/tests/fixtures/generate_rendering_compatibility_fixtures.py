#!/usr/bin/env python3
"""Generate the deterministic rendering-compatibility corpus for release 0.8."""

from __future__ import annotations

import hashlib
import json
import zlib
from pathlib import Path

from generate_font_fixtures import subset_font


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
            "/ID [<08080808080808080808080808080808> "
            "<08080808080808080808080808080808>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def compatibility_pdf() -> bytes:
    inline_samples = bytes([255, 0, 0, 0, 255, 0])
    content = b"\n".join(
        [
            b"0.95 g 0 0 400 300 re f",
            b"1 0 0 rg 20 160 160 80 re f",
            b"0 g BT /FBase 48 Tf 40 180 Td (ABC) Tj ET",
            b"0 0 1 rg 100 160 80 80 re f",
            (
                b"q 80 0 0 50 220 180 cm "
                b"BI /W 2 /H 1 /BPC 8 /CS /RGB ID "
                + inline_samples
                + b"\nEI Q"
            ),
            b"BT /FType3 60 Tf 40 60 Td (A) Tj ET",
        ]
    )
    char_proc = (
        b"700 0 0 0 700 700 d1 "
        b"0 0.7 0 rg 0 0 m 700 0 l 350 700 l h f"
    )
    return build(
        [
            b"<< /Type /Catalog /Pages 2 0 R >>",
            b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] "
                b"/Resources << /Font << /FBase 5 0 R /FType3 6 0 R >> >> "
                b"/Contents 4 0 R >>"
            ),
            stream(f"<< /Length {len(content)} >>", content),
            (
                b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica "
                b"/Encoding /WinAnsiEncoding >>"
            ),
            (
                b"<< /Type /Font /Subtype /Type3 /Name /FType3 "
                b"/FontBBox [0 0 700 700] "
                b"/FontMatrix [0.001 0 0 0.001 0 0] "
                b"/CharProcs << /A 7 0 R >> "
                b"/Encoding << /Type /Encoding /Differences [65 /A] >> "
                b"/FirstChar 65 /LastChar 65 /Widths [700] "
                b"/Resources << >> >>"
            ),
            stream(f"<< /Length {len(char_proc)} >>", char_proc),
        ]
    )


def type1_pdf(program: bytes) -> bytes:
    compressed = zlib.compress(program, 9)
    content = b"\n".join(
        [
            b"0 g BT /F1 72 Tf 30 190 Td 0 Tr (ABC) Tj ET",
            b"BT /F1 90 Tf 50 40 Td 7 Tr (A) Tj ET",
            b"0 0 1 rg 0 0 400 130 re f",
        ]
    )
    return build(
        [
            b"<< /Type /Catalog /Pages 2 0 R >>",
            b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 280] "
                b"/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"
            ),
            stream(f"<< /Length {len(content)} >>", content),
            (
                b"<< /Type /Font /Subtype /Type1 /BaseFont /NimbusSans-Regular "
                b"/FirstChar 65 /LastChar 67 /Widths [667 667 722] "
                b"/Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>"
            ),
            (
                b"<< /Type /FontDescriptor /FontName /NimbusSans-Regular "
                b"/Flags 32 /FontBBox [-210 -299 1032 1075] /ItalicAngle 0 "
                b"/Ascent 729 /Descent -219 /CapHeight 729 /StemV 80 "
                b"/FontFile 7 0 R >>"
            ),
            stream(
                (
                    f"<< /Length {len(compressed)} /Filter /FlateDecode "
                    f"/Length1 {len(program)} >>"
                ),
                compressed,
            ),
        ]
    )


def write_fixture(name: str, data: bytes) -> dict[str, str]:
    (ROOT / name).write_bytes(data)
    return {"file": name, "sha256": hashlib.sha256(data).hexdigest()}


def main() -> None:
    font_bytes, _, _, _ = subset_font(
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
        text="ABCimW",
    )
    substitute = ROOT / "Helvetica.ttf"
    substitute.write_bytes(font_bytes)
    type1_program = Path(
        "/usr/share/fonts/X11/Type1/NimbusSans-Regular.pfb"
    ).read_bytes()
    fixtures = [
        write_fixture("rendering-compatibility.pdf", compatibility_pdf()),
        write_fixture("type1-rendering.pdf", type1_pdf(type1_program)),
        {
            "file": substitute.name,
            "sha256": hashlib.sha256(font_bytes).hexdigest(),
        },
    ]
    (ROOT / "rendering-compatibility-fixtures.json").write_text(
        json.dumps(
            {
                "fixtures": fixtures,
                "features": [
                    "ordered-text-display-list",
                    "base14-managed-substitution",
                    "inline-image",
                    "cff1-type2-charstrings",
                    "type1-charstrings",
                    "type3-charprocs",
                    "text-rendering-mode",
                    "text-clipping",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
