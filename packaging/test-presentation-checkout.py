#!/usr/bin/env python3
"""Recreate fresh Git checkouts and verify pinned presentation notice bytes."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parent.parent


class PresentationCheckoutTests(unittest.TestCase):
    def test_pinned_notice_hashes_survive_fresh_lf_and_autocrlf_checkouts(self):
        expected = json.loads((REPO / 'packaging/presentation-backend.lock.json').read_text())['noticeSha256']
        for autocrlf in ('false', 'true'):
            with self.subTest(autocrlf=autocrlf), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                subprocess.run(['git', 'init', '-q', str(root)], check=True)
                shutil.copyfile(REPO / '.gitattributes', root / '.gitattributes')
                for name in expected:
                    path = root / 'packaging/notices' / name
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes((REPO / 'packaging/notices' / name).read_bytes())
                # Seed the index with the repository's exact upstream bytes, then perform a fresh checkout.
                subprocess.run(['git', '-C', str(root), '-c', 'core.autocrlf=false', 'add', '.'], check=True)
                for name in expected:
                    (root / 'packaging/notices' / name).unlink()
                subprocess.run(['git', '-C', str(root), '-c', 'core.autocrlf=' + autocrlf, 'checkout-index', '--all', '--force'], check=True)
                for name, digest in expected.items():
                    self.assertEqual(digest, hashlib.sha256((root / 'packaging/notices' / name).read_bytes()).hexdigest(), name)


if __name__ == '__main__':
    unittest.main()
