# Citrus Interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Citrus's Explorer wheel stable and give the working Windows app the polished, intuitive interface shown in the user's Tangerine references.

**Architecture:** Preserve the existing WPF resident, genuine OLE handoff, submission service and safe conversion engines. Separate drag ownership from visual presentation; retain wheel visuals and build an accessible workspace and quiet job feedback on shared resources.

**Tech Stack:** .NET 10.0.402, WPF, existing Citrus.Core/Citrus.Conversion engines, xUnit, Windows CI.

**Spec:** `docs/superpowers/specs/2026-10-18-citrus-interface-design.md`

## Global Constraints

- Windows 10 22H2/11 x64; native WPF desktop app; local processing and no runtime backend downloads.
- Non-suppressing global input; genuine physical OLE FileDrop and Copy permission required; freeze the first payload and validate the final drop against it.
- Preserve original files, collision-safe adjacent output copies and existing SubmissionDraft → SubmissionChoice → queue/output writer path.
- Keep 750 ms unconfirmed timeout, 8 DIP deliberate arming, 400 ms page hover and up to nine targets per page with equal angular spacing for the actual visible count.
- No placeholder tools, proprietary assets, global volume changes or unsubstantiated Windows/100% parity claims.
- Respect reduced motion, high contrast, light/dark/system theme, per-monitor DPI, keyboard access and shutdown cancellation.

## Review Focus

- Ordinary Explorer range/marquee selection and non-file dragging must not become a full wheel or submission (Task 1).
- A delayed queued key event after release must not reopen the wheel; valid OLE completion must survive release ordering (Task 1).
- Rounded petal gaps/corners and missing slots must be neutral; animation cannot move hit targets (Task 2).
- Huge/corrupt previews and rapid drag changes must not block input or show stale thumbnails (Tasks 2–3).
- Batch inclusion must remain explicit through modifier clicks, preview changes and quiet wheel submissions (Task 3).

---

### Task 1: Stable genuine drag ownership

**Files:** Modify `src/Citrus.Core/GestureState.cs`, `WheelGeometry.cs` (WheelSession only), `src/Citrus.Windows/GestureMonitor.cs`, `WheelWindow.xaml.cs`; tests `GestureTests.cs`, `WheelSessionTests.cs`, new `tests/Citrus.Windows.Tests/WheelInteractionTests.cs`.

**Interfaces:** Preserve `WheelWindow.ShowAtPointer(OperationMode)`, `ObserveGlobalRelease()`, `Dismiss()` and `OperationDropped`. Add an observable immutable gesture epoch to `GestureState`; queued dispatch must capture and validate it. `WheelSession.ConfirmPayload` must keep its first defensive snapshot; mode changes must not replace it. Expose production interaction helpers only where they represent a real responsibility, not test-only hooks.

- [ ] Write failing regressions: first payload [A,B] survives later [A,B,C]/reorder, source array mutation and mode switches; directory Actions→Convert→Actions retains payload; queued key activation invalid after button-up/new mouse-down/disable/Escape; confirmed OLE completion survives earlier global release.
- [ ] Run the affected Core tests and record intended failures. Implement the drag epoch and immutable admission protocol. An unconfirmed capture surface may be nearly invisible but must be real OLE, nonactivating and short-lived; do not display full menu content before genuine admission. On a confirmed drag, read copied paths once on entry, cache availability for all modes, and compare freshly read drop paths at completion. DragOver performs cached point/selection/effect work only.
- [ ] Add Windows integration coverage for genuine FileDrop versus non-file/unsupported effects and repeated DragOver. Keep non-file/marquee candidates visually empty and ensure changed payload/drop is rejected. Run host Core tests and locked Release Windows compile; report native-only tests explicitly unrun on Linux.
- [ ] Commit a focused fix and write the task report with cause, protocol, red/green evidence and remaining native acceptance limits.

### Task 2: Retained rounded wheel, previews, motion and audio

**Files:** Modify `WheelWindow.xaml`, `WheelWindow.xaml.cs`, `WheelGeometry.cs`, `SettingsStore.cs`, settings UI and ThemeManager; create focused `WheelPetal`/geometry, `FilePreviewService`, `FeedbackAudio` components and resource dictionaries `Resources/Tokens.xaml`, `Controls.xaml`, `Icons.xaml`, `Motion.xaml`; behavioral tests in Core and Windows.

**Interfaces:** Preserve Task 1's ownership API and frozen payload protocol. `FilePreviewService.LoadAsync(string path, int pixelLimit, CancellationToken)` returns a frozen `ImageSource?`; it must use bounded image decoding and no UI-thread filesystem/decoder work. Audio offers preloaded nonblocking open/selection/success/error cues with enabled preference. Existing settings files must retain defaults and remaps on migration.

- [ ] Add failing behavioral tests for rounded-boundary/gap neutrality, stable slot order across 1–9 targets, retained object identity after repeated hover, stale preview cancellation and settings migration. Run host regressions before implementation; Windows render tests are compiled and run natively by Task 4.
- [ ] Implement the exact wheel dimensions, geometry, palette, icon/thumbnail/count/caption hierarchy and durations from the spec. Define `WheelGeometry.MaxTargetsPerPage = 9`, and use it for every page count/offset; draw actual-N equally spaced petals, including 1–2 targets and partial last pages. Use one shared rounded geometry for draw and hit tests. Retain petals across hover; update old/new state only. Prepare reset content before showing. No full layout or catalog/COM/filesystem work on DragOver.
- [ ] Implement bounded asynchronous previews and cached optional original sound cues. Add independently configurable sound/reduced-motion settings; sound is enabled by default with an easy mute control. Respect system high contrast and animation preference; cancel pending decode/audio/presentation on teardown.
- [ ] Run affected host tests and locked Release solution compile. Commit and report actual execution versus compiled-only Windows coverage.

### Task 3: Intuitive desktop workspace and quiet feedback

**Files:** Rework `MainWindow.xaml/.cs`, `ToolOptionsWindow.xaml/.cs`, `App.xaml.cs`, `JobViewModel.cs`, common resources; create focused workspace selection/view models and a compact nonactivating job toast; tests `SubmissionWindowTests.cs`, `ResidentLifecycleTests.cs` and Core selection tests where appropriate.

**Interfaces:** Keep `MainWindow.Configure`, `Report`, `AddJobs`, `RefreshJobs`, `UpdateGestureGuide`, `SetBusy`, `RefreshSelection` usable by App. Extend `App.SubmitAsync` with explicit origin (Home/Wheel/Retry) so ordinary wheel conversions do not call `OpenHome`. `SubmissionDraft`/`SubmissionChoice`, batch requests, queue and safe writer stay authoritative. Explicit file inclusion must be independent of the active preview item.

- [ ] Add failing regressions for preview selection versus included files, modifier clicks preserving inclusion, quiet wheel conversion not activating Home, and errors/results remaining recoverable. Use real submission services where possible.
- [ ] Build the adaptive workspace described in the spec using shared resources: icon categories, drop/add controls, explicit file inclusion/count, previews, readable operation cards, inspector and job results/retry/reveal. Show functional routes only. Preserve original-safe output choices and validation. Style existing options with labeled controls and focus/scroll behavior; rich editors are the next subproject rather than fake previews.
- [ ] Add nonactivating progress/result toast and safe wheel demonstration/onboarding. Keep drag submission callback deferred until after OLE. Ensure cancellation, close-to-tray and quit dispose all preview/audio/toast resources.
- [ ] Run affected host tests, locked Release solution compile and compile native integration tests. Commit and report keyboard/theme/DPI evidence limitations honestly.

### Task 4: Windows visual and interaction evidence

**Files:** Extend `.github/workflows/windows.yml`, `tests/Citrus.Windows.Tests/DesktopFixture.cs`, new `WheelRenderingTests.cs`/`WorkspaceRenderingTests.cs` and focused artifact helper; update `docs/windows-acceptance.md`, `docs/verification.md`, packaging version/docs as needed.

**Interfaces:** Existing DesktopFixture provides an STA Dispatcher. Add an artifact helper confined to the test project, writing PNG and JSON under `TestResults/visual`. Exercise real WPF resource dictionaries and production presentation components; avoid test-only production rendering paths.

- [ ] Add Windows render tests for light/dark/high contrast where controllable, wheel empty/hover/batch/page states and workspace minimum/default size. Check visible bounds, no stale content and retained visual identity; save PNGs for inspection.
- [ ] Measure repeated-hover work after warmup and record elapsed/allocation data without claiming a CI timer measures real desktop frame pacing. Add real clipboard/OLE effects/input-order integration where the runner permits; clearly separate synthetic OLE tests from interactive Explorer acceptance.
- [ ] Wire Windows CI to upload render/test/performance artifacts with `if: always()`. Run host suites and locked build, packaging checks and candidate installer. Execute native CI if credentials permit an isolated feature branch/draft PR; do not push shared main or publish a release.
- [ ] Review generated native images/logs if available, correct concrete defects, and document any unavailable native evidence. Deliver a concrete installable candidate through the already established usable private download route.
