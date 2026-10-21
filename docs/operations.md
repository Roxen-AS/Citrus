# Enabled operations

Availability is the intersection for the selected files. Unsupported, offline
cloud placeholders, recall-marked files and virtual drag payloads are rejected
without hydration. Locally ready files remain eligible when they retain reparse
metadata. Folders offer Actions only. Actual decoder errors report per file; extension
recognition is not a promise that every codec in a container is supported.
This describes the **0.3 preview**, not the historical 0.1 installer payload.
Availability also depends on the bundled engine pack and its capability checks.
See the [preview notes](PREVIEW-NOTES.md) for current acceptance limits; implemented does
not mean every native control/state, physical DPI or Explorer workflow is accepted.

| Area | Enabled outputs and tools | Limits |
| --- | --- | --- |
| Single-frame images | JPEG, PNG, WebP, HEIC, TIFF, AVIF, BMP, still GIF, PDF; Edit Photo, Annotate, Add BG, Crop, Redact, Compress, Metadata, resize/rotate/strip | HEIC/HEIF requires the verified genuine HEVC helper; it is not an AVIF file renamed to HEIC. Animated/multipage still-image jobs reject. Self-contained SVG uses internal MSVG for raster conversion and image-to-PDF; external resources and PDF/PS/EPS image delegates remain disabled. |
| Audio | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA; Compress, Metadata, Normalize, Visualize, Trim, Channels, Bleep | Processed PCM audition, channel gains, ranges and loudness use the accepted recipe. Trim/normalize/channels/bleep default to WAV; visualization creates real MP4 media. Byte targets are measured and can be unreachable; no silent channel/duration change to meet one. |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, animated GIF; Compress, Metadata, Mute, Trim, Crop, Speed, Snapshot, Split, Redact; retained resize/audio extraction | MPEG-4 Part 2/AAC is the baseline MP4 editing output; optional H.264 requires the actual Media Foundation probe. Crop MP4 needs even dimensions of 2–8190 pixels per axis; snapshots retain source resolution, including odd dimensions. Visual editing is SDR; GIF is silent and retains animation. |
| PDF | DOCX, PPTX, XLSX, JPG, PNG, EPUB, TXT, MD; Compress, Metadata, Split, Organize, ordered merge and image-to-PDF | Editable Office objects and OCR text are reconstructed where recoverable, with warnings and best-effort layout/table recognition. English OCR needs bundled data. Encrypted PDFs reject. Organizer permits explicit duplication; textual page-range syntax rejects duplicates. Raster rebuilding is explicit and removes editable content. |
| Document | DOCX (Apple Pages compatible), PDF, TXT, MD, HTML, EPUB, PNG, JPG | Offline Office/Pandoc pack required. PAGES destinations write honestly labeled `.docx`, never a renamed `.pages`; font/layout substitution can occur. Page-image outputs may form a multi-file directory. |
| Presentation | PDF, JPG, PNG | Offline Office pack; exports actual pages/slides. Layout/font fidelity is best effort. |
| Spreadsheet | ODS, CSV, TSV, PDF | Offline Office pack; CSV/TSV export every sheet as separate files when needed. Delimited text cannot retain workbook formatting/formulas. |
| EPUB | PDF, DOCX (Pages compatible), TXT, MD, HTML | Offline converters; flow/layout reconstruction is best effort. |
| Subtitles | SRT ↔ VTT; either to readable TXT | Preserves multiline cue text; VTT identifiers/settings/regions survive only VTT. NOTE/STYLE/header metadata omitted; TXT retains inline markup. |
| Archives | Ordered regular-file groups to ZIP/TAR, one file to GZIP; existing archive member repack; extract ZIP/TAR/GZIP/TGZ/RAR | Existing archives are repacked by members, not nested as the original file; GZIP repacks use `.tar.gz`. Compound TAR/GZIP extracts in one operation. No recursive selected-folder creation. Unsafe paths, duplicate members, links and encrypted/incomplete RAR reject. Shared maximum 100,000 entries and 20 GiB expanded bytes. |
| Actions | Copy files, Copy paths, Move originals…, Reveal in Explorer, Open Terminal, Float | Full selection, including folders. Shelf holds references only. Move asks for a destination and does not overwrite/merge collisions; it can report partial completion. |

Ordinary batches run independently and show partial successes. PDF merge,
image-to-PDF and ZIP/TAR show an ordered list with Move up/down. Mixed-parent
groups require an explicit destination. The UI displays actual returned output
paths; collision suffixes are chosen atomically. ZIP/TAR use the first confirmed
input's stem plus `-archive.zip` / `-archive.tar`, even for a one-file group;
reordering changes the first stem. An existing `first-archive.zip` yields
`first-archive (1).zip`. GZIP retains the whole source filename (`first.txt.gz`).
Tool names use operation suffixes. Source bytes and locations remain unchanged by conversion/tools.

Quality accepts 1–100 (default 85 for ordinary quality-based conversions);
compression editors start with a 50% reduction goal and also offer Quality and
Target Size. A byte/reduction target succeeds only after measured output fits;
an unreachable target publishes no copy. It does not silently reduce dimensions,
change the selected format, channels or duration. PDF Quality requires explicitly
opting into page-image rebuilding. Speed accepts 0.25–4 times (default 1). Resize needs
positive dimensions and preserves aspect ratio by default. Trims need a
nonnegative start and later end within the actual duration. Canceling an options
dialog submits no work. Canceling work removes staging files; unrelated batch
items continue. PDF groups roll back outputs created by that request on failure.

Ordinary format-conversion presets use the first picture/audio streams and omit
other streams, chapters and metadata. The typed Metadata tools instead remux
supported container/global/stream/chapter fields without reencoding, allow
supported edits/removal and verify the saved result. Other editor exports apply
their explicit metadata policy. Runtime media catalogs gate presets; unavailable Media
Foundation retains the MPEG-4 preset. For details see [media](media-engine.md),
[PDF](pdf-engine.md), [subtitles/archives](subtitle-archive-engines.md),
[conversion UI](conversion-interface.md), and [native actions/settings](native-actions-settings.md).
The offline image/document packs are pinned local build inputs with licenses,
hashes and source records; runtime jobs do not fetch codecs. Their large runtime
trees are not checked in. Clean-checkout acquisition and clean-machine execution
still need acceptance. Native Windows sharing remains outside this reference scope.

## Interactive video crop (local development)

Choose **Crop** from the Tools wheel or workspace. Drag the preview corners to resize,
drag inside to move, or use arrow keys to nudge (Shift moves ten pixels). The full source
plane remains visible and excluded areas darken. Width/height percentages, pixel dimensions
and aspect presets update the same accepted crop. Reset restores the full even-pixel area;
Ctrl+Z/Y undo/redo and Ctrl+0 resets. Apply writes a separate `-cropped.mp4` copy.

Playback and scrubbing do not trim the export. Preview uses bounded 15-second windows,
continues across them and steps using real frame timestamps. The crop timeline ends with
the video picture even if an audio track has a longer tail; export keeps the full original
duration and selected first audio stream. Surround sound or unavailable audio hardware
uses an explicitly indicated silent video preview; export retains the source channel count
in AAC at 48 kHz. Source identities are verified again before admission and export.

For multiple selected videos, each editor is confirmed before the batch is queued. Canceling
any editor submits none of that batch. Existing files are never replaced.
