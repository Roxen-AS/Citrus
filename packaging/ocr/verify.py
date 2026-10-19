#!/usr/bin/env python3
"""Offline asset identity and actual PE32+ import/export closure verification."""
import hashlib
import json
from pathlib import Path
import struct
import sys

SYSTEM_DLLS = {"kernel32.dll", "msvcrt.dll", "gdi32.dll", "user32.dll"}


class PE:
    def __init__(self, data):
        self.data = data
        if data[:2] != b"MZ":
            raise ValueError("missing DOS signature")
        pe = self.u32(0x3c)
        if data[pe:pe+4] != b"PE\0\0" or self.u16(pe+4) != 0x8664:
            raise ValueError("not Windows x64 PE")
        opt = pe+24
        if self.u16(opt) != 0x20b or self.u32(opt+108) < 2:
            raise ValueError("not PE32+ with import/export directories")
        sections = opt+self.u16(pe+20)
        self.sections = []
        for i in range(self.u16(pe+6)):
            pos = sections+40*i
            virtual_size, rva, raw_size, raw = struct.unpack_from("<IIII", data, pos+8)
            self.sections.append((rva, virtual_size, raw, raw_size))
        self.export_rva = self.u32(opt+112)
        self.import_rva = self.u32(opt+120)
        # This minimal engine has no delay-loaded dependency escape hatch.
        if self.u32(opt+108) > 13 and self.u32(opt+112+13*8):
            raise ValueError("unexpected delay imports")

    def u16(self, pos):
        return struct.unpack_from("<H", self.data, pos)[0]

    def u32(self, pos):
        return struct.unpack_from("<I", self.data, pos)[0]

    def offset(self, rva):
        for start, size, raw, raw_size in self.sections:
            if start <= rva < start+max(size, raw_size):
                delta = rva-start
                if delta >= raw_size or raw+delta >= len(self.data):
                    raise ValueError("RVA outside stored section")
                return raw+delta
        raise ValueError("unmapped RVA")

    def string(self, rva):
        pos = self.offset(rva)
        end = self.data.index(b"\0", pos, min(pos+4096, len(self.data)))
        return self.data[pos:end].decode("ascii")

    def imports(self):
        names = []
        if not self.import_rva:
            return names
        pos = self.offset(self.import_rva)
        for _ in range(1024):
            fields = struct.unpack_from("<IIIII", self.data, pos)
            if not any(fields):
                return names
            names.append(self.string(fields[3]))
            pos += 20
        raise ValueError("unterminated imports")

    def exports(self):
        if not self.export_rva:
            return set()
        pos = self.offset(self.export_rva)
        count = self.u32(pos+24)
        if count > 100000:
            raise ValueError("invalid export count")
        names = self.offset(self.u32(pos+32))
        return {self.string(self.u32(names+4*i)) for i in range(count)}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(root):
    root = Path(root).resolve()
    lock = json.loads((root/"manifest.json").read_text(encoding="utf-8"))
    files = lock["files"]
    actual = {p.relative_to(root).as_posix() for p in root.rglob("*")
              if p.is_file() and p.name != "manifest.json" and "__pycache__" not in p.parts}
    if actual != set(files):
        raise ValueError(f"inventory mismatch: extra={actual-set(files)}, missing={set(files)-actual}")
    for name, identity in files.items():
        path = root/name
        if path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError(f"unowned file: {name}")
        if path.stat().st_size != identity["bytes"] or digest(path) != identity["sha256"]:
            raise ValueError(f"identity mismatch: {name}")
    native = {p.name.lower() for p in (root/"native").glob("*.dll")}
    for name, expected in lock["imports"].items():
        pe = PE((root/name).read_bytes())
        imports = {s.lower() for s in pe.imports()}
        if imports != {s.lower() for s in expected}:
            raise ValueError(f"import mismatch: {name}: {imports}")
        if imports - SYSTEM_DLLS - native:
            raise ValueError(f"unbundled dependency: {name}: {imports-SYSTEM_DLLS-native}")
        required = set(lock["requiredExports"].get(name, []))
        if required-pe.exports():
            raise ValueError(f"missing exports: {name}: {required-pe.exports()}")
    pe_files = {name for name in files if name.endswith((".dll", ".exe"))}
    if pe_files != set(lock["imports"]):
        raise ValueError("PE files missing dependency inspection")
    repo = root.parent.parent
    for name, expected in lock["existingMediaClosure"].items():
        if digest(repo/name) != expected:
            raise ValueError(f"media source/runtime/notice reference mismatch: {name}")
    if digest(root/"native/libwinpthread-1.dll") != digest(repo/"packaging/media/win-x64/libwinpthread-1.dll"):
        raise ValueError("winpthreads differs from audited media runtime")
    print(f"PASS: {len(files)} asset identities, {len(pe_files)} actual x64 PEs, imports/exports and media provenance closure")
    return lock


if __name__ == "__main__":
    try:
        verify(Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent)
    except (ValueError, KeyError, OSError, struct.error) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
