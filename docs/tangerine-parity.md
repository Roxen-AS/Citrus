# Tangerine parity work

The target is Tangerine's practical workflow on Windows: start dragging, tap a mode key, choose a wheel target, and receive a local result. The user's supplied screenshots are the visual reference. Official public references: https://tangerineformac.com/ and https://tangerineformac.com/tools . Research observed App Store 2.0.1; public marketing counts do not establish every directed route or numeric default.

## Delivery sequence

1. Stable immutable drag selection, retained rounded wheel, file previews, motion/sound, usable workspace and Windows rendering evidence: `docs/superpowers/plans/2026-10-18-citrus-interface-implementation.md`.
2. Real visual image editors: target-size compression, metadata, adjustments, annotations, backgrounds, crop, redaction, collage, image-to-PDF.
3. Media timeline tools: measured compression, metadata, mute, trim, crop, speed, snapshot, split and audio tools with preview/waveform.
4. PDF organizer/export, subtitle/archive refinement, offline QR/OCR/face detection where a verified redistributable model/runtime is available.
5. Native sharing and pinned floating shelf; document/slides/spreadsheet/EPUB routes with an app-owned offline backend and tested fixtures.

## Current gaps and release gates

The existing app has local image/media/PDF/subtitle/archive conversion and six native actions, but lacks the rich visual editors and Office/EPUB backend. Existing Reveal is not a substitute for native Share. HEIC is disabled pending a working decoder/fixture. Automatic face/text redaction requires bundled inference/OCR and verification; manual masks must not be called automatic detection. Office layout conversion needs a real renderer; PDF-to-editable reconstruction and Apple output formats are not established by LibreOffice. macOS AirDrop requires a Windows sharing equivalent.

The exact public conversion matrix, proprietary animations/audio and unpublished defaults are not available as source evidence. Implement original Citrus assets and observed behavior; do not label unverified parity as 100%. Cross-building on Linux does not prove Explorer stability, mixed-DPI rendering, native sound, accessibility or Windows installer behavior. Track actual native evidence in `docs/verification.md`.
