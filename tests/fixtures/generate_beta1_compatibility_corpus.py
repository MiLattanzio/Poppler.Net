#!/usr/bin/env python3
"""Generate cross-feature graphics regressions for Poppler.Net 0.12.0-beta.1."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from generate_beta2_graphics_corpus import Objects, pack_records, stream
from generate_shading_alpha3_corpus import patch_record, sampled_function


ROOT = Path(__file__).resolve().parent
PAGE_NAMES = [
    "reused-isolated-dashed-stroke-groups",
    "coons-mesh-through-type1-luminosity-mask-and-evenodd-clip",
    "rotated-cropbox-with-transformed-groups-and-conservative-svg-fallback",
]


def page(
    objects: Objects,
    parent: int,
    content: bytes,
    resources: str,
    *,
    media_box: str = "[0 0 260 180]",
    crop_box: str | None = None,
    rotation: int = 0,
) -> int:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    crop = f" /CropBox {crop_box}" if crop_box is not None else ""
    rotate = f" /Rotate {rotation}" if rotation else ""
    return objects.add(
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox {media_box}{crop}{rotate} "
            f"/Resources {resources} /Contents {content_ref} 0 R >>"
        ).encode("ascii")
    )


def stroke_group(objects: Objects) -> int:
    alpha = objects.add(
        b"<< /Type /ExtGState /CA 0.75 /ca 0.65 /BM /Multiply >>"
    )
    content = (
        b"q 20 18 200 124 re W n /StrokeAlpha gs\n"
        b"q 1 0.24 0 0.82 4 4 cm\n"
        b"0.05 0.35 0.9 RG 10 w 1 J 1 j [18 7 4] -13 d\n"
        b"15 35 m 55 120 150 5 218 108 c S Q\n"
        b"0.85 0.1 0.15 RG 6 w 2 J 2 j [15 8] 4 d\n"
        b"24 28 m 210 28 l 210 132 l 24 132 l h S Q"
    )
    return objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 240 160] "
                "/Group << /S /Transparency /I true /K false /CS /DeviceRGB >> "
                f"/Resources << /ExtGState << /StrokeAlpha {alpha} 0 R >> >> "
                f"/Length {len(content)} >>"
            ),
            content,
        )
    )


def coons_mesh(objects: Objects) -> int:
    colors = [
        (255, 32, 32),
        (20, 220, 80),
        (32, 80, 255),
        (255, 235, 60),
    ]
    first_points = [
        (12, 82), (32, 18), (82, 6), (120, 58),
        (134, 82), (132, 122), (112, 150),
        (78, 170), (30, 156), (8, 126),
        (0, 106), (0, 92),
    ]
    second_points = [
        (154, 170), (196, 156), (222, 124),
        (248, 82), (228, 38), (190, 20),
        (152, 34), (134, 60),
    ]
    data = pack_records(
        [
            patch_record(0, first_points, colors),
            patch_record(1, second_points, [(255, 64, 220), (20, 225, 235)]),
        ]
    )
    return objects.add(
        stream(
            (
                "<< /ShadingType 6 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode [0 260 0 180 0 1 0 1 0 1] /Length {len(data)} >>"
            ),
            data,
        )
    )


def masked_mesh(objects: Objects) -> tuple[int, int]:
    mesh = coons_mesh(objects)
    mesh_content = (
        b"q 8 8 244 164 re 66 44 126 88 re W* n /Coons sh Q"
    )
    mesh_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 260 180] "
                "/Group << /S /Transparency /I true /K false /CS /DeviceRGB >> "
                f"/Resources << /Shading << /Coons {mesh} 0 R >> >> "
                f"/Length {len(mesh_content)} >>"
            ),
            mesh_content,
        )
    )

    mask_function = sampled_function(objects, bytes([0, 255, 255, 0]), outputs=1)
    mask_shading = objects.add(
        (
            "<< /ShadingType 1 /ColorSpace /DeviceGray /Domain [0 1 0 1] "
            f"/Matrix [240 0 0 160 10 10] /Function {mask_function} 0 R >>"
        ).encode("ascii")
    )
    mask_content = b"/MaskShading sh"
    mask_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 260 180] "
                "/Group << /S /Transparency /I true /K false /CS /DeviceGray >> "
                f"/Resources << /Shading << /MaskShading {mask_shading} 0 R >> >> "
                f"/Length {len(mask_content)} >>"
            ),
            mask_content,
        )
    )
    masked = objects.add(
        (
            f"<< /Type /ExtGState /ca 0.9 /CA 0.9 "
            f"/SMask << /S /Luminosity /G {mask_group} 0 R /BC [0] >> >>"
        ).encode("ascii")
    )
    return mesh_group, masked


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()

    strokes = stroke_group(objects)
    screen = objects.add(
        b"<< /Type /ExtGState /ca 0.55 /CA 0.55 /BM /Screen >>"
    )
    first = page(
        objects,
        pages,
        (
            b"0.94 0.95 0.98 rg 0 0 260 180 re f\n"
            b"q 1 0.08 -0.12 1 10 4 cm /StrokeGroup Do Q\n"
            b"q /Screen gs 0.65 0 0 0.65 84 58 cm /StrokeGroup Do Q"
        ),
        (
            f"<< /XObject << /StrokeGroup {strokes} 0 R >> "
            f"/ExtGState << /Screen {screen} 0 R >> >>"
        ),
    )

    mesh_group, masked = masked_mesh(objects)
    second = page(
        objects,
        pages,
        (
            b"q /Masked gs /MeshGroup Do Q\n"
            b"0.08 0.08 0.12 RG 4 w 1 J 1 j [12 6 3] -7 d\n"
            b"18 24 m 68 154 l 194 146 l 238 28 l S"
        ),
        (
            f"<< /XObject << /MeshGroup {mesh_group} 0 R >> "
            f"/ExtGState << /Masked {masked} 0 R >> >>"
        ),
    )

    third = page(
        objects,
        pages,
        (
            b"q /Masked gs 0.62 0 0 0.62 42 42 cm /MeshGroup Do Q\n"
            b"q 0.5 0.08 -0.05 0.45 108 45 cm /StrokeGroup Do Q"
        ),
        (
            f"<< /XObject << /MeshGroup {mesh_group} 0 R "
            f"/StrokeGroup {strokes} 0 R >> "
            f"/ExtGState << /Masked {masked} 0 R >> >>"
        ),
        media_box="[0 0 280 200]",
        crop_box="[25 15 255 185]",
        rotation=90,
    )

    kids = " ".join(f"{reference} 0 R" for reference in [first, second, third])
    objects.set(pages, f"<< /Type /Pages /Kids [{kids}] /Count 3 >>".encode("ascii"))
    objects.set(catalog, f"<< /Type /Catalog /Pages {pages} 0 R >>".encode("ascii"))
    return objects.build()


MANAGED_PNG_SHA256: dict[str, list[str]] = {
    "dpi72-aa4-opaque": [
        "c7bdc057ccacf0e1a30320a40a71e35a88a0eeeabd5e43d87e483490695383d0",
        "9c87bfdb04658e6e369c980a1c62a26ac3d7ee36f75688dc9d31110648f72432",
        "6e9127e0a1e28a58648f6471b7eda8798e05e42afde8598e8cdc9c4f01ca8cd7",
    ],
}
MANAGED_TRANSPARENT_SHA256: list[str] = [
    "c7bdc057ccacf0e1a30320a40a71e35a88a0eeeabd5e43d87e483490695383d0",
    "fe8a568777a38bdfc58e78f6e05ffcc38f4811cff03c236d2bf6e43939189c6a",
    "429c6b076ffc179129bb878aa7ac5dae72036c6abeae2a70ad30ed61943ac920",
]
MANAGED_SVG_SHA256: list[str] = [
    "f0a4bbf1ef06c587b3d979c532167be8908900e500b3242e7875cef86e0da0c7",
    "13e3eba1ef221c2b41a9d798de69b7f38ea7a8f6301ed2dc00c69448225ccbda",
    "7849e97fe13f54a088fb0fc3a4b438dea4dff47eb985b9b44a52e82384809993",
]


def main() -> None:
    data = corpus()
    pdf = ROOT / "compatibility-beta1.pdf"
    manifest = ROOT / "compatibility-beta1-fixture.json"
    pdf.write_bytes(data)
    contents = json.dumps(
        {
            "file": pdf.name,
            "sha256": hashlib.sha256(data).hexdigest(),
            "pages": PAGE_NAMES,
            "managed_png_hash_mode": "canonical-png-content-v1",
            "managed_png_sha256": MANAGED_PNG_SHA256,
            "managed_transparent_sha256": MANAGED_TRANSPARENT_SHA256,
            "managed_svg_hash_mode": "canonical-svg-content-v1",
            "managed_svg_sha256": MANAGED_SVG_SHA256,
        },
        indent=2,
    ) + "\n"
    manifest.write_bytes(contents.encode("utf-8"))


if __name__ == "__main__":
    main()
