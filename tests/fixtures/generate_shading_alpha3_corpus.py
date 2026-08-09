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
        "b0f2f572830e916ea67690ec9d516e284931bf7289faad0a07eb46f3f26ed31a",
        "42f7a2c073cc4c35f762d9457c9a0cb668175f53571f1f926bb49153b22c1d0f",
        "c420909653beed165bf5d3de9592a957afbf8d97ee459a6ee185d79a35156b8b",
        "2d30ce42c498bc77374c7305a73d026e92d284a18a846637425bbf7a9658006d",
        "10c5b75067271643dddcd1355a09e4fa79909723e840807a9dda690ae8768fc9",
    ],
    "dpi96-aa4-opaque": [
        "5379c4b8f62e021eb4f811a338aa45bc5f585d803ad8d3851cc3ca7f0b44a9a2",
        "8c583b1646f643f4860554af35d616e15efb66659239a34834a955f2f1695ac6",
        "39060b1797cb041dac130423bf7cc8342573e08da3246eee9126fafb28fecee8",
        "36ff434eb4cc4af01ad83ac16b89f97bc92d41ef2b32216eb8d0e963b8e069a0",
        "75a7462c0fae2de8b895de2bd5129aca2ad2ca577dfc23541404f66b85e5a76f",
    ],
    "dpi144-aa4-opaque": [
        "89ecc336968cb040d2abbcd2d6225a21273f94dc091230d9bad71250478dd698",
        "6097358f7eff1f14af7c7aa1ac1edd50741428f4862b59749ed3b24aeb73fe00",
        "8c132c23caefca8a0cea645590d5d143b15b5c25b832b097ba0a0c9fc59e19bd",
        "a0ccf01fa396cdaa306ac30bf708e18b1879b4e5df156fa8d699ddf898306c07",
        "9fcb89c4c23be5f6d8f7c4f5e291dd379f3e4065204fe13cb286c2ef74c34c39",
    ],
    "dpi300-aa4-opaque": [
        "69c66a85d0bb0e41c67f3e375b62351139ae2377abb009eb85146fe8367b735c",
        "e0c7f2a2ee957bf2a4b2f4314370ae5c739ea51867f492541769a3a7d9bc2a7f",
        "aa86dd170952f3ef0a79e165cfccca5c4130cf345031f51ee0bb330685986bee",
        "24f2aec8a93f61fb4cdd6f1f2a25d788d9127c0a8de50ca2b7e431fbbee895b2",
        "a06855256ccd8482147774719b064c44dbb1f1c1f9decd029f979a84154ccfe0",
    ],
}
MANAGED_TRANSPARENT_PAGE_SHA256: dict[str, str] = {
    "dpi72-aa4-page4": "14a947ba22a6852b5789017ca367174798ed4623e7c8cded3f6a73df4c76bb61",
    "dpi96-aa4-page4": "0364e3ef22326ff4ffc8dca7c24098fb3c96040a36fe7f4ed2e6380f6c8e1adb",
    "dpi144-aa4-page4": "8c8ee9414eff09a66671f2a26fb61bd9f485b49b1481647c99652185989fe5a6",
    "dpi300-aa4-page4": "1c3387c0c4b24070390a2b53d8a41d9e2dfa199575564d799389683e337d846f",
}
MANAGED_SVG_SHA256: list[str] = [
    "4a00a65db78f9802216973b6da6a208a2197d48ae82b5d9a10e93b11770cdaec",
    "889f3a096717c5a801c719869e8fe09121dc6df0e37282107a082decb3406178",
    "14c8f4ae65eb0837d51aa472751bc256b85f5eabccf1e6d3fffb983b2da30578",
    "fe540b7538916f5b1fa02dc48d70719bc03b4ee91913c8da227ff44ec4905697",
    "a9f9fd5e2a853b3f776516ab65945b8923d99fda84c6420bf3dd849a1b73e099",
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
            "managed_png_sha256": MANAGED_PNG_SHA256,
            "managed_transparent_page_sha256": MANAGED_TRANSPARENT_PAGE_SHA256,
            "managed_svg_sha256": MANAGED_SVG_SHA256,
        },
        indent=2,
    ) + "\n"
    manifest.write_bytes(contents.encode("utf-8"))


if __name__ == "__main__":
    main()
