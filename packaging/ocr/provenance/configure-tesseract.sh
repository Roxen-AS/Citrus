#!/bin/bash
set -euo pipefail
export PATH=/workspace/.citrus-tools/apt/root/usr/bin:$PATH
export LD_LIBRARY_PATH=/workspace/.citrus-tools/apt/root/usr/lib/x86_64-linux-gnu
export PKG_CONFIG_LIBDIR=/workspace/Citrus/.tools/ocr-minimal-experiment/prefix/lib/pkgconfig
export PKG_CONFIG_PATH=$PKG_CONFIG_LIBDIR
ocr_work=/workspace/Citrus/.tools/ocr-minimal-experiment
cmake -S "$ocr_work/src/tesseract" -B "$ocr_work/build-tesseract" -G 'Unix Makefiles' \
 -DCMAKE_TOOLCHAIN_FILE="$ocr_work/toolchain.cmake" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ocr_work/prefix" \
 -DCMAKE_PREFIX_PATH="$ocr_work/prefix" -DBUILD_SHARED_LIBS=ON -DBUILD_TRAINING_TOOLS=OFF -DBUILD_TESTS=OFF \
 -DOPENMP_BUILD=OFF -DGRAPHICS_DISABLED=ON -DDISABLED_LEGACY_ENGINE=ON -DDISABLE_TIFF=ON -DDISABLE_ARCHIVE=ON \
 -DDISABLE_CURL=ON -DENABLE_NATIVE=OFF -DENABLE_LTO=OFF -DFAST_FLOAT=ON -DINSTALL_CONFIGS=OFF \
 -DUSE_SYSTEM_ICU=OFF -DENABLE_PRECOMPILED_HEADERS=OFF -DENABLE_CCACHE=OFF -DLEPT_TIFF_RESULT=1
cmake --build "$ocr_work/build-tesseract" --target libtesseract -j 4

# Runtime DLL is copied by package-research.py; no CLI/training/install component is built.

