# Citrus Windows file converter design

Citrus is a Windows desktop utility that brings local file conversion to the pointer while a user drags a file in Explorer. The user requested Tangerine's experience for Windows, named Citrus, and approved a native tray app with a conversion wheel, separate tools and actions, local batch processing, and new output copies beside the originals.

This specification proposes the first installable release and records the remaining work toward broader Tangerine parity. Approval of this specification approves that release scope; it does not imply that the first release reproduces every Tangerine editor or conversion path.

## Product reference

Tangerine's current official material describes a radial menu that appears during a file drag: Shift opens conversion, Command opens tools, and Control opens actions. Releasing the modifier leaves the wheel open until drop or cancellation. Processing is local, and conversions and edits create adjacent copies. The website advertises 275 conversion paths, 26 tools, and six actions. These counts do not provide an exhaustive manifest of directed conversions. [Official website](https://tangerineformac.com/) and [press kit](https://tangerineformac.com/press).

The App Store describes compatible batch selections, menu-bar residence, compact progress windows, and revealing results in Finder. Its shortcut description differs from the newer website, so Citrus follows the website's tap-and-latch interaction. [App Store](https://apps.apple.com/us/app/tangerine-file-converter/id6813034065?mt=12).

The privacy policy states that processing runs on the device and that files, filenames, and contents are not transmitted. Citrus adopts local processing without accounts, analytics, advertising, or runtime engine downloads. [Privacy policy](https://tangerineformac.com/privacy).

Public screenshot links were located, but their pixels could not be inspected with the available research tool. Geometry, typography, colors, and animation are therefore Citrus design choices, rather than verified visual matches. Citrus uses original citrus-slice artwork, a compact translucent wheel, rounded panels, and an orange accent.

## First release scope

The first release targets Windows 10 22H2 and Windows 11, x64. It includes an installer, a tray utility, the Explorer conversion wheel, a normal window for accessible file selection and job progress, and working offline conversion engines.

| Area | First release behavior |
| --- | --- |
| Images | Convert common raster images to JPEG, PNG, WebP, TIFF, BMP, GIF, and AVIF where the bundled codec passes validation; resize, rotate, compress, and remove metadata. SVG is an input only. HEIC is enabled only after a fixture verifies the bundled decoder. |
| Audio | Convert to MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, and WMA when the bundled FFmpeg build provides the required encoder; trim and normalize audio. |
| Video | Convert to MP4, MOV, MKV, WebM, AVI, WMV, and GIF; extract audio to MP3; trim, resize, and change speed with audio synchronization. |
| PDF | Combine images into a PDF; merge PDFs; split or select pages; export page images and extract text using a bundled local PDF backend. |
| Subtitles | Convert SRT and VTT, and export readable TXT while preserving multiline cues and correctly translating timestamps. |
| Archives | Create ZIP, TAR, and GZIP; extract supported ZIP, TAR, GZIP, and RAR inputs into a new sibling directory. Extraction rejects paths outside that directory and unsupported links. |
| Batches | Offer the intersection of compatible operations. Ordinary conversions process each item independently and report partial success accurately; merge and archive-creation operations create an explicit group job. |
| Actions | Copy files to the clipboard, move to a chosen folder, copy paths, reveal in Explorer, open the containing folder in a terminal, and hold files in a floating shelf. |

The app never advertises a format solely because its extension appears in a list. The shipped backend must support the required decode/encode pair, and release validation must exercise representative fixtures. An unavailable operation is omitted or disabled with an explanation.

## Broader parity work

Tangerine's wider suite is a sequence of later subsystems, rather than placeholder buttons in the first release:

1. Documents, presentations, spreadsheets, and EPUB conversion, using an explicit local Office/document backend. Microsoft Office is not assumed to be installed. Pages and RTFD need independently validated support; PDF-to-editable Office files must describe reconstruction limits.
2. Visual image editing: crop previews, adjustments, annotations, backgrounds, redaction, collage, and QR reading.
3. Advanced audio/video editing: waveform trim, crop previews, join, split, frame capture, visualizer export, channel conversion, bleep, and redaction.
4. Advanced PDF organization, richer metadata editing, exact-size compression, and native Windows sharing.

These items remain part of the user's requested broader target. The first release must be described as an initial Windows counterpart, not as verified complete parity with all 275 conversions and 26 tools. Its six actions retain clipboard Copy and add Reveal in Explorer; AirDrop has no literal Windows equivalent, and native Windows sharing is a later addition.

## Interaction

### Resident app

Citrus runs once per user session as a normal user. Its tray menu opens the main window or settings, pauses gesture monitoring, and quits cleanly. First launch shows an illustrated gesture guide and a file picker. Closing the main window leaves the tray utility running; Quit stops monitoring and conversion workers.

Settings include launch at sign-in, light/dark/system theme, whether to reveal completed outputs, and gesture enablement. Launch at sign-in is off until the user enables it. A keyboard-accessible file picker provides the same conversions without the drag gesture.

### Gesture and wheel

During a file drag, tapping Shift requests the conversion wheel, Alt requests tools, and Control requests actions. The most recently pressed mode key wins; releasing a key does not dismiss the wheel. Mode keys can be disabled or remapped in settings because they also have ordinary Explorer meanings.

The input monitor observes only the necessary mouse and modifier states and never suppresses ordinary input. A held left button, movement beyond the system drag threshold, and a mode-key transition are candidate activation evidence. If a mode key was already held when dragging began, crossing the threshold also requests activation. They do not establish that filenames are available. When simultaneous transitions have the same timestamp, Control takes precedence over Alt, then Shift. Users can pause or remap Citrus gestures when they want the usual modifier-assisted Explorer move/copy behavior.

Citrus shows a small, nonactivating, hit-testable capture window near the pointer. Windows OLE drag events must confirm a real file payload before actionable formats appear. Unconfirmed capture surfaces time out after 750 milliseconds. Text drags, window drags, virtual files, and unsupported payloads do not enqueue jobs. Initial support covers physical paths from Explorer and the desktop. Folders and selections containing folders offer actions only; conversions and tools accept files. Cloud placeholders marked offline or requiring recall are rejected without hydration.

The wheel stays anchored after activation. Its bounds fit inside the current monitor's work area with per-monitor DPI conversion and support for negative monitor coordinates. Transparent visual margins must not make the capture area click-through. Up to eight choices appear per page, with a source/count badge and a neutral center. Hovering over a previous/next target for 400 milliseconds switches pages without releasing the drag; releasing on a navigation target cancels rather than starting a job.

Releasing over an enabled format or tool accepts that operation. Releasing over the neutral center or outside the active targets cancels. Escape cancels. Leaving or canceling an unaccepted drag produces no files. The target reports Copy for conversions and tools, only when Copy is permitted by the drag source, regardless of Shift's usual Move meaning.

Tools that need parameters open a modal settings dialog after the drop, before a job starts. Resize preserves aspect ratio by default and requires positive dimensions; quality accepts 1–100; trims require nonnegative start and a later end within the media duration; speed accepts 0.25–4 times; normalization defaults to -16 LUFS. Invalid values prevent submission. Canceling this dialog creates no files. Plain format conversion uses documented presets and needs no additional dialog.

Clipboard Copy places physical file references on the clipboard with Copy semantics so the user can paste in Explorer. Move opens a destination picker and is explicitly labeled as moving the original; cancellation does nothing and existing destinations are never replaced. The floating shelf holds references to files and supports dragging them out; it does not create duplicate storage.

### Jobs and results

The compact jobs window shows source, operation, progress, result, cancellation, and a Reveal action. Work runs outside the UI thread, with at most two active conversions by default. Indeterminate progress is honest when a backend has no meaningful percentage. A failed item does not cancel unrelated batch items.

PDF merging, image-to-PDF combination, and multi-file ZIP/TAR creation show an ordered input list before submission. Preserve the incoming file-drop order and offer Move up/down controls. Use the first item's directory by default; inputs from different directories require an explicit destination choice. Group outputs use the first input's stem plus an operation suffix. A group fails without a final output if any required input fails. GZIP creation accepts one file per output.

The main window uses ordinary Windows focus navigation, accessible names, visible focus, and text labels. Color is not the sole indicator of selection, progress, or errors. Reduced-motion and high-contrast settings must not prevent use.

## Architecture

Use .NET 10 LTS and WPF, with narrow Win32 interop for input observation, nonactivating overlay placement, monitor information, and tray integration. WPF owns the STA message loop and OLE drag data; workers receive plain paths and operation parameters, never live COM drag objects.

| Component | Responsibility |
| --- | --- |
| Citrus.Core | Operation catalogue, capability intersection, job states, output naming, validation, and engine interfaces; independent of WPF. |
| Citrus.Windows | Tray lifecycle, gesture state machine, capture surface, radial selection, settings, file dialogs, floating shelf, and job presentation. |
| Citrus.Conversion | Image, media, PDF, subtitle, and archive engines implementing the core interfaces. |
| Packaging | Pinned backend acquisition, checksums, notices, self-contained Windows publishing, NSIS installation, and Windows CI. |

Use Magick.NET for image operations, a pinned FFmpeg/FFprobe pair for audio/video, and local PDF libraries for creation, structural operations, rendering, and text extraction. PDF rendering must use an explicitly bundled backend; ImageMagick must not silently depend on an absent Ghostscript installation. Use .NET archive APIs and an explicitly licensed RAR reader as needed.

Backend subprocesses use installed absolute paths and structured argument lists without a command shell. Probe the shipped binaries' capabilities, constrain supported operations to that catalogue, capture bounded diagnostic output, and terminate the entire process tree on cancellation. No fallback uploads are permitted.

WPF is preferred over Tauri or Electron because the central integration depends on Windows drop targets regardless of the rendering layer. WPF avoids a separate browser runtime and native web bridge. The capture approach is an engineering design that requires a real Explorer test; hooks alone cannot identify dragged files. [WPF drag and drop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop), [OLE drop target registration](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop), and [layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows).

## Data and file safety

Conversions and tools preserve original file contents and location. Choose the original stem with the target extension; if that path exists, append a numerical suffix. Same-format edits use an operation suffix before collision numbering. Claim final names through a non-replacing commit and retry with the next suffix if another job wins the name.

Write to a unique temporary sibling, close and validate the result, then move it into its final name without replacement. On failure or cancellation, delete the temporary output. Archive extraction uses a fresh directory, validates every resolved entry path, rejects links, and stops before exceeding 100,000 entries or 20 GiB of expanded data. These extraction limits are shown when an archive exceeds them.

If the source directory is not writable, report the reason and let the user select a destination. Do not elevate the app to write there. Locked sources, unsupported/corrupt inputs, missing files, and unavailable codecs receive actionable per-item errors.

Only local settings are persisted. Job history is session-only by default. Diagnostics must not record file contents or full paths unless the user explicitly exports a report. Completed outputs are user files and are never removed by uninstallation.

## Packaging and delivery

Publish a self-contained win-x64 build, bundling the selected codecs and their required notices. Produce a per-user NSIS installer with Start menu entry, optional desktop shortcut, and uninstaller; no administrator rights or separate .NET installation are required. Pin backend downloads and verify checksums during packaging. Include the source and license information required by the exact FFmpeg build. [FFmpeg distribution guidance](https://ffmpeg.org/legal.html).

The Linux workspace can cross-compile a Windows candidate using EnableWindowsTargeting and can compile NSIS installers. A Windows CI build and real Windows smoke test remain release requirements. Signing requires a publisher identity and certificate and is outside the initial unsigned candidate. [Windows targeting](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#enablewindowstargeting) and [NSIS platforms](https://nsis.sourceforge.io/Docs/Chapter2.html).

## Verification and acceptance

Start with the highest-risk integration: an Explorer drag opens the real capture wheel, dropping on one image format creates a valid copy, and canceling preserves the source without output. If this cannot be demonstrated, keep the installer labeled a candidate and report the unresolved integration honestly.

Automated checks cover format compatibility, collision-safe output commits, cancellation cleanup, subtitle timestamps, archive traversal rejection, representative conversion fixtures, and batch partial failure. Build the WPF application and installer, and provide a Windows CI workflow for the same checks. Tests should validate behavior and actual output files rather than duplicate implementation details.

Windows acceptance covers Explorer and desktop drags; Shift before/during drag; tap-and-latch mode switching; normal file moves with gestures paused and normal text selection; cancel and unsupported payloads; all supported output formats; multiple files and grouped-operation ordering; tool-dialog cancellation; read-only folders; spaces and Unicode paths; repeated output names; 100/150/200 percent DPI; screen corners; multiple monitors; standard-user install/uninstall; startup settings; and offline operation on a clean machine.

Delivery consists of repository source, an installer candidate, a short usage guide, a precise enabled-format/tool list, automated verification results, and the status of the Windows acceptance checks. Compilation on Linux must never be presented as proof of successful Explorer interaction.
