#!/usr/bin/env python3
"""Generate the deterministic 0.12.0-alpha.1 stroke/clip corpus."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent
PDF_PATH = ROOT / "raster-geometry-alpha1.pdf"
MANIFEST_PATH = ROOT / "raster-geometry-alpha1-fixture.json"

PAGE_NAMES = [
    "caps-joins-and-miter-limits",
    "zero-length-and-hairlines",
    "dash-phase-odd-zero-and-closure",
    "anisotropic-shear-and-reflection",
    "tight-curves-cusps-and-reversals",
    "self-intersection-and-collinearity",
    "nested-nonzero-evenodd-page-edge-clips",
    "cropbox-edge-and-page-rotation",
]

# Approved managed render matrix. Keeping it in the generator makes both the
# PDF and its manifest byte-identical when the corpus is regenerated.
RENDER_HASHES: dict[str, list[str]] = {
    "dpi96-aa1-opaque": [
        "3c777630490e168ffb27d6cf82cdd9c25cefee86b5a4ba114fde0fe7813d9848",
        "f25cc79531ed64a241a395bd4ec4f6ea91c96b8e84c3d606240c697e28f79643",
        "2a974901a9dbd84b631c09fc89401fe6b34bca33c5d768e13df8b16c4c228486",
        "40055955ad0b81bf09f3319093b469128e39f1592a56121035580b2b72a34632",
        "0c620e6b3cf09aa5b5cbaa52ed999a5d043bd0407d149e30bd289e91dad371dc",
        "b4d9c676f75793ab8ab8c356e2dee1b2efe97069e13f2015f5f390dc1494dba7",
        "fc480c339d209399dcd1cf33c4ba0076677d3f8002e92fe1d768864bff9e3466",
        "cf1a0a90f6ddc558935223446f9c8382040a00bb7a78832662b6fd03ab0e5376",
    ],
    "dpi96-aa1-transparent": [
        "17945faa560b6d91875e1646bbf64c3fcf5cd35d736f147fe7fb9b85f1269846",
        "4f50f9afdb597e1da0bd7404ab3fac8f059f4ae56ab8669e000a4d8eee65855a",
        "b82d9476d03632d0b152e92f00a9c82e7cd670255f1fb64e1325b82cbb8c859e",
        "f9c7deee9061e5ff38a5647454efe7ec69b1a159047b4567fa7f1aaddf77577e",
        "d7b09fca31e5e48ca37a8df3aa7840a09db8c9b94932bf3441ee43a89047714f",
        "74668f87af96ae726cfc1b8d9d702d7edfdbd02b141119c0b0bfc1c4abb76beb",
        "da342b98bd5eb87fb7c9a448aa94148a03b4afe0b98cf7e067a449e54d23d032",
        "ad1f68a797ac791b76578fe87b3d8dd4890f5e95a522e8d12a6d8cdd353ae1c1",
    ],
    "dpi96-aa4-opaque": [
        "47278a91cf0145d91865620985a2637c07ac3e321320b54ed52d245ab33912f1",
        "5b73329313ebbbc987c99384eaa31e1e62a5ce634289a763114553302468f45f",
        "93112817728305cb906e2413420160553159107f1e7e1bccb1c7b0bcd54b40e5",
        "fe0a61d43820548b2a732811b63ff621f2d182c3bdedff5e65f6f9e60c95d655",
        "6052e592d5f531e92d58a893e480da62780a29f7499ca9091d45c7cc8fe44f4e",
        "aa6025fe969423b6c01580da6c169a28c0460d970871bb8e7145e8156ccccc10",
        "8839d35318039287d9722170e15efb3d2118cba1c7ab4878c40dee9e54a7989d",
        "28c54206e25191df5bc45f5fe53bc53e84b1a7a6f1ccfd96a5b65be279015329",
    ],
    "dpi96-aa4-transparent": [
        "eed1b8952d77e459a8951ba869ad4aa4da8d29c139ecf6adb667befa2d7eb77a",
        "bbd1a1cf4ba414ae0635e6fa43ec3807774567c5a27e592fe8c95900d8dad22f",
        "3c568073897eb82485ab2c3efc01bd5fd130c51656feb5238205e4f64dfa9c76",
        "632608d3b66dfb9f74235201cfd4c2a144a7001b4b63d7e7685096002edd1446",
        "258f97f58bd31e9b686ab7c99419dbd778f4b2154509536ed4bda07eac1bf7a5",
        "be497e2f867976f4c806f3804fc130bec521b8bf3efdbafb74ca1a28fcf3847e",
        "1577c5cc672d3a229da5e2d174fac9e9eb6d44b61d4b48578df49470e23d1960",
        "d88a76546452491d0518eea554cb9ebb0fef67bc4d3da731e5f7693c449dc27c",
    ],
    "dpi300-aa1-opaque": [
        "d741945434f2651382f58f37eabc2ae53e037786acb82e3a551ea54b18f59371",
        "20ab12037a5e99740bc9775f1ffb98d60873a961977d6fd833fbab874f37b66a",
        "23b149b5a4adc16d213b2c85517cbf901bf7d03c619194b2aa9bac24afabe989",
        "bc53cfa10182401c670d6fdcf292968576f735fedb82bf94234b0e1b15be196c",
        "95643b9e79cb0fd2fe29c1a7b9462cb844888f7d6aee9aa21d18379c9610e037",
        "a312f6393acaa17dfbe473cacc8a3c65eef7ea1757f353ad6bf2fd1132b28e62",
        "5183a7a06a5fc4eb8e03640fe3a91117b7de531e744d38397c257dc02add3017",
        "830388f7f8d37453dcf104e7e1ca02ad2a9fba238487291b5a9b7ae6742b18ec",
    ],
    "dpi300-aa1-transparent": [
        "9f5c545041aa10c34a4cda8e1c4d676568d6ce8edf82f0b28c35010bcc5cb38d",
        "1b173d38f4b81f9c4f0ff3e0013bb63864e25324ea3f8c56259d97f7b25b23de",
        "3476201c86407cd19d2b8affa654cb4e8523569dca1888f04e6536636136dfb5",
        "ae97492be1858402222624c8ac524b102f6bce9c6b63266b5ba04190500f9f69",
        "37e97337f1b838cc03d4ead5b848916b1515b14dc74d5929b74c478005e64db6",
        "f2b643e4e4bb0a1c70df7230161f7bf6c0e707bfdd5c386a54db27109864762c",
        "abab164601b05f6a8a9589d7a58f1e64bb3cc987c78090611fe488a9098e9eae",
        "73984bb662c474343c14b472a803907eed04fe5f04bb00e69d06d4f2291b3ebb",
    ],
    "dpi300-aa4-opaque": [
        "4f19f37a4333ba86999ce8f687085397ab4168bbf951c6ae7bee5901fc785be1",
        "7cae59f71cee1f0433adf7277df3cadb58d3623b2095efd794ad57bd2252b38c",
        "2de1fe56bb00ce2c5dd7248e19ea670705aea9a723770a439a64fcdf44a21cf1",
        "115b6cadca8d32d1bb81e37422d1d2d61e6f2d19fe86aa29859a264e5634feab",
        "cc7046e1023a1ffc36434a5da440c955e2ec01389737f3d5db852c70f1aeacd8",
        "f27b776398d634fd0dcd351eabde2d02526712213e0780a3b2e345cf96a03409",
        "e6cb71c50392fa477509b4ce1e89f83706590e73580bf8971c3296f379b1cfe2",
        "6acd14fa81e4020bb6965a43d92d8ca45104e74d7cb3e35dcf42f2867edefcd4",
    ],
    "dpi300-aa4-transparent": [
        "3330a38e545a933aa971ea90d936a55b61f19be524c26484fd59aec1bd20d50e",
        "00cfa0e9532550b6ba0ef49ae5e11de1d719f733ea4cc381ac21a2dde232e230",
        "edf21a51d3985369447a086139035a0d3af9e685e59b4792159f002db07eccee",
        "e0df127be265f2bd82f308c8771ba7abff7b015e6da598bbdee9fdd513844daa",
        "2970955ed970d70c429ceb64a3aafa284da7e1ed99c9d88fb997a148b99f165d",
        "991b585bf2b0b22e3588eba51d334c41a62932daff2b160a3094ebe7ac48cb01",
        "415109adf8e0e8c0c93bb75347a6c557d76e53ac25d8a261bf21ed809cb00705",
        "52799798a0f30dbb04f96e94e28aef9c458cc3837501eab5b8aeab1d9a55188b",
    ],
}


def stream(data: str) -> bytes:
    payload = data.strip().encode("ascii") + b"\n"
    return (
        f"<< /Length {len(payload)} >>\nstream\n".encode("ascii")
        + payload
        + b"endstream"
    )


def build_pdf(contents: list[str]) -> bytes:
    kids = " ".join(f"{3 + index * 2} 0 R" for index in range(len(contents)))
    objects: list[bytes] = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        f"<< /Type /Pages /Kids [{kids}] /Count {len(contents)} >>".encode("ascii"),
    ]
    for index, content in enumerate(contents):
        page_object = 3 + index * 2
        content_object = page_object + 1
        crop = " /CropBox [12 10 228 150]" if index == 7 else ""
        rotation = " /Rotate 90" if index == 7 else ""
        objects.append(
            (
                f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 160]"
                f"{crop}{rotation} /Resources << >> /Contents {content_object} 0 R >>"
            ).encode("ascii")
        )
        objects.append(stream(content))

    output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for number, obj in enumerate(objects, start=1):
        offsets.append(len(output))
        output.extend(f"{number} 0 obj\n".encode("ascii"))
        output.extend(obj)
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


def page_contents() -> list[str]:
    return [
        # Caps at three widths; joins and two miter-limit outcomes.
        """
        0 0 0 RG
        2 w 0 J 16 142 m 70 142 l S
        6 w 1 J 16 125 m 70 125 l S
        12 w 2 J 16 102 m 70 102 l S
        0.85 0.1 0.08 RG 10 w
        0 j 10 M 92 106 m 118 145 l 144 106 l S
        0 j 2 M 154 106 m 180 145 l 206 106 l S
        0.05 0.35 0.85 RG 9 w
        1 j 22 28 m 50 74 l 78 28 l S
        2 j 92 28 m 120 74 l 148 28 l S
        0 j 176 28 m 202 74 l 226 28 l S
        """,
        # Zero length with all caps plus horizontal/vertical/transformed hairlines.
        """
        0 0 0 RG 14 w
        0 J 28 128 m 28 128 l S
        1 J 72 128 m 72 128 l S
        2 J 116 128 m 116 128 l S
        0.8 0.1 0.1 RG 0 w
        14 92 m 226 92 l S
        0.1 0.3 0.85 RG
        42 18 m 42 78 l S
        q 1 0.55 0.35 1 0 0 cm 72 20 m 176 20 l S Q
        q 8 0 0 0.25 176 42 cm 0 0 m 6 0 l S Q
        q 0 0 1 0 204 20 cm 0 0 m 30 0 l S Q
        """,
        # Negative phase, odd pattern, zero elements, continuity and closed seam.
        """
        0 0 0 RG 6 w 1 J 1 j
        [24 9] -17 d 12 140 m 80 140 l 80 108 l 146 108 l S
        0.8 0.1 0.1 RG [13 5 3] -11 d
        14 84 m 68 84 l 96 60 l 142 84 l 224 84 l S
        0.05 0.35 0.85 RG [0 8 12 0] 0 d
        12 42 m 224 42 l S
        0.1 0.6 0.25 RG [17 6] -10 d
        160 104 62 48 re S
        """,
        # The same user-space strokes through anisotropy, shear and reflection.
        """
        0 0 0 RG 5 w 1 J 0 j
        q 18 0 0 0.22 12 132 cm 0 0 m 10 0 l 10 80 l S Q
        0.8 0.1 0.1 RG
        q 0.22 0 0 18 84 12 cm 0 0 m 0 7 l 55 7 l S Q
        0.05 0.35 0.85 RG
        q 1 0.65 0.8 1 20 38 cm 0 0 m 50 0 l 68 34 l S Q
        0.1 0.6 0.25 RG
        q -1 0 0 1 232 0 cm 18 24 m 62 68 l 104 24 l S Q
        """,
        # Tight cubics, near-cusps and exact reversals.
        """
        0 0 0 RG 7 w 1 J 1 j
        12 126 m 38 154 62 94 88 126 c 112 154 136 94 160 126 c S
        0.8 0.1 0.1 RG 9 w 0 j 3 M
        18 70 m 68 142 68 -2 118 70 c 118 70 118 70 118 70 c S
        0.05 0.35 0.85 RG 8 w 2 j
        142 30 m 206 94 l 158 46 l 222 110 l S
        0.1 0.6 0.25 RG 5 w 1 j
        138 128 m 174 152 198 96 226 132 c S
        """,
        # Self intersections, duplicate points and nearly collinear joins.
        """
        0 0 0 RG 8 w 0 j 8 M
        120 148 m 137 98 l 190 98 l 147 67 l 164 18 l
        120 48 l 76 18 l 93 67 l 50 98 l 103 98 l h S
        0.8 0.1 0.1 RG 6 w 2 j
        14 132 m 14 132 l 54 132 l 94 132.000001 l 134 132 l S
        0.05 0.35 0.85 RG 7 w 1 j
        18 26 m 72 78 l 18 78 l 72 26 l S
        0.1 0.6 0.25 RG 5 w
        150 32 m 204 32 l 204 32 l 204 82 l S
        """,
        # Page-edge clip plus nested nonzero/even-odd clips.
        """
        q 0 0 240 160 re W n
        10 10 220 140 re W n
        28 24 184 112 re 76 52 88 56 re W* n
        0.92 0.92 0.2 rg 0 0 240 160 re f
        0.8 0.1 0.1 RG 16 w 1 J
        -20 20 m 260 140 l S
        0.05 0.35 0.85 RG 10 w
        -10 80 m 250 80 l S
        Q
        0 0 0 RG 4 w 0 0 240 160 re S
        """,
        # CropBox boundaries, rotated page, mirror and a singular CTM to skip.
        """
        q 12 10 216 140 re W n
        0.05 0.35 0.85 RG 12 w 2 J 1 j
        12 10 m 228 150 l S
        0.8 0.1 0.1 RG 8 w [16 5] -9 d
        12 150 m 228 10 l S
        0.1 0.6 0.25 RG 6 w
        q -1 0 0 1 240 0 cm 24 32 m 120 132 l 216 32 l S Q
        0 0 0 RG q 1 0 1 0 0 0 cm 20 20 m 220 140 l S Q
        Q
        """,
    ]


def main() -> None:
    pdf = build_pdf(page_contents())
    PDF_PATH.write_bytes(pdf)
    manifest = {
        "file": PDF_PATH.name,
        "sha256": hashlib.sha256(pdf).hexdigest(),
        "pages": PAGE_NAMES,
        "render_matrix": {
            "dpi": [96, 300],
            "antialiasing": [1, 4],
            "background": ["opaque", "transparent"],
        },
        "managed_png_sha256": RENDER_HASHES,
    }
    MANIFEST_PATH.write_text(
        json.dumps(manifest, indent=2, sort_keys=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )


if __name__ == "__main__":
    main()
