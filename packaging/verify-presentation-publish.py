#!/usr/bin/env python3
"""Verify the pinned implicit SDK projection and its app-local published closure.
This verifies integrity and recorded distribution conditions, not legal counsel or optical eligibility.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def digest(path, algorithm='sha256'):
    return hashlib.new(algorithm, path.read_bytes()).digest()


def verify(directory, package_cache=None):
    expected = json.loads((ROOT / 'presentation-backend.lock.json').read_text())
    if expected.get('sdkLicense', {}).get('status') != 'RedistributionTermsRecorded':
        raise ValueError('SDK redistribution terms have not been recorded for this release')
    published = directory / 'presentation-backend.lock.json'
    if json.loads(published.read_text()) != expected:
        raise ValueError('Published presentation provenance differs from the reviewed manifest')
    for name, metadata in expected['publishedAssemblies'].items():
        if digest(directory / name).hex() != metadata['sha256']:
            raise ValueError('Presentation assembly hash mismatch: ' + name)
        print('PASS app-local projection', name, metadata['assemblyVersion'])
    for name in expected['excludedRuntimeProfiles']:
        if (directory / name).exists():
            raise ValueError('Unexpected projection profile: ' + name)
    for name, sha in expected['noticeSha256'].items():
        if digest(directory / 'notices' / name).hex() != sha:
            raise ValueError('Presentation notice/provenance mismatch: ' + name)
    if package_cache is not None:
        for package in expected['implicitPackages']:
            stem = package['id'].lower() + '.' + package['version']
            root = package_cache / package['id'].lower() / package['version']
            archive = root / (stem + '.nupkg')
            if digest(archive).hex() != package['nupkgSha256'] or base64.b64encode(digest(archive, 'sha512')).decode() != package['nupkgSha512']:
                raise ValueError('Implicit SDK package archive integrity mismatch')
            if digest(root / (package['id'].lower() + '.nuspec')).hex() != package['nuspecSha256']:
                raise ValueError('Implicit SDK package nuspec mismatch')
            if digest(root / 'data/RuntimeList.xml').hex() != package['runtimeListSha256']:
                raise ValueError('Implicit SDK runtime list mismatch')
            for metadata in expected['publishedAssemblies'].values():
                if digest(root / metadata['packagePath']).hex() != metadata['sha256']:
                    raise ValueError('Implicit SDK runtime assembly mismatch')
            print('PASS exact implicit package', package['id'], package['version'])
    print('PASS presentation payload integrity and recorded SDK distribution terms/notices')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--package-cache', nargs='?', const=os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages')), type=Path)
    args = parser.parse_args()
    verify(args.directory, args.package_cache)
