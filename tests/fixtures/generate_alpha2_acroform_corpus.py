#!/usr/bin/env python3
"""Generate deterministic AcroForm regressions for Poppler.Net 0.9.0-alpha.2."""

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
                "/ID [<09000209000209000209000209000209> "
                "<09000209000209000209000209000209>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def form(
    objects: Objects,
    bbox: str,
    content: bytes,
    resources: str = "<< >>",
) -> int:
    return objects.add(
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /FormType 1 /BBox {bbox} "
                f"/Resources {resources} /Length {len(content)} >>"
            ),
            content,
        )
    )


def dictionary(objects: Objects, value: str) -> int:
    return objects.add(f"<< {value} >>".encode("ascii"))


def set_page(
    objects: Objects,
    reference: int,
    parent: int,
    content: bytes,
    annotations: list[int],
) -> None:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    annots = " ".join(f"{item} 0 R" for item in annotations)
    objects.set(
        reference,
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 360 260] "
            f"/Resources << >> /Contents {content_ref} 0 R "
            f"/Annots [{annots}] >>"
        ).encode("ascii"),
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs = [objects.reserve() for _ in range(4)]
    font = dictionary(
        objects,
        "/Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding",
    )
    font_resources = f"<< /Font << /Helv {font} 0 R >> >>"

    text_appearance = form(
        objects,
        "[0 0 190 30]",
        b"\n".join(
            [
                b"0.92 0.96 1 rg 0 0 190 30 re f",
                b"0 0 0.7 RG 1 w 0.5 0.5 189 29 re S",
                b"BT /Helv 11 Tf 0 0 0.8 rg 5 10 Td (MI LATTANZIO) Tj ET",
            ]
        ),
        font_resources,
    )
    check_off = form(
        objects,
        "[0 0 24 24]",
        b"1 g 0 0 24 24 re f 0 G 1 w 0.5 0.5 23 23 re S",
    )
    check_yes = form(
        objects,
        "[0 0 24 24]",
        b"1 g 0 0 24 24 re f 0 G 1 w 0.5 0.5 23 23 re S "
        b"0 0.45 0 RG 3 w 5 12 m 10 6 l 20 19 l S",
    )
    radio_off = form(
        objects,
        "[0 0 24 24]",
        b"1 g 0 0 24 24 re f 0 G 1 w 2 2 20 20 re S",
    )
    radio_red = form(
        objects,
        "[0 0 24 24]",
        b"1 g 0 0 24 24 re f 0 G 1 w 2 2 20 20 re S "
        b"1 0 0 rg 7 7 10 10 re f",
    )
    radio_blue = form(
        objects,
        "[0 0 24 24]",
        b"1 g 0 0 24 24 re f 0 G 1 w 2 2 20 20 re S "
        b"0 0.2 0.9 rg 7 7 10 10 re f",
    )
    orphan_appearance = form(
        objects,
        "[0 0 120 30]",
        b"0.85 0.75 1 rg 0 0 120 30 re f",
    )

    person = objects.reserve()
    name = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {person} 0 R "
            f"/P {page_refs[0]} 0 R /FT /Tx /T (name) /TU (Full name) "
            "/TM (person_name) /Rect [20 205 210 235] /V (Mi Lattanzio) "
            "/DV (Default Name) /DA (/Helv 11 Tf 0 0 0.8 rg) /Q 0 "
            "/MK << /BC [0 0 0.7] /BG [0.92 0.96 1] >> "
            f"/BS << /W 1 /S /S >> /AP << /N {text_appearance} 0 R >>"
        ),
    )
    biography = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {person} 0 R "
            f"/P {page_refs[0]} 0 R /FT /Tx /T (biography) "
            "/Rect [20 125 210 190] /V (MANAGED\\nFORM FIELD) /Ff 4096 "
            "/DA (/Helv 9 Tf 0.65 0 0 rg) /Q 1 "
            "/MK << /BC [0.5 0 0] /BG [1 0.94 0.94] >> /BS << /W 1 >>"
        ),
    )
    password = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {person} 0 R "
            f"/P {page_refs[0]} 0 R /FT /Tx /T (password) "
            "/Rect [230 205 340 235] /V (secret) /Ff 8192 "
            "/MK << /BC [0.3] /BG [0.95] >> /BS << /W 1 >>"
        ),
    )
    code = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {person} 0 R "
            f"/P {page_refs[0]} 0 R /FT /Tx /T (code) "
            "/Rect [230 155 340 185] /V (A42Z) /Ff 16777216 /MaxLen 4 "
            "/Q 2 /MK << /BC [0 0.3 0] /BG [0.92 1 0.92] >> /BS << /W 1 >>"
        ),
    )
    objects.set(
        person,
        (
            f"<< /T (person) /Kids [{name} 0 R {biography} 0 R "
            f"{password} 0 R {code} 0 R] >>"
        ).encode("ascii"),
    )

    accept = objects.reserve()
    accept_widget = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {accept} 0 R "
            f"/P {page_refs[1]} 0 R /Rect [25 205 49 229] /AS /Off "
            f"/AP << /N << /Off {check_off} 0 R /Yes {check_yes} 0 R >> >>"
        ),
    )
    objects.set(
        accept,
        (
            f"<< /FT /Btn /T (accept) /V /Yes /DV /Off "
            f"/Kids [{accept_widget} 0 R] >>"
        ).encode("ascii"),
    )

    colour = objects.reserve()
    red_widget = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {colour} 0 R "
            f"/P {page_refs[1]} 0 R /Rect [25 155 49 179] /AS /Off "
            f"/AP << /N << /Off {radio_off} 0 R /Red {radio_red} 0 R >> >>"
        ),
    )
    blue_widget = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {colour} 0 R "
            f"/P {page_refs[1]} 0 R /Rect [75 155 99 179] /AS /Off "
            f"/AP << /N << /Off {radio_off} 0 R /Blue {radio_blue} 0 R >> >>"
        ),
    )
    objects.set(
        colour,
        (
            f"<< /FT /Btn /T (colour) /Ff 49152 /V /Blue "
            f"/Kids [{red_widget} 0 R {blue_widget} 0 R] >>"
        ).encode("ascii"),
    )
    fallback_check = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[1]} 0 R "
            "/FT /Btn /T (fallback-check) /Rect [25 95 51 121] "
            "/V /On /AS /Off /MK << /BC [0 0.4 0] /BG [0.92 1 0.92] >> "
            "/BS << /W 1 >>"
        ),
    )
    push = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[1]} 0 R "
            "/FT /Btn /T (submit) /TU (Submit form) /Ff 65536 "
            "/Rect [150 190 315 230] "
            "/MK << /CA (SUBMIT) /BC [0.1 0.25 0.6] /BG [0.85 0.9 1] >> "
            "/BS << /W 2 >>"
        ),
    )

    country = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[2]} 0 R "
            "/FT /Ch /T (country) /Ff 131072 /Rect [20 205 180 235] "
            "/Opt [[(it) (Italy)] [(fr) (France)] [(de) (Germany)]] /V (it) "
            "/MK << /BC [0.2] /BG [1] >> /BS << /W 1 >>"
        ),
    )
    interests = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[2]} 0 R "
            "/FT /Ch /T (interests) /Ff 2097152 /Rect [20 105 180 190] "
            "/Opt [(Code) (PDF) (Security) (Graphics)] /V [(Code)] /I [1 2] /TI 1 "
            "/DA (/Helv 8 Tf 0.1 0.1 0.5 rg) "
            "/MK << /BC [0.1 0.1 0.5] /BG [0.94 0.94 1] >> /BS << /W 1 >>"
        ),
    )
    custom = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[2]} 0 R "
            "/FT /Ch /T (custom) /Ff 393216 /Rect [200 205 340 235] "
            "/Opt [(One) (Two)] /V (Custom value) /Q 2 "
            "/MK << /BC [0.4 0.2 0] /BG [1 0.96 0.9] >> /BS << /W 1 >>"
        ),
    )
    signature = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[2]} 0 R "
            "/FT /Sig /T (approval) /Rect [200 120 340 180] "
            "/V << /Type /Sig /Filter /Adobe.PPKLite "
            "/SubFilter /adbe.pkcs7.detached /Name (Signer) >> "
            "/MK << /CA (APPROVED) /BC [0 0.35 0] /BG [0.9 1 0.9] >> "
            "/BS << /W 2 >>"
        ),
    )

    settings = objects.reserve()
    settings_widget = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /Parent {settings} 0 R "
            f"/P {page_refs[3]} 0 R /Rect [20 195 220 230] "
            "/MK << /BC [0 0.45 0] /BG [0.92 1 0.92] >> /BS << /W 1 >>"
        ),
    )
    objects.set(
        settings,
        (
            f"<< /FT /Tx /T (settings) /Ff 1 /V (INHERITED VALUE) "
            f"/DA (/Helv 10 Tf 0 0.45 0 rg) /Kids [{settings_widget} 0 R] >>"
        ).encode("ascii"),
    )
    circular_a = objects.reserve()
    circular_b = objects.reserve()
    objects.set(
        circular_a,
        f"<< /T (loop-a) /Kids [{circular_b} 0 R] >>".encode("ascii"),
    )
    objects.set(
        circular_b,
        (
            f"<< /T (loop-b) /Parent {circular_a} 0 R "
            f"/Kids [{circular_a} 0 R] >>"
        ).encode("ascii"),
    )
    orphan = dictionary(
        objects,
        (
            f"/Type /Annot /Subtype /Widget /P {page_refs[3]} 0 R "
            f"/Rect [240 195 340 225] /AP << /N {orphan_appearance} 0 R >>"
        ),
    )

    set_page(
        objects,
        page_refs[0],
        pages,
        b"0.98 g 0 0 360 260 re f 0.9 g 10 115 210 130 re f",
        [name, biography, password, code],
    )
    set_page(
        objects,
        page_refs[1],
        pages,
        b"0.97 g 0 0 360 260 re f 0.92 g 10 80 110 165 re f",
        [accept_widget, red_widget, blue_widget, fallback_check, push],
    )
    set_page(
        objects,
        page_refs[2],
        pages,
        b"0.98 g 0 0 360 260 re f 0.93 g 10 95 180 150 re f",
        [country, interests, custom, signature],
    )
    set_page(
        objects,
        page_refs[3],
        pages,
        b"0.96 g 0 0 360 260 re f",
        [settings_widget, orphan],
    )

    all_fields = [
        person,
        accept,
        colour,
        fallback_check,
        push,
        country,
        interests,
        custom,
        signature,
        settings,
        circular_a,
    ]
    fields = " ".join(f"{reference} 0 R" for reference in all_fields)
    acro_form = dictionary(
        objects,
        (
            f"/Fields [{fields}] /NeedAppearances true "
            f"/DR {font_resources} /DA (/Helv 10 Tf 0 g)"
        ),
    )
    objects.set(
        catalog,
        (
            f"<< /Type /Catalog /Pages {pages} 0 R "
            f"/AcroForm {acro_form} 0 R >>"
        ).encode("ascii"),
    )
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Kids "
            f"[{' '.join(f'{page} 0 R' for page in page_refs)}] "
            f"/Count {len(page_refs)} >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    data = corpus()
    pdf = ROOT / "acroform-alpha2.pdf"
    pdf.write_bytes(data)
    (ROOT / "acroform-alpha2-fixture.json").write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "hierarchical-text-password-and-comb",
                    "button-state-selection-and-fallbacks",
                    "choice-fields-and-signature",
                    "inheritance-cycle-and-orphan-widget",
                ],
                "managed_png_sha256": [
                    "b8459dc201c595b58ae81e1a53d3d6bf2dc24428b1289e00368cfe146a2380b8",
                    "3cfeda9506df006f2d7f857e7504989eb71afdc2b8dda6ed201c7985d862df2d",
                    "4f4c95dd0eaff7237945909a6dccf98b784f0da60379bf979e1bf2be05004d01",
                    "80bcdbb957481fc5409b56aaf67af6d93b61750ce25f65b020059afbee6c1017",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
