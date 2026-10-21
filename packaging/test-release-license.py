"""Release distribution conditions for the unmodified Windows SDK components."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('presentation', ROOT / 'verify-presentation-publish.py')
presentation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(presentation)


class ReleaseLicenseTests(unittest.TestCase):
    def test_unresolved_sdk_terms_cannot_qualify_a_release_payload(self):
        manifest = json.loads((ROOT / 'presentation-backend.lock.json').read_text())
        manifest['sdkLicense']['status'] = 'Pending redistribution review'
        manifest['publishedAssemblies'] = {}
        manifest['excludedRuntimeProfiles'] = []
        manifest['noticeSha256'] = {}
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'presentation-backend.lock.json').write_text(json.dumps(manifest))
            with patch.object(presentation, 'ROOT', root):
                with self.assertRaisesRegex(ValueError, 'SDK redistribution terms'):
                    presentation.verify(root)

    def test_installer_requires_separate_microsoft_component_terms_before_installation(self):
        installer = (ROOT / 'Citrus.nsi').read_text()
        self.assertIn('!define MUI_LICENSEPAGE_CHECKBOX', installer)
        license_page = '!insertmacro MUI_PAGE_LICENSE "${__FILEDIR__}\\notices\\presentation\\Windows-SDK-License.rtf"'
        self.assertIn(license_page, installer)
        self.assertLess(installer.index(license_page), installer.index('!insertmacro MUI_PAGE_COMPONENTS'))
        self.assertIn('Open-source rights remain unchanged.', installer)

    def test_sdk_permission_and_exact_terms_are_bound_to_the_published_notice_inventory(self):
        manifest = json.loads((ROOT / 'presentation-backend.lock.json').read_text())
        self.assertEqual('RedistributionTermsRecorded', manifest['sdkLicense']['status'])
        self.assertEqual('https://learn.microsoft.com/en-us/legal/windows-sdk/redist', manifest['sdkLicense']['officialRedistUrl'])
        for name in ['presentation/Windows-SDK-License.rtf', 'presentation/Windows-SDK-REDIST.html',
                     'presentation/Windows-SDK-Distribution-Review.json']:
            with self.subTest(notice=name):
                self.assertEqual(manifest['noticeSha256'][name], presentation.digest(ROOT / 'notices' / name).hex())

    def test_silent_installation_requires_explicit_sdk_terms_acceptance_before_mutation(self):
        installer = (ROOT / 'Citrus.nsi').read_text()
        self.assertIn('Function RequireDistributionTerms', installer)
        self.assertIn('"/ACCEPTSDKTERMS="', installer)
        self.assertIn('SetErrorLevel 26', installer)
        init = installer.split('Function .onInit', 1)[1].split('FunctionEnd', 1)[0]
        self.assertLess(init.index('Call RequireDistributionTerms'), init.index('Call AcquireGuards'))


if __name__ == '__main__':
    unittest.main()
