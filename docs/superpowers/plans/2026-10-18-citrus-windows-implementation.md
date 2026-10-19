# Citrus Windows implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved first release of Citrus as an installable Windows file converter with an Explorer drag wheel and offline engines.

**Architecture:** A WPF tray process owns the native capture surface and presentation. A platform-independent core defines operations, safe output commits, and jobs; local conversion adapters implement those contracts. Native interaction, conversion families, and packaging are separate reviewable work streams, integrated through the interfaces below.

**Tech Stack:** .NET SDK 10.0.402, WPF, xUnit, Magick.NET Q8 x64, an owned LGPL FFmpeg/FFprobe build, PDFsharp 6.5.0, PDFtoImage 5.4.0, PdfPig 0.1.16, SharpCompress 0.50.5, NSIS. Resolve and pin the stable Magick.NET package in Task 2; commit package lock files and the exact media source/build manifest.

**Spec:** [Citrus Windows file converter design](../specs/2026-10-18-citrus-windows-design.md).

## Global Constraints

- The first release targets Windows 10 22H2 and Windows 11, x64.
- Use .NET 10 LTS and WPF, with narrow Win32 interop.
- Releasing a key does not dismiss the wheel.
- Unconfirmed capture surfaces time out after 750 milliseconds.
- Up to eight choices appear per page; previous/next targets switch pages after 400 milliseconds of hover.
- Conversions and tools preserve original file contents and location.
- Work runs outside the UI thread, with at most two active conversions by default.
- Quality accepts 1–100; speed accepts 0.25–4 times; normalization defaults to -16 LUFS.
- Archive extraction stops before exceeding 100,000 entries or 20 GiB of expanded data.
- Launch at sign-in is off until the user enables it.
- No accounts, analytics, advertising, uploads, or runtime engine downloads.
- Publish a self-contained win-x64 build and a per-user NSIS installer.
- Compilation on Linux must never be presented as proof of successful Explorer interaction.

## Review Focus

- Concurrent same-name jobs must preserve all existing files and produce distinct complete outputs; Task 1 pins this.
- Paths with Unicode, quotes, spaces, or leading dashes must survive media invocation without shell interpretation; Task 3 pins this.
- Folder selections, offline cloud placeholders, and non-file drags must not become conversion jobs; Task 2 pins this.
- Archive entries disguised as links or using Windows-style traversal must never escape the fresh extraction directory; Task 5 pins this.
- Canceling parameter dialogs or grouped jobs must leave originals intact and no final group output; Tasks 4 and 6 pin this.

## File structure and shared contracts

Create `Citrus.sln`, `global.json`, `Directory.Build.props`, `.gitignore`, and the following projects:

| Path | Responsibility |
| --- | --- |
| `src/Citrus.Core/` | `Models.cs`, `OperationCatalog.cs`, `OutputWriter.cs`, `JobQueue.cs`, and pure gesture/wheel geometry models. |
| `src/Citrus.Conversion/` | `ImageEngine.cs`, `MediaEngine.cs`, `MediaProcess.cs`, `PdfEngine.cs`, `SubtitleEngine.cs`, `ArchiveEngine.cs`, and `EngineRegistry.cs`. |
| `src/Citrus.Windows/` | `App.xaml`, `App.xaml.cs`, `MainWindow`, `WheelWindow`, `ToolOptionsWindow`, `SettingsWindow`, `ShelfWindow`, `NativeMethods`, `GestureMonitor`, `FileActions`, and resources. |
| `tests/Citrus.Core.Tests/` | Core behavior, output safety, queue, geometry, and gesture state tests. |
| `tests/Citrus.Conversion.Tests/` | Small generated fixtures and real engine output checks. |
| `tests/Citrus.Windows.Tests/` | Windows-only integration tests for tray lifecycle, drop validation, settings, and actions. |
| `packaging/` | Media lock manifest, acquisition script, publish script, NSIS definition, notices, and source distribution metadata. |
| `docs/` | Usage, enabled operations, manual Windows acceptance, and third-party information. |

Keep the public contracts in `Citrus.Core/Models.cs`:

- `FileKind`: `Unknown`, `Directory`, `Image`, `Audio`, `Video`, `Pdf`, `Subtitle`, `Archive`.
- `OperationMode`: `Convert`, `Tools`, `Actions`.
- `OperationDescriptor(string Id, string Label, OperationMode Mode, string? OutputExtension, bool IsGroup, bool NeedsOptions)`.
- `OperationOptions`: optional `Width`/`Height`, `Quality = 85`, `RotationDegrees = 90`, `StartSeconds = 0`, optional `EndSeconds`, `Speed = 1`, `NormalizeLufs = -16`, optional `PageSelection`, and `PreserveAspectRatio = true`.
- `OperationRequest(OperationDescriptor Operation, IReadOnlyList<string> Inputs, OperationOptions Options, string? DestinationDirectory)`.
- `ConversionProgress(double? Fraction, string Message)` and `ConversionResult(IReadOnlyList<string> Outputs)`.
- `IConversionEngine` exposes `IReadOnlyList<OperationDescriptor> Operations`, `bool CanExecute(OperationRequest request)`, and `Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)`.
- `OutputWriter.WriteFileAsync(string desiredPath, Func<string, CancellationToken, Task> writeTemporary, CancellationToken cancellationToken)` returns `Task<string>` with the final collision-safe path; add the equivalent `WriteDirectoryAsync` for archive extraction.
- `OperationCatalog(IEnumerable<IConversionEngine> engines)` exposes `IReadOnlyList<OperationDescriptor> GetOperations(IReadOnlyList<string> paths, OperationMode mode)` as the intersection of registered engines' supported operations, with explicit group operations where applicable.

Operation IDs use stable strings: `image.convert.png`, `image.resize`, `media.convert.mp4`, `video.trim`, `audio.normalize`, `pdf.merge`, `pdf.split`, `pdf.images`, `pdf.text`, `images.pdf`, `subtitle.vtt`, `subtitle.srt`, `subtitle.txt`, `archive.zip`, `archive.tar`, `archive.gzip`, and `archive.extract`. Other formats follow the same prefix conventions.

## Task 1 Establish safe conversion contracts

**Files:** Core project, solution/build configuration, `tests/Citrus.Core.Tests/OutputWriterTests.cs`, `CatalogTests.cs`, and `JobQueueTests.cs`.

**Interfaces:** Produces the shared contracts above, `JobQueue(IEnumerable<IConversionEngine> engines, int maxConcurrency = 2)`, and `ConversionJob Enqueue(OperationRequest request)`. `ConversionJob` exposes request, state, progress, error, outputs, and `Cancel()`; `JobQueue.JobChanged` notifies presentation, which marshals updates to its dispatcher. Engines consume an injected `OutputWriter`.

- [ ] Write failing tests: two simultaneous writes targeting `photo.png` produce distinct outputs; existing `photo.png` bytes stay identical; a canceled/throwing writer leaves no final or temporary output; an unwritable destination reports failure; a third job waits while two are active.
- [ ] Install the pinned SDK outside the repository, scaffold only the projects required by this task, and run `dotnet test tests/Citrus.Core.Tests`; confirm failures concern the missing behavior.
- [ ] Implement collision handling by unique sibling staging and non-replacing final moves with suffix retries. Preserve the requested extension on staging files. Implement queue cancellation and per-item failure states without blocking the UI.
- [ ] Run `dotnet test tests/Citrus.Core.Tests -c Release`; require all tests to pass and no leaked staging files.
- [ ] Commit the core deliverable and locked dependencies.

## Task 2 Prove the native wheel with real image conversion

**Files:** `ImageEngine.cs`, Windows app/bootstrap, `GestureMonitor.cs`, `NativeMethods.cs`, `WheelWindow.xaml`/`.cs`, core gesture/geometry models, `ImageEngineTests.cs`, and `GestureTests.cs`.

**Interfaces:** Consumes Task 1 contracts. Produces `GestureMonitor.ModeRequested`, `WheelWindow.ShowAtPointer(OperationMode mode)`, `WheelWindow.OperationDropped`, and image operations discovered by `OperationCatalog`.

- [ ] Write failing tests for image PNG/JPEG/WebP round trips, resize dimensions and metadata removal; gesture pre-held/tapped mode behavior and latching; 750 ms unconfirmed timeout; wheel bounds on negative-coordinate/mixed-DPI monitors; directory/unsupported/offline payload rejection for conversions.
- [ ] Run the targeted core/conversion tests and verify they fail before implementation.
- [ ] Pin a stable Magick.NET Q8 x64 package and implement image conversion, rotation, resize, quality, and metadata stripping through `OutputWriter`. Reject document/vector inputs that require unavailable external delegates; enable SVG only after a bundled renderer fixture passes.
- [ ] Implement a non-suppressing modifier/mouse state observer, a nonactivating hit-testable WPF capture surface, and real `FileDrop` handlers on the STA dispatcher. Extract plain paths there. Return Copy for conversions, cancel center/outside drops, and implement eight choices/page with 400 ms hover navigation.
- [ ] Run `dotnet build src/Citrus.Windows/Citrus.Windows.csproj -c Release -p:EnableWindowsTargeting=true`. Run automated tests; if a Windows session exists, drag an Explorer image through the wheel and inspect the real output. Otherwise record that central acceptance as pending and keep the build labeled candidate.
- [ ] Commit the first runnable Windows app. Do not claim Explorer acceptance from geometry tests or a successful cross-build.

## Task 3 Add real offline media processing

**Files:** `MediaProcess.cs`, `MediaEngine.cs`, `MediaEngineTests.cs`, `packaging/media-lock.json`, `packaging/build-media.sh`, and third-party source/license metadata.

**Interfaces:** Consumes core requests/output writer. Produces `MediaProcess.RunAsync(IReadOnlyList<string> arguments, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)` and capability discovery from the exact shipped FFmpeg binary.

- [ ] Write failing tests using a generated tone and short test-pattern video: format conversion, trim duration, resize dimensions, 0.25/4-times speed boundaries, audio normalization, cancellation, and paths containing spaces, Unicode, quotes, or leading dashes.
- [ ] Run tests against the available host FFmpeg for the subprocess/engine behavior, failing for missing adapters; keep Windows-binary smoke tests separate.
- [ ] Build Windows x64 FFmpeg/FFprobe with MinGW from these source commits: FFmpeg n9.0.2 `946fcce07b6dcd0331c8cc609192aeff5e1924f8`; Opus v1.5.2 `ddbe48383984d56acd9e1ab6a090c54ca6b735a6`; Vorbis v1.3.7 `0657aee69dec8508a0011f47f3b69d7538e9d262`; Ogg v1.3.6 `be05b13e98b048f0b5a0f5fa8ce514d56db5f822`; libvpx v1.15.2 `d168454ecd099805c675d4a98c66f4891373302a`; LAME `1f5cc9487284d5950343aa5d4f70de433468070a`. Pin source archive SHA256 values in the manifest. Build shared FFmpeg libraries with GPL, nonfree, network, autodetection, and ffplay disabled; build a shared LAME encoder library with decoder/frontend disabled. Disable unused Opus DRED/deep-PLC bootstrap downloads. Include actual source trees, patches, build commands, configuration and toolchain versions as a compressed source archive in the installer; do not rely on a future source offer or a generic repository link.
- [ ] Use `mpeg4` plus AAC for MP4/MOV/MKV presets, `libvpx-vp9` plus Opus for WebM, and the explicit LAME/Opus/Vorbis/native PCM/FLAC/WMAv2 encoders for audio. A Windows Media Foundation H.264 preset is enabled only after a real encode probe succeeds; software H.264 is not assumed. Preserve FFmpeg's network protocol restrictions for local-only inputs.
- [ ] Implement absolute-path invocation with `ProcessStartInfo.ArgumentList`, bounded stderr, machine-readable progress, ffprobe duration, and process-tree termination. Use the actual encoder catalogue to choose presets and expose only supported image/audio/video routes.
- [ ] Run engine tests and inspect outputs with ffprobe. Verify Windows assets are included at publish time and unavailable binaries produce a clear error rather than a download attempt.
- [ ] Commit media adapters, tests, and the distribution manifest.

## Task 4 Add PDF creation and structural operations

**Files:** `PdfEngine.cs`, `PdfPageSelection.cs`, `PdfEngineTests.cs`, and PDF package references.

**Interfaces:** Consumes group requests for `images.pdf`/`pdf.merge`, and single-input `pdf.split`, `pdf.images`, and `pdf.text`. Returns all resulting output paths through `ConversionResult`.

- [ ] Write failing tests: ordered images produce the expected PDF page count and page sizes; merge preserves order; selected-page split validates one-based ranges; text export contains known text; raster export returns one image/page; malformed/encrypted input fails without a final group result; canceled group work cleans up its outputs.
- [ ] Run the PDF tests and verify the expected missing behavior.
- [ ] Use PDFsharp 6.5.0 Core for creation/import, PDFtoImage 5.4.0 for 300-DPI rendering, and PdfPig 0.1.16 for text. Serialize PDFium render calls with a semaphore while allowing other job families to use the second queue slot. Include native PDFium/Skia runtime files and notices in publishing.
- [ ] Use ordered group inputs and Task 1 output staging. Clean up outputs owned by a failed multi-output/group request without touching pre-existing files. Validate page selection before starting output.
- [ ] Run PDF fixtures on the host when supported and in Windows CI; verify a clean publish directory contains the required rendering libraries.
- [ ] Commit the PDF deliverable.

## Task 5 Add subtitles and bounded archive processing

**Files:** `SubtitleEngine.cs`, `ArchiveEngine.cs`, `SafeArchivePath.cs`, `SubtitleEngineTests.cs`, and `ArchiveEngineTests.cs`.

**Interfaces:** Consumes the shared engine interface and output writer. Produces subtitle conversion and archive create/extract operations, including grouped ZIP/TAR and single-file GZIP.

- [ ] Write failing subtitle tests for BOM/CRLF, multiline cues, hours above 24, comma/dot milliseconds, cue settings, malformed timestamps, and text-only export. Write archive tests for ZIP/TAR/GZIP round trips, RAR fixtures, `../` and backslash traversal, drive/UNC paths, duplicate entries, symlinks/hard links/reparse entries, cancellation, and actual expanded-byte/entry limits.
- [ ] Run these tests and confirm missing implementations fail.
- [ ] Implement timestamp-aware parsing rather than blind delimiter replacement. Preserve cue text and compatible settings; report malformed cues clearly.
- [ ] Use `ZipArchive`, `System.Formats.Tar`, `GZipStream`, and SharpCompress 0.50.5 for RAR. Extract entries individually into a fresh staged directory. Inspect ZIP Unix mode bits and RAR redirection/Unix attributes, because generic link properties can be empty. Reject links and escaped resolved paths before opening outputs; count bytes while copying, stopping before 20 GiB or 100,000 entries.
- [ ] Run subtitle/archive tests and verify rejected archives leave no final extraction directory and no files outside staging.
- [ ] Commit the subtitle/archive deliverable.

## Task 6 Integrate jobs and complete the conversion interface

**Files:** `EngineRegistry.cs`, `MainWindow.xaml`/`.cs`, `ToolOptionsWindow.xaml`/`.cs`, `JobViewModel.cs`, `BatchRequestBuilder.cs`, and corresponding core/Windows tests.

**Interfaces:** `EngineRegistry.Create(OutputWriter writer, string toolsDirectory)` produces the engines consumed by the catalogue/queue. `BatchRequestBuilder.Build(...)` turns a selection and operation into individual or grouped `OperationRequest` objects.

- [ ] Write failing tests for compatible-operation intersection, partial batch success, ordered groups, destination prompting for mixed-directory groups, parameter bounds, and canceling the options/group dialogs without enqueueing work.
- [ ] Run the targeted tests and confirm failures.
- [ ] Build the compact Citrus main/jobs window with original vector artwork, drop area, file picker, contextual operations, source/output names, honest progress, cancellation, retry, and Reveal. Connect wheel drops to the same path.
- [ ] Implement parameter controls and validation for resize/quality/trim/speed/normalization/page selection. Group dialogs preserve drop order with Move up/down; default to the first directory and require a destination for mixed-directory inputs. Surface destination failures with a Choose folder action.
- [ ] Run all core/conversion tests, Windows UI tests where available, and cross-build the complete WPF app. Inspect every visible control for a real connected action and document disabled unsupported operations.
- [ ] Commit the integrated interface.

## Task 7 Add native actions and resident settings

**Files:** `FileActions.cs`, `ShelfWindow.xaml`/`.cs`, `SettingsWindow.xaml`/`.cs`, `SettingsStore.cs`, app lifecycle/tray resources, and Windows tests.

**Interfaces:** `FileActions.ExecuteAsync(string actionId, IReadOnlyList<string> paths)` implements `action.copy`, `action.move`, `action.paths`, `action.reveal`, `action.terminal`, and `action.float`. Settings persist under the user's local application-data directory.

- [ ] Write failing Windows tests for clipboard file-drop Copy semantics, collision-safe Move cancellation, path clipboard text, terminal/reveal arguments, shelf reference removal when a file disappears, settings defaults, and single-instance lifecycle.
- [ ] Run the Windows tests on a Windows host; otherwise record them as pending rather than substitute passing mocks for OS behavior.
- [ ] Implement actions, physical-file/folder validation, and a floating shelf that supports outbound OLE drags. Move requires an explicit destination and does not overwrite. Open Terminal at the containing directory using structured arguments and a documented installed-shell fallback.
- [ ] Add tray Open/Pause/Settings/Quit, single-instance activation, clean hook/process disposal, theme selection, reveal preference, gesture remapping, and opt-in sign-in launch. Honor high contrast/reduced motion; provide focus labels and keyboard navigation throughout.
- [ ] Build and run lifecycle/settings tests; manually inspect screen-reader names and focus order in Windows acceptance.
- [ ] Commit the native-actions deliverable.

## Task 8 Package and review the installer candidate

**Files:** `packaging/publish.ps1`, `packaging/publish.sh`, `packaging/Citrus.nsi`, `.github/workflows/windows.yml`, `README.md`, `docs/operations.md`, `docs/windows-acceptance.md`, and `THIRD-PARTY-NOTICES.md`.

**Interfaces:** Publishing produces `dist/Citrus-win-x64/`, `dist/Citrus-Setup.exe`, source/license artifacts required by bundled dependencies, and a checksum manifest.

- [ ] Add a package-content check that fails when the app, native codecs, FFmpeg/FFprobe, licenses, or required source distribution material is absent. Verify it fails against an incomplete publish directory.
- [ ] Publish with `dotnet publish src/Citrus.Windows/Citrus.Windows.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true`; preserve native dependency files rather than assume single-file embedding supports them all.
- [ ] Compile a per-user NSIS installer on the available host, with Start menu entry, optional desktop shortcut, uninstaller, no elevation, and no automatic sign-in launch. Ensure uninstall never deletes user-converted files.
- [ ] Add Windows CI for restore, core/conversion/Windows tests, self-contained publish, asset verification, and installer creation. Test Windows codecs through the actual bundled tools, not host substitutes.
- [ ] Run all available automated checks once, inspect installer metadata/checksums, review the whole branch with a fresh reviewer, and fix actionable issues. Execute the manual Windows acceptance checklist if a Windows session exists; record unchecked items clearly if it does not.
- [ ] Document exact enabled operations, installation, gesture conflicts/pause, source preservation, candidate/signing status, and remaining Windows checks. Commit and deliver artifact links with verification results; do not imply full Tangerine feature parity.

## Execution review

The tasks above cover the approved first release. Office conversion and advanced visual editors remain the explicit later scope in the design. The central native drag acceptance is a Windows release gate even if all host tests and cross-compilation pass.

Recommended execution is subagent-driven: the engine families have separate files and test fixtures, while fresh reviews can check their shared output-safety and job contracts before packaging. A native execution path can use the same sequence with a whole-branch review at the end.

The native workflow, conversion families, and installer can also be split into separate execution plans if the user prefers smaller delivery milestones. This plan keeps them together because the approved deliverable is a single installable app, while each task still has its own verification and review gate.
