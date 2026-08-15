#!/usr/bin/env python3
"""Run the optional Poppler 26.07 conversion differential for 0.13.0-beta.1."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile


FIXTURES = Path(__file__).resolve().parent
ROOT = FIXTURES.parents[1]
MANIFEST = FIXTURES / "conversion-beta1-compatibility.json"


def run(arguments: list[str], *, cwd: Path = ROOT) -> str:
    result = subprocess.run(
        arguments,
        cwd=cwd,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    return result.stdout


def executable(directory: Path | None, name: str) -> str:
    suffix = ".exe" if directory is not None and (directory / f"{name}.exe").exists() else ""
    return str(directory / f"{name}{suffix}") if directory is not None else name


def verify_version(command: str, name: str) -> None:
    output = run([command, "-v"])
    match = re.search(rf"{name} version ([0-9]+\.[0-9]+\.[0-9]+)", output)
    if match is None or match.group(1) != "26.07.0":
        raise RuntimeError(f"expected {name} 26.07.0, received: {output.strip()}")


def password_arguments(case: dict[str, object]) -> list[str]:
    password = case.get("userPassword")
    return ["-upw", str(password)] if password else []


def managed_password_arguments(case: dict[str, object]) -> list[str]:
    password = case.get("userPassword")
    return ["--user-password", str(password)] if password else []


def managed_command(dotnet: str, cli: Path, *arguments: str) -> list[str]:
    return [dotnet, str(cli), *arguments]


def verify_managed_case(
    case: dict[str, object],
    output: Path,
    dotnet: str,
    cli: Path,
) -> None:
    identifier = str(case["id"])
    pdf = FIXTURES / str(case["fixture"])
    pages = int(case["pages"])
    password = managed_password_arguments(case)
    html = output / f"{identifier}-managed.html"
    structured = output / f"{identifier}-managed.json"
    separated = output / f"{identifier}-managed-pages"

    run(managed_command(
        dotnet,
        cli,
        "html",
        str(pdf),
        str(html),
        "--first-page",
        "1",
        "--last-page",
        str(pages),
        *password,
    ))
    run(managed_command(dotnet, cli, "json", str(pdf), str(structured), *password))
    run(managed_command(dotnet, cli, "separate", str(pdf), str(separated), *password))

    data = json.loads(structured.read_text(encoding="utf-8"))
    if len(data["pages"]) != pages:
        raise RuntimeError(f"{identifier}: managed structured page count differs")
    if len(list(separated.glob("*.pdf"))) != pages:
        raise RuntimeError(f"{identifier}: managed separated page count differs")
    expected_text = case.get("textContains")
    if expected_text and str(expected_text) not in "\n".join(page["text"] for page in data["pages"]):
        raise RuntimeError(f"{identifier}: managed text token is missing")
    expected_images = case.get("images")
    if expected_images is not None:
        actual_images = sum(len(page["images"]) for page in data["pages"])
        if actual_images != int(expected_images):
            raise RuntimeError(f"{identifier}: managed image count differs")


def verify_poppler_case(
    case: dict[str, object],
    output: Path,
    tools: dict[str, str],
) -> None:
    identifier = str(case["id"])
    pdf = FIXTURES / str(case["fixture"])
    pages = int(case["pages"])
    password = password_arguments(case)
    selected = set(case["popplerTools"])

    if "pdftotext" in selected:
        text_path = output / f"{identifier}-poppler.txt"
        run([tools["pdftotext"], *password, str(pdf), str(text_path)])
        expected = case.get("textContains")
        if expected and str(expected) not in text_path.read_text(encoding="utf-8"):
            raise RuntimeError(f"{identifier}: Poppler text token is missing")

    if "pdfseparate" in selected:
        pattern = output / f"{identifier}-poppler-page-%d.pdf"
        run([
            tools["pdfseparate"],
            "-f",
            "1",
            "-l",
            str(pages),
            *password,
            str(pdf),
            str(pattern),
        ])
        if len(list(output.glob(f"{identifier}-poppler-page-*.pdf"))) != pages:
            raise RuntimeError(f"{identifier}: Poppler separated page count differs")

    if "pdftohtml" in selected:
        root = output / f"{identifier}-poppler"
        run([
            tools["pdftohtml"],
            "-f",
            "1",
            "-l",
            str(pages),
            "-s",
            "-i",
            *password,
            str(pdf),
            str(root),
        ])
        if not list(output.glob(f"{identifier}-poppler*.html")):
            raise RuntimeError(f"{identifier}: Poppler HTML output is missing")

    if "pdfimages" in selected:
        listing = run([
            tools["pdfimages"],
            "-list",
            "-f",
            "1",
            "-l",
            str(pages),
            *password,
            str(pdf),
        ])
        expected_images = case.get("images")
        if expected_images is not None:
            rows = [line for line in listing.splitlines() if re.match(r"^\s*\d+\s+\d+\s+", line)]
            if len(rows) != int(expected_images):
                raise RuntimeError(f"{identifier}: Poppler image count differs")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument(
        "--poppler-bin",
        type=Path,
        help="directory containing the Poppler 26.07.0 utility executables",
    )
    result.add_argument("--dotnet", default="dotnet")
    result.add_argument(
        "--managed-cli",
        type=Path,
        default=ROOT / "src" / "Poppler.Net.Cli" / "bin" / "Release" / "net8.0" / "poppler-net.dll",
    )
    return result


def main() -> int:
    args = parser().parse_args()
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    tools = {
        name: executable(args.poppler_bin, name)
        for name in manifest["reference"]["tools"]
    }
    for name, command in tools.items():
        verify_version(command, name)
    if not args.managed_cli.exists():
        raise FileNotFoundError(f"managed CLI not found: {args.managed_cli}")

    with tempfile.TemporaryDirectory(prefix="poppler-net-013-beta1-") as temporary:
        output = Path(temporary)
        for case in manifest["cases"]:
            verify_managed_case(case, output, args.dotnet, args.managed_cli)
            verify_poppler_case(case, output, tools)
            print(f"PASS {case['id']}: {case['classification']}")

    print(f"All {len(manifest['cases'])} conversion compatibility cases passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
