# Third-party software in Citrus

The installer carries the same `notices/`, provenance manifests and
`source/citrus-media-source.tar.gz` as its self-contained payload. Licenses are
bundled locally; conversion does not fetch them or any backend at runtime.

| Component | Pinned version / source | License material |
| --- | --- | --- |
| .NET / Windows Desktop runtime | 10.0.13 from SDK 10.0.402 win-x64 runtime packs | `notices/dotnet/`: Microsoft MIT licenses and .NET third-party notices |
| Magick.NET Q8 x64 / Core | 15.0.0; ImageMagick 7.1.2-33 | `notices/Magick.NET-15.0.0-Notice.txt` includes bundled delegates; `image-backend.lock.json` records package/source/native hashes |
| PDFsharp / PDFtoImage / PdfPig | 6.2.4 / 5.4.0 / 0.1.16 | `notices/PDF-Notice.txt`, `notices/pdf-licenses/`, `pdf-backend.lock.json`; MIT / MIT / Apache-2.0 |
| PDFium / SkiaSharp and native Skia | PDFium 7961 / SkiaSharp 4.150.1 | BSD-style and upstream third-party notices under `notices/pdf-licenses/`, including PDFium delegate texts |
| System.Security.Cryptography.Pkcs and Microsoft extension dependencies | Pkcs 10.0.13; exact dependency versions in PDF lock and NuGet lock files | MIT and corresponding notices under `notices/pdf-licenses/` |
| SharpCompress | 0.50.5 | MIT: `notices/archive-licenses/SharpCompress-0.50.5-MIT.txt`, `archive-backend.lock.json` |
| Owned FFmpeg / FFprobe build | Exact six upstream commits, downloads and SHA256s in `media-lock.json` | LGPL-2.1-or-later FFmpeg/LAME, BSD-style Ogg/Vorbis/Opus/libvpx and MinGW runtime notices in `notices/media-licenses/`; summary `notices/Media-Notice.txt` |
| NSIS installer engine | 3.11 | `notices/NSIS-3.11-COPYING.txt`; zlib/libpng, bzip2 and Common Public License components as listed there |

The FFmpeg build uses shared replaceable DLLs; GPL, nonfree and version3 code,
network protocols and capture devices are disabled. LAME is a separate LGPL
shared encoder. The included media source archive contains all upstream source
archives/trees, patch, exact configuration, build scripts/logs, headers and import
libraries. [Media rebuild instructions](docs/media-engine.md#build-and-corresponding-source)
include the complete archive-only reconstruction. Reverse engineering for
debugging modifications to LGPL libraries is permitted. See exact upstream
notices for copyrights, terms and patent notices; this summary does not replace
them. Build/test-only dependencies are pinned in repository NuGet lock files.
