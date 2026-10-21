#!/usr/bin/env python3
"""Standalone archive verifier checks using tiny files and the pinned upstream fixture."""
import hashlib
import base64
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

REPO = Path(__file__).resolve().parent.parent
UPSTREAM = 'image/exiftool/exiftool_files/t/images/ZIP.rar'


class ArchivePublishTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        library = b'archive dependency'
        self.put('SharpCompress.dll', library)
        self.put('archive-backend.lock.json', json.dumps({'package': {
            'version': '0.50.5', 'runtimeAsset': 'SharpCompress.dll',
            'runtimeAssetSha256': hashlib.sha256(library).hexdigest()}}).encode())
        for name in ['notices/Archive-Notice.txt', 'notices/archive-licenses/SharpCompress-0.50.5-MIT.txt',
                     'docs/subtitle-archive-engines.md']:
            self.put(name, b'notice')
        # Exact 74-byte upstream test fixture, pinned by image-backend.lock.json.
        self.fixture = base64.b64decode('UmFyIRoHAQAzkrXlCgEFBgAFAQGAgABk6MNQIQIDC4UABIUAIIKJ0feAAAAFMS50eHQKAwKGdvNmYBDZAUhlbGxvHXdWUQMFBAA=')

    def put(self, name, data):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def verify(self):
        return subprocess.run([sys.executable, str(REPO / 'packaging/verify-archive-publish.py'), str(self.root)],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def test_no_rar_fixture_is_valid(self):
        result = self.verify()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_exact_pinned_upstream_fixture_is_valid(self):
        self.put(UPSTREAM, self.fixture)
        result = self.verify()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_identical_bytes_at_any_other_path_remain_rejected(self):
        for name in ['development.rar', 'source/exiftool/ZIP.rar', 'image/exiftool/OTHER.RAR']:
            with self.subTest(name=name):
                self.put(name, self.fixture)
                try: self.assertNotEqual(0, self.verify().returncode)
                finally: (self.root / name).unlink()

    def test_changed_fixture_cannot_be_admitted_by_candidate_manifest(self):
        data = b'changed upstream fixture'
        self.put(UPSTREAM, data)
        self.put('image-backend.lock.json', json.dumps({'offlineFeaturePack': {'files': [{
            'path': 'win-x64/exiftool/exiftool_files/t/images/ZIP.rar',
            'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}]}}).encode())
        self.assertNotEqual(0, self.verify().returncode)


if __name__ == '__main__': unittest.main()
