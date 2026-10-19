#!/usr/bin/env bash
# Offline clean source rebuild recipe. Does not replace admitted assets.
set -euo pipefail
ocr_root=$(cd "$(dirname "$0")" && pwd)
ocr_work=${CITRUS_OCR_WORK:?Set CITRUS_OCR_WORK to a new empty scratch directory}
ocr_cache=${CITRUS_OCR_SOURCE_CACHE:?Set CITRUS_OCR_SOURCE_CACHE to the pinned archive cache}
ocr_tool=${CITRUS_OCR_TOOL_ROOT:?Set CITRUS_OCR_TOOL_ROOT to the extracted toolchain root}
ocr_jobs=${CITRUS_OCR_JOBS:-4}
if [[ -e "$ocr_work" ]]; then echo 'Rebuild requires a new scratch path' >&2; exit 1; fi
mkdir -p "$ocr_work"
ocr_work=$(cd "$ocr_work" && pwd)
export PATH="$ocr_tool/usr/bin:$PATH"
export LD_LIBRARY_PATH="$ocr_tool/usr/lib/x86_64-linux-gnu"
export PKG_CONFIG_LIBDIR="$ocr_work/prefix/lib/pkgconfig"
export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"
exec > >(tee "$ocr_work/rebuild.log") 2>&1
python3 - "$ocr_root" "$ocr_cache" "$ocr_work" <<'PY'
import hashlib,json,pathlib,sys,tarfile
root,cache,work=map(pathlib.Path,sys.argv[1:])
lock=json.loads((root/'manifest.json').read_text())
for source in lock['sources']:
    archive=cache/(source['name']+'-'+source['commit']+'.tar.gz')
    data=archive.read_bytes()
    if len(data)!=source['archiveBytes'] or hashlib.sha256(data).hexdigest()!=source['sha256']:
        raise RuntimeError('Source archive identity mismatch: '+str(archive))
    dest=work/'src'/source['name'];dest.mkdir(parents=True)
    with tarfile.open(archive) as stream:
        prefix=stream.getmembers()[0].name.split('/')[0]+'/'
        for member in stream.getmembers():
            if not member.name.startswith(prefix):
                if member.name.rstrip('/')!=prefix.rstrip('/'): raise RuntimeError('Unexpected archive root')
                continue
            member.name=member.name[len(prefix):]
            if member.name: stream.extract(member,dest,filter='data')
PY
patch -d "$ocr_work/src/tesseract" -p1 < "$ocr_root/provenance/tesseract-mingw-ws2-case.patch"
cat > "$ocr_work/toolchain.cmake" <<TOOLCHAIN
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER "$ocr_tool/usr/bin/x86_64-w64-mingw32-gcc-posix")
set(CMAKE_CXX_COMPILER "$ocr_tool/usr/bin/x86_64-w64-mingw32-g++-posix")
set(CMAKE_RC_COMPILER "$ocr_tool/usr/bin/x86_64-w64-mingw32-windres")
set(CMAKE_FIND_ROOT_PATH "$ocr_work/prefix" "$ocr_tool/usr/x86_64-w64-mingw32")
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)
set(CMAKE_SHARED_LINKER_FLAGS_INIT "-static-libgcc -static-libstdc++")
set(CMAKE_EXE_LINKER_FLAGS_INIT "-static-libgcc -static-libstdc++")
TOOLCHAIN
cmake -S "$ocr_work/src/leptonica" -B "$ocr_work/build-leptonica" -G 'Unix Makefiles' \
 -DCMAKE_TOOLCHAIN_FILE="$ocr_work/toolchain.cmake" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ocr_work/prefix" \
 -DSW_BUILD=OFF -DBUILD_PROG=OFF -DBUILD_SHARED_LIBS=ON -DENABLE_ZLIB=OFF -DENABLE_PNG=OFF -DENABLE_GIF=OFF \
 -DENABLE_JPEG=OFF -DENABLE_TIFF=OFF -DENABLE_WEBP=OFF -DENABLE_OPENJPEG=OFF
cmake --build "$ocr_work/build-leptonica" -j "$ocr_jobs"
cmake --install "$ocr_work/build-leptonica"
cmake -S "$ocr_work/src/tesseract" -B "$ocr_work/build-tesseract" -G 'Unix Makefiles' \
 -DCMAKE_TOOLCHAIN_FILE="$ocr_work/toolchain.cmake" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ocr_work/prefix" \
 -DCMAKE_PREFIX_PATH="$ocr_work/prefix" -DBUILD_SHARED_LIBS=ON -DBUILD_TRAINING_TOOLS=OFF -DBUILD_TESTS=OFF \
 -DOPENMP_BUILD=OFF -DGRAPHICS_DISABLED=ON -DDISABLED_LEGACY_ENGINE=ON -DDISABLE_TIFF=ON -DDISABLE_ARCHIVE=ON \
 -DDISABLE_CURL=ON -DENABLE_NATIVE=OFF -DENABLE_LTO=OFF -DFAST_FLOAT=ON -DINSTALL_CONFIGS=OFF \
 -DUSE_SYSTEM_ICU=OFF -DENABLE_PRECOMPILED_HEADERS=OFF -DENABLE_CCACHE=OFF -DLEPT_TIFF_RESULT=1
cmake --build "$ocr_work/build-tesseract" --target libtesseract -j "$ocr_jobs"
mkdir "$ocr_work/runtime"
cp "$ocr_work/build-tesseract/bin/libtesseract55.dll" "$ocr_work/prefix/bin/libleptonica-1.87.0.dll" "$ocr_work/runtime/"
cp "$ocr_root/native/libwinpthread-1.dll" "$ocr_work/runtime/"
for ocr_dll in libtesseract55.dll libleptonica-1.87.0.dll; do
 SOURCE_DATE_EPOCH=1784917622 x86_64-w64-mingw32-strip --strip-unneeded "$ocr_work/runtime/$ocr_dll"
done
python3 - "$ocr_root" "$ocr_work/runtime" <<'PY'
import hashlib,json,pathlib,sys
root,runtime=map(pathlib.Path,sys.argv[1:]);lock=json.loads((root/'manifest.json').read_text())
for path in sorted(runtime.glob('*.dll')):
    actual=hashlib.sha256(path.read_bytes()).hexdigest()
    expected=lock['files']['native/'+path.name]['sha256']
    print(path.name,actual,'MATCH' if actual==expected else 'DIFFERS: review build provenance before any asset replacement')
PY
# Engine binaries are immutable admission inputs. This recipe never overwrites them.
