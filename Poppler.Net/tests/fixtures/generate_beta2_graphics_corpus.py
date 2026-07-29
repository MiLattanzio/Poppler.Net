#!/usr/bin/env python3
"""Generate deterministic graphics regressions for Poppler.Net 0.8.0-beta.2."""

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
                "/ID [<08000208000208000208000208000208> "
                "<08000208000208000208000208000208>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def pack_records(records: list[list[tuple[int, int]]]) -> bytes:
    output = bytearray()
    for record in records:
        bits: list[int] = []
        for value, width in record:
            if value < 0 or value >= 1 << width:
                raise ValueError((value, width))
            bits.extend((value >> shift) & 1 for shift in range(width - 1, -1, -1))
        while len(bits) % 8:
            bits.append(0)
        for offset in range(0, len(bits), 8):
            byte = 0
            for bit in bits[offset : offset + 8]:
                byte = (byte << 1) | bit
            output.append(byte)
    return bytes(output)


def vertex(flag: int | None, x: int, y: int, color: tuple[int, int, int]) -> list[tuple[int, int]]:
    fields: list[tuple[int, int]] = []
    if flag is not None:
        fields.append((flag, 2))
    fields.extend([(x, 8), (y, 8)])
    fields.extend((component, 8) for component in color)
    return fields


def patch_record(
    points: list[tuple[int, int]],
    colors: list[tuple[int, int, int]],
) -> list[tuple[int, int]]:
    fields: list[tuple[int, int]] = [(0, 2)]
    for x, y in points:
        fields.extend([(x, 8), (y, 8)])
    for color in colors:
        fields.extend((component, 8) for component in color)
    return fields


def page(objects: Objects, parent: int, content: bytes, resources: str) -> int:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    return objects.add(
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 240 160] "
            f"/Resources {resources} /Contents {content_ref} 0 R >>"
        ).encode("ascii")
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()

    decode = "[0 240 0 160 0 1 0 1 0 1]"
    type4_data = pack_records(
        [
            vertex(0, 20, 20, (255, 0, 0)),
            vertex(0, 110, 20, (0, 255, 0)),
            vertex(0, 20, 130, (0, 0, 255)),
            vertex(1, 110, 130, (255, 255, 255)),
        ]
    )
    type4 = objects.add(
        stream(
            (
                "<< /ShadingType 4 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode {decode} /Length {len(type4_data)} >>"
            ),
            type4_data,
        )
    )
    type5_data = pack_records(
        [
            vertex(None, 130, 20, (255, 128, 0)),
            vertex(None, 220, 20, (128, 0, 255)),
            vertex(None, 130, 130, (0, 255, 255)),
            vertex(None, 220, 130, (255, 255, 0)),
        ]
    )
    type5 = objects.add(
        stream(
            (
                "<< /ShadingType 5 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow 2 "
                f"/Decode {decode} /Length {len(type5_data)} >>"
            ),
            type5_data,
        )
    )
    first_page = page(
        objects,
        pages,
        b"/Free sh\n/Lattice sh",
        f"<< /Shading << /Free {type4} 0 R /Lattice {type5} 0 R >> >>",
    )

    coons_points = [
        (15, 20), (45, 5), (80, 5), (110, 20),
        (125, 55), (125, 95), (110, 135),
        (80, 150), (45, 150), (15, 135),
        (0, 95), (0, 55),
    ]
    tensor_points = [
        (130, 20), (160, 10), (195, 10), (225, 20),
        (238, 55), (238, 100), (225, 135),
        (195, 150), (160, 150), (130, 135),
        (120, 100), (120, 55),
        (158, 55), (197, 52), (198, 105), (158, 108),
    ]
    patch_colors = [
        (255, 0, 0),
        (0, 255, 0),
        (0, 0, 255),
        (255, 255, 255),
    ]
    type6_data = pack_records([patch_record(coons_points, patch_colors)])
    type7_data = pack_records([patch_record(tensor_points, patch_colors)])
    type6 = objects.add(
        stream(
            (
                "<< /ShadingType 6 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode {decode} /Length {len(type6_data)} >>"
            ),
            type6_data,
        )
    )
    type7 = objects.add(
        stream(
            (
                "<< /ShadingType 7 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode {decode} /Length {len(type7_data)} >>"
            ),
            type7_data,
        )
    )
    second_page = page(
        objects,
        pages,
        b"/Coons sh\n/Tensor sh",
        f"<< /Shading << /Coons {type6} 0 R /Tensor {type7} 0 R >> >>",
    )

    pattern_content = b"0 0 5 10 re f"
    pattern = objects.add(
        stream(
            (
                "<< /Type /Pattern /PatternType 1 /PaintType 2 /TilingType 1 "
                "/BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << >> "
                f"/Length {len(pattern_content)} >>"
            ),
            pattern_content,
        )
    )
    pattern_page = b"\n".join(
        [
            b"/PCS cs 1 0 0 /Hatch scn 10 20 100 120 re f",
            b"/PCS cs 0 0 1 /Hatch scn 130 20 100 120 re f",
        ]
    )
    third_page = page(
        objects,
        pages,
        pattern_page,
        (
            f"<< /ColorSpace << /PCS [/Pattern /DeviceRGB] >> "
            f"/Pattern << /Hatch {pattern} 0 R >> >>"
        ),
    )

    calculator = objects.add(
        stream(
            "<< /FunctionType 4 /Domain [0 1] /Range [0 1] /Length 11 >>",
            b"{ dup mul }",
        )
    )
    mask_function = objects.add(
        b"<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>"
    )
    mask_shading = objects.add(
        (
            "<< /ShadingType 2 /ColorSpace /DeviceGray "
            f"/Coords [0 0 100 0] /Function {mask_function} 0 R "
            "/Extend [true true] >>"
        ).encode("ascii")
    )
    mask_content = b"/MaskGradient sh"
    mask_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 100 100] "
                "/Group << /S /Transparency /I true /CS /DeviceGray >> "
                f"/Resources << /Shading << /MaskGradient {mask_shading} 0 R >> >> "
                f"/Length {len(mask_content)} >>"
            ),
            mask_content,
        )
    )
    soft_state = objects.add(
        (
            "<< /Type /ExtGState "
            f"/SMask << /S /Luminosity /G {mask_group} 0 R "
            f"/TR {calculator} 0 R /BC [0] >> >>"
        ).encode("ascii")
    )
    half_state = objects.add(b"<< /Type /ExtGState /ca 0.5 >>")
    group_content = b"\n".join(
        [
            b"/Half gs 1 0 0 rg 0 0 80 100 re f",
            b"/Half gs 0 0 1 rg 40 0 80 100 re f",
        ]
    )
    knockout_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /BBox [0 0 120 100] "
                "/Group << /S /Transparency /I true /K true >> "
                f"/Resources << /ExtGState << /Half {half_state} 0 R >> >> "
                f"/Length {len(group_content)} >>"
            ),
            group_content,
        )
    )
    fourth_content = b"\n".join(
        [
            b"q 1 0 0 1 10 30 cm /Mask gs 1 0 0 rg 0 0 100 100 re f Q",
            b"q 1 0 0 1 115 30 cm /Knockout Do Q",
        ]
    )
    fourth_page = page(
        objects,
        pages,
        fourth_content,
        (
            f"<< /ExtGState << /Mask {soft_state} 0 R >> "
            f"/XObject << /Knockout {knockout_group} 0 R >> >>"
        ),
    )

    half_multiply = objects.add(
        b"<< /Type /ExtGState /ca 0.5 /BM /Multiply >>"
    )
    blend_group_content = b"\n".join(
        [
            b"/Blend gs 1 0 0 rg 0 0 70 100 re f",
            b"/Blend gs 0 0 1 rg 30 0 70 100 re f",
        ]
    )
    nonisolated_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] "
                "/Group << /S /Transparency /I false /K false >> "
                f"/Resources << /ExtGState << /Blend {half_multiply} 0 R >> >> "
                f"/Length {len(blend_group_content)} >>"
            ),
            blend_group_content,
        )
    )
    isolated_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] "
                "/Group << /S /Transparency /I true /K false >> "
                f"/Resources << /ExtGState << /Blend {half_multiply} 0 R >> >> "
                f"/Length {len(blend_group_content)} >>"
            ),
            blend_group_content,
        )
    )
    group_page_content = b"\n".join(
        [
            b"1 1 0 rg 0 0 240 160 re f",
            b"q 1 0 0 1 10 30 cm /NonIsolated Do Q",
            b"q 1 0 0 1 130 30 cm /Isolated Do Q",
        ]
    )
    fifth_page = page(
        objects,
        pages,
        group_page_content,
        (
            f"<< /XObject << /NonIsolated {nonisolated_group} 0 R "
            f"/Isolated {isolated_group} 0 R >> >>"
        ),
    )

    overprint = objects.add(b"<< /Type /ExtGState /op true /OP true /OPM 1 >>")
    normal = objects.add(b"<< /Type /ExtGState /op false /OP false /OPM 0 >>")
    overprint_content = b"\n".join(
        [
            b"1 0 0 0 k 20 20 80 120 re f",
            b"1 0 0 0 k 140 20 80 120 re f",
            b"/Overprint gs 0 1 0 0 k 20 20 80 120 re f",
            b"/Normal gs 0 1 0 0 k 140 20 80 120 re f",
        ]
    )
    sixth_page = page(
        objects,
        pages,
        overprint_content,
        f"<< /ExtGState << /Overprint {overprint} 0 R /Normal {normal} 0 R >> >>",
    )

    kids = [
        first_page,
        second_page,
        third_page,
        fourth_page,
        fifth_page,
        sixth_page,
    ]
    objects.set(catalog, f"<< /Type /Catalog /Pages {pages} 0 R >>".encode("ascii"))
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Kids [{' '.join(f'{item} 0 R' for item in kids)}] "
            f"/Count {len(kids)} >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    data = corpus()
    pdf = ROOT / "rendering-beta2.pdf"
    pdf.write_bytes(data)
    (ROOT / "rendering-beta2-fixture.json").write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "free-form-and-lattice-gouraud-meshes",
                    "coons-and-tensor-product-patch-meshes",
                    "uncolored-tiling-pattern-reuse",
                    "calculator-soft-mask-and-knockout-group",
                    "isolated-and-non-isolated-transparency-groups",
                    "process-overprint-mode-one",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
