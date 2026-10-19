#!/usr/bin/env python3
"""Verify the offline managed archive backend and its publish notices."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
manifest = json.loads((root / "archive-backend.lock.json").read_text())
package = manifest["package"]
assert package["version"] == "0.50.5"
assert hashlib.sha256((root / package["runtimeAsset"]).read_bytes()).hexdigest() == package["runtimeAssetSha256"]
for name in ["notices/Archive-Notice.txt", "notices/archive-licenses/SharpCompress-0.50.5-MIT.txt", "docs/subtitle-archive-engines.md"]:
    assert (root / name).is_file(), name
assert not list(root.rglob("*.rar")), "Development fixtures must not be published"
print("Archive publish verified: SharpCompress 0.50.5 DLL hash, manifest, MIT notice, documentation; no RAR fixtures.")
