# Windows acceptance — pending

No checkbox below has been executed on a native Windows host for this candidate.
Record OS build, account type, candidate SHA256, tester/date and actual outcome
when running it on Windows 10 22H2 and Windows 11 x64. Linux compilation and host
codec tests do not check these items. CI results supplement, not replace, real
Explorer, accessibility and clean-machine checks.

## Highest-risk interaction

- [ ] Explorer physical image drag → tap Shift → real wheel → PNG drop → valid new copy; source hash and location unchanged.
- [ ] Escape, neutral center, outside drop, navigation-target drop, canceled dialog: no outputs. Unsupported text/window/virtual/cloud-placeholder drags never enqueue. Unconfirmed capture times out in 750 ms.
- [ ] Shift held before drag, tapped during drag, released after opening; Alt/Control mode switching; latest key wins and tap-and-latch persists.
- [ ] More than eight choices: arrows switch after 400 ms hover. Wheel remains anchored and hit-testable at all screen corners.
- [ ] Desktop and Explorer multiple files; folders Actions only; 100/150/200% DPI, mixed-DPI monitors and negative monitor coordinates.
- [ ] Pause/remap gestures; ordinary Explorer move/copy and normal text selection continue working.

## Operations and resident behavior

- [ ] Every enabled output in operations.md produces decodable copies offline on a clean machine. Real bundled image/PDF/media libraries load without development tools or separate .NET.
- [ ] Optional H.264 positive probe and Windows N/unavailable-MFT fallback; MP4 default is MPEG-4. Animated GIF retains frames in video operations.
- [ ] PDF merge, selected split/pages/text, image group order and mixed-folder destination; encrypted/bad inputs reject. PDF text has no OCR.
- [ ] ZIP/TAR/GZIP creation, RAR extraction, `.tar.gz` two-stage extraction, traversal/link/junction rejection and both extraction limits; cancellation cleanup on NTFS.
- [ ] Two-job concurrency, actual progress, cancel during encode/render, accurate partial batch failure; read-only/locked/missing/corrupt inputs; spaces/Unicode, long filenames and repeated names.
- [ ] Full-selection clipboard Copy/files/paths, Move originals cancellation/conflicts/late collisions and cross-volume folders; explicit partial errors. Use CITRUS_TEST_SECOND_VOLUME on a second actual volume for the gated integration case.
- [ ] Shelf drag/Copy/remove/missing reference cleanup; Reveal; Windows Terminal and PowerShell/cmd fallback.
- [ ] Closing Home keeps tray alive; second launch activates it; Quit awaits active owned work and releases hooks/tray/IPC. Separate same-user sessions remain independent residents.
- [ ] Settings persist across restarts; default startup off; explicit startup enable/disable and actual sign-in behavior; themes/high contrast/reduced motion, keyboard/tab/focus and screen-reader labels.

## Installer contract

The installer is a Unicode NSIS x86 process carrying a self-contained x64 app.
It requests `user` execution and fixes the destination to
`%LOCALAPPDATA%\Programs\Citrus`; `/D` overrides are ignored. It stores identity,
inventory hash and uninstall metadata only in HKCU's 64-bit registry view.
Start menu links are per user; desktop link is optional; installation never
writes a Run value. Uninstall removes Run value `Citrus` only when it exactly
matches this installation's quoted executable plus `--background`.

Setup and uninstall obtain the current token SID and current process session ID,
then **own** `Global\Citrus-Install-{SID}` and
`Local\Citrus-{SID}-{SessionId}` mutexes through the mutation. The app briefly
owns the global gate during startup, rejecting startup while setup owns it.
A Toolhelp process snapshot rejects any `Citrus.Windows.exe` process in any
account/session, conservatively including other users. This covers ordinary
shipped app startup across sessions of the same SID; it is not exclusion against
renamed executables, older apps without the gate, or hostile processes racing
filesystem changes. Locked-file failures remain errors. There is no force kill
or install-time app launch. Silent guard/path failures return 23; owned-file
uninstall failures return 24.

The embedded uninstall instructions list literal verified payload paths, not
paths read from the editable installed manifest. Each parent/file is checked
for reparse points before deletion, and parent directories are removed only if
empty. Unknown outputs beneath the installation survive. Settings survive.
Before creating the install tree, setup writes and flushes a single HKCU receipt
binding the fixed root to the embedded inventory hash. It then creates the root,
writes the embedded uninstaller, and flushes the complete identity before ordinary
payload extraction. A matching receipt permits the same setup to retry after an
interruption, including a partial/missing bootstrap uninstaller. Once the
uninstaller is complete, it accepts that receipt even if later identity writes
were interrupted. Unrelated trees and mismatching receipts/identity still reject.
The receipt remains until successful uninstall removes the Citrus-owned key.

Same-inventory reinstall is supported; differing inventory requires running the
old uninstaller first. A first install refuses a pre-existing destination tree.
These checks assume the current user controls the destination during setup;
they are not an OS-handle sandbox against same-account races.

- [ ] Standard-user install/uninstall with no UAC elevation or network; correct Start menu, optional desktop, Apps uninstall metadata, no startup registration.
- [ ] Same-payload reinstall; different-inventory refusal; old embedded uninstaller; spaces/Unicode profile; settings preserved and no opt-in reset.
- [ ] Installed `notes.txt` and `media\converted.mp4` survive uninstall byte-for-byte. No wildcard/recursive deletion. Settings and other Run values survive; exact owned startup value removed.
- [ ] Root or nested junction/reparse substitution aborts before deletes. Missing/wrong registration/root aborts; locked owned file reports error and retains registration for retry.
- [ ] App already running (including hidden Home), same-user RDP/other session, and other user's resident: install/uninstall abort without killing work. Identity matches the app's SID/session exactly.
- [ ] Launch app while installer owns gate: fails clearly without opening resident UI; guard releases on finish/cancel/failure. Installer/uninstaller running concurrently are excluded.
- [ ] `/S` guard failures exit nonzero without waiting for a dialog. Uninstaller's copied temporary executable and current directory permit clean root removal when empty.

## Native recovery failure-injection procedure — pending

On a disposable Windows account with no Citrus directory or registration, publish
normally and run `pwsh ./packaging/test-install-recovery.ps1`. The harness compiles
a separate installer with `CITRUS_TEST_FAIL_AFTER_FIRST_FILE`; the release build
contains no runtime fault switch. The fault build exits 25 immediately after the
first successful ordinary payload write. The harness checks strict refusal of an
unrelated tree and wrong receipt, the partial payload and bootstrapped uninstaller,
successful retry with the normal installer, every installed payload hash, and
uninstall preserving root/nested user-output fixtures. It removes only exact
fixture files and empty directories after success; on failure it retains evidence.
The complete Windows workflow now runs this harness. It has **not run on Linux**.

- [ ] Execute the native harness on supported Windows and record results, including first-file interruption/retry and unknown-file preservation.
- [ ] Interrupt during initial receipt write/flush, root creation and uninstaller write (for example with controlled disk/access failures). If no tree was created, ordinary retry works; if a tree exists, the matching receipt permits same-candidate recovery. A partial uninstaller is replaced on retry.
- [ ] After first-file interruption, run the bootstrapped uninstaller directly without first completing setup; owned partial files are removed and unknown files survive. Native registry flushing, silent exit codes and real disk-full behavior remain pending.
