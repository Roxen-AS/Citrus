#!/usr/bin/env python3
"""Build public release metadata from final bytes and central version properties."""
import hashlib
import argparse
import re
import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
def sha(path):
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()

def installer_file_version(path):
    api=ctypes.WinDLL('version',use_last_error=True)
    api.GetFileVersionInfoSizeW.argtypes=[wintypes.LPCWSTR,ctypes.POINTER(wintypes.DWORD)]
    api.GetFileVersionInfoSizeW.restype=wintypes.DWORD
    api.GetFileVersionInfoW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p]
    api.GetFileVersionInfoW.restype=wintypes.BOOL
    api.VerQueryValueW.argtypes=[ctypes.c_void_p,wintypes.LPCWSTR,ctypes.POINTER(ctypes.c_void_p),ctypes.POINTER(wintypes.UINT)]
    api.VerQueryValueW.restype=wintypes.BOOL
    length=api.GetFileVersionInfoSizeW(str(path),None)
    if not length:raise RuntimeError('Installer version resource missing')
    data=ctypes.create_string_buffer(length)
    if not api.GetFileVersionInfoW(str(path),0,length,data):raise ctypes.WinError(ctypes.get_last_error())
    pointer=ctypes.c_void_p();characters=wintypes.UINT()
    if not api.VerQueryValueW(data,r'\StringFileInfo\040904b0\FileVersion',ctypes.byref(pointer),ctypes.byref(characters)):
        raise RuntimeError('Installer FileVersion string missing')
    return ctypes.wstring_at(pointer.value)

def write_release(dist=None, source_commit=None):
    dist=(Path(dist) if dist else ROOT/'dist').resolve()
    if not dist.is_relative_to(ROOT.resolve()) or not dist.is_dir():raise RuntimeError('Release output must be an existing directory inside this workspace')
    version=ET.parse(ROOT/'CitrusVersion.props').getroot().findtext('./PropertyGroup/Version')
    setup=dist/'Citrus-Setup.exe';payload=dist/'Citrus-win-x64'
    if not setup.is_file():raise RuntimeError('Build the final complete installer first')
    actual_version=installer_file_version(setup)
    if actual_version!=version:raise RuntimeError('Installer version resource does not match central release version')
    inventory=json.loads((payload/'install-inventory.json').read_text(encoding='utf-8-sig'))
    commit=source_commit or subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    if not re.fullmatch('[0-9a-f]{40}',commit):raise RuntimeError('Source commit must be a complete lowercase Git SHA')
    subprocess.run(['git','cat-file','-e',commit+'^{commit}'],cwd=ROOT,check=True,capture_output=True)
    committed_props=ET.fromstring(subprocess.check_output(['git','show',commit+':CitrusVersion.props'],cwd=ROOT))
    if committed_props.findtext('./PropertyGroup/Version')!=version:raise RuntimeError('Compiled source commit central version does not match this release')
    manifest={'schema':2,'product':'Citrus','version':version,'platform':'Windows x64','sourceCommit':commit,
              'installer':{'file':setup.name,'bytes':setup.stat().st_size,'sha256':sha(setup),'fileVersion':actual_version,'signature':'NotSigned'},
              'payload':{'files':len(inventory),'inventorySha256':sha(payload/'install-inventory.json')},
              'sdkLicenseSha256':sha(ROOT/'packaging/notices/presentation/Windows-SDK-License.rtf'),
              'updatePolicy':'Published versioned GitHub releases only; no update for documentation, website or media changes.',
              'qualification':'Automated packaging checks do not establish clean-machine, physical Explorer, visual or performance acceptance.'}
    output=dist/'release-manifest.json';output.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
    public=dist/'release';public.mkdir(exist_ok=True)
    (public/'SHA256SUMS').write_text(manifest['installer']['sha256']+'  Citrus-Setup.exe\n'+sha(output)+'  release-manifest.json\n',encoding='utf-8',newline='\n')
    print(json.dumps(manifest,indent=2))
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dist',type=Path,help='Existing complete release directory inside this workspace; defaults to dist')
    parser.add_argument('--source-commit',help='Exact existing public source commit used for compilation; defaults to HEAD')
    args=parser.parse_args();write_release(args.dist,args.source_commit)
