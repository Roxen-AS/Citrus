# Candidate verification record

Status: **0.3.0-preview.1 source candidate; native UI/control harnesses executed; manual interaction and optical acceptance pending**.
This record is committed product documentation so evidence survives cleanup of
local development reports. Build outputs use `dist/Citrus-Setup.exe`,
`dist/Citrus-win-x64/`, and `dist/SHA256SUMS`. The source archive inside the payload
is corresponding media source, not the Citrus application source ZIP.

Environment: Linux x64; .NET SDK 10.0.402, runtime 10.0.13; Python 3.12;
NSIS 3.11. No local Windows desktop session was available; hosted Windows results
are recorded separately below. Earlier bounded Wine attempts
could not start the shipped runtime; no further Wine attempt is used as evidence.

## Source checks — named 0.3 preview revisions

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

## Executed hosted Windows checks — exact cb8313c candidate

Candidate `cb8313c84492badc425e3c42b2db85ba1f6befd8`, 2026-10-19:
[general run 39238359290](https://github.com/Skytuhua/Citrus/actions/runs/39238359290)
and [desktop run 39238359292](https://github.com/Skytuhua/Citrus/actions/runs/39238359292).
Original TRX, PNG/JSON and available completed logs were retained with verified
artifact hashes.

| Suite | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| Core | 171 | 0 | 0 | 171 |
| Conversion, bundled Windows FFmpeg/FFprobe 9.0.2 | 485 | 0 | 3 | 488 |
| Windows, independently in general and desktop runs | 281 | 0 | 4 | 285 |

The three Conversion skips are explicit Linux-only source-link surrogates.
The four Windows skips are actual WASAPI opt-in/endpoint, second-volume Shell
move and two animation-capability cases. They establish no device-audio,
second-volume or native-animation success.

Executed passing cases include material/control interaction **8/8**, annotation
interaction **8/8**, image-editor windows **6/6**, media teardown **4/4**, the real
job-history template **1/1**, full-name wheel UI Automation **2/2**, populated
Light/Dark history/warning stress **2/2**, Expander headers/access keys **3/3**,
and toast behavior **2/2**. The neutral Light/Dark controls, pinned action/footer,
actual renderer-backed annotation exports, exact text bounds, readable font
selection and scrolling history are exercised by these tests. The four obsolete
route/container assertions observed at `c1ec51c` are corrected and pass here.
No fatal dispatcher exit or quarantine was recorded.

All three header PNGs at earlier exact
`71fbdbb4e4c405f0e600b71415331b99a62bc3bb` were inspected: Dark 100%, Dark 200% and
forced application High Contrast 200% have readable source/Batch/Save/custom
headers. The same three cases pass again at cb8313c. The access-key
cases use the actual WPF PresentationSource and documented ProcessKey dispatch;
they do not inject physical Alt+B. WPF images and application text/raster scales
are evidence of their recorded layouts, not physical monitor DPI, actual OS
High Contrast or frame pacing. Full clean-machine/accessibility/Explorer
acceptance remains in [windows-acceptance.md](windows-acceptance.md).

Glass lifecycle **6/6** and all five guarded compositor cases, including the
ordinary-window pattern capture, pass on the x64 hosted desktop. These are
executed capture/lifetime assertions, not accepted optical blur. The earlier
`c1ec51c` run retained 22 owned compositor frames with measured live-background
difference **0**, so its production companion eligibility also stayed false.
The separate [ARM-host glass run 39238359274](https://github.com/Skytuhua/Citrus/actions/runs/39238359274)
remains **76 passed / eight capability refusals** (seven capture ownership and
one pointer preflight); it emitted no unowned input or refused-frame screen read.
Its filter does not include the raw host-backdrop probe, so it supplies no
Windows 11 client host-sampling result.

### Host-backdrop probe and current source

At earlier exact 71fb, the test probe stopped after successful COM configuration
but before configured-host capture: requested geometric right endpoint **360**
versus native raster bounding-box right edge **359**. Reviewed test-only fix
`889c3726be5b258620ab9ecfd9786b7c0eea39ac`, integrated as cb8313c, now passes strict
EqualRgn shape and native-reference raster-bound comparisons. Requested
Composition geometry remains separately exact; no tolerance or ownership guard
was relaxed.

Both cb8313c x64 runs completed the two host variants (original redirection and
no-redirection-bitmap), with **12 guarded PNGs per run**, complete raw
measurements and cleanup. In both variants, live host A-to-B change is **0**,
while the matching no-helper baseline changes by **222.10698** mean RGB units.
The opaque magenta control is visible (**356.12454** mean difference from its
matching baseline), demonstrating target presentation. Measured outside-ellipse
leakage and protected hub/caption changes are **0**. Owned objects/queue clean up
and the shell returns to Solid with unchanged foreground.

The observed Windows Server build 26100 x64 scene therefore shows no host
sampling/live response despite successful target presentation. This is neither
accepted optical glass nor a Windows 11 client failure claim; the ARM client
workflow did not run this experiment. The production native wheel gate remains
**false**. WPF material images, successful COM/API setup and ordinary capture
assertions do not independently establish host-backdrop eligibility.

### Installer checkpoint

The earlier exact 71fb general job
[39236830130](https://github.com/Skytuhua/Citrus/actions/runs/39236830130) passed
prepared-payload/native hash and media smoke verification, five negative packaging checks, two portable recovery contracts,
official NSIS acquisition and installer compilation. At **10:33:30 UTC** its
native recovery harness passed first-file interruption, receipt refusal, retry,
owned uninstall and byte-preserving user-output checks. These are actual
Windows installer/recovery results, not a Linux simulation. Its checksum log
records **587 artifacts**. That run's success-only large candidate upload was
skipped because of its probe failure. The cb8313c installer/recovery execution is
still in progress at this record; no current candidate is recorded as installed
on the user's PC. General clean-machine installation, sign-in, multi-session and
the remaining failure-injection scenarios are still manual acceptance items.

The older installer and test counts below are retained baseline evidence. They do
not identify a rebuilt 0.3 installer or establish acceptance of the current UI.

## Earlier 0.2 baseline checks and interpretation

Historical baseline checks executed on 2026-10-19; current native results are
recorded above. This table does not replace the remaining manual checklist:

| Check | Status |
| --- | --- |
| Locked solution build, all six projects | PASS: clean, locked restore, six projects, zero warnings/errors |
| Portable core suite | PASS: 70 passed, zero failures/skips |
| Complete conversion suite on Linux native libraries / host FFmpeg | PASS: 166 passed, zero failures/skips; entire suite used the no-lavfi fixture-contract wrapper |
| Managed fixture regression | Red observed: prior lavfi initializer fails under a host wrapper enforcing shipped CONFIG_LAVFI_INDEV=0. Replacement uses generated PCM WAV/raw YUV; all 160 conversion cases subsequently pass with that same wrapper. This is a fixture-contract simulation, not Windows PE execution. |
| Package negative and ownership checks | PASS: five tests, including 12 missing-asset subcases (prior Task 8 evidence; packaging code unchanged in the final source/naming fix). Empty/missing app, runtime, image/PDF/archive/media native, license and source assets reject. Literal owned-file deletes preserve unknown root and nested outputs; unsafe paths/links reject. Initial interface red preceded implementation. |
| Self-contained Windows publish / NSIS compile | PASS: 579 payload files inventoried; NSIS 3.11 x86-unicode installer and uninstaller compiled |
| Native payload hashes, source provenance and notices | PASS: image/PDF/archive/media native hashes, every bundled notice and media source provenance verified; archive-only reconstruction matches all recipe inputs |
| Windows CI / actual shipped PE smoke / Windows tests | At this earlier baseline: configured; current executed results are recorded above |
| Standard-user installer/uninstaller and Explorer acceptance | Hosted recovery scope is recorded above; remaining manual checks are in windows-acceptance.md |

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
includes this record. Current executed native harness results are identified
above; they do not certify unchecked manual acceptance. Final whole-branch review
is a separate delivery gate.

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
Windows. The full Windows workflow invokes it; its first-file interruption,
receipt refusal, retry and output-preserving uninstall passed at 71fb as recorded
above. Earlier bootstrap/disk-full failure scenarios remain unchecked in
windows-acceptance.md; compilation alone does not establish those scenarios.

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
of online-only/recall files without data access. Cloud-provider behavior, physical
Explorer and clean-machine checks remain pending. Current native PE/loading and
installer-harness results are recorded separately above; Wine supplies no acceptance evidence.

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
accessibility setting is changed. Specific control/header cases also force the
application High Contrast palette; that is not an actual OS setting change. The
hover JSON records warm synchronous callback time and dispatcher-thread allocation without timing thresholds or frame-pacing claims. It excludes native
OLE initiation, rasterization/composition, preview decode and other-thread memory.

Native rendering and the named WPF/UIA cases **executed at cb8313c** as recorded
above; Linux compilation did not produce those images. Actual Explorer,
mixed-monitor DPI, device sound and OS accessibility checks remain in
windows-acceptance.md. Recorded WPF/dispatch results do not establish physical
input, optical frosting or complete UI acceptance. Installer-harness execution
has its separate checkpoint above.

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
results are recorded above; original task reports and CI artifacts retain the
per-case diagnostics and capture limitations.
