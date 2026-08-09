#!/usr/bin/env python3
"""Generate deterministic shading regressions for Poppler.Net 0.12.0-alpha.3."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from generate_beta2_graphics_corpus import Objects, pack_records, stream


ROOT = Path(__file__).resolve().parent


def page(objects: Objects, parent: int, content: bytes, resources: str) -> int:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    return objects.add(
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 240 160] "
            f"/Resources {resources} /Contents {content_ref} 0 R >>"
        ).encode("ascii")
    )


def sampled_function(
    objects: Objects,
    samples: bytes,
    outputs: int,
) -> int:
    ranges = " ".join("0 1" for _ in range(outputs))
    return objects.add(
        stream(
            (
                f"<< /FunctionType 0 /Domain [0 1 0 1] /Range [{ranges}] "
                f"/Size [2 2] /BitsPerSample 8 /Decode [{ranges}] "
                f"/Length {len(samples)} >>"
            ),
            samples,
        )
    )


def exponential_function(
    objects: Objects,
    first: tuple[float, float, float],
    second: tuple[float, float, float],
    exponent: float = 1,
) -> int:
    c0 = " ".join(str(value) for value in first)
    c1 = " ".join(str(value) for value in second)
    return objects.add(
        (
            f"<< /FunctionType 2 /Domain [0 1] /Range [0 1 0 1 0 1] "
            f"/C0 [{c0}] /C1 [{c1}] /N {exponent} >>"
        ).encode("ascii")
    )


def mesh_vertex(
    flag: int | None,
    x: int,
    y: int,
    components: tuple[int, ...],
) -> list[tuple[int, int]]:
    fields: list[tuple[int, int]] = []
    if flag is not None:
        fields.append((flag, 2))
    fields.extend([(x, 8), (y, 8)])
    fields.extend((component, 8) for component in components)
    return fields


def patch_record(
    flag: int,
    points: list[tuple[int, int]],
    colors: list[tuple[int, int, int]],
) -> list[tuple[int, int]]:
    fields: list[tuple[int, int]] = [(flag, 2)]
    for x, y in points:
        fields.extend([(x, 8), (y, 8)])
    for color in colors:
        fields.extend((component, 8) for component in color)
    return fields


def type1_pages(objects: Objects, pages: int) -> tuple[int, int]:
    sampled = sampled_function(
        objects,
        bytes(
            [
                255, 0, 0,
                0, 255, 0,
                0, 0, 255,
                255, 255, 255,
            ]
        ),
        outputs=3,
    )
    red = sampled_function(objects, bytes([0, 255, 0, 255]), outputs=1)
    green = sampled_function(objects, bytes([0, 0, 255, 255]), outputs=1)
    blue = sampled_function(objects, bytes([64, 128, 192, 255]), outputs=1)
    combined = objects.add(
        (
            "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] "
            f"/Matrix [100 0 0 120 10 20] /BBox [20 25 100 130] "
            f"/Function {sampled} 0 R >>"
        ).encode("ascii")
    )
    components = objects.add(
        (
            "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] "
            f"/Matrix [90 0 0 120 135 20] /BBox [145 25 215 130] "
            f"/Function [{red} 0 R {green} 0 R {blue} 0 R] >>"
        ).encode("ascii")
    )
    first = page(
        objects,
        pages,
        b"q 25 30 70 90 re W n /Combined sh Q\n"
        b"q 150 30 60 90 re W n /Components sh Q",
        (
            f"<< /Shading << /Combined {combined} 0 R "
            f"/Components {components} 0 R >> >>"
        ),
    )

    calculator_source = b"{ pop dup 1 exch sub exch 0.25 }"
    calculator = objects.add(
        stream(
            (
                "<< /FunctionType 4 /Domain [0 1 0 1] "
                "/Range [0 1 0 1 0 1] "
                f"/Length {len(calculator_source)} >>"
            ),
            calculator_source,
        )
    )
    calculator_shading = objects.add(
        (
            "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] "
            f"/Matrix [100 0 0 55 10 90] /BBox [15 95 105 140] "
            f"/Function {calculator} 0 R >>"
        ).encode("ascii")
    )
    singular = objects.add(
        (
            "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] "
            f"/Matrix [0 0 0 0 180 120] /Function {calculator} 0 R >>"
        ).encode("ascii")
    )
    first_exp = exponential_function(objects, (1, 0, 0), (0, 1, 0), 1)
    second_exp = exponential_function(objects, (0, 0, 1), (1, 1, 0), 2)
    stitching = objects.add(
        (
            "<< /FunctionType 3 /Domain [0 1] /Range [0 1 0 1 0 1] "
            f"/Functions [{first_exp} 0 R {second_exp} 0 R] "
            "/Bounds [0.5] /Encode [0 1 0 1] >>"
        ).encode("ascii")
    )
    exponential_shading = objects.add(
        (
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [10 35 110 35] "
            f"/Function {first_exp} 0 R /Extend [true true] >>"
        ).encode("ascii")
    )
    stitching_shading = objects.add(
        (
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [130 35 230 35] "
            f"/Function {stitching} 0 R /Extend [true true] >>"
        ).encode("ascii")
    )
    second = page(
        objects,
        pages,
        b"q 10 10 100 50 re W n /Exponential sh Q\n"
        b"q 130 10 100 50 re W n /Stitching sh Q\n"
        b"q 15 95 90 45 re W n /Calculator sh Q\n/Singular sh",
        (
            f"<< /Shading << /Exponential {exponential_shading} 0 R "
            f"/Stitching {stitching_shading} 0 R "
            f"/Calculator {calculator_shading} 0 R "
            f"/Singular {singular} 0 R >> >>"
        ),
    )
    return first, second


def triangle_mesh_page(objects: Objects, pages: int) -> int:
    decode = "[0 100 0 100 0 1 0 1 0 1]"
    free_data = pack_records(
        [
            mesh_vertex(0, 0, 0, (255, 0, 0)),
            mesh_vertex(0, 100, 0, (0, 255, 0)),
            mesh_vertex(0, 50, 1, (0, 0, 255)),
            mesh_vertex(1, 100, 100, (255, 255, 255)),
            mesh_vertex(1, 100, 100, (0, 0, 0)),
        ]
    )
    free = objects.add(
        stream(
            (
                "<< /ShadingType 4 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode {decode} /Length {len(free_data)} >>"
            ),
            free_data,
        )
    )
    lattice_data = pack_records(
        [
            mesh_vertex(None, 0, 0, (255, 128, 0)),
            mesh_vertex(None, 100, 0, (128, 0, 255)),
            mesh_vertex(None, 0, 100, (0, 255, 255)),
            mesh_vertex(None, 100, 100, (255, 255, 0)),
        ]
    )
    lattice = objects.add(
        stream(
            (
                "<< /ShadingType 5 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow 2 "
                f"/Decode {decode} /Length {len(lattice_data)} >>"
            ),
            lattice_data,
        )
    )
    return page(
        objects,
        pages,
        b"q 1 0.2 0 1 10 30 cm 0 0 105 105 re W n /Free sh Q\n"
        b"q 0.9 0 0 0.9 135 25 cm 0 0 100 100 re W n /Lattice sh Q",
        f"<< /Shading << /Free {free} 0 R /Lattice {lattice} 0 R >> >>",
    )


def patch_meshes(objects: Objects) -> tuple[int, int]:
    decode = "[0 240 0 160 0 1 0 1 0 1]"
    colors = [
        (255, 0, 0),
        (0, 255, 0),
        (0, 0, 255),
        (255, 255, 255),
    ]
    first_points = [
        (10, 80), (40, 20), (80, 20), (110, 80),
        (125, 100), (125, 125), (110, 145),
        (80, 155), (40, 155), (10, 145),
        (0, 125), (0, 100),
    ]
    adjacent_points = [
        (150, 155), (180, 145), (190, 110),
        (205, 80), (195, 50), (170, 30),
        (145, 40), (125, 60),
    ]
    coons_data = pack_records(
        [
            patch_record(0, first_points, colors),
            patch_record(1, adjacent_points, [(255, 255, 0), (0, 255, 255)]),
        ]
    )
    coons = objects.add(
        stream(
            (
                "<< /ShadingType 6 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode {decode} /Length {len(coons_data)} >>"
            ),
            coons_data,
        )
    )
    tensor_points = [
        (10, 10), (55, 0), (105, 0), (150, 10),
        (180, 30), (180, 65), (150, 80),
        (105, 95), (55, 95), (10, 80),
        (0, 65), (0, 30),
        (35, 15), (125, 10), (130, 75), (30, 80),
    ]
    tensor_data = pack_records([patch_record(0, tensor_points, colors)])
    tensor = objects.add(
        stream(
            (
                "<< /ShadingType 7 /ColorSpace /DeviceRGB "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 2 "
                f"/Decode {decode} /Length {len(tensor_data)} >>"
            ),
            tensor_data,
        )
    )
    return coons, tensor


def patch_mesh_page(objects: Objects, pages: int, coons: int, tensor: int) -> int:
    return page(
        objects,
        pages,
        b"q 0 65 230 95 re W n /Coons sh Q\n"
        b"q 0.65 0 0 0.65 125 0 cm 0 0 175 100 re W n /Tensor sh Q",
        f"<< /Shading << /Coons {coons} 0 R /Tensor {tensor} 0 R >> >>",
    )


def composited_mesh_page(
    objects: Objects,
    pages: int,
    coons: int,
) -> int:
    group_content = b"/Coons sh"
    mesh_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 220 160] "
                "/Group << /S /Transparency /I true /K false >> "
                f"/Resources << /Shading << /Coons {coons} 0 R >> >> "
                f"/Length {len(group_content)} >>"
            ),
            group_content,
        )
    )
    gray_decode = "[0 100 0 100 0 1]"
    mask_data = pack_records(
        [
            mesh_vertex(None, 0, 0, (0,)),
            mesh_vertex(None, 100, 0, (255,)),
            mesh_vertex(None, 0, 100, (0,)),
            mesh_vertex(None, 100, 100, (255,)),
        ]
    )
    mask_mesh = objects.add(
        stream(
            (
                "<< /ShadingType 5 /ColorSpace /DeviceGray "
                "/BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow 2 "
                f"/Decode {gray_decode} /Length {len(mask_data)} >>"
            ),
            mask_data,
        )
    )
    mask_content = b"/MaskMesh sh"
    mask_group = objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 100 100] "
                "/Matrix [1 0 0 1 125 30] "
                "/Group << /S /Transparency /I true /CS /DeviceGray >> "
                f"/Resources << /Shading << /MaskMesh {mask_mesh} 0 R >> >> "
                f"/Length {len(mask_content)} >>"
            ),
            mask_content,
        )
    )
    mask_state = objects.add(
        (
            f"<< /Type /ExtGState /SMask << /S /Luminosity /G {mask_group} 0 R "
            "/BC [0] >> >>"
        ).encode("ascii")
    )
    alpha_state = objects.add(b"<< /Type /ExtGState /ca 0.75 >>")
    return page(
        objects,
        pages,
        b"0.95 g 0 0 240 160 re f\n"
        b"q 0.5 0 0 0.75 5 20 cm /Alpha gs /MeshGroup Do Q\n"
        b"q /Mask gs 0 0 1 rg 125 30 100 100 re f Q",
        (
            f"<< /XObject << /MeshGroup {mesh_group} 0 R >> "
            f"/ExtGState << /Mask {mask_state} 0 R /Alpha {alpha_state} 0 R >> >>"
        ),
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()

    first, second = type1_pages(objects, pages)
    third = triangle_mesh_page(objects, pages)
    coons, tensor = patch_meshes(objects)
    fourth = patch_mesh_page(objects, pages, coons, tensor)
    fifth = composited_mesh_page(objects, pages, coons)

    kids = " ".join(f"{reference} 0 R" for reference in [first, second, third, fourth, fifth])
    objects.set(pages, f"<< /Type /Pages /Kids [{kids}] /Count 5 >>".encode("ascii"))
    objects.set(catalog, f"<< /Type /Catalog /Pages {pages} 0 R >>".encode("ascii"))
    return objects.build()


MANAGED_PNG_SHA256: dict[str, list[str]] = {
    "dpi72-aa4-opaque": [
        "df0bebb34a70e2e7a4b52b04188a2a7abab2589a3febf030e95e93f68fad684f",
        "8c617e610172f72e1da3dd97b34dab73841a2d2accba249f5d12c286387ab232",
        "b25a263c060f51964fbb72de49c8e06e8fad8da98ca665a92c88a109ef865536",
        "e867fa17b6b2e20862f9d6814df0984586b7243d7993a5149b1ccdddff0d7f6f",
        "ba0228eb33b475b3e163f251d8777792ce792be707086249ae822c47573f221d",
    ],
    "dpi96-aa4-opaque": [
        "a4dfb5c9d8aca51d855c61f813972690f477946bc059f4a3d1ed8c8cfc91eebc",
        "7df8039bd2f4bbafe329c2ff1b9184fe73dc1befd0bfeaf7c9997da5030a3b90",
        "0ef84f8970c50c37ca17680ceb59e198d16c6251f30c61afe0f054658ecd5cc0",
        "4e92cd086f130379ad743e8ed6d7a2baccdce73cfb9a28097695df7ff43a4e60",
        "b703918e94fa9190849e0fd1f88bc745bd5667cb138dd2e159f76f8e30fcc55f",
    ],
    "dpi144-aa4-opaque": [
        "1bbc2c1340f5136cdd17ea5331812a15136544ea8f55b81ca5dabc25863d520e",
        "f6b360d7a27d217dccfb96d82c34a1f85f11f1dfe15687b4a5b997b4cbff2f3c",
        "4c9a87bf8984ae69614a938febed4b2f1680d4af2755bff53fe92e5deccb2dad",
        "d27422404d2409aa1f83be2d8307dc983db64f960dcdce73ea73f30764ce08c2",
        "13eb4b35be3076ae64240f3984c0f7945b2b92ecf2b66fe88b34dca235c31444",
    ],
    "dpi300-aa4-opaque": [
        "f9f7d3e6fd2b4392dacf1f1489d7e67181e58a32e295bd2b12083854fe8b8bec",
        "e3d1835df3d729d3c1bae3b526095d98874027fdd29efeb74c3cea44960c122a",
        "9e6fff91b4150b589c6f538ca8c3e5930d2e7086ec57ffd239c574a3f23f3377",
        "7d60b87099c55e06ebc04b6efd41c2c95292e9034e5ac2c49f356f15bd76e83f",
        "3f0993d7ad4f591fbffa2d5876fc3ea0446d6d5c3af464a06e8f3febae7f7d08",
    ],
}
MANAGED_TRANSPARENT_PAGE_SHA256: dict[str, str] = {
    "dpi72-aa4-page4": "5a6ce1a56129d3632a06d1068b44225754d92cd1d5696b2ed92538c93065b27e",
    "dpi96-aa4-page4": "4aa2bee749065a7d271283c27fd05991021941daaffe72e3b1d6bb7651f79562",
    "dpi144-aa4-page4": "bc5e2f4f0b3d9933849f8dd58195e175005693f35ab569eb45b55eea8ae32127",
    "dpi300-aa4-page4": "debeb2168dd684c22d80289456314781813325001616ebe065e828f6f002ff10",
}
MANAGED_SVG_SHA256: list[str] = [
    "4148f1f19848b532f527d6e6ef0b8a77fc168b1eca0920b2071ac5c16c15ae85",
    "39c921c307a3062243ff3ad13aceae6a3dd5b319a365370e9b5b74dc45403616",
    "90f8f63c60bbbea836bc4c2d8db063444eab7fd8401a9832320579c53fe3c20e",
    "6a9d836c2ee807a0f457a204519f88b67b774b00acc47332d3162bc28b0fd751",
    "b5d9d513dea5d3df16c27ec8e654516a206057bcb0c41f97427db606c4da642d",
]


def main() -> None:
    data = corpus()
    pdf = ROOT / "shading-alpha3.pdf"
    manifest = ROOT / "shading-alpha3-fixture.json"
    pdf.write_bytes(data)
    contents = json.dumps(
        {
            "file": pdf.name,
            "sha256": hashlib.sha256(data).hexdigest(),
            "pages": [
                "type1-sampled-and-component-functions",
                "type1-calculator-type2-type3-and-singular-matrix",
                "transformed-clipped-thin-and-degenerate-gouraud-meshes",
                "adaptive-adjacent-coons-and-curved-tensor-patches",
                "mesh-transparency-group-and-mesh-luminosity-mask",
            ],
            "managed_png_hash_mode": "canonical-png-content-v1",
            "managed_png_sha256": MANAGED_PNG_SHA256,
            "managed_transparent_page_sha256": MANAGED_TRANSPARENT_PAGE_SHA256,
            "managed_svg_hash_mode": "canonical-svg-content-v1",
            "managed_svg_sha256": MANAGED_SVG_SHA256,
        },
        indent=2,
    ) + "\n"
    manifest.write_bytes(contents.encode("utf-8"))


if __name__ == "__main__":
    main()
