#!/usr/bin/env python3
"""Real package negative checks; run against the candidate produced by publish."""
import importlib.util
from pathlib import Path
import tempfile
import sys
import shutil
import os
import subprocess

PAYLOAD = Path(sys.argv.pop(1)).resolve() if len(sys.argv) > 1 else None
import unittest

spec = importlib.util.spec_from_file_location('package', Path(__file__).with_name('package.py'))
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)

class PackageTests(unittest.TestCase):
    @unittest.skipIf(PAYLOAD is None, 'Pass actual candidate directory for missing-asset checks')
    def test_real_payload_missing_each_asset_class_is_rejected(self):
        with tempfile.TemporaryDirectory(dir=PAYLOAD.parent) as temp:
            root = Path(temp) / 'payload'
            shutil.copytree(PAYLOAD, root, copy_function=os.link)
            for name in ['Citrus.Windows.exe', 'coreclr.dll', 'Magick.Native-Q8-x64.dll',
                         'pdfium.dll', 'libSkiaSharp.dll', 'SharpCompress.dll', 'media/ffmpeg.exe',
                         'media/ffprobe.exe', 'media/avcodec-63.dll', 'notices/Media-Notice.txt',
                         'notices/dotnet/NET-10.0.13-LICENSE.txt', 'source/citrus-media-source.tar.gz']:
                with self.subTest(name=name):
                    path = root / name
                    held = path.with_name(path.name + '.held')
                    path.rename(held)
                    try:
                        with self.assertRaises((ValueError, FileNotFoundError, AssertionError, subprocess.CalledProcessError)):
                            package.verify(root)
                    finally: held.rename(path)

    def test_empty_payload_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises((ValueError, FileNotFoundError)):
                package.verify(Path(root))

    def test_owned_inventory_preserves_unknown_outputs(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'media').mkdir()
            (root / 'app.exe').write_bytes(b'app')
            (root / 'media' / 'codec.dll').write_bytes(b'codec')
            files = package.inventory(root)
            (root / 'notes.txt').write_text('user output')
            (root / 'media' / 'converted.mp4').write_bytes(b'user output')
            _, delete, _ = package.instructions(files, root)
            self.assertNotIn('notes.txt', delete)
            self.assertNotIn('converted.mp4', delete)
            self.assertNotIn('RMDir /r', delete)
            self.assertNotIn('*', delete)
            # Execute precisely the literal Delete instructions, not the input list.
            owned_deletes = [line[len('Delete "$INSTDIR\\'):-1].replace('\\', '/')
                             for line in delete.splitlines() if line.startswith('Delete "$INSTDIR\\')]
            self.assertEqual(set(files), set(owned_deletes))
            for path in owned_deletes:
                (root / path).unlink()
            for directory in sorted(root.rglob('*'), reverse=True):
                if directory.is_dir():
                    try: directory.rmdir()
                    except OSError: pass
            self.assertEqual('user output', (root / 'notes.txt').read_text())
            self.assertEqual(b'user output', (root / 'media/converted.mp4').read_bytes())

    def test_unsafe_inventory_paths_rejected(self):
        for name in ['../outside', '/absolute', 'C:/outside', 'stream:ads', 'a/../b', 'a\\b', 'a\nFile x', 'wild*card', 'trailing.']:
            with self.subTest(name=name), self.assertRaises(ValueError):
                package.instructions({name: '0' * 64}, Path('/tmp/payload'))

    def test_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'target').write_text('data')
            try: (root / 'link').symlink_to(root / 'target')
            except OSError: self.skipTest('Host cannot create symbolic links')
            with self.assertRaises(ValueError): package.inventory(root)

if __name__ == '__main__': unittest.main()
