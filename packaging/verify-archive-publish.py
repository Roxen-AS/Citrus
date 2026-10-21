#!/usr/bin/env python3
"""Verify the offline managed archive backend and its publish notices."""
import hashlib
import json
from pathlib import Path
import stat
import sys

root = Path(sys.argv[1])
manifest = json.loads((root / "archive-backend.lock.json").read_bytes())
package = manifest["package"]
assert package["version"] == "0.50.5"
assert hashlib.sha256((root / package["runtimeAsset"]).read_bytes()).hexdigest() == package["runtimeAssetSha256"]
for name in ["notices/Archive-Notice.txt", "notices/archive-licenses/SharpCompress-0.50.5-MIT.txt", "docs/subtitle-archive-engines.md"]:
    assert (root / name).is_file(), name
upstream_name = "image/exiftool/exiftool_files/t/images/ZIP.rar"
for path in root.rglob("*"):
    if path.suffix.casefold() != ".rar": continue
    if path.relative_to(root).as_posix() != upstream_name:
        raise ValueError("Unadmitted RAR fixture must not be published: " + str(path))
    # Trust the repository lock, never an allowlist supplied by the candidate.
    lock = json.loads((Path(__file__).resolve().parent / "image-backend.lock.json").read_bytes())
    records = [entry for entry in lock["offlineFeaturePack"]["files"]
               if entry["path"] == "win-x64/exiftool/exiftool_files/t/images/ZIP.rar"]
    if len(records) != 1: raise ValueError("Missing or duplicate pinned ExifTool RAR provenance")
    for item in [path, *path.parents]:
        info = item.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked RAR fixture must not be published")
        if item == root: break
    record = records[0]
    if not path.is_file() or path.stat().st_size != record["bytes"] or hashlib.sha256(path.read_bytes()).hexdigest() != record["sha256"]:
        raise ValueError("Changed pinned ExifTool RAR fixture")
print("Archive publish verified: SharpCompress 0.50.5 DLL hash, manifest, MIT notice, documentation; only the hash-pinned upstream ExifTool RAR fixture is admitted.")
