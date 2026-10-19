# Isolated OCR asset admission

This directory contains an English-only CPU Tesseract 5.5.3 raw-pixel runtime for
native acceptance. It is independent of the Citrus product build: no application
route, .NET adapter, publish copy or installer entry is enabled. No runtime model,
engine, language or dependency download is provided. Windows inference remains
unverified until the new Windows OCR workflow actually passes on the reviewed
commit. A successful Linux compilation or package check is not inference evidence.

`manifest.json` locks every admitted file, source archive pin, actual x64 PE
imports, required C API exports and the existing media provenance references.
Run `python3 packaging/ocr/verify.py` and
`python3 packaging/ocr/validation/test_verify.py -v` from the repository root.
The checker uses Python's standard library, parses actual PE bytes and does not
need objdump, installed Tesseract, NuGet or network access. Mutation tests change
actual bytes and reject corrupt models, missing/extra runtime files, modified
imports, unknown imports even with updated hashes/import declarations, missing
exports and non-PE executables.

## Runtime and license closure

- Tesseract 5.5.3: `db0ec62f81b0737fbbe184d8fea40af5738f8eef`, Apache-2.0.
- Leptonica 1.87.0: `13275a278eb55b5746e33f95fbf5a2c8f604b3ab`, BSD-2-Clause.
- English `tessdata_fast`: license-complete repository commit
  `27cfc71a8874cce2483679eea010e391bb38c2ae`, Apache-2.0. Model content matches
  commit `923915d4ced2a7235221788285785a29c4a42d4a`; 4,113,088 bytes.
- MinGW-w64 winpthreads: exactly the existing media DLL and Debian package pin in
  `../media-lock.json`. Its compiled runtime is MIT/BSD/public-domain, excluding
  the LGPL test suite. Full MinGW copyright and GCC Runtime Library Exception
  notices are retained here. GCC/libstdc++ runtimes are statically linked under
  their runtime exception; winpthreads is a separate replaceable DLL.

The existing `../media/source/citrus-media-source.tar.gz`, media lock and runtime
notice files are hash-referenced rather than duplicated. That archive supplies
the existing FFmpeg/LAME LGPL source/relink closure; those libraries are not OCR
runtime dependencies and their LGPL designation does not apply to winpthreads.
The exact permissive runtime package provenance is in the media lock. Preserve
these referenced files when distributing this directory as part of the repository;
a standalone redistribution needs the notices and its own reviewed source policy.
All upstream license texts are in `notices/`. Source archive URLs and measured
hashes are in the manifest. No unlicensed third-party image fixture is included:
the test renders original Citrus text into pixels using Windows Arial.

Actual imports are Tesseract -> Leptonica, winpthreads, KERNEL32, msvcrt;
Leptonica -> GDI32, USER32, KERNEL32, msvcrt; winpthreads -> KERNEL32, msvcrt.
There is no VC redistributable, shared GCC/libstdc++, OpenMP, codec, curl, archive,
ICU or font-library dependency in this closure. GDI/USER32 are OS dependencies.

## Source build reproduction

The original successful build scripts, exact CMake caches, config headers,
compiler versions and build/relink logs are preserved in `provenance/`.
Their absolute `/workspace` paths are historical evidence. The only upstream
source modification is the retained one-line Linux cross-link correction
`Ws2_32` -> `ws2_32`; no algorithm or ABI change was made.

`rebuild.sh` is an offline, parameterized clean reproduction recipe with the same
flags and patch. Supply the two original archives named `<name>-<commit>.tar.gz`
from the manifest in an external cache; it verifies archive bytes/hashes before
safe extraction. Obtain those pinned public sources separately through normal
permitted channels if needed. The script itself performs no network requests.

```bash
CITRUS_OCR_TOOL_ROOT=/workspace/.citrus-tools/apt/root \
CITRUS_OCR_SOURCE_CACHE=/workspace/Citrus/.tools/ocr-minimal-experiment/downloads \
CITRUS_OCR_WORK=/path/to/new/ocr-rebuild \
bash packaging/ocr/rebuild.sh
```

Toolchain: extracted MinGW GCC/G++ 14 POSIX, binutils 2.44, CMake 3.31.6, GNU Make
4.4.1, Python 3.12+ (safe tar extraction), host patch/pkg-config. The recipe uses
isolated target include/library/package roots and static GCC/C++ runtime flags.
`LEPT_TIFF_RESULT=1` explicitly seeds the upstream cross-compile check from the
codec-free configuration; no target test execution is implied. All image codecs,
legacy OCR, graphics, training, tests, CLI, native-CPU tuning and OpenMP are disabled.
Upstream runtime CPU dispatch remains present. Strip uses SOURCE_DATE_EPOCH
1784917622. Clean repeated-build byte reproducibility has not been established;
the recipe reports resulting hashes for review and never replaces admitted bytes.
The parameterized recipe has been syntax checked; the retained original scripts
are the ones that produced these DLLs. No engine rebuild was performed at admission.

## Native Windows known answer

`validation/known-answer.exe` is a cross-compiled, dynamically bound C fixture.
Recompile it on the documented MinGW toolchain using:

```bash
CITRUS_OCR_CC=/workspace/.citrus-tools/apt/root/usr/bin/x86_64-w64-mingw32-gcc \
bash packaging/ocr/validation/compile-known-answer.sh
```

The compiler uses C11, `-Wall -Wextra -Werror`, static GCC runtime and no PE insert
timestamp. The fixture loads an absolute owned DLL with
`LoadLibraryExW(DLL_LOAD_DIR|SYSTEM32)`, checks the loaded Tesseract and dependency
module paths, reads model bytes using `CreateFileW`, calls Init5 in LSTM-only mode,
and passes top-down 8-bit grayscale rows rendered from `CITRUS OCR 12345` in Arial.
It requires TessVersion 5.5.3, successful recognition, exact text and exactly three
TSV words, ordered positive boxes within the image and confidence in [50,100].
The C API TSV has no renderer header. Every text/model/pixel/GDI/API resource is
released. A failing expectation exits nonzero; stdout retains actual text, TSV,
word bounds and confidence. This fixture is a native ABI/known-answer gate, not an
OCR accuracy benchmark or a product backend.

On native Windows x64 with PowerShell 7, run:

```powershell
python packaging/ocr/verify.py
./packaging/ocr/validation/run-native.ps1 -Results C:\owned\ocr-results
```

The runner stages only the owned EXE, three DLLs and English model under hidden
private scratch, repeats actual inference in ASCII/spaced and Unicode/spaced
paths, uses an empty current directory and a system-only PATH, removes
TESSDATA_PREFIX, enforces a 60-second per-process bound, saves logs/results, and
removes scratch in `finally`. The workflow runs this on Windows Server 2022 and
always uploads evidence. Hosted CI requires a controller push; none was initiated
by this admission task. The fixture performs no network calls or file decoding.

Windows Server proof does not establish Windows 10 22H2 or Windows 11 desktop,
non-AVX hardware, long-path support, installer behavior, real screenshots,
paragraphs, rotations, no-text cases, recall, corrupt-model error UX, worker
lifecycle/cancellation or .NET memory ownership. The supported intended runtime
floor is Windows 10, but that floor needs native desktop validation. Only English,
CPU inference and raw grayscale ingestion are in this package; no OSD, language
selector, image codec or detection/editor product capability is claimed.
