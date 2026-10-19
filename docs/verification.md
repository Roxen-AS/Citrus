# Candidate verification record

Status: **0.3.0-preview.1 source candidate; current Windows UI acceptance pending**.
This record is committed product documentation so evidence survives cleanup of
local development reports. Artifacts are `dist/Citrus-Setup.exe`,
`dist/Citrus-win-x64/`, and `dist/SHA256SUMS`. The source archive inside the payload
is corresponding media source, not the Citrus application source ZIP.

Environment: Linux x64; .NET SDK 10.0.402, runtime 10.0.13; Python 3.12;
NSIS 3.11. No Windows desktop session was available. Earlier bounded Wine attempts
could not start the shipped runtime; no further Wine attempt is used as evidence.

## Current source checks — 0.3 preview

The combined editor and presentation source at `c1ec51c` passed a locked Release
solution build with zero warnings/errors. Core: **170 passed, one Windows-only
sharing-violation case skipped**. Conversion: **492 passed, zero failures/skips**.
These ran on Linux with host FFmpeg 7.1.5; they do not establish Windows rendering,
physical Explorer behavior, device audio or compositor appearance. The application
version is 0.3.0-preview.1 (assembly/file version 0.3.0.0).

The exact native media correction at `aa032b8` passed **49/49** tests on Windows
using the bundled FFmpeg/FFprobe 9.0.2 executables in run
[39220122966](https://github.com/Skytuhua/Citrus/actions/runs/39220122966).
That focused result is distinct from the combined Windows checks below.

At `db93897`, Windows run
[39226665887](https://github.com/Skytuhua/Citrus/actions/runs/39226665887) passed
**171 Core tests** and **447 Conversion tests, with three explicit Linux-only
surrogate skips**. The 450 Windows total versus 454 Linux total is explained by
two skipped theories not expanding into their six Linux data rows. All other
case names and multiplicities match. All four image-policy tests, all 49 focused
media cases, and all four native AVIF lifetime scenarios passed. The real
self-contained payload, native hashes/notices, 586 owned files, media smoke,
five package-negative cases and two portable recovery cases also passed. The
official Chocolatey NSIS feed subsequently returned HTTP 503, so current Windows
installer compilation and native installer recovery did not run.

The neutral Light/Dark visual rework replaces the earlier brown palette, keeps
the workspace action footer pinned, and adapts wheel metadata to enlarged text.
The standard Light/Dark minimum-size and enlarged-text WPF images have been
inspected. Actual full-name wheel UI Automation regressions passed twice on
Windows. At `7c29284`, both populated-history/warnings stress cases passed,
including 200% text, with all eight Light/Dark screenshots inspected. The actual
failure was a TwoWay ProgressBar binding to the read-only job Percent property;
the production binding now explicitly uses OneWay. The pinned action, reachable
retry controls and full warning text remain visible through scrolling.

That run then exposed a separate late media-control Unloaded event cancelling
an already disposed CancellationTokenSource. The reviewed correction at
`c1ec51c` makes disposal joinable and late Stop/Pause idempotent, with four causal
portable regressions. Real paused/playing unload/reload/replacement cases are
included in the next native run. This source also integrates the reviewed image
annotation/manual-mask host, exact text geometry and readable installed-font
catalog. Windows run
[39230998084](https://github.com/Skytuhua/Citrus/actions/runs/39230998084)
completed without a fatal dispatcher exit or quarantine: **265 passed, four
failed, four skipped**. All eight annotation tests, four media teardown tests,
the real job template, six image-editor window tests and both history stress
cases passed. The four remaining failures were obsolete route and immutable-array
comparison assertions; their reviewed test-only corrections preserve the source
identity and real capability contracts. The actual editor images exposed an
unreadable native ComboBox face in Dark mode. A reviewed control/material polish
now supplies consistent dropdown, checkbox and scrollbar surfaces, plus opaque
directional wheel shading and a recessed hub. Corrected native execution and
fresh visual acceptance remain pending. The whole interface suite is not yet
accepted.

Native glass run
[39226665695](https://github.com/Skytuhua/Citrus/actions/runs/39226665695)
passed all six lifecycle tests, including actual Quit cleanup. Seven screen
captures were refused because an unrelated foreground window owned the first
pixel, and the real-pointer check explicitly refused unowned input before any
injection. There are zero optical glass proof images. The wheel's production
native-backdrop gate remains disabled; WPF images and DWM return codes are not
optical blur evidence. Actual device audio, physical Explorer drag behavior and
mixed-monitor transitions also remain unobserved.

The later `c1ec51c` x64 desktop run produced 22 owned compositor frames as well as
103 WPF renders. Those raw frames have not established optical eligibility:
the measured live-background difference is zero and the production companion
gate stays false. The separate ARM workflow still refused its seven captures
and real-pointer action before reading/injecting. The compositor test's fixed
Host description mentions ARM even when executed by the x64 desktop workflow;
the recorded runtime architecture and workflow identify the actual environment.

The older installer and test counts below are retained baseline evidence. They do
not identify a rebuilt 0.3 installer or establish acceptance of the current UI.

## Earlier 0.2 baseline checks and interpretation

Checks executed on 2026-10-19. Native Windows items remain pending:

| Check | Status |
| --- | --- |
| Locked solution build, all six projects | PASS: clean, locked restore, six projects, zero warnings/errors |
| Portable core suite | PASS: 70 passed, zero failures/skips |
| Complete conversion suite on Linux native libraries / host FFmpeg | PASS: 166 passed, zero failures/skips; entire suite used the no-lavfi fixture-contract wrapper |
| Managed fixture regression | Red observed: prior lavfi initializer fails under a host wrapper enforcing shipped CONFIG_LAVFI_INDEV=0. Replacement uses generated PCM WAV/raw YUV; all 160 conversion cases subsequently pass with that same wrapper. This is a fixture-contract simulation, not Windows PE execution. |
| Package negative and ownership checks | PASS: five tests, including 12 missing-asset subcases (prior Task 8 evidence; packaging code unchanged in the final source/naming fix). Empty/missing app, runtime, image/PDF/archive/media native, license and source assets reject. Literal owned-file deletes preserve unknown root and nested outputs; unsafe paths/links reject. Initial interface red preceded implementation. |
| Self-contained Windows publish / NSIS compile | PASS: 579 payload files inventoried; NSIS 3.11 x86-unicode installer and uninstaller compiled |
| Native payload hashes, source provenance and notices | PASS: image/PDF/archive/media native hashes, every bundled notice and media source provenance verified; archive-only reconstruction matches all recipe inputs |
| Windows CI / actual shipped PE smoke / Windows tests | Configured, not executed here |
| Standard-user installer/uninstaller and Explorer acceptance | Pending every item in windows-acceptance.md |

The Windows workflow runs all core/conversion/desktop tests and the independent
shipped-media smoke test. Managed media fixture paths are explicitly the published
FFmpeg/FFprobe, and raw WAV/YUV inputs need no disabled capture device. PDF/image
coverage is consolidated into this complete workflow. The second-volume Shell
case requires a real separate volume and remains explicitly skipped otherwise.

The startup/installer gate has a Windows-only regression test for contention and
release. Compilation cannot prove Win32 SID conversion, Toolhelp enumeration,
mutex ownership, reparse behavior, native codec loading or Explorer interaction.
Installer metadata/checksums provide artifact identity and content verification,
not Authenticode publisher signing or Windows acceptance.

## Reproduce the available checks

```sh
dotnet build Citrus.sln -c Release -p:RestoreLockedMode=true -m:1 -nodeReuse:false
dotnet test tests/Citrus.Core.Tests -c Release --no-build --no-restore
dotnet test tests/Citrus.Conversion.Tests -c Release --no-build --no-restore
bash packaging/publish.sh
python3 packaging/test-package.py dist/Citrus-win-x64
python3 packaging/package.py verify dist/Citrus-win-x64
(cd dist && sha256sum -c SHA256SUMS)
```

For the fixture regression, the Linux host wrapper rejected `-f lavfi` with a
nonzero exit and otherwise exec'd `/usr/bin/ffmpeg` with unchanged arguments.
The old initializer failed before its assertion; generated WAV/YUV fixtures
allow the same behavior suite to pass. Distributed `config_components.h` confirms
`CONFIG_LAVFI_INDEV 0`, `CONFIG_WAV_DEMUXER 1`, and `CONFIG_RAWVIDEO_DEMUXER 1`.
This bounded simulation tests fixture compatibility, not the shipped PE runtime.

The source archive SHA256 is
`9411fb1d2b78bc56af3255777dcac70396c5f067b1d5f2ec2eb62c94b5cd5803`.
Application/installer identities belong to the generated SHA256SUMS, avoiding a
self-referential checksum embedded inside the installer. The final payload
includes this record; no native Windows test or manual checkbox is represented
as passed. Final whole-branch review is a separate delivery gate.

## First-install interruption correction (T8-I1)

Review found that a failure during initial extraction could create a directory
before recording ownership or writing an uninstaller, stranding subsequent setup.
Setup now flushes a fixed-root/inventory recovery receipt first, then establishes
the embedded uninstaller and complete identity before ordinary file extraction.
Only that matching receipt or a complete matching identity authorizes retry;
unrelated trees and mismatches remain refused. The receipt also authorizes a
complete bootstrap uninstaller after partial metadata initialization.

Focused portable evidence: `python3 packaging/test-install-recovery.py` initially
failed against the prior source after the first payload write. Both tests now
pass: interruptions at each source-derived bootstrap mutation boundary retain
a flushed receipt before the directory exists, and a first-file interruption can
replay the recipe and perform literal owned cleanup while preserving unknown root
and nested outputs. These tests use real temporary files but substitute registry
and uninstaller operations. They are **ordering/contract evidence, not native
NSIS fault-recovery execution**. Unchanged .NET suites were not rerun for this fix.

`packaging/test-install-recovery.ps1` compiles a separate fault-injection installer
and exercises actual interruption, refusal, retry, payload hashes and uninstall on
Windows. The full Windows workflow invokes it. Native execution and bootstrap
failure scenarios remain unchecked in windows-acceptance.md. The actual ordinary
installer is rebuilt and its payload/checksums verified after the fix; no Windows
success is inferred from that compilation.

## Final source and output naming correction (F-I1, M1, M3)

Fresh verification after these product changes: clean solution build and locked
restore succeed with zero warnings/errors; all **70 core** and **166 conversion**
cases pass with zero failures/skips on Linux. The full conversion suite uses the
same `/tmp/citrus-no-devices-ffmpeg` host wrapper described above. These suite runs
include archive-entry traversal/link/duplicate/budget checks and shared writer
concurrent-collision/cancellation behavior. The five package negatives and two
portable recovery checks retain their prior passing evidence; neither packaging
logic nor those contracts changed, so those suites were not repeated.

Focused regression evidence:

- M1: actual writes to a 220-character file stem plus `.png` and a 220-character
  directory name first failed with `PathTooLongException`. Both now pass, retain
  exact final bytes/nested contents and file extension, and leave no staging.
  Staging uses a short `.citrus-` prefix plus GUID and the requested file extension;
  final collision numbering and atomic nonreplacement are unchanged.
- M3: four ZIP/TAR single/group naming cases first failed for missing `-archive`.
  They now pass with first-input naming, explicit reordering, preserved preexisting
  collision bytes, numbered returned paths and archive entry order. GZIP remains
  `first.txt.gz` and its round-trip case passes unchanged.
- F-I1: six actual source-link cases first failed eligibility for three archive
  formats and three subtitle conversions. After removing source exclusions,
  archive creation still failed because its name validator interpreted adjacent
  selected sources as extraction paths. Creation now validates logical names under
  its unique temporary archive path; extraction still validates beneath its real
  staging directory with all original guards intact. All six cases now read actual
  selected bytes, produce normal ZIP/regular TAR entries or converted subtitles,
  and extract a linked outer archive. Three shared attribute cases confirm ready
  reparse inputs are accepted in each mode, each Offline/Recall bit still rejects,
  and directories remain Actions-only. Shared availability code is unchanged.

The six file-symlink cases are explicitly Linux-only host surrogates and skip on
other operating systems. They are **not Cloud Files runtime tests**. Pending native
Windows acceptance records actual downloaded/pinned cloud attributes, archive
creation, cloud-hosted outer-archive extraction, subtitle conversion, and rejection
of online-only/recall files without data access. Windows long-component behavior,
actual shipped PE/native loading, Explorer and installer execution also remain
pending. No Wine attempt or Windows CI success is claimed.

This verification text is finalized before the corrected candidate's single
`packaging/publish.sh` run, which rebuilds the actual self-contained app and NSIS
installer and generates SHA256SUMS. Artifact-specific identities remain in that
manifest and the delivery report, outside the embedded documentation. The media
recipe, notices, binary assets and corresponding source archive are unchanged.


## Interface visual evidence harness (Task 4)

The native test project now captures the production WheelWindow and MainWindow
with the actual App resource dictionaries and ThemeManager. The wheel uses physical
image fixtures, real Image/Archive/PDF catalog eligibility and FilePreviewService;
synthetic routed WPF callbacks drive entry, hover and timed page navigation. No
production rendering hooks, replacement artwork, OS setting changes or conversion
execution are involved. Assertions cover visible bounds, fixed hover geometry,
retained petal identity, clean reuse, batch count and partial page state. Existing
callback-level OLE effect/release-order tests remain distinct from physical Explorer
acceptance; no duplicate tests are presented as additional native input evidence.

Both Windows workflows upload recursive `**/TestResults/**` under `if: always()`.
PNG/JSON files reside in `TestResults/visual` relative to the test process, while
TRX files use the command's results directory. This also collects older workspace
artifacts below `tests/Citrus.Windows.Tests/bin/.../TestResults/workspace`.

Images span light/dark and 100/150/200% **raster export scales**. JSON records actual
visual DPI and OS high-contrast/animation capabilities. No monitor DPI or system
accessibility setting is changed. High contrast is exercised only if already
active. The hover JSON records warm synchronous callback time and dispatcher-thread
allocation without timing thresholds or frame-pacing claims. It excludes native
OLE initiation, rasterization/composition, preview decode and other-thread memory.

Native rendering and new-test behavioral red/green are **pending Windows execution**;
Linux compilation cannot produce these WPF images. Actual Explorer, mixed-monitor
DPI, animation, sound and accessibility checks remain in windows-acceptance.md.
Earlier verification counts above belong to their named candidate revisions and
are not a native pass claim for this harness. Packaging and installer generation
are unchanged by Task 4 and remain separate controller delivery checks.

Workspace file names contain the **requested** window size. The window can be
clamped by the hosted desktop; JSON records requested size, actual window/content
size, actual DPI and monitor bounds/work area. Adaptive inspector placement is
checked against actual window width, matching the production breakpoint. These
images do not establish the 1180 DIP layout unless the recorded actual width is
1180; inspect that layout on a sufficiently large native desktop. No offscreen
layout substitute or altered OS display setting is used.

## Glass Task 4 evidence seams

The native [glass evidence procedure](native-glass/README.md) documents separate
ordinary/wheel actual compositor crops, fine/broad-pattern raw measurements,
explicit solid captures, and lifecycle/quit diagnostics. The original capture and
ownership algorithms are preserved; the owned scene's guarded target slot also
supports the actual ordinary MainWindow HWND. The capture-dependent clip-input
fixture now uses the same single normal owned-background activation experiment as
the native proof and asserts preservation of its resulting foreground baseline.

GlassResourceLifetimeTests extends confirmed routed admission/cancel/reopen,
policy fallback/helper-owner destruction, dark/reduced-motion reconfiguration,
service teardown, actual resident QuitAsync/OnExit observations and native initial
work-area fitting. These remain synthetic routed admission and policy decisions,
not physical OLE or OS accessibility changes. Actual compositor review and physical
monitor/accessibility acceptance are pending; no WPF image is promoted to DWM proof.

`windows-glass.yml` selects the new classes plus relevant surface, interaction,
spring and workspace regressions and uploads all TestResults recursively even on
failure. Runtime guard refusal remains a nonzero xUnit 2 result with truthful
capability metadata and no refused-frame PNG. Numeric measurements have no invented
cross-OS optical pass threshold. The production wheel gate remains false and SDK
projection redistribution licensing remains separately unresolved. Exact revision
build/test results belong in the task report and CI artifacts, not inferred here.
