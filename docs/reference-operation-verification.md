# Reference operation verification

> Public text summary. Raw machine receipts and user reference captures are retained privately. See [preview notes](PREVIEW-NOTES.md) for current limits; test sources are included in the repository.

Updated: 2026-10-21.

See the later compact-editor and 29-image follow-up audit (machine-local evidence retained privately) for the smaller menus, visual video editors and current checks. This operation inventory retains its complete native/state acceptance gates.

This checklist is linked to the [83 reference entry points](reference-wheel-inventory.json) and the [coverage report](reference-feature-coverage.md). Repeated entry points share one operation below. A registered entry is not proof of complete native acceptance.

All 81 IDs below resolve through the current engine registry. Requested tools have typed editor routes rather than a generic copy form. The output column names the applicable regression families; it is **not** a claim that each operation has passed every native control and state. Concrete run results and remaining failures are in the [coverage report](reference-feature-coverage.md) and durable evidence index (machine-local evidence retained privately).

The final acceptance gate for each row remains: native workflow, independently reopened/decoded output, control comparison, default/loading/edited/invalid/processing/canceled/failed/successful states as applicable, keyboard/focus, applicable undo/redo and safe close/cancel. Raster captures at 100%, 150% and 200% do not establish physical monitor DPI behavior. Unseen dropdowns/cropped controls cannot be claimed as exact reproduction.

Native host evidence is additionally recorded in `ImageReferenceControlTests`, `ImageEditorWindowTests`, `ImageNativePreviewInteractionTests`, `ImageMetadataWindowReferenceTests`, `MediaEditorReferenceTests`, `LongMediaNavigationTests` and `PdfEditorReferenceTests`. These are bounded workflows and layout checks, not universal acceptance. `ReferenceChromeConsistencyTests` checks the shared shell independently.

The controlled native cancellation run verifies real mouse-message suppression and recovered ordinary clicks over owned windows. It does not close the separate Explorer/OLE or complete per-operation state gates below. The broad filtered Windows run recorded one capture-ownership failure; diagnostic metadata proved the computer-use cursor overlays caused it, and that unchanged case passed after the agent ended its own inspection session. This targeted pass is not a rerun of the whole suite. Package integrity/negative checks do not establish clean-machine deployment. Current results and limits remain in the linked coverage report.

| Unique operation | Reference family | Applicable output regression family | Native/state acceptance |
|---|---|---|---|
| archive.extract | archive | ArchiveEngineTests / ArchiveRepackTests | Pending complete acceptance |
| archive.gzip | archive | ArchiveEngineTests / ArchiveRepackTests | Pending complete acceptance |
| archive.tar | archive | ArchiveEngineTests / ArchiveRepackTests | Pending complete acceptance |
| audio.bleep | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| audio.channels | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| audio.compress | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| audio.metadata | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| audio.normalize | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| audio.trim | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| audio.visualizer | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| document.convert.docx | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.epub | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.html | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.jpg | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.md | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.pdf | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.png | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| document.convert.txt | document | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| epub.convert.docx | epub | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| epub.convert.html | epub | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| epub.convert.md | epub | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| epub.convert.pdf | epub | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| epub.convert.txt | epub | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| image.annotate | image | ImageReferenceParityTests / ImageEditorWindowTests | Pending complete acceptance |
| image.background | image | ImageReferenceParityTests / ImageEditorWindowTests | Pending complete acceptance |
| image.compress | image | ImageReferenceParityTests / ImageEditorWindowTests | Pending complete acceptance |
| image.convert.avif | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| image.convert.bmp | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| image.convert.heic | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| image.convert.png | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| image.convert.tiff | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| image.convert.webp | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| image.crop | image | ImageReferenceParityTests / ImageEditorWindowTests | Pending complete acceptance |
| image.editor | image | ImageReferenceParityTests / ImageEditorWindowTests | Pending complete acceptance |
| image.metadata | image | ImageMetadataReferenceTests / ImageMetadataWindowReferenceTests | Pending complete acceptance |
| image.redact | image | ImageReferenceParityTests / ImageEditorWindowTests | Pending complete acceptance |
| images.pdf | image | ImageEngineTests / ImageReferenceParityTests | Pending complete acceptance |
| media.convert.aiff | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.avi | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| media.convert.flac | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.gif | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| media.convert.m4a | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.mkv | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| media.convert.mp3 | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| media.convert.mp4 | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| media.convert.ogg | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.opus | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.wav | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.webm | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| media.convert.wma | audio | MediaEngineTests / MediaEffectParityTests / MediaEditorExportParityTests / MediaNestedMetadataTests | Pending complete acceptance |
| media.convert.wmv | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| pdf.compress | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.docx | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.epub | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.images | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.jpg | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.markdown | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.metadata | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.organize | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.pptx | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.split | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.text | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| pdf.xlsx | pdf | PdfEngineTests / PdfReferenceExportsTests / PdfReconstructionTests / PdfEditorEngineTests | Pending complete acceptance |
| presentation.convert.jpg | presentation | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| presentation.convert.pdf | presentation | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| presentation.convert.png | presentation | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| spreadsheet.convert.csv | spreadsheet | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| spreadsheet.convert.ods | spreadsheet | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| spreadsheet.convert.pdf | spreadsheet | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| spreadsheet.convert.tsv | spreadsheet | DocumentConversionTests / OfficeFeatureMatrixTests | Pending complete acceptance |
| subtitle.txt | subtitle | SubtitleEngineTests | Pending complete acceptance |
| subtitle.vtt | subtitle | SubtitleEngineTests | Pending complete acceptance |
| video.compress | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.crop | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.metadata | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.mute | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.redact | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.snapshot | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.speed | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.split | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
| video.trim | video | MediaEngineTests / MediaEditorExportParityTests / VideoCropEngineTests | Pending complete acceptance |
