#!/usr/bin/env bash
# Build-time acquisition only. Citrus never downloads an engine at runtime.
set -euo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
work=${CITRUS_MEDIA_WORK:-"$repo/.tools/media"}
out=${CITRUS_MEDIA_OUTPUT:-"$repo/packaging/media/win-x64"}
jobs=${CITRUS_MEDIA_JOBS:-4}
cross=${CROSS_COMPILE:-x86_64-w64-mingw32-}
mkdir -p "$work" "$out" "$work/src" "$work/downloads" "$work/provenance"
exec > >(tee -a "$work/provenance/build.log") 2>&1
set -x
python3 - "$repo/packaging/media-lock.json" "$work" "${CITRUS_MEDIA_SOURCE_CACHE:-}" <<'PY'
import hashlib,json,pathlib,sys,tarfile,urllib.request
manifest,work,cache=sys.argv[1:]; work=pathlib.Path(work)
for s in json.load(open(manifest))['sources']:
 name=s['name']+'-'+s['commit']+'.tar.gz'; archive=work/'downloads'/name
 cached=pathlib.Path(cache)/name
 if not archive.exists():
  if cache and cached.exists(): archive.write_bytes(cached.read_bytes())
  else: urllib.request.urlretrieve(s['url'],archive)
 if hashlib.sha256(archive.read_bytes()).hexdigest()!=s['sha256']: raise RuntimeError('SHA256 mismatch: '+name)
 dest=work/'src'/s['name']
 if not dest.exists():
  dest.mkdir()
  with tarfile.open(archive) as t:
   prefix=t.getmembers()[0].name.split('/')[0]+'/'
   for m in t.getmembers():
    if m.name.startswith(prefix) and m.name[len(prefix):]:
     m.name=m.name[len(prefix):]; t.extract(m,dest,filter='data')
PY
if [[ ! -f "$work/lame-patched" ]]; then
 patch -d "$work/src/lame" -p1 < "$repo/packaging/patches/lame-encoder-only-exports.patch"
 touch "$work/lame-patched"
fi
prefix="$work/prefix"
mkdir -p "$prefix"
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"
{
 "${cross}gcc" --version; cmake --version; nasm -v; make --version; pkg-config --version; uname -a
} > "$work/provenance/toolchain.txt"
cat > "$work/toolchain.cmake" <<EOF
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER ${cross}gcc)
set(CMAKE_CXX_COMPILER ${cross}g++)
set(CMAKE_RC_COMPILER ${cross}windres)
set(CMAKE_FIND_ROOT_PATH "$prefix")
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_POLICY_VERSION_MINIMUM 3.5)
EOF
build_cmake() {
 local name=$1; shift
 cmake -S "$work/src/$name" -B "$work/build-$name" -DCMAKE_TOOLCHAIN_FILE="$work/toolchain.cmake" -DCMAKE_INSTALL_PREFIX="$prefix" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTING=OFF "$@"
 cmake --build "$work/build-$name" -j "$jobs"
 cmake --install "$work/build-$name"
}
if [[ ! -f "$work/dependencies.done" ]]; then
 build_cmake ogg -DINSTALL_DOCS=OFF
 build_cmake vorbis -DINSTALL_DOCS=OFF
 build_cmake opus -DOPUS_BUILD_TESTING=OFF -DOPUS_BUILD_PROGRAMS=OFF -DOPUS_DRED=OFF -DOPUS_DEEP_PLC=OFF -DOPUS_OSCE=OFF
 mkdir -p "$work/build-lame"
 (cd "$work/build-lame"; "$work/src/lame/configure" --host=x86_64-w64-mingw32 --prefix="$prefix" --enable-shared --disable-static --disable-decoder --disable-frontend --disable-mp3x --disable-mp3rtp; make -j "$jobs"; make install)
 mkdir -p "$work/build-vpx"
 (cd "$work/build-vpx"; CROSS="$cross" "$work/src/vpx/configure" --target=x86_64-win64-gcc --prefix="$prefix" --disable-examples --disable-tools --disable-docs --disable-unit-tests --disable-shared --enable-static; make -j "$jobs"; make install)
 touch "$work/dependencies.done"
fi
mkdir -p "$work/build-ffmpeg"
(cd "$work/build-ffmpeg"
 "$work/src/ffmpeg/configure" --prefix="$prefix" --target-os=mingw32 --arch=x86_64 --cross-prefix="$cross" --enable-cross-compile --pkg-config=pkg-config --pkg-config-flags=--static --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib -static-libgcc" --enable-shared --disable-static --disable-gpl --disable-nonfree --disable-version3 --disable-network --disable-autodetect --disable-ffplay --disable-doc --disable-debug --disable-devices --disable-protocols --enable-protocol=file,pipe --enable-libmp3lame --enable-libopus --enable-libvorbis --enable-libvpx --enable-mediafoundation --enable-d3d11va
 make -j "$jobs"
 make install
)
cp "$prefix/bin/ffmpeg.exe" "$prefix/bin/ffprobe.exe" "$out/"
cp "$prefix/bin/"*.dll "$out/"
runtime=$("${cross}gcc" -print-file-name=libwinpthread-1.dll)
test -f "$runtime"
cp "$runtime" "$out/"
python3 - "$repo/packaging/media-lock.json" "$runtime" <<'PYRUNTIME'
import hashlib,json,sys
expected=json.load(open(sys.argv[1]))['toolchainRuntime']['sha256']
assert hashlib.sha256(open(sys.argv[2],'rb').read()).hexdigest()==expected, 'Unexpected winpthreads toolchain runtime; update lock and review provenance'
PYRUNTIME
# All build flags, logs, unmodified upstream source trees, original hash-checked archives,
# import libraries and headers are shipped, allowing recipients to rebuild/replace DLLs.
cp "$repo/packaging/build-media.sh" "$repo/packaging/media-lock.json" "$work/provenance/"
cp "$work/build-ffmpeg/config_components.h" "$work/build-ffmpeg/config.h" "$work/build-ffmpeg/ffbuild/config.mak" "$work/build-ffmpeg/ffbuild/config.log" "$work/provenance/"
for library in ogg vorbis opus; do cp "$work/build-$library/CMakeCache.txt" "$work/provenance/$library-CMakeCache.txt"; done
cp "$work/build-lame/config.log" "$work/provenance/lame-config.log"
cp "$work/build-vpx/config.log" "$work/provenance/vpx-config.log"
cp "$work/toolchain.cmake" "$work/provenance/"
cp "$repo/docs/media-engine.md" "$repo/packaging/verify-media.py" "$work/provenance/"
cp -r "$repo/packaging/notices/media-licenses" "$work/provenance/"
cp -r "$repo/packaging/patches" "$work/provenance/"
printf '%s\n' 'lame-encoder-only-exports.patch removes decoder exports when decoder is disabled.' > "$work/provenance/PATCHES.txt"
for file in "$out/"*.exe "$out/"*.dll; do "${cross}objdump" -p "$file"; done > "$work/provenance/pe-imports.txt"
if grep -Ei 'DLL Name: (MFPlat|MFReadWrite|MF)\.dll' "$work/provenance/pe-imports.txt"; then echo 'Unexpected mandatory Media Foundation import'; exit 1; fi
python3 - "$out" "$work/provenance/pe-imports.txt" <<'PYIMPORTS'
import pathlib,re,sys
root=pathlib.Path(sys.argv[1]); shipped={p.name.lower() for p in root.iterdir()}
windows={'kernel32.dll','shell32.dll','bcrypt.dll','msvcrt.dll','ole32.dll','user32.dll','advapi32.dll','ntdll.dll','ws2_32.dll'}
imports=set(re.findall(r'DLL Name: (\S+)',pathlib.Path(sys.argv[2]).read_text()))
missing={name for name in imports if name.lower() not in shipped|windows}
assert not missing, 'Unbundled non-system DLL imports: '+repr(missing)
print('PASS: all non-system DLL imports are bundled')
PYIMPORTS
mkdir -p "$repo/packaging/media/source"
tar -C "$work" -czf "$repo/packaging/media/source/citrus-media-source.tar.gz" src downloads provenance toolchain.cmake prefix/include prefix/lib
python3 - "$repo/packaging/media" <<'PY'
import hashlib,json,pathlib,sys
root=pathlib.Path(sys.argv[1]); files={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob('*')) if p.is_file() and p.name!='sha256.json'}
(root/'sha256.json').write_text(json.dumps(files,indent=2)+'\n')
PY
