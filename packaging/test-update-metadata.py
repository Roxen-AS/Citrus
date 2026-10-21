"""Metadata and generated verification contracts for automatic releases."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('package',Path(__file__).with_name('package.py'))
package=importlib.util.module_from_spec(spec);spec.loader.exec_module(package)

class UpdateMetadataTests(unittest.TestCase):
    def test_nsis_version_is_generated_from_authoritative_props_and_license_hash(self):
        self.assertTrue(hasattr(package,'version_include'),'Package generation must derive updater metadata from central version props')
        actual=package.version_include()
        props=ET.parse(package.REPO/'CitrusVersion.props').getroot()
        self.assertIn('!define CITRUS_VERSION "'+props.findtext('./PropertyGroup/Version')+'"',actual)
        self.assertIn('!define CITRUS_FILE_VERSION "'+props.findtext('./PropertyGroup/FileVersion')+'"',actual)
        self.assertIn(package.sha(package.REPO/'packaging/notices/presentation/Windows-SDK-License.rtf'),actual)

    def test_every_written_owned_file_gets_a_hash_check_before_commit(self):
        self.assertTrue(hasattr(package,'verification_instructions'),'Installer commit requires independent installed-byte verification')
        text=package.verification_instructions({'one.txt':'a'*64,'folder/two.txt':'b'*64})
        self.assertIn('Push "$INSTDIR\\one.txt"',text)
        self.assertIn('Push "$INSTDIR\\folder\\two.txt"',text)
        self.assertEqual(text.count('Call HashFile'),2)
        self.assertEqual(text.count('Call Fail'),2)
        with self.assertRaises(ValueError):package.verification_instructions({'../escape':'a'*64})

if __name__=='__main__':unittest.main()
