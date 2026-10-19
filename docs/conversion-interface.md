# Converting files in Citrus

Add local files with **Add files…**, or drop them into the main window. Use **Remove** or **Clear** to change the selection. Convert and Tools show the intersection of operations supported by every input. The Explorer wheel offers the same catalogue: tap Shift for Convert or Alt for Tools during a file drag, then drop on an operation. Releasing the key leaves the wheel open; the center or Escape cancels.

Image, PDF, subtitle and archive engines are available locally. Media discovery runs in the background against the bundled FFmpeg/FFprobe files, including the conditional Windows H.264 encoder probe. If discovery fails, the app keeps the other engines and displays a reinstall message. It never downloads engines at runtime.

**Create copies** submits one independent job per ordinary input. Errors do not stop unrelated jobs. Group operations (merge PDF, images to PDF, ZIP and TAR) always open confirmation, even when they need no parameters. The input list preserves incoming order; Move up/down changes it. The first input supplies the output name. A group from one folder defaults to that folder; mixed-folder groups require an explicit **Choose folder…** choice. Cancel or closing the dialog submits nothing.

Options cover resize (positive dimensions, aspect ratio on by default), image quality (1–100, default 85), rotation, trim (seconds), speed (0.25–4), normalization (-70 to -5 LUFS, default -16), and PDF pages (blank means all; `1,3-5` selects ordered pages). Trim end must fit within every readable selected input. PDF ranges are checked against every readable page count before submission. An unreadable input receives its own failed job; valid siblings can still run. Plain format conversions use the engine presets without an additional dialog.

Each job displays its source, operation, state, progress and actual output names. Progress remains indeterminate when the engine has no meaningful percentage. **Cancel** requests cancellation of queued/running work. **Retry** creates a new job using the previous options, with confirmation again where needed. **Choose folder…** on a failed job retries into a chosen destination; it never elevates privileges. **Reveal** opens Explorer at a committed output. Existing outputs and originals are never overwritten by conversion.

The window lists session-only history. Closing it leaves Citrus in the tray; Open Citrus restores it, Pause drag gestures pauses capture, and Quit cancels workers. Work uses at most two concurrent conversions by default.

## Availability and limits

Unsupported operations are omitted, and submission is disabled when the entire selection has no common operation. This interface does not display deferred Office/document conversion or editor buttons. Folders and offline cloud placeholders cannot enter the conversion selection.

Still-image operations reject animated/multipage images. The distinct **GIF still image** and **GIF animation** choices identify the image and media routes; they keep separate stable operation IDs. SVG is input-only. HEIC is not advertised without decoder validation. H.264 appears only after the local encoder probe succeeds. ZIP/TAR groups reject duplicate basenames; encrypted PDFs are unsupported. Archive extraction caps are 100,000 entries and 20 GiB expanded data. Extracting `.tar.gz` produces a TAR file that requires a second extraction; nesting is not automatic.

The current build is a Windows candidate. Linux cross-compilation and host engine tests do not establish Explorer, native dialog, tray, accessibility, high-contrast, or DPI behavior. Execute `tests/Citrus.Windows.Tests` and the manual Windows acceptance list before release.
