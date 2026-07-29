#!/usr/bin/env python3
"""Generate deterministic optional-content regressions for Poppler.Net 0.9.0-alpha.3."""

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
                "/ID [<09000309000309000309000309000309> "
                "<09000309000309000309000309000309>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def form(
    objects: Objects,
    bbox: str,
    content: bytes,
    resources: str = "<< >>",
    extra: str = "",
) -> int:
    return objects.add(
        stream(
            (
                f"<< /Type /XObject /Subtype /Form /BBox {bbox} "
                f"/Resources {resources} {extra}/Length {len(content)} >>"
            ),
            content,
        )
    )


def image(objects: Objects, colour: bytes, optional_content: int) -> int:
    return objects.add(
        stream(
            (
                "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 "
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 "
                f"/OC {optional_content} 0 R /Length {len(colour)} >>"
            ),
            colour,
        )
    )


def annotation(objects: Objects, dictionary: str) -> int:
    return objects.add(f"<< /Type /Annot {dictionary} >>".encode("ascii"))


def set_page(
    objects: Objects,
    reference: int,
    parent: int,
    content: bytes,
    resources: str,
    annotations: list[int] | None = None,
) -> None:
    content_ref = objects.add(stream(f"<< /Length {len(content)} >>", content))
    annots = ""
    if annotations:
        refs = " ".join(f"{item} 0 R" for item in annotations)
        annots = f"/Annots [{refs}] "
    objects.set(
        reference,
        (
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 400 300] "
            f"/Resources {resources} /Contents {content_ref} 0 R {annots}>>"
        ).encode("ascii"),
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs = [objects.reserve() for _ in range(4)]

    red = objects.add(
        b"<< /Type /OCG /Name (Red plans) /Intent [/View /Design] "
        b"/Usage << /View << /ViewState /ON >> >> >>"
    )
    blue = objects.add(
        b"<< /Type /OCG /Name (Blue notes) /Intent /View "
        b"/Usage << /View << /ViewState /OFF >> >> >>"
    )
    green = objects.add(
        b"<< /Type /OCG /Name (Locked green) /Intent /View >>"
    )
    any_on = objects.add(
        (
            f"<< /Type /OCMD /OCGs [{red} 0 R {blue} 0 R] "
            "/P /AnyOn >>"
        ).encode("ascii")
    )
    all_on = objects.add(
        (
            f"<< /Type /OCMD /OCGs [{red} 0 R {blue} 0 R] "
            "/P /AllOn >>"
        ).encode("ascii")
    )
    any_off = objects.add(
        (
            f"<< /Type /OCMD /OCGs [{red} 0 R {blue} 0 R] "
            "/P /AnyOff >>"
        ).encode("ascii")
    )
    all_off = objects.add(
        (
            f"<< /Type /OCMD /OCGs [{red} 0 R {blue} 0 R] "
            "/P /AllOff >>"
        ).encode("ascii")
    )
    expression = objects.add(
        (
            f"<< /Type /OCMD /VE [/And {red} 0 R "
            f"[/Not {blue} 0 R]] >>"
        ).encode("ascii")
    )
    font = objects.add(
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica "
        b"/Encoding /WinAnsiEncoding >>"
    )

    properties = (
        f"<< /Red {red} 0 R /Blue {blue} 0 R /Green {green} 0 R "
        f"/AnyOn {any_on} 0 R /AllOn {all_on} 0 R "
        f"/AnyOff {any_off} 0 R /AllOff {all_off} 0 R "
        f"/Expr {expression} 0 R >>"
    )
    base_resources = (
        f"<< /Font << /F1 {font} 0 R >> /Properties {properties} >>"
    )

    page_one = b"\n".join(
        [
            b"0.96 g 0 0 400 300 re f",
            b"0 G 1 w 10 10 380 280 re S",
            b"BT /F1 13 Tf 0 g 20 278 Td (OPTIONAL CONTENT DEFAULT) Tj ET",
            b"/OC /Red BDC",
            b"1 0.2 0.2 rg 20 200 165 55 re f",
            b"BT /F1 12 Tf 0 g 30 225 Td (VISIBLE RED TEXT) Tj ET",
            b"EMC",
            b"/OC /Blue BDC",
            b"0.2 0.3 1 rg 215 200 165 55 re f",
            b"BT /F1 12 Tf 1 g 225 225 Td (HIDDEN BLUE TEXT) Tj ET",
            b"EMC",
            b"/OC /AnyOn BDC 0.9 0.55 0.1 rg 20 120 165 55 re f EMC",
            b"/OC /AllOn BDC 0.55 0.2 0.8 rg 215 120 165 55 re f EMC",
            b"/OC /Expr BDC 0.2 0.75 0.35 rg 20 40 165 55 re f EMC",
            b"/OC /Red BDC",
            b"0.85 0.85 0.85 rg 215 40 165 55 re f",
            b"/OC /Blue BDC 0 0 0 rg 230 55 135 25 re f EMC",
            b"0.8 0 0 rg 230 55 135 25 re S",
            b"EMC",
        ]
    )
    set_page(
        objects,
        page_refs[0],
        pages,
        page_one,
        base_resources,
    )

    red_form = form(
        objects,
        "[0 0 150 55]",
        b"1 0.25 0.25 rg 0 0 150 55 re f",
        extra=f"/OC {red} 0 R ",
    )
    blue_form = form(
        objects,
        "[0 0 150 55]",
        b"0.2 0.3 1 rg 0 0 150 55 re f",
        extra=f"/OC {blue} 0 R ",
    )
    any_form = form(
        objects,
        "[0 0 150 55]",
        b"0.9 0.55 0.1 rg 0 0 150 55 re f",
        extra=f"/OC {any_on} 0 R ",
    )
    nested_form = form(
        objects,
        "[0 0 150 55]",
        b"\n".join(
            [
                b"0.2 0.75 0.35 rg 0 0 150 55 re f",
                b"/OC /Blue BDC 0 0 0 rg 20 15 110 25 re f EMC",
            ]
        ),
        resources=f"<< /Properties << /Blue {blue} 0 R >> >>",
    )
    red_image = image(objects, bytes([255, 80, 80]), red)
    blue_image = image(objects, bytes([50, 80, 255]), blue)
    page_two_resources = (
        "<< /XObject << "
        f"/RedForm {red_form} 0 R /BlueForm {blue_form} 0 R "
        f"/AnyForm {any_form} 0 R /Nested {nested_form} 0 R "
        f"/RedImage {red_image} 0 R /BlueImage {blue_image} 0 R "
        ">> >>"
    )
    page_two = b"\n".join(
        [
            b"0.96 g 0 0 400 300 re f",
            b"q 1 0 0 1 20 220 cm /RedForm Do Q",
            b"q 1 0 0 1 220 220 cm /BlueForm Do Q",
            b"q 1 0 0 1 20 140 cm /AnyForm Do Q",
            b"q 1 0 0 1 220 140 cm /Nested Do Q",
            b"q 150 0 0 55 20 45 cm /RedImage Do Q",
            b"q 150 0 0 55 220 45 cm /BlueImage Do Q",
        ]
    )
    set_page(
        objects,
        page_refs[1],
        pages,
        page_two,
        page_two_resources,
    )

    red_appearance = form(
        objects,
        "[0 0 100 55]",
        b"1 0.25 0.25 rg 0 0 100 55 re f",
    )
    blue_appearance = form(
        objects,
        "[0 0 100 55]",
        b"0.2 0.3 1 rg 0 0 100 55 re f",
    )
    any_appearance = form(
        objects,
        "[0 0 100 55]",
        b"0.9 0.55 0.1 rg 0 0 100 55 re f",
    )
    widget_appearance = form(
        objects,
        "[0 0 160 40]",
        b"0.2 0.3 1 rg 0 0 160 40 re f",
    )
    red_annotation = annotation(
        objects,
        (
            f"/Subtype /Stamp /Rect [20 210 120 265] /OC {red} 0 R "
            f"/AP << /N {red_appearance} 0 R >> /Contents (visible layer)"
        ),
    )
    blue_annotation = annotation(
        objects,
        (
            f"/Subtype /Stamp /Rect [150 210 250 265] /OC {blue} 0 R "
            f"/AP << /N {blue_appearance} 0 R >> /Contents (hidden layer)"
        ),
    )
    any_annotation = annotation(
        objects,
        (
            f"/Subtype /Stamp /Rect [280 210 380 265] /OC {any_on} 0 R "
            f"/AP << /N {any_appearance} 0 R >> /Contents (membership layer)"
        ),
    )
    widget = annotation(
        objects,
        (
            f"/Subtype /Widget /FT /Tx /T (layered-field) /V (HIDDEN) "
            f"/Rect [20 120 180 160] /P {page_refs[2]} 0 R /OC {blue} 0 R "
            f"/AP << /N {widget_appearance} 0 R >>"
        ),
    )
    page_three = b"\n".join(
        [
            b"0.96 g 0 0 400 300 re f",
            b"0.85 g 20 210 100 55 re f",
            b"0.85 g 150 210 100 55 re f",
            b"0.85 g 280 210 100 55 re f",
            b"0.85 g 20 120 160 40 re f",
        ]
    )
    set_page(
        objects,
        page_refs[2],
        pages,
        page_three,
        f"<< /Font << /F1 {font} 0 R >> >>",
        [red_annotation, blue_annotation, any_annotation, widget],
    )

    page_four = b"\n".join(
        [
            b"0.96 g 0 0 400 300 re f",
            b"/OC /AnyOn BDC 1 0.55 0.1 rg 20 210 160 55 re f EMC",
            b"/OC /AllOn BDC 0.55 0.2 0.8 rg 220 210 160 55 re f EMC",
            b"/OC /AnyOff BDC 0.1 0.7 0.7 rg 20 125 160 55 re f EMC",
            b"/OC /AllOff BDC 0.25 0.25 0.25 rg 220 125 160 55 re f EMC",
            b"/OC /Expr BDC 0.2 0.75 0.35 rg 20 40 160 55 re f EMC",
            (
                f"/OC << /Type /OCMD /OCGs [{red} 0 R {green} 0 R] "
                "/P /AllOn >> BDC 0.1 0.45 0.1 rg 220 40 160 55 re f EMC"
            ).encode("ascii"),
        ]
    )
    set_page(
        objects,
        page_refs[3],
        pages,
        page_four,
        base_resources,
    )

    kids = " ".join(f"{reference} 0 R" for reference in page_refs)
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Count {len(page_refs)} /Kids [{kids}] >>"
        ).encode("ascii"),
    )
    objects.set(
        catalog,
        (
            f"<< /Type /Catalog /Pages {pages} 0 R /PageMode /UseOC "
            f"/AcroForm << /Fields [{widget} 0 R] /DA (/F1 10 Tf 0 g) "
            f"/DR << /Font << /F1 {font} 0 R >> >> >> "
            f"/OCProperties << /OCGs [{red} 0 R {blue} 0 R {green} 0 R] "
            f"/D << /Name (Default View) /Creator (Poppler.Net alpha.3) "
            f"/BaseState /ON /Intent /View "
            f"/ON [{red} 0 R {green} 0 R] /OFF [{blue} 0 R] "
            f"/Order [(Layers) {red} 0 R {blue} 0 R "
            f"[(Protected) {green} 0 R]] "
            f"/RBGroups [[{red} 0 R {blue} 0 R]] /Locked [{green} 0 R] "
            f"/AS [<< /Event /View /Category [/View] "
            f"/OCGs [{red} 0 R {blue} 0 R] >>] >> "
            f"/Configs [<< /Name (All off) /BaseState /OFF >>] >> >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    pdf = corpus()
    pdf_path = ROOT / "optional-content-alpha3.pdf"
    pdf_path.write_bytes(pdf)
    manifest = {
        "file": pdf_path.name,
        "sha256": hashlib.sha256(pdf).hexdigest(),
        "pages": [
            "marked-content-default-visibility-and-text",
            "form-and-image-xobject-visibility",
            "annotation-widget-and-membership-visibility",
            "ocmd-policies-and-visibility-expression",
        ],
        "managed_png_sha256": [
            "ab0c74b1793fbe9ea06b48fd43cd56242a29c3fb48cff18d3de1736f3c44a2b2",
            "7b0a0ca09e5b2cee919aae248ec7d2211131e0add9a727b66bb9e69597f30a19",
            "2b74fb3d00bcdadb67a159a93ce5e94dd6d8428f126f082011bd799328b8bb75",
            "72012479807e591cbe913f265824bbe9b0998fdb9cc00f24a8965019c6b0fdd8",
        ],
        "inverted_png_sha256": [
            "e4d2fd3d2ce5f353671fd97a3c6c4142965b85cbdc6da2cd22c690b40d454a57",
            "7d5a4600c32a222308ea1d52313518f31eef798eb1f675fd04255978b858051b",
            "e327385c8339c78c92619483a4ef44f54312b6ecb80a616d704f35f56aa92982",
            "f6d223af7ce45f5ea8c42cf41cf32adbb0bc35eefcce63e95a7ece5dd0e3edc6",
        ],
    }
    (ROOT / "optional-content-alpha3-fixture.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


if __name__ == "__main__":
    main()
