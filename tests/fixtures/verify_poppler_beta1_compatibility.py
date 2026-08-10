#!/usr/bin/env python3
"""Reproduce the optional Poppler differential gate for 0.12.0-beta.1."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import tempfile
import zlib


FIXTURES = Path(__file__).resolve().parent
ROOT = FIXTURES.parents[1]
MANIFEST = FIXTURES / "poppler-beta1-compatibility.json"


def command(args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )


def png_rgb(path: Path) -> tuple[int, int, bytes]:
    data = path.read_bytes()
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"{path} is not a PNG image")

    header = None
    compressed = bytearray()
    offset = 8
    while offset < len(data):
        length = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        offset += length + 12
        if kind == b"IHDR":
            header = payload
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break

    if header is None or len(header) != 13:
        raise ValueError(f"{path} has no valid PNG header")
    width, height, depth, color, compression, filtering, interlace = struct.unpack(
        ">IIBBBBB", header
    )
    if (depth, color, compression, filtering, interlace) != (8, 6, 0, 0, 0):
        raise ValueError(f"{path} is not a production RGBA8 PNG")

    decoded = zlib.decompress(bytes(compressed))
    stride = width * 4
    if len(decoded) != height * (stride + 1):
        raise ValueError(f"{path} has an unexpected payload length")
    rgb = bytearray(width * height * 3)
    source = 0
    target = 0
    for _ in range(height):
        if decoded[source] != 0:
            raise ValueError(f"{path} does not use the production PNG row filter")
        source += 1
        for _ in range(width):
            red, green, blue, alpha = decoded[source : source + 4]
            if alpha != 255:
                raise ValueError(f"{path} is not an opaque render")
            rgb[target : target + 3] = bytes((red, green, blue))
            source += 4
            target += 3
    return width, height, bytes(rgb)


def ppm_rgb(path: Path) -> tuple[int, int, bytes]:
    data = path.read_bytes()
    tokens: list[bytes] = []
    offset = 0
    while len(tokens) < 4:
        while offset < len(data) and chr(data[offset]).isspace():
            offset += 1
        if offset < len(data) and data[offset] == ord("#"):
            offset = data.index(b"\n", offset) + 1
            continue
        end = offset
        while end < len(data) and not chr(data[end]).isspace():
            end += 1
        tokens.append(data[offset:end])
        offset = end
    if data[offset : offset + 2] == b"\r\n":
        offset += 2
    elif offset < len(data) and chr(data[offset]).isspace():
        offset += 1
    else:
        raise ValueError(f"{path} has no separator before its pixel payload")

    magic, width_token, height_token, maximum_token = tokens
    if magic != b"P6" or maximum_token != b"255":
        raise ValueError(f"{path} is not an RGB8 binary PPM")
    width = int(width_token)
    height = int(height_token)
    pixels = data[offset:]
    if len(pixels) != width * height * 3:
        raise ValueError(f"{path} has an unexpected payload length")
    return width, height, pixels


def compare(managed: Path, reference: Path) -> tuple[float, float]:
    managed_width, managed_height, managed_pixels = png_rgb(managed)
    reference_width, reference_height, reference_pixels = ppm_rgb(reference)
    if (managed_width, managed_height) != (reference_width, reference_height):
        raise ValueError(
            "render dimensions differ: "
            f"managed={managed_width}x{managed_height}, "
            f"Poppler={reference_width}x{reference_height}"
        )

    absolute_error = 0
    changed = 0
    for index in range(0, len(managed_pixels), 3):
        differences = [
            abs(managed_pixels[index + component] - reference_pixels[index + component])
            for component in range(3)
        ]
        absolute_error += sum(differences)
        if max(differences) > 8:
            changed += 1
    pixels = managed_width * managed_height
    return absolute_error / (pixels * 3 * 255), changed / pixels


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument(
        "--dotnet",
        default=os.environ.get("DOTNET_HOST_PATH", "dotnet"),
        help="path to the dotnet host",
    )
    result.add_argument(
        "--pdftoppm",
        default="pdftoppm",
        help="path to a Poppler 26.x pdftoppm executable",
    )
    result.add_argument("--configuration", default="Release")
    result.add_argument("--no-build", action="store_true")
    return result


def main() -> int:
    args = parser().parse_args()
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    render = manifest["render"]

    version = command([args.pdftoppm, "-v"]).stdout
    match = re.search(r"pdftoppm version ([0-9]+\.[0-9]+\.[0-9]+)", version)
    if match is None or not match.group(1).startswith("26."):
        raise RuntimeError(f"expected Poppler 26.x, received: {version.strip()}")
    print(f"Poppler renderer: {match.group(1)}")
    print(f"Semantic reference: {manifest['semantic_reference']}")

    project = ROOT / "src" / "Poppler.Net.Cli" / "Poppler.Net.Cli.csproj"
    cli = (
        ROOT
        / "src"
        / "Poppler.Net.Cli"
        / "bin"
        / args.configuration
        / "net8.0"
        / "poppler-net.dll"
    )
    if not args.no_build:
        build = command(
            [
                args.dotnet,
                "build",
                str(project),
                "--configuration",
                args.configuration,
                "--no-restore",
            ]
        )
        print(build.stdout.rstrip())
    if not cli.exists():
        raise FileNotFoundError(f"CLI not found after build: {cli}")

    failures = 0
    with tempfile.TemporaryDirectory(prefix="poppler-net-beta1-") as temporary:
        output = Path(temporary)
        for corpus in manifest["corpora"]:
            pdf = FIXTURES / corpus["file"]
            actual_hash = hashlib.sha256(pdf.read_bytes()).hexdigest()
            if actual_hash != corpus["sha256"]:
                raise ValueError(f"fixture hash changed: {pdf.name}")

            for page in corpus["pages"]:
                stem = f"{corpus['id']}-{page['number']}"
                managed = output / f"{stem}-managed.png"
                reference_stem = output / f"{stem}-poppler"
                command(
                    [
                        args.dotnet,
                        str(cli),
                        "render",
                        str(pdf),
                        str(managed),
                        "--page",
                        str(page["number"]),
                        "--dpi",
                        str(render["dpi"]),
                        "--antialias",
                        str(render["managed_antialiasing"]),
                        "--no-font-substitution",
                    ]
                )
                command(
                    [
                        args.pdftoppm,
                        "-r",
                        str(render["dpi"]),
                        *corpus["poppler_options"],
                        "-f",
                        str(page["number"]),
                        "-l",
                        str(page["number"]),
                        "-singlefile",
                        str(pdf),
                        str(reference_stem),
                    ]
                )
                mae, changed = compare(managed, reference_stem.with_suffix(".ppm"))
                passed = mae <= page["maximum_mae"]
                failures += not passed
                status = "PASS" if passed else "FAIL"
                print(
                    f"{status} {corpus['id']} p{page['number']} "
                    f"mae={mae:.9f} budget={page['maximum_mae']:.9f} "
                    f"changed>8={changed:.6f} {page['name']}"
                )

    if failures:
        print(f"{failures} compatibility comparison(s) exceeded their budget")
        return 1
    comparisons = sum(len(corpus["pages"]) for corpus in manifest["corpora"])
    print(f"All {comparisons} Poppler compatibility comparisons are within budget")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
