# Enabled operations

Availability is the intersection for the selected files. Unsupported, offline
cloud placeholders and virtual drag payloads are rejected without hydration.
Folders offer Actions only. Actual decoder errors report per file; extension
recognition is not a promise that every codec in a container is supported.
Windows native fixture and Explorer acceptance are still pending.

| Area | Enabled outputs and tools | Limits |
| --- | --- | --- |
| Single-frame images | JPEG, PNG, WebP, TIFF, BMP, still GIF, AVIF; resize, rotate, compress, strip metadata | PNG/JPEG/WebP/TIFF/BMP/GIF/AVIF input; animated or multipage image jobs reject. Self-contained SVG input uses internal MSVG for raster conversion and image-to-PDF, no external resources. HEIC, PDF/PS/EPS delegates disabled. |
| Audio | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA; trim, normalize | Lossy presets 192 kbps/48 kHz stereo; trim/normalize produce WAV. -16 LUFS default, no promised exact file size. |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, animated GIF; trim, resize, speed, extract MP3 audio | MP4/MOV/MKV/AVI use MPEG-4 Part 2, WebM VP9/Opus, WMV WMV2/WMAv2. Optional MP4 H.264 appears only after an actual Windows Media Foundation encode/probe succeeds. GIF is silent; animated GIF is valid video input. |
| PDF | Ordered image-to-PDF, ordered PDF merge; split selected pages into individual PDFs; PNG pages at 300 DPI; TXT extraction | No encrypted PDFs or OCR. Page selection accepts `3,1-2`, preserving order; blank means all. Invalid/duplicate pages reject before output. Native render cancellation is between pages. |
| Subtitles | SRT ↔ VTT; either to readable TXT | Preserves multiline cue text; VTT identifiers/settings/regions survive only VTT. NOTE/STYLE/header metadata omitted; TXT retains inline markup. |
| Archives | Ordered regular-file groups to ZIP/TAR; one file to GZIP; extract ZIP/TAR/GZIP/RAR | No recursive folder creation. Duplicate grouped basenames reject. `.tar.gz` extraction yields a TAR for a second extraction. Unsafe paths, links and encrypted/incomplete RAR reject. Maximum 100,000 entries and 20 GiB expanded bytes. |
| Actions | Copy files, Copy paths, Move originals…, Reveal in Explorer, Open Terminal, Float | Full selection, including folders. Shelf holds references only. Move asks for a destination and does not overwrite/merge collisions; it can report partial completion. |

Ordinary batches run independently and show partial successes. PDF merge,
image-to-PDF and ZIP/TAR show an ordered list with Move up/down. Mixed-parent
groups require an explicit destination. The UI displays actual returned output
paths; collision suffixes are chosen atomically. Tool names use operation
suffixes. Source bytes and locations remain unchanged by conversion/tools.

Quality accepts 1–100 (default 85); speed 0.25–4 times (default 1). Resize needs
positive dimensions and preserves aspect ratio by default. Trims need a
nonnegative start and later end within the actual duration. Canceling an options
dialog submits no work. Canceling work removes staging files; unrelated batch
items continue. PDF groups roll back outputs created by that request on failure.

Media streams use the first picture/audio stream; subtitle streams, chapters and
metadata are omitted. Runtime media catalogs gate presets; unavailable Media
Foundation retains the MPEG-4 preset. For details see [media](media-engine.md),
[PDF](pdf-engine.md), [subtitles/archives](subtitle-archive-engines.md),
[conversion UI](conversion-interface.md), and [native actions/settings](native-actions-settings.md).
Office/document/EPUB conversion, visual editors, waveform tools, OCR and native
Windows sharing are later work, not placeholder operations in this candidate.
