# Reference implementation coverage

> Public text summary. Raw machine receipts and user reference captures are retained privately. See [preview notes](PREVIEW-NOTES.md) for current limits; test sources are included in the repository.

Updated: 2026-10-21. Contract: approved plan (machine-local evidence retained privately).

The later compact-editor and 29-image follow-up audit (machine-local evidence retained privately) records the smaller wheel/windows, visual video editors, current per-run evidence and remaining performance/native gates. The results below remain historical evidence for their stated scopes.

The requested routes and controls are implemented. **Full acceptance remains open:** automated output checks, WPF window checks, physical Explorer workflows, native visual review and performance qualification are separate requirements. A registered operation or a passing backend test does not establish matching menus or complete state coverage.

The linked inventories contain [83 reference wheel entries](reference-wheel-inventory.json) and [81 unique operations](reference-operation-verification.md). `ReferenceCoverageTests` checks registration, editor routing and honest Pages-compatible DOCX labeling. It does not execute every native workflow. PAGES entries resolve to `.docx`; no `.pages` extension is fabricated. Additional established conversions remain available.

## Implementation and evidence

| Area | Implemented behavior | Current automated evidence | Remaining acceptance |
|---|---|---|---|
| Shared white menus | Opaque white surfaces in both themes, dark text, shared rounded fields/buttons, centered titles, right close, fixed action footers | Chrome/white-surface checks, actual XAML template title size check; 27 window cases captured at default/minimum sizes and three raster scales | Per-window human comparison, physical DPI, keyboard and complete state matrix |
| Wheel and routing | Nine visible targets where appropriate, reference ordering, typed editor routing; accepted petals/backing/pagination composition retained | Geometry, menu ordering, host routing, prepared-target lifecycle and controlled native click/suppression checks | Actual Explorer modifier-first drag, outside release, scrolling, multiselection, sound and OLE cancel/reopen |
| Image editing | Whites/Blacks, signed Gamma, other requested adjustments, per-control reset, Original comparison, Fit and physical-pixel 100% view | Image reference/control/preview tests; native 100% and Fit workflow; saved crop and annotation checks | Full edited/invalid/canceled/failed/successful native state review |
| Crop / Add BG | Eight crop handles, presets and dimensions; gradient/solid/custom backgrounds, pixel padding/corners/shadow and ratios | Geometry, source-context and export checks; five transition/reset/reload and 34 image-host checks; rebuilt native Adjust→Background transition, settled controls and Save with Background→reopened output pass | Other states/sources, broader pointer/proportion comparisons and physical DPI matrix |
| Image compression / metadata / HEIC | Measured byte goals, quality mode, safe failure for unreachable goals; grouped metadata search/edit/remove; genuine HEVC/HEIC support | Independent decode/readback, alpha preservation, UTF-8 ExifTool edits, source/pixel preservation and native metadata save/cancel checks | Clean-machine engine-pack qualification and complete native states |
| Video / audio | Tool-specific editors, actual playback/scrubbing, full-duration timeline, processed audition, crop/trim/masks, channels, bleeps, normalization, snapshots, split and visualization | Decoded-sample/export checks, stream/chapter metadata readback, 15 media window cases, source-switch hydration and long-media navigation | Native playback/audio audition, all controls/states, physical DPI and source diversity |
| PDF | Thumbnails, selection/order/rotation/duplicates/removal, split/export, metadata and measured compression; editable Office reconstruction with OCR | Independent page/document reopening, OOXML schema/cell checks, native organizer and metadata exports, source mutation and rollback tests | Native state coverage, varied real documents/scans, reconstruction quality review |
| Document / presentation / spreadsheet / EPUB | Offline conversions using pinned Office/Pandoc pack; real cells and all delimited sheets; Pages-compatible DOCX | Requested Writer/EPUB destinations and Calc/Impress destinations have reopened output checks; macro/rollback tests; 19,424 pinned runtime files verified; relocated published-pack DOCX→PDF smoke passes | Clean-machine execution, SDK redistribution-license review and full native state coverage |
| Archive / subtitle | Archive-member repack, compound `.tar.gz` extraction, path/link/budget guards; existing subtitle conversion retained | Archive repack checks and existing subtitle suite | Complete native states and representative real archives/subtitles |

## Control-by-control scope

These are implemented controls, not a declaration that every state has passed native acceptance.

| Screen | Identifiable requested controls |
|---|---|
| Edit Photo | Exposure, Brightness, Contrast, Highlights, Shadows, Whites, Blacks, Gamma, Temperature, Tint; values and individual resets; zoom −/+, 100%, Fit, Reset and labeled Original comparison. Gamma offset zero maps to neutral gamma one. |
| Crop Image | Movable selection, eight handles, Free/1:1/16:9/9:16/4:3/3:4, editable W/H pixels, Reset, Apply. |
| Add BG | Gradient/solid swatches, selection outline, horizontal overflow, custom image/color, Padding/Corners/Shadow pixels, Auto/1:1/16:9/4:5, Reset, Save with Background. |
| Compress | Filename, original bytes, Percentage/reduction/value/target explanation; Quality and Target Size authored additions; output bytes verified rather than assumed. PDF Quality explains and requires explicit page-image rebuilding because it removes editable content; it cannot silently act as a no-op. |
| Metadata | Filename, search, grouped supported edits/removal, read-only technical facts, Remove All, warning and Save. PDF drafts/removal restore and technical facts follow the selected source. |
| Trim Audio | Waveform handles, duration, hundredth-second start/end, silence trimming, Play Selected Range, Apply. |
| Channels | Separate output waveforms and audition, combined playback, Mono/Stereo, per-channel gains/readouts, Apply. |
| Bleep | Bleep/Audio lanes, draggable ranges, count, explicit range selection/removal, Original/With Bleeps, zero-range explanation and disabled Save Bleeped Copy. |
| Video | Source playback and scrubbing, direct crop/trim/masks, tool-specific speed/snapshot/split controls; full source preview retained while crop boundaries change. |
| PDF | Thumbnails, selection, move/rotate/duplicate/remove, undo/redo, split/page export, compression and searchable descriptive metadata. |

Closed dropdowns, covered controls and cropped panels cannot establish unseen reference behavior. Quality/Target Size compression, Fit, missing error/loading states, reconstruction warnings and Windows-safe responsive layouts are authored behavior. OCR and PDF reconstruction recover editable content where possible; they are not a guarantee of exact source layout or universal table recognition. Native Windows dialogs retain platform rendering.

## Verification status

The durable evidence index (machine-local evidence retained privately) contains logs, timing reports and **162 WPF PNGs with 162 metadata sidecars**. The raster collection covers six image editors, fifteen media editors, four PDF views and image metadata in both application themes, each at default/minimum size and 1.0/1.5/2.0 raster scale. Raster scale is **not** a physical monitor DPI change; native video composition, DWM glass and desktop content are outside these captures.

Three additional actual native JPEGs (machine-local evidence retained privately) and scope sidecars record the rebuilt app's corrected Add BG transition, settled preview, lower controls and successful job window. A saved native-workflow output PNG and independent-decode sidecar also preserve the successful export. These qualify one Add BG happy path at the current desktop scale, including its 560 × 900 editor window, not the physical 100%/150%/200% matrix or every window/state.

- Core run: **234 passed**.
- Broad Windows baseline: **709 passed, 43 failed, 12 skipped**. Targeted followups corrected obsolete expectations and discovered image/PDF/media defects; native clipboard/foreground/capture and symlink-privilege cases remain unqualified. This is not a green full-suite claim.
- Latest broad Windows run under an explicit native-guard filter: **766 passed, 13 skipped, 1 failed / 780**. Its `WheelBackdropLifecycleTests.WindowFromPoint_resolves_admitted_wheel_targets_inside_entire_clip` failure and resident-restart retries are preserved. Test-only diagnostic identity metadata later proved both rejecting HWNDs belonged to `CodexComputerUseCursorOverlay` in the computer-use helper (PID 45660). After ending the agent's own UI-inspection session through documented `node_repl` reset, the unchanged native case passed **1/1** with preflight `EntireCropOwnedBeforeCapture`. No production behavior, guard or quality change obtained the pass. Diagnostic metadata tests passed **20/20** and code review approved the test-only change. This supersedes that specific failure; it does not rerun the entire broad suite or qualify filtered/skipped capabilities.
- Changed Windows batch: **174 passed, 2 failed** on WPF floating layout precision. The subsequent native 100%/Fit and PDF followup passed **14/14** with a subpixel layout tolerance; source/output pixel checks remain exact. The latest focused host run passed **18/18**: fourteen PDF cases, three shared-title cases and an actual XAML title-template regression. It covers selected-source metadata, filename display and invalid Quality without page-image rebuilding.
- Final non-Office conversion batch: **568 passed, 3 skipped, 0 failed**. This deliberately excludes the Office feature matrix, active-content, worker-isolation and document-conversion families while the Office runtime is exercised separately, avoiding cross-process ACL lease contention. The skips require unavailable symlink privileges. It supersedes the earlier 554-pass/7-failure conversion baseline only within that tested scope.
- Media output followup: **22 passed**. PDF typed/split/rollback/reading-order followup: **7 passed**. Calc ODS/CSV/TSV: **3 passed**; actual PPTX to PDF/PNG/JPG: **3 passed**. Prior Writer/EPUB and Calc PDF successes remain bounded to their recorded fixtures.
- Controlled native cancellation run: **3 passed**. A claimed right-button down/up delivers neither right-button messages nor a context menu, the next ordinary pair works, an unknown-source canceled target releases normal left-click access, and retained source identity requires independent completion evidence. These use owned windows/controlled admission, **not an Explorer/OLE drag**.
- Final isolated Office policy/rollback run: **2 passed**. The active-content positive-control/production-policy test passed again in a separate repeat; this proves that fixture's executable macro is disabled in production, not universal active-content safety. Pack verification checked **19,424 runtime files and all pinned downloads/sources**. After the final background repair and source-archive refresh, the candidate was republished and positive package verification passed for **21,573 owned payload files**, including app-local CRT dependencies and sources/notices. Fresh negative-package validation passed **17 with 1 host-symlink skip / 18**, exercising a valid baseline, 34 asset-class omissions and 20 CRT-copy omissions. An explicitly selected relocated published feature pack converted a fresh DOCX to a reopened PDF while preserving its source. This is not clean-machine execution; SDK redistribution-license review remains pending. Complete default/loading/edited/invalid/processing/canceled/failed/successful native coverage is pending for every unique operation.
- Limited actual app review covered a 22-pixel Edit Photo title, white Photo/Crop surfaces and dropdown, eight crop handles and actual crop dragging, with cancel preserving the original. It found a Crop→Add BG initialization defect; the corrected entry/reset/reload behavior passes **5 native-window regressions** and **34 broader image-host checks**. Those initial screen observations were inline. After rebuilding, actual Adjust→Background review passed: the settled preview shows the gradient/padding, rounded photo and shadow; white shell, centered title, right close, swatches/custom image/color, pixel values, ratios and fixed Save with Background footer are visible. Two native JPEGs preserve that bounded review. **Save with Background** then produced one successful job and a collision-safe PNG, independently fully loaded through Pillow: 1672 × 941 source became the expected 1992 × 1261 (+160 px per edge), gradient samples vary and the original SHA-256 is unchanged. The output and decode sidecar are archived. Other per-window/reference and all-state acceptance remain open.
- The final background-repair Release build passes with **zero warnings/errors**, and the app has been reopened with the existing development profile. The frozen media guide was refreshed inside its corresponding-source archive: comparison covered **13,913 members**, preserving member metadata and every other member's decoded bytes; only `provenance/media-engine.md` changed. All eleven runtime binary hashes remained intact. The new archive SHA-256 is `dbc9ce8992f4edd3b0b10a422636cc60c00cc983198022eb156ed88e5d6999a0`; independent verification and an injected replacement-failure rollback check passed.

## Performance status

The quality and 60 FPS target are retained. Performance acceptance is **not complete**.

| Measurement | Recorded p95 | Qualification / gate |
|---|---|---|
| Generated GPU rendering/readback/UI installation, 100% / 150% / 200% raster | 2.89 / 6.46 / 12.21 ms | Synthetic owned source; excludes WGC contention, Explorer and scanout |
| Original fresh target creation/show, 30 warm samples | 64.07 ms | Real native target; misses 16.7 ms |
| Initial prepared target binding + complete native show, 30 warm samples | 26.33 ms | Earlier real-target result; idle preparation separate |
| Current hidden-preplaced target, two paired 30-warm runs | 20.53 / 21.62 ms | Repeatable median improvement; both full-show p95 values still miss 16.7 ms |
| Current production admission callback, same two runs | 11.66 / 10.96 ms | Generated WPF physical-file callback; not first blurred presentation |
| Current request through production callback, same two runs | 27.98 / 28.43 ms | Misses 16.7 ms; not a warm first-blur result |

The paired experiment alternates fresh control and hidden-preplaced targets and repeats in a separate process. Hidden physical sizing occurs during `ApplicationIdle`; both admission placements remain for DPI/z-order safety. Generated callback return is not bitmap presentation, DWM scanout or Explorer acceptance. The summary and raw reports (machine-local evidence retained privately) preserve those limits and missed gates.

The guarded capture run stopped at activation 10 with `InputCropOwnershipUnavailable` after five instrumented cold/device-reset and four warm samples. It is not five real cold app launches plus thirty warm accepted activations. Do not subtract guard time or call admission time first blur. The warm ≤100 ms / cold ≤250 ms first-blur gates and complete native 16.7 ms steady-state gate remain open.

There is also a harness limitation: `NativeBackingStartupTests` injects a custom
`createSession`, making `LiveWheelGlass.UsesBootstrap` false and bypassing the
production first-blur bootstrap. Its `FirstLiveMs` measures guarded WGC first
bitmap installation with exhaustive ownership-probe overhead (roughly 9–12 s in
the preserved run), not production opening latency. Rerunning that harness after
tool-overlay cleanup would still not establish the requested production first
blur or five true cold app launches; the reports and raw data are unchanged.
