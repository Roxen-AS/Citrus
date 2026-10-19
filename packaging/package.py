#!/usr/bin/env python3
"""Pinned payload verification, literal NSIS ownership instructions, release hashes."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import tarfile

REPO = Path(__file__).resolve().parent.parent

def sha(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()

def safe(name):
    parts = name.split('/')
    if not name or any(p in ('', '.', '..') or p.endswith((' ', '.')) for p in parts) or any(c in name for c in '\\:"<>|?*\r\n\x00'):
        raise ValueError('Unsafe inventory path: ' + repr(name))
    return name

def inventory(root):
    if root.is_symlink(): raise ValueError("Linked payload root")
    result = {}
    for path in sorted(root.rglob('*')):
        if path.is_symlink(): raise ValueError('Links are forbidden in payload: ' + str(path))
        if path.is_file(): result[safe(path.relative_to(root).as_posix())] = sha(path)
    return result

def quote(value):
    return '"' + str(value).replace('$', '$$').replace('"', '$\\"') + '"'

def instructions(files, root):
    install, delete, checks = [], [], []
    dirs = set()
    for index, name in enumerate(sorted(files)):
        safe(name)
        relative = name.replace('/', '\\')
        parent = PurePosixPath(name).parent
        while str(parent) != '.':
            dirs.add(str(parent)); parent = parent.parent
        folder = str(PurePosixPath(name).parent).replace('/', '\\')
        install += ['SetOutPath "$INSTDIR' + ('' if folder == '.' else '\\' + folder.replace('$','$$')) + '"', 'ClearErrors', 'File ' + quote(root / name), 'IfErrors 0 +2', 'Call Fail']
        if index == 0:
            # Only a separately compiled fault-test installer enables this boundary.
            install += ['!ifdef CITRUS_TEST_FAIL_AFTER_FIRST_FILE', 'SetErrorLevel 25', 'Quit', '!endif']
        delete += ['ClearErrors', 'Delete "$INSTDIR\\' + relative.replace('$','$$') + '"', 'IfErrors 0 +2', 'StrCpy $DeleteFailed 1']
    # Check root, all directory ancestors and owned file paths for reparse points.
    for name in sorted(dirs | set(files), key=lambda n: (n.count('/'), n)):
        checks += ['Push "$INSTDIR\\' + name.replace('/', '\\').replace('$','$$') + '"', 'Call ${PREFIX}CheckPath']
    for name in sorted(dirs, key=lambda n: (n.count('/'), n), reverse=True):
        delete += ['RMDir "$INSTDIR\\' + name.replace('/', '\\').replace('$','$$') + '"']
    return '\n'.join(install)+'\n', '\n'.join(delete)+'\n', '\n'.join(checks)+'\n'

def verify(root):
    required = ['Citrus.Windows.exe','Citrus.Windows.dll','Citrus.Core.dll','Citrus.Conversion.dll',
        'Citrus.Windows.deps.json','Citrus.Windows.runtimeconfig.json','hostfxr.dll','hostpolicy.dll','coreclr.dll',
        'System.Private.CoreLib.dll','PresentationFramework.dll','PresentationNative_cor3.dll',
        'Magick.NET-Q8-x64.dll','Magick.NET.Core.dll','THIRD-PARTY-NOTICES.md','README.md',
        'docs/operations.md','docs/windows-acceptance.md','docs/verification.md']
    for name in required:
        if not (root / name).is_file() or not (root / name).stat().st_size: raise ValueError('Missing/empty package asset: ' + name)
    for name in ['image-backend.lock.json','media-lock.json','pdf-backend.lock.json','archive-backend.lock.json','presentation-backend.lock.json']:
        if (root / name).read_bytes() != (REPO / 'packaging' / name).read_bytes(): raise ValueError('Changed provenance: ' + name)
    image = json.loads((root / 'image-backend.lock.json').read_text())['windowsNative']
    if sha(root / Path(image['path']).name) != image['sha256']: raise ValueError('Image native hash mismatch')
    media = json.loads((REPO / 'packaging/media/sha256.json').read_text())
    if json.loads((root / 'media-sha256.json').read_text()) != media: raise ValueError('Media manifest mismatch')
    for name, digest in media.items():
        destination = name.replace('win-x64/', 'media/', 1)
        if sha(root / destination) != digest: raise ValueError('Media/source hash mismatch: ' + name)
    for source in (REPO / 'packaging/notices').rglob('*'):
        if source.is_file() and sha(root / 'notices' / source.relative_to(REPO / 'packaging/notices')) != sha(source):
            raise ValueError('Missing/changed license: ' + source.name)
    with tarfile.open(root / 'source/citrus-media-source.tar.gz', 'r:gz') as archive:
        for source, member in [('packaging/build-media.sh','provenance/build-media.sh'),('packaging/media-lock.json','provenance/media-lock.json'),('packaging/verify-media.py','provenance/verify-media.py'),('docs/media-engine.md','provenance/media-engine.md')]:
            if archive.extractfile(member).read() != (REPO / source).read_bytes(): raise ValueError('Stale corresponding source: ' + member)
        for source in (REPO / 'packaging/notices/media-licenses').iterdir():
            if archive.extractfile('provenance/media-licenses/' + source.name).read() != source.read_bytes(): raise ValueError('Stale source license: ' + source.name)
    for script in ['verify-pdf-publish.py','verify-archive-publish.py','verify-presentation-publish.py']:
        import subprocess, sys
        subprocess.run([sys.executable, str(REPO / 'packaging' / script), str(root)], check=True)
    inventory(root)
    print('PASS complete application, self-contained runtime, image/PDF/archive/media natives, sources and licenses')

def prepare(root, generated):
    for name in ['README.md','THIRD-PARTY-NOTICES.md']:
        shutil.copyfile(REPO / name, root / name)
    for name in ['operations.md','windows-acceptance.md','verification.md']:
        shutil.copyfile(REPO / 'docs' / name, root / 'docs' / name)
    shutil.copyfile(REPO / 'packaging/image-backend.lock.json', root / 'image-backend.lock.json')
    shutil.copytree(REPO / 'packaging/notices', root / 'notices', dirs_exist_ok=True)
    (root / 'install-inventory.json').unlink(missing_ok=True)
    verify(root)
    files = inventory(root)
    (root / 'install-inventory.json').write_text(json.dumps(files, indent=2)+'\n', encoding='utf-8')
    files['install-inventory.json'] = sha(root / 'install-inventory.json')
    install, delete, checks = instructions(files, root.resolve())
    generated.mkdir(parents=True, exist_ok=True)
    for name, text in [('install.nsh',install),('uninstall.nsh',delete),('paths.nsh',checks)]:
        (generated / name).write_text(text, encoding='utf-8')
    (generated / 'identity.nsh').write_text('!define INVENTORY_HASH "' + files['install-inventory.json'] + '"\n')
    print('PASS generated embedded ownership for', len(files), 'files')

def verify_prepared(root, generated):
    """Read-only ownership closure; the CLI also runs full backend verification."""
    files = inventory(root)
    inventory_hash = files.pop('install-inventory.json')
    if files != json.loads((root / 'install-inventory.json').read_text(encoding='utf-8')):
        raise ValueError('Prepared payload changed after ownership inventory generation')
    files['install-inventory.json'] = inventory_hash
    install, delete, checks = instructions(files, root.resolve())
    expected = {'install.nsh': install, 'uninstall.nsh': delete, 'paths.nsh': checks,
                'identity.nsh': '!define INVENTORY_HASH "' + inventory_hash + '"\n'}
    if generated.is_symlink(): raise ValueError('Linked generated include directory')
    for name, text in expected.items():
        path = generated / name
        if path.is_symlink() or path.read_bytes() != text.replace('\n', os.linesep).encode('utf-8'):
            raise ValueError('Changed generated ownership include: ' + name)
    print('PASS unchanged prepared payload, inventory and generated ownership includes')

def checksums(dist):
    files = [dist / 'Citrus-Setup.exe'] + sorted(p for p in (dist / 'Citrus-win-x64').rglob('*') if p.is_file())
    if not files[0].is_file(): raise ValueError('Missing installer')
    (dist / 'SHA256SUMS').write_text(''.join(sha(p)+'  '+p.relative_to(dist).as_posix()+'\n' for p in files), encoding='utf-8')
    print('PASS SHA256SUMS:', len(files), 'artifacts')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['verify','prepare','verify-prepared','checksums'])
    parser.add_argument('directory', type=Path)
    parser.add_argument('--generated', type=Path, default=REPO / 'dist/nsis')
    args = parser.parse_args()
    if args.command == 'verify': verify(args.directory)
    elif args.command == 'prepare': prepare(args.directory, args.generated)
    elif args.command == 'verify-prepared':
        verify(args.directory)
        verify_prepared(args.directory, args.generated)
    else: checksums(args.directory)
