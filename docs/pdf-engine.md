# Local PDF operations

The current `PdfEngine` supports ordered image-to-PDF and merge groups;
organization, splitting, compression and metadata editors; PNG/JPG pages; and
DOCX/PPTX/XLSX/EPUB/TXT/MD reconstruction. `EngineRegistry` registers it with the
application's catalog and queue. The [reference checklist](reference-operation-verification.md)
and [acceptance ledger](reference-feature-coverage.md) distinguish implementation
and fixture output proof from complete native workflow/visual acceptance.

Image creation shares `ValidatedImageLoader` with image conversion: explicit
raster decoders, internal MSVG, self-contained SVG checks, exactly one frame,
and automatic orientation. Animated GIF/WebP and multipage TIFF inputs fail
before a PDF is created. Images map at a fixed 96 pixels/inch to PDF points;
embedded density metadata does not change page size. There is no JPEG recompress
step: validated images are normalized to lossless PNG streams before PDFsharp.

Blank page selection selects every actual PDF page. Commas and ascending
one-based ranges preserve the requested order (`3,1-2` selects 3,1,2). Zero,
out-of-range/reversed ranges, duplicates and malformed tokens fail before output.
That restriction applies to textual page-range syntax; the organizer intentionally
supports duplicating page objects.
Split creates one PDF for each selected page. Raster export creates one PNG for
each selected page at 300 DPI, including annotations and filled forms. Text export
uses the shared semantic extraction model, including table cells and OCR where
needed. Explicit selected-page order is retained before within-page reading order.
English OCR uses the pinned Tesseract runtime/data, including scanned regions of
otherwise textual pages. Missing data, low-quality scans, complex layout and
font encoding can limit reconstruction; warnings travel with the job result.

## Typed editors and editable exports

The white organizer shows page thumbnails and a preview, selection, move up/down,
rotation, duplication/removal and undo/redo. Split exports selected retained pages.
Metadata provides search, supported descriptive edits, field removal/restore and
Remove All; page counts and other technical facts remain read-only and follow the
selected source. Tool-specific Apply, Compress and Save actions produce immutable
recipes over captured source identities. Accepted source bytes are verified again
before export.

DOCX/PPTX/XLSX outputs contain real editable text, paragraphs/text boxes and table
cells where the shared extractor recovers them. Raster artwork may remain images;
a page screenshot is not presented as recovered editable text. Scanned text and
ruled tables are best effort, not guaranteed reproduction. EPUB, Markdown and TXT
reuse the extraction and diagnostics. Markdown with retained artwork returns an
asset directory together with the document. Office schema, cell content and output
reopening are tested on bounded fixtures, not every real-world document.

Compression defaults to preserving PDF structure and compressing content streams.
Percentage and Target Size compare actual final bytes; an unreachable goal saves
no copy. Quality explains and requires explicit **Rebuild as page images** consent,
because that path removes editable text, forms, links and accessibility. Its JPEG
quality value reaches the raster export recipe. Reset restores the complete
compression goal. Compression does not silently rasterize to meet a byte target.

Typed previews and exports share page order, rotation and recipe settings. A
multi-file typed export publishes one collision-safe directory after validation;
the older direct split/raster API returns individual paths with request-wide
rollback. Cancellation and final-notification failure remove only outputs owned
by that request, preserving originals and unrelated existing files.

## Source and output handling

Inputs remain in place. A group uses its first input's stem and directory, unless
an explicit destination is provided; mixed input directories require one. Output
names are `first-combined.pdf`, `first-merged.pdf`, `first-page-N.pdf/.png`, or
`first-text.txt`. `OutputWriter` stages each file beside its destination and claims
a collision-safe name without replacement. If any later step fails or cancellation
is observed, the engine deletes only output paths it created for that request.
A cleanup permission/I/O failure is reported with the original failure. Outputs
are ordinary copies; successful paths are all returned in `ConversionResult`.

Encrypted PDFs, including files readable with an empty user password, are not
supported. PDF inputs are preflighted before writing; malformed data can also fail
later during page import, rendering or extraction, invoking the same rollback.
PDFium rendering has a process-wide engine semaphore held through each synchronous
render. Waiting is cancellable; a synchronous native page render cannot be
interrupted mid-page, so cancellation is checked before/after it and rolls back.
Other engines do not acquire this semaphore and can use the queue's other slot.
All work is scheduled off the calling/UI thread.

The core PDF dependencies and licenses are recorded in `packaging/pdf-backend.lock.json`
and `packaging/notices/PDF-Notice.txt`. Semantic extraction also uses pinned
Open XML/Tabula/PdfPig dependencies; the Office/OCR pack and source/license hashes
are in `packaging/feature-pack.lock.json` and `packaging/feature-pack-runtime.sha256.json`.
Those runtime trees must be provisioned locally; managed restore does not acquire
the ignored Office binaries or OCR data. Clean-machine deployment remains a
separate gate. The patched cryptography reference avoids
PDFsharp6.2.4's vulnerable minimum dependency. No PDFsharp6.5.0 dependency is used.
Publishing carries native PDFium/Skia, all PDF notices and this document.

Run the portable real fixtures with:

```sh
dotnet test tests/Citrus.Conversion.Tests --filter FullyQualifiedName~PdfEngineTests
```

`packaging/verify-pdf-publish.py PUBLISH_DIRECTORY` checks Windows native hashes,
managed PDF assemblies, provenance and every notice from a clean self-contained
win-x64 publish. `.github/workflows/windows.yml` runs PDF fixtures on Windows
and checks that publish. Linux fixtures exercise Linux binaries; Linux
cross-compilation or publishing does not prove Windows rendering, Explorer, WPF,
installer, focus, drag/drop or accessibility behavior.
