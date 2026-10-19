# Native glass evidence

The production wheel companion gate is **false**. Ordinary windows may report
`ConfiguredNativeAcrylic`; that records native configuration, not verified optical
frosting. No compositor acceptance is inferred from an HRESULT or a WPF raster.

Run the `windows-glass.yml` workflow on the exact reviewed SHA. It requires Windows
11 client build 22621 or later and explicitly selects x64 SDK 10.0.402, including
when the runner is ARM64 and executes the x64 product under emulation. Retain host
OS/architecture, SDK host path, SHA, full TRX and recursive artifacts together.
For a local native run, set `CITRUS_GLASS_ARTIFACTS` to a fresh empty owned directory
and record the exact source commit alongside the output; do not mix earlier PNGs
with a later refusal. The Linux cross-build cannot run WPF tests.

`GlassCompositorTests` uses actual MainWindow/WindowBackdropController and the real
layered WheelWindow/WheelBackdropController over an opaque test-owned background.
The wheel proof controller is explicitly test-enabled; the production controller
remains gated solid. Each surface's material state follows that proof controller's
actual result. Ordinary chrome keeps production content panels and native frame.
The resource-only DesktopFixture must suppress resident startup so it cannot add
a competing controller. The new ordinary fixture asserts there is no preexisting
resident animator before attaching its controller.

The existing capture implementation and desktop/input/visual-ownership checks are
shared unchanged. In ordinary mode the snapshot's legacy `Wheel` field holds the
actual ordinary target HWND, identified explicitly in fixture/state JSON. Before
**each** screen read the entire bounded crop must be covered by owned opaque
background, the target must be above it, native visual z order must have no foreign
occluder, every physical crop input pixel must resolve to an allowed root, and the
actual station/input desktop/active console session must match. Failed setup or
ownership writes capability-unavailable metadata and throws before GetDC/BitBlt;
it performs no screen read and emits no PNG for that refused frame. No foreign
window is clicked/dismissed, no desktop is switched and no OS preference is set.

Optical fixtures and the exhaustive clip-input test permit one normal `Activate()`
on their owned opaque background, then record the foreground baseline **before**
showing the nonactivating wheel. Refused activation is diagnostic, never permission
to capture. Tests specifically checking original foreground preservation keep the
nonactivating fixture default. xUnit 2 cannot mark these runtime capability failures
as a reliable dynamic skip: they remain nonzero failures with explicit reason.
Do not convert that result to a green optical pass.

Artifacts are deliberately distinct:

| Artifact | Meaning |
|---|---|
| `glass-native/fixture-activation.json`, `clip-fixture-activation.json` | Original foreground, one activation attempt, resulting owned-scene baseline |
| `glass-native/01…04*.png` and matching preflights | Existing guarded wheel tint-only reference, configured helper, live background, fallback |
| `glass-native/{ordinary,wheel}/patterns/` | Fine black/white stripes, broad pattern and inverse, explicit broad/fine solid fallback, per-frame state and raw measurements |
| `glass-native/{ordinary,wheel}/fallback/` | Explicit local ReduceTransparency state over two broad patterns; real compositor frames only if guards allow |
| `glass-native/wheel/sequence/` | Visible light confirm, dark reopen, ReduceTransparency and reduced-motion frames; hidden interval records immediate state only, with no screen read |
| `glass-native/lifetime/` | Native handle cleanup, actual work-area bounds, service teardown and actual resident QuitAsync/OnExit diagnostics; no optical claim |
| `**/TestResults/{visual,workspace}/` | WPF-only RenderTargetBitmap images, with raster scale identified separately from actual HWND/monitor DPI |

The fine pattern has 16-DIP period/8-DIP stripes. Broad pattern uses 256-DIP
period/128-DIP stripes and inversion. Raw per-region mean RGB horizontal neighbor
difference records fine contrast; paired broad frames record mean RGB temporal
difference. Sample counts and the exact physical rectangle or actual-DPI wheel
annulus are recorded. The ordinary sample is the empty top chrome strip, not text
or a content card. Solid is an opaque fallback comparison, **not** an unblurred
transmission reference. The separate wheel proof retains its actual native-tint-only
reference. No universal blur threshold, assumed blur radius or numerical optical
pass is applied. Inspect spatial fine-pattern attenuation, broad-pattern influence,
clip/corner seams and content readability on the actual images. If the OS suppresses
material, report suppression/unproven rather than relabeling configuration success.

Resource tests exercise actual confirmed routed admission, synchronous cancel,
reopen, stale generations, simulated high-contrast/ReduceTransparency decisions,
dark/reduced-motion controller configuration, close and service disposal. The
opt-in `--glass-lifetime` resident harness retains actual wheel/animator references,
primes a real routed admission, calls actual `App.QuitAsync`, then samples after
`App.OnExit` and before process exit. It records zero pending material callbacks,
active/retained spring channels, rendering subscription and live wheel/helper state.
Native helper/hidden-owner destruction is separately exercised even though the
production wheel gate is false. Simulated policy tests do not establish actual OS
high-contrast behavior or physical Explorer/OLE initiation.

Pending acceptance includes an available owned interactive Windows 11 scene,
reviewed ordinary optical frames, any disabled/clipped helper optical success,
actual transition sequences (including hidden intervals), light/dark readability,
physical mixed-monitor DPI/negative coordinates, real accessibility settings and
external OLE input. A hidden target deliberately fails the capture guard; lifetime
assertions are not video proof of no transient bare circle. Raster exports at
150/200% are not physical monitor transitions. Release accounting and SDK projection
redistribution licensing remain separate unresolved gates; do not distribute or
claim completion from source compilation or this evidence harness.
