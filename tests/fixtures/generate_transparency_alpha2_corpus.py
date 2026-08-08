#!/usr/bin/env python3
"""Generate deterministic transparency fixtures for Poppler.Net 0.12 alpha 2."""

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
                "/ID [<12000212000212000212000212000212> "
                "<12000212000212000212000212000212>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def form(
    objects: Objects,
    content: bytes,
    resources: str = "<< >>",
    *,
    isolated: bool,
    knockout: bool,
    bbox: str = "[0 0 100 80]",
    color_space: str = "",
) -> int:
    group = (
        f"/Group << /S /Transparency /I {'true' if isolated else 'false'} "
        f"/K {'true' if knockout else 'false'}{color_space} >> "
    )
    return objects.add(
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox {bbox} {group}"
                f"/Resources {resources} /Length {len(content)} >>"
            ),
            content,
        )
    )


def page(
    objects: Objects,
    parent: int,
    content: bytes,
    resources: str,
    *,
    media_box: str,
    annots: str = "",
) -> int:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    return objects.add(
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox {media_box} "
            f"/Resources {resources} /Contents {content_ref} 0 R{annots} >>"
        ).encode("ascii")
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs: list[int] = []

    alpha65 = objects.add(b"<< /Type /ExtGState /ca 0.65 /CA 0.65 >>")
    overlap = b"\n".join(
        [
            b"/A gs 1 0 0 rg 0 5 70 65 re f",
            b"/A gs 0 0 1 rg 30 10 70 65 re f",
        ]
    )
    matrix_groups: list[int] = []
    for isolated, knockout in ((False, False), (True, False), (False, True), (True, True)):
        matrix_groups.append(
            form(
                objects,
                overlap,
                f"<< /ExtGState << /A {alpha65} 0 R >> >>",
                isolated=isolated,
                knockout=knockout,
            )
        )
    matrix_content = [b"0.92 0.72 0.18 rg"]
    placements = ((10, 110), (130, 110), (10, 15), (130, 15))
    for index, (x, y) in enumerate(placements):
        matrix_content.append(f"{x} {y} 100 80 re f".encode("ascii"))
        matrix_content.append(
            f"q 1 0 0 1 {x} {y} cm /G{index} Do Q".encode("ascii")
        )
    page_refs.append(
        page(
            objects,
            pages,
            b"\n".join(matrix_content),
            "<< /XObject << "
            + " ".join(f"/G{i} {ref} 0 R" for i, ref in enumerate(matrix_groups))
            + " >> >>",
            media_box="[0 0 240 205]",
        )
    )

    inner = form(
        objects,
        overlap,
        f"<< /ExtGState << /A {alpha65} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 100 80]",
    )
    middle_content = b"\n".join(
        [
            b"0 0.7 0.2 rg 0 0 120 90 re f",
            b"q 1 0 0 1 10 5 cm /Inner Do Q",
        ]
    )
    middle = form(
        objects,
        middle_content,
        f"<< /XObject << /Inner {inner} 0 R >> >>",
        isolated=False,
        knockout=False,
        bbox="[0 0 120 90]",
    )
    outer_content = b"\n".join(
        [
            b"q /Half gs 1 0 0 1 0 0 cm /Middle Do Q",
            b"q /Half gs 1 0 0 1 35 25 cm /Middle Do Q",
        ]
    )
    outer = form(
        objects,
        outer_content,
        f"<< /ExtGState << /Half {alpha65} 0 R >> "
        f"/XObject << /Middle {middle} 0 R >> >>",
        isolated=True,
        knockout=True,
        bbox="[0 0 155 115]",
    )
    multiply = objects.add(b"<< /Type /ExtGState /BM /Multiply /ca 0.8 >>")
    hue = objects.add(b"<< /Type /ExtGState /BM /Hue /ca 0.8 >>")
    nested_content = b"\n".join(
        [
            b"0.1 0.55 0.95 rg 0 0 360 150 re f",
            b"q /Multiply gs 1 0 0 1 15 15 cm /Outer Do Q",
            b"q /Hue gs 1 0 0 1 190 15 cm /Outer Do Q",
        ]
    )
    page_refs.append(
        page(
            objects,
            pages,
            nested_content,
            f"<< /ExtGState << /Multiply {multiply} 0 R /Hue {hue} 0 R >> "
            f"/XObject << /Outer {outer} 0 R >> >>",
            media_box="[0 0 370 150]",
        )
    )

    mask_nested = form(
        objects,
        b"0 g 0 0 120 80 re f",
        isolated=True,
        knockout=False,
        bbox="[0 0 120 80]",
        color_space=" /CS /DeviceGray",
    )
    alpha_mask_group = form(
        objects,
        b"/Quarter gs 0 g 0 0 60 80 re f q 1 0 0 1 60 0 cm /Nested Do Q",
        f"<< /ExtGState << /Quarter {objects.add(b'<< /Type /ExtGState /ca 0.5 >>')} 0 R >> "
        f"/XObject << /Nested {mask_nested} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 120 80]",
        color_space=" /CS /DeviceGray",
    )
    gradient_function = objects.add(
        b"<< /FunctionType 2 /Domain [0 1] /C0 [0.05 0.2 0.8] "
        b"/C1 [1 0.9 0.1] /N 1 >>"
    )
    mask_shading = objects.add(
        f"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 120 0] "
        f"/Function {gradient_function} 0 R /Extend [true true] >>".encode("ascii")
    )
    luminosity_mask_group = form(
        objects,
        b"/Gradient sh",
        f"<< /Shading << /Gradient {mask_shading} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 120 80]",
        color_space=" /CS /DeviceRGB",
    )
    luminosity_transfer = objects.add(
        stream(
            "<< /FunctionType 4 /Domain [0 1] /Range [0 1] /Length 19 >>",
            b"{ 0.8 mul 0.1 add }",
        )
    )
    alpha_mask_state = objects.add(
        f"<< /Type /ExtGState /SMask << /S /Alpha /G {alpha_mask_group} 0 R >> >>".encode("ascii")
    )
    luminosity_mask_state = objects.add(
        f"<< /Type /ExtGState /SMask << /S /Luminosity "
        f"/G {luminosity_mask_group} 0 R /BC [0.2 0.4 0.6] "
        f"/TR {luminosity_transfer} 0 R >> >>".encode("ascii")
    )
    masked_group = form(
        objects,
        b"/AlphaMask gs 0.8 0 0.8 rg 0 0 120 80 re f",
        f"<< /ExtGState << /AlphaMask {alpha_mask_state} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 120 80]",
    )
    masks_content = b"\n".join(
        [
            b"q 10 15 90 70 re W n /AlphaMask gs 1 0.2 0.2 rg 0 0 120 80 re f Q",
            b"q 1 0 0 1 140 0 cm 20 10 80 70 re W n /LumMask gs 0.1 0.8 0.2 rg 0 0 120 80 re f Q",
            b"q 1 0 0 1 270 0 cm /MaskedGroup Do Q",
        ]
    )
    page_refs.append(
        page(
            objects,
            pages,
            masks_content,
            f"<< /ExtGState << /AlphaMask {alpha_mask_state} 0 R "
            f"/LumMask {luminosity_mask_state} 0 R >> "
            f"/XObject << /MaskedGroup {masked_group} 0 R >> >>",
            media_box="[0 0 400 100]",
        )
    )

    font = objects.add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
    image_data = bytes([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0])
    image = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 "
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 "
                f"/Length {len(image_data)} >>"
            ),
            image_data,
        )
    )
    pattern_content = b"1 0.4 0 rg 0 0 5 10 re f 0 0 0 rg 5 0 5 10 re f"
    pattern = objects.add(
        stream(
            (
                "<< /Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 "
                "/BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << >> "
                f"/Length {len(pattern_content)} >>"
            ),
            pattern_content,
        )
    )
    mixed_function = objects.add(
        b"<< /FunctionType 2 /Domain [0 1] /C0 [0 1 1] /C1 [1 0 1] /N 1 >>"
    )
    mixed_shading = objects.add(
        f"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 180 0] "
        f"/Function {mixed_function} 0 R /Extend [true true] >>".encode("ascii")
    )
    mixed_content = b"\n".join(
        [
            b"/MixedGradient sh",
            b"/Pattern cs /Stripes scn 10 10 70 50 re f",
            b"q 70 0 0 60 90 10 cm /Image Do Q",
            b"BT /F1 20 Tf 1 1 1 rg 15 70 Td (Text Image Pattern Shading) Tj ET",
        ]
    )
    mixed_group = form(
        objects,
        mixed_content,
        f"<< /Font << /F1 {font} 0 R >> /Pattern << /Stripes {pattern} 0 R >> "
        f"/Shading << /MixedGradient {mixed_shading} 0 R >> "
        f"/XObject << /Image {image} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 360 100]",
    )
    annotation_alpha = objects.add(b"<< /Type /ExtGState /ca 0.6 >>")
    annotation_content = b"/A gs 1 0 0 rg 0 0 80 50 re f 0 0 1 rg 30 0 50 50 re f"
    annotation_appearance = form(
        objects,
        annotation_content,
        f"<< /ExtGState << /A {annotation_alpha} 0 R >> >>",
        isolated=True,
        knockout=True,
        bbox="[0 0 80 50]",
    )
    annotation = objects.add(
        f"<< /Type /Annot /Subtype /Stamp /Rect [270 105 350 155] "
        f"/F 4 /AP << /N {annotation_appearance} 0 R >> >>".encode("ascii")
    )
    page_refs.append(
        page(
            objects,
            pages,
            b"q 1 0 0 1 10 10 cm /Mixed Do Q",
            f"<< /XObject << /Mixed {mixed_group} 0 R >> >>",
            media_box="[0 0 380 170]",
            annots=f" /Annots [{annotation} 0 R]",
        )
    )

    blend_modes = [
        "Multiply", "Screen", "Overlay", "Darken",
        "Lighten", "ColorDodge", "ColorBurn", "HardLight",
        "SoftLight", "Difference", "Exclusion", "Hue",
        "Saturation", "Color", "Luminosity", "Normal",
    ]
    blend_group = form(
        objects,
        b"/A gs 1 0.1 0.1 rg 0 0 62 62 re f",
        f"<< /ExtGState << /A {alpha65} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 62 62]",
    )
    blend_states = {
        mode: objects.add(
            f"<< /Type /ExtGState /BM /{mode} /ca 0.85 >>".encode("ascii")
        )
        for mode in blend_modes
    }
    blend_content: list[bytes] = []
    for index, mode in enumerate(blend_modes):
        column = index % 4
        row = 3 - index // 4
        x = column * 80 + 8
        y = row * 80 + 8
        blend_content.append(
            f"0.1 0.6 0.95 rg {x} {y} 62 62 re f".encode("ascii")
        )
        blend_content.append(
            f"q /{mode} gs 1 0 0 1 {x} {y} cm /Blend Do Q".encode("ascii")
        )
    page_refs.append(
        page(
            objects,
            pages,
            b"\n".join(blend_content),
            "<< /ExtGState << "
            + " ".join(f"/{mode} {ref} 0 R" for mode, ref in blend_states.items())
            + f" >> /XObject << /Blend {blend_group} 0 R >> >>",
            media_box="[0 0 320 320]",
        )
    )

    micro_alpha = objects.add(b"<< /Type /ExtGState /ca 0.5 >>")
    micro_overlap = b"\n".join(
        [
            b"/A gs 1 0 0 rg 0 0 1 1 re f",
            b"/A gs 0 0 1 rg 0 0 1 1 re f",
        ]
    )
    micro_isolated = form(
        objects,
        micro_overlap,
        f"<< /ExtGState << /A {micro_alpha} 0 R >> >>",
        isolated=True,
        knockout=False,
        bbox="[0 0 1 1]",
    )
    micro_knockout = form(
        objects,
        micro_overlap,
        f"<< /ExtGState << /A {micro_alpha} 0 R >> >>",
        isolated=True,
        knockout=True,
        bbox="[0 0 1 1]",
    )
    micro_nonisolated = form(
        objects,
        b"/A gs 1 0 0 rg 0 0 1 1 re f",
        f"<< /ExtGState << /A {micro_alpha} 0 R >> >>",
        isolated=False,
        knockout=False,
        bbox="[0 0 1 1]",
    )
    micro_same = form(
        objects,
        b"1 0 0 rg 0 0 1 1 re f 1 1 1 rg 0 0 1 1 re f",
        isolated=False,
        knockout=True,
        bbox="[0 0 1 1]",
    )
    micro_content = b"\n".join(
        [
            b"q /Boundary gs 1 0 0 1 0 1 cm /Isolated Do Q",
            b"q 1 0 0 1 1 1 cm /Knockout Do Q",
            b"0 0 1 rg 0 0 1 1 re f q 1 0 0 1 0 0 cm /NonIso Do Q",
            b"q 1 0 0 1 1 0 cm /Same Do Q",
        ]
    )
    boundary_half = objects.add(b"<< /Type /ExtGState /ca 0.5 >>")
    page_refs.append(
        page(
            objects,
            pages,
            micro_content,
            f"<< /ExtGState << /Boundary {boundary_half} 0 R >> /XObject << "
            f"/Isolated {micro_isolated} 0 R /Knockout {micro_knockout} 0 R "
            f"/NonIso {micro_nonisolated} 0 R /Same {micro_same} 0 R >> >>",
            media_box="[0 0 2 2]",
        )
    )

    objects.set(catalog, f"<< /Type /Catalog /Pages {pages} 0 R >>".encode("ascii"))
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Count {len(page_refs)} /Kids ["
            + " ".join(f"{reference} 0 R" for reference in page_refs)
            + "] >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    data = corpus()
    pdf = ROOT / "transparency-alpha2.pdf"
    manifest = ROOT / "transparency-alpha2-fixture.json"
    pdf.write_bytes(data)
    manifest.write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "isolated-knockout-grid",
                    "three-level-nesting-and-boundary-blend",
                    "alpha-luminosity-backdrop-transfer-and-clips",
                    "text-image-pattern-shading-and-annotation",
                    "separable-and-nonseparable-blend-grid",
                    "two-by-two-numeric-formulas",
                ],
                "managed_png_sha256": {
                    "dpi72-aa4-opaque-fixed-fonts": [
                        "c949800434f34450e646c4e264da1e0e1bf569f857ec7b36d1a719cfd509e610",
                        "0898fdd5cdddd2d3cfa41520150711b426fb451e6479829cc6e55495a2244ef8",
                        "a17ec1bb25c34d716fa22755c21e8c6ffc0e0ec5b5bbd47a797f9ce3bf38204d",
                        "0dad8928d2b4a227721aa5aaebaefb5fb83683ecf87005fd1a1429f79d3c2165",
                        "f0ff0404b0ec64c8ab161d7125f01b35108d3e59a0a5284cc42f7909f8a21823",
                        "165cb278116fa3d6c6b9c7551cb4424355ebba5933fb36e0ada6e55b0700538f",
                    ]
                },
                "managed_svg_sha256": {
                    "default": [
                        "cb12b8f668eec6677eb344523158b5ad18659f1b2ef617a91f3694ba90681c6e",
                        "542274fb88165de2938377c855250350081cdf1e4a8ba514c99f7d1bae1a3905",
                        "a537ca647ded27f853c02f3736e4371674474af892663fd75277d2afa08b8910",
                        "a50de6486a73abd39253d8a0b30169cee6e28c285c1397281e11891364abfbd2",
                        "76bfb10f95d1de6fd539e8738f48dc7d8fbd93bc500419f53b8394de0ae6aab7",
                        "7d90d81daed39f7c41b554cf5f40b522ac0ce93dc9d7003ba335810c6d617821",
                    ]
                },
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
