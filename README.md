# Citrus for Windows

Citrus is a local file converter with a tray app, an Explorer drag wheel, and a
keyboard-accessible file picker. This repository contains the **unsigned 0.1.0
installer candidate** for Windows 10 22H2 and Windows 11, x64. The Linux build and
fixture results do not establish Windows Explorer, installer, or native-codec
acceptance. See [verification](docs/verification.md) and the unchecked
[Windows acceptance checklist](docs/windows-acceptance.md).

Run `dist/Citrus-Setup.exe` as your ordinary Windows account. It installs its own
.NET runtime and codecs under `%LOCALAPPDATA%\Programs\Citrus`, adds Start menu
entries, and offers an optional desktop shortcut. Launch at sign-in is off until
you enable it in Settings. The candidate has no signing certificate or verified
publisher signature. No administrator rights, accounts, analytics, uploads,
advertising, or runtime codec downloads are used.

Open Citrus from Start, add files, choose Convert, Tools or Actions, then Run.
During an Explorer or desktop file drag, tap Shift for Convert, Alt for Tools,
or Control for Actions. Releasing the key leaves the wheel open. Drop on a
choice to accept, or use Escape, the neutral center, or outside the wheel to
cancel. Hover a page arrow for 400 ms to see more choices. Unconfirmed capture
surfaces disappear after 750 ms. Modifier keys retain their normal Windows
behavior: pause drag gestures from the tray or remap them in Settings when they
conflict with Explorer copy/move shortcuts. Closing Home leaves Citrus running;
choose **Quit** from the tray to stop it.

Conversions and tools create collision-safe copies beside the inputs or in your
chosen destination, preserving original bytes and location. **Move originals…**
is a separate explicit action. At most two conversion jobs run concurrently.
See the precise [enabled operations and limitations](docs/operations.md); this
first release does not claim Tangerine's complete format/editor parity.

Quit Citrus in every session before installing again or uninstalling. Setup
aborts on running app processes and never force-kills them. Reinstalling the same
payload is supported; a different candidate inventory requires uninstalling the
old version first so its embedded ownership list removes old files safely.
If first installation is interrupted, fix the underlying disk/access issue and
rerun the **same candidate**. A durable receipt allows retry; once its embedded
uninstaller has been written, you can also run
`%LOCALAPPDATA%\Programs\Citrus\Uninstall.exe` to remove the partial installation.
Uninstall removes only installer-owned files and shortcuts. Extra outputs under
the install directory and `%LOCALAPPDATA%\Citrus\settings.json` survive. A retained
nonempty install directory must be moved aside before a fresh install. See
[packaging behavior](docs/windows-acceptance.md#installer-contract).

Build with .NET SDK **10.0.402**, Python **3.12+**, and NSIS **3.11**. The pinned
Windows media binaries and corresponding source are checked in. Restore and run
portable suites, then publish:

```sh
dotnet restore Citrus.sln --locked-mode
dotnet build Citrus.sln -c Release --no-restore -m:1
dotnet test tests/Citrus.Core.Tests -c Release --no-build --no-restore
dotnet test tests/Citrus.Conversion.Tests -c Release --no-build --no-restore
bash packaging/publish.sh
python3 packaging/test-package.py dist/Citrus-win-x64
```

On Windows use `./packaging/publish.ps1`; run all three test projects. Set
`CITRUS_TEST_FFMPEG` and `CITRUS_TEST_FFPROBE` to absolute paths of the published
`media/ffmpeg.exe` and `media/ffprobe.exe` before conversion tests; defaults are
Linux host executables for portable development. Complete Windows CI in
`.github/workflows/windows.yml` uses the shipped tools, image/PDF natives and
real desktop test project. Its configured workflow is not evidence of a run.

`DOTNET`, `PYTHON`, and `MAKENSIS` override build executable paths. Linux rootless
NSIS may additionally need `NSISDIR`. Publishing always cleans its designated
`dist/Citrus-win-x64` and `dist/nsis` staging directories. It produces the
self-contained payload, `dist/Citrus-Setup.exe`, and `dist/SHA256SUMS`. Scripts
repeat the pinned recipe; NSIS/package timestamps mean byte-for-byte installer
reproducibility is not claimed. [Third-party notices](THIRD-PARTY-NOTICES.md)
describe bundled licenses and the included media source rebuild instructions.
