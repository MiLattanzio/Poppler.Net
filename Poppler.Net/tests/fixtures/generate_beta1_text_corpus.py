#!/usr/bin/env python3
"""Generate the deterministic font/text corpus introduced in 0.8.0-beta.1."""

from __future__ import annotations

import hashlib
import json
import zlib
from pathlib import Path

from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.fontBuilder import FontBuilder
from fontTools.misc.psCharStrings import T2CharString
from fontTools.pens.t2CharStringPen import T2CharStringPen


ROOT = Path(__file__).resolve().parent
FONT_ROOT = ROOT / "beta-fonts"
CMAP_ROOT = ROOT / "cmaps"
FIXED_FONT_TIME = 3406620153


def stream(dictionary: str, payload: bytes) -> bytes:
    return (
        dictionary.encode("ascii")
        + b"\nstream\n"
        + payload
        + b"\nendstream"
    )


def build_pdf(objects: list[bytes]) -> bytes:
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
            "/ID [<08000100080001000800010008000100> "
            "<08000100080001000800010008000100>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def rectangle_charstring(
    left: int,
    bottom: int,
    right: int,
    top: int,
) -> T2CharString:
    pen = T2CharStringPen(None, None, CFF2=True)
    pen.moveTo((left, bottom))
    pen.lineTo((right, bottom))
    pen.lineTo((right, top))
    pen.lineTo((left, top))
    pen.closePath()
    return pen.getCharString()


def build_cff2_font(
    family: str,
    *,
    narrow: bool = False,
) -> bytes:
    glyph_order = [".notdef", "A", "B", "A.vert", "f", "i", "fi"]
    builder = FontBuilder(1000, isTTF=False)
    builder.setupGlyphOrder(glyph_order)
    builder.setupCharacterMap(
        {65: "A", 66: "B", 102: "f", 105: "i", 0xFB01: "fi"}
    )
    builder.setupHorizontalMetrics(
        {glyph: (600, 0) for glyph in glyph_order}
    )
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable(
        {
            "familyName": family,
            "styleName": "Regular",
            "uniqueFontIdentifier": f"{family}-Regular",
            "fullName": f"{family} Regular",
            "psName": family.replace(" ", "") + "-Regular",
        }
    )
    builder.setupOS2(
        sTypoAscender=800,
        sTypoDescender=-200,
        usWinAscent=800,
        usWinDescent=200,
        usWidthClass=3 if narrow else 5,
    )
    builder.setupPost()
    empty = T2CharStringPen(None, None, CFF2=True).getCharString()
    a_left, a_right = (250, 350) if narrow else (80, 520)
    charstrings = {
        ".notdef": empty,
        "A": rectangle_charstring(a_left, 0, a_right, 700),
        # Deliberately uses the escaped Type 2 add operator before rmoveto.
        "B": T2CharString(
            program=[
                40,
                40,
                "add",
                1,
                "blend",
                0,
                "rmoveto",
                300,
                0,
                "rlineto",
                0,
                700,
                "rlineto",
                -300,
                0,
                "rlineto",
            ]
        ),
        "A.vert": rectangle_charstring(300, 0, 600, 700),
        "f": rectangle_charstring(50, 0, 180, 700),
        "i": rectangle_charstring(250, 0, 330, 700),
        "fi": rectangle_charstring(50, 500, 520, 700),
    }
    builder.setupCFF2(charstrings)
    builder.setupMaxp()
    addOpenTypeFeaturesFromString(
        builder.font,
        """
        feature vert { sub A by A.vert; } vert;
        feature vrt2 { sub A by A.vert; } vrt2;
        feature liga { sub f i by fi; } liga;
        """,
    )
    builder.font["head"].created = FIXED_FONT_TIME
    builder.font["head"].modified = FIXED_FONT_TIME
    builder.font.recalcBBoxes = False
    builder.font.recalcTimestamp = False
    from io import BytesIO

    output = BytesIO()
    builder.font.save(output, reorderTables=True)
    return output.getvalue()


def corpus_pdf(font: bytes) -> bytes:
    compressed_font = zlib.compress(font, 9)
    horizontal = b"0 g BT /F1 72 Tf 20 30 Td <4142> Tj ET"
    vertical = b"0 g BT /F0 72 Tf 90 250 Td <00010002> Tj ET"
    ligature = b"0 g BT /FSub 72 Tf 30 40 Td <41> Tj ET"
    narrow = b"0 g BT /FNarrow 72 Tf 30 40 Td <41> Tj ET"
    to_unicode_vertical = b"\n"
    to_unicode_ligature = (
        b"1 begincodespacerange <00> <FF> endcodespacerange\n"
        b"1 beginbfchar <41> <00660069> endbfchar"
    )
    return build_pdf(
        [
            b"<< /Type /Catalog /Pages 2 0 R >>",
            b"<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R 9 0 R] /Count 4 >>",
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 180 140] "
                b"/Resources << /Font << /F1 11 0 R >> >> /Contents 4 0 R >>"
            ),
            stream(f"<< /Length {len(horizontal)} >>", horizontal),
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 180 300] "
                b"/Resources << /Font << /F0 12 0 R >> >> /Contents 6 0 R >>"
            ),
            stream(f"<< /Length {len(vertical)} >>", vertical),
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 180 140] "
                b"/Resources << /Font << /FSub 16 0 R >> >> /Contents 8 0 R >>"
            ),
            stream(f"<< /Length {len(ligature)} >>", ligature),
            (
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 180 140] "
                b"/Resources << /Font << /FNarrow 18 0 R >> >> /Contents 10 0 R >>"
            ),
            stream(f"<< /Length {len(narrow)} >>", narrow),
            (
                b"<< /Type /Font /Subtype /Type1 /BaseFont /PopplerNetBetaCFF2 "
                b"/FirstChar 65 /LastChar 66 /Widths [600 600] "
                b"/Encoding /WinAnsiEncoding /FontDescriptor 13 0 R >>"
            ),
            (
                b"<< /Type /Font /Subtype /Type0 /BaseFont /PopplerNetBetaCFF2 "
                b"/Encoding /BetaBase-V /DescendantFonts [14 0 R] "
                b"/ToUnicode 15 0 R >>"
            ),
            (
                b"<< /Type /FontDescriptor /FontName /PopplerNetBetaCFF2 "
                b"/Flags 32 /FontBBox [0 0 600 700] /ItalicAngle 0 "
                b"/Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 "
                b"/FontFile3 19 0 R >>"
            ),
            (
                b"<< /Type /Font /Subtype /CIDFontType0 "
                b"/BaseFont /PopplerNetBetaCFF2 "
                b"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) "
                b"/Supplement 0 >> /FontDescriptor 13 0 R "
                b"/DW 600 /W [1 [600 600]] /DW2 [880 -1000] "
                b"/W2 [1 [-1000 300 880 -1000 300 880]] >>"
            ),
            stream(
                (
                    f"<< /Length {len(to_unicode_vertical)} "
                    "/UseCMap /BetaUnicode >>"
                ),
                to_unicode_vertical,
            ),
            (
                b"<< /Type /Font /Subtype /Type1 /BaseFont /LigatureSans "
                b"/FirstChar 65 /LastChar 65 /Widths [600] "
                b"/Encoding << /Differences [65 /fi] >> "
                b"/ToUnicode 17 0 R >>"
            ),
            stream(
                f"<< /Length {len(to_unicode_ligature)} >>",
                to_unicode_ligature,
            ),
            (
                b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Narrow "
                b"/FirstChar 65 /LastChar 65 /Widths [500] "
                b"/Encoding /WinAnsiEncoding >>"
            ),
            stream(
                (
                    f"<< /Length {len(compressed_font)} /Filter /FlateDecode "
                    "/Subtype /OpenType >>"
                ),
                compressed_font,
            ),
        ]
    )


def main() -> None:
    FONT_ROOT.mkdir(exist_ok=True)
    CMAP_ROOT.mkdir(exist_ok=True)
    embedded = build_cff2_font("PopplerNet Beta CFF2")
    ligature = build_cff2_font("Ligature Sans")
    wide = build_cff2_font("Helvetica")
    narrow = build_cff2_font("Helvetica Narrow", narrow=True)
    (FONT_ROOT / "LigatureSans.otf").write_bytes(ligature)
    (FONT_ROOT / "Helvetica.otf").write_bytes(wide)
    (FONT_ROOT / "HelveticaNarrow.otf").write_bytes(narrow)
    pdf = corpus_pdf(embedded)
    path = ROOT / "rendering-beta1.pdf"
    path.write_bytes(pdf)
    files = [path, *sorted(FONT_ROOT.glob("*.otf")), *sorted(CMAP_ROOT.iterdir())]
    (ROOT / "rendering-beta1-fixture.json").write_text(
        json.dumps(
            {
                "file": path.name,
                "sha256": hashlib.sha256(pdf).hexdigest(),
                "files": [
                    {
                        "file": str(item.relative_to(ROOT)),
                        "sha256": hashlib.sha256(item.read_bytes()).hexdigest(),
                    }
                    for item in files
                ],
                "features": [
                    "cff2-charstrings",
                    "type2-arithmetic",
                    "external-cmap",
                    "usecmap-inheritance",
                    "vertical-gsub",
                    "standard-ligature-gsub",
                    "narrow-font-substitution",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
