#!/bin/bash
set -euo pipefail
ocr_work=/workspace/Citrus/.tools/ocr-minimal-experiment
/workspace/.citrus-tools/apt/root/usr/bin/x86_64-w64-mingw32-gcc-posix -std=c11 -O2 -municode -Wall -Wextra -Werror -static-libgcc \
 "$ocr_work/known-answer.c" -o "$ocr_work/known-answer.exe" -lgdi32 -luser32
