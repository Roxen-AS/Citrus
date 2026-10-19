# Local PDF operations

`PdfEngine` offers ordered `images.pdf` groups, ordered `pdf.merge` groups, and
single-PDF `pdf.split`, `pdf.images`, and `pdf.text`. The application composition
layer must register this engine with `OperationCatalog`/`JobQueue`.

Image creation shares `ValidatedImageLoader` with image conversion: explicit
raster decoders, internal MSVG, self-contained SVG checks, exactly one frame,
and automatic orientation. Animated GIF/WebP and multipage TIFF inputs fail
before a PDF is created. Images map at a fixed 96 pixels/inch to PDF points;
embedded density metadata does not change page size. There is no JPEG recompress
step: validated images are normalized to lossless PNG streams before PDFsharp.

Blank page selection selects every actual PDF page. Commas and ascending
one-based ranges preserve the requested order (`3,1-2` selects 3,1,2). Zero,
out-of-range/reversed ranges, duplicates and malformed tokens fail before output.
Split creates one PDF for each selected page. Raster export creates one PNG for
each selected page at 300 DPI, including annotations and filled forms. Text export
uses PdfPig's content-order extractor, with a blank line between selected pages;
it performs no OCR and can be empty for image-only pages. Complex layout reading
order and font-specific text extraction remain limited by the backend.

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

The exact dependencies and licenses are recorded in `packaging/pdf-backend.lock.json`
and `packaging/notices/PDF-Notice.txt`. The patched cryptography reference avoids
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
