#!/usr/bin/env python3
"""Regenerate the tiny embedded sfnt fixtures used by the 0.4 font tests."""

from __future__ import annotations

import io
import struct
import zlib
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._c_m_a_p import CmapSubtable


ROOT = Path(__file__).resolve().parent
TEXT = "ABC"


def subset_font(source: Path) -> tuple[bytes, list[int], list[int], str]:
    font = TTFont(source)
    options = subset.Options()
    options.retain_gids = True
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.name_languages = ["*"]
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(text=TEXT)
    subsetter.subset(font)
    font.recalcTimestamp = False
    font["head"].created = 2082844800
    font["head"].modified = 2082844800

    cmap = font.getBestCmap()
    glyph_ids = [font.getGlyphID(cmap[ord(character)]) for character in TEXT]
    units_per_em = font["head"].unitsPerEm
    widths = [
        round(font["hmtx"].metrics[cmap[ord(character)]][0] * 1000 / units_per_em)
        for character in TEXT
    ]
    postscript_name = "FixtureFont"
    for record in font["name"].names:
        if record.nameID == 6:
            try:
                postscript_name = record.toUnicode()
                break
            except UnicodeDecodeError:
                pass

    output = io.BytesIO()
    font.save(output)
    return output.getvalue(), glyph_ids, widths, postscript_name


def subset_format_zero_font(source: Path) -> tuple[bytes, list[int], str]:
    font = TTFont(source)
    options = subset.Options()
    options.retain_gids = True
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.name_languages = ["*"]
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(text=TEXT)
    subsetter.subset(font)
    font.recalcTimestamp = False
    font["head"].created = 2082844800
    font["head"].modified = 2082844800

    unicode_cmap = font.getBestCmap()
    glyph_names = [unicode_cmap[ord(character)] for character in TEXT]
    units_per_em = font["head"].unitsPerEm
    widths = [
        round(font["hmtx"].metrics[glyph_name][0] * 1000 / units_per_em)
        for glyph_name in glyph_names
    ]
    postscript_name = "FixtureFont"
    for record in font["name"].names:
        if record.nameID == 6:
            try:
                postscript_name = record.toUnicode()
                break
            except UnicodeDecodeError:
                pass

    cmap = newTable("cmap")
    cmap.tableVersion = 0
    format_zero = CmapSubtable.newSubtable(0)
    format_zero.platformID = 1
    format_zero.platEncID = 0
    format_zero.language = 0
    format_zero.cmap = {
        code: glyph_name
        for code, glyph_name in zip((1, 2, 3), glyph_names)
    }
    cmap.tables = [format_zero]
    font["cmap"] = cmap

    output = io.BytesIO()
    font.save(output)
    return output.getvalue(), widths, postscript_name


def stream(dictionary: str, payload: bytes) -> bytes:
    return (
        dictionary.encode("ascii")
        + b"\nstream\n"
        + payload
        + b"\nendstream"
    )


def build_pdf(
    font_bytes: bytes,
    glyph_ids: list[int],
    widths: list[int],
    postscript_name: str,
    cff: bool,
) -> bytes:
    character_codes = [ord(character) for character in TEXT]
    content_hex = "".join(f"{code:04X}" for code in character_codes)
    content = f"BT /F0 24 Tf 72 700 Td <{content_hex}> Tj ET".encode("ascii")
    width_array = " ".join(
        f"{code} [{width}]" for code, width in zip(character_codes, widths)
    )
    cid_to_gid = bytearray((max(character_codes) + 1) * 2)
    for code, glyph_id in zip(character_codes, glyph_ids):
        struct.pack_into(">H", cid_to_gid, code * 2, glyph_id)
    compressed_font = zlib.compress(font_bytes, level=9)
    descendant_subtype = "CIDFontType0" if cff else "CIDFontType2"
    font_file_key = "FontFile3" if cff else "FontFile2"
    font_stream_subtype = " /Subtype /OpenType" if cff else ""
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F0 5 0 R >> >> /Contents 4 0 R >>"
        ),
        stream(f"<< /Length {len(content)} >>", content),
        (
            f"<< /Type /Font /Subtype /Type0 /BaseFont /{postscript_name} "
            f"/Encoding /Identity-H /DescendantFonts [6 0 R] >>"
        ).encode("ascii"),
        (
            f"<< /Type /Font /Subtype /{descendant_subtype} "
            f"/BaseFont /{postscript_name} "
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            f"/FontDescriptor 7 0 R /CIDToGIDMap 9 0 R /DW 1000 /W [{width_array}] >>"
        ).encode("ascii"),
        (
            f"<< /Type /FontDescriptor /FontName /{postscript_name} /Flags 4 "
            "/FontBBox [-1024 -1024 4096 4096] /ItalicAngle 0 /Ascent 800 "
            f"/Descent -200 /CapHeight 700 /StemV 80 /{font_file_key} 8 0 R >>"
        ).encode("ascii"),
        stream(
            f"<< /Length {len(compressed_font)} /Filter /FlateDecode "
            f"/Length1 {len(font_bytes)}{font_stream_subtype} >>",
            compressed_font,
        ),
        stream(f"<< /Length {len(cid_to_gid)} >>", bytes(cid_to_gid)),
    ]

    output = io.BytesIO()
    output.write(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for number, obj in enumerate(objects, 1):
        offsets.append(output.tell())
        output.write(f"{number} 0 obj\n".encode("ascii"))
        output.write(obj)
        output.write(b"\nendobj\n")
    xref = output.tell()
    output.write(f"xref\n0 {len(objects) + 1}\n".encode("ascii"))
    output.write(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        output.write(f"{offset:010d} 00000 n \n".encode("ascii"))
    output.write(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R "
            "/ID [<11111111111111111111111111111111> "
            "<22222222222222222222222222222222>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return output.getvalue()


def build_format_zero_pdf(
    font_bytes: bytes,
    widths: list[int],
    postscript_name: str,
) -> bytes:
    content = b"1 0.6627 0.2275 rg BT /F0 24 Tf 72 700 Td <010203> Tj ET"
    to_unicode = (
        b"1 begincodespacerange <00> <FF> endcodespacerange\n"
        b"3 beginbfchar <01> <0041> <02> <0042> <03> <0043> endbfchar"
    )
    compressed_font = zlib.compress(font_bytes, level=9)
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F0 5 0 R >> >> /Contents 4 0 R >>"
        ),
        stream(f"<< /Length {len(content)} >>", content),
        (
            f"<< /Type /Font /Subtype /TrueType "
            f"/BaseFont /ABCDEF+{postscript_name} "
            f"/FirstChar 1 /LastChar 3 /Widths [{' '.join(map(str, widths))}] "
            "/FontDescriptor 6 0 R /ToUnicode 8 0 R >>"
        ).encode("ascii"),
        (
            f"<< /Type /FontDescriptor /FontName /ABCDEF+{postscript_name} "
            "/Flags 4 /FontBBox [-1024 -1024 4096 4096] /ItalicAngle 0 "
            "/Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 "
            "/FontFile2 7 0 R >>"
        ).encode("ascii"),
        stream(
            f"<< /Length {len(compressed_font)} /Filter /FlateDecode "
            f"/Length1 {len(font_bytes)} >>",
            compressed_font,
        ),
        stream(f"<< /Length {len(to_unicode)} >>", to_unicode),
    ]

    output = io.BytesIO()
    output.write(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for number, obj in enumerate(objects, 1):
        offsets.append(output.tell())
        output.write(f"{number} 0 obj\n".encode("ascii"))
        output.write(obj)
        output.write(b"\nendobj\n")
    xref = output.tell()
    output.write(f"xref\n0 {len(objects) + 1}\n".encode("ascii"))
    output.write(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        output.write(f"{offset:010d} 00000 n \n".encode("ascii"))
    output.write(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R "
            "/ID [<33333333333333333333333333333333> "
            "<44444444444444444444444444444444>] >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return output.getvalue()


def generate(source: Path, destination: str, cff: bool) -> None:
    font_bytes, glyph_ids, widths, name = subset_font(source)
    (ROOT / destination).write_bytes(
        build_pdf(font_bytes, glyph_ids, widths, f"ABCDEF+{name}", cff)
    )


if __name__ == "__main__":
    generate(
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
        "truetype-cmap-fallback.pdf",
        cff=False,
    )
    generate(
        Path("/usr/share/fonts/opentype/urw-base35/NimbusSans-Regular.otf"),
        "opentype-cff-cmap-fallback.pdf",
        cff=True,
    )
    format_zero_bytes, format_zero_widths, format_zero_name = (
        subset_format_zero_font(
            Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
        )
    )
    (ROOT / "truetype-format0-subset.pdf").write_bytes(
        build_format_zero_pdf(
            format_zero_bytes,
            format_zero_widths,
            format_zero_name,
        )
    )
