#!/bin/bash
set -euo pipefail
export PATH=/workspace/.citrus-tools/apt/root/usr/bin:$PATH
export LD_LIBRARY_PATH=/workspace/.citrus-tools/apt/root/usr/lib/x86_64-linux-gnu
export PKG_CONFIG_LIBDIR=/workspace/Citrus/.tools/ocr-minimal-experiment/prefix/lib/pkgconfig
export PKG_CONFIG_PATH=$PKG_CONFIG_LIBDIR
ocr_work=/workspace/Citrus/.tools/ocr-minimal-experiment
cmake -S "$ocr_work/src/leptonica" -B "$ocr_work/build-leptonica" -G 'Unix Makefiles' \
 -DCMAKE_TOOLCHAIN_FILE="$ocr_work/toolchain.cmake" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ocr_work/prefix" \
 -DSW_BUILD=OFF -DBUILD_PROG=OFF -DBUILD_SHARED_LIBS=ON -DENABLE_ZLIB=OFF -DENABLE_PNG=OFF -DENABLE_GIF=OFF \
 -DENABLE_JPEG=OFF -DENABLE_TIFF=OFF -DENABLE_WEBP=OFF -DENABLE_OPENJPEG=OFF
cmake --build "$ocr_work/build-leptonica" -j 4
cmake --install "$ocr_work/build-leptonica"
