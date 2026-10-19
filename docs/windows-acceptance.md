# Windows acceptance — pending

No checkbox below has been executed on a native Windows host for this candidate.
Record OS build, account type, candidate SHA256, tester/date and actual outcome
when running it on Windows 10 22H2 and Windows 11 x64. Linux compilation and host
codec tests do not check these items. CI results supplement, not replace, real
Explorer, accessibility and clean-machine checks.

## Highest-risk interaction

- [ ] Explorer physical image drag → tap Shift → real wheel → PNG drop → valid new copy; source hash and location unchanged.
- [ ] Escape, neutral center, outside drop, navigation-target drop, canceled dialog: no outputs. Unsupported text/window/virtual/offline-or-recall-cloud drags never enqueue. Unconfirmed capture times out in 750 ms.
- [ ] Shift held before drag, tapped during drag, released after opening; Alt/Control mode switching; latest key wins and tap-and-latch persists.
- [ ] More than nine choices: arrows switch after 400 ms hover. Wheel remains anchored and hit-testable at all screen corners.
- [ ] Desktop and Explorer multiple files; folders Actions only; 100/150/200% DPI, mixed-DPI monitors and negative monitor coordinates.
- [ ] Pause/remap gestures; ordinary Explorer move/copy and normal text selection continue working.

## Operations and resident behavior

- [ ] Every enabled output in operations.md produces decodable copies offline on a clean machine. Real bundled image/PDF/media libraries load without development tools or separate .NET.
- [ ] Optional H.264 positive probe and Windows N/unavailable-MFT fallback; MP4 default is MPEG-4. Animated GIF retains frames in video operations.
- [ ] PDF merge, selected split/pages/text, image group order and mixed-folder destination; encrypted/bad inputs reject. PDF text has no OCR.
- [ ] Actually downloaded/pinned cloud files: record provider, hydration/pin state and observed `FileAttributes` (including ReparsePoint, Offline, RecallOnOpen and RecallOnDataAccess). With no unavailable bits, archive creation (including a mixed group), extraction of a cloud-hosted outer archive and SRT/VTT conversion read local bytes and preserve sources. Record whether the ready file exposes ReparsePoint; a file lacking that bit does not exercise the conditional regression. Online-only/recall inputs stay ineligible without reading/hydrating their data. Linux source-symlink fixtures are not this Cloud Files acceptance.
- [ ] ZIP/TAR groups use the first confirmed stem plus `-archive` even for one input; reordering changes that stem, existing output bytes survive collision numbering, and GZIP remains `first.txt.gz`. A legal 220-character stem succeeds for both file output and extraction directory, preserves contents/extension, and leaves no staging.
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

## Interface evidence harness — native execution pending

Run `dotnet test tests/Citrus.Windows.Tests -c Release --no-build --no-restore
--logger trx --results-directory TestResults/windows` on Windows after the locked
Release build. Both Windows workflows always collect `**/TestResults/**`, including
artifacts below the test executable's working directory, even when assertions fail.

`TestResults/visual` contains actual production WPF wheel and workspace PNGs and
JSON sidecars. Wheel states cover an admitted folder with no Convert choices,
first page, selected petal, batch badge/real decoded image, and partial second
page reached through the 400 ms navigation timer. Clean reuse checks that the next
unconfirmed capture has no stale petals, caption or preview. Bounds and retained
identity assertions supplement image inspection; they do not establish visual parity.
The workspace matrix covers 1180×820 and 860×640, light/dark, before/after scrolling
the actual inspector Run button into view.

Exports at 100%, 150% and 200% are **RenderTargetBitmap raster scales of the current
WPF layout**, not actual native DPI transitions. Sidecars record actual visual DPI,
OS high contrast, animation capability and requested theme. High contrast overrides
light/dark when already enabled; the harness never changes system settings and
cannot claim high-contrast coverage when the runner has it disabled.
`wheel-hover-performance.json` reports five warm repeated-hover samples, dispatcher
thread allocation and elapsed time. Preallocated synthetic routed events exercise
real hit testing and selection with motion/audio disabled. The measurements exclude
COM/OLE initiation, rendering/composition and other threads; no FPS or latency
threshold is asserted.

- [ ] Inspect every PNG at both palettes and each export scale; confirm petal gutters, caption, icons, thumbnail, badge, navigation and workspace controls are readable and unclipped. Record artifact/run/commit identity and actual observations.
- [ ] On a high-contrast Windows session, rerun the suite and inspect the system-brush renders; verify keyboard focus and screen-reader names. Record the actual OS setting rather than relabeling a light/dark image.
- [ ] Use actual 100/150/200% displays and mixed-DPI monitors, including negative coordinates; confirm hit geometry, wheel anchor, initial presentation and monitor transitions. Raster-scale exports do not satisfy this item.
- [ ] Drag actual Explorer files with preheld/tapped Shift/Control, release before/after OLE Drop, Escape, leave/reentry and ordinary range/marquee selection. Confirm no unwanted full wheel, payload changes, focus stealing or duplicate submissions. Existing WheelInteractionTests use synthetic routed WPF callbacks and do not satisfy this item.
- [ ] On an animation-capable desktop, inspect opening/hover/dismissal and optional audio; profile actual input-to-presentation latency separately from the warm callback metrics.

Workspace file names contain the **requested** window size. The window can be
clamped by the hosted desktop; JSON records requested size, actual window/content
size, actual DPI and monitor bounds/work area. Adaptive inspector placement is
checked against actual window width, matching the production breakpoint. These
images do not establish the 1180 DIP layout unless the recorded actual width is
1180; inspect that layout on a sufficiently large native desktop. No offscreen
layout substitute or altered OS display setting is used.

## Native glass and lifecycle acceptance

Use [the native evidence procedure](native-glass/README.md) on the exact reviewed
commit. The production wheel remains explicitly solid until disabled/clipped
companion frosting has accepted compositor evidence. Ordinary `ConfiguredNativeAcrylic`
is configuration only. Keep WPF raster images separate from native compositor crops.

- [ ] Guarded ordinary-window and wheel fine/broad-pattern captures: full desktop,
  station/session, visual z order and every crop input pixel verified before reads;
  frame/state/preflight JSON and source SHA retained; raw measurements visually reviewed.
- [ ] Explicit solid fallback over changed owned patterns, with opaque readable
  content and caption backing; no global transparency or accessibility writes.
- [ ] Confirm/cancel/reopen, theme and accessibility transitions visually reviewed
  on real composition, including hidden intervals; no bare helper or clipping seam.
- [ ] Actual resident QuitAsync/OnExit clears wheel HWND/helper, pending material
  callback, attached animator, active/retained channels and Rendering subscription;
  configuration tests independently destroy native helper and hidden owner on close.
- [ ] Actual mixed-monitor DPI and negative-coordinate placement/input ownership;
  actual 200% text, high contrast and reduced motion; genuine external OLE drag.
- [ ] Review ordinary and wheel optical eligibility separately; retain explicit
  capability-unavailable status and zero screen reads/PNGs for refused frames.

The xUnit 2 harness reports runtime capture capability refusal as a nonzero test
failure with diagnostic JSON, not a passing optical skip. One normal activation of
the owned opaque background is permitted fixture setup; failure does not weaken
capture guards. Source-only checks cannot mark any of these manual/native boxes done.
