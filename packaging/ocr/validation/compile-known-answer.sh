#!/usr/bin/env bash
set -euo pipefail
ocr_validation=$(cd "$(dirname "$0")" && pwd)
ocr_cc=${CITRUS_OCR_CC:-x86_64-w64-mingw32-gcc}
"$ocr_cc" -std=c11 -O2 -municode -Wall -Wextra -Werror -static-libgcc \
  -Wl,--no-insert-timestamp "$ocr_validation/known-answer.c" \
  -o "$ocr_validation/known-answer.exe" -lgdi32 -luser32
