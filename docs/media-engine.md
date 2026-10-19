# Offline media engine

Citrus uses its bundled `media/ffmpeg.exe` and `media/ffprobe.exe` by absolute path.
It does not search PATH, run a shell, upload inputs, or download codecs at runtime.
A missing binary reports a reinstall error. The shipped build disables network
support and all protocols except `file` and `pipe`; invocations also whitelist
those two protocols, including when developer tests use a host FFmpeg.

## Presets and behavior

The encoder, muxer, demuxer and filter catalogues are read from the selected
binary before the engine is registered. Only available output presets are
registered. An input must have a recognized local media extension and an
available demuxer; actual decoding errors are reported by the engine.

| Output | Video encoder | Audio encoder |
| --- | --- | --- |
| MP4, MOV, MKV | native MPEG-4 Part 2 (`mpeg4`) | native AAC |
| WebM | libvpx VP9 | libopus |
| AVI | native MPEG-4 Part 2 | libmp3lame |
| WMV | native WMV2 | native WMAv2 |
| GIF | native GIF, all decoded frames | none |
| MP3 | none | libmp3lame |
| M4A | none | native AAC |
| WAV, AIFF | none | native PCM s16le / s16be |
| FLAC | none | native FLAC |
| OGG | none | libvorbis |
| Opus | none | libopus |
| WMA | none | native WMAv2 |

MP4 (H.264) is a separate optional preset. On Windows only, Citrus must actually
encode two raw frames through `h264_mf -hw_encoding 0` and probe the result before
registering it. A failed or timed-out MFT probe retains MPEG-4 presets. There is
no assumption that software H.264 is bundled or that Windows N has Media
Foundation installed. Desktop FFmpeg loads MFPlat and D3D11 dynamically; the
build checks imports for mandatory MFPlat/MFReadWrite/MF DLL dependencies.

Video trim/resize/speed tools produce MP4 copies with MPEG-4 and AAC. Audio trim
and normalization produce lossless WAV copies. Video audio extraction produces
MP3. Tools retain the source filename with a suffix. Every output is staged in
its destination directory and committed through OutputWriter without replacing
originals or existing outputs. Canceled/failed stages are deleted. Normalization
uses FFmpeg loudnorm with default -16 LUFS, true peak -1.5 dBTP and LRA 11; audio
output is 48 kHz. Lossy audio presets use 192 kbps, 48 kHz stereo. Quality 1–100
maps to MPEG-4/WMV quantization and VP9 CRF. Resize preserves aspect ratio by
default, fits within both supplied dimensions, and uses even dimensions for
YUV420 output. Speed accepts 0.25–4 times with chained atempo filters and adjusted
video timestamps. Trim validates finite start/end values against ffprobe duration.
Media conversion selects the first picture and audio streams; subtitles, other
streams, chapters and metadata are not copied.

GIF is accepted by this media engine as a video input: it decodes and preserves
animation frames through time, and does not flatten the file to a single frame.
GIF output is silent. The separate image engine remains restricted to single-frame
images. GIF does not offer audio extraction. Other image formats use ImageEngine.

## Build and corresponding source

`packaging/media-lock.json` pins all six upstream source commits, URLs and SHA256
hashes. `packaging/build-media.sh` builds x86_64 Windows binaries using MinGW GCC,
CMake, NASM, make, pkg-config, patch and Python 3.12+. Acquisition is build-time
only. On a Linux machine with those tools available:

```sh
bash packaging/build-media.sh
```

Set `CITRUS_MEDIA_SOURCE_CACHE` to a directory containing the named upstream
archives to build without downloading. `CITRUS_MEDIA_WORK`, `CITRUS_MEDIA_OUTPUT`,
`CITRUS_MEDIA_JOBS` and `CROSS_COMPILE` customize directories/concurrency/toolchain.
The default output is `packaging/media/win-x64`. Ogg, Vorbis, Opus and libvpx are
BSD-style static dependencies within replaceable FFmpeg DLLs. FFmpeg is shared,
LGPL 2.1 or later, with GPL/nonfree/version3/autodetection/network/ffplay disabled.
LAME is a separate LGPL shared encoder; its frontend and decoder are disabled.
The pinned MinGW-w64 winpthreads DLL is bundled alongside the codecs; its MIT/BSD
notices and the GCC runtime exception notices are included. The small LAME export-list patch removes symbols belonging to that disabled
decoder, so the DLL links without enabling it. Opus DRED/deep PLC/OSCE are disabled;
no neural model bootstrap downloads are performed.

The published app includes `source/citrus-media-source.tar.gz`, actual source
trees, original archives, the LAME patch, build script, lock manifest, toolchain
versions, build log, FFmpeg configuration, headers and import libraries. To rebuild
from that source archive without a repository checkout (requires the toolchain above):

```sh
mkdir extracted rebuild
# Run from the directory containing the distributed source archive:
tar -xzf citrus-media-source.tar.gz -C extracted
mkdir -p rebuild/packaging/notices rebuild/docs
cp extracted/provenance/build-media.sh extracted/provenance/media-lock.json rebuild/packaging/
cp extracted/provenance/verify-media.py rebuild/packaging/
cp -R extracted/provenance/patches rebuild/packaging/
cp -R extracted/provenance/media-licenses rebuild/packaging/notices/
cp extracted/provenance/media-engine.md rebuild/docs/
CITRUS_MEDIA_SOURCE_CACHE="$(pwd)/extracted/downloads" bash rebuild/packaging/build-media.sh
```

The recipe reconstructs every repository input read by the build and source
redistribution steps. Use a fresh work directory, not the extracted patched source
as the work tree: the builder extracts the hash-checked original downloads and
applies the LAME patch once. Configuration and versions are in `provenance/`.

Replace ABI-compatible FFmpeg or LAME DLLs beside the two executables to use
modified versions; both executables are ordinary dynamically linked programs.
The application uses child processes, not static links to these libraries.
Reverse engineering for debugging modifications to the LGPL libraries is
permitted. Full copyright/license texts are under `notices/media-licenses/`.

## Validation

Host integration tests use actual generated tone/video files and ffprobe output:

```sh
dotnet test tests/Citrus.Conversion.Tests --filter FullyQualifiedName~MediaEngineTests
```

Override `CITRUS_TEST_FFMPEG` and `CITRUS_TEST_FFPROBE` with absolute executable paths
when needed. The default host test paths are `/usr/bin/ffmpeg` and `/usr/bin/ffprobe`.
Managed fixtures generate PCM WAV and raw YUV files directly, then encode/mux
with the selected FFmpeg. Windows CI runs both the managed suite and this
raw-file smoke test against the bundled executables (no capture devices ship):

```sh
python packaging/verify-media.py --directory packaging/media/win-x64
```

Linux can run the PE smoke test with `--wine /absolute/path/to/wine64`. Wine results
are compatibility checks, not Windows acceptance. Before release run the smoke
test and managed app on Windows 10 22H2 and Windows 11 x64, including Windows N or
an unavailable MFT; verify optional H.264 gating, cancellation, non-ASCII paths,
and playback of produced copies. Explorer behavior belongs to separate Windows
acceptance. Publishing must retain the media DLLs, notices and source archive.
