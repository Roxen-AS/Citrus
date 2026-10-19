#!/usr/bin/env python3
"""Fail if a win-x64 publication omits or changes required local PDF assets."""
import hashlib
import json
from pathlib import Path
import sys


def verify(directory: Path) -> None:
    expected = json.loads((Path(__file__).parent / "pdf-backend.lock.json").read_text())
    actual = json.loads((directory / "pdf-backend.lock.json").read_text())
    if actual != expected:
        raise ValueError("Published PDF provenance differs from the checked-in manifest")
    for name, sha in expected["winX64NativeSha256"].items():
        path = directory / name
        if hashlib.sha256(path.read_bytes()).hexdigest() != sha:
            raise ValueError(f"Native hash mismatch: {path}")
        print(f"PASS native {name} {sha}")
    for name in ("PdfSharp.dll", "PDFtoImage.dll", "UglyToad.PdfPig.dll", "SkiaSharp.dll",
                 "System.Security.Cryptography.Pkcs.dll", "notices/PDF-Notice.txt", "docs/pdf-engine.md"):
        if not (directory / name).is_file() or (directory / name).stat().st_size == 0:
            raise ValueError(f"Missing/empty PDF asset: {name}")
        print(f"PASS asset {name}")
    for name, sha in expected["noticeSha256"].items():
        path = directory / "notices/pdf-licenses" / name
        if hashlib.sha256(path.read_bytes()).hexdigest() != sha:
            raise ValueError(f"Notice hash mismatch: {path}")
        print(f"PASS notice {name}")
    if list(directory.rglob("PdfSharp.License.dll")):
        raise ValueError("Unexpected PDFsharp license assembly (6.5.0 must not be used)")
    print("PASS clean win-x64 PDF publish assets and notices")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: verify-pdf-publish.py PUBLISH_DIRECTORY")
    verify(Path(sys.argv[1]))
