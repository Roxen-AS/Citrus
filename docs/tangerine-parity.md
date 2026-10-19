# Tangerine parity work

The target is Tangerine's practical workflow on Windows: start dragging, tap a mode key, choose a wheel target, and receive a local result. The user's supplied screenshots are the visual reference. Official public references: https://tangerineformac.com/ and https://tangerineformac.com/tools . Research observed App Store 2.0.1; public marketing counts do not establish every directed route or numeric default.

## Delivery sequence

1. Stable immutable drag selection, retained rounded wheel, file previews, motion/sound, usable workspace and Windows rendering evidence: `docs/superpowers/plans/2026-10-18-citrus-interface-implementation.md`.
2. Real visual image editors: target-size compression, metadata, adjustments, annotations, backgrounds, crop, redaction, collage, image-to-PDF.
3. Media timeline tools: measured compression, metadata, mute, trim, crop, speed, snapshot, split and audio tools with preview/waveform.
4. PDF organizer/export, subtitle/archive refinement, offline QR/OCR/face detection where a verified redistributable model/runtime is available.
5. Native sharing and pinned floating shelf; document/slides/spreadsheet/EPUB routes with an app-owned offline backend and tested fixtures.

## Current gaps and release gates

The app has local image/media/PDF/subtitle/archive conversion and six native actions. The current source adds a real image crop/adjustment/resize/rotate editor with frozen per-image exports, plus renderer-backed annotation and manual masking with exact font selection, manipulation, undo and preview. At exact `cb8313c84492badc425e3c42b2db85ba1f6befd8`, eight native annotation cases, six image-editor window cases and eight material/control cases passed; WPF/UIA execution is recorded in [verification.md](verification.md). Physical Explorer, mixed-monitor/OS-accessibility and complete visual acceptance remain pending.

Media playback components are implemented; complete media editor workflows are still being integrated. Canvas/collage, richer media/PDF editors and the Office/EPUB backend remain open work. Existing Reveal is not a substitute for native Share. HEIC is disabled pending a working decoder/fixture. Automatic face/text redaction requires bundled inference/OCR and verification; manual masks must not be called automatic detection. Office layout conversion needs a real renderer; PDF-to-editable reconstruction and Apple output formats are not established by LibreOffice. macOS AirDrop requires a Windows sharing equivalent.

The exact public conversion matrix, proprietary animations/audio and unpublished defaults are not available as source evidence. Implement original Citrus assets and observed behavior; do not label unverified parity as 100%.

Hosted Windows installer compilation and interruption/retry/owned-uninstall/output-preservation checks passed at earlier exact `71fbdbb4e4c405f0e600b71415331b99a62bc3bb`. Current cb8313c installer/recovery execution is still in progress at this record; no current candidate is recorded as installed on the user’s PC. Cross-building and WPF renders do not prove physical Explorer stability, mixed-DPI transitions, device sound or actual OS accessibility. The production native wheel gate remains false: both raw host-probe variants now complete guarded captures with a visible magenta target control but zero live host response on Windows Server 26100 x64. This does not establish Windows 11 client behavior or accepted optical glass. Track exact native evidence and remaining scope in [verification.md](verification.md).
