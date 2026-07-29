#!/usr/bin/env python3
"""Generate deterministic 0.6 image/color fixtures.

Pillow is used only by this fixture generator, never by Poppler.Net at runtime.
"""

from __future__ import annotations

import base64
import hashlib
import io
import json
import struct
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent
JBIG2_PDF_STREAM = base64.b64decode(
    "AAAAADAAAQAAABMAAABgAAAAYAAAAGAAAABgAQAAAAAAASYAAQAAAPsAAABgAAAAY"
    "AAAAAAAAAAAAAAD//3/Av7+/qU0eJn1XJxNSolaxuKNOzYqBM4UGoeUcEys/Eoe"
    "vXF9sKz7lnXleKpfpS/cOA9hJWalEvifoO0fv6nozzYcu0Z3KrqRIWToh/BBYPvz"
    "jGgLBbtel4AATnDUqXh0W/F/5/9oyLjtCUtxkDascTvCEH6O4/XLhFhIQi/7kch"
    "SLf3cFXHX+TdMgUHFB3vvgyBnSxgetHtFMuSjWuMVASEH0meSBm0JoRUyEw8XnMb"
    "IjVq5PkLR+92r/FGiMPE1bf1mHT24XCnVjkPLR/gqGRW9dpBsOaWVnRBj/M/+Dp"
    "19vGf/rAAAAAIxAAEAAAAAAAAAAzMAAQAAAAA="
)


def stream(dictionary: str, data: bytes) -> bytes:
    return (
        f"<< {dictionary} /Length {len(data)} >>\nstream\n".encode("ascii")
        + data
        + b"\nendstream"
    )


def s15fixed16(value: float) -> bytes:
    return struct.pack(">i", round(value * 65536))


def make_icc_profile() -> bytes:
    def xyz(values: tuple[float, float, float]) -> bytes:
        return b"XYZ " + b"\0" * 4 + b"".join(s15fixed16(value) for value in values)

    def curve(gamma: float) -> bytes:
        return (
            b"para"
            + b"\0" * 4
            + struct.pack(">H", 0)
            + b"\0" * 2
            + s15fixed16(gamma)
        )

    description = b"Poppler.Net fixture sRGB\0"
    tags = {
        b"rXYZ": xyz((0.43607, 0.22249, 0.01392)),
        b"gXYZ": xyz((0.38515, 0.71687, 0.09708)),
        b"bXYZ": xyz((0.14307, 0.06061, 0.71410)),
        b"rTRC": curve(2.2),
        b"gTRC": curve(2.2),
        b"bTRC": curve(2.2),
        b"desc": b"desc" + b"\0" * 4 + struct.pack(">I", len(description)) + description,
    }
    header = bytearray(128)
    header[16:20] = b"RGB "
    header[20:24] = b"XYZ "
    header[36:40] = b"acsp"
    table = bytearray(struct.pack(">I", len(tags)))
    payload = bytearray()
    offset = 128 + 4 + len(tags) * 12
    for signature, value in tags.items():
        padding = (-offset) % 4
        if padding:
            payload.extend(b"\0" * padding)
            offset += padding
        table.extend(signature)
        table.extend(struct.pack(">II", offset, len(value)))
        payload.extend(value)
        offset += len(value)
    profile = header + table + payload
    profile[0:4] = struct.pack(">I", len(profile))
    return bytes(profile)


def make_codec_payloads() -> tuple[bytes, bytes, bytes, bytes]:
    image = Image.new("RGB", (2, 2))
    image.putdata([(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)])
    jpeg_output = io.BytesIO()
    image.save(jpeg_output, "JPEG", quality=100, subsampling=0)
    jpx_output = io.BytesIO()
    image.save(jpx_output, "JPEG2000", irreversible=False)

    fax = Image.new("1", (16, 8), 1)
    fax_pixels = fax.load()
    for y in range(8):
        for x in range(16):
            if x == y or x == 15 - y or (4 <= x < 12 and y in (2, 5)):
                fax_pixels[x, y] = 0
    def fax_payload(compression: str) -> bytes:
        tiff_output = io.BytesIO()
        fax.save(tiff_output, "TIFF", compression=compression)
        tiff_bytes = tiff_output.getvalue()
        with Image.open(io.BytesIO(tiff_bytes)) as parsed:
            offset_value = parsed.tag_v2[273]
            count_value = parsed.tag_v2[279]
            offset = offset_value[0] if isinstance(offset_value, tuple) else offset_value
            count = count_value[0] if isinstance(count_value, tuple) else count_value
        return tiff_bytes[offset : offset + count]

    return (
        jpeg_output.getvalue(),
        jpx_output.getvalue(),
        fax_payload("group4"),
        fax_payload("group3"),
    )


def build_pdf() -> bytes:
    jpeg, jpx, ccitt, ccitt_group3 = make_codec_payloads()
    icc = make_icc_profile()
    content_parts: list[str] = []
    image_names = [
        "Raw",
        "Indexed",
        "Spot",
        "DeviceN",
        "Lab",
        "Icc",
        "Jpeg",
        "Jpx",
        "Fax",
        "FaxG3",
        "Jbig",
        "Soft",
    ]
    for index, name in enumerate(image_names):
        column = index % 4
        row = index // 4
        content_parts.append(
            f"q 90 0 0 90 {30 + column * 135} {650 - row * 200} cm /{name} Do Q"
        )
    content_parts.extend(
        [
            "/SpotCS cs 1 scn 30 40 50 20 re f",
            "/LabCS cs 50 0 0 scn 100 40 50 20 re f",
        ]
    )
    content = "\n".join(content_parts).encode("ascii")

    raw_rgb = bytes(
        [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]
    )
    indexed = bytes([0, 85, 170, 255])
    lookup = bytes(
        [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]
    )
    sampled_tint = bytes(
        [255, 255, 255, 255, 0, 0, 0, 0, 255, 0, 0, 0]
    )
    soft_color = bytes(
        [255, 128, 0, 0, 128, 255, 128, 0, 255, 255, 0, 128]
    )
    soft_mask = bytes([255, 170, 85, 0])

    objects: list[bytes] = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] "
            b"/Resources << /XObject << "
            b"/Raw 5 0 R /Indexed 6 0 R /Spot 7 0 R /DeviceN 8 0 R "
            b"/Lab 10 0 R /Icc 11 0 R /Jpeg 13 0 R /Jpx 14 0 R "
            b"/Fax 15 0 R /FaxG3 20 0 R /Jbig 16 0 R /Soft 17 0 R >> "
            b"/ColorSpace << "
            b"/SpotCS [/Separation /FixtureRed /DeviceRGB 9 0 R] "
            b"/LabCS [/Lab << /WhitePoint [0.95047 1 1.08883] "
            b"/Range [-100 100 -100 100] >>] >> >> "
            b"/Contents 4 0 R >>"
        ),
        stream("", content),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace /DeviceRGB /BitsPerComponent 8",
            raw_rgb,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace [/Indexed /DeviceRGB 3 <"
            + lookup.hex().upper()
            + ">] /BitsPerComponent 8",
            indexed,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 1 "
            "/ColorSpace [/Separation /FixtureRed /DeviceRGB 9 0 R] "
            "/BitsPerComponent 8",
            bytes([0, 255]),
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace [/DeviceN [/CyanSpot /MagentaSpot] /DeviceRGB 19 0 R] "
            "/BitsPerComponent 8",
            bytes([0, 0, 255, 0, 0, 255, 255, 255]),
        ),
        (
            b"<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] "
            b"/C1 [1 0 0] /N 1 >>"
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 1 "
            "/ColorSpace [/Lab << /WhitePoint [0.95047 1 1.08883] "
            "/Range [-100 100 -100 100] >>] /BitsPerComponent 8",
            bytes([128, 128, 128, 204, 170, 85]),
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace [/ICCBased 12 0 R] /BitsPerComponent 8",
            raw_rgb,
        ),
        stream("/N 3 /Alternate /DeviceRGB", icc),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",
            jpeg,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /JPXDecode",
            jpx,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 16 /Height 8 "
            "/ColorSpace /DeviceGray /BitsPerComponent 1 "
            "/Filter /CCITTFaxDecode "
            "/DecodeParms << /K -1 /Columns 16 /Rows 8 /BlackIs1 true >>",
            ccitt,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 96 /Height 96 "
            "/ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode",
            JBIG2_PDF_STREAM,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 18 0 R",
            soft_color,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 "
            "/ColorSpace /DeviceGray /BitsPerComponent 8",
            soft_mask,
        ),
        stream(
            "/FunctionType 0 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] "
            "/Size [2 2] /BitsPerSample 8 "
            "/Encode [0 1 0 1] /Decode [0 1 0 1 0 1]",
            sampled_tint,
        ),
        stream(
            "/Type /XObject /Subtype /Image /Width 16 /Height 8 "
            "/ColorSpace /DeviceGray /BitsPerComponent 1 "
            "/Filter /CCITTFaxDecode "
            "/DecodeParms << /K 0 /Columns 16 /Rows 8 "
            "/EndOfLine true /BlackIs1 true >>",
            ccitt_group3,
        ),
    ]

    output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for number, value in enumerate(objects, start=1):
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
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
            f"startxref\n{xref}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(output)


def main() -> None:
    pdf = build_pdf()
    pdf_path = ROOT / "images-and-color.pdf"
    pdf_path.write_bytes(pdf)
    manifest = {
        "file": pdf_path.name,
        "sha256": hashlib.sha256(pdf).hexdigest(),
        "page_size": [600, 800],
        "decoded_images": 12,
        "special_color_paths": 2,
        "icc_curve": "parametric-type-0",
        "resource_names": [
            "Raw",
            "Indexed",
            "Spot",
            "DeviceN",
            "Lab",
            "Icc",
            "Jpeg",
            "Jpx",
            "Fax",
            "FaxG3",
            "Jbig",
            "Soft",
        ],
        "jbig2_fixture": {
            "source": "JBig2Decoder.NETStandard examplepicture_nl.jb2",
            "license": "MIT",
            "dimensions": [96, 96],
        },
    }
    (ROOT / "images-color-fixture.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
