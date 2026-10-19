"""Mutation tests alter real packaged bytes, not a copy of checker constants."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("verify_ocr", ROOT/"verify.py")
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class PackageMutationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)/"packaging/ocr"
        shutil.copytree(ROOT, self.root, ignore=shutil.ignore_patterns("__pycache__"))
        # References remain exact files in a realistic repository layout.
        lock = json.loads((self.root/"manifest.json").read_text())
        for name in lock["existingMediaClosure"]:
            target = self.root.parent.parent/name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT.parent.parent/name, target)

    def tearDown(self):
        self.temp.cleanup()

    def rewrite_identity(self, name):
        manifest = self.root/"manifest.json"
        lock = json.loads(manifest.read_text())
        data = (self.root/name).read_bytes()
        lock["files"][name] = {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
        manifest.write_text(json.dumps(lock))

    def test_original_passes(self):
        VERIFY.verify(self.root)

    def test_corrupt_model_fails_identity(self):
        path = self.root/"tessdata/eng.traineddata"
        data = bytearray(path.read_bytes());data[100] ^= 1;path.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "identity mismatch"):
            VERIFY.verify(self.root)

    def test_missing_dependency_fails_inventory(self):
        (self.root/"native/libleptonica-1.87.0.dll").unlink()
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            VERIFY.verify(self.root)

    def test_extra_dependency_fails_inventory(self):
        (self.root/"native/unreviewed.dll").write_bytes(b"unexpected")
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            VERIFY.verify(self.root)

    def test_modified_import_fails_even_with_updated_file_hash(self):
        name = "native/libtesseract55.dll"
        path = self.root/name
        data = path.read_bytes()
        self.assertIn(b"KERNEL32.dll\0", data)
        path.write_bytes(data.replace(b"KERNEL32.dll\0", b"EVIL1234.dll\0"))
        self.rewrite_identity(name)
        with self.assertRaisesRegex(ValueError, "import mismatch"):
            VERIFY.verify(self.root)

    def test_unknown_import_fails_even_if_manifest_import_is_updated(self):
        name = "native/libtesseract55.dll"
        path = self.root/name
        path.write_bytes(path.read_bytes().replace(b"KERNEL32.dll\0", b"EVIL1234.dll\0"))
        self.rewrite_identity(name)
        manifest = self.root/"manifest.json"
        lock = json.loads(manifest.read_text())
        lock["imports"][name] = ["EVIL1234.dll" if x.lower()=="kernel32.dll" else x for x in lock["imports"][name]]
        manifest.write_text(json.dumps(lock))
        with self.assertRaisesRegex(ValueError, "unbundled dependency"):
            VERIFY.verify(self.root)

    def test_missing_export_fails_even_with_updated_hash(self):
        name = "native/libtesseract55.dll"
        path = self.root/name
        data = path.read_bytes()
        self.assertIn(b"TessBaseAPIInit5\0", data)
        path.write_bytes(data.replace(b"TessBaseAPIInit5\0", b"XessBaseAPIInit5\0"))
        self.rewrite_identity(name)
        with self.assertRaisesRegex(ValueError, "missing exports"):
            VERIFY.verify(self.root)

    def test_non_pe_fails_even_with_updated_hash(self):
        name = "validation/known-answer.exe"
        (self.root/name).write_bytes(b"not an executable")
        self.rewrite_identity(name)
        with self.assertRaisesRegex(ValueError, "missing DOS signature"):
            VERIFY.verify(self.root)


if __name__ == "__main__":
    unittest.main()
