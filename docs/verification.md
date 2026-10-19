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
| Locked solution build, all six projects | PASS: six projects, zero warnings/errors |
| Portable core suite | PASS: 65 passed, zero failures/skips |
| Complete conversion suite on Linux native libraries / host FFmpeg | PASS: 160 passed, zero failures/skips; entire suite used the no-lavfi fixture-contract wrapper |
| Managed fixture regression | Red observed: prior lavfi initializer fails under a host wrapper enforcing shipped CONFIG_LAVFI_INDEV=0. Replacement uses generated PCM WAV/raw YUV; all 160 conversion cases subsequently pass with that same wrapper. This is a fixture-contract simulation, not Windows PE execution. |
| Package negative and ownership checks | PASS: five tests, including 12 missing-asset subcases. Empty/missing app, runtime, image/PDF/archive/media native, license and source assets reject. Literal owned-file deletes preserve unknown root and nested outputs; unsafe paths/links reject. Initial interface red preceded implementation. |
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
