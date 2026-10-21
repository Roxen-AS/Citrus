# Citrus 0.3.0-preview.1

**Convert files. Keep your flow.** A Windows preview with a compact file wheel and focused image, video, audio and PDF editors. Processing stays local; conversion and editing outputs are saved separately.

[Explore Citrus](https://citrous.app) · [Watch the product film](https://citrous.app/#product-demo) · [Quick start](https://github.com/Skytuhua/Citrus/blob/main/docs/QUICKSTART.md)

![The current Citrus interface](https://raw.githubusercontent.com/Skytuhua/Citrus/main/docs/launch/2026-10-21/media/product-hero.webp)

## In this preview

- A compact drag wheel: Shift to convert, Alt for tools, Ctrl for file actions. Scroll for pages; Escape, right-click or the neutral center cancels.
- White menus, centered titles and close buttons on the right. Glass stays on the wheel backing and pagination.
- Photo adjustments, cropping, annotation, redaction, backgrounds, compression and metadata.
- Real video playback and scrubbing, direct trim/crop controls, speed, mute, snapshot, split and redaction.
- Audio waveforms, trimming, channels, normalization, bleeps and visualizations.
- PDF organization and reconstruction, Office/EPUB conversions, archive repacking and subtitle conversion with the bundled offline engines.

## Choose a download

**Citrus-Setup.exe** is the Windows x64 installer. It includes the .NET runtime and offline engine pack, so it is a substantial download. Installation is per user. The installer presents the bundled components' notices and applicable Microsoft runtime terms. This preview is unsigned; Windows may show an unknown publisher. Verify the release source and SHA-256 before deciding to run it.

**SHA256SUMS** records the hashes of the supplied assets. **Citrus-Launch-Media.zip** contains product pictures, three silent edited demonstration videos, posters, captions, sample outputs and provenance. Product sequences use the actual native interface; they are composed demonstrations rather than live Explorer recordings or speed benchmarks.

Windows 11 is recommended. The technical minimum is Windows 10 build 19041. No macOS, Linux or native ARM64 installer is included. No account is needed for local file processing.

## Known limits and verification

This is a public preview, not a stable-release or zero-delay claim. Codec/container support varies. PDF reconstruction and OCR can require correction. Pages destinations produce clearly labeled compatible DOCX files. Lossy processing can change output quality; compression targets may be unreachable.

The clean public source builds, and its 234 core tests pass. Prior bounded application checks recorded 604 backend passes across two disjoint batches and 52 passes in the final compact-video UI batch. Launch demonstrations include independently reopened engine outputs. These checks do not establish every native workflow.

Full Explorer/OLE combinations, physical mixed-DPI transitions, clean-machine installation/recovery and five-cold/thirty-warm activation timing remain unqualified. Measured native target-show timings exceeded the 16.7-ms target. Read the [full preview notes](https://github.com/Skytuhua/Citrus/blob/main/docs/PREVIEW-NOTES.md) and the release's verification summary for the exact package checks.

[Report a problem](https://github.com/Skytuhua/Citrus/issues/new/choose) using a small non-sensitive sample. Keep backups and inspect your exported result.
