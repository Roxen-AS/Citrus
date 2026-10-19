# Candidate verification record

Status: **unsigned Windows installer candidate; native Windows acceptance pending**.
This record is committed product documentation so evidence survives cleanup of
local development reports. Artifacts are `dist/Citrus-Setup.exe`,
`dist/Citrus-win-x64/`, and `dist/SHA256SUMS`. The source archive inside the payload
is corresponding media source, not the Citrus application source ZIP.

Environment: Linux x64; .NET SDK 10.0.402, runtime 10.0.13; Python 3.12;
NSIS 3.11. No Windows desktop session was available. Earlier bounded Wine attempts
could not start the shipped runtime; no further Wine attempt is used as evidence.

## Checks and interpretation

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
