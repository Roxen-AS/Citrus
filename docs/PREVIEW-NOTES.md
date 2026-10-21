# 0.3 preview notes

Citrus 0.3.0-preview.1 includes the compact wheel, white menus and focused image, media and PDF editors. The complete package bundles offline engines. Source registration, native control matching, performance and clean-machine acceptance are separate questions.

## Current boundaries

- Windows x64 is supplied. The technical minimum is build 19041; Windows 11 is recommended. Other operating systems and native ARM64 are not packaged.
- The installer is unsigned. Windows may identify its publisher as unknown. Verify the release source and checksum before deciding to run it.
- Extension recognition does not guarantee every codec/container combination. H.264 depends on actual Windows Media Foundation capability; baseline MP4 editing uses MPEG-4 Part 2/AAC. Visual editing is SDR.
- Compression goals may be unreachable. The app reports failure rather than quietly changing dimensions, channels or duration to meet one.
- PDF-to-Office and OCR reconstruct recoverable content. Complex tables, fonts and layouts may need correction. English OCR data is included; encrypted PDFs reject. Pages destinations write compatible `.docx`, not fabricated `.pages` files.
- Still-image tools reject animated/multipage inputs where an operation cannot preserve them. Some conversion presets omit secondary streams or metadata; use the Metadata tool when that is the task.
- Archives reject unsafe paths, links, duplicates and unsupported encrypted/incomplete members, with entry-count and expanded-size limits.
- The workspace's static MP4 thumbnail is limited. Actual video editors show decoded, playable video.
- Native glass excludes its windows from capture to prevent feedback. Recordings may omit it. Opaque accessibility/device fallbacks remain available.

## Verification and performance

Current local verification recorded **604 backend passes** across two disjoint batches, **234 core passes**, and **52 passes** in the final compact video/control/crop batch. Windows batches overlap and must not be summed. Outputs were independently reopened where applicable. Source-hash, cancellation and collision-safe publication checks remain enabled.

Generated-source optical rendering/readback/bitmap publication measured p95 2.60 / 3.30 / 8.06 ms at 1× / 1.5× / 2×. These are bounded GPU experiments, not desktop scanout. Prepared native target-show p95 was 18.17 and 20.54 ms, above the 16.7-ms target. Tiny-file video-editor readiness ranged from 347.3 to 611.7 ms: single observations per tool, not percentiles. Exact source scanning and verification still take work.

Complete five-cold/thirty-warm production first-blur timing, the full Explorer/OLE sequence, physical mixed-DPI transitions and every operation's full native state matrix remain unqualified. There is no zero-latency promise. Package validation is recorded separately with the release.

## Reporting a problem

Include version, Windows build, display scale, format/codec where known and exact steps. Use a small non-sensitive reproduction. For cancellation trouble, say which buttons/modifiers were held and whether normal clicks recovered after release. Do not post private files or unredacted screenshots. [Open an issue](https://github.com/Skytuhua/Citrus/issues/new/choose).
