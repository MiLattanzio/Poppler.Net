#!/usr/bin/env python3
"""Generate deterministic advanced-annotation regressions for 0.9.0-beta.1."""

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
                "/ID [<09000001090000010900000109000001> "
                "<09000001090000010900000109000001>] >>\n"
                f"startxref\n{xref}\n%%EOF\n"
            ).encode("ascii")
        )
        return bytes(output)


def annotation(objects: Objects, body: str) -> int:
    return objects.add(f"<< /Type /Annot {body} >>".encode("ascii"))


def page(
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
            f"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 420 300] "
            f"/Resources << >> /Contents {content_ref} 0 R "
            f"/Annots [{annots}] >>"
        ).encode("ascii"),
    )


def corpus() -> bytes:
    objects = Objects()
    catalog = objects.reserve()
    pages = objects.reserve()
    page_refs = [objects.reserve() for _ in range(3)]

    attachment_payload = b"Poppler.Net beta 1 attachment\n"
    attachment_stream = objects.add(
        stream(
            (
                f"<< /Type /EmbeddedFile /Subtype /text#plain "
                f"/Params << /Size {len(attachment_payload)} "
                "/CreationDate (D:20260729180000+02'00') >> "
                f"/Length {len(attachment_payload)} >>"
            ),
            attachment_payload,
        )
    )
    file_spec = objects.add(
        (
            "<< /Type /Filespec /F (beta1-note.txt) /UF (beta1-note.txt) "
            "/Desc (Advanced annotation attachment) "
            f"/EF << /F {attachment_stream} 0 R /UF {attachment_stream} 0 R >> >>"
        ).encode("ascii")
    )

    parent = objects.reserve()
    popup = objects.reserve()
    reply = objects.reserve()
    objects.set(
        parent,
        (
            "<< /Type /Annot /Subtype /Text /Rect [24 238 48 262] "
            "/NM (comment-root) /Contents (ROOT COMMENT) /T (Reviewer) "
            "/State /Accepted /StateModel /Review /Open true "
            f"/Popup {popup} 0 R >>"
        ).encode("ascii"),
    )
    objects.set(
        popup,
        (
            "<< /Type /Annot /Subtype /Popup /Rect [55 205 210 278] "
            "/NM (comment-popup) /Contents (POPUP THREAD) /Open true "
            f"/Parent {parent} 0 R >>"
        ).encode("ascii"),
    )
    objects.set(
        reply,
        (
            "<< /Type /Annot /Subtype /Text /Rect [220 238 244 262] "
            "/NM (comment-reply) /Contents (REPLY) /RT /R "
            "/State /Completed /StateModel /Marked "
            f"/IRT {parent} 0 R >>"
        ).encode("ascii"),
    )
    advanced = [
        parent,
        popup,
        reply,
        annotation(
            objects,
            (
                "/Subtype /Caret /Rect [270 232 300 265] /C [0.7 0 0.1] "
                "/Sy /P /Contents (CARET)"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /FileAttachment /Rect [330 232 365 267] "
                f"/FS {file_spec} 0 R /Name /Paperclip /Contents (ATTACHMENT)"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /FreeText /Rect [25 125 200 190] "
                "/Contents (CALLOUT NOTE) /CL [200 155 230 175 250 155] "
                "/LE /OpenArrow /IT /FreeTextCallout "
                "/RC (<body><p>Rich text</p></body>) "
                "/DS (font: 10pt sans-serif; color: #202020)"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Line /Rect [245 135 390 185] /L [255 145 380 175] "
                "/LE [/ClosedArrow /Circle] /IT /LineDimension "
                "/C [0.1 0.25 0.8] /BS << /W 3 /S /S >>"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Redact /Rect [25 45 190 95] /RD [3 3 3 3] "
                "/OverlayText (REDACTED) /Contents (REDACTED) "
                "/C [0 0 0] /IC [0 0 0]"
            ),
        ),
        annotation(
            objects,
            (
                "/Subtype /Watermark /Rect [225 35 390 105] "
                "/Contents (DRAFT) /FixedPrint << /H 0 /V 0 >> /CA 0.35"
            ),
        ),
    ]
    page(
        objects,
        page_refs[0],
        pages,
        b"\n".join(
            [
                b"0.97 g 0 0 420 300 re f",
                b"0.88 g 18 225 382 55 re f",
                b"0.93 g 18 115 382 85 re f",
                b"0.9 g 18 25 382 90 re f",
            ]
        ),
        advanced,
    )

    circular_a = objects.reserve()
    circular_b = objects.reserve()
    objects.set(
        circular_a,
        (
            "<< /S /GoToR /F (remote.pdf) /D (chapter-2) /NewWindow true "
            f"/Next {circular_b} 0 R >>"
        ).encode("ascii"),
    )
    objects.set(
        circular_b,
        (
            "<< /S /JavaScript /JS (app.alert\\(\\\"inspection only\\\"\\);) "
            f"/Next {circular_a} 0 R >>"
        ).encode("ascii"),
    )
    action_groups = [
        objects.add(f"<< /Type /OCG /Name (Action layer {index}) >>".encode("ascii"))
        for index in range(1, 4)
    ]
    action_specs = [
        f"/A {circular_a} 0 R",
        "/A << /S /Launch /F (manual.pdf) /NewWindow false >>",
        (
            "/A << /S /SubmitForm /F << /F (https://example.test/form) >> "
            "/Fields [(person.name) (person.email)] /Flags 4 >>"
        ),
        "/A << /S /ResetForm /Fields [(person.name)] /Flags 1 >>",
        "/A << /S /ImportData /F (values.fdf) >>",
        "/A << /S /Hide /T [(comment-root) (comment-popup)] /H true >>",
        (
            f"/A << /S /SetOCGState /State [/ON {action_groups[0]} 0 R "
            f"/OFF {action_groups[1]} 0 R /Toggle {action_groups[2]} 0 R] >>"
        ),
        "/A << /S /Rendition /N (Trailer) /OP 0 >>",
        "/A << /S /Trans /Trans << /S /Dissolve /D 1 >> >>",
        "/A << /S /GoTo3DView /V /Front >>",
    ]
    action_annotations: list[int] = []
    for index, spec in enumerate(action_specs):
        column = index % 2
        row = index // 2
        left = 25 + column * 200
        top = 275 - row * 50
        action_annotations.append(
            annotation(
                objects,
                (
                    f"/Subtype /Link /Rect [{left} {top - 32} {left + 170} {top}] "
                    f"/Border [0 0 1] /C [0.1 0.25 0.8] {spec}"
                ),
            )
        )
    page(
        objects,
        page_refs[1],
        pages,
        b"0.98 g 0 0 420 300 re f 0.92 g 15 15 390 270 re f",
        action_annotations,
    )

    multimedia = [
        annotation(
            objects,
            "/Subtype /Sound /Rect [25 205 125 265] /Contents (SOUND)",
        ),
        annotation(
            objects,
            "/Subtype /Movie /Rect [160 205 260 265] /Contents (MOVIE)",
        ),
        annotation(
            objects,
            "/Subtype /Screen /Rect [295 205 395 265] /Contents (SCREEN)",
        ),
        annotation(
            objects,
            "/Subtype /3D /Rect [25 95 160 175] /Contents (3D VIEW)",
        ),
        annotation(
            objects,
            "/Subtype /PrinterMark /Rect [190 110 240 160] /Contents (MARK)",
        ),
        annotation(
            objects,
            "/Subtype /TrapNet /Rect [270 95 395 175] /Contents (TRAP NET)",
        ),
    ]
    page(
        objects,
        page_refs[2],
        pages,
        b"0.95 g 0 0 420 300 re f 0.86 g 15 80 390 200 re f",
        multimedia,
    )

    objects.set(catalog, f"<< /Type /Catalog /Pages {pages} 0 R >>".encode("ascii"))
    objects.set(
        pages,
        (
            f"<< /Type /Pages /Kids "
            f"[{' '.join(f'{item} 0 R' for item in page_refs)}] "
            f"/Count {len(page_refs)} >>"
        ).encode("ascii"),
    )
    return objects.build()


def main() -> None:
    data = corpus()
    pdf = ROOT / "annotations-beta1.pdf"
    pdf.write_bytes(data)
    (ROOT / "annotations-beta1-fixture.json").write_text(
        json.dumps(
            {
                "file": pdf.name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "pages": [
                    "review-thread-geometry-and-attachment",
                    "safe-action-inspection-and-circular-next",
                    "multimedia-and-production-subtypes",
                ],
                "managed_png_sha256": [
                    "967de55f26d9117d2ea37aa274f59fc4113a63ef905b067d53fa9e48dbd1fa0f",
                    "14056e286b72f7171e498abe2d432f93109da481e4a907acb48c49c563b98fde",
                    "14b74e3517b0f21e65f9db1b9e5787e7889579f0515e1988df82752a397f59fc",
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
